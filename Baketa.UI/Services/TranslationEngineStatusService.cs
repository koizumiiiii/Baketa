using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;

namespace Baketa.UI.Services;

/// <summary>
/// 翻訳エンジンの状態を監視するサービスの実装
/// </summary>
internal sealed class TranslationEngineStatusService : ITranslationEngineStatusService, IDisposable
{
    private readonly ILogger<TranslationEngineStatusService> _logger;
    private readonly TranslationEngineStatusOptions _options;
    private readonly Subject<TranslationEngineStatusUpdate> _statusUpdateSubject;
    private readonly Timer _monitoringTimer;
    private readonly CancellationTokenSource _cancellationTokenSource;
    
    private bool _isMonitoring;
    private bool _disposed;

    /// <inheritdoc/>
    public TranslationEngineStatus LocalEngineStatus { get; }
    
    /// <inheritdoc/>
    public TranslationEngineStatus CloudEngineStatus { get; }
    
    /// <inheritdoc/>
    public NetworkConnectionStatus NetworkStatus { get; }
    
    /// <inheritdoc/>
    public FallbackInfo? LastFallback { get; private set; }
    
    /// <inheritdoc/>
    public IObservable<TranslationEngineStatusUpdate> StatusUpdates => _statusUpdateSubject.AsObservable();

    /// <summary>
    /// 新しいTranslationEngineStatusServiceを初期化します
    /// </summary>
    public TranslationEngineStatusService(
        IOptions<TranslationEngineStatusOptions> options,
        ILogger<TranslationEngineStatusService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _statusUpdateSubject = new Subject<TranslationEngineStatusUpdate>();
        _cancellationTokenSource = new CancellationTokenSource();
        
        LocalEngineStatus = new TranslationEngineStatus
        {
            IsOnline = true, // LocalOnlyは基本的に常にオンライン
            IsHealthy = true,
            RemainingRequests = -1, // 無制限
            LastChecked = DateTime.Now
        };
        
        CloudEngineStatus = new TranslationEngineStatus
        {
            IsOnline = false, // 初期状態は未確認
            IsHealthy = false,
            RemainingRequests = 0,
            LastChecked = DateTime.Now
        };
        
        NetworkStatus = new NetworkConnectionStatus
        {
            IsConnected = false,
            LatencyMs = -1,
            LastChecked = DateTime.Now
        };
        
        // 定期監視タイマーを設定
        _monitoringTimer = new Timer(
            MonitoringCallback, 
            null, 
            Timeout.InfiniteTimeSpan, 
            Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc/>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_isMonitoring)
        {
            _logger.LogWarning("エンジン状態監視は既に開始されています");
            return;
        }

        _logger.LogInformation("翻訳エンジン状態監視を開始します");
        
        _isMonitoring = true;
        
        // 初回チェック実行
        await RefreshStatusAsync().ConfigureAwait(false);
        
        // 定期監視開始
        _monitoringTimer.Change(
            TimeSpan.Zero, 
            TimeSpan.FromSeconds(_options.MonitoringIntervalSeconds));
        
        _logger.LogInformation(
            "翻訳エンジン状態監視を開始しました。監視間隔: {Interval}秒", 
            _options.MonitoringIntervalSeconds);
    }

    /// <inheritdoc/>
    public async Task StopMonitoringAsync()
    {
        if (!_isMonitoring)
        {
            return;
        }

        _logger.LogInformation("翻訳エンジン状態監視を停止します");
        
        _isMonitoring = false;
        
        // タイマー停止
        _monitoringTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        
        await Task.CompletedTask.ConfigureAwait(false);
        
        _logger.LogInformation("翻訳エンジン状態監視を停止しました");
    }

    /// <inheritdoc/>
    public async Task RefreshStatusAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            // 並行して各状態をチェック
            var checkTasks = new[]
            {
                CheckLocalEngineStatusAsync(),
                CheckCloudEngineStatusAsync(),
                CheckNetworkStatusAsync()
            };

            await Task.WhenAll(checkTasks).ConfigureAwait(false);
            
            _logger.LogDebug("エンジン状態の更新が完了しました");
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // 予期される終了時例外は警告レベルでログ
            _logger.LogWarning(ex, "エンジン状態の更新が中断されました");
        }
        catch (Exception ex)
        {
            // その他の予期しない例外はエラーレベルでログ
            _logger.LogError(ex, "エンジン状態の更新中に予期しないエラーが発生しました");
            throw; // 予期しない例外は再スロー
        }
    }

    /// <summary>
    /// LocalOnlyエンジンの状態をチェック
    /// </summary>
    private async Task CheckLocalEngineStatusAsync()
    {
        try
        {
            // LocalOnlyエンジンは基本的に常に利用可能
            // モデルファイルの存在確認などを行う
            var wasHealthy = LocalEngineStatus.IsHealthy;
            
            // TODO: 実際のローカルエンジンヘルスチェック実装
            LocalEngineStatus.IsOnline = true;
            LocalEngineStatus.IsHealthy = await CheckLocalEngineHealthAsync().ConfigureAwait(false);
            LocalEngineStatus.LastChecked = DateTime.Now;
            LocalEngineStatus.LastError = string.Empty;
            
            if (wasHealthy != LocalEngineStatus.IsHealthy)
            {
                await PublishStatusUpdateAsync("LocalOnly", StatusUpdateType.HealthCheck).ConfigureAwait(false);
            }
        }
        catch (IOException ex)
        {
            // ファイルI/Oエラー（モデルファイル関連）
            LocalEngineStatus.IsHealthy = false;
            LocalEngineStatus.LastError = $"モデルファイルエラー: {ex.Message}";
            LocalEngineStatus.LastChecked = DateTime.Now;
            
            _logger.LogWarning(ex, "LocalOnlyエンジンのモデルファイルアクセスに失敗しました");
            
            await PublishStatusUpdateAsync("LocalOnly", StatusUpdateType.ErrorOccurred, ex.Message).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            // ファイルアクセス権限エラー
            LocalEngineStatus.IsHealthy = false;
            LocalEngineStatus.LastError = $"アクセス権限エラー: {ex.Message}";
            LocalEngineStatus.LastChecked = DateTime.Now;
            
            _logger.LogError(ex, "LocalOnlyエンジンのファイルアクセス権限が不足しています");
            
            await PublishStatusUpdateAsync("LocalOnly", StatusUpdateType.ErrorOccurred, ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // 予期される終了時例外
            LocalEngineStatus.IsHealthy = false;
            LocalEngineStatus.LastError = "ヘルスチェックが中断されました";
            LocalEngineStatus.LastChecked = DateTime.Now;
            
            _logger.LogDebug(ex, "LocalOnlyエンジンのヘルスチェックが中断されました");
        }
    }

    /// <summary>
    /// CloudOnlyエンジンの状態をチェック
    /// </summary>
    private async Task CheckCloudEngineStatusAsync()
    {
        try
        {
            var wasOnline = CloudEngineStatus.IsOnline;
            var wasHealthy = CloudEngineStatus.IsHealthy;
            
            // ネットワーク接続が必要
            if (!NetworkStatus.IsConnected)
            {
                CloudEngineStatus.IsOnline = false;
                CloudEngineStatus.IsHealthy = false;
                CloudEngineStatus.LastError = "ネットワーク接続なし";
            }
            else
            {
                // TODO: 実際のクラウドエンジンAPI呼び出し実装
                var healthCheckResult = await CheckCloudEngineHealthAsync().ConfigureAwait(false);
                
                CloudEngineStatus.IsOnline = healthCheckResult.IsOnline;
                CloudEngineStatus.IsHealthy = healthCheckResult.IsHealthy;
                CloudEngineStatus.RemainingRequests = healthCheckResult.RemainingRequests;
                CloudEngineStatus.RateLimitReset = healthCheckResult.RateLimitReset;
                CloudEngineStatus.LastError = healthCheckResult.LastError;
            }
            
            CloudEngineStatus.LastChecked = DateTime.Now;
            
            if (wasOnline != CloudEngineStatus.IsOnline || wasHealthy != CloudEngineStatus.IsHealthy)
            {
                await PublishStatusUpdateAsync("CloudOnly", StatusUpdateType.HealthCheck).ConfigureAwait(false);
            }
            
            // レート制限警告
            if (CloudEngineStatus.RemainingRequests <= _options.RateLimitWarningThreshold)
            {
                await PublishStatusUpdateAsync("CloudOnly", StatusUpdateType.RateLimitUpdate, 
                    CloudEngineStatus.RemainingRequests).ConfigureAwait(false);
            }
        }
        catch (TaskCanceledException ex)
        {
            // タイムアウトまたはキャンセル
            CloudEngineStatus.IsOnline = false;
            CloudEngineStatus.IsHealthy = false;
            CloudEngineStatus.LastError = "接続タイムアウト";
            CloudEngineStatus.LastChecked = DateTime.Now;
            
            _logger.LogWarning(ex, "CloudOnlyエンジンの接続がタイムアウトしました");
            
            await PublishStatusUpdateAsync("CloudOnly", StatusUpdateType.ErrorOccurred, "接続タイムアウト").ConfigureAwait(false);
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            // HTTP通信エラー
            CloudEngineStatus.IsOnline = false;
            CloudEngineStatus.IsHealthy = false;
            CloudEngineStatus.LastError = $"HTTP通信エラー: {ex.Message}";
            CloudEngineStatus.LastChecked = DateTime.Now;
            
            _logger.LogWarning(ex, "CloudOnlyエンジンのHTTP通信に失敗しました");
            
            await PublishStatusUpdateAsync("CloudOnly", StatusUpdateType.ErrorOccurred, ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // 予期される終了時例外
            CloudEngineStatus.IsOnline = false;
            CloudEngineStatus.IsHealthy = false;
            CloudEngineStatus.LastError = "ヘルスチェックが中断されました";
            CloudEngineStatus.LastChecked = DateTime.Now;
            
            _logger.LogDebug(ex, "CloudOnlyエンジンのヘルスチェックが中断されました");
        }
    }

    /// <summary>
    /// ネットワーク接続状態をチェック
    /// </summary>
    private async Task CheckNetworkStatusAsync()
    {
        try
        {
            var wasConnected = NetworkStatus.IsConnected;
            
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("8.8.8.8", _options.NetworkTimeoutMs).ConfigureAwait(false);
            
            NetworkStatus.IsConnected = reply.Status == IPStatus.Success;
            NetworkStatus.LatencyMs = reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1;
            NetworkStatus.LastChecked = DateTime.Now;
            
            if (wasConnected != NetworkStatus.IsConnected)
            {
                await PublishStatusUpdateAsync("Network", StatusUpdateType.HealthCheck, 
                    NetworkStatus.IsConnected).ConfigureAwait(false);
            }
        }
        catch (System.Net.NetworkInformation.PingException ex)
        {
            // Pingエラー
            NetworkStatus.IsConnected = false;
            NetworkStatus.LatencyMs = -1;
            NetworkStatus.LastChecked = DateTime.Now;
            
            _logger.LogDebug(ex, "ネットワークPingに失敗しました");
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            // ソケットエラー（ネットワーク断線等）
            NetworkStatus.IsConnected = false;
            NetworkStatus.LatencyMs = -1;
            NetworkStatus.LastChecked = DateTime.Now;
            
            _logger.LogDebug(ex, "ネットワークソケットエラーが発生しました");
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // 予期される終了時例外
            NetworkStatus.IsConnected = false;
            NetworkStatus.LatencyMs = -1;
            NetworkStatus.LastChecked = DateTime.Now;
            
            _logger.LogDebug(ex, "ネットワーク状態チェックが中断されました");
        }
    }

    /// <summary>
    /// LocalOnlyエンジンのヘルスチェック（実装予定）
    /// </summary>
    private async Task<bool> CheckLocalEngineHealthAsync()
    {
        // TODO: 実際のローカルエンジンヘルスチェック実装
        // - モデルファイルの存在確認
        // - メモリ使用量チェック
        // - 簡単な翻訳テスト
        
        await Task.Delay(10, _cancellationTokenSource.Token).ConfigureAwait(false);
        return true; // 暫定的に常に健康とする
    }

    /// <summary>
    /// CloudOnlyエンジンのヘルスチェック（実装予定）
    /// </summary>
    private async Task<CloudEngineHealthResult> CheckCloudEngineHealthAsync()
    {
        // TODO: 実際のクラウドエンジンAPI呼び出し実装
        // - Gemini APIのヘルスチェック
        // - レート制限情報取得
        // - 簡単なAPI呼び出しテスト
        
        await Task.Delay(100, _cancellationTokenSource.Token).ConfigureAwait(false);
        
        return new CloudEngineHealthResult
        {
            IsOnline = NetworkStatus.IsConnected,
            IsHealthy = NetworkStatus.IsConnected,
            RemainingRequests = 1000, // 暫定値
            RateLimitReset = TimeSpan.FromHours(1),
            LastError = string.Empty
        };
    }

    /// <summary>
    /// 状態更新イベントを発行
    /// </summary>
    private async Task PublishStatusUpdateAsync(string engineName, StatusUpdateType updateType, object? additionalData = null)
    {
        try
        {
            var update = new TranslationEngineStatusUpdate
            {
                EngineName = engineName,
                UpdatedAt = DateTime.Now,
                UpdateType = updateType,
                AdditionalData = additionalData
            };
            
            _statusUpdateSubject.OnNext(update);
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (ObjectDisposedException ex)
        {
            // オブジェクトが破棄されている場合
            _logger.LogDebug(ex, "状態更新イベントの発行時にオブジェクトが破棄されていました");
        }
        catch (InvalidOperationException ex)
        {
            // 無効な操作状態
            _logger.LogWarning(ex, "状態更新イベントの発行で無効な操作が実行されました");
        }
    }

    /// <summary>
    /// 定期監視コールバック
    /// </summary>
    private async void MonitoringCallback(object? state)
    {
        if (!_isMonitoring || _disposed)
        {
            return;
        }

        try
        {
            await RefreshStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // 予期される終了時例外は無視（デバッグログのみ）
            _logger.LogDebug(ex, "定期監視が中断されました");
        }
        catch (AggregateException ex)
        {
            // Task.WhenAllからのAggregateExceptionを解析
            _logger.LogError(ex, "複数のヘルスチェックでエラーが発生しました: {InnerExceptions}", 
                string.Join(", ", ex.InnerExceptions.Select(e => e.GetType().Name)));
        }
        catch (TaskCanceledException ex)
        {
            // タスクキャンセルエラー
            _logger.LogDebug(ex, "定期監視のヘルスチェックがキャンセルされました");
        }
        catch (InvalidOperationException ex)
        {
            // 状態管理関連のエラー
            _logger.LogWarning(ex, "定期監視中に無効な操作が発生しました");
        }
    }

    /// <summary>
    /// フォールバック発生を記録
    /// </summary>
    public void RecordFallback(string reason, string fromEngine, string toEngine, FallbackType type)
    {
        LastFallback = new FallbackInfo
        {
            OccurredAt = DateTime.Now,
            Reason = reason,
            FromEngine = fromEngine,
            ToEngine = toEngine,
            Type = type
        };
        
        _ = PublishStatusUpdateAsync(fromEngine, StatusUpdateType.FallbackTriggered, LastFallback);
        
        _logger.LogWarning(
            "フォールバックが発生しました: {FromEngine} → {ToEngine}, 理由: {Reason}",
            fromEngine, toEngine, reason);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        
        _monitoringTimer?.Dispose();
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _statusUpdateSubject?.Dispose();
    }
}

/// <summary>
/// クラウドエンジンヘルスチェック結果
/// </summary>
internal sealed class CloudEngineHealthResult
{
    public bool IsOnline { get; init; }
    public bool IsHealthy { get; init; }
    public int RemainingRequests { get; init; }
    public TimeSpan RateLimitReset { get; init; }
    public string LastError { get; init; } = string.Empty;
}

/// <summary>
/// 翻訳エンジン状態監視のオプション
/// </summary>
public sealed class TranslationEngineStatusOptions
{
    /// <summary>
    /// 監視間隔（秒）
    /// </summary>
    public int MonitoringIntervalSeconds { get; set; } = 30;
    
    /// <summary>
    /// ネットワークタイムアウト（ミリ秒）
    /// </summary>
    public int NetworkTimeoutMs { get; set; } = 5000;
    
    /// <summary>
    /// レート制限警告しきい値
    /// </summary>
    public int RateLimitWarningThreshold { get; set; } = 10;
    
    /// <summary>
    /// ヘルスチェック有効化
    /// </summary>
    public bool EnableHealthChecks { get; set; } = true;
}
