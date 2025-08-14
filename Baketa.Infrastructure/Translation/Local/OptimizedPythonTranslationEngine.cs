using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Common;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local;

/// <summary>
/// 最適化された高速Python翻訳エンジン（目標: 500ms以下）
/// </summary>
public class OptimizedPythonTranslationEngine : ITranslationEngine
{
    private readonly ILogger<OptimizedPythonTranslationEngine> _logger;
    private readonly SemaphoreSlim _serverLock = new(1, 1);
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    
    // 永続化サーバー管理
    private Process? _serverProcess;
    private TcpClient? _persistentClient;
    private NetworkStream? _persistentStream;
    private StreamReader? _persistentReader;
    private StreamWriter? _persistentWriter;
    
    // パフォーマンス監視
    private readonly ConcurrentDictionary<string, TranslationMetrics> _metricsCache = new();
    private long _totalRequests;
    private long _totalProcessingTimeMs;
    private readonly Stopwatch _uptimeStopwatch = new();
    
    // 設定
    private const string ServerHost = "127.0.0.1";
    private const int ServerPort = 5556; // ポート番号を5556に変更
    private const int ConnectionTimeoutMs = 5000;
    private const int StartupTimeoutMs = 30000; // 起動タイムアウトを30秒に短縮
    private const int HealthCheckIntervalMs = 30000; // ヘルスチェック間隔
    
    // Python実行パス
    private readonly string _pythonPath;
    private readonly string _serverScriptPath;
    
    public string Name => "OptimizedPythonTranslation";
    public string Description => "高速化されたPython翻訳エンジン（500ms目標）";
    public bool RequiresNetwork => false;

    public OptimizedPythonTranslationEngine(ILogger<OptimizedPythonTranslationEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Python実行環境設定（py launcherを使用）
        _pythonPath = "py";
        
        // サーバースクリプトパス設定
        var currentDir = Directory.GetCurrentDirectory();
        var projectRoot = FindProjectRoot(currentDir);
        _serverScriptPath = Path.Combine(projectRoot, "scripts", "optimized_translation_server.py");
        
        _logger.LogInformation("OptimizedPythonTranslationEngine初期化 - Python: {PythonPath}, Script: {ScriptPath}", 
            _pythonPath, _serverScriptPath);
        
        // バックグラウンドで初期化開始（ブロックしない）
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000); // 起動を少し遅延
                await InitializeAsync().ConfigureAwait(false);
                _logger.LogInformation("バックグラウンド初期化完了");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "バックグラウンド初期化失敗");
            }
        });
        
        _uptimeStopwatch.Start();
    }

    public async Task<bool> InitializeAsync()
    {
        try
        {
            _logger.LogInformation("永続化Pythonサーバー起動開始");
            
            // 既存サーバープロセスをクリーンアップ
            await CleanupExistingProcessesAsync().ConfigureAwait(false);
            
            // サーバー起動
            if (!await StartOptimizedServerAsync().ConfigureAwait(false))
            {
                _logger.LogError("サーバー起動失敗");
                return false;
            }
            
            // 接続確立
            if (!await EstablishPersistentConnectionAsync().ConfigureAwait(false))
            {
                _logger.LogError("永続接続確立失敗");
                return false;
            }
            
            // ヘルスチェックタスク開始
            _ = Task.Run(async () => await MonitorServerHealthAsync().ConfigureAwait(false));
            
            _logger.LogInformation("OptimizedPythonTranslationEngine初期化完了");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初期化エラー");
            return false;
        }
    }

    private async Task<bool> StartOptimizedServerAsync()
    {
        try
        {
            await _serverLock.WaitAsync().ConfigureAwait(false);
            
            // 直接Python実行（PowerShell経由を排除）
            var processInfo = new ProcessStartInfo
            {
                FileName = _pythonPath,
                Arguments = $"\"{_serverScriptPath}\" --port {ServerPort} --optimized",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            
            _serverProcess = new Process { StartInfo = processInfo };
            _serverProcess.Start();
            
            _logger.LogInformation("Pythonサーバープロセス起動 - PID: {ProcessId}", _serverProcess.Id);
            
            // 非同期でログ監視
            _ = Task.Run(async () => await MonitorServerOutputAsync().ConfigureAwait(false));
            
            // サーバー起動待機（最大30秒）
            var startTime = DateTime.UtcNow;
            while ((DateTime.UtcNow - startTime).TotalMilliseconds < StartupTimeoutMs)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                
                try
                {
                    if (_serverProcess.HasExited)
                    {
                        _logger.LogError("サーバープロセスが異常終了 - ExitCode: {ExitCode}", _serverProcess.ExitCode);
                        return false;
                    }
                }
                catch (InvalidOperationException)
                {
                    _logger.LogError("サーバープロセスが無効な状態");
                    return false;
                }
                
                // TCP接続テスト
                if (await TestConnectionAsync().ConfigureAwait(false))
                {
                    var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    _logger.LogInformation("サーバー起動成功 - 起動時間: {ElapsedMs}ms", elapsedMs);
                    return true;
                }
            }
            
            _logger.LogError("サーバー起動タイムアウト");
            return false;
        }
        finally
        {
            _serverLock.Release();
        }
    }

    private async Task<bool> EstablishPersistentConnectionAsync()
    {
        try
        {
            await _connectionLock.WaitAsync().ConfigureAwait(false);
            
            // 既存接続をクローズ
            DisposePersistentConnection();
            
            _persistentClient = new TcpClient();
            _persistentClient.NoDelay = true; // Nagleアルゴリズム無効化
            _persistentClient.ReceiveTimeout = 30000;
            _persistentClient.SendTimeout = 30000;
            
            await _persistentClient.ConnectAsync(ServerHost, ServerPort).ConfigureAwait(false);
            
            _persistentStream = _persistentClient.GetStream();
            _persistentReader = new StreamReader(_persistentStream, Encoding.UTF8, false, 4096, true);
            _persistentWriter = new StreamWriter(_persistentStream, Encoding.UTF8, 4096, true)
            {
                AutoFlush = true
            };
            
            // Keep-Alive設定
            ConfigureKeepAlive(_persistentClient);
            
            _logger.LogInformation("永続接続確立成功");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "永続接続確立失敗");
            return false;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // 初期化確認（テスト環境では迅速に失敗）
            if (!await IsReadyAsync().ConfigureAwait(false))
            {
                // テスト環境やサーバーなし環境では初期化を試行しない
                if (!File.Exists(_serverScriptPath))
                {
                    _logger.LogWarning("サーバースクリプトが見つかりません: {ScriptPath}", _serverScriptPath);
                    return new TranslationResponse
                    {
                        RequestId = request.RequestId,
                        TranslatedText = "翻訳エラーが発生しました",
                        SourceText = request.SourceText,
                        SourceLanguage = request.SourceLanguage,
                        TargetLanguage = request.TargetLanguage,
                        ConfidenceScore = 0.0f,
                        EngineName = Name,
                        IsSuccess = false
                    };
                }
                
                var initResult = await InitializeAsync().ConfigureAwait(false);
                if (!initResult)
                {
                    return new TranslationResponse
                    {
                        RequestId = request.RequestId,
                        TranslatedText = "翻訳エラーが発生しました",
                        SourceText = request.SourceText,
                        SourceLanguage = request.SourceLanguage,
                        TargetLanguage = request.TargetLanguage,
                        ConfidenceScore = 0.0f,
                        EngineName = Name,
                        IsSuccess = false
                    };
                }
            }

            // 言語ペアのサポート確認
            var languagePair = new LanguagePair 
            { 
                SourceLanguage = request.SourceLanguage, 
                TargetLanguage = request.TargetLanguage 
            };
            bool isSupported = await SupportsLanguagePairAsync(languagePair).ConfigureAwait(false);
            if (!isSupported)
            {
                return new TranslationResponse
                {
                    RequestId = request.RequestId,
                    TranslatedText = $"言語ペア {request.SourceLanguage.Code}-{request.TargetLanguage.Code} はサポートされていません",
                    SourceText = request.SourceText,
                    SourceLanguage = request.SourceLanguage,
                    TargetLanguage = request.TargetLanguage,
                    ConfidenceScore = 0.0f,
                    EngineName = Name,
                    IsSuccess = false
                };
            }
            
            // 高速パス: キャッシュチェック
            var cacheKey = GenerateCacheKey(request);
            if (_metricsCache.TryGetValue(cacheKey, out var cached))
            {
                stopwatch.Stop();
                _logger.LogDebug("キャッシュヒット - 処理時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
                
                return new TranslationResponse
                {
                    RequestId = request.RequestId,
                    TranslatedText = cached.TranslatedText,
                    SourceText = request.SourceText,
                    SourceLanguage = request.SourceLanguage,
                    TargetLanguage = request.TargetLanguage,
                    ConfidenceScore = cached.ConfidenceScore,
                    EngineName = Name,
                    IsSuccess = true,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
            
            // 永続接続で翻訳実行
            var result = await TranslateWithOptimizedServerAsync(request, cancellationToken).ConfigureAwait(false);
            
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            
            // 処理時間を設定
            result.ProcessingTimeMs = elapsedMs;
            
            // メトリクス更新
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Add(ref _totalProcessingTimeMs, elapsedMs);
            
            // 500ms目標チェック
            if (elapsedMs > 500)
            {
                _logger.LogWarning("処理時間が目標を超過: {ElapsedMs}ms > 500ms", elapsedMs);
            }
            else
            {
                _logger.LogInformation("高速翻訳成功: {ElapsedMs}ms", elapsedMs);
            }
            
            // キャッシュ保存
            if (result.IsSuccess)
            {
                _metricsCache.TryAdd(cacheKey, new TranslationMetrics
                {
                    TranslatedText = result.TranslatedText,
                    ConfidenceScore = result.ConfidenceScore,
                    ProcessingTimeMs = elapsedMs,
                    Timestamp = DateTime.UtcNow
                });
            }
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "翻訳エラー - 処理時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                TranslatedText = "翻訳エラーが発生しました",
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                ConfidenceScore = 0.0f,
                EngineName = Name,
                IsSuccess = false,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    public virtual async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
            
        if (requests.Count == 0)
            return [];
            
        // バッチ処理をサポートするカスタム実装がある場合は、サブクラスで上書きしてください
        // このデフォルト実装は個別のリクエストを順次処理します
        
        var results = new List<TranslationResponse>();
        
        foreach (var request in requests)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            var response = await TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            results.Add(response);
        }
        
        return results;
    }

    public virtual async Task<bool> IsReadyAsync()
    {
        if (_disposed)
            return false;
            
        // サーバープロセスの確認
        if (_serverProcess == null)
            return false;
            
        try
        {
            if (_serverProcess.HasExited)
                return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
            
        // 接続テスト
        return await TestConnectionAsync().ConfigureAwait(false);
    }

    private async Task<TranslationResponse> TranslateWithOptimizedServerAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var connectionLockStopwatch = Stopwatch.StartNew();
        
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        connectionLockStopwatch.Stop();
        _logger.LogInformation("[TIMING] 接続ロック取得: {ElapsedMs}ms", connectionLockStopwatch.ElapsedMilliseconds);
        
        try
        {
            var connectionCheckStopwatch = Stopwatch.StartNew();
            // 接続確認と再接続
            if (_persistentWriter == null || _persistentReader == null || 
                _persistentClient == null || !_persistentClient.Connected)
            {
                _logger.LogInformation("永続接続が無効 - 再接続を試行");
                if (!await EstablishPersistentConnectionAsync().ConfigureAwait(false))
                {
                    throw new InvalidOperationException("永続接続の確立に失敗しました");
                }
            }
            connectionCheckStopwatch.Stop();
            _logger.LogInformation("[TIMING] 接続確認・再接続: {ElapsedMs}ms", connectionCheckStopwatch.ElapsedMilliseconds);
            
            var serializationStopwatch = Stopwatch.StartNew();
            // リクエスト送信
            var requestData = new
            {
                text = request.SourceText,
                source_lang = request.SourceLanguage.Code,
                target_lang = request.TargetLanguage.Code,
                request_id = request.RequestId
            };
            
            var jsonRequest = JsonSerializer.Serialize(requestData);
            serializationStopwatch.Stop();
            _logger.LogInformation("[TIMING] JSONシリアライゼーション: {ElapsedMs}ms", serializationStopwatch.ElapsedMilliseconds);
            
            var networkSendStopwatch = Stopwatch.StartNew();
            await _persistentWriter!.WriteLineAsync(jsonRequest).ConfigureAwait(false);
            networkSendStopwatch.Stop();
            _logger.LogInformation("[TIMING] ネットワーク送信: {ElapsedMs}ms", networkSendStopwatch.ElapsedMilliseconds);
            
            var networkReceiveStopwatch = Stopwatch.StartNew();
            // レスポンス受信
            var jsonResponse = await _persistentReader!.ReadLineAsync().ConfigureAwait(false);
            networkReceiveStopwatch.Stop();
            _logger.LogInformation("[TIMING] ネットワーク受信（Python処理含む）: {ElapsedMs}ms", networkReceiveStopwatch.ElapsedMilliseconds);
            
            if (string.IsNullOrEmpty(jsonResponse))
            {
                throw new InvalidOperationException("サーバーから空のレスポンスを受信しました");
            }
            
            var deserializationStopwatch = Stopwatch.StartNew();
            var response = JsonSerializer.Deserialize<PythonTranslationResponse>(jsonResponse);
            deserializationStopwatch.Stop();
            _logger.LogInformation("[TIMING] JSONデシリアライゼーション: {ElapsedMs}ms", deserializationStopwatch.ElapsedMilliseconds);
            
            if (response == null)
            {
                throw new InvalidOperationException("レスポンスのデシリアライズに失敗しました");
            }
            
            var resultCreationStopwatch = Stopwatch.StartNew();
            
            // エラー時の適切なハンドリング
            string translatedText;
            float confidenceScore;
            bool isSuccess;
            
            if (response.success && !string.IsNullOrEmpty(response.translation))
            {
                translatedText = response.translation;
                confidenceScore = response.confidence ?? 0.95f;
                isSuccess = true;
            }
            else
            {
                translatedText = "翻訳エラーが発生しました";
                confidenceScore = 0.0f;
                isSuccess = false;
            }
            
            var result = new TranslationResponse
            {
                RequestId = request.RequestId,
                TranslatedText = translatedText,
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                ConfidenceScore = confidenceScore,
                EngineName = Name,
                IsSuccess = isSuccess
            };
            resultCreationStopwatch.Stop();
            _logger.LogInformation("[TIMING] レスポンス生成: {ElapsedMs}ms", resultCreationStopwatch.ElapsedMilliseconds);
            
            totalStopwatch.Stop();
            _logger.LogInformation("[TIMING] 合計処理時間（C#側）: {ElapsedMs}ms", totalStopwatch.ElapsedMilliseconds);
            _logger.LogInformation("[TIMING] Python側処理時間: {PythonTimeMs}ms", (response.processing_time ?? 0) * 1000);
            
            // 詳細ログ出力
            _logger.LogInformation("翻訳結果詳細 - IsSuccess: {IsSuccess}, Text: '{Text}', Length: {Length}", 
                result.IsSuccess, result.TranslatedText, result.TranslatedText?.Length ?? 0);
                
            return result;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task MonitorServerHealthAsync()
    {
        while (!_disposed)
        {
            try
            {
                await Task.Delay(HealthCheckIntervalMs).ConfigureAwait(false);
                
                if (!await TestConnectionAsync().ConfigureAwait(false))
                {
                    _logger.LogWarning("ヘルスチェック失敗 - 再接続を試行");
                    await EstablishPersistentConnectionAsync().ConfigureAwait(false);
                }
                
                // メトリクスログ
                if (_totalRequests > 0)
                {
                    var avgMs = _totalProcessingTimeMs / _totalRequests;
                    _logger.LogInformation("パフォーマンス統計 - 平均処理時間: {AvgMs}ms, 総リクエスト: {TotalRequests}", 
                        avgMs, _totalRequests);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ヘルスチェックエラー");
            }
        }
    }

    private async Task MonitorServerOutputAsync()
    {
        if (_serverProcess == null) return;
        
        try
        {
            while (true)
            {
                try
                {
                    if (_serverProcess.HasExited)
                        break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }
                
                var line = await _serverProcess.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (!string.IsNullOrEmpty(line))
                {
                    _logger.LogDebug("[PYTHON] {Output}", line);
                }
                else
                {
                    break; // EOF or process ended
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "サーバー出力監視エラー");
        }
    }

    private async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var testClient = new TcpClient();
            var connectTask = testClient.ConnectAsync(ServerHost, ServerPort);
            var timeoutTask = Task.Delay(ConnectionTimeoutMs);
            
            var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
            
            if (completedTask == connectTask && testClient.Connected)
            {
                testClient.Close();
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void ConfigureKeepAlive(TcpClient client)
    {
        try
        {
            client.Client.SetSocketOption(
                System.Net.Sockets.SocketOptionLevel.Socket,
                System.Net.Sockets.SocketOptionName.KeepAlive,
                true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keep-Alive設定失敗");
        }
    }

    private void DisposePersistentConnection()
    {
        try
        {
            _persistentWriter?.Dispose();
            _persistentReader?.Dispose();
            _persistentStream?.Dispose();
            _persistentClient?.Dispose();
            
            _persistentWriter = null;
            _persistentReader = null;
            _persistentStream = null;
            _persistentClient = null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "永続接続のクリーンアップ中にエラー");
        }
    }

    private async Task CleanupExistingProcessesAsync()
    {
        try
        {
            var processes = Process.GetProcessesByName("python");
            foreach (var process in processes)
            {
                try
                {
                    var cmdLine = process.MainModule?.FileName;
                    if (cmdLine?.Contains("optimized_translation_server") == true)
                    {
                        process.Kill();
                        await Task.Delay(100).ConfigureAwait(false);
                        _logger.LogInformation("既存Pythonサーバープロセスを終了: PID {ProcessId}", process.Id);
                    }
                }
                catch
                {
                    // 個別プロセスのエラーは無視
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "既存プロセスのクリーンアップ中にエラー");
        }
    }

    private string GenerateCacheKey(TranslationRequest request)
    {
        return $"{request.SourceLanguage.Code}_{request.TargetLanguage.Code}_{request.SourceText.GetHashCode()}";
    }

    private string FindProjectRoot(string currentDir)
    {
        var dir = new DirectoryInfo(currentDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Baketa.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? currentDir;
    }

    public async Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        return await Task.FromResult<IReadOnlyCollection<LanguagePair>>(
        [
            new() { SourceLanguage = new() { Code = "ja", DisplayName = "Japanese" }, 
                   TargetLanguage = new() { Code = "en", DisplayName = "English" } },
            new() { SourceLanguage = new() { Code = "en", DisplayName = "English" }, 
                   TargetLanguage = new() { Code = "ja", DisplayName = "Japanese" } }
        ]).ConfigureAwait(false);
    }

    public async Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
    {
        var supportedPairs = await GetSupportedLanguagePairsAsync().ConfigureAwait(false);
        return supportedPairs.Any(p => 
            p.SourceLanguage.Code == languagePair.SourceLanguage.Code &&
            p.TargetLanguage.Code == languagePair.TargetLanguage.Code);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            
            DisposePersistentConnection();
            
            if (_serverProcess != null)
            {
                try
                {
                    // Processの状態を安全に確認
                    if (!_serverProcess.HasExited)
                    {
                        _serverProcess.Kill();
                        _serverProcess.WaitForExit(5000);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "サーバープロセス終了中にエラー");
                }
                finally
                {
                    _serverProcess?.Dispose();
                    _serverProcess = null;
                }
            }
            
            _serverLock?.Dispose();
            _connectionLock?.Dispose();
            
            _logger.LogInformation("OptimizedPythonTranslationEngineが破棄されました");
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private bool _disposed;

    // 内部クラス
    private class TranslationMetrics
    {
        public string TranslatedText { get; set; } = string.Empty;
        public float ConfidenceScore { get; set; }
        public long ProcessingTimeMs { get; set; }
        public DateTime Timestamp { get; set; }
    }

    private class PythonTranslationResponse
    {
        public bool success { get; set; }
        public string? translation { get; set; }
        public float? confidence { get; set; }
        public string? error { get; set; }
        public double? processing_time { get; set; }
    }
}