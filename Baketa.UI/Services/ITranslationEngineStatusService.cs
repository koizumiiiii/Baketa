using System;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;

namespace Baketa.UI.Services;

/// <summary>
/// 翻訳エンジンの状態を監視するサービスのインターフェース
/// </summary>
public interface ITranslationEngineStatusService
{
    /// <summary>
    /// LocalOnlyエンジンの状態
    /// </summary>
    TranslationEngineStatus LocalEngineStatus { get; }
    
    /// <summary>
    /// CloudOnlyエンジンの状態
    /// </summary>
    TranslationEngineStatus CloudEngineStatus { get; }
    
    /// <summary>
    /// ネットワーク接続状態
    /// </summary>
    NetworkConnectionStatus NetworkStatus { get; }
    
    /// <summary>
    /// 最後のフォールバック情報
    /// </summary>
    FallbackInfo? LastFallback { get; }
    
    /// <summary>
    /// 状態更新イベント
    /// </summary>
    IObservable<TranslationEngineStatusUpdate> StatusUpdates { get; }
    
    /// <summary>
    /// 状態監視を開始
    /// </summary>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 状態監視を停止
    /// </summary>
    Task StopMonitoringAsync();
    
    /// <summary>
    /// 手動で状態を更新
    /// </summary>
    Task RefreshStatusAsync();
}

/// <summary>
/// 翻訳エンジンの状態
/// </summary>
public sealed class TranslationEngineStatus : ReactiveObject
{
    private bool _isOnline;
    private bool _isHealthy;
    private int _remainingRequests;
    private TimeSpan _rateLimitReset;
    private string _lastError = string.Empty;
    private DateTime _lastChecked;
    
    /// <summary>
    /// エンジンがオンラインかどうか
    /// </summary>
    public bool IsOnline
    {
        get => _isOnline;
        set => this.RaiseAndSetIfChanged(ref _isOnline, value);
    }
    
    /// <summary>
    /// エンジンが正常に動作しているかどうか
    /// </summary>
    public bool IsHealthy
    {
        get => _isHealthy;
        set => this.RaiseAndSetIfChanged(ref _isHealthy, value);
    }
    
    /// <summary>
    /// 残りリクエスト数（CloudOnlyのみ）
    /// </summary>
    public int RemainingRequests
    {
        get => _remainingRequests;
        set => this.RaiseAndSetIfChanged(ref _remainingRequests, value);
    }
    
    /// <summary>
    /// レート制限リセット時刻まで
    /// </summary>
    public TimeSpan RateLimitReset
    {
        get => _rateLimitReset;
        set => this.RaiseAndSetIfChanged(ref _rateLimitReset, value);
    }
    
    /// <summary>
    /// 最後のエラーメッセージ
    /// </summary>
    public string LastError
    {
        get => _lastError;
        set => this.RaiseAndSetIfChanged(ref _lastError, value);
    }
    
    /// <summary>
    /// 最後にチェックした時刻
    /// </summary>
    public DateTime LastChecked
    {
        get => _lastChecked;
        set => this.RaiseAndSetIfChanged(ref _lastChecked, value);
    }
    
    /// <summary>
    /// 総合的な状態
    /// </summary>
    public EngineHealthStatus OverallStatus => 
        !IsOnline ? EngineHealthStatus.Offline :
        !IsHealthy ? EngineHealthStatus.Error :
        RemainingRequests <= 10 ? EngineHealthStatus.Warning :
        EngineHealthStatus.Healthy;
}

/// <summary>
/// ネットワーク接続状態
/// </summary>
public sealed class NetworkConnectionStatus : ReactiveObject
{
    private bool _isConnected;
    private int _latencyMs;
    private DateTime _lastChecked;
    
    /// <summary>
    /// インターネットに接続されているかどうか
    /// </summary>
    public bool IsConnected
    {
        get => _isConnected;
        set => this.RaiseAndSetIfChanged(ref _isConnected, value);
    }
    
    /// <summary>
    /// ネットワークレイテンシ（ミリ秒）
    /// </summary>
    public int LatencyMs
    {
        get => _latencyMs;
        set => this.RaiseAndSetIfChanged(ref _latencyMs, value);
    }
    
    /// <summary>
    /// 最後にチェックした時刻
    /// </summary>
    public DateTime LastChecked
    {
        get => _lastChecked;
        set => this.RaiseAndSetIfChanged(ref _lastChecked, value);
    }
}

/// <summary>
/// フォールバック情報
/// </summary>
public sealed class FallbackInfo
{
    /// <summary>
    /// フォールバック発生時刻
    /// </summary>
    public DateTime OccurredAt { get; init; }
    
    /// <summary>
    /// フォールバック理由
    /// </summary>
    public string Reason { get; init; } = string.Empty;
    
    /// <summary>
    /// 元のエンジン
    /// </summary>
    public string FromEngine { get; init; } = string.Empty;
    
    /// <summary>
    /// フォールバック先エンジン
    /// </summary>
    public string ToEngine { get; init; } = string.Empty;
    
    /// <summary>
    /// フォールバック種別
    /// </summary>
    public FallbackType Type { get; init; }
}

/// <summary>
/// 状態更新情報
/// </summary>
public sealed class TranslationEngineStatusUpdate
{
    /// <summary>
    /// 更新されたエンジン名
    /// </summary>
    public string EngineName { get; init; } = string.Empty;
    
    /// <summary>
    /// 更新時刻
    /// </summary>
    public DateTime UpdatedAt { get; init; }
    
    /// <summary>
    /// 更新種別
    /// </summary>
    public StatusUpdateType UpdateType { get; init; }
    
    /// <summary>
    /// 追加情報
    /// </summary>
    public object? AdditionalData { get; init; }
}

/// <summary>
/// エンジン健康状態
/// </summary>
public enum EngineHealthStatus
{
    /// <summary>健康</summary>
    Healthy,
    /// <summary>警告</summary>
    Warning,
    /// <summary>エラー</summary>
    Error,
    /// <summary>オフライン</summary>
    Offline
}

/// <summary>
/// フォールバック種別
/// </summary>
public enum FallbackType
{
    /// <summary>レート制限</summary>
    RateLimit,
    /// <summary>ネットワークエラー</summary>
    NetworkError,
    /// <summary>APIエラー</summary>
    ApiError,
    /// <summary>タイムアウト</summary>
    Timeout
}

/// <summary>
/// 状態更新種別
/// </summary>
public enum StatusUpdateType
{
    /// <summary>ヘルスチェック</summary>
    HealthCheck,
    /// <summary>レート制限更新</summary>
    RateLimitUpdate,
    /// <summary>エラー発生</summary>
    ErrorOccurred,
    /// <summary>復旧</summary>
    Recovery,
    /// <summary>フォールバック発生</summary>
    FallbackTriggered
}
