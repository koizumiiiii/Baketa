using System;
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

            var pythonResult = await TranslateWithPersistentServerAsync(request.SourceText, timeoutCts.Token).ConfigureAwait(false);

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
            
            // 既にサーバーが実行中かチェック
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                if (await CheckServerHealthAsync().ConfigureAwait(false))
                {
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
            Console.WriteLine($"🔍 [HEALTH_CHECK] サーバー接続試行 - {ServerHost}:{ServerPort}");
            
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ServerHost, ServerPort);
            var timeoutTask = Task.Delay(ConnectionTimeoutMs);
            
            if (await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false) == timeoutTask)
            {
                Console.WriteLine($"⏰ [HEALTH_CHECK] 接続タイムアウト（{ConnectionTimeoutMs}ms）");
                return false; // タイムアウト
            }
            
            if (!client.Connected)
            {
                Console.WriteLine($"❌ [HEALTH_CHECK] 接続失敗 - client.Connected = false");
                return false;
            }
            
            Console.WriteLine($"🔗 [HEALTH_CHECK] TCP接続成功 - PING送信中");
            
            var stream = client.GetStream();
            var pingRequest = Encoding.UTF8.GetBytes("PING\n");
            await stream.WriteAsync(pingRequest, 0, pingRequest.Length).ConfigureAwait(false);
            
            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            Console.WriteLine($"📨 [HEALTH_CHECK] サーバーレスポンス: '{response.Trim()}'");
            
            var isAlive = response.Contains("\"status\":\"alive\"");
            Console.WriteLine($"💓 [HEALTH_CHECK] サーバー状態: {(isAlive ? "生存" : "異常")}");
            
            return isAlive;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 [HEALTH_CHECK] ヘルスチェック例外: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 常駐サーバーを使った高速翻訳
    /// </summary>
    private async Task<PersistentTranslationResult?> TranslateWithPersistentServerAsync(string text, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"⚡ [SERVER_TRANSLATE] 常駐サーバー翻訳開始: '{text}'");
        _logger.LogInformation("常駐サーバーで翻訳開始: '{Text}'", text);
        
        var startTime = DateTime.Now;
        
        try
        {
            // キャンセレーション確認
            cancellationToken.ThrowIfCancellationRequested();
            
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