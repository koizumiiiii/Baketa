using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Common;
using Baketa.Core.Abstractions.Monitoring;
using Baketa.Core.Abstractions.ResourceManagement;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.ResourceManagement;

/// <summary>
/// Phase 3: 高度なハイブリッドリソース管理システム実装
/// GPU/VRAM監視、ヒステリシス制御、動的並列度調整、予測的クールダウンを統合
/// </summary>
public sealed class HybridResourceManagerEnhanced : IHybridResourceManager
{
    private readonly ILogger<HybridResourceManagerEnhanced> _logger;
    private readonly IResourceMonitor _resourceMonitor;
    private readonly HysteresisParallelismController _hysteresisController;
    private readonly IOptionsMonitor<HysteresisControlSettings> _hysteresisSettings;
    private readonly IOptionsMonitor<PredictiveControlSettings> _predictiveSettings;
    private readonly VramCapacityDetector _vramDetector;
    
    // ゲーム負荷パターン学習データベース
    private readonly Dictionary<string, GameLoadPattern> _gamePatterns = new();
    private readonly object _gamePatternLock = new();
    
    // A/Bテスト設定管理
    private string _activeConfigurationVariant = "Default";
    private readonly object _configurationLock = new();
    
    // 初期化状態管理
    private bool _isInitialized;
    private readonly object _initializationLock = new();

    public HybridResourceManagerEnhanced(
        ILogger<HybridResourceManagerEnhanced> logger,
        IResourceMonitor resourceMonitor,
        HysteresisParallelismController hysteresisController,
        IOptionsMonitor<HysteresisControlSettings> hysteresisSettings,
        IOptionsMonitor<PredictiveControlSettings> predictiveSettings,
        VramCapacityDetector vramDetector)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceMonitor = resourceMonitor ?? throw new ArgumentNullException(nameof(resourceMonitor));
        _hysteresisController = hysteresisController ?? throw new ArgumentNullException(nameof(hysteresisController));
        _hysteresisSettings = hysteresisSettings ?? throw new ArgumentNullException(nameof(hysteresisSettings));
        _predictiveSettings = predictiveSettings ?? throw new ArgumentNullException(nameof(predictiveSettings));
        _vramDetector = vramDetector ?? throw new ArgumentNullException(nameof(vramDetector));

        _logger.LogInformation("🎯 [PHASE3] HybridResourceManagerEnhanced初期化開始 - 動的VRAM検出統合");
    }

    public bool IsInitialized => _isInitialized;

    public event EventHandler<ResourceStateChangedEventArgs>? ResourceStateChanged;
    public event EventHandler<HysteresisStateChangedEventArgs>? HysteresisStateChanged;
    public event EventHandler<PredictiveControlTriggeredEventArgs>? PredictiveControlTriggered;

    public bool Initialize()
    {
        try
        {
            InitializeAsync().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] 同期初期化エラー");
            return false;
        }
    }

    public void Shutdown()
    {
        Dispose();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_initializationLock)
        {
            if (_isInitialized) return;
        }

        try
        {
            _logger.LogInformation("🔧 [PHASE3] 高度なリソース管理システム初期化開始");

            // リソース監視システムの初期化
            if (!_resourceMonitor.IsInitialized)
            {
                _resourceMonitor.Initialize();
            }

            // ヒステリシス制御システムの初期化（既に初期化済み）
            _hysteresisController.HysteresisStateChanged += OnHysteresisStateChanged;

            // ゲーム負荷パターン学習データの初期化
            await InitializeGameLoadPatternsAsync(cancellationToken).ConfigureAwait(false);

            lock (_initializationLock)
            {
                _isInitialized = true;
            }

            _logger.LogInformation("✅ [PHASE3] 高度なリソース管理システム初期化完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] 高度なリソース管理システム初期化エラー");
            throw;
        }
    }

    public async Task<ResourceMetrics> GetSystemMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        return await _resourceMonitor.GetCurrentMetricsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<GpuVramMetrics> GetGpuVramMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        var systemMetrics = await _resourceMonitor.GetCurrentMetricsAsync(cancellationToken).ConfigureAwait(false);
        
        // システムメトリクスをGpuVramMetricsに変換（動的VRAM容量使用）
        var vramCapacityInfo = await _vramDetector.GetVramCapacityInfoAsync(cancellationToken).ConfigureAwait(false);
        var gpuVramMetrics = new GpuVramMetrics(
            GpuUtilizationPercent: systemMetrics.GpuUsagePercent ?? 0.0,
            VramUsagePercent: vramCapacityInfo.UsagePercent,
            VramUsedMB: vramCapacityInfo.UsedCapacityMB,
            VramTotalMB: vramCapacityInfo.TotalCapacityMB,
            GpuTemperatureCelsius: systemMetrics.GpuTemperature ?? 0.0,
            PowerUsageWatts: 0.0, // Windows APIでは取得困難
            GpuClockMhz: 0, // Windows APIでは取得困難  
            MemoryClockMhz: 0, // Windows APIでは取得困難
            IsOptimalForProcessing: vramCapacityInfo.UsagePercent < 80.0 && (systemMetrics.GpuUsagePercent ?? 0.0) < 80.0,
            MeasuredAt: DateTime.UtcNow
        );

        return gpuVramMetrics;
    }

    public async Task<int> CalculateOptimalParallelismAsync(SystemLoad systemLoad, CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        var gpuMetrics = await GetGpuVramMetricsAsync(cancellationToken).ConfigureAwait(false);
        
        // ヒステリシス制御による最適並列度計算
        return await _hysteresisController.AdjustParallelismAsync(gpuMetrics, systemLoad, cancellationToken).ConfigureAwait(false);
    }

    public async Task AdjustParallelismWithHysteresisAsync(SystemLoad systemLoad, CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        var gpuMetrics = await GetGpuVramMetricsAsync(cancellationToken).ConfigureAwait(false);
        var previousMetrics = _resourceMonitor.CurrentMetrics;
        
        // ヒステリシス制御実行
        var newParallelism = await _hysteresisController.AdjustParallelismAsync(gpuMetrics, systemLoad, cancellationToken).ConfigureAwait(false);
        
        // リソース状態変更イベント発行
        if (previousMetrics != null)
        {
            var currentMetrics = await GetSystemMetricsAsync(cancellationToken).ConfigureAwait(false);
            ResourceStateChanged?.Invoke(this, new ResourceStateChangedEventArgs
            {
                PreviousMetrics = previousMetrics,
                CurrentMetrics = currentMetrics,
                SystemLoad = systemLoad,
                RecommendedAction = DetermineRecommendedAction(gpuMetrics)
            });
        }
    }

    public async Task<TimeSpan> CalculatePredictiveCooldownAsync(GameLoadPattern gamePattern, CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        var settings = _predictiveSettings.CurrentValue;
        var gpuMetrics = await GetGpuVramMetricsAsync(cancellationToken).ConfigureAwait(false);
        
        // 基本クールダウン時間の計算
        var baseCooldown = TimeSpan.FromSeconds(5); // デフォルト5秒
        
        // Phase 3: 高度な予測制御アルゴリズム
        var temperatureMultiplier = CalculateTemperatureMultiplier(gpuMetrics.GpuTemperatureCelsius, settings);
        var vramPressureMultiplier = CalculateVramPressureMultiplier(gpuMetrics.GetVramPressureLevel(), settings);
        var gamePatternMultiplier = CalculateGamePatternMultiplier(gamePattern);
        
        // 総合クールダウン計算
        var totalMultiplier = settings.CooldownBaseMultiplier * temperatureMultiplier * vramPressureMultiplier * gamePatternMultiplier;
        var finalCooldown = TimeSpan.FromMilliseconds(baseCooldown.TotalMilliseconds * totalMultiplier);
        
        // 範囲制限（最小1秒、最大60秒）
        if (finalCooldown < TimeSpan.FromSeconds(1))
            finalCooldown = TimeSpan.FromSeconds(1);
        else if (finalCooldown > TimeSpan.FromSeconds(60))
            finalCooldown = TimeSpan.FromSeconds(60);

        _logger.LogDebug("🕒 [PHASE3] 予測的クールダウン計算完了: {FinalCooldown}秒 " +
            "(基本={BaseCooldown}秒, 温度係数={TempMult:F2}, VRAM係数={VramMult:F2}, ゲーム係数={GameMult:F2})",
            finalCooldown.TotalSeconds, baseCooldown.TotalSeconds, temperatureMultiplier, vramPressureMultiplier, gamePatternMultiplier);

        return finalCooldown;
    }

    public async Task ApplyGameProfileAsync(string gameProcessName, CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            lock (_gamePatternLock)
            {
                if (_gamePatterns.TryGetValue(gameProcessName, out var existingPattern))
                {
                    _logger.LogInformation("🎮 [PHASE3] 既存のゲームプロファイル適用: {GameName}, 学習セッション数: {SessionCount}",
                        gameProcessName, existingPattern.LearningSessionCount);
                }
                else
                {
                    // 新しいゲームの場合、デフォルトプロファイルを作成
                    var defaultProfile = CreateDefaultGameProfile(gameProcessName);
                    _gamePatterns[gameProcessName] = defaultProfile;
                    
                    _logger.LogInformation("🎮 [PHASE3] 新しいゲームプロファイル作成: {GameName}", gameProcessName);
                }
            }

            // A/Bテスト設定の動的切り替え検討
            await ConsiderConfigurationVariantSwitch(gameProcessName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] ゲームプロファイル適用エラー: {GameName}", gameProcessName);
            throw;
        }
    }

    public async Task<string> GetActiveConfigurationVariantAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // 非同期インターフェース対応
        
        lock (_configurationLock)
        {
            return _activeConfigurationVariant;
        }
    }

    public async Task<ResourceConflictResult> DetectAndResolveConflictsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var systemMetrics = await GetSystemMetricsAsync(cancellationToken).ConfigureAwait(false);
            var gpuMetrics = await GetGpuVramMetricsAsync(cancellationToken).ConfigureAwait(false);
            
            // リソース競合検出ロジック
            var conflictingProcesses = await DetectConflictingProcessesAsync(cancellationToken).ConfigureAwait(false);
            var hasConflict = conflictingProcesses.Count > 0;
            
            var recommendedAction = DetermineConflictResolutionAction(systemMetrics, gpuMetrics, conflictingProcesses);
            var recommendedParallelism = CalculateConflictAwareParallelism(recommendedAction);
            var recommendedCooldown = CalculateConflictAwareCooldown(recommendedAction);

            var result = new ResourceConflictResult(
                HasConflict: hasConflict,
                ConflictingProcesses: conflictingProcesses,
                RecommendedAction: recommendedAction,
                RecommendedParallelism: recommendedParallelism,
                RecommendedCooldown: recommendedCooldown
            );

            if (hasConflict)
            {
                _logger.LogWarning("⚠️ [PHASE3] リソース競合検出: 競合プロセス数={ConflictCount}, 推奨アクション={Action}",
                    conflictingProcesses.Count, recommendedAction);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] リソース競合検出エラー");
            return new ResourceConflictResult(false, [], RecommendedAction.Maintain, 2, TimeSpan.FromSeconds(5));
        }
    }

    private void OnHysteresisStateChanged(object? sender, HysteresisStateChangedEventArgs e)
    {
        // ヒステリシス状態変更イベントの転送
        HysteresisStateChanged?.Invoke(this, e);
        
        _logger.LogDebug("🎯 [PHASE3] ヒステリシス状態変更通知: {PreviousParallelism} → {NewParallelism}",
            e.PreviousParallelism, e.NewParallelism);
    }

    private async Task InitializeGameLoadPatternsAsync(CancellationToken cancellationToken)
    {
        try
        {
            // TODO: 将来的には永続化ストレージからゲーム負荷パターンを読み込み
            _logger.LogDebug("🎮 [PHASE3] ゲーム負荷パターン学習システム初期化完了");
            
            await Task.CompletedTask; // 非同期インターフェース対応
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] ゲーム負荷パターン初期化エラー");
            throw;
        }
    }

    private static double CalculateVramUsagePercent(ResourceMetrics metrics)
    {
        if (!metrics.GpuMemoryUsageMB.HasValue) return 0.0;
        
        // デフォルト8GB仮定（後でGpuEnvironmentDetectorから取得予定）
        const long defaultVramMB = 8192;
        return Math.Min(100.0, (double)metrics.GpuMemoryUsageMB.Value / defaultVramMB * 100.0);
    }

    private static long GetTotalVramMB()
    {
        // TODO: 実際のVRAM容量をGpuEnvironmentDetectorから取得
        return 8192; // デフォルト8GB
    }

    private static bool IsOptimalForGpuProcessing(ResourceMetrics metrics)
    {
        var vramUsage = CalculateVramUsagePercent(metrics);
        var gpuUsage = metrics.GpuUsagePercent ?? 0.0;
        
        return vramUsage < 70.0 && gpuUsage < 80.0;
    }

    private static RecommendedAction DetermineRecommendedAction(GpuVramMetrics gpuMetrics)
    {
        var vramPressure = gpuMetrics.GetVramPressureLevel();
        var tempState = gpuMetrics.GetTemperatureState();

        return (vramPressure, tempState) switch
        {
            (VramPressureLevel.Emergency, _) => RecommendedAction.EmergencyFallback,
            (VramPressureLevel.Critical, GpuTemperatureState.Critical) => RecommendedAction.EmergencyFallback,
            (VramPressureLevel.Critical, _) => RecommendedAction.FallbackToCpu,
            (VramPressureLevel.High, GpuTemperatureState.Hot) => RecommendedAction.ScaleDown,
            (VramPressureLevel.Low, GpuTemperatureState.Cool) => RecommendedAction.ScaleUp,
            _ => RecommendedAction.Maintain
        };
    }

    private double CalculateTemperatureMultiplier(double temperatureCelsius, PredictiveControlSettings settings)
    {
        return temperatureCelsius switch
        {
            < 60 => 1.0, // 正常温度
            < 75 => 1.0 + (temperatureCelsius - 60) / 60.0 * settings.TemperatureAdjustmentMultiplier,
            < 85 => 1.0 + settings.TemperatureAdjustmentMultiplier,
            _ => 1.0 + settings.TemperatureAdjustmentMultiplier * 2.0 // 高温時は大幅延長
        };
    }

    private double CalculateVramPressureMultiplier(VramPressureLevel pressureLevel, PredictiveControlSettings settings)
    {
        return pressureLevel switch
        {
            VramPressureLevel.Low => 1.0,
            VramPressureLevel.Moderate => 1.0 + settings.VramPressureAdjustmentMultiplier * 0.5,
            VramPressureLevel.High => 1.0 + settings.VramPressureAdjustmentMultiplier,
            VramPressureLevel.Critical => 1.0 + settings.VramPressureAdjustmentMultiplier * 1.5,
            VramPressureLevel.Emergency => 1.0 + settings.VramPressureAdjustmentMultiplier * 2.0,
            _ => 1.0
        };
    }

    private static double CalculateGamePatternMultiplier(GameLoadPattern gamePattern)
    {
        // ゲーム負荷パターンに基づく係数計算
        if (gamePattern.LearningSessionCount < 3)
            return 1.0; // 学習データ不足
        
        // 平均負荷と最大負荷の差が大きいほど慎重にする
        var loadVariability = gamePattern.PeakLoad - gamePattern.AverageLoad;
        return loadVariability switch
        {
            < 20 => 0.8, // 安定したゲーム
            < 40 => 1.0, // 通常のゲーム
            < 60 => 1.3, // 負荷変動の大きいゲーム
            _ => 1.6 // 非常に不安定なゲーム
        };
    }

    private static GameLoadPattern CreateDefaultGameProfile(string gameProcessName)
    {
        return new GameLoadPattern(
            GameProcessName: gameProcessName,
            LoadProfile: new Dictionary<TimeSpan, double>(),
            AverageLoad: 50.0,
            PeakLoad: 80.0,
            PredictedPeakTime: TimeSpan.FromMinutes(10),
            LearningSessionCount: 0
        );
    }

    private async Task ConsiderConfigurationVariantSwitch(string gameProcessName, CancellationToken cancellationToken)
    {
        // A/Bテスト設定の動的切り替えロジック（将来実装）
        await Task.CompletedTask; // 非同期インターフェース対応
    }

    private async Task<List<ConflictingProcess>> DetectConflictingProcessesAsync(CancellationToken cancellationToken)
    {
        // 競合プロセス検出ロジック（将来実装）
        await Task.CompletedTask; // 非同期インターフェース対応
        return [];
    }

    private static RecommendedAction DetermineConflictResolutionAction(
        ResourceMetrics systemMetrics, 
        GpuVramMetrics gpuMetrics, 
        List<ConflictingProcess> conflictingProcesses)
    {
        if (conflictingProcesses.Count == 0)
            return RecommendedAction.Maintain;
        
        var highSeverityConflicts = conflictingProcesses.Count(p => p.Severity is ConflictSeverity.High or ConflictSeverity.Critical);
        
        return highSeverityConflicts switch
        {
            0 => RecommendedAction.ScaleDown,
            1 => RecommendedAction.FallbackToCpu,
            _ => RecommendedAction.EmergencyFallback
        };
    }

    private static int CalculateConflictAwareParallelism(RecommendedAction action)
    {
        return action switch
        {
            RecommendedAction.EmergencyFallback => 1,
            RecommendedAction.FallbackToCpu => 1,
            RecommendedAction.ScaleDown => 2,
            RecommendedAction.Maintain => 4,
            RecommendedAction.ScaleUp => 6,
            _ => 2
        };
    }

    private static TimeSpan CalculateConflictAwareCooldown(RecommendedAction action)
    {
        return action switch
        {
            RecommendedAction.EmergencyFallback => TimeSpan.FromSeconds(30),
            RecommendedAction.FallbackToCpu => TimeSpan.FromSeconds(20),
            RecommendedAction.ScaleDown => TimeSpan.FromSeconds(10),
            RecommendedAction.Maintain => TimeSpan.FromSeconds(5),
            RecommendedAction.ScaleUp => TimeSpan.FromSeconds(3),
            _ => TimeSpan.FromSeconds(5)
        };
    }

    public void Dispose()
    {
        if (_hysteresisController != null)
        {
            _hysteresisController.HysteresisStateChanged -= OnHysteresisStateChanged;
            _hysteresisController.Dispose();
        }

        _resourceMonitor?.Dispose();
        
        _logger.LogInformation("🔄 [PHASE3] HybridResourceManagerEnhanced正常終了");
    }
}