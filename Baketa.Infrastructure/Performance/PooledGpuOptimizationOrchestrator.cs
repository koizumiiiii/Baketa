using System.Collections.Concurrent;
using System.Diagnostics;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Performance;
using Baketa.Infrastructure.DI.Modules;
using Baketa.Infrastructure.Memory.Pools;
using Baketa.Infrastructure.OCR.GPU;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;

namespace Baketa.Infrastructure.Performance;

/// <summary>
/// プール化×GPU最適化統合オーケストレーター
/// 全てのプーリングシステムとGPU最適化システムを統合制御し、
/// 動的リソース管理とパフォーマンス最適化を実現
/// </summary>
public sealed class PooledGpuOptimizationOrchestrator : IHostedService, IDisposable
{
    private readonly ILogger<PooledGpuOptimizationOrchestrator> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IUnifiedGpuOptimizer _gpuOptimizer;
    // [ROI_DELETION] IPerformanceOrchestrator削除 - 実際に使用されていない依存関係
    private readonly IObjectPoolStatisticsReporter _poolStatistics;

    // GPU最適化プール管理
    private readonly ConcurrentDictionary<string, OptimizedPoolConfiguration> _poolConfigurations = new();
    private readonly ConcurrentDictionary<string, GpuMemoryAllocation> _gpuMemoryAllocations = new();

    // パフォーマンス監視
    private readonly System.Threading.Timer _optimizationTimer;
    private readonly SemaphoreSlim _optimizationLock = new(1, 1);

    // 動的最適化設定
    private AdaptiveOptimizationSettings _currentSettings = new();
    private GpuResourceSnapshot _lastGpuSnapshot = new();

    private bool _disposed;

    public PooledGpuOptimizationOrchestrator(
        ILogger<PooledGpuOptimizationOrchestrator> logger,
        IServiceProvider serviceProvider,
        IUnifiedGpuOptimizer gpuOptimizer,
        // [ROI_DELETION] IPerformanceOrchestrator performanceOrchestrator, パラメーター削除
        IObjectPoolStatisticsReporter poolStatistics)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _gpuOptimizer = gpuOptimizer ?? throw new ArgumentNullException(nameof(gpuOptimizer));
        // [ROI_DELETION] _performanceOrchestrator初期化削除 - 使用されていない依存関係
        _poolStatistics = poolStatistics ?? throw new ArgumentNullException(nameof(poolStatistics));

        // 30秒間隔で最適化実行
        _optimizationTimer = new System.Threading.Timer(PerformOptimizationCycle, null, Timeout.Infinite, Timeout.Infinite);

        _logger.LogInformation("🚀 PooledGpuOptimizationOrchestrator初期化完了 - 統合最適化システム開始");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("🔥 プール化×GPU最適化システム開始中...");

            // 1. GPU環境初期化
            await InitializeGpuEnvironmentAsync(cancellationToken).ConfigureAwait(false);

            // 2. プール設定初期化
            await InitializePoolConfigurationsAsync(cancellationToken).ConfigureAwait(false);

            // 3. パフォーマンス監視開始
            await InitializePerformanceMonitoringAsync(cancellationToken).ConfigureAwait(false);

            // 4. 最適化タイマー開始
            _optimizationTimer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            _logger.LogInformation("✅ プール化×GPU最適化システム開始完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ プール化×GPU最適化システム開始エラー");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("⏸️ プール化×GPU最適化システム停止中...");

            // 最適化タイマー停止
            await _optimizationTimer.DisposeAsync().ConfigureAwait(false);

            // 最終最適化実行
            await PerformOptimizationCycleAsync(cancellationToken).ConfigureAwait(false);

            // 統計レポート出力
            await GenerateFinalPerformanceReportAsync().ConfigureAwait(false);

            _logger.LogInformation("✅ プール化×GPU最適化システム停止完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ プール化×GPU最適化システム停止エラー");
        }
    }

    /// <summary>
    /// GPU環境の初期化と最適設定の決定
    /// </summary>
    private async Task InitializeGpuEnvironmentAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🎯 GPU環境初期化開始");

        // GPU環境情報取得
        var providerStatus = await _gpuOptimizer.GetProviderStatusAsync(cancellationToken).ConfigureAwait(false);
        var optimalProvider = await _gpuOptimizer.SelectOptimalProviderAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("🔍 利用可能なGPUプロバイダー:");
        foreach (var (type, isSupported, priority, info) in providerStatus)
        {
            _logger.LogInformation("  📌 {ProviderType}: {Status} (優先度: {Priority}) - {Info}",
                type, isSupported ? "サポート" : "非サポート", priority, info);
        }

        _logger.LogInformation("⚡ 選択された最適プロバイダー: {ProviderType}", optimalProvider.Type);

        // GPU最適化設定を決定
        _currentSettings = DetermineOptimizationSettings(providerStatus);

        _logger.LogInformation("🎛️ 最適化設定決定: プール容量={PoolCapacity}, GPU並列度={GpuParallelism}",
            _currentSettings.OptimalPoolCapacity, _currentSettings.GpuParallelism);
    }

    /// <summary>
    /// 全プールの初期設定と最適化
    /// </summary>
    private async Task InitializePoolConfigurationsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🏊 プール設定最適化開始");

        // 各プールの最適設定を決定
        var poolTypes = new[]
        {
            ("ImagePool", typeof(IAdvancedImagePool)),
            ("TextRegionPool", typeof(ITextRegionPool)),
            ("MatPool", typeof(IObjectPool<IMatWrapper>)),
            ("OcrEnginePool", typeof(ObjectPool<IOcrEngine>))
        };

        foreach (var (poolName, poolType) in poolTypes)
        {
            var poolService = _serviceProvider.GetService(poolType);
            if (poolService != null)
            {
                var config = await OptimizePoolConfigurationAsync(poolName, poolService).ConfigureAwait(false);
                _poolConfigurations.TryAdd(poolName, config);

                _logger.LogInformation("✅ {PoolName}プール最適化完了 - 容量: {Capacity}, GPU割り当て: {GpuMemoryMB}MB",
                    poolName, config.OptimalCapacity, config.GpuMemoryAllocationMB);
            }
            else
            {
                _logger.LogWarning("⚠️ {PoolName}プールサービスが見つかりません", poolName);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// パフォーマンス監視システムの初期化
    /// </summary>
    private async Task InitializePerformanceMonitoringAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("📊 パフォーマンス監視初期化開始");

        // [ROI_DELETION] IntegratedPerformanceOrchestrator削除済み - パフォーマンス監視は独立動作
        // パフォーマンスメトリクス収集システム初期化

        await Task.CompletedTask;
        _logger.LogInformation("✅ パフォーマンス監視初期化完了");
    }

    /// <summary>
    /// 定期最適化サイクル実行
    /// </summary>
    private async void PerformOptimizationCycle(object? state)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await PerformOptimizationCycleAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 最適化サイクルでエラー発生");
        }
    }

    /// <summary>
    /// 最適化サイクルの実行
    /// </summary>
    private async Task PerformOptimizationCycleAsync(CancellationToken cancellationToken)
    {
        if (!await _optimizationLock.WaitAsync(1000, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogDebug("🔄 最適化サイクル実行中のため、このサイクルをスキップ");
            return;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogDebug("🔄 最適化サイクル開始");

            // 1. プール統計取得
            var poolReport = _poolStatistics.GetReport();

            // 2. GPU状態取得
            var currentGpuSnapshot = await CaptureGpuResourceSnapshotAsync().ConfigureAwait(false);

            // 3. パフォーマンス分析
            var optimizationRecommendations = AnalyzePerformance(poolReport, currentGpuSnapshot);

            // 4. 推奨最適化を適用
            await ApplyOptimizationRecommendationsAsync(optimizationRecommendations, cancellationToken).ConfigureAwait(false);

            // 5. スナップショット更新
            _lastGpuSnapshot = currentGpuSnapshot;

            stopwatch.Stop();
            _logger.LogDebug("✅ 最適化サイクル完了 - 実行時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            _optimizationLock.Release();
        }
    }

    /// <summary>
    /// ワークロード状況に基づく動的最適化調整
    /// </summary>
    private async Task AdjustOptimizationBasedOnWorkloadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogTrace("📊 ワークロードベース動的調整開始");

            // 現在のシステム負荷状況を取得
            var currentMetrics = await GetCurrentWorkloadMetricsAsync().ConfigureAwait(false);
            var previousSnapshot = _lastGpuSnapshot;

            // GPU使用率とメモリ使用状況をチェック
            var gpuStatus = await _gpuOptimizer.GetProviderStatusAsync(cancellationToken).ConfigureAwait(false);
            var currentSnapshot = CreateGpuResourceSnapshot(currentMetrics, gpuStatus);

            // 動的調整の判定と実行
            var adjustments = DetermineOptimizationAdjustments(currentMetrics, previousSnapshot, currentSnapshot);

            if (adjustments.Any())
            {
                _logger.LogInformation("🔧 動的調整実行: {AdjustmentCount}項目", adjustments.Count);

                foreach (var adjustment in adjustments)
                {
                    await ApplyOptimizationAdjustmentAsync(adjustment, cancellationToken).ConfigureAwait(false);
                }

                // スナップショット更新
                _lastGpuSnapshot = currentSnapshot;
            }

            _logger.LogTrace("✅ ワークロード動的調整完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ワークロード動的調整エラー");
        }
    }

    /// <summary>
    /// 現在のワークロードメトリクス取得
    /// </summary>
    private async Task<WorkloadMetrics> GetCurrentWorkloadMetricsAsync()
    {
        var process = Process.GetCurrentProcess();
        var metrics = new WorkloadMetrics();

        // CPU使用率計算 (簡易的な実装)
        var startTime = DateTime.UtcNow;
        var startCpuUsage = process.TotalProcessorTime;
        await Task.Delay(100).ConfigureAwait(false); // 短時間待機
        var endTime = DateTime.UtcNow;
        var endCpuUsage = process.TotalProcessorTime;

        var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
        var totalMsPassed = (endTime - startTime).TotalMilliseconds;
        var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

        metrics.CpuUsagePercent = Math.Min(100.0, cpuUsageTotal * 100.0);
        metrics.MemoryUsageMB = process.WorkingSet64 / (1024 * 1024);
        metrics.ThreadCount = process.Threads.Count;

        // プール統計情報も含める
        var poolReport = _poolStatistics.GetReport();
        metrics.PoolEfficiencyPercent = CalculateOverallPoolEfficiency(poolReport);
        metrics.ActivePoolObjects = (int)poolReport.TotalObjectCreationsAvoided;

        return metrics;
    }

    /// <summary>
    /// 最適化調整項目の決定
    /// </summary>
    private List<OptimizationAdjustment> DetermineOptimizationAdjustments(
        WorkloadMetrics current,
        GpuResourceSnapshot previous,
        GpuResourceSnapshot current_gpu)
    {
        var adjustments = new List<OptimizationAdjustment>();

        // CPU使用率が高い場合のGPU移行調整
        if (current.CpuUsagePercent > 80 && current_gpu.GpuMemoryAvailableMB > 512)
        {
            adjustments.Add(new OptimizationAdjustment
            {
                Type = OptimizationAdjustmentType.IncreaseGpuUtilization,
                Priority = AdjustmentPriority.High,
                TargetIncrease = 25, // 25%増加
                Reason = $"CPU使用率高負荷 ({current.CpuUsagePercent:F1}%) - GPU移行を増強",
                EstimatedImpact = "CPU負荷軽減、処理速度向上"
            });
        }

        // GPU使用率が高い場合の負荷分散
        if (current_gpu.GpuMemoryUsagePercent > 85)
        {
            adjustments.Add(new OptimizationAdjustment
            {
                Type = OptimizationAdjustmentType.DistributeGpuLoad,
                Priority = AdjustmentPriority.High,
                TargetDecrease = 15, // 15%削減
                Reason = $"GPU使用率飽和状態 ({current_gpu.GpuMemoryUsagePercent:F1}%) - 負荷分散必要",
                EstimatedImpact = "GPU過負荷回避、安定性向上"
            });
        }

        // プール効率が低い場合のプール容量調整
        if (current.PoolEfficiencyPercent < 70)
        {
            adjustments.Add(new OptimizationAdjustment
            {
                Type = OptimizationAdjustmentType.AdjustPoolCapacity,
                Priority = AdjustmentPriority.Medium,
                TargetIncrease = 20, // 20%増加
                Reason = $"プール効率低下 ({current.PoolEfficiencyPercent:F1}%) - 容量不足の可能性",
                EstimatedImpact = "オブジェクト再利用率向上、GC負荷軽減"
            });
        }

        // メモリ使用量が急増している場合
        var memoryGrowthRate = previous.MemoryUsageMB > 0 ?
            (current_gpu.MemoryUsageMB - previous.MemoryUsageMB) / previous.MemoryUsageMB : 0;

        if (memoryGrowthRate > 0.5) // 50%以上の増加
        {
            adjustments.Add(new OptimizationAdjustment
            {
                Type = OptimizationAdjustmentType.OptimizeMemoryUsage,
                Priority = AdjustmentPriority.High,
                TargetDecrease = 30,
                Reason = $"メモリ使用量急増 (+{memoryGrowthRate * 100:F1}%) - メモリリークの可能性",
                EstimatedImpact = "メモリ使用量安定化、OOM回避"
            });
        }

        return adjustments;
    }

    /// <summary>
    /// 最適化調整の適用
    /// </summary>
    private async Task ApplyOptimizationAdjustmentAsync(OptimizationAdjustment adjustment, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔧 調整適用: {Type} (優先度: {Priority})", adjustment.Type, adjustment.Priority);
        _logger.LogInformation("   理由: {Reason}", adjustment.Reason);
        _logger.LogInformation("   期待効果: {Impact}", adjustment.EstimatedImpact);

        try
        {
            switch (adjustment.Type)
            {
                case OptimizationAdjustmentType.IncreaseGpuUtilization:
                    await AdjustGpuUtilizationAsync(adjustment.TargetIncrease, cancellationToken).ConfigureAwait(false);
                    break;

                case OptimizationAdjustmentType.DistributeGpuLoad:
                    await DistributeGpuLoadAsync(adjustment.TargetDecrease, cancellationToken).ConfigureAwait(false);
                    break;

                case OptimizationAdjustmentType.AdjustPoolCapacity:
                    await AdjustPoolCapacityAsync(adjustment.TargetIncrease, cancellationToken).ConfigureAwait(false);
                    break;

                case OptimizationAdjustmentType.OptimizeMemoryUsage:
                    await OptimizeMemoryUsageAsync(adjustment.TargetDecrease, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    _logger.LogWarning("⚠️ 未対応の調整タイプ: {Type}", adjustment.Type);
                    break;
            }

            _logger.LogInformation("✅ 調整適用完了: {Type}", adjustment.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 調整適用エラー: {Type}", adjustment.Type);
        }
    }

    /// <summary>
    /// GPU使用率の動的調整
    /// </summary>
    private async Task AdjustGpuUtilizationAsync(int targetIncreasePercent, CancellationToken cancellationToken)
    {
        // GPU並列度を増加
        var newParallelism = Math.Min(8, _currentSettings.GpuParallelism + (targetIncreasePercent / 25));
        if (newParallelism != _currentSettings.GpuParallelism)
        {
            _currentSettings.GpuParallelism = newParallelism;
            _logger.LogInformation("📈 GPU並列度を {Old} から {New} に調整",
                _currentSettings.GpuParallelism, newParallelism);
        }

        // GPUメモリ割り当ても調整
        var memoryIncrease = _currentSettings.GpuMemoryAllocationMB * targetIncreasePercent / 100;
        var newMemoryAllocation = Math.Min(4096, _currentSettings.GpuMemoryAllocationMB + memoryIncrease);

        if (Math.Abs(newMemoryAllocation - _currentSettings.GpuMemoryAllocationMB) > 50)
        {
            _currentSettings.GpuMemoryAllocationMB = newMemoryAllocation;
            _logger.LogInformation("💾 GPUメモリ割り当てを {Old}MB から {New}MB に調整",
                _currentSettings.GpuMemoryAllocationMB, newMemoryAllocation);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// GPU負荷分散
    /// </summary>
    private async Task DistributeGpuLoadAsync(int targetDecreasePercent, CancellationToken cancellationToken)
    {
        // GPU並列度を一時的に削減
        var newParallelism = Math.Max(1, _currentSettings.GpuParallelism - (targetDecreasePercent / 15));
        if (newParallelism != _currentSettings.GpuParallelism)
        {
            _currentSettings.GpuParallelism = newParallelism;
            _logger.LogInformation("📉 GPU負荷軽減: 並列度を {Old} から {New} に一時調整",
                _currentSettings.GpuParallelism, newParallelism);
        }

        // バッチサイズも調整
        _currentSettings.OptimalBatchSize = Math.Max(8, _currentSettings.OptimalBatchSize - 4);
        _logger.LogInformation("📦 GPU負荷軽減: バッチサイズを {BatchSize} に調整",
            _currentSettings.OptimalBatchSize);

        await Task.CompletedTask;
    }

    /// <summary>
    /// プール容量の動的調整
    /// </summary>
    private async Task AdjustPoolCapacityAsync(int targetIncreasePercent, CancellationToken cancellationToken)
    {
        var capacityIncrease = (int)(_currentSettings.OptimalPoolCapacity * targetIncreasePercent / 100);
        var newCapacity = (int)Math.Min(500, _currentSettings.OptimalPoolCapacity + capacityIncrease);

        if (newCapacity != (int)_currentSettings.OptimalPoolCapacity)
        {
            _currentSettings.OptimalPoolCapacity = newCapacity;
            _logger.LogInformation("🏊 プール容量を {Old} から {New} に調整",
                (int)_currentSettings.OptimalPoolCapacity, newCapacity);

            // 各プールに新しい設定を適用
            await ApplyPoolCapacityChangesAsync(newCapacity, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// メモリ使用量最適化
    /// </summary>
    private async Task OptimizeMemoryUsageAsync(int targetDecreasePercent, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🧹 メモリ最適化開始 (目標削減: {TargetPercent}%)", targetDecreasePercent);

        // 1. 強制GC実行
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();

        // 2. プールの未使用オブジェクト削減
        foreach (var poolConfig in _poolConfigurations.Values)
        {
            // プール容量を一時的に削減
            var reductionRatio = 1.0 - (targetDecreasePercent / 100.0);
            poolConfig.MaximumRetained = Math.Max(5, (int)(poolConfig.MaximumRetained * reductionRatio));
        }

        // 3. GPUメモリも削減
        var memoryReduction = _currentSettings.GpuMemoryAllocationMB * targetDecreasePercent / 100;
        _currentSettings.GpuMemoryAllocationMB = Math.Max(256, _currentSettings.GpuMemoryAllocationMB - memoryReduction);

        _logger.LogInformation("✅ メモリ最適化完了 - GPU割り当て: {GpuMemory}MB",
            _currentSettings.GpuMemoryAllocationMB);

        await Task.CompletedTask;
    }

    /// <summary>
    /// プール容量変更の適用
    /// </summary>
    private async Task ApplyPoolCapacityChangesAsync(int newCapacity, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔧 プール容量変更を各プールに適用中: {NewCapacity}", newCapacity);

        foreach (var (poolName, config) in _poolConfigurations)
        {
            var previousCapacity = config.OptimalCapacity;
            config.OptimalCapacity = newCapacity;

            _logger.LogDebug("📦 {PoolName}: {OldCapacity} → {NewCapacity}",
                poolName, previousCapacity, newCapacity);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// GPUリソーススナップショットの作成
    /// </summary>
    private GpuResourceSnapshot CreateGpuResourceSnapshot(
        WorkloadMetrics workload,
        IReadOnlyList<(ExecutionProvider Type, bool IsSupported, int Priority, string Info)> gpuStatus)
    {
        var snapshot = new GpuResourceSnapshot
        {
            Timestamp = DateTime.UtcNow,
            AvailableProviders = gpuStatus.Count(p => p.IsSupported),
            EstimatedMemoryUsageMB = _gpuMemoryAllocations.Values.Sum(a => a.AllocatedMemoryMB),
            ActiveSessions = _gpuMemoryAllocations.Count,
            MemoryUsageMB = workload.MemoryUsageMB,
            GpuMemoryUsagePercent = CalculateGpuMemoryUsagePercent(),
            GpuMemoryAvailableMB = CalculateAvailableGpuMemory()
        };

        return snapshot;
    }

    /// <summary>
    /// GPU メモリ使用率の計算
    /// </summary>
    private double CalculateGpuMemoryUsagePercent()
    {
        var totalAllocated = _gpuMemoryAllocations.Values.Sum(a => a.AllocatedMemoryMB);
        var totalAvailable = _currentSettings.GpuMemoryAllocationMB;

        return totalAvailable > 0 ? (totalAllocated / totalAvailable) * 100.0 : 0.0;
    }

    /// <summary>
    /// 利用可能GPUメモリの計算
    /// </summary>
    private double CalculateAvailableGpuMemory()
    {
        var totalAllocated = _gpuMemoryAllocations.Values.Sum(a => a.AllocatedMemoryMB);
        return Math.Max(0, _currentSettings.GpuMemoryAllocationMB - totalAllocated);
    }

    /// <summary>
    /// 全体的なプール効率の計算
    /// </summary>
    private double CalculateOverallPoolEfficiency(ObjectPoolReport poolReport)
    {
        return poolReport.OverallHitRate * 100.0;
    }

    /// <summary>
    /// プール設定の最適化
    /// </summary>
    private async Task<OptimizedPoolConfiguration> OptimizePoolConfigurationAsync(string poolName, object poolService)
    {
        var config = new OptimizedPoolConfiguration
        {
            PoolName = poolName,
            OptimalCapacity = _currentSettings.OptimalPoolCapacity,
            GpuMemoryAllocationMB = _currentSettings.GpuMemoryAllocationMB,
            AdaptiveResize = true,
            PerformanceTarget = PerformanceTarget.Balanced
        };

        // プール種別に応じた最適化
        switch (poolName)
        {
            case "ImagePool":
                config.OptimalCapacity = Math.Max(50, _currentSettings.OptimalPoolCapacity);
                config.GpuMemoryAllocationMB = Math.Max(256, _currentSettings.GpuMemoryAllocationMB / 2);
                break;
            case "OcrEnginePool":
                config.OptimalCapacity = Math.Min(8, _currentSettings.OptimalPoolCapacity);
                config.GpuMemoryAllocationMB = Math.Max(512, _currentSettings.GpuMemoryAllocationMB);
                break;
            case "TextRegionPool":
                config.OptimalCapacity = Math.Max(200, _currentSettings.OptimalPoolCapacity * 4);
                config.GpuMemoryAllocationMB = 64; // 軽量なテキスト領域
                break;
            case "MatPool":
                config.OptimalCapacity = Math.Max(30, _currentSettings.OptimalPoolCapacity / 2);
                config.GpuMemoryAllocationMB = Math.Max(128, _currentSettings.GpuMemoryAllocationMB / 4);
                break;
        }

        await Task.CompletedTask;
        return config;
    }

    /// <summary>
    /// GPU最適化設定の決定
    /// </summary>
    private AdaptiveOptimizationSettings DetermineOptimizationSettings(
        IReadOnlyList<(ExecutionProvider Type, bool IsSupported, int Priority, string Info)> providerStatus)
    {
        var settings = new AdaptiveOptimizationSettings();

        // サポートされている最高優先度プロバイダーを基準に設定決定
        var bestProvider = providerStatus.Where(p => p.IsSupported).OrderByDescending(p => p.Priority).FirstOrDefault();

        if (bestProvider.Type == ExecutionProvider.DirectML)
        {
            // DirectML: 高性能GPU環境
            settings.OptimalPoolCapacity = 100;
            settings.GpuParallelism = 4;
            settings.GpuMemoryAllocationMB = 1024;
        }
        else if (bestProvider.Type == ExecutionProvider.OpenVINO)
        {
            // OpenVINO: 中性能最適化環境
            settings.OptimalPoolCapacity = 75;
            settings.GpuParallelism = 3;
            settings.GpuMemoryAllocationMB = 512;
        }
        else
        {
            // CPU環境: 保守的設定
            settings.OptimalPoolCapacity = 50;
            settings.GpuParallelism = 2;
            settings.GpuMemoryAllocationMB = 256;
        }

        return settings;
    }

    /// <summary>
    /// GPU リソーススナップショットの取得
    /// </summary>
    private async Task<GpuResourceSnapshot> CaptureGpuResourceSnapshotAsync()
    {
        try
        {
            // GPU環境情報を取得（UnifiedGpuOptimizerから）
            var providerStatus = await _gpuOptimizer.GetProviderStatusAsync().ConfigureAwait(false);

            return new GpuResourceSnapshot
            {
                Timestamp = DateTime.UtcNow,
                AvailableProviders = providerStatus.Count(p => p.IsSupported),
                EstimatedMemoryUsageMB = _gpuMemoryAllocations.Values.Sum(a => a.AllocatedMemoryMB),
                ActiveSessions = _gpuMemoryAllocations.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPU リソーススナップショット取得エラー");
            return new GpuResourceSnapshot { Timestamp = DateTime.UtcNow };
        }
    }

    /// <summary>
    /// パフォーマンス分析と推奨事項生成
    /// </summary>
    private List<OptimizationRecommendation> AnalyzePerformance(
        ObjectPoolReport poolReport,
        GpuResourceSnapshot currentSnapshot)
    {
        var recommendations = new List<OptimizationRecommendation>();

        // プール効率分析
        var overallEfficiency = poolReport.OverallHitRate;
        if (overallEfficiency < 0.7) // 70%未満の場合
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.IncreasePoolCapacity,
                Description = $"プール効率が低い ({overallEfficiency:P1})",
                Priority = RecommendationPriority.High,
                EstimatedImpact = "メモリ効率15-25%向上"
            });
        }

        // GPU メモリ使用量分析
        if (currentSnapshot.EstimatedMemoryUsageMB > 2048) // 2GB超過
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.OptimizeGpuMemory,
                Description = $"GPU メモリ使用量が高い ({currentSnapshot.EstimatedMemoryUsageMB}MB)",
                Priority = RecommendationPriority.Medium,
                EstimatedImpact = "GPU効率10-20%向上"
            });
        }

        // プール容量不足検出
        var totalCreationsAvoided = poolReport.TotalObjectCreationsAvoided;
        var totalOperations = poolReport.ImagePoolStats.TotalGets + poolReport.TextRegionPoolStats.TotalGets + poolReport.MatPoolStats.TotalGets;

        if (totalCreationsAvoided < totalOperations * 0.5)
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.AdaptivePoolResize,
                Description = "プールオブジェクト再利用率が低い",
                Priority = RecommendationPriority.Medium,
                EstimatedImpact = "オブジェクト生成コスト20-30%削減"
            });
        }

        return recommendations;
    }

    /// <summary>
    /// 最適化推奨事項の適用
    /// </summary>
    private async Task ApplyOptimizationRecommendationsAsync(
        List<OptimizationRecommendation> recommendations,
        CancellationToken cancellationToken)
    {
        foreach (var recommendation in recommendations.OrderByDescending(r => r.Priority))
        {
            try
            {
                switch (recommendation.Type)
                {
                    case OptimizationType.IncreasePoolCapacity:
                        await ApplyPoolCapacityOptimizationAsync(recommendation).ConfigureAwait(false);
                        break;
                    case OptimizationType.OptimizeGpuMemory:
                        await ApplyGpuMemoryOptimizationAsync(recommendation).ConfigureAwait(false);
                        break;
                    case OptimizationType.AdaptivePoolResize:
                        await ApplyAdaptivePoolResizeAsync(recommendation).ConfigureAwait(false);
                        break;
                }

                _logger.LogInformation("✅ 最適化適用完了: {Description} - 期待効果: {Impact}",
                    recommendation.Description, recommendation.EstimatedImpact);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ 最適化適用エラー: {Description}", recommendation.Description);
            }
        }
    }

    private async Task ApplyPoolCapacityOptimizationAsync(OptimizationRecommendation recommendation)
    {
        // 各プールの容量を動的調整
        foreach (var (poolName, config) in _poolConfigurations)
        {
            config.OptimalCapacity = Math.Min(config.OptimalCapacity * 1.2, 200); // 20%増加、上限200
            _logger.LogDebug("🔧 {PoolName} 容量調整: {NewCapacity}", poolName, config.OptimalCapacity);
        }
        await Task.CompletedTask;
    }

    private async Task ApplyGpuMemoryOptimizationAsync(OptimizationRecommendation recommendation)
    {
        // GPU メモリ割り当ての最適化
        var totalAllocation = _gpuMemoryAllocations.Values.Sum(a => a.AllocatedMemoryMB);
        if (totalAllocation > 1536) // 1.5GB超過の場合、削減
        {
            foreach (var allocation in _gpuMemoryAllocations.Values)
            {
                allocation.AllocatedMemoryMB = Math.Max(allocation.AllocatedMemoryMB * 0.9, 64); // 10%削減、最低64MB
            }
            _logger.LogDebug("🔧 GPU メモリ割り当て最適化完了");
        }
        await Task.CompletedTask;
    }

    private async Task ApplyAdaptivePoolResizeAsync(OptimizationRecommendation recommendation)
    {
        // アダプティブプールサイズ調整の有効化
        foreach (var config in _poolConfigurations.Values)
        {
            config.AdaptiveResize = true;
            _logger.LogDebug("🔧 {PoolName} アダプティブリサイズ有効化", config.PoolName);
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// 最終パフォーマンスレポート生成
    /// </summary>
    private async Task GenerateFinalPerformanceReportAsync()
    {
        try
        {
            var poolReport = _poolStatistics.GetReport();
            var finalSnapshot = await CaptureGpuResourceSnapshotAsync().ConfigureAwait(false);

            var overallEfficiency = poolReport.OverallHitRate;
            var totalCreationsAvoided = poolReport.TotalObjectCreationsAvoided;

            _logger.LogInformation("📊 プール化×GPU最適化 最終パフォーマンスレポート:");
            _logger.LogInformation("  🏊 プール統計:");
            _logger.LogInformation("    📈 総合効率: {Efficiency:P1}", overallEfficiency);
            _logger.LogInformation("    🔄 オブジェクト作成回避: {CreationsAvoided}", totalCreationsAvoided);

            _logger.LogInformation("  🎯 GPU最適化統計:");
            _logger.LogInformation("    💾 GPU メモリ使用量: {MemoryMB}MB", finalSnapshot.EstimatedMemoryUsageMB);
            _logger.LogInformation("    🔧 アクティブセッション: {Sessions}", finalSnapshot.ActiveSessions);

            _logger.LogInformation("  ⚡ 最適化設定:");
            _logger.LogInformation("    📦 プール容量: {Capacity}", _currentSettings.OptimalPoolCapacity);
            _logger.LogInformation("    🔀 GPU 並列度: {Parallelism}", _currentSettings.GpuParallelism);

            var totalOptimizations = _poolConfigurations.Count;
            _logger.LogInformation("  🎉 実行された最適化: {Count}項目", totalOptimizations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "最終パフォーマンスレポート生成エラー");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _optimizationTimer?.Dispose();
        _optimizationLock?.Dispose();
        _disposed = true;

        _logger.LogInformation("🏁 PooledGpuOptimizationOrchestrator リソース解放完了");
    }
}

#region サポートクラス

/// <summary>
/// 動的最適化設定
/// </summary>
/// <summary>
/// ワークロードメトリクス
/// </summary>
public sealed class WorkloadMetrics
{
    public double CpuUsagePercent { get; set; }
    public long MemoryUsageMB { get; set; }
    public int ThreadCount { get; set; }
    public double PoolEfficiencyPercent { get; set; }
    public int ActivePoolObjects { get; set; }
}

/// <summary>
/// 最適化調整項目
/// </summary>
public sealed class OptimizationAdjustment
{
    public OptimizationAdjustmentType Type { get; set; }
    public AdjustmentPriority Priority { get; set; }
    public int TargetIncrease { get; set; }
    public int TargetDecrease { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string EstimatedImpact { get; set; } = string.Empty;
}

/// <summary>
/// 最適化調整タイプ
/// </summary>
public enum OptimizationAdjustmentType
{
    IncreaseGpuUtilization,
    DistributeGpuLoad,
    AdjustPoolCapacity,
    OptimizeMemoryUsage
}

/// <summary>
/// 調整優先度
/// </summary>
public enum AdjustmentPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public sealed class AdaptiveOptimizationSettings
{
    public double OptimalPoolCapacity { get; set; } = 50;
    public int GpuParallelism { get; set; } = 2;
    public double GpuMemoryAllocationMB { get; set; } = 512;
    public TimeSpan OptimizationInterval { get; set; } = TimeSpan.FromSeconds(30);
    public int OptimalBatchSize { get; set; } = 16;
}

/// <summary>
/// 最適化プール設定
/// </summary>
public sealed class OptimizedPoolConfiguration
{
    public string PoolName { get; set; } = string.Empty;
    public double OptimalCapacity { get; set; }
    public double GpuMemoryAllocationMB { get; set; }
    public bool AdaptiveResize { get; set; }
    public PerformanceTarget PerformanceTarget { get; set; }
    public int MaximumRetained { get; set; } = 50;
}

/// <summary>
/// GPU メモリ割り当て情報
/// </summary>
public sealed class GpuMemoryAllocation
{
    public string AllocationId { get; set; } = string.Empty;
    public double AllocatedMemoryMB { get; set; }
    public DateTime AllocationTime { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// GPU リソーススナップショット
/// </summary>
public sealed class GpuResourceSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int AvailableProviders { get; set; }
    public double EstimatedMemoryUsageMB { get; set; }
    public int ActiveSessions { get; set; }
    public long MemoryUsageMB { get; set; }
    public double GpuMemoryUsagePercent { get; set; }
    public double GpuMemoryAvailableMB { get; set; }
}

/// <summary>
/// 最適化推奨事項
/// </summary>
public sealed class OptimizationRecommendation
{
    public OptimizationType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public RecommendationPriority Priority { get; set; }
    public string EstimatedImpact { get; set; } = string.Empty;
}

/// <summary>
/// 最適化種別
/// </summary>
public enum OptimizationType
{
    IncreasePoolCapacity,
    OptimizeGpuMemory,
    AdaptivePoolResize,
    ReduceGpuSessions
}

/// <summary>
/// 推奨事項優先度
/// </summary>
public enum RecommendationPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

/// <summary>
/// パフォーマンスターゲット
/// </summary>
public enum PerformanceTarget
{
    Memory,      // メモリ効率重視
    Speed,       // 速度重視
    Balanced,    // バランス
    Quality      // 品質重視
}

#endregion
