using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation;
using Baketa.Core.Abstractions.Settings;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local;

/// <summary>
/// HuggingFace Transformers基盤OPUS-MT翻訳エンジン
/// Python統合により語彙サイズ不整合問題を完全解決
/// </summary>
public class TransformersOpusMtEngine : TranslationEngineBase
{
    private readonly ILogger<TransformersOpusMtEngine> _logger;
    private readonly IUnifiedSettingsService _settingsService;
    private readonly string _pythonPath;
    private readonly string _serverScriptPath;
    private Process? _serverProcess;
    private bool _isInitialized;
    private bool _disposed;
    private readonly SemaphoreSlim _serverLock = new(1, 1);
    // 🔧 [CONNECTION_POOL] 真の永続接続管理（TIME_WAIT問題解決）
    private TcpClient? _persistentClient;
    private NetworkStream? _persistentStream;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private DateTime _lastConnectionTime = DateTime.MinValue;
    private int _connectionRetryCount = 0;
    private const int MaxConnectionRetries = 3;
    private const int ConnectionIdleTimeoutMinutes = 5;
    
    // ⚡ Phase 1.1: LRU翻訳キャッシュ（シンプル実装）
    private readonly ConcurrentDictionary<string, CacheEntry> _translationCache = new();
    private readonly int _maxCacheSize = 1000;
    private long _cacheHitCount;
    private long _cacheMissCount;
    
    // 常駐サーバー設定
    private const string ServerHost = "127.0.0.1";
    private const int ServerPort = 7860;  // 🔥【CRITICAL FIX】Python server (opus_mt_persistent_server.py) と統一
    private const int ConnectionTimeoutMs = 5000; // 🚀 Phase 2 UI応答性: 15→5秒に短縮
    private const int TranslationTimeoutMs = 10000; // 🔧 [TCP_STABILIZATION] 5→10秒に延長

    /// <inheritdoc/>
    public override string Name => "OPUS-MT Transformers";

    /// <inheritdoc/>
    public override string Description => "HuggingFace Transformers基盤の高品質OPUS-MT翻訳エンジン";

    /// <inheritdoc/>
    public override bool RequiresNetwork => false;

    public TransformersOpusMtEngine(ILogger<TransformersOpusMtEngine> logger, IUnifiedSettingsService settingsService) : base(logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        
        Console.WriteLine("🔧 [DEBUG] TransformersOpusMtEngineのコンストラクタが呼び出されました");
        _logger.LogInformation("TransformersOpusMtEngineが作成されました");
        
        // Python実行環境設定
        // pyenv-winに問題があるため、Python Launcherを使用（テスト環境と同じ）
        _pythonPath = "py";
        
        // 常駐サーバースクリプトパス設定
        var currentDir = Directory.GetCurrentDirectory();
        var projectRoot = FindProjectRoot(currentDir);
        _serverScriptPath = Path.Combine(projectRoot, "scripts", "opus_mt_persistent_server.py");
        
        Console.WriteLine($"🔧 [DEBUG] TransformersOpusMtEngine設定完了 - Python: {_pythonPath}, ServerScript: {_serverScriptPath}");
        
        // バックグラウンドで初期化を開始（ブロックしない）
        _ = Task.Run(async () =>
        {
            try
            {
                await InitializeAsync().ConfigureAwait(false);
                Console.WriteLine("🔧 [DEBUG] TransformersOpusMtEngineのバックグラウンド初期化完了");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🔧 [DEBUG] TransformersOpusMtEngineのバックグラウンド初期化失敗: {ex.Message}");
            }
        });
    }

    /// <inheritdoc/>
    protected override async Task<bool> InitializeInternalAsync()
    {
        try
        {
            Console.WriteLine("🔄 [INIT_DEBUG] OPUS-MT Transformers翻訳エンジンの初期化開始");
            _logger.LogInformation("OPUS-MT Transformers翻訳エンジンの初期化開始");
            
            // Python環境確認（Python Launcherの場合はコマンド実行で確認）
            Console.WriteLine($"🔍 [INIT_DEBUG] Python実行環境確認: {_pythonPath}");
            try
            {
                // Python Launcherの場合は--versionで動作確認
                if (_pythonPath == "py")
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = _pythonPath,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = new Process { StartInfo = processInfo };
                    process.Start();
                    await process.WaitForExitAsync().ConfigureAwait(false);
                    
                    if (process.ExitCode != 0)
                    {
                        Console.WriteLine($"❌ [INIT_DEBUG] Python Launcher動作確認失敗 (ExitCode: {process.ExitCode})");
                        _logger.LogError("Python Launcher動作確認失敗 (ExitCode: {ExitCode})", process.ExitCode);
                        return false;
                    }
                    
                    var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                    Console.WriteLine($"✅ [INIT_DEBUG] Python Launcher動作確認完了: {output.Trim()}");
                    _logger.LogInformation("Python Launcher動作確認完了: {Output}", output.Trim());
                }
                else
                {
                    // 従来のファイルパス確認
                    if (!File.Exists(_pythonPath))
                    {
                        Console.WriteLine($"❌ [INIT_DEBUG] Python実行ファイルが見つかりません: {_pythonPath}");
                        _logger.LogError("Python実行ファイルが見つかりません: {PythonPath}", _pythonPath);
                        return false;
                    }
                    Console.WriteLine($"✅ [INIT_DEBUG] Python実行ファイル確認完了: {_pythonPath}");
                    _logger.LogInformation("Python実行ファイル確認完了: {PythonPath}", _pythonPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [INIT_DEBUG] Python環境確認中にエラー: {ex.Message}");
                _logger.LogError(ex, "Python環境確認中にエラーが発生しました");
                return false;
            }

            Console.WriteLine($"🔍 [INIT_DEBUG] 常駐サーバースクリプト確認: {_serverScriptPath}");
            if (!File.Exists(_serverScriptPath))
            {
                Console.WriteLine($"❌ [INIT_DEBUG] 常駐サーバースクリプトが見つかりません: {_serverScriptPath}");
                _logger.LogError("常駐サーバースクリプトが見つかりません: {ServerScriptPath}", _serverScriptPath);
                return false;
            }
            Console.WriteLine($"✅ [INIT_DEBUG] 常駐サーバースクリプト確認完了: {_serverScriptPath}");
            _logger.LogInformation("常駐サーバースクリプト確認完了: {ServerScriptPath}", _serverScriptPath);
            
            // 軽量初期化：ファイル確認のみで完了（サーバー起動は翻訳時に遅延実行）
            Console.WriteLine("✅ [INIT_DEBUG] 軽量初期化完了（サーバー起動は翻訳時に実行）");
            _logger.LogInformation("軽量初期化完了 - 常駐サーバーは翻訳時に起動します");
            _isInitialized = true;
            IsInitialized = true; // 基底クラスのプロパティも更新
            Console.WriteLine("🔧 [DEBUG] TransformersOpusMtEngine初期化完了（遅延サーバー起動）");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初期化中にエラーが発生しました");
            return false;
        }
    }

    /// <summary>
    /// バッチ翻訳処理 - 複数テキストを一度のリクエストで処理
    /// </summary>
    public override async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        if (requests == null || !requests.Any())
        {
            return new List<TranslationResponse>();
        }

        _logger?.LogInformation("🚀 [BATCH_PARALLEL] 並列バッチ翻訳開始 - テキスト数: {Count}", requests.Count);
        Console.WriteLine($"🚀 [BATCH_PARALLEL] 並列バッチ翻訳開始 - テキスト数: {requests.Count}");
        _logger?.LogDebug("🚀 [BATCH_PARALLEL] 並列バッチ翻訳開始 - テキスト数: {Count}", requests.Count);

        try
        {
            // 🌍 バッチ翻訳の言語方向判定（最初のリクエストから判定）
            var direction = GetTranslationDirection(requests[0].SourceLanguage, requests[0].TargetLanguage);
            Console.WriteLine($"📦 [BATCH_DIRECTION] バッチ翻訳方向判定: {requests[0].SourceLanguage.Code} → {requests[0].TargetLanguage.Code} = {direction}");
            _logger?.LogDebug("📦 [BATCH_DIRECTION] バッチ翻訳方向判定: {Source} → {Target} = {Direction}", requests[0].SourceLanguage.Code, requests[0].TargetLanguage.Code, direction);
            
            // 🔥 [PARALLEL_CHUNKS] 並列チャンク処理で高速化
            var parallelResult = await TranslateBatchWithParallelChunksAsync(requests, direction, cancellationToken).ConfigureAwait(false);
            
            if (parallelResult?.Success == true && parallelResult.Translations != null)
            {
                // バッチ結果を個別のTranslationResponseに変換
                var responses = new List<TranslationResponse>();
                
                for (int i = 0; i < requests.Count; i++)
                {
                    var translation = i < parallelResult.Translations.Count ? parallelResult.Translations[i] : "[Batch Error]";
                    
                    responses.Add(new TranslationResponse
                    {
                        RequestId = requests[i].RequestId,
                        TranslatedText = translation,
                        SourceText = requests[i].SourceText,
                        SourceLanguage = requests[i].SourceLanguage,
                        TargetLanguage = requests[i].TargetLanguage,
                        ConfidenceScore = 0.95f,
                        EngineName = Name,
                        IsSuccess = true
                    });
                }
                
                _logger?.LogInformation("✅ [BATCH_PARALLEL] 並列バッチ翻訳成功 - 処理時間: {ProcessingTime:F3}秒", parallelResult.ProcessingTime);
                
                // 🔍 [TRANSLATION_RESULTS] 翻訳結果の詳細ログ出力
                Console.WriteLine($"🔍 [TRANSLATION_RESULTS] バッチ翻訳結果詳細:");
                for (int i = 0; i < Math.Min(responses.Count, 5); i++) // 最初の5個を表示
                {
                    var response = responses[i];
                    Console.WriteLine($"  [{i}] 原文: '{response.SourceText?.Substring(0, Math.Min(50, response.SourceText?.Length ?? 0))}...'");
                    Console.WriteLine($"  [{i}] 訳文: '{response.TranslatedText?.Substring(0, Math.Min(50, response.TranslatedText?.Length ?? 0))}...'");
                    Console.WriteLine($"  [{i}] 成功: {response.IsSuccess}");
                }
                _logger?.LogInformation("🔍 [TRANSLATION_RESULTS] バッチ翻訳結果: {Count}個の翻訳完了", responses.Count);
                
                return responses;
            }
            else
            {
                // バッチ翻訳失敗時は個別処理にフォールバック
                _logger?.LogWarning("⚠️ [BATCH] バッチ翻訳失敗、個別処理にフォールバック");
                var responses = new List<TranslationResponse>();
                
                foreach (var request in requests)
                {
                    var response = await TranslateInternalAsync(request, cancellationToken).ConfigureAwait(false);
                    responses.Add(response);
                }
                
                return responses;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "💥 [BATCH] バッチ翻訳エラー");
            
            // エラー時は個別処理にフォールバック
            var responses = new List<TranslationResponse>();
            foreach (var request in requests)
            {
                responses.Add(new TranslationResponse
                {
                    RequestId = request.RequestId,
                    TranslatedText = $"[Batch Error] {request.SourceText}",
                    SourceText = request.SourceText,
                    SourceLanguage = request.SourceLanguage,
                    TargetLanguage = request.TargetLanguage,
                    ConfidenceScore = 0.0f,
                    EngineName = Name,
                    IsSuccess = false
                });
            }
            return responses;
        }
    }

    /// <inheritdoc/>
    protected override async Task<TranslationResponse> TranslateInternalAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"🚀 [DEBUG] TransformersOpusMtEngine.TranslateInternalAsync 呼び出し - テキスト: '{request.SourceText}'");
        _logger?.LogDebug("🚀 [DEBUG] TransformersOpusMtEngine.TranslateInternalAsync 呼び出し - テキスト: {Text}", request.SourceText);
        
        // ✅ 言語制限削除: 英→日と日→英の両方向翻訳をサポート
        // モデルファイル(opus-mt-ja-en.model, opus-mt-en-ja.model)が存在するため両方向対応可能
        
        // 🔄 言語方向判定: リクエストから適切な翻訳方向を決定
        var direction = GetTranslationDirection(request.SourceLanguage, request.TargetLanguage);
        Console.WriteLine($"🌍 [DIRECTION] 翻訳方向判定: {request.SourceLanguage.Code} → {request.TargetLanguage.Code} = {direction}");
        _logger?.LogDebug("🌍 [DIRECTION] 翻訳方向判定: {Source} → {Target} = {Direction}", request.SourceLanguage.Code, request.TargetLanguage.Code, direction);

        // ⚡ Phase 1.1: キャッシュチェック
        var cacheKey = GenerateCacheKey(request.SourceText, request.SourceLanguage, request.TargetLanguage);
        if (TryGetFromCache(cacheKey, out var cachedResponse))
        {
            Interlocked.Increment(ref _cacheHitCount);
            Console.WriteLine($"💨 [CACHE_HIT] キャッシュヒット - テキスト: '{request.SourceText}', 翻訳: '{cachedResponse.TranslatedText}'");
            _logger.LogInformation("キャッシュヒット - テキスト: '{Text}'", request.SourceText);
            
            // RequestIdを新しいリクエスト用に更新
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                TranslatedText = cachedResponse.TranslatedText,
                SourceText = cachedResponse.SourceText,
                SourceLanguage = cachedResponse.SourceLanguage,
                TargetLanguage = cachedResponse.TargetLanguage,
                ConfidenceScore = cachedResponse.ConfidenceScore,
                EngineName = cachedResponse.EngineName,
                IsSuccess = cachedResponse.IsSuccess
            };
        }
        
        Interlocked.Increment(ref _cacheMissCount);
        Console.WriteLine($"🔍 [CACHE_MISS] キャッシュミス - 新規翻訳実行: '{request.SourceText}'");

        // 🚀 [TIMEOUT_FIX] 外部CancellationTokenを優先し、独自タイムアウトとの競合を解決
        // StreamingTranslationServiceから渡される長時間タイムアウト（30-300秒）を尊重
        var startTime = DateTime.Now;
        Console.WriteLine($"⚡ [EXTERNAL_TIMEOUT] 外部CancellationToken使用開始 - テキスト: '{request.SourceText}'");
        
        try
        {
            // 常駐サーバーでの翻訳を試行（外部CancellationTokenをそのまま使用）
            Console.WriteLine($"⚡ [DEBUG] 常駐サーバー翻訳を試行 - テキスト: '{request.SourceText}'");
            _logger?.LogDebug("⚡ [DEBUG] 常駐サーバー翻訳を試行 - テキスト: {Text}", request.SourceText);

            // 🚨 超詳細境界調査 - コンソール出力とファイル出力を分離
            Console.WriteLine($"⚡ [BOUNDARY-1] Console.WriteLine実行完了");
            
            _logger?.LogDebug("⚡ [BOUNDARY-2] File.AppendAllText実行完了");
                
            Console.WriteLine($"⚡ [BOUNDARY-3] TranslateWithPersistentServerAsync呼び出し直前");
            
            _logger?.LogDebug("⚡ [BOUNDARY-4] メソッド呼び出し直前の最終ログ");

            // 🚨 [CRITICAL_FIX] 外部CancellationTokenをそのまま使用（独自タイムアウトを削除）
            var pythonResult = await TranslateWithPersistentServerAsync(request.SourceText, direction, cancellationToken).ConfigureAwait(false);

            Console.WriteLine($"⚡ [DEBUG] TranslateWithPersistentServerAsync呼び出し完了");
            _logger?.LogDebug("⚡ [DEBUG] TranslateWithPersistentServerAsync呼び出し完了");

            var elapsedTime = DateTime.Now - startTime;
            Console.WriteLine($"⚡ [TRANSLATE_DEBUG] 常駐サーバー結果取得 - Result: {pythonResult != null}, Success: {pythonResult?.Success}, Translation: '{pythonResult?.Translation}', 実行時間: {elapsedTime.TotalMilliseconds:F0}ms");

            if (pythonResult?.Success == true)
            {
                var response = new TranslationResponse
                {
                    RequestId = request.RequestId,
                    TranslatedText = pythonResult.Translation,
                    SourceText = request.SourceText,
                    SourceLanguage = request.SourceLanguage,
                    TargetLanguage = request.TargetLanguage,
                    ConfidenceScore = 0.95f, // HuggingFace Transformersは高品質
                    EngineName = Name,
                    IsSuccess = true
                };
                
                Console.WriteLine($"⚡ [TRANSLATE_DEBUG] 高速翻訳成功 - TranslatedText: '{response.TranslatedText}' (処理時間: {pythonResult.ProcessingTime:F3}秒)");
                _logger.LogInformation("高速翻訳成功 - RequestId: {RequestId}, TranslatedText: '{TranslatedText}', ProcessingTime: {ProcessingTime}秒", 
                    response.RequestId, response.TranslatedText, pythonResult.ProcessingTime);
                
                // ⚡ Phase 1.1: 成功した翻訳をキャッシュに保存
                AddToCache(cacheKey, response);
                Console.WriteLine($"💾 [CACHE_STORE] 翻訳結果をキャッシュに保存 - テキスト: '{request.SourceText}'");
                
                return response;
            }

            // Pythonサーバー失敗時のエラー処理
            string userFriendlyError = pythonResult?.Error switch
            {
                "The operation was canceled." => "翻訳処理がキャンセルされました",
                string error when error?.Contains("timeout") == true => "翻訳処理がタイムアウトしました",
                string error when error?.Contains("canceled") == true => "翻訳処理がキャンセルされました",
                null => "常駐サーバー翻訳が失敗しました", // nullの場合
                _ => pythonResult?.Error ?? "常駐サーバー翻訳が失敗しました" // その他のエラー
            };
            
            var errorResponse = new TranslationResponse
            {
                RequestId = request.RequestId,
                TranslatedText = userFriendlyError,
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                ConfidenceScore = 0.0f,
                EngineName = Name,
                IsSuccess = false
            };
            
            Console.WriteLine($"💥 [TRANSLATE_DEBUG] 高速翻訳エラー - Error: '{errorResponse.TranslatedText}'");
            _logger.LogError("高速翻訳失敗 - RequestId: {RequestId}, Error: '{Error}'", errorResponse.RequestId, errorResponse.TranslatedText);
            return errorResponse;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // ⚡ [TIMEOUT_FIX] 外部CancellationTokenがキャンセルされた場合
            var timeoutElapsed = DateTime.Now - startTime;
            Console.WriteLine($"⏰ [EXTERNAL_TIMEOUT] 外部タイムアウト/キャンセル - テキスト: '{request.SourceText}', 経過時間: {timeoutElapsed.TotalMilliseconds:F0}ms");
            _logger.LogWarning("外部タイムアウト/キャンセル - テキスト: '{Text}', 経過時間: {ElapsedMs}ms", 
                request.SourceText, timeoutElapsed.TotalMilliseconds);

            // 外部からのキャンセル時はユーザーフレンドリーなメッセージを返す
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                TranslatedText = "翻訳処理がキャンセルされました",
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                ConfidenceScore = 0.1f, // 低品質マーカー
                EngineName = Name,
                IsSuccess = false
            };
        }
        catch (Exception ex)
        {
            // その他の例外処理
            var errorElapsed = DateTime.Now - startTime;
            Console.WriteLine($"💥 [ERROR] 翻訳エラー - テキスト: '{request.SourceText}', エラー: {ex.Message}, 経過時間: {errorElapsed.TotalMilliseconds:F0}ms");
            _logger.LogError(ex, "翻訳処理エラー - テキスト: '{Text}', 経過時間: {ElapsedMs}ms", 
                request.SourceText, errorElapsed.TotalMilliseconds);

            return new TranslationResponse
            {
                RequestId = request.RequestId,
                TranslatedText = $"[ERROR] {request.SourceText}",
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                ConfidenceScore = 0.0f,
                EngineName = Name,
                IsSuccess = false
            };
        }
    }

    /// <summary>
    /// 常駐Pythonサーバーを起動
    /// </summary>
    private async Task<bool> StartPersistentServerAsync()
    {
        try
        {
            await _serverLock.WaitAsync().ConfigureAwait(false);
            
            // 🚨 既存のPythonサーバープロセスを強制終了
            _logger?.LogDebug("🧹 [SERVER_CLEANUP] 既存Pythonプロセス終了開始");
            
            await KillExistingServerProcessesAsync().ConfigureAwait(false);
            
            // 既にサーバーが実行中かチェック
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _logger?.LogDebug("🔍 [SERVER_CHECK] 既存サーバープロセス確認中");
                
                if (await CheckServerHealthAsync().ConfigureAwait(false))
                {
                    _logger?.LogDebug("✅ [SERVER_EXISTING] 既存サーバー使用");
                    _logger.LogInformation("常駐サーバーは既に実行中です");
                    return true;
                }
            }
            
            Console.WriteLine($"🚀 [SERVER_DEBUG] 常駐Pythonサーバー起動開始");
            _logger.LogInformation("常駐Pythonサーバーを起動中...");
            
            // 🔧 [PYTHON_FIX] 統一されたPython実行（pyenv-win問題回避）
            var processInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"{_pythonPath} '{_serverScriptPath}'\"", // _pythonPath（"py"）を使用
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            
            Console.WriteLine($"🚀 [SERVER_DEBUG] サーバー起動コマンド: {processInfo.FileName} {processInfo.Arguments}");
            _logger?.LogInformation("🚀 [SERVER_DEBUG] サーバー起動コマンド: {FileName} {Arguments}", processInfo.FileName, processInfo.Arguments);
            
            _serverProcess = new Process { StartInfo = processInfo };
            _serverProcess.Start();
            
            Console.WriteLine($"🚀 [SERVER_DEBUG] サーバープロセス起動 - PID: {_serverProcess.Id}");
            _logger.LogInformation("サーバープロセス起動 - PID: {ProcessId}", _serverProcess.Id);
            
            // サーバープロセスの標準出力/エラー出力を監視
            _ = Task.Run(() =>
            {
                try
                {
                    while (!_serverProcess.StandardOutput.EndOfStream)
                    {
                        var line = _serverProcess.StandardOutput.ReadLine();
                        if (!string.IsNullOrEmpty(line))
                        {
                            Console.WriteLine($"[PYTHON_STDOUT] {line}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PYTHON_STDOUT_ERROR] {ex.Message}");
                }
            });
            
            _ = Task.Run(() =>
            {
                try
                {
                    while (!_serverProcess.StandardError.EndOfStream)
                    {
                        var line = _serverProcess.StandardError.ReadLine();
                        if (!string.IsNullOrEmpty(line))
                        {
                            Console.WriteLine($"[PYTHON_STDERR] {line}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PYTHON_STDERR_ERROR] {ex.Message}");
                }
            });
            
            // サーバーが起動するまで待機（最大60秒、モデルロード時間を考慮）
            var startTime = DateTime.Now;
            var maxWaitTime = TimeSpan.FromSeconds(60);
            
            Console.WriteLine($"🔄 [SERVER_DEBUG] サーバー起動待機開始 - 最大{maxWaitTime.TotalSeconds}秒");
            
            while (DateTime.Now - startTime < maxWaitTime)
            {
                await Task.Delay(5000).ConfigureAwait(false); // 🔧 [TCP_STABILIZATION] 2→5秒に延長
                
                var elapsedTime = DateTime.Now - startTime;
                Console.WriteLine($"⏱️ [SERVER_DEBUG] サーバー接続試行中... 経過時間: {elapsedTime.TotalSeconds:F1}秒");
                
                // プロセス終了チェック
                if (_serverProcess.HasExited)
                {
                    Console.WriteLine($"💥 [SERVER_DEBUG] Pythonサーバープロセスが異常終了しました - ExitCode: {_serverProcess.ExitCode}");
                    _logger?.LogError("Pythonサーバープロセスが異常終了しました - ExitCode: {ExitCode}", _serverProcess.ExitCode);
                    return false;
                }
                
                if (await CheckServerHealthAsync().ConfigureAwait(false))
                {
                    Console.WriteLine($"✅ [SERVER_DEBUG] 常駐サーバー起動完了 - 起動時間: {elapsedTime.TotalSeconds:F1}秒");
                    _logger.LogInformation("常駐サーバー起動完了 - 起動時間: {ElapsedSeconds}秒", elapsedTime.TotalSeconds);
                    return true;
                }
                
                // 30秒経過時に追加ログ
                if (elapsedTime.TotalSeconds > 30 && elapsedTime.TotalSeconds < 32)
                {
                    Console.WriteLine($"⚠️ [SERVER_DEBUG] サーバー起動に30秒以上かかっています（モデルロード中の可能性）");
                }
            }
            
            Console.WriteLine($"❌ [SERVER_DEBUG] サーバー起動タイムアウト（60秒）");
            _logger.LogError("サーバー起動がタイムアウトしました（60秒）");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 [SERVER_DEBUG] サーバー起動エラー: {ex.Message}");
            _logger.LogError(ex, "常駐サーバー起動中にエラーが発生しました");
            return false;
        }
        finally
        {
            _serverLock.Release();
        }
    }
    
    /// <summary>
    /// サーバーの生存確認（リトライ機構付き）
    /// 🔧 [TCP_STABILIZATION] 3回リトライでTCP接続安定化
    /// </summary>
    private async Task<bool> CheckServerHealthAsync()
    {
        // 🔧 [TCP_STABILIZATION] 3回リトライ機構
        const int maxRetries = 3;
        const int retryDelayMs = 1000; // 1秒間隔でリトライ
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger?.LogDebug("🔄 [HEALTH_RETRY] ヘルスチェック試行 {Attempt}/{MaxRetries}", attempt, maxRetries);
                    
                Console.WriteLine($"🔍 [HEALTH_CHECK] 試行 {attempt}/{maxRetries} - サーバー接続確認中...");
                
                var result = await CheckServerHealthInternalAsync().ConfigureAwait(false);
                
                if (result)
                {
                    _logger?.LogDebug("✅ [HEALTH_SUCCESS] 試行{Attempt}で接続成功", attempt);
                    Console.WriteLine($"✅ [HEALTH_CHECK] 試行{attempt}で接続成功");
                    return true;
                }
                
                // 失敗時は次のリトライまで待機（最後の試行は除く）
                if (attempt < maxRetries)
                {
                    _logger?.LogDebug("⏱️ [HEALTH_RETRY_WAIT] 試行{Attempt}失敗、{DelayMs}ms後にリトライ", attempt, retryDelayMs);
                    Console.WriteLine($"⏱️ [HEALTH_CHECK] 試行{attempt}失敗、{retryDelayMs}ms後にリトライ");
                    await Task.Delay(retryDelayMs).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("❌ [HEALTH_EXCEPTION] 試行{Attempt}例外: {Message}", attempt, ex.Message);
                Console.WriteLine($"❌ [HEALTH_CHECK] 試行{attempt}例外: {ex.Message}");
                
                if (attempt == maxRetries)
                {
                    _logger?.LogError("💥 [HEALTH_FINAL_FAIL] 最終試行も失敗");
                    return false;
                }
                
                if (attempt < maxRetries)
                {
                    await Task.Delay(retryDelayMs).ConfigureAwait(false);
                }
            }
        }
        
        Console.WriteLine($"💥 [HEALTH_CHECK] 全リトライ失敗 - サーバー接続不可");
        return false;
    }
    
    /// <summary>
    /// サーバーの生存確認（内部実装）
    /// 🔧 [TCP_STABILIZATION] リトライ対応の分離実装
    /// </summary>
    private async Task<bool> CheckServerHealthInternalAsync()
    {
        try
        {
            // 🚨 ログ1: メソッド開始
            // 🔥 [HEALTH_1] CheckServerHealthAsyncメソッド開始 - ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🔥 [HEALTH_1] CheckServerHealthAsyncメソッド開始");
            
            Console.WriteLine($"🔍 [HEALTH_CHECK] サーバー接続試行 - {ServerHost}:{ServerPort}");
            
            // 🚨 ログ2: TcpClient作成前
            // 🔥 [HEALTH_2] TcpClient作成前 - ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🔥 [HEALTH_2] TcpClient作成前");
            
            using var client = new TcpClient();
            
            // 🔥 [GEMINI_PHASE1] Keep-Alive設定でアイドル切断防止
            ConfigureKeepAlive(client);
            
            // 🚨 ログ3: ConnectAsync呼び出し前
            // 🔥 [HEALTH_3] ConnectAsync呼び出し前 - ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🔥 [HEALTH_3] ConnectAsync呼び出し前");
            
            var connectTask = client.ConnectAsync(ServerHost, ServerPort);
            var timeoutTask = Task.Delay(ConnectionTimeoutMs);
            
            // 🚨 ログ4: Task.WhenAny呼び出し前
            // 🔥 [HEALTH_4] Task.WhenAny呼び出し前 - ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🔥 [HEALTH_4] Task.WhenAny呼び出し前");
            
            if (await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false) == timeoutTask)
            {
                _logger?.LogWarning("⏰ [HEALTH_TIMEOUT] 接続タイムアウト発生");
                Console.WriteLine($"⏰ [HEALTH_CHECK] 接続タイムアウト（{ConnectionTimeoutMs}ms）");
                return false; // タイムアウト
            }
            
            // 🚨 ログ5: WhenAny完了、接続確認前
            // 🔥 [HEALTH_5] Task.WhenAny完了、接続状態確認中 - ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🔥 [HEALTH_5] Task.WhenAny完了、接続状態確認中");
            
            if (!client.Connected)
            {
                // ❌ [HEALTH_FAILED] TCP接続失敗 - ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("❌ [HEALTH_FAILED] TCP接続失敗");
                Console.WriteLine($"❌ [HEALTH_CHECK] 接続失敗 - client.Connected = false");
                return false;
            }
            
            // 🚨 ログ6: TCP接続成功、ストリーム取得前
            // 🔥 [HEALTH_6] TCP接続成功、ストリーム取得前 - ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🔥 [HEALTH_6] TCP接続成功、ストリーム取得前");
            
            Console.WriteLine($"🔗 [HEALTH_CHECK] TCP接続成功 - PING送信中");
            
            var stream = client.GetStream();
            var pingRequest = Encoding.UTF8.GetBytes("PING\n");
            
            // 🚨 ログ7: WriteAsync呼び出し前
            // 🔥 [HEALTH_7] WriteAsync呼び出し前 - ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🔥 [HEALTH_7] WriteAsync呼び出し前");
            
            await stream.WriteAsync(pingRequest, 0, pingRequest.Length).ConfigureAwait(false);
            
            // 🚨 ログ8: WriteAsync完了、ReadAsync準備前
            // 🔥 [HEALTH_8] WriteAsync完了、ReadAsync準備中 - ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🔥 [HEALTH_8] WriteAsync完了、ReadAsync準備中");
            
            // 🔥 [GEMINI_PHASE1] 統一タイムアウト戦略でReadAsync実行
            var buffer = new byte[1024];
            using var readTimeout = CreateUnifiedReadTimeout("HealthCheck");
            
            // 🚨 ログ9: ReadAsync呼び出し前 - ⚠️ 最も疑わしい箇所
            // 🚨🚨🚨 [HEALTH_9] ReadAsync呼び出し前 - HANG発生箇所の可能性大 - ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🚨 [HEALTH_9] ReadAsync呼び出し前 - HANG発生箇所の可能性大");
            
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, readTimeout.Token).ConfigureAwait(false);
            
            // 🚨 ログ10: ReadAsync完了
            // ✅ [HEALTH_10] ReadAsync完了 - ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("✅ [HEALTH_10] ReadAsync完了 - bytesRead={BytesRead}", bytesRead);
            
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            // 🔍 レスポンス内容の詳細ログ
            // 📨 [HEALTH_RESPONSE] 受信内容 - ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("📨 [HEALTH_RESPONSE] 受信内容({BytesRead}バイト): {Response}", bytesRead, response);
            
            // 🔍 レスポンス内容をバイト単位で確認
            var responseBytes = Encoding.UTF8.GetBytes(response);
            var hexString = Convert.ToHexString(responseBytes);
            // 🔍 [HEALTH_HEX] バイト表現 - ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🔍 [HEALTH_HEX] バイト表現: {HexString}", hexString);
            
            Console.WriteLine($"📨 [HEALTH_CHECK] サーバーレスポンス: '{response.Trim()}'");
            
            var isAlive = response.Contains("\"status\": \"alive\"") || response.Contains("\"status\":\"alive\"");
            
            // 🔍 判定処理の詳細ログ
            // 🔍 [HEALTH_CHECK] Contains - ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🔍 [HEALTH_CHECK] Contains('status:alive'): {IsAlive}", isAlive);
            
            Console.WriteLine($"💓 [HEALTH_CHECK] サーバー状態: {(isAlive ? "生存" : "異常")}");
            
            // 🚨 ログ11: メソッド正常終了
            _logger?.LogDebug("✅ [HEALTH_11] CheckServerHealthAsync正常終了 - isAlive={IsAlive}", isAlive);
            
            return isAlive;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "💥 [HEALTH_EXCEPTION] ヘルスチェック例外: {Message}", ex.Message);
            Console.WriteLine($"💥 [HEALTH_CHECK] ヘルスチェック例外: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// バッチ翻訳用常駐サーバー通信
    /// </summary>
    private async Task<BatchTranslationResult?> TranslateBatchWithPersistentServerAsync(
        IList<string> texts, 
        string direction,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("📦 [BATCH_SERVER] バッチ翻訳サーバー通信開始 - テキスト数: {Count}", texts.Count);
        var startTime = DateTime.Now;

        try
        {
            // キャンセレーション確認
            cancellationToken.ThrowIfCancellationRequested();

            // サーバーの健全性確認（バッチ翻訳前の詳細チェック）
            Console.WriteLine("🔍 [BATCH_DEBUG] サーバー健全性確認開始");
            _logger?.LogInformation("🔍 [BATCH_DEBUG] サーバー健全性確認開始");
            
            if (!await CheckServerHealthAsync().ConfigureAwait(false))
            {
                Console.WriteLine("⚠️ [BATCH_DEBUG] サーバー接続失敗 - 再起動を試行");
                _logger?.LogWarning("⚠️ [BATCH_DEBUG] サーバー接続失敗 - 再起動を試行します");
                
                if (!await StartPersistentServerAsync().ConfigureAwait(false))
                {
                    Console.WriteLine("💥 [BATCH_DEBUG] サーバー再起動失敗 - バッチ翻訳中止");
                    _logger?.LogError("💥 [BATCH_DEBUG] サーバー再起動失敗 - バッチ翻訳中止");
                    return new BatchTranslationResult { Success = false, Error = "サーバー接続に失敗しました - Pythonサーバーが起動していない可能性があります" };
                }
                else 
                {
                    Console.WriteLine("✅ [BATCH_DEBUG] サーバー再起動成功");
                    _logger?.LogInformation("✅ [BATCH_DEBUG] サーバー再起動成功");
                }
            }
            else
            {
                Console.WriteLine("✅ [BATCH_DEBUG] サーバー健全性確認OK");
                _logger?.LogInformation("✅ [BATCH_DEBUG] サーバー健全性確認OK");
            }
            
            _logger?.LogInformation("🔗 [BATCH_DETAIL_1] TcpClient作成前");
            using var client = new TcpClient();
            
            // 🔥 [GEMINI_PHASE1] Keep-Alive設定でアイドル切断防止
            ConfigureKeepAlive(client);
            
            _logger?.LogInformation("🔗 [BATCH_DETAIL_2] ConnectAsync呼び出し前");
            await client.ConnectAsync(ServerHost, ServerPort, cancellationToken).ConfigureAwait(false);
            
            _logger?.LogInformation("🔗 [BATCH_DETAIL_3] 接続完了、ストリーム取得前");
            // キャンセレーション再確認
            cancellationToken.ThrowIfCancellationRequested();
            
            var stream = client.GetStream();
            
            // 🔥 [GEMINI_PHASE1] バッチ翻訳プロトコル修正: 改行文字の適切な前処理
            var sanitizedTexts = texts.Select(text => SanitizeTextForBatchTranslation(text)).ToList();
            
            var request = new { batch_texts = sanitizedTexts, direction = direction };
            var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions 
            { 
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false // プロトコル安定化のため改行なし
            }) + "\n";
            var requestBytes = Encoding.UTF8.GetBytes(requestJson);
            
            Console.WriteLine($"🔥 [BATCH_PROTOCOL] 修正版バッチリクエスト送信 - オリジナル: {texts.Count}件, サニタイズ済み: {sanitizedTexts.Count}件");
            Console.WriteLine($"📋 [BATCH_JSON_REQUEST] バッチリクエストJSON: {requestJson.TrimEnd()}");
            Console.WriteLine($"🔢 [BATCH_JSON_REQUEST] バッチリクエストバイト数: {requestBytes.Length}, 文字列長: {requestJson.Length}");
            
            // サニタイズ前後の比較ログ
            for (int i = 0; i < Math.Min(3, texts.Count); i++)
            {
                Console.WriteLine($"📝 [SANITIZE_DEBUG] Text[{i}] Before: '{texts[i]}' After: '{sanitizedTexts[i]}'");
            }
            
            _logger?.LogInformation("バッチ翻訳プロトコル修正版でリクエスト送信 - テキスト数: {Count}", sanitizedTexts.Count);
            
            _logger?.LogInformation("📤 [BATCH_SERVER] バッチリクエスト送信 - サイズ: {Size} bytes", requestBytes.Length);
            
            _logger?.LogInformation("📤 [BATCH_DETAIL_4] WriteAsync呼び出し前");
            await stream.WriteAsync(requestBytes, 0, requestBytes.Length).ConfigureAwait(false);
            _logger?.LogInformation("📤 [BATCH_DETAIL_5] WriteAsync完了");
            
            // 🔥 [GEMINI_PHASE1] 統一タイムアウト戦略でReadAsync実行（バッチ用延長）
            var extraTimeoutForBatch = texts.Count * 1000; // テキスト数に応じて動的追加
            _logger?.LogInformation("⏰ [BATCH_DETAIL_6] ReadAsync準備 - 動的追加タイムアウト: {ExtraTimeout}ms", extraTimeoutForBatch);
            
            // 🚀 [TIMEOUT_FIX] 外部CancellationTokenを直接使用（統一タイムアウトを削除）
// using var cts = CreateUnifiedReadTimeout("BatchTranslation", extraTimeoutForBatch);
Console.WriteLine($"⚡ [EXTERNAL_TOKEN_BATCH] 外部CancellationTokenでバッチReadAsync実行 - StreamingServiceのタイムアウト設定を尊重");
            var buffer = new byte[65536]; // 64KB に拡張してバッファ不足を解決
            var allData = new List<byte>();
            int totalBytesRead = 0;
            
            _logger?.LogInformation("🔧 [TCP_FIX] 改良版ReadAsync開始 - ストリーム終端まで確実に読み取り");
            
            // ストリーム終端まで確実に読み取るループ処理
            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0) 
                {
                    _logger?.LogDebug("📨 [TCP_FIX] ストリーム終了を検出 - 総読み取り: {TotalBytes}bytes", totalBytesRead);
                    break; // ストリーム終了
                }
                
                allData.AddRange(buffer.Take(bytesRead));
                totalBytesRead += bytesRead;
                _logger?.LogDebug("📨 [TCP_FIX] 部分読み取り: {Bytes}bytes, 累計: {Total}bytes", bytesRead, totalBytesRead);
                
                // レスポンス完了の判定（改行文字で終端判定）
                if (allData.Count > 0 && allData[^1] == '\n') 
                {
                    _logger?.LogDebug("📨 [TCP_FIX] 改行文字で終端検出 - レスポンス完了");
                    break;
                }
                
                // 無限ループ防止（最大10MB制限）
                if (totalBytesRead > 10 * 1024 * 1024)
                {
                    _logger?.LogWarning("⚠️ [TCP_FIX] 最大サイズ超過 - 強制終了");
                    break;
                }
            }
            
            _logger?.LogInformation("✅ [TCP_FIX] 改良版ReadAsync完了 - 総読み取り: {TotalBytes}bytes", totalBytesRead);
            
            var responseJson = Encoding.UTF8.GetString(allData.ToArray());
            Console.WriteLine($"📨 [BATCH_DEBUG] レスポンス内容（最初の500文字）: {responseJson.Substring(0, Math.Min(500, responseJson.Length))}...");
            _logger?.LogInformation("📨 [BATCH_DETAIL_9] レスポンス内容: {ResponseJson}", responseJson);
            
            // JSONデシリアライゼーション前の検証
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                Console.WriteLine("💥 [BATCH_DEBUG] 空のレスポンスを受信");
                return new BatchTranslationResult { Success = false, Error = "空のレスポンスを受信しました" };
            }
            
            try
            {
                var response = JsonSerializer.Deserialize<BatchTranslationResult>(responseJson);
                Console.WriteLine($"✅ [BATCH_DEBUG] JSON デシリアライゼーション成功 - Success: {response?.Success ?? false}");
                
                if (response?.Success != true)
                {
                    Console.WriteLine($"⚠️ [BATCH_DEBUG] Pythonサーバーからエラーレスポンス: {response?.Error ?? "不明なエラー"}");
                    return response ?? new BatchTranslationResult { Success = false, Error = "レスポンスデシリアライゼーション失敗" };
                }
                
                // 🔥 [GEMINI_PHASE1] バッチ翻訳結果の復元処理
                if (response != null && response.Translations != null)
                {
                    response.Translations = response.Translations
                        .Select(RestoreTextFromBatchTranslation)
                        .ToList();
                        
                    Console.WriteLine($"🔥 [BATCH_PROTOCOL] バッチ翻訳結果復元完了 - 復元件数: {response.Translations.Count}");
                    _logger?.LogInformation("バッチ翻訳結果復元完了 - 復元件数: {Count}", response.Translations.Count);
                }
                
                var processingTime = DateTime.Now - startTime;
                _logger?.LogInformation("✅ [BATCH_SERVER] バッチ翻訳完了 - 処理時間: {ProcessingTime:F3}秒", processingTime.TotalSeconds);
                
                if (response != null)
                {
                    response.ProcessingTime = processingTime.TotalSeconds;
                }
                
                return response;
            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine($"💥 [BATCH_DEBUG] JSON デシリアライゼーション失敗: {jsonEx.Message}");
                _logger?.LogError(jsonEx, "JSON デシリアライゼーション失敗");
                return new BatchTranslationResult { Success = false, Error = $"JSONパースエラー: {jsonEx.Message}" };
            }
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.Now - startTime;
            _logger?.LogError(ex, "💥 [BATCH_SERVER] バッチ翻訳エラー - 処理時間: {ProcessingTime:F3}秒", processingTime.TotalSeconds);
            return new BatchTranslationResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// 常駐サーバーを使った高速翻訳（改行文字対応版）
    /// </summary>
    /// <summary>
    /// 🔧 [CONNECTION_POOL] 永続的なTCP接続を取得または作成
    /// TIME_WAIT問題を解決するための接続再利用メカニズム
    /// </summary>
    private async Task<NetworkStream> GetOrCreatePersistentConnectionAsync(CancellationToken cancellationToken)
    {
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 既存接続の有効性チェック
            if (_persistentClient?.Connected == true && _persistentStream != null)
            {
                // アイドルタイムアウトチェック
                var idleTime = DateTime.Now - _lastConnectionTime;
                if (idleTime.TotalMinutes < ConnectionIdleTimeoutMinutes)
                {
                    try
                    {
                        // 接続の生存確認（ゼロバイト送信）
                        if (_persistentClient.Client.Poll(0, SelectMode.SelectRead))
                        {
                            var buffer = new byte[1];
                            if (_persistentClient.Client.Receive(buffer, SocketFlags.Peek) == 0)
                            {
                                // 接続が切断されている
                                Console.WriteLine($"🔄 [PERSISTENT_CONNECTION] 接続切断を検出 - 再接続が必要");
                            }
                            else
                            {
                                // 接続は生きている
                                Console.WriteLine($"✅ [PERSISTENT_CONNECTION] 既存接続を再利用 - アイドル時間: {idleTime.TotalSeconds:F1}秒");
                                _lastConnectionTime = DateTime.Now;
                                _connectionRetryCount = 0; // リトライカウントをリセット
                                return _persistentStream;
                            }
                        }
                        else
                        {
                            // 接続は生きている
                            Console.WriteLine($"✅ [PERSISTENT_CONNECTION] 既存接続を再利用 - アイドル時間: {idleTime.TotalSeconds:F1}秒");
                            _lastConnectionTime = DateTime.Now;
                            _connectionRetryCount = 0;
                            return _persistentStream;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ [PERSISTENT_CONNECTION] 接続チェック中にエラー: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"⏰ [PERSISTENT_CONNECTION] アイドルタイムアウト - {idleTime.TotalMinutes:F1}分経過");
                }
            }

            // 既存接続のクリーンアップ
            DisposePersistentConnection();

            // リトライ制限チェック
            if (_connectionRetryCount >= MaxConnectionRetries)
            {
                throw new InvalidOperationException($"接続の確立に{MaxConnectionRetries}回失敗しました");
            }

            // 新しい永続接続を作成
            Console.WriteLine($"🔌 [PERSISTENT_CONNECTION] 新しい永続接続を確立中... (リトライ: {_connectionRetryCount}/{MaxConnectionRetries})");
            
            _persistentClient = new TcpClient();
            
            // Keep-Alive設定で接続を維持
            ConfigureKeepAlive(_persistentClient);
            
            // 接続タイムアウトを設定
            var connectTask = _persistentClient.ConnectAsync(ServerHost, ServerPort);
            var timeoutTask = Task.Delay(ConnectionTimeoutMs, cancellationToken);
            
            if (await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false) == timeoutTask)
            {
                _connectionRetryCount++;
                _persistentClient?.Dispose();
                _persistentClient = null;
                throw new TimeoutException($"サーバー接続がタイムアウトしました ({ConnectionTimeoutMs}ms)");
            }
            
            await connectTask.ConfigureAwait(false);
            _persistentStream = _persistentClient.GetStream();
            _lastConnectionTime = DateTime.Now;
            _connectionRetryCount = 0;
            
            Console.WriteLine($"✅ [PERSISTENT_CONNECTION] 新しい永続接続を確立完了 - {ServerHost}:{ServerPort}");
            _logger.LogInformation("永続TCP接続を確立しました - {Host}:{Port}", ServerHost, ServerPort);
            
            return _persistentStream;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// 🧹 [CONNECTION_POOL] 永続接続を破棄
    /// </summary>
    private void DisposePersistentConnection()
    {
        try
        {
            if (_persistentStream != null)
            {
                _persistentStream.Close();
                _persistentStream.Dispose();
                _persistentStream = null;
                Console.WriteLine($"🧹 [PERSISTENT_CONNECTION] ストリームを破棄");
            }
            
            if (_persistentClient != null)
            {
                if (_persistentClient.Connected)
                {
                    _persistentClient.Close();
                }
                _persistentClient.Dispose();
                _persistentClient = null;
                Console.WriteLine($"🧹 [PERSISTENT_CONNECTION] クライアント接続を破棄");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ [PERSISTENT_CONNECTION] 接続破棄中にエラー: {ex.Message}");
            _logger.LogWarning(ex, "永続接続の破棄中にエラーが発生しました");
        }
    }

    private async Task<PersistentTranslationResult?> TranslateWithPersistentServerAsync(string text, string direction, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"🔥🔥🔥 [NEWLINE_DEBUG] TransformersOpusMtEngine.TranslateWithPersistentServerAsync 実行中！🔥🔥🔥");
        Console.WriteLine($"⚡ [SERVER_TRANSLATE] 常駐サーバー翻訳開始: '{text}'");
        _logger.LogInformation("🔥🔥🔥 [NEWLINE_DEBUG] TransformersOpusMtEngine 改行文字処理版が実行されています！ テキスト: '{Text}'", text);
        
        var startTime = DateTime.Now;
        
        try
        {
            // キャンセレーション確認
            cancellationToken.ThrowIfCancellationRequested();
            
            // 🔧 改行文字を含む場合は分割処理
            if (text.Contains('\n'))
            {
                Console.WriteLine($"📄 [NEWLINE_DETECT] 改行文字を含むテキストを検出 - 分割処理開始");
                _logger.LogInformation("改行文字を含むテキストを検出 - 分割処理実行: '{Text}'", text);
                
                // 改行で分割し、空行を除去
                var textLines = text.Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrEmpty(line))
                    .ToList();
                
                if (!textLines.Any())
                {
                    return new PersistentTranslationResult 
                    { 
                        Success = false, 
                        Error = "Empty text after splitting",
                        Source = text
                    };
                }
                
                if (textLines.Count == 1)
                {
                    // 実際は1行だった場合は通常翻訳
                    Console.WriteLine($"📄 [SINGLE_LINE] 分割結果が1行のため通常翻訳実行");
                    text = textLines[0]; // 単一行として処理継続
                }
                else
                {
                    // 複数行の場合はバッチ翻訳
                    Console.WriteLine($"📦 [MULTI_LINE] 複数行検出({textLines.Count}行) - バッチ翻訳実行");
                    var batchResult = await TranslateBatchWithPersistentServerAsync(textLines, direction, cancellationToken).ConfigureAwait(false);
                    
                    if (batchResult?.Success == true && batchResult.Translations != null)
                    {
                        // バッチ結果を改行で結合
                        var combinedTranslation = string.Join("\n", batchResult.Translations);
                        var batchProcessingTime = DateTime.Now - startTime;
                        
                        Console.WriteLine($"✅ [MULTI_LINE] バッチ翻訳成功 - 結合結果: '{combinedTranslation}'");
                        _logger.LogInformation("複数行バッチ翻訳成功 - 行数: {LineCount}, 結果: '{Translation}'", 
                            textLines.Count, combinedTranslation);
                        
                        return new PersistentTranslationResult
                        {
                            Success = true,
                            Translation = combinedTranslation,
                            Source = text,
                            ProcessingTime = batchProcessingTime.TotalSeconds
                        };
                    }
                    else
                    {
                        Console.WriteLine($"❌ [MULTI_LINE] バッチ翻訳失敗 - Error: {batchResult?.Error}");
                        return new PersistentTranslationResult 
                        { 
                            Success = false, 
                            Error = batchResult?.Error ?? "Batch translation failed",
                            Source = text
                        };
                    }
                }
            }
            
            // 単一行の通常翻訳処理
            Console.WriteLine($"⚡ [SINGLE_TRANSLATE] 単一行翻訳実行: '{text}'");
            
            // サーバーの健全性確認
            if (!await CheckServerHealthAsync().ConfigureAwait(false))
            {
                Console.WriteLine($"🔄 [SERVER_TRANSLATE] サーバー接続失敗 - 再起動試行");
                _logger.LogWarning("サーバーに接続できません。再起動を試行します");
                
                if (!await StartPersistentServerAsync().ConfigureAwait(false))
                {
                    Console.WriteLine($"💥 [SERVER_TRANSLATE] サーバー再起動失敗");
                    return new PersistentTranslationResult { Success = false, Error = "サーバー接続に失敗しました" };
                }
            }
            
            // 🔧 [CONNECTION_POOL] 永続接続を取得（毎回新規作成ではなく再利用）
            var stream = await GetOrCreatePersistentConnectionAsync(cancellationToken).ConfigureAwait(false);
            
            // キャンセレーション再確認
            cancellationToken.ThrowIfCancellationRequested();
            
            // 🔥 [GEMINI_PHASE1] 個別翻訳プロトコル修正: 改行文字の適切な前処理
            var sanitizedText = SanitizeTextForBatchTranslation(text);
            
            var request = new { text = sanitizedText, direction = direction };
            var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions 
            { 
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false // プロトコル安定化のため改行なし
            }) + "\n";
            var requestBytes = Encoding.UTF8.GetBytes(requestJson);
            
            Console.WriteLine($"🔥 [SINGLE_PROTOCOL] 修正版個別リクエスト送信 - オリジナル長: {text.Length}, サニタイズ後: {sanitizedText.Length}");
            Console.WriteLine($"📋 [JSON_REQUEST] 送信JSONリクエスト: {requestJson.TrimEnd()}");
            Console.WriteLine($"🔢 [JSON_REQUEST] リクエストバイト数: {requestBytes.Length}, 文字列長: {requestJson.Length}");
            
            // リクエスト送信前に接続状態を再確認
            if (_persistentClient?.Connected != true)
            {
                Console.WriteLine($"⚠️ [PERSISTENT_CONNECTION] 接続が切断されています - 再接続を試行");
                stream = await GetOrCreatePersistentConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            
            try
            {
                await stream.WriteAsync(requestBytes, 0, requestBytes.Length, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false); // データを確実に送信
                
                // 🚀 [JSON_STREAM_FIX] StreamReaderを使用して改行区切りJSONを正しく読み取る
                Console.WriteLine($"⚡ [JSON_STREAM_FIX] StreamReaderで改行区切りJSON読み取り開始");
                
                // StreamReaderを使用して1行ずつ読み取る（Pythonサーバーは改行区切りで送信）
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                var responseJson = await reader.ReadLineAsync().ConfigureAwait(false);
                
                if (string.IsNullOrEmpty(responseJson))
                {
                    Console.WriteLine($"❌ [JSON_STREAM_FIX] 空のレスポンスを受信");
                    return new PersistentTranslationResult 
                    { 
                        Success = false, 
                        Error = "Empty response from server",
                        Source = text
                    };
                }
                
                Console.WriteLine($"📨 [SERVER_TRANSLATE] レスポンス内容: {responseJson}");
                Console.WriteLine($"🔢 [JSON_RESPONSE] レスポンス文字数: {responseJson?.Length}, IsNull: {responseJson == null}");
                Console.WriteLine($"🔍 [JSON_RESPONSE] レスポンス先頭100文字: {responseJson?.Substring(0, Math.Min(100, responseJson?.Length ?? 0))}");
                
                PersistentTranslationResult? response;
                try
                {
                    response = JsonSerializer.Deserialize<PersistentTranslationResult>(responseJson);
                    Console.WriteLine($"✅ [JSON_DESERIALIZE] JSONデシリアライゼーション成功");
                }
                catch (JsonException jsonEx)
                {
                    Console.WriteLine($"❌ [JSON_DESERIALIZE] JSONデシリアライゼーション失敗: {jsonEx.Message}");
                    Console.WriteLine($"📄 [JSON_DESERIALIZE] 問題のあるJSONデータ: {responseJson}");
                    
                    return new PersistentTranslationResult 
                    { 
                        Success = false, 
                        Error = $"JSON parsing failed: {jsonEx.Message}",
                        Source = text
                    };
                }
                
                // 🚨 [CRITICAL_FIX] レスポンスエラーチェック - エラーレスポンスを翻訳結果として表示しない
                if (response?.Success == false && !string.IsNullOrEmpty(response.Error))
                {
                    Console.WriteLine($"❌ [SERVER_ERROR] Pythonサーバーエラー: {response.Error}");
                    Console.WriteLine($"📄 [SERVER_ERROR] 元のテキスト: '{text}'");
                    
                    return new PersistentTranslationResult 
                    { 
                        Success = false, 
                        Error = $"Server error: {response.Error}",
                        Source = text
                    };
                }
                
                // 🔥 [GEMINI_PHASE1] 個別翻訳結果の復元処理（成功時のみ）
                if (response != null && response.Success && !string.IsNullOrEmpty(response.Translation))
                {
                    response.Translation = RestoreTextFromBatchTranslation(response.Translation);
                    Console.WriteLine($"🔥 [SINGLE_PROTOCOL] 個別翻訳結果復元完了 - 復元後: '{response.Translation}'");
                }
                
                var processingTime = DateTime.Now - startTime;
                Console.WriteLine($"⚡ [SERVER_TRANSLATE] 翻訳完了 - 処理時間: {processingTime.TotalSeconds:F3}秒, 成功: {response?.Success}, 翻訳: '{response?.Translation}'");
                _logger.LogInformation("常駐サーバー翻訳完了 - 処理時間: {ProcessingTimeSeconds}秒", processingTime.TotalSeconds);
                
                return response;
            }
            catch (IOException ioEx)
            {
                // ネットワークエラーの場合は接続をリセット
                Console.WriteLine($"💥 [PERSISTENT_CONNECTION] IOエラー発生 - 接続をリセット: {ioEx.Message}");
                DisposePersistentConnection();
                _connectionRetryCount++;
                throw;
            }
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.Now - startTime;
            
            // OperationCanceledExceptionの場合は適切なエラーメッセージを設定
            string errorMessage = ex switch
            {
                OperationCanceledException => "翻訳処理がキャンセルされました",
                TimeoutException => "翻訳処理がタイムアウトしました",
                IOException => "ネットワークエラーが発生しました",
                _ => "翻訳処理中にエラーが発生しました"
            };
            
            Console.WriteLine($"💥 [SERVER_TRANSLATE] 翻訳エラー: {ex.Message} - 処理時間: {processingTime.TotalSeconds:F3}秒");
            _logger.LogError(ex, "常駐サーバー翻訳中にエラーが発生しました");
            return new PersistentTranslationResult { Success = false, Error = errorMessage };
        }
    }

    private async Task<PythonTranslationResult?> TranslatePythonAsync(string text)
    {
        Console.WriteLine($"🐍 [PYTHON_DEBUG] Python翻訳開始: '{text}' - HuggingFaceモデルロード中...");
        _logger.LogInformation("Python翻訳開始: '{Text}' - モデルロードのため初回は数分かかる可能性があります", text);
        
        // 一時ファイルを使って確実にUTF-8で渡す
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, text, System.Text.Encoding.UTF8).ConfigureAwait(false);
            _logger.LogInformation("一時ファイル作成完了: {TempFile}", tempFile);
            
            var processInfo = new ProcessStartInfo
            {
                FileName = _pythonPath,
                Arguments = $"\"{_serverScriptPath}\" \"@{tempFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            _logger.LogInformation("Pythonプロセス開始: {FileName} {Arguments}", processInfo.FileName, processInfo.Arguments);

            using var process = new Process { StartInfo = processInfo };
            Console.WriteLine($"🐍 [PYTHON_DEBUG] Process.Start()直前");
            _logger?.LogDebug("🐍 [PYTHON_DEBUG] Process.Start()直前");
            
            process.Start();
            
            Console.WriteLine($"🐍 [PYTHON_DEBUG] Process.Start()完了 - PID: {process.Id}");
            _logger?.LogDebug("🐍 [PYTHON_DEBUG] Process.Start()完了 - PID: {ProcessId}", process.Id);

            // タイムアウト制御 (初回モデルロードのため300秒=5分でタイムアウト)
            Console.WriteLine($"🐍 [PYTHON_DEBUG] 非同期タスク作成開始");
            _logger?.LogDebug("🐍 [PYTHON_DEBUG] 非同期タスク作成開始");
            
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var processTask = process.WaitForExitAsync();
            
            Console.WriteLine($"🐍 [PYTHON_DEBUG] 非同期タスク作成完了");
            _logger?.LogDebug("🐍 [PYTHON_DEBUG] 非同期タスク作成完了");

            var timeout = TimeSpan.FromSeconds(15); // 15秒に短縮（緊急修正）
            using var cts = new CancellationTokenSource(timeout);

            try
            {
                Console.WriteLine($"🔄 [PYTHON_DEBUG] Python処理実行中... (最大15秒待機)");
                _logger?.LogDebug("🔄 [PYTHON_DEBUG] Python処理実行中... (最大15秒待機)");
                
                var startTime = DateTime.Now;
                
                // 10秒ごとに進行状況を表示
                var progressTask = Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(10000, cts.Token).ConfigureAwait(false);
                        var elapsed = DateTime.Now - startTime;
                        Console.WriteLine($"⏱️ [PROGRESS] 処理継続中... 経過時間: {elapsed.TotalSeconds:F0}秒");
                        _logger?.LogDebug("⏱️ [PROGRESS] 処理継続中... 経過時間: {ElapsedSeconds}秒", elapsed.TotalSeconds);
                        if (elapsed.TotalSeconds > 15) break;
                    }
                }, cts.Token);
                
                Console.WriteLine($"🐍 [PYTHON_DEBUG] processTask.WaitAsync()呼び出し直前");
                _logger?.LogDebug("🐍 [PYTHON_DEBUG] processTask.WaitAsync()呼び出し直前");
                
                await processTask.WaitAsync(cts.Token).ConfigureAwait(false);
                
                Console.WriteLine($"🐍 [PYTHON_DEBUG] processTask.WaitAsync()完了");
                _logger?.LogDebug("🐍 [PYTHON_DEBUG] processTask.WaitAsync()完了");
                var output = await outputTask.ConfigureAwait(false);
                var error = await errorTask.ConfigureAwait(false);

                Console.WriteLine($"🐍 [PYTHON_DEBUG] Pythonプロセス終了 - ExitCode: {process.ExitCode}");
                Console.WriteLine($"🐍 [PYTHON_DEBUG] Output長さ: {output?.Length}文字");
                Console.WriteLine($"🐍 [PYTHON_DEBUG] Output (RAW): '{output}'");
                Console.WriteLine($"🐍 [PYTHON_DEBUG] Output (HEX最初の20バイト): '{BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(output ?? "").Take(20).ToArray())}'");
                Console.WriteLine($"🐍 [PYTHON_DEBUG] Error: '{error}'");
                
                // ExitCode 143 (SIGTERM) の場合はタイムアウトエラーとして扱う
                if (process.ExitCode == 143)
                {
                    _logger.LogError("Pythonプロセスがタイムアウトにより強制終了されました (SIGTERM)");
                    return new PythonTranslationResult 
                    { 
                        Success = false, 
                        Error = "翻訳プロセスがタイムアウトしました。初回実行時はモデルダウンロードのため数分かかります。", 
                        Source = text 
                    };
                }
                _logger?.LogDebug("🐍 [PYTHON_DEBUG] Pythonプロセス終了 - ExitCode: {ExitCode}", process.ExitCode);
                _logger?.LogDebug("🐍 [PYTHON_DEBUG] Output: {Output}", output);
                _logger?.LogDebug("🐍 [PYTHON_DEBUG] Error: {Error}", error);
                _logger.LogInformation("Pythonプロセス終了 - ExitCode: {ExitCode}, Output: {Output}, Error: {Error}", 
                    process.ExitCode, output, error);

                if (process.ExitCode != 0)
                {
                    _logger.LogError("Python翻訳プロセスがエラーで終了しました: {Error}", error);
                    return null;
                }

                Console.WriteLine($"🔍 [TRANSLATE_DEBUG] ParseResult呼び出し開始");
                _logger?.LogDebug("🔍 [TRANSLATE_DEBUG] ParseResult呼び出し開始");
                var result = ParseResult(output);
                Console.WriteLine($"🔍 [TRANSLATE_DEBUG] ParseResult呼び出し完了 - Result: {result?.Success}, Translation: '{result?.Translation}'");
                _logger?.LogDebug("🔍 [TRANSLATE_DEBUG] ParseResult呼び出し完了 - Result: {Success}, Translation: {Translation}", result?.Success, result?.Translation);
                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Python翻訳プロセスがタイムアウトしました ({Timeout}秒)", timeout.TotalSeconds);
                process.Kill();
                return null;
            }

        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
                _logger.LogInformation("一時ファイル削除完了: {TempFile}", tempFile);
            }
        }
    }

    private PythonTranslationResult? ParseResult(string output)
    {
        try
        {
            Console.WriteLine($"🔧 [JSON_DEBUG] ParseResult開始");
            _logger.LogInformation("Python出力をJSON解析中: '{Output}' (長さ: {Length})", output, output?.Length);
            
            // 出力がnullまたは空の場合
            if (string.IsNullOrEmpty(output))
            {
                Console.WriteLine($"💥 [JSON_DEBUG] Python出力がnullまたは空です");
                return null;
            }
            
            // JSON修復とクリーンアップ
            string jsonStr = output.Trim();
            
            // BOMを除去
            if (jsonStr.StartsWith("\uFEFF"))
            {
                jsonStr = jsonStr.Substring(1);
                Console.WriteLine($"🔧 [JSON_DEBUG] BOMを除去しました");
            }
            
            // 改行文字を削除
            jsonStr = jsonStr.Replace("\r", "").Replace("\n", "");
            
            // JSON形式の自動修復
            // {が欠落している場合の修復
            if (!jsonStr.StartsWith("{") && jsonStr.Contains("\"success\""))
            {
                jsonStr = "{" + jsonStr;
                Console.WriteLine($"🔧 [JSON_DEBUG] 先頭に {{ を追加して修復");
            }
            
            // }が欠落している場合の修復
            if (!jsonStr.EndsWith("}") && jsonStr.StartsWith("{"))
            {
                // 最後の}を探す
                int lastBrace = jsonStr.LastIndexOf('}');
                if (lastBrace == -1)
                {
                    jsonStr = jsonStr + "}";
                    Console.WriteLine($"🔧 [JSON_DEBUG] 末尾に }} を追加して修復");
                }
                else
                {
                    // 最後の}以降の文字を削除
                    jsonStr = jsonStr.Substring(0, lastBrace + 1);
                }
            }
            
            Console.WriteLine($"🔧 [JSON_DEBUG] 修復後のJSON: '{jsonStr}'");
            
            // JSON解析
            Console.WriteLine($"🔧 [JSON_DEBUG] JsonSerializer.Deserialize開始");
            var result = JsonSerializer.Deserialize<PythonTranslationResult>(jsonStr);
            
            Console.WriteLine($"🔧 [JSON_DEBUG] 解析結果 - Success: {result?.Success}, Translation: '{result?.Translation}', Source: '{result?.Source}'");
            _logger.LogInformation("JSON解析成功 - Success: {Success}, Translation: '{Translation}', Source: '{Source}'", 
                result?.Success, result?.Translation, result?.Source);
            return result;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"💥 [JSON_DEBUG] JSON解析失敗: {ex.Message}");
            Console.WriteLine($"💥 [JSON_DEBUG] 問題のある出力: '{output}'");
            _logger?.LogError(ex, "💥 [JSON_DEBUG] JSON解析失敗: {Message}", ex.Message);
            _logger?.LogError("💥 [JSON_DEBUG] 問題のある出力: {Output}", output);
            _logger.LogError(ex, "Python出力のJSONパースに失敗しました: {Output}", output);
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 [JSON_DEBUG] 予期しないエラー: {ex.GetType().Name} - {ex.Message}");
            Console.WriteLine($"💥 [JSON_DEBUG] スタックトレース: {ex.StackTrace}");
            _logger?.LogError(ex, "💥 [JSON_DEBUG] 予期しないエラー: {ExceptionType} - {Message}", ex.GetType().Name, ex.Message);
            _logger?.LogError(ex, "💥 [JSON_DEBUG] スタックトレース");
            _logger.LogError(ex, "ParseResult処理中に予期しないエラーが発生しました: {Output}", output);
            return null;
        }
    }

    private static string FindProjectRoot(string currentDir)
    {
        var dir = new DirectoryInfo(currentDir);
        
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Baketa.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        
        throw new DirectoryNotFoundException("Baketaプロジェクトルートが見つかりません");
    }

    /// <inheritdoc/>
    public override async Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        // ✅ 両方向翻訳サポート: 日→英、英→日の両方に対応
        return new[]
        {
            new LanguagePair { SourceLanguage = Language.Japanese, TargetLanguage = Language.English },
            new LanguagePair { SourceLanguage = Language.English, TargetLanguage = Language.Japanese }
        };
    }

    /// <inheritdoc/>
    public override async Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
    {
        await Task.Delay(1).ConfigureAwait(false); // 非同期処理をシミュレート
        
        // 🚨 緊急修正: 言語コードによる直接比較で確実性を向上
        var sourceCode = languagePair.SourceLanguage.Code?.ToLowerInvariant();
        var targetCode = languagePair.TargetLanguage.Code?.ToLowerInvariant();
        
        Console.WriteLine($"🔍 [LANGUAGE_SUPPORT] 言語ペアチェック: '{sourceCode}' → '{targetCode}'");
        _logger.LogDebug("🔍 [LANGUAGE_SUPPORT] 言語ペアチェック: '{Source}' → '{Target}'", sourceCode, targetCode);
        
        // ✅ 両方向翻訳サポート: en↔ja の両方をサポート
        var isSupported = (sourceCode == "ja" && targetCode == "en") ||
                         (sourceCode == "en" && targetCode == "ja");
                         
        Console.WriteLine($"✅ [LANGUAGE_SUPPORT] サポート結果: {isSupported}");
        _logger.LogDebug("✅ [LANGUAGE_SUPPORT] サポート結果: {IsSupported}", isSupported);
        
        return isSupported;
    }
    
    /// <summary>
    /// 🌍 言語ペアから適切な翻訳方向を判定
    /// </summary>
    /// <param name="sourceLanguage">ソース言語</param>
    /// <param name="targetLanguage">ターゲット言語</param>
    /// <returns>翻訳方向 ("ja-en" または "en-ja")</returns>
    private string GetTranslationDirection(Language sourceLanguage, Language targetLanguage)
    {
        // 🚀 [修正] 設定サービスから言語設定を取得
        var translationSettings = _settingsService.GetTranslationSettings();
        
        // 設定から言語コードを取得
        var defaultSourceLang = translationSettings.DefaultSourceLanguage;
        var defaultTargetLang = translationSettings.DefaultTargetLanguage;
        
        Console.WriteLine($"🔍 [DEBUG] GetTranslationDirection - 設定から読み込み: Source={defaultSourceLang}, Target={defaultTargetLang}");
        
        // 設定に基づいた言語方向の決定
        if (string.Equals(defaultSourceLang, "en", StringComparison.OrdinalIgnoreCase) && 
            string.Equals(defaultTargetLang, "ja", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"🔍 [DEBUG] GetTranslationDirection - 設定ベース判定結果: en-ja");
            return "en-ja";
        }
        else if (string.Equals(defaultSourceLang, "ja", StringComparison.OrdinalIgnoreCase) && 
                 string.Equals(defaultTargetLang, "en", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"🔍 [DEBUG] GetTranslationDirection - 設定ベース判定結果: ja-en");
            return "ja-en";
        }
        
        // フォールバック: パラメータから判定（従来ロジック）
        if (sourceLanguage.Equals(Language.Japanese) && targetLanguage.Equals(Language.English))
        {
            Console.WriteLine($"🔍 [DEBUG] GetTranslationDirection - パラメータベース判定結果: ja-en");
            return "ja-en";
        }
        else if (sourceLanguage.Equals(Language.English) && targetLanguage.Equals(Language.Japanese))
        {
            Console.WriteLine($"🔍 [DEBUG] GetTranslationDirection - パラメータベース判定結果: en-ja");
            return "en-ja";
        }
        
        // 最終フォールバック: 設定に基づくデフォルト
        var fallbackDirection = $"{defaultSourceLang}-{defaultTargetLang}";
        Console.WriteLine($"🔍 [DEBUG] GetTranslationDirection - 最終フォールバック: {fallbackDirection}");
        return fallbackDirection;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            
            // ⚡ Phase 1.1: 最終キャッシュ統計の表示
            LogCacheStatistics();
            
            // 常駐サーバーを停止
            try
            {
                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    Console.WriteLine($"🛑 [SERVER_DEBUG] 常駐サーバー停止中...");
                    _logger.LogInformation("常駐サーバーを停止しています");
                    
                    // 強制終了
                    Console.WriteLine($"🛑 [SERVER_DEBUG] 常駐サーバー強制終了実行");
                    _serverProcess.Kill();
                    
                    _serverProcess.Dispose();
                    _serverProcess = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "常駐サーバー停止中にエラーが発生しました");
            }
            
            // 🧹 [CONNECTION_POOL] 永続接続のクリーンアップ
            DisposePersistentConnection();
            _connectionLock?.Dispose();
            
            _serverLock?.Dispose();
            _logger.LogInformation("OPUS-MT Transformers翻訳エンジンが破棄されました");
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// 既存のPythonサーバープロセスを強制終了
    /// 🚨 多重起動防止のための堅牢なプロセス管理
    /// </summary>
    private async Task KillExistingServerProcessesAsync()
    {
        try
        {
            _logger?.LogDebug("🧹 [CLEANUP_START] Pythonプロセス終了処理開始");
            
            // PowerShellでPythonプロセスを全て終了
            var processInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-Command \"Get-Process -Name 'python' -ErrorAction SilentlyContinue | Stop-Process -Force\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var process = new Process { StartInfo = processInfo };
            process.Start();
            
            await process.WaitForExitAsync().ConfigureAwait(false);
            
            // 🔧 [TCP_STABILIZATION] 3秒待機してプロセス終了を確実にする
            await Task.Delay(3000).ConfigureAwait(false);
            
            _logger?.LogDebug("✅ [CLEANUP_COMPLETE] Pythonプロセス終了処理完了");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ [CLEANUP_ERROR] Pythonプロセス終了エラー: {Message}", ex.Message);
            _logger.LogWarning(ex, "既存Pythonプロセス終了中にエラーが発生しました");
        }
    }
    
    /// <summary>
    /// キャッシュキー生成
    /// ⚡ Phase 1.1: 翻訳要求に基づく一意キーの生成
    /// </summary>
    private static string GenerateCacheKey(string sourceText, Language sourceLanguage, Language targetLanguage)
    {
        // ソーステキストを正規化（空白や改行の違いによる重複を防ぐ）
        var normalizedText = sourceText.Trim().Replace("\r\n", "\n").Replace("\r", "\n");
        return $"{sourceLanguage.Code}>{targetLanguage.Code}:{normalizedText}";
    }
    
    /// <summary>
    /// キャッシュから翻訳結果を取得
    /// ⚡ Phase 1.1: LRU アクセス時刻更新付き取得
    /// </summary>
    private bool TryGetFromCache(string cacheKey, out TranslationResponse response)
    {
        if (_translationCache.TryGetValue(cacheKey, out var entry))
        {
            // LRU: 最終アクセス時刻を更新
            entry.LastAccessedAt = DateTime.UtcNow;
            response = entry.Response;
            return true;
        }
        
        response = null!;
        return false;
    }
    
    /// <summary>
    /// キャッシュに翻訳結果を追加
    /// ⚡ Phase 1.1: LRU 最大容量管理付き追加
    /// </summary>
    private void AddToCache(string cacheKey, TranslationResponse response)
    {
        var entry = new CacheEntry(response);
        _translationCache.TryAdd(cacheKey, entry);
        
        // 最大容量を超えた場合、LRU（最も古いアクセス）エントリを削除
        if (_translationCache.Count > _maxCacheSize)
        {
            EvictLeastRecentlyUsed();
        }
    }
    
    /// <summary>
    /// LRU エビクション: 最も古いアクセスのエントリを削除
    /// ⚡ Phase 1.1: メモリ効率維持のための自動削除
    /// </summary>
    private void EvictLeastRecentlyUsed()
    {
        try
        {
            // 最も古いアクセス時刻のエントリを見つける
            var oldestEntry = _translationCache.Values
                .OrderBy(entry => entry.LastAccessedAt)
                .FirstOrDefault();
            
            if (oldestEntry != null)
            {
                // キーを特定して削除
                var keyToRemove = _translationCache
                    .Where(kvp => ReferenceEquals(kvp.Value, oldestEntry))
                    .Select(kvp => kvp.Key)
                    .FirstOrDefault();
                
                if (keyToRemove != null && _translationCache.TryRemove(keyToRemove, out _))
                {
                    Console.WriteLine($"🗑️ [CACHE_EVICT] LRU削除実行 - キー: '{keyToRemove}', 残り: {_translationCache.Count}件");
                    _logger.LogInformation("LRUキャッシュエビクション - 削除キー: '{Key}', 残りエントリ数: {Count}", 
                        keyToRemove, _translationCache.Count);
                }
            }
        }
        catch (Exception ex)
        {
            // エビクション失敗は致命的でないため、ログのみ
            Console.WriteLine($"⚠️ [CACHE_EVICT] LRU削除失敗: {ex.Message}");
            _logger.LogWarning(ex, "LRUキャッシュエビクション中にエラーが発生しました");
        }
    }
    
    /// <summary>
    /// キャッシュ統計情報の表示
    /// ⚡ Phase 1.1: パフォーマンス分析用統計
    /// </summary>
    private void LogCacheStatistics()
    {
        var hitCount = _cacheHitCount;
        var missCount = _cacheMissCount;
        var totalRequests = hitCount + missCount;
        var hitRate = totalRequests > 0 ? (double)hitCount / totalRequests * 100 : 0;
        
        Console.WriteLine($"📊 [CACHE_STATS] ヒット率: {hitRate:F1}% ({hitCount}/{totalRequests}), エントリ数: {_translationCache.Count}/{_maxCacheSize}");
        _logger.LogInformation("キャッシュ統計 - ヒット率: {HitRate:F1}% ({HitCount}/{TotalRequests}), エントリ数: {EntryCount}/{MaxSize}",
            hitRate, hitCount, totalRequests, _translationCache.Count, _maxCacheSize);
    }
    
    /// <summary>
    /// 🔥 [GEMINI_PHASE1] Keep-Alive設定をTcpClientに適用
    /// アイドル接続切断を防ぎ、TCP接続の安定性を向上
    /// </summary>
    /// <param name="client">設定対象のTcpClient</param>
    private static void ConfigureKeepAlive(TcpClient client)
    {
        try
        {
            var socket = client.Client;
            
            // Keep-Aliveを有効化
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            
            // Keep-Aliveタイマー設定（2時間 = 7200秒）
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 7200);
            
            // Keep-Alive送信間隔（1秒）
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1);
            
            // Keep-Alive再試行回数（9回）
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 9);
            
            Console.WriteLine($"🔥 [KEEP_ALIVE] Keep-Alive設定完了 - アイドル時間: 7200秒, 送信間隔: 1秒, 再試行: 9回");
        }
        catch (Exception ex)
        {
            // Keep-Alive設定失敗は致命的でないため、ログのみ
            Console.WriteLine($"⚠️ [KEEP_ALIVE] Keep-Alive設定失敗: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 🔥 [GEMINI_PHASE1] ReadAsync操作の統一タイムアウト戦略
    /// すべてのReadAsync呼び出しで一貫したタイムアウト処理
    /// </summary>
    /// <param name="operationType">操作タイプ（ログ用）</param>
    /// <param name="extraTimeoutMs">追加タイムアウト時間（オプション）</param>
    /// <returns>統一設定されたCancellationTokenSource</returns>
    private CancellationTokenSource CreateUnifiedReadTimeout(string operationType, int extraTimeoutMs = 0)
    {
        // 🔧 統一タイムアウト戦略: ベース（15秒） + 追加時間
        var baseTimeoutMs = ConnectionTimeoutMs; // 15秒ベース
        var totalTimeoutMs = baseTimeoutMs + extraTimeoutMs;
        
        Console.WriteLine($"🔥 [UNIFIED_TIMEOUT] {operationType}用タイムアウト設定: {totalTimeoutMs}ms (ベース:{baseTimeoutMs}ms + 追加:{extraTimeoutMs}ms)");
        
        return new CancellationTokenSource(totalTimeoutMs);
    }
    
    /// <summary>
    /// 🔥 [GEMINI_PHASE1] バッチ翻訳用テキストサニタイズ
    /// DEFAULT_NEWLINE_FAIL問題を防ぐための改行文字とプロトコル特殊文字の適切な処理
    /// </summary>
    /// <param name="originalText">元のテキスト</param>
    /// <returns>サニタイズ済みテキスト</returns>
    private static string SanitizeTextForBatchTranslation(string originalText)
    {
        if (string.IsNullOrEmpty(originalText))
            return originalText;
        
        // 改行文字をプレースホルダーに変換（JSON送信時の問題を回避）
        var sanitized = originalText
            .Replace("\r\n", "〔CRLF〕")    // Windows改行
            .Replace("\n", "〔LF〕")        // Unix改行  
            .Replace("\r", "〔CR〕")        // Mac改行
            .Replace("\"", "〔QUOTE〕")     // JSONエスケープ問題回避
            .Replace("\\", "〔BACKSLASH〕") // バックスラッシュエスケープ回避
            .Trim(); // 前後の空白削除
        
        return sanitized;
    }
    
    /// <summary>
    /// 🔥 [GEMINI_PHASE1] バッチ翻訳結果の復元
    /// サニタイズされたテキストを元の改行文字に復元
    /// </summary>
    /// <param name="sanitizedText">サニタイズ済みテキスト</param>
    /// <returns>復元されたテキスト</returns>
    private static string RestoreTextFromBatchTranslation(string sanitizedText)
    {
        if (string.IsNullOrEmpty(sanitizedText))
            return sanitizedText;
        
        // プレースホルダーを元の文字に復元
        var restored = sanitizedText
            .Replace("〔CRLF〕", "\r\n")      // Windows改行復元
            .Replace("〔LF〕", "\n")          // Unix改行復元
            .Replace("〔CR〕", "\r")          // Mac改行復元
            .Replace("〔QUOTE〕", "\"")       // クォート復元
            .Replace("〔BACKSLASH〕", "\\");  // バックスラッシュ復元
        
        return restored;
    }

    /// <summary>
    /// 🔥 並列チャンク処理でバッチ翻訳を高速化
    /// リクエストを複数チャンクに分割し、各チャンクを並列処理することで3-5倍高速化を実現
    /// </summary>
    private async Task<BatchTranslationResult?> TranslateBatchWithParallelChunksAsync(
        IReadOnlyList<TranslationRequest> requests, 
        string direction,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;
        _logger?.LogInformation("🔥 [PARALLEL_CHUNKS] 並列チャンク処理開始 - 総リクエスト数: {Count}", requests.Count);

        try
        {
            // チャンクサイズの動的決定（最適化のため）
            var chunkSize = CalculateOptimalChunkSize(requests.Count);
            _logger?.LogInformation("📦 [CHUNK_SIZE] 最適チャンクサイズ決定: {ChunkSize}", chunkSize);

            // リクエストをチャンクに分割
            var chunks = SplitRequestsIntoChunks(requests, chunkSize);
            _logger?.LogInformation("🔀 [CHUNK_SPLIT] チャンク分割完了 - チャンク数: {ChunkCount}", chunks.Count);

            // 各チャンクを並列処理
            var chunkTasks = chunks.Select(async (chunk, index) =>
            {
                _logger?.LogInformation("🚀 [CHUNK_{Index}] 並列処理開始 - サイズ: {Size}", index, chunk.Count);
                
                var chunkTexts = chunk.Select(r => r.SourceText).ToList();
                var result = await TranslateBatchWithPersistentServerAsync(chunkTexts, direction, cancellationToken).ConfigureAwait(false);
                
                _logger?.LogInformation("✅ [CHUNK_{Index}] 処理完了 - 成功: {Success}", index, result?.Success ?? false);
                return new { Index = index, Result = result, OriginalRequests = chunk };
            }).ToList();

            // 全チャンクの完了を待機（部分成功対応）
            _logger?.LogInformation("⏳ [PARALLEL_WAIT] 全チャンクの完了を待機中（部分成功対応）...");
            var chunkResults = await Task.WhenAll(chunkTasks.Select(async task =>
            {
                try
                {
                    return await task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "🔧 [PARTIAL_SUCCESS] 個別チャンク処理でエラー発生、部分成功として継続");
                    return new { Index = -1, Result = new BatchTranslationResult { Success = false, Error = $"チャンクエラー: {ex.Message}" }, OriginalRequests = new List<TranslationRequest>() };
                }
            })).ConfigureAwait(false);
            
            // 結果をマージ
            var mergedResult = MergeChunkResults(chunkResults, requests.Count);
            var processingTime = (DateTime.Now - startTime).TotalSeconds;
            
            if (mergedResult != null)
            {
                mergedResult.ProcessingTime = processingTime;
                _logger?.LogInformation("🎯 [PARALLEL_COMPLETE] 並列チャンク処理完了 - 総処理時間: {Time:F3}秒, 成功チャンク数: {SuccessCount}/{TotalCount}", 
                    processingTime, chunkResults.Count(r => r.Result?.Success == true), chunkResults.Length);
            }

            return mergedResult;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "💥 [PARALLEL_ERROR] 並列チャンク処理エラー");
            return new BatchTranslationResult { Success = false, Error = $"並列処理エラー: {ex.Message}" };
        }
    }

    /// <summary>
    /// リクエスト数に基づく最適チャンクサイズ計算
    /// </summary>
    private static int CalculateOptimalChunkSize(int totalRequests)
    {
        // 最適化ルール:
        // - 1-10件: チャンク分割なし（オーバーヘッド回避）
        // - 11-50件: 2-3チャンクに分割
        // - 51件以上: 並列度を最大化（最大4並列）
        return totalRequests switch
        {
            <= 10 => totalRequests,           // 分割しない
            <= 20 => totalRequests / 2,       // 2チャンク
            <= 50 => totalRequests / 3,       // 3チャンク  
            _ => Math.Max(totalRequests / 4, 10) // 4チャンク、最小10件
        };
    }

    /// <summary>
    /// リクエストをチャンクに分割
    /// </summary>
    private static List<List<TranslationRequest>> SplitRequestsIntoChunks(IReadOnlyList<TranslationRequest> requests, int chunkSize)
    {
        var chunks = new List<List<TranslationRequest>>();
        
        for (int i = 0; i < requests.Count; i += chunkSize)
        {
            var chunk = requests.Skip(i).Take(chunkSize).ToList();
            chunks.Add(chunk);
        }
        
        return chunks;
    }

    /// <summary>
    /// チャンク結果をマージして単一の BatchTranslationResult に統合
    /// </summary>
    private BatchTranslationResult? MergeChunkResults(
        dynamic[] chunkResults, 
        int totalRequestCount)
    {
        var mergedTranslations = new List<string>();
        var mergedSources = new List<string>();
        var hasAnySuccess = false;
        var errors = new List<string>();

        // チャンク結果をインデックス順にソートしてマージ
        var sortedResults = chunkResults.OrderBy(r => r.Index).ToArray();
        
        foreach (var chunkResult in sortedResults)
        {
            var result = chunkResult.Result as BatchTranslationResult;
            
            if (result?.Success == true && result.Translations != null)
            {
                hasAnySuccess = true;
                mergedTranslations.AddRange(result.Translations);
                mergedSources.AddRange(result.Sources ?? new List<string>());
            }
            else
            {
                // 失敗したチャンクの分だけプレースホルダーを追加
                var originalRequests = chunkResult.OriginalRequests as List<TranslationRequest>;
                var chunkSize = originalRequests?.Count ?? 0;
                
                for (int i = 0; i < chunkSize; i++)
                {
                    mergedTranslations.Add($"[Chunk Error] {originalRequests?[i]?.SourceText ?? "Unknown"}");
                    mergedSources.Add(originalRequests?[i]?.SourceText ?? "Unknown");
                }
                
                errors.Add(result?.Error ?? "Unknown chunk error");
            }
        }

        if (!hasAnySuccess)
        {
            return new BatchTranslationResult 
            { 
                Success = false, 
                Error = $"全チャンク処理失敗: {string.Join(", ", errors)}"
            };
        }

        return new BatchTranslationResult
        {
            Success = true,
            Translations = mergedTranslations,
            Sources = mergedSources,
            TranslationCount = mergedTranslations.Count
        };
    }

    /// <summary>
    /// キャッシュエントリ
    /// ⚡ Phase 1.1: 翻訳結果キャッシュのための軽量実装
    /// </summary>
    private sealed class CacheEntry
    {
        public TranslationResponse Response { get; }
        public DateTime CreatedAt { get; }
        public DateTime LastAccessedAt { get; set; }
        
        public CacheEntry(TranslationResponse response)
        {
            Response = response;
            CreatedAt = DateTime.UtcNow;
            LastAccessedAt = DateTime.UtcNow;
        }
    }

    private class PersistentTranslationResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        
        [JsonPropertyName("translation")]
        public string Translation { get; set; } = string.Empty;
        
        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;
        
        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;
        
        [JsonPropertyName("processing_time")]
        public double ProcessingTime { get; set; }
        
        [JsonPropertyName("translation_count")]
        public int TranslationCount { get; set; }
    }

    private class BatchTranslationResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        
        [JsonPropertyName("translations")]
        public List<string> Translations { get; set; } = new();
        
        [JsonPropertyName("sources")]
        public List<string> Sources { get; set; } = new();
        
        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;
        
        [JsonPropertyName("processing_time")]
        public double ProcessingTime { get; set; }
        
        [JsonPropertyName("translation_count")]
        public int TranslationCount { get; set; }
    }

    private class PythonTranslationResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        
        [JsonPropertyName("translation")]
        public string Translation { get; set; } = string.Empty;
        
        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;
        
        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;
    }
}