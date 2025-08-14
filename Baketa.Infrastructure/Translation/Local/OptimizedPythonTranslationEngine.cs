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
using Baketa.Core.Settings;
using Baketa.Infrastructure.Translation.Local.ConnectionPool;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Translation.Local;

/// <summary>
/// 最適化された高速Python翻訳エンジン（目標: 500ms以下）
/// Issue #147: 接続プール統合により接続ロック競合を解決
/// </summary>
public class OptimizedPythonTranslationEngine : ITranslationEngine
{
    private readonly ILogger<OptimizedPythonTranslationEngine> _logger;
    private readonly SemaphoreSlim _serverLock = new(1, 1);
    private readonly FixedSizeConnectionPool _connectionPool; // Issue #147: 接続プール統合
    private readonly TranslationSettings _translationSettings; // Issue #147: 設定管理
    
    // サーバープロセス管理（接続は接続プールが管理）
    private Process? _serverProcess;
    
    // パフォーマンス監視
    private readonly ConcurrentDictionary<string, TranslationMetrics> _metricsCache = new();
    private long _totalRequests;
    private long _totalProcessingTimeMs;
    private readonly Stopwatch _uptimeStopwatch = new();
    
    // 設定
    private const string ServerHost = "127.0.0.1";
    private const int ServerPort = 5555; // ポート番号を5555に統一（既存サーバーと一致）
    private const int ConnectionTimeoutMs = 5000;
    private const int StartupTimeoutMs = 30000; // 起動タイムアウトを30秒に短縮
    private const int HealthCheckIntervalMs = 30000; // ヘルスチェック間隔
    
    // Python実行パス
    private readonly string _pythonPath;
    private readonly string _serverScriptPath;
    
    public string Name => "OptimizedPythonTranslation";
    public string Description => "高速化されたPython翻訳エンジン（500ms目標）";
    public bool RequiresNetwork => false;

    public OptimizedPythonTranslationEngine(
        ILogger<OptimizedPythonTranslationEngine> logger,
        FixedSizeConnectionPool connectionPool,
        IOptions<TranslationSettings> translationSettings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _translationSettings = translationSettings?.Value ?? throw new ArgumentNullException(nameof(translationSettings));
        
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
            // Issue #147: 外部サーバー使用設定の確認
            if (_translationSettings.UseExternalServer)
            {
                _logger.LogInformation("外部Pythonサーバー使用モード - プロセス起動をスキップ");
            }
            else
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
            }
            
            // Issue #147: 接続プールによるサーバー接続確認
            try
            {
                using var testCts = new CancellationTokenSource(5000); // 5秒タイムアウト
                var testConnection = await _connectionPool.AcquireConnectionAsync(testCts.Token).ConfigureAwait(false);
                await _connectionPool.ReleaseConnectionAsync(testConnection).ConfigureAwait(false);
                _logger.LogInformation("接続プール経由でサーバー接続を確認");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "接続プール経由のサーバー接続確認失敗");
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
                
                // Issue #147: 接続プールによる接続テスト
                try
                {
                    using var testCts = new CancellationTokenSource(3000);
                    var testConnection = await _connectionPool.AcquireConnectionAsync(testCts.Token).ConfigureAwait(false);
                    await _connectionPool.ReleaseConnectionAsync(testConnection).ConfigureAwait(false);
                    
                    var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    _logger.LogInformation("サーバー起動成功 - 起動時間: {ElapsedMs}ms", elapsedMs);
                    return true;
                }
                catch
                {
                    // 接続テスト失敗 - サーバーがまだ起動していない
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

    // Issue #147: EstablishPersistentConnectionAsyncメソッドは接続プール統合により削除
    // 接続管理は FixedSizeConnectionPool が担当

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
        var connectionAcquireStopwatch = Stopwatch.StartNew();
        
        // Issue #147: 接続プールから接続を取得（接続ロック競合を解決）
        PersistentConnection? connection = null;
        try
        {
            connection = await _connectionPool.AcquireConnectionAsync(cancellationToken).ConfigureAwait(false);
            connectionAcquireStopwatch.Stop();
            _logger.LogInformation("[TIMING] 接続プール取得: {ElapsedMs}ms", connectionAcquireStopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            connectionAcquireStopwatch.Stop();
            _logger.LogError(ex, "接続プール取得失敗 - 経過時間: {ElapsedMs}ms", connectionAcquireStopwatch.ElapsedMilliseconds);
            throw new InvalidOperationException($"接続プールから接続取得に失敗: {ex.Message}", ex);
        }
        
        try
        {
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
            await connection.Writer.WriteLineAsync(jsonRequest).ConfigureAwait(false);
            await connection.Writer.FlushAsync().ConfigureAwait(false); // 手動フラッシュ
            networkSendStopwatch.Stop();
            _logger.LogInformation("[TIMING] ネットワーク送信: {ElapsedMs}ms", networkSendStopwatch.ElapsedMilliseconds);
            
            var networkReceiveStopwatch = Stopwatch.StartNew();
            // レスポンス受信
            var jsonResponse = await connection.Reader.ReadLineAsync().ConfigureAwait(false);
            networkReceiveStopwatch.Stop();
            _logger.LogInformation("[TIMING] ネットワーク受信（Python処理含む）: {ElapsedMs}ms", networkReceiveStopwatch.ElapsedMilliseconds);
            
            if (string.IsNullOrEmpty(jsonResponse))
            {
                _logger.LogError("空のレスポンス受信 - 接続状態: Connected={Connected}, DataAvailable={DataAvailable}", 
                    connection?.TcpClient?.Connected ?? false, 
                    connection?.TcpClient?.GetStream()?.DataAvailable ?? false);
                throw new InvalidOperationException("サーバーから空のレスポンスを受信しました");
            }
            
            _logger.LogDebug("Python応答受信: {Response}", jsonResponse.Length > 200 ? jsonResponse[..200] + "..." : jsonResponse);
            
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
                _logger.LogDebug("翻訳成功 - Text: '{Text}', Confidence: {Confidence}", 
                    translatedText, confidenceScore);
            }
            else
            {
                translatedText = "翻訳エラーが発生しました";
                confidenceScore = 0.0f;
                isSuccess = false;
                _logger.LogError("翻訳失敗 - Success: {Success}, Translation: '{Translation}', Error: '{Error}'", 
                    response.success, response.translation ?? "null", response.error ?? "none");
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
            // Issue #147: 接続プールに接続を返却
            await _connectionPool.ReleaseConnectionAsync(connection).ConfigureAwait(false);
        }
    }

    private async Task MonitorServerHealthAsync()
    {
        while (!_disposed)
        {
            try
            {
                await Task.Delay(HealthCheckIntervalMs).ConfigureAwait(false);
                
                // Issue #147: 接続プールのヘルスチェックに委任
                // 接続プール自体がヘルスチェックを行うため、サーバープロセスの監視に専念
                if (_serverProcess == null || _serverProcess.HasExited)
                {
                    _logger.LogWarning("サーバープロセス異常終了を検出 - 再起動を試行");
                    await StartOptimizedServerAsync().ConfigureAwait(false);
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
            // Issue #147: 接続プールによる接続テスト
            using var testCts = new CancellationTokenSource(ConnectionTimeoutMs);
            var testConnection = await _connectionPool.AcquireConnectionAsync(testCts.Token).ConfigureAwait(false);
            await _connectionPool.ReleaseConnectionAsync(testConnection).ConfigureAwait(false);
            return true;
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

    // Issue #147: DisposePersistentConnectionメソッドは接続プール統合により削除
    // 接続管理は FixedSizeConnectionPool が担当

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
            
            // Issue #147: 接続プールの破棄は DI コンテナが管理
            // FixedSizeConnectionPool は IAsyncDisposable として適切に破棄される
            
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