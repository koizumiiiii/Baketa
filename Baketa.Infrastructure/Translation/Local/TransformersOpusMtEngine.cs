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
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local;

/// <summary>
/// HuggingFace Transformers基盤OPUS-MT翻訳エンジン
/// Python統合により語彙サイズ不整合問題を完全解決
/// </summary>
public class TransformersOpusMtEngine : TranslationEngineBase
{
    private readonly ILogger<TransformersOpusMtEngine> _logger;
    private readonly string _pythonPath;
    private readonly string _serverScriptPath;
    private Process? _serverProcess;
    private bool _isInitialized;
    private bool _disposed;
    private readonly SemaphoreSlim _serverLock = new(1, 1);
    
    // ⚡ Phase 1.1: LRU翻訳キャッシュ（シンプル実装）
    private readonly ConcurrentDictionary<string, CacheEntry> _translationCache = new();
    private readonly int _maxCacheSize = 1000;
    private long _cacheHitCount;
    private long _cacheMissCount;
    
    // 常駐サーバー設定
    private const string ServerHost = "127.0.0.1";
    private const int ServerPort = 29876;
    private const int ConnectionTimeoutMs = 3000;
    private const int TranslationTimeoutMs = 5000;

    /// <inheritdoc/>
    public override string Name => "OPUS-MT Transformers";

    /// <inheritdoc/>
    public override string Description => "HuggingFace Transformers基盤の高品質OPUS-MT翻訳エンジン";

    /// <inheritdoc/>
    public override bool RequiresNetwork => false;

    public TransformersOpusMtEngine(ILogger<TransformersOpusMtEngine> logger) : base(logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
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
    public async Task<IList<TranslationResponse>> TranslateBatchAsync(
        IList<TranslationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        if (requests == null || !requests.Any())
        {
            return new List<TranslationResponse>();
        }

        _logger?.LogInformation("🚀 [BATCH] バッチ翻訳開始 - テキスト数: {Count}", requests.Count);

        try
        {
            // 全てのリクエストから翻訳対象テキストを抽出
            var sourceTexts = requests.Select(r => r.SourceText).ToList();
            
            // バッチ翻訳実行
            var batchResult = await TranslateBatchWithPersistentServerAsync(sourceTexts, cancellationToken).ConfigureAwait(false);
            
            if (batchResult?.Success == true && batchResult.Translations != null)
            {
                // バッチ結果を個別のTranslationResponseに変換
                var responses = new List<TranslationResponse>();
                
                for (int i = 0; i < requests.Count; i++)
                {
                    var translation = i < batchResult.Translations.Count ? batchResult.Translations[i] : "[Batch Error]";
                    
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
                
                _logger?.LogInformation("✅ [BATCH] バッチ翻訳成功 - 処理時間: {ProcessingTime:F3}秒", batchResult.ProcessingTime);
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
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [DEBUG] TransformersOpusMtEngine.TranslateInternalAsync 呼び出し - テキスト: '{request.SourceText}'{Environment.NewLine}");
        
        if (!request.SourceLanguage.Equals(Language.Japanese) || 
            !request.TargetLanguage.Equals(Language.English))
        {
            throw new ArgumentException("このエンジンは日英翻訳のみサポートしています");
        }

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

        // ⚡ Phase 0 緊急対応: 3秒タイムアウト実装
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3)); // 3秒でタイムアウト
        
        var startTime = DateTime.Now;
        Console.WriteLine($"⚡ [TIMEOUT] タイムアウト付き翻訳開始 - テキスト: '{request.SourceText}' (制限: 3秒)");
        
        try
        {
            // 常駐サーバーでの翻訳を試行（タイムアウト付き）
            Console.WriteLine($"⚡ [DEBUG] 常駐サーバー翻訳を試行 - テキスト: '{request.SourceText}'");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⚡ [DEBUG] 常駐サーバー翻訳を試行 - テキスト: '{request.SourceText}'{Environment.NewLine}");

            // 🚨 超詳細境界調査 - コンソール出力とファイル出力を分離
            Console.WriteLine($"⚡ [BOUNDARY-1] Console.WriteLine実行完了");
            
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⚡ [BOUNDARY-2] File.AppendAllText実行完了{Environment.NewLine}");
                
            Console.WriteLine($"⚡ [BOUNDARY-3] TranslateWithPersistentServerAsync呼び出し直前");
            
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⚡ [BOUNDARY-4] メソッド呼び出し直前の最終ログ{Environment.NewLine}");

            // 🚨 メソッド呼び出し境界
            var pythonResult = await TranslateWithPersistentServerAsync(request.SourceText, timeoutCts.Token).ConfigureAwait(false);

            Console.WriteLine($"⚡ [DEBUG] TranslateWithPersistentServerAsync呼び出し完了");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⚡ [DEBUG] TranslateWithPersistentServerAsync呼び出し完了{Environment.NewLine}");

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
            var errorResponse = new TranslationResponse
            {
                RequestId = request.RequestId,
                TranslatedText = pythonResult?.Error ?? "常駐サーバー翻訳が失敗しました",
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
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            // ⚡ Phase 0: 3秒タイムアウト時のフォールバック処理
            var timeoutElapsed = DateTime.Now - startTime;
            Console.WriteLine($"⏰ [TIMEOUT] 翻訳タイムアウト - テキスト: '{request.SourceText}', 経過時間: {timeoutElapsed.TotalMilliseconds:F0}ms");
            _logger.LogWarning("翻訳タイムアウト(3秒) - テキスト: '{Text}', 経過時間: {ElapsedMs}ms", 
                request.SourceText, timeoutElapsed.TotalMilliseconds);

            // TODO: 将来的にはキャッシュから取得またはONNX直接推論へフォールバック
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                TranslatedText = $"[TIMEOUT-3s] {request.SourceText}", // 暫定フォールバック
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
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🧹 [SERVER_CLEANUP] 既存Pythonプロセス終了開始{Environment.NewLine}");
            
            await KillExistingServerProcessesAsync().ConfigureAwait(false);
            
            // 既にサーバーが実行中かチェック
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [SERVER_CHECK] 既存サーバープロセス確認中{Environment.NewLine}");
                
                if (await CheckServerHealthAsync().ConfigureAwait(false))
                {
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ [SERVER_EXISTING] 既存サーバー使用{Environment.NewLine}");
                    _logger.LogInformation("常駐サーバーは既に実行中です");
                    return true;
                }
            }
            
            Console.WriteLine($"🚀 [SERVER_DEBUG] 常駐Pythonサーバー起動開始");
            _logger.LogInformation("常駐Pythonサーバーを起動中...");
            
            var processInfo = new ProcessStartInfo
            {
                FileName = _pythonPath,
                Arguments = $"\"{_serverScriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            
            _serverProcess = new Process { StartInfo = processInfo };
            _serverProcess.Start();
            
            Console.WriteLine($"🚀 [SERVER_DEBUG] サーバープロセス起動 - PID: {_serverProcess.Id}");
            _logger.LogInformation("サーバープロセス起動 - PID: {ProcessId}", _serverProcess.Id);
            
            // サーバーが起動するまで待機（最大60秒、モデルロード時間を考慮）
            var startTime = DateTime.Now;
            var maxWaitTime = TimeSpan.FromSeconds(60);
            
            Console.WriteLine($"🔄 [SERVER_DEBUG] サーバー起動待機開始 - 最大{maxWaitTime.TotalSeconds}秒");
            
            while (DateTime.Now - startTime < maxWaitTime)
            {
                await Task.Delay(2000).ConfigureAwait(false); // 待機間隔を2秒に延長
                
                var elapsedTime = DateTime.Now - startTime;
                Console.WriteLine($"⏱️ [SERVER_DEBUG] サーバー接続試行中... 経過時間: {elapsedTime.TotalSeconds:F1}秒");
                
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
    /// サーバーの生存確認
    /// </summary>
    private async Task<bool> CheckServerHealthAsync()
    {
        try
        {
            // 🚨 ログ1: メソッド開始
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [HEALTH_1] CheckServerHealthAsyncメソッド開始{Environment.NewLine}");
            
            Console.WriteLine($"🔍 [HEALTH_CHECK] サーバー接続試行 - {ServerHost}:{ServerPort}");
            
            // 🚨 ログ2: TcpClient作成前
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [HEALTH_2] TcpClient作成前{Environment.NewLine}");
            
            using var client = new TcpClient();
            
            // 🚨 ログ3: ConnectAsync呼び出し前
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [HEALTH_3] ConnectAsync呼び出し前{Environment.NewLine}");
            
            var connectTask = client.ConnectAsync(ServerHost, ServerPort);
            var timeoutTask = Task.Delay(ConnectionTimeoutMs);
            
            // 🚨 ログ4: Task.WhenAny呼び出し前
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [HEALTH_4] Task.WhenAny呼び出し前{Environment.NewLine}");
            
            if (await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false) == timeoutTask)
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⏰ [HEALTH_TIMEOUT] 接続タイムアウト発生{Environment.NewLine}");
                Console.WriteLine($"⏰ [HEALTH_CHECK] 接続タイムアウト（{ConnectionTimeoutMs}ms）");
                return false; // タイムアウト
            }
            
            // 🚨 ログ5: WhenAny完了、接続確認前
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [HEALTH_5] Task.WhenAny完了、接続状態確認中{Environment.NewLine}");
            
            if (!client.Connected)
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ [HEALTH_FAILED] TCP接続失敗{Environment.NewLine}");
                Console.WriteLine($"❌ [HEALTH_CHECK] 接続失敗 - client.Connected = false");
                return false;
            }
            
            // 🚨 ログ6: TCP接続成功、ストリーム取得前
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [HEALTH_6] TCP接続成功、ストリーム取得前{Environment.NewLine}");
            
            Console.WriteLine($"🔗 [HEALTH_CHECK] TCP接続成功 - PING送信中");
            
            var stream = client.GetStream();
            var pingRequest = Encoding.UTF8.GetBytes("PING\n");
            
            // 🚨 ログ7: WriteAsync呼び出し前
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [HEALTH_7] WriteAsync呼び出し前{Environment.NewLine}");
            
            await stream.WriteAsync(pingRequest, 0, pingRequest.Length).ConfigureAwait(false);
            
            // 🚨 ログ8: WriteAsync完了、ReadAsync準備前
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [HEALTH_8] WriteAsync完了、ReadAsync準備中{Environment.NewLine}");
            
            // ⚡ CRITICAL FIX: ReadAsyncにタイムアウトを追加
            var buffer = new byte[1024];
            using var readTimeout = new CancellationTokenSource(ConnectionTimeoutMs);
            
            // 🚨 ログ9: ReadAsync呼び出し前 - ⚠️ 最も疑わしい箇所
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨🚨🚨 [HEALTH_9] ReadAsync呼び出し前 - HANG発生箇所の可能性大 🚨🚨🚨{Environment.NewLine}");
            
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, readTimeout.Token).ConfigureAwait(false);
            
            // 🚨 ログ10: ReadAsync完了
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ [HEALTH_10] ReadAsync完了 - bytesRead={bytesRead}{Environment.NewLine}");
            
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            // 🔍 レスポンス内容の詳細ログ
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📨 [HEALTH_RESPONSE] 受信内容({bytesRead}バイト): '{response}'{Environment.NewLine}");
            
            // 🔍 レスポンス内容をバイト単位で確認
            var responseBytes = Encoding.UTF8.GetBytes(response);
            var hexString = Convert.ToHexString(responseBytes);
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [HEALTH_HEX] バイト表現: {hexString}{Environment.NewLine}");
            
            Console.WriteLine($"📨 [HEALTH_CHECK] サーバーレスポンス: '{response.Trim()}'");
            
            var isAlive = response.Contains("\"status\": \"alive\"") || response.Contains("\"status\":\"alive\"");
            
            // 🔍 判定処理の詳細ログ
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [HEALTH_CHECK] Contains('\"status\":\"alive\"'): {isAlive}{Environment.NewLine}");
            
            Console.WriteLine($"💓 [HEALTH_CHECK] サーバー状態: {(isAlive ? "生存" : "異常")}");
            
            // 🚨 ログ11: メソッド正常終了
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ [HEALTH_11] CheckServerHealthAsync正常終了 - isAlive={isAlive}{Environment.NewLine}");
            
            return isAlive;
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 [HEALTH_EXCEPTION] ヘルスチェック例外: {ex.Message}{Environment.NewLine}");
            Console.WriteLine($"💥 [HEALTH_CHECK] ヘルスチェック例外: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// バッチ翻訳用常駐サーバー通信
    /// </summary>
    private async Task<BatchTranslationResult?> TranslateBatchWithPersistentServerAsync(
        IList<string> texts, 
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("📦 [BATCH_SERVER] バッチ翻訳サーバー通信開始 - テキスト数: {Count}", texts.Count);
        var startTime = DateTime.Now;

        try
        {
            // キャンセレーション確認
            cancellationToken.ThrowIfCancellationRequested();

            // サーバーの健全性確認
            if (!await CheckServerHealthAsync().ConfigureAwait(false))
            {
                _logger?.LogWarning("サーバーに接続できません。再起動を試行します");
                
                if (!await StartPersistentServerAsync().ConfigureAwait(false))
                {
                    return new BatchTranslationResult { Success = false, Error = "サーバー接続に失敗しました" };
                }
            }
            
            using var client = new TcpClient();
            await client.ConnectAsync(ServerHost, ServerPort, cancellationToken).ConfigureAwait(false);
            
            // キャンセレーション再確認
            cancellationToken.ThrowIfCancellationRequested();
            
            var stream = client.GetStream();
            
            // バッチリクエスト送信
            var request = new { batch_texts = texts };
            var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
            var requestBytes = Encoding.UTF8.GetBytes(requestJson);
            
            _logger?.LogInformation("📤 [BATCH_SERVER] バッチリクエスト送信 - サイズ: {Size} bytes", requestBytes.Length);
            
            await stream.WriteAsync(requestBytes, 0, requestBytes.Length).ConfigureAwait(false);
            
            // バッチレスポンス受信（長めのタイムアウト）
            var batchTimeout = Math.Max(TranslationTimeoutMs, texts.Count * 1000); // テキスト数に応じて動的調整
            using var cts = new CancellationTokenSource(batchTimeout);
            var buffer = new byte[8192]; // バッファサイズを増加
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false);
            
            _logger?.LogInformation("📨 [BATCH_SERVER] バッチレスポンス受信 - サイズ: {Size} bytes", bytesRead);
            
            var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            var response = JsonSerializer.Deserialize<BatchTranslationResult>(responseJson);
            
            var processingTime = DateTime.Now - startTime;
            _logger?.LogInformation("✅ [BATCH_SERVER] バッチ翻訳完了 - 処理時間: {ProcessingTime:F3}秒", processingTime.TotalSeconds);
            
            if (response != null)
            {
                response.ProcessingTime = processingTime.TotalSeconds;
            }
            
            return response;
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
    private async Task<PersistentTranslationResult?> TranslateWithPersistentServerAsync(string text, CancellationToken cancellationToken = default)
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
                    var batchResult = await TranslateBatchWithPersistentServerAsync(textLines, cancellationToken).ConfigureAwait(false);
                    
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
            
            using var client = new TcpClient();
            await client.ConnectAsync(ServerHost, ServerPort, cancellationToken).ConfigureAwait(false);
            
            // キャンセレーション再確認
            cancellationToken.ThrowIfCancellationRequested();
            
            var stream = client.GetStream();
            
            // 翻訳リクエスト送信
            var request = new { text = text };
            var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
            var requestBytes = Encoding.UTF8.GetBytes(requestJson);
            
            await stream.WriteAsync(requestBytes, 0, requestBytes.Length).ConfigureAwait(false);
            
            // レスポンス受信（タイムアウト付き）
            using var cts = new CancellationTokenSource(TranslationTimeoutMs);
            var buffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false);
            
            var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            Console.WriteLine($"📨 [SERVER_TRANSLATE] レスポンス内容: {responseJson}");
            
            var response = JsonSerializer.Deserialize<PersistentTranslationResult>(responseJson);
            
            var processingTime = DateTime.Now - startTime;
            Console.WriteLine($"⚡ [SERVER_TRANSLATE] 翻訳完了 - 処理時間: {processingTime.TotalSeconds:F3}秒, 翻訳: '{response?.Translation}'");
            _logger.LogInformation("常駐サーバー翻訳完了 - 処理時間: {ProcessingTimeSeconds}秒", processingTime.TotalSeconds);
            
            return response;
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.Now - startTime;
            Console.WriteLine($"💥 [SERVER_TRANSLATE] 翻訳エラー: {ex.Message} - 処理時間: {processingTime.TotalSeconds:F3}秒");
            _logger.LogError(ex, "常駐サーバー翻訳中にエラーが発生しました");
            return new PersistentTranslationResult { Success = false, Error = ex.Message };
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
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] Process.Start()直前{Environment.NewLine}");
            
            process.Start();
            
            Console.WriteLine($"🐍 [PYTHON_DEBUG] Process.Start()完了 - PID: {process.Id}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] Process.Start()完了 - PID: {process.Id}{Environment.NewLine}");

            // タイムアウト制御 (初回モデルロードのため300秒=5分でタイムアウト)
            Console.WriteLine($"🐍 [PYTHON_DEBUG] 非同期タスク作成開始");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] 非同期タスク作成開始{Environment.NewLine}");
            
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var processTask = process.WaitForExitAsync();
            
            Console.WriteLine($"🐍 [PYTHON_DEBUG] 非同期タスク作成完了");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] 非同期タスク作成完了{Environment.NewLine}");

            var timeout = TimeSpan.FromSeconds(15); // 15秒に短縮（緊急修正）
            using var cts = new CancellationTokenSource(timeout);

            try
            {
                Console.WriteLine($"🔄 [PYTHON_DEBUG] Python処理実行中... (最大15秒待機)");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔄 [PYTHON_DEBUG] Python処理実行中... (最大15秒待機){Environment.NewLine}");
                
                var startTime = DateTime.Now;
                
                // 10秒ごとに進行状況を表示
                var progressTask = Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(10000, cts.Token).ConfigureAwait(false);
                        var elapsed = DateTime.Now - startTime;
                        Console.WriteLine($"⏱️ [PROGRESS] 処理継続中... 経過時間: {elapsed.TotalSeconds:F0}秒");
                        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⏱️ [PROGRESS] 処理継続中... 経過時間: {elapsed.TotalSeconds:F0}秒{Environment.NewLine}");
                        if (elapsed.TotalSeconds > 15) break;
                    }
                }, cts.Token);
                
                Console.WriteLine($"🐍 [PYTHON_DEBUG] processTask.WaitAsync()呼び出し直前");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] processTask.WaitAsync()呼び出し直前{Environment.NewLine}");
                
                await processTask.WaitAsync(cts.Token).ConfigureAwait(false);
                
                Console.WriteLine($"🐍 [PYTHON_DEBUG] processTask.WaitAsync()完了");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] processTask.WaitAsync()完了{Environment.NewLine}");
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
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] Pythonプロセス終了 - ExitCode: {process.ExitCode}{Environment.NewLine}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] Output: '{output}'{Environment.NewLine}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🐍 [PYTHON_DEBUG] Error: '{error}'{Environment.NewLine}");
                _logger.LogInformation("Pythonプロセス終了 - ExitCode: {ExitCode}, Output: {Output}, Error: {Error}", 
                    process.ExitCode, output, error);

                if (process.ExitCode != 0)
                {
                    _logger.LogError("Python翻訳プロセスがエラーで終了しました: {Error}", error);
                    return null;
                }

                Console.WriteLine($"🔍 [TRANSLATE_DEBUG] ParseResult呼び出し開始");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [TRANSLATE_DEBUG] ParseResult呼び出し開始{Environment.NewLine}");
                var result = ParseResult(output);
                Console.WriteLine($"🔍 [TRANSLATE_DEBUG] ParseResult呼び出し完了 - Result: {result?.Success}, Translation: '{result?.Translation}'");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [TRANSLATE_DEBUG] ParseResult呼び出し完了 - Result: {result?.Success}, Translation: '{result?.Translation}'{Environment.NewLine}");
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
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 [JSON_DEBUG] JSON解析失敗: {ex.Message}{Environment.NewLine}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 [JSON_DEBUG] 問題のある出力: '{output}'{Environment.NewLine}");
            _logger.LogError(ex, "Python出力のJSONパースに失敗しました: {Output}", output);
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 [JSON_DEBUG] 予期しないエラー: {ex.GetType().Name} - {ex.Message}");
            Console.WriteLine($"💥 [JSON_DEBUG] スタックトレース: {ex.StackTrace}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 [JSON_DEBUG] 予期しないエラー: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 [JSON_DEBUG] スタックトレース: {ex.StackTrace}{Environment.NewLine}");
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
        return new[]
        {
            new LanguagePair { SourceLanguage = Language.Japanese, TargetLanguage = Language.English }
        };
    }

    /// <inheritdoc/>
    public override async Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
    {
        return languagePair.SourceLanguage.Equals(Language.Japanese) && 
               languagePair.TargetLanguage.Equals(Language.English);
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
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🧹 [CLEANUP_START] Pythonプロセス終了処理開始{Environment.NewLine}");
            
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
            
            // 2秒待機してプロセス終了を確実にする
            await Task.Delay(2000).ConfigureAwait(false);
            
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ [CLEANUP_COMPLETE] Pythonプロセス終了処理完了{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ [CLEANUP_ERROR] Pythonプロセス終了エラー: {ex.Message}{Environment.NewLine}");
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