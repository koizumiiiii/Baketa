using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// サーバー状態に応じて動的にヘルスチェックタイムアウトを調整するマネージャー
/// Phase 1: False negative防止機能
/// </summary>
public sealed class DynamicHealthCheckManager : IEventProcessor<PythonServerStatusChangedEvent>, IDisposable
{
    private readonly ILogger<DynamicHealthCheckManager> _logger;

    private ServerHealthState _currentState = ServerHealthState.Starting;
    private DateTime _lastStateChange = DateTime.UtcNow;
    private bool _disposed;

    public DynamicHealthCheckManager(ILogger<DynamicHealthCheckManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("🩺 DynamicHealthCheckManager初期化");
    }

    /// <summary>
    /// 現在のサーバー状態に応じた適切なヘルスチェックタイムアウトを取得
    /// </summary>
    public TimeSpan GetRecommendedHealthCheckTimeout()
    {
        var timeout = _currentState switch
        {
            ServerHealthState.Starting => TimeSpan.FromSeconds(180), // 起動時: 180秒（初回モデル読み込み用）
            ServerHealthState.Ready => TimeSpan.FromSeconds(30),     // 通常時: 30秒（応答性監視）
            ServerHealthState.Unhealthy => TimeSpan.FromSeconds(60), // 異常時: 60秒（回復待ち）
            ServerHealthState.Failed => TimeSpan.FromSeconds(30),    // 失敗時: 30秒（再起動判定）
            _ => TimeSpan.FromSeconds(30)
        };

        _logger.LogDebug("⏱️ ヘルスチェックタイムアウト: {Timeout}秒 (状態: {State})",
            timeout.TotalSeconds, _currentState);

        return timeout;
    }

    /// <summary>
    /// 現在のサーバー状態を取得
    /// </summary>
    public ServerHealthState CurrentState => _currentState;

    /// <summary>
    /// 状態変更からの経過時間を取得
    /// </summary>
    public TimeSpan TimeSinceLastStateChange => DateTime.UtcNow - _lastStateChange;

    /// <summary>
    /// 起動時専用の延長タイムアウト判定
    /// </summary>
    public bool ShouldUseExtendedStartupTimeout()
    {
        return _currentState == ServerHealthState.Starting &&
               TimeSinceLastStateChange < TimeSpan.FromMinutes(5); // 5分以内は起動時扱い
    }

    /// <summary>
    /// サーバー状態に基づいたヘルスチェック戦略を取得
    /// </summary>
    public HealthCheckStrategy GetHealthCheckStrategy()
    {
        return _currentState switch
        {
            ServerHealthState.Starting => new HealthCheckStrategy
            {
                Timeout = TimeSpan.FromSeconds(180),
                RetryCount = 2,
                RetryInterval = TimeSpan.FromSeconds(10),
                RequireWarmupPeriod = true,
                WarmupDuration = TimeSpan.FromSeconds(5)
            },
            ServerHealthState.Ready => new HealthCheckStrategy
            {
                Timeout = TimeSpan.FromSeconds(30),
                RetryCount = 1,
                RetryInterval = TimeSpan.FromSeconds(2),
                RequireWarmupPeriod = false
            },
            ServerHealthState.Unhealthy => new HealthCheckStrategy
            {
                Timeout = TimeSpan.FromSeconds(60),
                RetryCount = 3,
                RetryInterval = TimeSpan.FromSeconds(5),
                RequireWarmupPeriod = true,
                WarmupDuration = TimeSpan.FromSeconds(3)
            },
            ServerHealthState.Failed => new HealthCheckStrategy
            {
                Timeout = TimeSpan.FromSeconds(30),
                RetryCount = 0, // 失敗状態では即座に再起動判定
                RetryInterval = TimeSpan.Zero,
                RequireWarmupPeriod = false
            },
            _ => HealthCheckStrategy.CreateDefault()
        };
    }

    #region IEventProcessor<PythonServerStatusChangedEvent> Implementation

    /// <summary>
    /// サーバー状態変更イベントの処理
    /// </summary>
    public async Task HandleAsync(PythonServerStatusChangedEvent eventData)
    {
        try
        {
            var previousState = _currentState;

            // イベントデータに基づいて新しい状態を決定
            var newState = DetermineServerState(eventData);

            if (newState != previousState)
            {
                _currentState = newState;
                _lastStateChange = DateTime.UtcNow;

                _logger.LogInformation("🔄 サーバー状態変更: {PreviousState} → {NewState} (Port: {Port})",
                    previousState, newState, eventData.ServerPort);

                var strategy = GetHealthCheckStrategy();
                _logger.LogDebug("📋 新ヘルスチェック戦略: Timeout={Timeout}s, Retry={Retry}, Warmup={Warmup}",
                    strategy.Timeout.TotalSeconds, strategy.RetryCount, strategy.RequireWarmupPeriod);
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ サーバー状態変更イベント処理エラー");
        }
    }

    /// <summary>
    /// イベント処理優先度
    /// </summary>
    public int Priority => 200; // HealthCheck関連なので高優先度

    /// <summary>
    /// 同期実行フラグ
    /// </summary>
    public bool SynchronousExecution => false;

    #endregion

    /// <summary>
    /// イベントデータからサーバー状態を決定
    /// </summary>
    private static ServerHealthState DetermineServerState(PythonServerStatusChangedEvent eventData)
    {
        if (eventData.IsServerReady)
        {
            return ServerHealthState.Ready;
        }

        // エラーメッセージの内容で状態を判定
        if (eventData.StatusMessage.Contains("エラー") || eventData.StatusMessage.Contains("失敗"))
        {
            return ServerHealthState.Failed;
        }

        if (eventData.StatusMessage.Contains("初期化中") || eventData.StatusMessage.Contains("起動中"))
        {
            return ServerHealthState.Starting;
        }

        return ServerHealthState.Unhealthy;
    }

    /// <summary>
    /// リソース解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogDebug("🗑️ DynamicHealthCheckManager リソース解放");
        _disposed = true;
    }
}

/// <summary>
/// サーバーの健康状態
/// </summary>
public enum ServerHealthState
{
    /// <summary>起動中</summary>
    Starting,

    /// <summary>準備完了</summary>
    Ready,

    /// <summary>不健全（一時的な問題）</summary>
    Unhealthy,

    /// <summary>失敗（再起動が必要）</summary>
    Failed
}

/// <summary>
/// ヘルスチェック戦略
/// </summary>
public sealed record HealthCheckStrategy
{
    /// <summary>タイムアウト時間</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>リトライ回数</summary>
    public int RetryCount { get; init; } = 1;

    /// <summary>リトライ間隔</summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>ウォームアップ期間が必要か</summary>
    public bool RequireWarmupPeriod { get; init; } = false;

    /// <summary>ウォームアップ時間</summary>
    public TimeSpan WarmupDuration { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>デフォルト戦略を作成</summary>
    public static HealthCheckStrategy CreateDefault() => new();
}
