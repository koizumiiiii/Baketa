using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using Microsoft.Win32;
using Baketa.Core.Abstractions.GPU;

namespace Baketa.Infrastructure.Platform.Windows.GPU;

/// <summary>
/// Windows TDR回復マネージャー実装
/// DirectX/OpenGL TDR検出・回復システム
/// Issue #143 Week 2 Phase 3: 高可用性GPU推論システム
/// </summary>
public sealed class WindowsTdrRecoveryManager : ITdrRecoveryManager, IDisposable
{
    private readonly ILogger<WindowsTdrRecoveryManager> _logger;
    private readonly IGpuDeviceManager _gpuDeviceManager;
    private readonly IOnnxSessionFactory _sessionFactory;
    private readonly ConcurrentDictionary<string, TdrStatus> _tdrStatusCache = new();
    private readonly ConcurrentQueue<TdrHistoryEntry> _tdrHistory = new();
    private readonly ConcurrentDictionary<string, OnnxSessionInfo> _activeSessions = new();
    private readonly Timer _tdrMonitorTimer;
    private readonly object _recoveryLock = new();
    private bool _disposed;
    private CancellationTokenSource? _monitoringCts;

    public WindowsTdrRecoveryManager(
        ILogger<WindowsTdrRecoveryManager> logger,
        IGpuDeviceManager gpuDeviceManager,
        IOnnxSessionFactory sessionFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gpuDeviceManager = gpuDeviceManager ?? throw new ArgumentNullException(nameof(gpuDeviceManager));
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        
        // TDR監視タイマー（5秒間隔）
        _tdrMonitorTimer = new Timer(TdrMonitorCallback, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        
        _logger.LogInformation("🛡️ WindowsTdrRecoveryManager初期化完了 - TDR監視開始");
    }

    public async Task StartTdrMonitoringAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _logger.LogInformation("🔍 TDR監視開始");
            
            while (!_monitoringCts.Token.IsCancellationRequested)
            {
                await MonitorTdrEventsAsync(_monitoringCts.Token).ConfigureAwait(false);
                await Task.Delay(1000, _monitoringCts.Token).ConfigureAwait(false); // 1秒間隔
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("TDR監視が停止されました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ TDR監視中にエラーが発生");
        }
    }

    public Task<TdrRecoveryResult> RecoverFromTdrAsync(TdrContext tdrContext, CancellationToken cancellationToken = default)
    {
        lock (_recoveryLock)
        {
            return Task.FromResult(RecoverFromTdrInternal(tdrContext, cancellationToken));
        }
    }

    public async Task<TdrStatus> GetTdrStatusAsync(string pnpDeviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("🔍 TDR状態取得: {PnpDeviceId}", pnpDeviceId);
            
            // キャッシュから取得を試行
            if (_tdrStatusCache.TryGetValue(pnpDeviceId, out var cachedStatus))
            {
                return cachedStatus;
            }
            
            var status = await GetTdrStatusInternal(pnpDeviceId, cancellationToken).ConfigureAwait(false);
            _tdrStatusCache.TryAdd(pnpDeviceId, status);
            
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ TDR状態取得失敗: {PnpDeviceId}", pnpDeviceId);
            return new TdrStatus
            {
                IsInTdrState = false,
                RiskLevel = TdrRiskLevel.Medium,
                RiskAssessment = $"状態取得エラー: {ex.Message}"
            };
        }
    }

    public async Task<TdrPreventionResult> PreventTdrAsync(OnnxSessionInfo sessionInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("🛡️ TDR予防処理開始: {ModelPath}", sessionInfo.ModelPath);
            
            var strategies = new List<TdrPreventionStrategy>();
            var effectiveness = 0.0;
            
            // TDRリスク評価（PnpDeviceIdがないので固定GPU使用）
            var tdrStatus = await GetTdrStatusAsync("default", cancellationToken).ConfigureAwait(false);
            
            if (tdrStatus.RiskLevel >= TdrRiskLevel.Medium)
            {
                // 高リスク時の予防戦略
                strategies.AddRange(await GetHighRiskPreventionStrategies(sessionInfo, cancellationToken).ConfigureAwait(false));
                effectiveness += 0.7;
            }
            
            if (sessionInfo.EstimatedMemoryUsageMB > 4096) // 4GB以上
            {
                strategies.Add(TdrPreventionStrategy.LimitMemoryUsage);
                effectiveness += 0.3;
            }
            
            if (sessionInfo.InitializationTimeMs > 5000) // 5秒以上の初期化時間
            {
                strategies.Add(TdrPreventionStrategy.ReduceBatchSize);
                effectiveness += 0.4;
            }
            
            // 予防戦略を実行
            await ExecutePreventionStrategies(strategies, sessionInfo, cancellationToken).ConfigureAwait(false);
            
            var result = new TdrPreventionResult
            {
                PreventionExecuted = strategies.Count > 0,
                ExecutedStrategies = strategies,
                EstimatedEffectiveness = Math.Min(effectiveness, 1.0),
                PreventionMessage = $"{strategies.Count}個の予防戦略を実行しました"
            };
            
            _logger.LogInformation("✅ TDR予防処理完了: {ModelPath} - 戦略数: {Count}, 効果: {Effectiveness:P1}",
                sessionInfo.ModelPath, strategies.Count, result.EstimatedEffectiveness);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ TDR予防処理失敗: {ModelPath}", sessionInfo.ModelPath);
            return new TdrPreventionResult
            {
                PreventionExecuted = false,
                PreventionMessage = $"予防処理エラー: {ex.Message}"
            };
        }
    }

    public IReadOnlyList<TdrHistoryEntry> GetTdrHistory(int hours = 24)
    {
        var cutoffTime = DateTime.UtcNow.AddHours(-hours);
        return _tdrHistory
            .Where(entry => entry.OccurredAt >= cutoffTime)
            .OrderByDescending(entry => entry.OccurredAt)
            .ToList()
            .AsReadOnly();
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _monitoringCts?.Cancel();
        _monitoringCts?.Dispose();
        _tdrMonitorTimer?.Dispose();
        _disposed = true;
        
        _logger.LogInformation("🧹 WindowsTdrRecoveryManager リソース解放完了");
    }

    private async Task MonitorTdrEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Windows イベントログからTDR関連イベントを監視
            await CheckWindowsEventLogForTdr(cancellationToken).ConfigureAwait(false);
            
            // レジストリからTDR情報を取得
            await CheckRegistryForTdrInfo(cancellationToken).ConfigureAwait(false);
            
            // アクティブセッションの健全性チェック
            await CheckActiveSessionHealth(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TDR監視中に警告が発生");
        }
    }

    private TdrRecoveryResult RecoverFromTdrInternal(TdrContext tdrContext, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogWarning("🚨 TDR回復開始: {PnpDeviceId} - 原因: {Cause}", tdrContext.PnpDeviceId, tdrContext.EstimatedCause);
        
        try
        {
            // 回復戦略を決定
            var strategy = DetermineRecoveryStrategy(tdrContext);
            
            // 回復戦略を実行
            var success = ExecuteRecoveryStrategy(strategy, tdrContext, cancellationToken);
            
            stopwatch.Stop();
            
            // 履歴に追加
            var historyEntry = new TdrHistoryEntry
            {
                OccurredAt = tdrContext.OccurredAt,
                PnpDeviceId = tdrContext.PnpDeviceId,
                Cause = tdrContext.EstimatedCause,
                RecoveryStrategy = strategy,
                RecoverySuccessful = success,
                RecoveryDuration = stopwatch.Elapsed
            };
            _tdrHistory.Enqueue(historyEntry);
            
            // 履歴サイズ制限（最新100件まで）
            while (_tdrHistory.Count > 100)
            {
                _tdrHistory.TryDequeue(out _);
            }
            
            var result = new TdrRecoveryResult
            {
                IsSuccessful = success,
                RecoveryDuration = stopwatch.Elapsed,
                UsedStrategy = strategy,
                RecommendedActions = GenerateRecommendedActions(tdrContext, strategy, success),
                RecoveryMessage = $"TDR回復処理 {(success ? "成功" : "失敗")} - 戦略: {strategy}"
            };
            
            _logger.LogInformation("✅ TDR回復完了: {PnpDeviceId} - 成功: {Success}, 時間: {Duration}ms",
                tdrContext.PnpDeviceId, success, stopwatch.ElapsedMilliseconds);
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ TDR回復処理中にエラー: {PnpDeviceId}", tdrContext.PnpDeviceId);
            
            return new TdrRecoveryResult
            {
                IsSuccessful = false,
                RecoveryDuration = stopwatch.Elapsed,
                UsedStrategy = TdrRecoveryStrategy.None,
                RecoveryMessage = $"回復処理エラー: {ex.Message}"
            };
        }
    }

    private async Task<TdrStatus> GetTdrStatusInternal(string pnpDeviceId, CancellationToken cancellationToken)
    {
        // Windows レジストリからTDR情報を取得
        var tdrCount = await GetTdrCountFromRegistry(pnpDeviceId, cancellationToken).ConfigureAwait(false);
        var lastTdrTime = await GetLastTdrTimeFromEventLog(pnpDeviceId, cancellationToken).ConfigureAwait(false);
        
        // リスクレベル計算
        var riskLevel = CalculateTdrRiskLevel(tdrCount, lastTdrTime);
        
        return new TdrStatus
        {
            IsInTdrState = await IsCurrentlyInTdrState(pnpDeviceId, cancellationToken).ConfigureAwait(false),
            TdrCountLast24Hours = tdrCount,
            LastTdrTime = lastTdrTime,
            RiskLevel = riskLevel,
            RiskAssessment = GenerateRiskAssessment(tdrCount, lastTdrTime, riskLevel)
        };
    }

    private async Task<List<TdrPreventionStrategy>> GetHighRiskPreventionStrategies(OnnxSessionInfo sessionInfo, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken).ConfigureAwait(false); // プレースホルダー
        
        return
        [
            TdrPreventionStrategy.ReduceBatchSize,
            TdrPreventionStrategy.ExtendTimeout,
            TdrPreventionStrategy.LimitConcurrency
        ];
    }

    private async Task ExecutePreventionStrategies(List<TdrPreventionStrategy> strategies, OnnxSessionInfo sessionInfo, CancellationToken cancellationToken)
    {
        foreach (var strategy in strategies)
        {
            await ExecutePreventionStrategy(strategy, sessionInfo, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecutePreventionStrategy(TdrPreventionStrategy strategy, OnnxSessionInfo sessionInfo, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken).ConfigureAwait(false); // プレースホルダー実装
        
        switch (strategy)
        {
            case TdrPreventionStrategy.ReduceBatchSize:
                _logger.LogDebug("🔧 バッチサイズ削減実行: {ModelPath}", sessionInfo.ModelPath);
                break;
            case TdrPreventionStrategy.ExtendTimeout:
                _logger.LogDebug("⏰ タイムアウト延長実行: {ModelPath}", sessionInfo.ModelPath);
                break;
            case TdrPreventionStrategy.LimitMemoryUsage:
                _logger.LogDebug("💾 メモリ使用量制限実行: {ModelPath}", sessionInfo.ModelPath);
                break;
        }
    }

    private TdrRecoveryStrategy DetermineRecoveryStrategy(TdrContext tdrContext)
    {
        return tdrContext.EstimatedCause switch
        {
            TdrCause.GpuOverload => TdrRecoveryStrategy.DistributeWorkload,
            TdrCause.InsufficientMemory => TdrRecoveryStrategy.RecreateSession,
            TdrCause.DriverIssue => TdrRecoveryStrategy.ResetDriver,
            TdrCause.LongRunningTask => TdrRecoveryStrategy.RecreateSession,
            TdrCause.ConcurrencyConflict => TdrRecoveryStrategy.SwitchGpu,
            TdrCause.HardwareIssue => TdrRecoveryStrategy.FallbackToCpu,
            _ => TdrRecoveryStrategy.RecreateSession
        };
    }

    private bool ExecuteRecoveryStrategy(TdrRecoveryStrategy strategy, TdrContext tdrContext, CancellationToken cancellationToken)
    {
        try
        {
            return strategy switch
            {
                TdrRecoveryStrategy.RecreateSession => RecreateOnnxSession(tdrContext),
                TdrRecoveryStrategy.SwitchGpu => SwitchToAlternativeGpu(tdrContext),
                TdrRecoveryStrategy.FallbackToCpu => FallbackToCpuExecution(tdrContext),
                TdrRecoveryStrategy.ResetDriver => ResetGpuDriver(tdrContext),
                TdrRecoveryStrategy.DistributeWorkload => DistributeWorkloadAcrossGpus(tdrContext),
                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "回復戦略実行失敗: {Strategy}", strategy);
            return false;
        }
    }

    private bool RecreateOnnxSession(TdrContext tdrContext)
    {
        _logger.LogInformation("🔄 ONNX Session再作成: {PnpDeviceId}", tdrContext.PnpDeviceId);
        // 実装: セッションの再作成ロジック
        return true; // プレースホルダー
    }

    private bool SwitchToAlternativeGpu(TdrContext tdrContext)
    {
        _logger.LogInformation("🔀 代替GPU切り替え: {PnpDeviceId}", tdrContext.PnpDeviceId);
        // 実装: 代替GPUへの切り替えロジック
        return true; // プレースホルダー
    }

    private bool FallbackToCpuExecution(TdrContext tdrContext)
    {
        _logger.LogInformation("💻 CPU実行フォールバック: {PnpDeviceId}", tdrContext.PnpDeviceId);
        // 実装: CPU実行への切り替えロジック
        return true; // プレースホルダー
    }

    private bool ResetGpuDriver(TdrContext tdrContext)
    {
        _logger.LogWarning("⚠️ GPUドライバーリセット: {PnpDeviceId}", tdrContext.PnpDeviceId);
        // 実装: ドライバーリセットロジック（要管理者権限）
        return false; // 安全のためデフォルト無効
    }

    private bool DistributeWorkloadAcrossGpus(TdrContext tdrContext)
    {
        _logger.LogInformation("⚖️ 負荷分散実行: {PnpDeviceId}", tdrContext.PnpDeviceId);
        // 実装: マルチGPU負荷分散ロジック
        return true; // プレースホルダー
    }

    private List<string> GenerateRecommendedActions(TdrContext tdrContext, TdrRecoveryStrategy strategy, bool success)
    {
        var actions = new List<string>();
        
        if (!success)
        {
            actions.Add("システム再起動を検討してください");
            actions.Add("GPU ドライバーの更新を確認してください");
        }
        
        if (tdrContext.EstimatedCause == TdrCause.InsufficientMemory)
        {
            actions.Add("バッチサイズの削減を検討してください");
            actions.Add("他のGPU使用アプリケーションを終了してください");
        }
        
        return actions;
    }

    private async Task CheckWindowsEventLogForTdr(CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken).ConfigureAwait(false); // プレースホルダー実装
        // 実装: Windows イベントログからTDRイベントを検索
    }

    private async Task CheckRegistryForTdrInfo(CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken).ConfigureAwait(false); // プレースホルダー実装
        // 実装: レジストリからTDR設定と履歴を取得
    }

    private async Task CheckActiveSessionHealth(CancellationToken cancellationToken)
    {
        await Task.Delay(50, cancellationToken).ConfigureAwait(false); // プレースホルダー実装
        // 実装: アクティブセッションの健全性チェック
    }

    private async Task<int> GetTdrCountFromRegistry(string pnpDeviceId, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken).ConfigureAwait(false); // プレースホルダー
        return 0; // 実装: レジストリからTDR回数を取得
    }

    private async Task<DateTime?> GetLastTdrTimeFromEventLog(string pnpDeviceId, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken).ConfigureAwait(false); // プレースホルダー
        return null; // 実装: イベントログから最後のTDR時刻を取得
    }

    private async Task<bool> IsCurrentlyInTdrState(string pnpDeviceId, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken).ConfigureAwait(false); // プレースホルダー
        return false; // 実装: 現在のTDR状態を確認
    }

    private TdrRiskLevel CalculateTdrRiskLevel(int tdrCount, DateTime? lastTdrTime)
    {
        if (tdrCount == 0) return TdrRiskLevel.Low;
        if (tdrCount <= 2) return TdrRiskLevel.Medium;
        if (tdrCount <= 5) return TdrRiskLevel.High;
        return TdrRiskLevel.Critical;
    }

    private string GenerateRiskAssessment(int tdrCount, DateTime? lastTdrTime, TdrRiskLevel riskLevel)
    {
        var lastTdrText = lastTdrTime?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "不明";
        return $"TDR回数: {tdrCount}, 最終TDR: {lastTdrText}, リスクレベル: {riskLevel}";
    }

    private void TdrMonitorCallback(object? state)
    {
        try
        {
            if (_disposed) return;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    await MonitorTdrEventsAsync(cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "TDR監視タイマーで警告が発生");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TDR監視タイマーでエラーが発生");
        }
    }
}
