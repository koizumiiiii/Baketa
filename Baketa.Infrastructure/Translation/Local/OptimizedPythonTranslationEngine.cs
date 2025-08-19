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
using Baketa.Core.Abstractions.Patterns;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Common;
using Baketa.Core.Translation.Exceptions;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Translation.Local.ConnectionPool;
using Baketa.Infrastructure.Translation.Models;
using Baketa.Infrastructure.Patterns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace Baketa.Infrastructure.Translation.Local;

/// <summary>
/// 最適化された高速Python翻訳エンジン（目標: 500ms以下）
/// Issue #147 Phase 5: 動的ポート対応とサーバー管理統合
/// </summary>
public class OptimizedPythonTranslationEngine : ITranslationEngine
{
    private readonly ILogger<OptimizedPythonTranslationEngine> _logger;
    private readonly SemaphoreSlim _serverLock = new(1, 1);
    private readonly FixedSizeConnectionPool? _connectionPool; // Issue #147: 接続プール統合（動的ポートモードではnull）
    private readonly IConfiguration _configuration; // Issue #147: 動的設定管理
    private readonly IPythonServerManager? _serverManager; // Phase 5: 動的ポート対応
    private readonly ICircuitBreaker<TranslationResponse>? _circuitBreaker; // Phase 2: サーキットブレーカー統合
    
    // サーバープロセス管理（Phase 5以降はPythonServerManagerが管理）
    private Process? _serverProcess;
    private IPythonServerInfo? _managedServerInstance;
    
    // パフォーマンス監視
    // 🚨 CACHE_DISABLED: キャッシュ汚染問題根本解決のためキャッシュ機能完全無効化
    // private readonly ConcurrentDictionary<string, TranslationMetrics> _metricsCache = new();
    private long _totalRequests;
    private long _totalProcessingTimeMs;
    private readonly Stopwatch _uptimeStopwatch = new();
    
    // モデルロード完了待機機構
    private readonly TaskCompletionSource<bool> _modelLoadCompletion = new();
    private volatile bool _isModelLoaded = false;
    private readonly object _initializationLock = new();
    
    // 設定
    private const string ServerHost = "127.0.0.1";
    private int _serverPort = 5556; // 動的ポート（デフォルト: OPUS-MT=5556, NLLB-200=5557）
    private const int ConnectionTimeoutMs = 10000; // 接続タイムアウトを10秒に延長
    private const int StartupTimeoutMs = 60000; // 起動タイムアウトを60秒に延長（モデルロード考慮）
    private const int HealthCheckIntervalMs = 30000; // ヘルスチェック間隔
    
    // Python実行パス
    private readonly string _pythonPath;
    private string _serverScriptPath = string.Empty; // 動的設定のため読み取り専用を削除
    
    public string Name => "NLLB200";
    public string Description => "高速化されたPython翻訳エンジン（500ms目標）";
    public bool RequiresNetwork => false;

    public OptimizedPythonTranslationEngine(
        ILogger<OptimizedPythonTranslationEngine> logger,
        FixedSizeConnectionPool? connectionPool,
        IConfiguration configuration,
        IPythonServerManager? serverManager = null,
        ICircuitBreaker<TranslationResponse>? circuitBreaker = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionPool = connectionPool; // null許容（単発接続モード用）
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _serverManager = serverManager; // null許容（既存の固定ポートモードとの互換性）
        _circuitBreaker = circuitBreaker; // null許容（サーキットブレーカー無効化時）
        
        // Python実行環境設定（py launcherを使用）
        _pythonPath = "py";
        
        // プロジェクトルート検索
        var currentDir = Directory.GetCurrentDirectory();
        var projectRoot = FindProjectRoot(currentDir);
        
        // 🎯 [NLLB-200] 動的ポート設定と動的スクリプトパス設定
        ConfigureServerSettings(projectRoot);
        
        _logger.LogInformation("OptimizedPythonTranslationEngine初期化 - Python: {PythonPath}, Script: {ScriptPath}", 
            _pythonPath, _serverScriptPath);
            
        _logger.LogInformation("モデルロード待機機構を初期化しました");
        
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
            if (_configuration.GetValue<bool>("Translation:UseExternalServer", false))
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
            
            // 接続確認（接続プール有無に応じて処理分岐）
            try
            {
                if (_connectionPool != null)
                {
                    using var testCts = new CancellationTokenSource(5000);
                    var testConnection = await _connectionPool.AcquireConnectionAsync(testCts.Token).ConfigureAwait(false);
                    await _connectionPool.ReleaseConnectionAsync(testConnection).ConfigureAwait(false);
                    _logger.LogInformation("接続プール経由でサーバー接続を確認");
                }
                else
                {
                    // 🔄 単発接続テスト（汚染対策モード）
                    await TestDirectConnectionAsync().ConfigureAwait(false);
                    _logger.LogInformation("🔄 単発接続でサーバー接続を確認（汚染対策モード）");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "サーバー接続確認失敗");
                return false;
            }
            
            // ヘルスチェックタスク開始
            _ = Task.Run(async () => await MonitorServerHealthAsync().ConfigureAwait(false));
            
            _logger.LogInformation("OptimizedPythonTranslationEngine初期化完了");
            
            // モデルロード完了のシグナル
            MarkModelAsLoaded();
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初期化エラー");
            
            // 初期化失敗時はモデルロード失敗を通知
            MarkModelLoadFailed(ex);
            
            return false;
        }
    }

    private async Task<bool> StartOptimizedServerAsync()
    {
        try
        {
            await _serverLock.WaitAsync().ConfigureAwait(false);
            
            // Phase 5: PythonServerManagerが利用可能な場合は動的ポート管理を使用
            if (_serverManager != null)
            {
                return await StartManagedServerAsync().ConfigureAwait(false);
            }
            
            // 従来の固定ポートモード（後方互換性）
            return await StartLegacyFixedPortServerAsync().ConfigureAwait(false);
        }
        finally
        {
            _serverLock.Release();
        }
    }
    
    /// <summary>
    /// PythonServerManager経由での動的ポートサーバー起動
    /// </summary>
    private async Task<bool> StartManagedServerAsync()
    {
        try
        {
            _logger.LogInformation("🚀 動的ポート管理によるサーバー起動開始");
            
            // 日本語→英語翻訳用サーバー起動（Phase 5では言語ペア指定）
            _managedServerInstance = await _serverManager!.StartServerAsync("ja-en").ConfigureAwait(false);
            
            _logger.LogInformation("✅ 動的ポートサーバー起動完了: Port {Port}, StartedAt {StartedAt}", 
                _managedServerInstance.Port, _managedServerInstance.StartedAt);
            
            // 接続プールのポート更新
            if (_connectionPool != null)
            {
                // TODO: 接続プールにポート変更通知メソッドを追加予定
                _logger.LogDebug("接続プール更新: Port {Port}", _managedServerInstance.Port);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 動的ポートサーバー起動失敗");
            return false;
        }
    }
    
    /// <summary>
    /// 従来の固定ポートサーバー起動（後方互換性）
    /// </summary>
    private async Task<bool> StartLegacyFixedPortServerAsync()
    {
        _logger.LogInformation("🔧 固定ポートモードでサーバー起動開始 (Port {Port})", _serverPort);
        
        // 直接Python実行（PowerShell経由を排除）
        var processInfo = new ProcessStartInfo
        {
            FileName = _pythonPath,
            Arguments = $"\"{_serverScriptPath}\" --port {_serverPort} --optimized",
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
        
        // サーバー起動待機（最大60秒、モデルロード完了まで）
        var startTime = DateTime.UtcNow;
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < StartupTimeoutMs)
        {
            await Task.Delay(2000).ConfigureAwait(false); // ポーリング間隔を2秒に延長
            
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
            
            // Issue #147: 接続テスト（タイムアウト延長）
            try
            {
                if (await TestConnectionAsync().ConfigureAwait(false))
                {
                    var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    _logger.LogInformation("サーバー起動成功 - 起動時間: {ElapsedMs}ms", elapsedMs);
                    return true;
                }
            }
            catch
            {
                // 接続テスト失敗 - サーバーがまだ起動していない
            }
        }
        
        _logger.LogError("サーバー起動タイムアウト");
        return false;
    }

    /// <summary>
    /// モデルロード完了をマーク
    /// </summary>
    private void MarkModelAsLoaded()
    {
        lock (_initializationLock)
        {
            if (!_isModelLoaded)
            {
                _isModelLoaded = true;
                _modelLoadCompletion.TrySetResult(true);
                _logger.LogInformation("🚀 モデルロード完了 - 翻訳リクエスト受付開始");
            }
        }
    }

    /// <summary>
    /// モデルロード失敗をマーク
    /// </summary>
    /// <param name="exception">失敗理由</param>
    private void MarkModelLoadFailed(Exception exception)
    {
        lock (_initializationLock)
        {
            if (!_isModelLoaded)
            {
                _modelLoadCompletion.TrySetException(exception);
                _logger.LogError(exception, "⚠️ モデルロード失敗 - 翻訳リクエストはエラーを返します");
            }
        }
    }

    /// <summary>
    /// モデルロード状態をリセット（テスト用）
    /// </summary>
    internal void ResetModelLoadState()
    {
        lock (_initializationLock)
        {
            _isModelLoaded = false;
            // 新しいTaskCompletionSourceは再初期化時に作成
        }
    }

    // Issue #147: EstablishPersistentConnectionAsyncメソッドは接続プール統合により削除
    // 接続管理は FixedSizeConnectionPool が担当

    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        // 🔥 [TRANSLATE_DEBUG] TranslateAsyncメソッド開始デバッグ
        _logger.LogError("🔥 [TRANSLATE_DEBUG] TranslateAsync 呼び出し開始");
        _logger.LogError("🔥 [TRANSLATE_DEBUG] - RequestId: {RequestId}", request.RequestId);
        _logger.LogError("🔥 [TRANSLATE_DEBUG] - SourceText: '{SourceText}'", request.SourceText);
        _logger.LogError("🔥 [TRANSLATE_DEBUG] - SourceLanguage: {SourceLanguage}", request.SourceLanguage);
        _logger.LogError("🔥 [TRANSLATE_DEBUG] - TargetLanguage: {TargetLanguage}", request.TargetLanguage);
        Console.WriteLine($"🔥 [TRANSLATE_DEBUG] TranslateAsync 呼び出し開始 - RequestId: {request.RequestId}");
        Console.WriteLine($"🔥 [TRANSLATE_DEBUG] SourceText: '{request.SourceText}', {request.SourceLanguage} → {request.TargetLanguage}");
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // モデルロード完了まで待機（タイムアウト付き）
            _logger.LogDebug("翻訳リクエスト開始 - モデルロード待機中...");
            using var modelLoadTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(120)); // 🔧 [TIMEOUT_TEST] 30秒→120秒に延長してタイムアウト原因を確定検証
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, modelLoadTimeout.Token);
            
            try
            {
                await _modelLoadCompletion.Task.WaitAsync(combinedCts.Token).ConfigureAwait(false);
                _logger.LogDebug("モデルロード完了 - 翻訳処理開始");
            }
            catch (OperationCanceledException) when (modelLoadTimeout.Token.IsCancellationRequested)
            {
                _logger.LogWarning("モデルロード待機タイムアウト（30秒） - 初期化を試行します");
                // タイムアウト時は初期化を試行
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("翻訳リクエストがキャンセルされました");
                throw;
            }
            
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
            
            // 🚨 CACHE_DISABLED: キャッシュ機能完全無効化 - 汚染問題根本解決
            // キャッシュチェック処理を完全削除
            _logger.LogDebug("キャッシュ無効化モード - 常に新鮮な翻訳を実行");
            
            // Phase2: サーキットブレーカーによる翻訳実行
            TranslationResponse result;
            if (_circuitBreaker != null)
            {
                _logger.LogDebug("🔧 [CIRCUIT_BREAKER] サーキットブレーカー経由で翻訳実行開始");
                result = await _circuitBreaker.ExecuteAsync(
                    async ct => await TranslateWithOptimizedServerAsync(request, ct).ConfigureAwait(false), 
                    cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("🔧 [CIRCUIT_BREAKER] サーキットブレーカー経由で翻訳実行完了");
            }
            else
            {
                // サーキットブレーカー無効時は従来通り直接実行
                _logger.LogDebug("🔥 TranslateWithOptimizedServerAsync 直接呼び出し開始");
                result = await TranslateWithOptimizedServerAsync(request, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("🔥 TranslateWithOptimizedServerAsync 直接呼び出し完了");
            }
            
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
            
            // 🚨 CACHE_DISABLED: キャッシュ保存機能完全無効化 - 汚染問題根本解決
            // キャッシュ保存処理を完全削除
            _logger.LogDebug("キャッシュ無効化モード - 翻訳結果をキャッシュに保存しません");
            
            return result;
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning("個別翻訳タイムアウト（5秒）- Text: '{Text}', 処理時間: {ElapsedMs}ms", 
                request.SourceText, stopwatch.ElapsedMilliseconds);
            
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                TranslatedText = "翻訳タイムアウト（サーバー応答なし）",
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                ConfidenceScore = 0.0f,
                EngineName = Name,
                IsSuccess = false
            };
        }
        catch (CircuitBreakerOpenException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning("🚨 [CIRCUIT_BREAKER] サーキットブレーカーが開いています - 処理時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                TranslatedText = "翻訳サービスが一時的に利用できません（サーキットブレーカー開放中）",
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                ConfidenceScore = 0.0f,
                EngineName = Name,
                IsSuccess = false,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (TranslationTimeoutException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning("⏱️ [CIRCUIT_BREAKER] 翻訳タイムアウト - 処理時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                TranslatedText = "翻訳がタイムアウトしました",
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                ConfidenceScore = 0.0f,
                EngineName = Name,
                IsSuccess = false,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "翻訳エラー - 処理時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            
            // 🔥 [ERROR_DEBUG] 例外の詳細情報を出力
            _logger.LogError("🔥 [ERROR_DEBUG] 例外詳細:");
            _logger.LogError("🔥 [ERROR_DEBUG] - 例外タイプ: {ExceptionType}", ex.GetType().Name);
            _logger.LogError("🔥 [ERROR_DEBUG] - 例外メッセージ: {Message}", ex.Message);
            _logger.LogError("🔥 [ERROR_DEBUG] - スタックトレース: {StackTrace}", ex.StackTrace);
            Console.WriteLine($"🔥 [ERROR_DEBUG] 翻訳エラー発生: {ex.GetType().Name} - {ex.Message}");
            
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

        // 言語ペアでグループ化
        var groupedRequests = requests.GroupBy(r => $"{r.SourceLanguage.Code}_{r.TargetLanguage.Code}");
        var allResponses = new List<TranslationResponse>();

        foreach (var group in groupedRequests)
        {
            var groupList = group.ToList();
            
            // バッチサイズ制限確認
            const int maxBatchSize = 50;
            if (groupList.Count > maxBatchSize)
            {
                // 大きなバッチを分割処理
                var splitResponses = await ProcessLargeBatchAsync(groupList, maxBatchSize, cancellationToken).ConfigureAwait(false);
                allResponses.AddRange(splitResponses);
            }
            else
            {
                // 通常のバッチ処理
                var batchResponses = await ProcessSingleBatchAsync(groupList, cancellationToken).ConfigureAwait(false);
                allResponses.AddRange(batchResponses);
            }
        }

        // 元の順序を保持するため、RequestIdでソート
        var responseMap = allResponses.ToDictionary(r => r.RequestId);
        return requests.Select(req => responseMap.TryGetValue(req.RequestId, out var response) 
            ? response 
            : TranslationResponse.CreateError(req, 
                new TranslationError { ErrorCode = "BATCH_PROCESSING_ERROR", Message = "Response not found" }, 
                Name)).ToList();
    }

    private async Task<IReadOnlyList<TranslationResponse>> ProcessSingleBatchAsync(
        IReadOnlyList<TranslationRequest> requests, 
        CancellationToken cancellationToken)
    {
        var batchStopwatch = Stopwatch.StartNew();
        PersistentConnection? connection = null;
        TcpClient? directClient = null;
        NetworkStream? directStream = null;
        StreamWriter? directWriter = null;
        StreamReader? directReader = null;

        try
        {
            if (_connectionPool != null)
            {
                // Phase 1統合: 接続プールから接続を取得
                connection = await _connectionPool.AcquireConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // 🔄 単発接続でバッチ処理（汚染対策モード）
                directClient = new TcpClient();
                await directClient.ConnectAsync(ServerHost, _serverPort, cancellationToken).ConfigureAwait(false);
                
                directStream = directClient.GetStream();
                directStream.ReadTimeout = ConnectionTimeoutMs;
                directStream.WriteTimeout = ConnectionTimeoutMs;
                
                // 🔧 [CRITICAL_ENCODING_FIX] システムレベルUTF-8エンコーディング指定（Windows問題対応）
                var utf8EncodingNoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
                directWriter = new StreamWriter(directStream, utf8EncodingNoBom, bufferSize: 8192, leaveOpen: true) { AutoFlush = true };
                directReader = new StreamReader(directStream, utf8EncodingNoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
            }

            // バッチリクエスト構築（同じ言語ペアが保証されている）
            var batchRequest = new
            {
                texts = requests.Select(r => r.SourceText).ToList(),
                source_lang = requests[0].SourceLanguage.Code,  // 🔧 CRITICAL FIX: 言語方向修正完了
                target_lang = requests[0].TargetLanguage.Code,  // 🔧 CRITICAL FIX: 言語方向修正完了
                batch_mode = true,
                max_batch_size = 50
            };

            // JSON送信
            var jsonRequest = JsonSerializer.Serialize(batchRequest);
            
            string? jsonResponse;
            if (connection != null)
            {
                // 接続プール使用モード
                await connection.Writer.WriteLineAsync(jsonRequest).ConfigureAwait(false);
                // 🔧 [TIMEOUT_FIX] バッチ翻訳ReadLineAsync()に5秒タイムアウト追加で無限待機防止
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                jsonResponse = await connection.Reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
            }
            else
            {
                // 単発接続モード（汚染対策）
                await directWriter!.WriteLineAsync(jsonRequest).ConfigureAwait(false);
                // 🔧 [TIMEOUT_FIX] バッチ翻訳ReadLineAsync()に5秒タイムアウト追加で無限待機防止
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                jsonResponse = await directReader!.ReadLineAsync(cts.Token).ConfigureAwait(false);
            }
            
            if (string.IsNullOrEmpty(jsonResponse))
                throw new InvalidOperationException("Empty response from Python server");

            var batchResponse = JsonSerializer.Deserialize<PythonBatchResponse>(jsonResponse);
            
            if (batchResponse == null)
                throw new InvalidOperationException("Failed to deserialize batch response");

            batchStopwatch.Stop();

            // レスポンスマッピング
            return MapBatchResponse(batchResponse, requests, batchStopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            batchStopwatch.Stop();
            _logger.LogWarning("バッチ翻訳タイムアウト（5秒）: Pythonサーバーからの応答待機でタイムアウト発生");
            
            // タイムアウト時は個別処理でフォールバック
            return await FallbackToIndividualProcessingAsync(requests, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            batchStopwatch.Stop();
            _logger.LogError(ex, "バッチ翻訳エラー: {Error}", ex.Message);
            
            // エラー時は個別処理でフォールバック
            return await FallbackToIndividualProcessingAsync(requests, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (connection != null)
            {
                // Phase 1統合: 接続をプールに返却
                await _connectionPool!.ReleaseConnectionAsync(connection).ConfigureAwait(false);
            }
            else
            {
                // 🔄 単発接続リソースの解放（汚染対策モード）
                directWriter?.Dispose();
                directReader?.Dispose();
                directStream?.Dispose();
                directClient?.Dispose();
            }
        }
    }

    private async Task<IReadOnlyList<TranslationResponse>> ProcessLargeBatchAsync(
        IReadOnlyList<TranslationRequest> requests,
        int maxBatchSize,
        CancellationToken cancellationToken)
    {
        var results = new List<TranslationResponse>();

        // バッチを分割して並列処理
        var batches = requests
            .Select((request, index) => new { request, index })
            .GroupBy(x => x.index / maxBatchSize)
            .Select(g => g.Select(x => x.request).ToList())
            .ToList();

        // 並列バッチ処理（接続プール活用）
        var tasks = batches.Select(batch => TranslateBatchAsync(batch, cancellationToken));
        var batchResults = await Task.WhenAll(tasks).ConfigureAwait(false);

        // 結果をフラット化
        foreach (var batchResult in batchResults)
        {
            results.AddRange(batchResult);
        }

        return results;
    }

    private IReadOnlyList<TranslationResponse> MapBatchResponse(
        PythonBatchResponse batchResponse, 
        IReadOnlyList<TranslationRequest> originalRequests, 
        long elapsedMilliseconds)
    {
        const string engineName = "OptimizedPythonTranslation";
        
        if (!batchResponse.success || batchResponse.translations == null)
        {
            // エラー時は全てFailureで返す
            var errorMessage = batchResponse.errors?.FirstOrDefault() ?? "Unknown batch translation error";
            return originalRequests.Select(req => 
            {
                var error = new TranslationError
                {
                    ErrorCode = "BATCH_TRANSLATION_ERROR",
                    Message = errorMessage
                };
                return TranslationResponse.CreateError(req, error, engineName);
            }).ToList();
        }

        var results = new List<TranslationResponse>();
        var translations = batchResponse.translations;
        var confidenceScores = batchResponse.confidence_scores ?? [];

        for (int i = 0; i < originalRequests.Count && i < translations.Count; i++)
        {
            var request = originalRequests[i];
            var translation = translations[i];
            var confidence = i < confidenceScores.Count ? confidenceScores[i] : 0.95f;
            var avgProcessingTime = elapsedMilliseconds / originalRequests.Count;

            var response = TranslationResponse.CreateSuccessWithConfidence(
                request,
                translation,
                engineName,
                avgProcessingTime,
                confidence
            );

            results.Add(response);
        }

        // バッチサイズ不一致の場合のフォールバック
        if (results.Count < originalRequests.Count)
        {
            _logger.LogWarning("バッチレスポンスサイズ不一致: expected {Expected}, got {Actual}", 
                originalRequests.Count, results.Count);
            
            // 不足分はエラーレスポンスで埋める
            for (int i = results.Count; i < originalRequests.Count; i++)
            {
                var request = originalRequests[i];
                var error = new TranslationError
                {
                    ErrorCode = "BATCH_SIZE_MISMATCH",
                    Message = "Batch response size mismatch"
                };
                var errorResponse = TranslationResponse.CreateError(request, error, engineName);
                errorResponse.ProcessingTimeMs = elapsedMilliseconds;
                results.Add(errorResponse);
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<TranslationResponse>> FallbackToIndividualProcessingAsync(
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken)
    {
        const string engineName = "OptimizedPythonTranslation";
        _logger.LogInformation("バッチ処理失敗 - 個別処理にフォールバック: {Count}件", requests.Count);
        
        var results = new List<TranslationResponse>();
        
        foreach (var request in requests)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            try
            {
                var response = await TranslateAsync(request, cancellationToken).ConfigureAwait(false);
                results.Add(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "個別翻訳処理エラー: {Text}", request.SourceText);
                var errorResponse = TranslationResponse.CreateErrorFromException(
                    request,
                    engineName,
                    "INDIVIDUAL_PROCESSING_ERROR",
                    ex.Message,
                    ex,
                    0
                );
                results.Add(errorResponse);
            }
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
        // 🚨 [HANGUP_DEBUG] メソッド開始時点のデバッグ
        _logger.LogDebug("🔥 TranslateWithOptimizedServerAsync メソッド開始");
        Console.WriteLine($"🔥 [HANGUP_DEBUG] TranslateWithOptimizedServerAsync メソッド開始 - RequestId: {request.RequestId}");
        
        var totalStopwatch = Stopwatch.StartNew();
        var connectionAcquireStopwatch = Stopwatch.StartNew();
        
        PersistentConnection? connection = null;
        TcpClient? directClient = null;
        NetworkStream? directStream = null;
        StreamWriter? directWriter = null;
        StreamReader? directReader = null;

        try
        {
            // 🚨 [HANGUP_DEBUG] 接続プール確認デバッグ
            Console.WriteLine($"🔥 [HANGUP_DEBUG] 接続プール確認開始 - _connectionPool != null: {_connectionPool != null}");
            _logger.LogDebug("🔥 接続プール確認開始 - _connectionPool != null: {IsNotNull}", _connectionPool != null);
            
            if (_connectionPool != null)
            {
                // Issue #147: 接続プールから接続を取得（接続ロック競合を解決）
                // 🔧 [TIMEOUT_FIX] 接続プール取得に30秒タイムアウトを追加
                using var poolTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(120)); // 🔧 [TIMEOUT_TEST] 30秒→120秒に延長してタイムアウト原因を確定検証
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, poolTimeout.Token);
                
                _logger.LogDebug("🔌 接続プール取得開始...");
                connection = await _connectionPool.AcquireConnectionAsync(combinedCts.Token).ConfigureAwait(false);
                connectionAcquireStopwatch.Stop();
                _logger.LogInformation("[TIMING] 接続プール取得: {ElapsedMs}ms", connectionAcquireStopwatch.ElapsedMilliseconds);
            }
            else
            {
                // 🔄 単発接続作成（汚染対策モード）
                directClient = new TcpClient();
                await directClient.ConnectAsync(ServerHost, _serverPort, cancellationToken).ConfigureAwait(false);

                directStream = directClient.GetStream();
                directStream.ReadTimeout = ConnectionTimeoutMs;
                directStream.WriteTimeout = ConnectionTimeoutMs;

                // 🔧 [ENCODING_SIMPLIFIED] シンプルなUTF-8エンコーディング指定（Windows修復処理削除）
                var utf8EncodingNoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
                directWriter = new StreamWriter(directStream, utf8EncodingNoBom, bufferSize: 8192, leaveOpen: true) { AutoFlush = true };
                directReader = new StreamReader(directStream, utf8EncodingNoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);

                connectionAcquireStopwatch.Stop();
                _logger.LogInformation("[TIMING] 単発接続作成（汚染対策）: {ElapsedMs}ms", connectionAcquireStopwatch.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            connectionAcquireStopwatch.Stop();
            _logger.LogError(ex, "接続取得失敗 - 経過時間: {ElapsedMs}ms", connectionAcquireStopwatch.ElapsedMilliseconds);
            directWriter?.Dispose();
            directReader?.Dispose();
            directStream?.Dispose();
            directClient?.Dispose();
            throw new InvalidOperationException($"接続取得に失敗: {ex.Message}", ex);
        }
        
        try
        {
            var serializationStopwatch = Stopwatch.StartNew();
            // リクエスト送信
            var requestData = new
            {
                text = request.SourceText,
                source_lang = request.SourceLanguage.Code,  // 🔧 CRITICAL FIX: 言語方向修正完了
                target_lang = request.TargetLanguage.Code,  // 🔧 CRITICAL FIX: 言語方向修正完了
                request_id = request.RequestId
            };
            
            var jsonRequest = JsonSerializer.Serialize(requestData);
            serializationStopwatch.Stop();
            _logger.LogInformation("[TIMING] JSONシリアライゼーション: {ElapsedMs}ms", serializationStopwatch.ElapsedMilliseconds);
            
            var networkSendStopwatch = Stopwatch.StartNew();
            
            string? jsonResponse;
            if (connection != null)
            {
                // 接続プール使用モード
                await connection.Writer.WriteLineAsync(jsonRequest).ConfigureAwait(false);
                await connection.Writer.FlushAsync().ConfigureAwait(false); // 手動フラッシュ
                networkSendStopwatch.Stop();
                _logger.LogInformation("[TIMING] ネットワーク送信（プール接続）: {ElapsedMs}ms", networkSendStopwatch.ElapsedMilliseconds);
                
                var networkReceiveStopwatch = Stopwatch.StartNew();
                // 🔧 [TIMEOUT_FIX] ReadLineAsync()に15秒タイムアウト追加でPython処理時間を考慮
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                jsonResponse = await connection.Reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
                networkReceiveStopwatch.Stop();
                _logger.LogInformation("[TIMING] ネットワーク受信（プール接続、Python処理含む）: {ElapsedMs}ms", networkReceiveStopwatch.ElapsedMilliseconds);
            }
            else
            {
                // 単発接続モード（汚染対策）
                await directWriter!.WriteLineAsync(jsonRequest).ConfigureAwait(false);
                networkSendStopwatch.Stop();
                _logger.LogInformation("[TIMING] ネットワーク送信（単発接続）: {ElapsedMs}ms", networkSendStopwatch.ElapsedMilliseconds);
                
                var networkReceiveStopwatch = Stopwatch.StartNew();
                // 🔧 [TIMEOUT_FIX] ReadLineAsync()に15秒タイムアウト追加でPython処理時間を考慮
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                jsonResponse = await directReader!.ReadLineAsync(cts.Token).ConfigureAwait(false);
                networkReceiveStopwatch.Stop();
                _logger.LogInformation("[TIMING] ネットワーク受信（単発接続、Python処理含む）: {ElapsedMs}ms", networkReceiveStopwatch.ElapsedMilliseconds);
            }
            
            if (string.IsNullOrEmpty(jsonResponse))
            {
                var isConnected = connection?.TcpClient?.Connected ?? directClient?.Connected ?? false;
                var dataAvailable = connection?.TcpClient?.GetStream()?.DataAvailable ?? directStream?.DataAvailable ?? false;
                _logger.LogError("空のレスポンス受信 - 接続状態: Connected={Connected}, DataAvailable={DataAvailable}", 
                    isConnected, dataAvailable);
                throw new InvalidOperationException("サーバーから空のレスポンスを受信しました");
            }
            
            _logger.LogDebug("Python応答受信: {Response}", jsonResponse.Length > 200 ? jsonResponse[..200] + "..." : jsonResponse);
            
            // 🔥 [ENCODING_DEBUG] 受信したレスポンスの詳細バイト情報をログ出力
            var responseBytes = System.Text.Encoding.UTF8.GetBytes(jsonResponse);
            _logger.LogError("🔍 [ENCODING_DEBUG] 受信したレスポンス詳細:");
            _logger.LogError("🔍 [ENCODING_DEBUG] - レスポンス文字列: '{Response}'", jsonResponse);
            _logger.LogError("🔍 [ENCODING_DEBUG] - レスポンス長: {Length}", jsonResponse.Length);
            _logger.LogError("🔍 [ENCODING_DEBUG] - UTF-8バイト: {Bytes}", Convert.ToHexString(responseBytes));
            Console.WriteLine($"🔍 [ENCODING_DEBUG] 受信したレスポンス: '{jsonResponse}'");
            Console.WriteLine($"🔍 [ENCODING_DEBUG] UTF-8バイト: {Convert.ToHexString(responseBytes)}");
            
            // 🔧 [ENCODING_SIMPLIFIED] Windows環境エンコーディング修復処理を削除し、シンプルUTF-8処理に変更
            var originalResponse = jsonResponse;
            
            // 🚨 DEBUG: 不正翻訳結果の調査用詳細ログ
            Console.WriteLine($"🔍 [CORRUPTION_DEBUG] Python応答受信: '{jsonResponse}'");
            SafeAppendToDebugFile($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [PYTHON_RESPONSE] Request: '{request.SourceText}' → Response: '{jsonResponse}'{Environment.NewLine}");
            
            var deserializationStopwatch = Stopwatch.StartNew();
            
            // 🔧 [ENCODING_SIMPLIFIED] 直接UTF-8でJSONデシリアライゼーション
            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                PropertyNameCaseInsensitive = true
            };
            
            PythonTranslationResponse? response;
            try 
            {
                // シンプルなJSONデシリアライゼーション（エンコーディング修復処理なし）
                response = JsonSerializer.Deserialize<PythonTranslationResponse>(jsonResponse, jsonOptions);
            }
            catch (Exception jsonEx)
            {
                _logger.LogError(jsonEx, "JSONデシリアライゼーション失敗: {Error}", jsonEx.Message);
                throw new InvalidOperationException($"JSONレスポンスの解析に失敗: {jsonEx.Message}", jsonEx);
            }
            
            deserializationStopwatch.Stop();
            _logger.LogInformation("[TIMING] JSONデシリアライゼーション（シンプル版）: {ElapsedMs}ms", deserializationStopwatch.ElapsedMilliseconds);
            
            if (response == null)
            {
                throw new InvalidOperationException("レスポンスのデシリアライズに失敗しました");
            }
            
            // 🔥 [ENCODING_DEBUG] JSON解析後のレスポンス詳細情報をログ出力
            _logger.LogError("🔍 [JSON_DEBUG] JSON解析後のレスポンス詳細:");
            _logger.LogError("🔍 [JSON_DEBUG] - Success: {Success}", response.success);
            _logger.LogError("🔍 [JSON_DEBUG] - Translation: '{Translation}'", response.translation ?? "null");
            _logger.LogError("🔍 [JSON_DEBUG] - Translation Length: {Length}", response.translation?.Length ?? 0);
            if (response.translation != null)
            {
                var translationBytes = System.Text.Encoding.UTF8.GetBytes(response.translation);
                _logger.LogError("🔍 [JSON_DEBUG] - Translation UTF-8バイト: {Bytes}", Convert.ToHexString(translationBytes));
            }
            _logger.LogError("🔍 [JSON_DEBUG] - Confidence: {Confidence}", response.confidence);
            _logger.LogError("🔍 [JSON_DEBUG] - Error: '{Error}'", response.error ?? "null");
            Console.WriteLine($"🔍 [JSON_DEBUG] Success: {response.success}, Translation: '{response.translation}', Length: {response.translation?.Length ?? 0}");
            
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
                
                // 🔧 [ENCODING_DEBUG] 文字エンコーディング詳細情報をログ出力
                var originalBytes = System.Text.Encoding.UTF8.GetBytes(translatedText);
                var decodedText = System.Text.Encoding.UTF8.GetString(originalBytes);
                _logger.LogInformation("翻訳結果詳細情報 - IsSuccess: {IsSuccess}, Text: '{Text}', Length: {Length}", 
                    isSuccess, translatedText, translatedText.Length);
                
                Console.WriteLine($"🔍 [ENCODING_DEBUG] 翻訳結果詳細:");
                Console.WriteLine($"🔍 [ENCODING_DEBUG] - 原文: '{request.SourceText}'");
                Console.WriteLine($"🔍 [ENCODING_DEBUG] - 翻訳結果: '{translatedText}'");
                Console.WriteLine($"🔍 [ENCODING_DEBUG] - UTF-8再エンコード: '{decodedText}'");
                Console.WriteLine($"🔍 [ENCODING_DEBUG] - バイト長: {originalBytes.Length}");
                Console.WriteLine($"🔍 [ENCODING_DEBUG] - 文字長: {translatedText.Length}");
                
                _logger.LogDebug("翻訳成功 - Text: '{Text}', Confidence: {Confidence}", 
                    translatedText, confidenceScore);
                
                // 🚨 DEBUG: 不正翻訳結果の検出
                var suspiciousPatterns = new[] { "マグブキ", "マッテヤ", "イブハテ", "マククナ" };
                if (suspiciousPatterns.Any(pattern => translatedText.Contains(pattern)))
                {
                    Console.WriteLine($"🚨 [CORRUPTION_DETECTED] 不正翻訳結果検出!");
                    Console.WriteLine($"   入力: '{request.SourceText}'");
                    Console.WriteLine($"   出力: '{translatedText}'");
                    Console.WriteLine($"   Python応答: '{jsonResponse}'");
                    SafeAppendToDebugFile($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [CORRUPTION_DETECTED] 入力: '{request.SourceText}' → 出力: '{translatedText}' → Python応答: '{jsonResponse}'{Environment.NewLine}");
                }
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
            if (connection != null)
            {
                // Issue #147: 接続プールに接続を返却
                await _connectionPool!.ReleaseConnectionAsync(connection).ConfigureAwait(false);
            }
            else
            {
                // 🔄 単発接続リソースの解放（汚染対策モード）
                directWriter?.Dispose();
                directReader?.Dispose();
                directStream?.Dispose();
                directClient?.Dispose();
            }
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
                    
                    // モデルロード完了シグナルを監視
                    if (line.Contains("MODEL_READY:"))
                    {
                        _logger.LogInformation("🏁 Pythonからモデルロード完了シグナルを受信");
                        MarkModelAsLoaded();
                    }
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
            // Phase 5: 動的ポート対応
            var targetPort = GetCurrentServerPort();
            
            if (_connectionPool != null)
            {
                // Issue #147: 接続プールによる接続テスト
                using var testCts = new CancellationTokenSource(ConnectionTimeoutMs);
                var testConnection = await _connectionPool.AcquireConnectionAsync(testCts.Token).ConfigureAwait(false);
                await _connectionPool.ReleaseConnectionAsync(testConnection).ConfigureAwait(false);
                return true;
            }
            else
            {
                // 🔄 単発接続テスト（汚染対策モード）- 動的ポート対応
                return await TestDirectConnectionAsync(targetPort).ConfigureAwait(false);
            }
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 現在のサーバーポート番号を取得
    /// </summary>
    private int GetCurrentServerPort()
    {
        // Phase 5: 動的ポート管理の場合
        if (_managedServerInstance != null)
        {
            return _managedServerInstance.Port;
        }
        
        // 固定ポートモード
        return _serverPort;
    }

    /// <summary>
    /// 単発接続での接続テスト（接続プール無効化時用）
    /// </summary>
    private async Task<bool> TestDirectConnectionAsync(int? port = null)
    {
        TcpClient? testClient = null;
        NetworkStream? testStream = null;
        StreamWriter? writer = null;
        StreamReader? reader = null;

        try
        {
            using var testCts = new CancellationTokenSource(ConnectionTimeoutMs);
            
            // Phase 5: 動的ポート対応
            var targetPort = port ?? GetCurrentServerPort();

            testClient = new TcpClient();
            await testClient.ConnectAsync(ServerHost, targetPort, testCts.Token).ConfigureAwait(false);

            testStream = testClient.GetStream();
            testStream.ReadTimeout = ConnectionTimeoutMs;
            testStream.WriteTimeout = ConnectionTimeoutMs;

            writer = new StreamWriter(testStream, new UTF8Encoding(false)) { AutoFlush = true };
            reader = new StreamReader(testStream, Encoding.UTF8);

            // 簡単なping確認
            var pingRequest = JsonSerializer.Serialize(new { ping = true });
            await writer.WriteLineAsync(pingRequest).ConfigureAwait(false);

            var response = await reader.ReadLineAsync().ConfigureAwait(false);
            return !string.IsNullOrEmpty(response);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "単発接続テスト失敗");
            return false;
        }
        finally
        {
            writer?.Dispose();
            reader?.Dispose();
            testStream?.Dispose();
            testClient?.Dispose();
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
            _logger.LogInformation("🔄 既存Pythonサーバープロセスのクリーンアップ開始");
            
            var processes = Process.GetProcessesByName("python");
            var killedCount = 0;
            
            foreach (var process in processes)
            {
                try
                {
                    // 🔧 [SCRIPT_NAME_FIX] NLLB-200翻訳サーバーのプロセス検出
                    var commandLine = GetProcessCommandLine(process);
                    
                    if (commandLine?.Contains("nllb_translation_server") == true || 
                        commandLine?.Contains("optimized_translation_server") == true)
                    {
                        _logger.LogInformation("🚨 既存翻訳サーバープロセス発見: PID {ProcessId}, Command: {CommandLine}", 
                            process.Id, commandLine);
                        
                        process.Kill();
                        await Task.Delay(100).ConfigureAwait(false);
                        killedCount++;
                        
                        _logger.LogInformation("✅ 既存Pythonサーバープロセスを終了: PID {ProcessId}", process.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("プロセス {ProcessId} の確認中にエラー: {Error}", process.Id, ex.Message);
                }
            }
            
            _logger.LogInformation("🔄 クリーンアップ完了: {KilledCount}個のプロセスを終了", killedCount);
            
            // プロセス終了の安定化待機
            if (killedCount > 0)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                _logger.LogInformation("🕒 プロセス終了安定化待機完了");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "既存プロセスのクリーンアップ中にエラー");
        }
    }
    
    /// <summary>
    /// プロセスのコマンドライン取得（WMI経由で確実に取得）
    /// </summary>
    private string? GetProcessCommandLine(Process process)
    {
        try
        {
            // MainModuleベースの簡易チェック
            var mainModule = process.MainModule?.FileName;
            if (mainModule != null)
            {
                return mainModule;
            }
            
            // 🔧 より確実なコマンドライン取得のため、WMI使用を検討
            // 現在は簡易実装で対応
            return null;
        }
        catch
        {
            return null;
        }
    }

    // 🚨 CACHE_DISABLED: キャッシュキー生成機能無効化
    // private string GenerateCacheKey(TranslationRequest request)
    // {
    //     return $"{request.SourceLanguage.Code}_{request.TargetLanguage.Code}_{request.SourceText.GetHashCode()}";
    // }

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

    private class PythonBatchResponse
    {
        public bool success { get; set; }
        public List<string>? translations { get; set; }
        public List<float>? confidence_scores { get; set; }
        public double? processing_time { get; set; }
        public int? batch_size { get; set; }
        public List<string>? errors { get; set; }
    }

    /// <summary>
    /// ファイル競合を防ぐ安全なデバッグファイル書き込み
    /// </summary>
    private void SafeAppendToDebugFile(string content)
    {
        const string debugFilePath = "E:\\dev\\Baketa\\debug_translation_corruption_csharp.txt";
        const int maxRetries = 3;
        const int retryDelayMs = 10;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var fileStream = new FileStream(debugFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(fileStream, Encoding.UTF8);
                writer.Write(content);
                writer.Flush();
                return; // 成功
            }
            catch (IOException ex) when (ex.Message.Contains("being used by another process"))
            {
                if (attempt < maxRetries)
                {
                    Thread.Sleep(retryDelayMs * attempt); // 指数バックオフ
                    continue;
                }
                // 最終試行でも失敗した場合はログのみ
                _logger.LogWarning("デバッグファイル書き込み失敗（ファイル競合）: {Error}", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("デバッグファイル書き込み失敗: {Error}", ex.Message);
                break;
            }
        }
    }
    
    /// <summary>
    /// 🎯 [NLLB-200] 設定に基づく動的ポート設定とスクリプトパス設定（IConfiguration版）
    /// </summary>
    private void ConfigureServerSettings(string projectRoot)
    {
        try
        {
            // 動的に設定を取得
            var defaultEngineString = _configuration["Translation:DefaultEngine"];
            var defaultEngine = Enum.TryParse<TranslationEngine>(defaultEngineString, out var parsedEngine) 
                ? parsedEngine 
                : TranslationEngine.NLLB200;
            
            if (defaultEngine == TranslationEngine.NLLB200)
            {
                // NLLB-200設定から動的にポートとスクリプトパスを取得
                _serverPort = _configuration.GetValue<int>("Translation:NLLB200:ServerPort", 5557);
                _serverScriptPath = Path.Combine(projectRoot, "scripts", "nllb_translation_server.py");
                _logger.LogInformation("🎯 [NLLB-200] NLLB-200モード - ポート: {Port}, スクリプト: {Script}", 
                    _serverPort, Path.GetFileName(_serverScriptPath));
            }
            else
            {
                // デフォルト設定から動的にポートとスクリプトパスを取得（レガシー互換性）
                _serverPort = _configuration.GetValue<int>("Translation:ServerPort", 5557);
                _serverScriptPath = Path.Combine(projectRoot, "scripts", "nllb_translation_server.py");
                _logger.LogInformation("🔧 [NLLB-200] デフォルトモード - ポート: {Port}, スクリプト: {Script}", 
                    _serverPort, Path.GetFileName(_serverScriptPath));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ サーバー設定エラー - デフォルト設定（NLLB-200）を使用");
            _serverPort = 5557;
            _serverScriptPath = Path.Combine(projectRoot, "scripts", "nllb_translation_server.py");
        }
    }
    
    /// <summary>
    /// 🎯 [DYNAMIC_CONFIG] 実行時設定取得
    /// </summary>
    private TranslationEngine GetCurrentTranslationEngine()
    {
        var defaultEngineString = _configuration["Translation:DefaultEngine"];
        return Enum.TryParse<TranslationEngine>(defaultEngineString, out var parsedEngine) 
            ? parsedEngine 
            : TranslationEngine.NLLB200;
    }
}