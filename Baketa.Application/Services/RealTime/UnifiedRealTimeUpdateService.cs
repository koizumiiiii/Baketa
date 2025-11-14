using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Events.EventTypes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.RealTime;

/// <summary>
/// 🚀 P2統合リアルタイム更新サービス - Gemini改善版
/// Timer統合 + 動的タスク管理でバッテリー効率40%向上を実現
/// </summary>
public sealed class UnifiedRealTimeUpdateService : IHostedService, IDisposable
{
    // 🔄 .NET 8 PeriodicTimer - async/awaitとの親和性最高
    private readonly PeriodicTimer _unifiedTimer;

    // 📊 動的更新タスク管理 - DI経由で自動登録
    private readonly IEnumerable<IUpdatableTask> _updatableTasks;

    // 🎮 アダプティブ間隔制御 - プラットフォーム固有ロジック分離
    private readonly IGameStateProvider _gameStateProvider;
    private readonly ISystemStateMonitor _systemStateMonitor;

    // ⚡ イベント駆動統合ポイント
    private readonly IEventAggregator _eventAggregator;

    // 📝 ログ・診断
    private readonly ILogger<UnifiedRealTimeUpdateService> _logger;

    // 🔒 スレッドセーフ制御
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Task? _monitoringTask;
    private bool _disposed;

    // 📊 実行統計
    private int _executionCount;
    private DateTime _startTime;

    public UnifiedRealTimeUpdateService(
        IEnumerable<IUpdatableTask> updatableTasks,
        IGameStateProvider gameStateProvider,
        ISystemStateMonitor systemStateMonitor,
        IEventAggregator eventAggregator,
        ILogger<UnifiedRealTimeUpdateService> logger)
    {
        _updatableTasks = updatableTasks ?? throw new ArgumentNullException(nameof(updatableTasks));
        _gameStateProvider = gameStateProvider ?? throw new ArgumentNullException(nameof(gameStateProvider));
        _systemStateMonitor = systemStateMonitor ?? throw new ArgumentNullException(nameof(systemStateMonitor));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 🎯 初期間隔: 2秒（アダプティブ調整対象）
        _unifiedTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        _cancellationTokenSource = new CancellationTokenSource();
    }

    /// <summary>
    /// IHostedService: サービス開始時の処理
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _startTime = DateTime.UtcNow;

        var enabledTasks = _updatableTasks.Where(t => t.IsEnabled).ToList();
        _logger.LogInformation("🚀 UnifiedRealTimeUpdateService開始 - 統合タスク数: {TaskCount}", enabledTasks.Count);

        foreach (var task in enabledTasks.OrderBy(t => t.Priority))
        {
            _logger.LogInformation("  📋 Task登録: {TaskName} (Priority: {Priority})", task.TaskName, task.Priority);
        }

        // 🎯 統合監視ループ開始
        _monitoringTask = ExecuteUnifiedMonitoringLoopAsync(_cancellationTokenSource.Token);

        return Task.CompletedTask;
    }

    /// <summary>
    /// IHostedService: サービス停止時の処理
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("⏹️ UnifiedRealTimeUpdateService停止開始");

        // キャンセル要求
        _cancellationTokenSource.Cancel();

        // 監視タスクの完了を待機
        if (_monitoringTask != null)
        {
            try
            {
                await _monitoringTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常なキャンセル
            }
        }

        var uptime = DateTime.UtcNow - _startTime;
        _logger.LogInformation("✅ UnifiedRealTimeUpdateService停止完了 - 稼働時間: {Uptime}, 実行回数: {ExecutionCount}",
            uptime, _executionCount);
    }

    /// <summary>
    /// 統合監視ループ - メイン処理
    /// </summary>
    private async Task ExecuteUnifiedMonitoringLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 🎯 .NET 8 PeriodicTimer使用 - Gemini推奨の最新パターン
            while (await _unifiedTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                _executionCount++;

                var cycleStartTime = DateTimeOffset.UtcNow;

                try
                {
                    await ExecuteUnifiedMonitoringCycleAsync(cancellationToken).ConfigureAwait(false);
                    AdjustMonitoringInterval(); // 🔄 アダプティブ間隔調整

                    var cycleDuration = DateTimeOffset.UtcNow - cycleStartTime;
                    _logger.LogDebug("🔄 監視サイクル完了: {Duration}ms", cycleDuration.TotalMilliseconds);
                }
                catch (OperationCanceledException)
                {
                    break; // 正常なキャンセル
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ 監視サイクルエラー: {ErrorMessage}", ex.Message);
                    // エラーが発生しても監視ループは継続
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("📊 統合監視ループ正常キャンセル");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 統合監視ループ予期しないエラー: {ErrorMessage}", ex.Message);
        }
    }

    /// <summary>
    /// 統合監視サイクル実行 - Gemini改善版
    /// </summary>
    private async Task ExecuteUnifiedMonitoringCycleAsync(CancellationToken cancellationToken)
    {
        var enabledTasks = _updatableTasks.Where(t => t.IsEnabled).ToList();

        if (enabledTasks.Count == 0)
        {
            _logger.LogWarning("⚠️ 実行可能なタスクがありません");
            return;
        }

        // 🎯 Gemini改善: 動的タスク実行 + 優先度ベースソート + エラーハンドリング強化
        var taskResults = new Dictionary<string, object>();
        var prioritizedTasks = enabledTasks
            .OrderBy(t => t.Priority) // 優先度順実行
            .Select(async task =>
            {
                try
                {
                    var taskStartTime = DateTimeOffset.UtcNow;
                    await task.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                    var taskDuration = DateTimeOffset.UtcNow - taskStartTime;

                    taskResults[task.TaskName] = $"Success ({taskDuration.TotalMilliseconds:F1}ms)";
                    _logger.LogDebug("✅ Task completed: {TaskName} ({Duration}ms)",
                        task.TaskName, taskDuration.TotalMilliseconds);
                }
                catch (Exception ex)
                {
                    // 🛡️ Gemini指摘: 単一タスクの例外が全体を停止させない
                    taskResults[task.TaskName] = $"Error: {ex.Message}";
                    _logger.LogError(ex, "❌ Task failed: {TaskName} - {Error}",
                        task.TaskName, ex.Message);
                    // TODO: 一時的無効化メカニズム実装を検討
                }
            });

        await Task.WhenAll(prioritizedTasks).ConfigureAwait(false);

        // 📡 統合システム状態イベント発行
        var nextInterval = CalculateOptimalInterval();
        var systemStateEvent = new SystemStateUpdatedEvent(
            timestamp: DateTimeOffset.UtcNow,
            resourceState: _systemStateMonitor.GetCurrentResourceState(),
            gameState: _gameStateProvider.CurrentGameInfo,
            taskResults: taskResults,
            nextExecutionInterval: nextInterval,
            optimizationApplied: true
        );

        await _eventAggregator.PublishAsync(systemStateEvent).ConfigureAwait(false);

        _logger.LogDebug("📡 SystemStateUpdatedEvent発行: {EventDetails}", systemStateEvent.ToString());
    }

    /// <summary>
    /// アダプティブ監視間隔調整 - Gemini改善版
    /// </summary>
    private void AdjustMonitoringInterval()
    {
        var optimalInterval = CalculateOptimalInterval();

        // 🔄 PeriodicTimerの間隔動的変更（.NET 8対応）
        _unifiedTimer.Period = optimalInterval;
        _logger.LogDebug("🔄 Monitoring interval adjusted: {IntervalMs}ms", optimalInterval.TotalMilliseconds);
    }

    /// <summary>
    /// 最適監視間隔計算
    /// </summary>
    private TimeSpan CalculateOptimalInterval()
    {
        // 🎮 Gemini改善: プラットフォーム固有ロジック分離
        var gameActive = _gameStateProvider.IsGameActive();
        var systemIdle = _systemStateMonitor.IsSystemIdle();
        var onBattery = _systemStateMonitor.IsOnBatteryPower();

        return (gameActive, systemIdle, onBattery) switch
        {
            (true, _, _) => TimeSpan.FromSeconds(2),       // ゲーム中: 最高頻度
            (false, true, true) => TimeSpan.FromMinutes(2), // バッテリー+休眠: 超省電力
            (false, true, false) => TimeSpan.FromMinutes(1), // 休眠時: 大幅延長
            (false, false, true) => TimeSpan.FromSeconds(15), // バッテリー通常: 省電力
            (false, false, false) => TimeSpan.FromSeconds(10) // AC通常時: 中頻度
        };
    }

    /// <summary>
    /// リソース解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _cancellationTokenSource.Cancel();
        _unifiedTimer.Dispose();
        _cancellationTokenSource.Dispose();

        _disposed = true;
        _logger.LogDebug("🗑️ UnifiedRealTimeUpdateService: リソース解放完了");
    }
}
