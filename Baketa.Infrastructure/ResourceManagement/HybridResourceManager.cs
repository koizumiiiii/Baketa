using System.Threading.Channels;
using Baketa.Core.Abstractions.Common;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.Monitoring;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.ResourceManagement;

/// <summary>
/// システム負荷のトレンド方向
/// </summary>
public enum LoadTrend
{
    /// <summary>安定状態</summary>
    Stable,
    /// <summary>負荷上昇トレンド</summary>
    Increasing,
    /// <summary>負荷下降トレンド</summary>
    Decreasing,
    /// <summary>急激な負荷変動</summary>
    Volatile
}

/// <summary>
/// リソース状態スナップショット（トレンド分析用）
/// </summary>
public sealed record ResourceStatusSnapshot(
    double CpuUsage,
    double MemoryUsage,
    double GpuUtilization,
    double VramUsage,
    DateTime Timestamp)
{
    /// <summary>
    /// 総合負荷スコア計算
    /// </summary>
    public double CompositeScore => (CpuUsage + MemoryUsage + GpuUtilization + VramUsage) / 4.0;
}

/// <summary>
/// ハイブリッドリソース管理システム
/// OCRと翻訳処理のリソース競合を防ぐ統合制御システム
/// </summary>
public sealed class HybridResourceManager : IResourceManager, IDisposable
{
    // === パイプライン制御 ===
    private readonly Channel<ProcessingRequest> _ocrChannel;
    private readonly Channel<TranslationRequest> _translationChannel;

    // === 並列度制御（SemaphoreSlimベース） ===
    private SemaphoreSlim _ocrSemaphore;
    private SemaphoreSlim _translationSemaphore;
    private readonly object _semaphoreLock = new();

    // === リソース監視 ===
    private readonly IResourceMonitor _resourceMonitor;
    
    // === GPU環境検出（動的VRAM容量対応） ===
    private readonly IGpuEnvironmentDetector? _gpuEnvironmentDetector;
    private long _actualTotalVramMB = 8192; // デフォルトフォールバック値
    
    // === Phase 4.1: パフォーマンスメトリクス収集統合 ===
    private readonly IPerformanceMetricsCollector? _metricsCollector;

    // === Phase 3: 高度なヒステリシス制御 ===
    private DateTime _lastThresholdCrossTime = DateTime.UtcNow;
    private readonly Queue<ResourceStatusSnapshot> _recentStatusHistory = [];
    private LoadTrend _currentLoadTrend = LoadTrend.Stable;
    private DateTime _lastTrendChangeTime = DateTime.UtcNow;

    // === 設定（Phase 3: ホットリロード対応） ===
    private readonly IOptionsMonitor<HybridResourceSettings> _optionsMonitor;
    private HybridResourceSettings _settings;
    private readonly ILogger<HybridResourceManager> _logger;
    private IDisposable? _settingsChangeSubscription;

    // === 状態管理 ===
    private bool _isInitialized = false;
    private readonly CancellationTokenSource _disposalCts = new();

    public HybridResourceManager(
        IResourceMonitor resourceMonitor,
        IOptionsMonitor<HybridResourceSettings> optionsMonitor,
        ILogger<HybridResourceManager> logger,
        IGpuEnvironmentDetector? gpuEnvironmentDetector = null,
        IPerformanceMetricsCollector? metricsCollector = null)
    {
        ArgumentNullException.ThrowIfNull(resourceMonitor);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(logger);

        _resourceMonitor = resourceMonitor;
        _optionsMonitor = optionsMonitor;
        _settings = optionsMonitor.CurrentValue;
        _logger = logger;
        _gpuEnvironmentDetector = gpuEnvironmentDetector;
        _metricsCollector = metricsCollector;
        
        if (_metricsCollector != null)
        {
            _logger.LogInformation("📊 [PHASE4.1] パフォーマンスメトリクス統合が有効化されました");
        }
        
        // Phase 3: 設定変更の監視を開始
        if (_settings.EnableHotReload)
        {
            _settingsChangeSubscription = _optionsMonitor.OnChange(OnSettingsChanged);
            _logger.LogInformation("🔄 [PHASE3] ホットリロード機能が有効化されました - ポーリング間隔: {Interval}ms", 
                _settings.ConfigurationPollingIntervalMs);
        }

        // BoundedChannel で バックプレッシャー管理
        _ocrChannel = Channel.CreateBounded<ProcessingRequest>(
            new BoundedChannelOptions(_settings.OcrChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });

        _translationChannel = Channel.CreateBounded<TranslationRequest>(
            new BoundedChannelOptions(_settings.TranslationChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });

        // 初期並列度設定
        _ocrSemaphore = new SemaphoreSlim(
            _settings.InitialOcrParallelism,
            _settings.MaxOcrParallelism);

        _translationSemaphore = new SemaphoreSlim(
            _settings.InitialTranslationParallelism,
            _settings.MaxTranslationParallelism);

        // Phase 3: 閾値は設定から直接参照（ホットリロード対応）

        if (_settings.EnableDetailedLogging)
        {
            _logger.LogDebug("HybridResourceManager初期化 - OCR:{OcrParallelism}, Translation:{TranslationParallelism}",
                _settings.InitialOcrParallelism, _settings.InitialTranslationParallelism);
        }
    }

    /// <summary>
    /// リソース管理システムの初期化
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return;

        try
        {
            // IResourceMonitorの初期化
            if (_resourceMonitor is IInitializable initializable && !initializable.IsInitialized)
            {
                initializable.Initialize();
            }

            // リソース監視を開始
            if (!_resourceMonitor.IsMonitoring)
            {
                await _resourceMonitor.StartMonitoringAsync(cancellationToken).ConfigureAwait(false);
            }

            // 🎯 動的VRAM容量取得（8192MB固定問題解決）
            await DetectActualVramCapacityAsync(cancellationToken).ConfigureAwait(false);

            _isInitialized = true;

            _logger.LogInformation("HybridResourceManager初期化完了 - 動的リソース管理開始");

            if (_settings.EnableDetailedLogging)
            {
                _logger.LogDebug("初期設定 - CPU閾値:{CpuLow}-{CpuHigh}%, Memory閾値:{MemLow}-{MemHigh}%",
                    _settings.CpuLowThreshold, _settings.CpuHighThreshold,
                    _settings.MemoryLowThreshold, _settings.MemoryHighThreshold);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HybridResourceManager初期化失敗");
            throw;
        }
    }

    /// <summary>
    /// 現在のリソース状況取得
    /// </summary>
    public async Task<ResourceStatus> GetCurrentResourceStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            await InitializeAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var metrics = await _resourceMonitor.GetCurrentMetricsAsync(cancellationToken).ConfigureAwait(false);

            var status = new ResourceStatus
            {
                CpuUsage = metrics.CpuUsagePercent,
                MemoryUsage = metrics.MemoryUsagePercent,
                GpuUtilization = metrics.GpuUsagePercent ?? 0,
                VramUsage = CalculateVramUsagePercent(metrics),
                Timestamp = DateTime.UtcNow
            };

            // 最適性判定
            status.IsOptimalForOcr = IsOptimalForProcessing(status, isOcrOperation: true);
            status.IsOptimalForTranslation = IsOptimalForProcessing(status, isOcrOperation: false);

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "リソース状況取得失敗 - フォールバック値使用");
            return new ResourceStatus
            {
                CpuUsage = 50,
                MemoryUsage = 50,
                GpuUtilization = 0,
                VramUsage = 0,
                Timestamp = DateTime.UtcNow,
                IsOptimalForOcr = true,
                IsOptimalForTranslation = false
            };
        }
    }

    /// <summary>
    /// リソース状況に基づく動的並列度調整（ヒステリシス付き）
    /// </summary>
    /// <summary>
    /// Phase 3: 高度なヒステリシス付き動的並列度調整
    /// トレンド分析とボラティリティ検出による智能的制御
    /// </summary>
    public async Task AdjustParallelismAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.EnableDynamicParallelism)
            return;

        var status = await GetCurrentResourceStatusAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;

        // Phase 3: リソース状態履歴の記録
        var snapshot = new ResourceStatusSnapshot(
            status.CpuUsage, status.MemoryUsage, status.GpuUtilization, status.VramUsage, now);
        _recentStatusHistory.Enqueue(snapshot);
        
        // 履歴サイズ制限（直近10分間のデータ）
        while (_recentStatusHistory.Count > 0 && 
               (now - _recentStatusHistory.Peek().Timestamp).TotalMinutes > 10)
        {
            _recentStatusHistory.Dequeue();
        }

        // Important: VRAM動的監視統合（Gemini指摘事項対応）
        var vramMonitoring = await MonitorVramDynamicallyAsync(cancellationToken).ConfigureAwait(false);
        
        // Phase 3: 負荷トレンドの分析
        var currentTrend = AnalyzeLoadTrend();
        if (currentTrend != _currentLoadTrend)
        {
            _logger.LogInformation("🔄 [PHASE3] 負荷トレンド変更検出: {OldTrend} → {NewTrend}", 
                _currentLoadTrend, currentTrend);
            _currentLoadTrend = currentTrend;
            _lastTrendChangeTime = now;
        }

        // Important: VRAM監視結果と従来の閾値評価を統合
        var isHighLoad = status.CpuUsage > _settings.CpuHighThreshold ||
                        status.MemoryUsage > _settings.MemoryHighThreshold ||
                        status.GpuUtilization > _settings.GpuHighThreshold ||
                        vramMonitoring.ShouldFallbackToCpu; // VRAM監視統合

        var isLowLoad = status.CpuUsage < _settings.CpuLowThreshold &&
                       status.MemoryUsage < _settings.MemoryLowThreshold &&
                       status.GpuUtilization < _settings.GpuLowThreshold &&
                       vramMonitoring.OptimalForGpuProcessing; // VRAM監視統合

        // Important: VRAM推奨アクション統合による高度な制御（既存enum値使用）
        bool forceAdjustmentDueToVram = false;
        switch (vramMonitoring.RecommendedAction)
        {
            case VramAction.ScaleDown:
            case VramAction.FallbackToCpu:
            case VramAction.EmergencyFallback:
                forceAdjustmentDueToVram = true;
                isHighLoad = true;
                _logger.LogWarning("⚠️ [VRAM統合] VRAM圧迫による処理削減推奨 - 圧迫度: {Pressure}, アクション: {Action}", 
                    vramMonitoring.PressureLevel, vramMonitoring.RecommendedAction);
                break;
            
            case VramAction.ScaleUp:
                if (!isHighLoad && vramMonitoring.OptimalForGpuProcessing)
                {
                    forceAdjustmentDueToVram = true;
                    isLowLoad = true;
                    _logger.LogInformation("📈 [VRAM統合] VRAM最適状態による処理増強推奨 - 圧迫度: {Pressure}", 
                        vramMonitoring.PressureLevel);
                }
                break;
                
            case VramAction.Maintain:
                // VRAM状況は安定、従来ロジックを維持
                if (_settings.EnableVerboseLogging)
                {
                    _logger.LogTrace("✅ [VRAM統合] VRAM状況安定 - 従来制御継続 圧迫度: {Pressure}", 
                        vramMonitoring.PressureLevel);
                }
                break;
        }

        // Phase 3: 高度なヒステリシス制御（VRAM統合考慮）
        var shouldAdjust = ShouldAdjustParallelism(isHighLoad, isLowLoad, currentTrend, now);

        // Important: VRAM統合による強制調整の適用
        if (forceAdjustmentDueToVram)
        {
            shouldAdjust = (isHighLoad && !isLowLoad, !isHighLoad && isLowLoad);
        }

        if (shouldAdjust.Decrease)
        {
            await DecreaseParallelismAsync().ConfigureAwait(false);
            _lastThresholdCrossTime = now;
            _logger.LogWarning("🔻 [VRAM統合] 並列度減少実行: CPU={Cpu:F1}%, Memory={Memory:F1}%, GPU={Gpu:F1}%, " +
                "VRAM={Vram:F1}%({Pressure}), トレンド={Trend}, アクション={Action}", 
                status.CpuUsage, status.MemoryUsage, status.GpuUtilization, 
                vramMonitoring.CurrentUsagePercent, vramMonitoring.PressureLevel, 
                currentTrend, vramMonitoring.RecommendedAction);
        }
        else if (shouldAdjust.Increase)
        {
            await IncreaseParallelismAsync().ConfigureAwait(false);
            _lastThresholdCrossTime = now;
            _logger.LogInformation("🔺 [VRAM統合] 並列度増加実行: CPU={Cpu:F1}%, Memory={Memory:F1}%, GPU={Gpu:F1}%, " +
                "VRAM={Vram:F1}%({Pressure}), トレンド={Trend}, アクション={Action}", 
                status.CpuUsage, status.MemoryUsage, status.GpuUtilization, 
                vramMonitoring.CurrentUsagePercent, vramMonitoring.PressureLevel, 
                currentTrend, vramMonitoring.RecommendedAction);
        }
        else if (_settings.EnableVerboseLogging)
        {
            _logger.LogTrace("⚖️ [VRAM統合] 並列度調整不要 - 安定状態維持: トレンド={Trend}, VRAM圧迫度={Pressure}, " +
                "待機時間={Wait:F1}秒", currentTrend, vramMonitoring.PressureLevel, 
                (now - _lastThresholdCrossTime).TotalSeconds);
        }
    }

    /// <summary>
    /// OCR処理実行（リソース制御付き）
    /// 実際の処理を関数として受け取り、リソース管理下で実行する
    /// </summary>
    public async Task<TResult> ProcessOcrAsync<TResult>(
        Func<ProcessingRequest, CancellationToken, Task<TResult>> ocrTaskFactory, 
        ProcessingRequest request, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ocrTaskFactory);
        ArgumentNullException.ThrowIfNull(request);

        if (!_isInitialized)
            await InitializeAsync(cancellationToken).ConfigureAwait(false);

        // チャネルに投入（バックプレッシャー対応）
        await _ocrChannel.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);

        // リソース取得待機
        await _ocrSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 実際のOCR処理を関数で実行し、結果を受け取る
            var result = await ocrTaskFactory(request, cancellationToken).ConfigureAwait(false);

            if (_settings.EnableDetailedLogging)
            {
                _logger.LogDebug("OCR処理完了: {OperationId}", request.OperationId);
            }

            return result;
        }
        finally
        {
            _ocrSemaphore.Release();
        }
    }

    /// <summary>
    /// 翻訳処理実行（動的クールダウン付き）
    /// 実際の処理を関数として受け取り、リソース管理下で実行する
    /// </summary>
    public async Task<TResult> ProcessTranslationAsync<TResult>(
        Func<TranslationRequest, CancellationToken, Task<TResult>> translationTaskFactory,
        TranslationRequest request, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(translationTaskFactory);
        ArgumentNullException.ThrowIfNull(request);

        if (!_isInitialized)
            await InitializeAsync(cancellationToken).ConfigureAwait(false);

        // 動的クールダウン計算
        var cooldownMs = await CalculateDynamicCooldownAsync(cancellationToken).ConfigureAwait(false);
        if (cooldownMs > 0)
        {
            if (_settings.EnableDetailedLogging)
            {
                _logger.LogDebug("翻訳前クールダウン: {Cooldown}ms (OperationId: {OperationId})", cooldownMs, request.OperationId);
            }
            await Task.Delay(cooldownMs, cancellationToken).ConfigureAwait(false);
        }

        // チャネルに投入
        await _translationChannel.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);

        // リソース取得待機
        await _translationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 実際の翻訳処理を関数で実行し、結果を受け取る
            var result = await translationTaskFactory(request, cancellationToken).ConfigureAwait(false);

            if (_settings.EnableDetailedLogging)
            {
                _logger.LogDebug("翻訳処理完了: {OperationId}", request.OperationId);
            }

            return result;
        }
        finally
        {
            _translationSemaphore.Release();
        }
    }

    /// <summary>
    /// 動的クールダウン時間計算
    /// </summary>
    /// <summary>
    /// Phase 3: 高度な動的クールダウン計算
    /// トレンド分析・予測制御・アダプティブ調整機能搭載
    /// </summary>
    private async Task<int> CalculateDynamicCooldownAsync(CancellationToken cancellationToken)
    {
        var status = await GetCurrentResourceStatusAsync(cancellationToken).ConfigureAwait(false);

        // Phase 3: 基本負荷係数計算（改良版）
        var cpuFactor = CalculateAdaptiveFactor(status.CpuUsage, 50, 80, 1.2);      // CPU重要度 x1.2
        var memoryFactor = CalculateAdaptiveFactor(status.MemoryUsage, 60, 85, 1.0); // メモリ標準重要度
        var gpuFactor = CalculateAdaptiveFactor(status.GpuUtilization, 40, 75, 1.1); // GPU重要度 x1.1  
        var vramFactor = CalculateAdaptiveFactor(status.VramUsage, 50, 80, 1.3);     // VRAM最高重要度 x1.3

        // Phase 3: 重み付け総合負荷スコア
        var weightedScore = (cpuFactor * 1.2 + memoryFactor * 1.0 + gpuFactor * 1.1 + vramFactor * 1.3) / 4.6;
        
        // Phase 3: トレンド係数による調整
        var trendMultiplier = CalculateTrendMultiplier(_currentLoadTrend);
        
        // Phase 3: 履歴ベース予測調整
        var predictiveAdjustment = CalculatePredictiveAdjustment();
        
        // Phase 3: 最終クールダウン計算
        var baseCooldown = weightedScore * _settings.MaxCooldownMs;
        var trendAdjustedCooldown = baseCooldown * trendMultiplier;
        var finalCooldown = trendAdjustedCooldown + predictiveAdjustment;
        
        // 範囲制限と整数化
        var result = Math.Max(0, Math.Min((int)finalCooldown, _settings.MaxCooldownMs * 2)); // 最大2倍まで延長可能
        
        // Phase 3: 詳細ログ（設定有効時）
        if (_settings.EnableVerboseLogging)
        {
            _logger.LogTrace("🕒 [PHASE3] 高度動的クールダウン計算: " +
                "基本={Base:F0}ms, トレンド係数={Trend:F2}, 予測調整={Predict:+F0}ms, 最終={Final}ms, " +
                "負荷スコア={Score:F3} (CPU:{Cpu:F2}×{CpuW}, Mem:{Mem:F2}×{MemW}, GPU:{Gpu:F2}×{GpuW}, VRAM:{Vram:F2}×{VramW})",
                baseCooldown, trendMultiplier, predictiveAdjustment, result, weightedScore,
                cpuFactor, 1.2, memoryFactor, 1.0, gpuFactor, 1.1, vramFactor, 1.3);
        }
        
        return result;
    }

    /// <summary>
    /// Phase 3: アダプティブ負荷係数計算（非線形カーブ対応）
    /// </summary>
    private static double CalculateAdaptiveFactor(double usage, double lowThreshold, double highThreshold, double weight)
    {
        if (usage <= lowThreshold)
            return 0.0;
        
        var normalizedUsage = Math.Min(1.0, (usage - lowThreshold) / (highThreshold - lowThreshold));
        
        // 非線形カーブ適用（二次関数：高負荷時により敏感に反応）
        var curveAdjusted = Math.Pow(normalizedUsage, 1.5); 
        
        return curveAdjusted * weight;
    }

    /// <summary>
    /// Phase 3: トレンド係数による動的調整
    /// </summary>
    private double CalculateTrendMultiplier(LoadTrend trend)
    {
        return trend switch
        {
            LoadTrend.Stable => 1.0,      // 標準倍率
            LoadTrend.Decreasing => 0.7,  // 下降トレンド：クールダウン短縮
            LoadTrend.Increasing => 1.4,  // 上昇トレンド：クールダウン延長
            LoadTrend.Volatile => 1.6,    // 不安定：大幅延長で安定化
            _ => 1.0
        };
    }

    /// <summary>
    /// Phase 3: 履歴ベース予測調整
    /// </summary>
    private double CalculatePredictiveAdjustment()
    {
        if (_recentStatusHistory.Count < 3)
            return 0.0;

        var recent = _recentStatusHistory.TakeLast(3).ToArray();
        
        // 短期トレンド検出（直近3サンプル）
        var scores = recent.Select(r => r.CompositeScore).ToArray();
        var trend = scores.Length >= 2 ? scores[^1] - scores[^2] : 0.0;
        
        // 急激な負荷上昇の予測
        if (trend > 5.0) // 5%以上の急上昇
        {
            var severity = Math.Min(trend / 10.0, 1.0); // 最大+100msまで
            return severity * 100; // 予防的クールダウン延長
        }
        
        // 安定継続の検出
        var variance = CalculateVariance(scores);
        if (variance < 2.0) // 非常に安定
        {
            return -30; // 安定時はクールダウン短縮
        }
        
        return 0.0; // 標準状態
    }

    /// <summary>
    /// Phase 3: 分散計算ヘルパー
    /// </summary>
    private static double CalculateVariance(double[] values)
    {
        if (values.Length < 2) return 0.0;
        
        var mean = values.Average();
        return values.Select(v => Math.Pow(v - mean, 2)).Average();
    }

    /// <summary>
    /// 実際のVRAM容量を動的に検出
    /// </summary>
    private async Task DetectActualVramCapacityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_gpuEnvironmentDetector != null)
            {
                var gpuInfo = await _gpuEnvironmentDetector.DetectEnvironmentAsync(cancellationToken).ConfigureAwait(false);
                
                if (gpuInfo != null && gpuInfo.AvailableMemoryMB > 0)
                {
                    _actualTotalVramMB = gpuInfo.AvailableMemoryMB;
                    _logger.LogInformation("🎯 [VRAM-FIX] 動的VRAM容量検出成功: {ActualVramMB}MB (GPU: {GpuName})", 
                        _actualTotalVramMB, gpuInfo.GpuName);
                }
                else
                {
                    _logger.LogWarning("⚠️ [VRAM-FIX] GPU情報の取得に失敗、デフォルト値を使用: {DefaultVramMB}MB", _actualTotalVramMB);
                }
            }
            else
            {
                _logger.LogDebug("📝 [VRAM-FIX] IGpuEnvironmentDetectorが注入されていないため、デフォルト値を使用: {DefaultVramMB}MB", _actualTotalVramMB);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [VRAM-FIX] VRAM容量検出エラー、デフォルト値を使用: {DefaultVramMB}MB", _actualTotalVramMB);
        }
    }

    /// <summary>
    /// VRAMの使用率パーセンテージを計算（動的VRAM容量対応）
    /// Sprint 3: 高度なVRAM監視機能拡張
    /// </summary>
    private double CalculateVramUsagePercent(ResourceMetrics metrics)
    {
        if (!metrics.GpuMemoryUsageMB.HasValue)
            return 0;

        // 🎯 動的VRAM容量を使用（8192MB固定問題解決済み）
        var usagePercent = (double)metrics.GpuMemoryUsageMB.Value / _actualTotalVramMB * 100;
        
        return Math.Min(100, Math.Max(0, usagePercent));
    }

    /// <summary>
    /// Sprint 3: 拡張VRAM監視とGPU段階的制御
    /// </summary>
    private async Task<VramMonitoringResult> MonitorVramDynamicallyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await GetCurrentResourceStatusAsync(cancellationToken).ConfigureAwait(false);
            var vramUsagePercent = status.VramUsage;
            var vramUsageMB = (long)(vramUsagePercent / 100.0 * _actualTotalVramMB);
            var availableVramMB = _actualTotalVramMB - vramUsageMB;
            
            // Sprint 3: VRAM圧迫度分析
            var vramPressure = CalculateVramPressureLevel(vramUsagePercent);
            var recommendedAction = DetermineVramAction(vramPressure, vramUsagePercent);
            
            var result = new VramMonitoringResult
            {
                CurrentUsagePercent = vramUsagePercent,
                CurrentUsageMB = vramUsageMB,
                TotalCapacityMB = _actualTotalVramMB,
                AvailableMB = availableVramMB,
                PressureLevel = vramPressure,
                RecommendedAction = recommendedAction,
                ShouldFallbackToCpu = vramUsagePercent > _settings.VramHighThreshold,
                OptimalForGpuProcessing = vramUsagePercent < _settings.VramLowThreshold,
                Timestamp = DateTime.UtcNow
            };

            if (_settings.EnableVerboseLogging)
            {
                _logger.LogDebug("📊 Sprint 3 VRAM動的監視: 使用率={Usage:F1}% ({UsageMB}MB/{TotalMB}MB), " +
                    "圧迫度={Pressure}, 推奨アクション={Action}, CPU切替={Fallback}",
                    vramUsagePercent, vramUsageMB, _actualTotalVramMB, 
                    vramPressure, recommendedAction, result.ShouldFallbackToCpu);
            }

            // Phase 4.1: VRAM監視メトリクス記録（将来実装予定）
            if (_metricsCollector != null && _settings.EnableVerboseLogging)
            {
                _logger.LogDebug("📊 Phase 4.1: VRAM監視メトリクス記録 - 使用率={Usage:F1}%, 圧迫度={Pressure}", 
                    vramUsagePercent, vramPressure);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Sprint 3 VRAM動的監視エラー");
            return new VramMonitoringResult
            {
                CurrentUsagePercent = 0,
                PressureLevel = VramPressureLevel.Unknown,
                RecommendedAction = VramAction.Maintain,
                ShouldFallbackToCpu = true, // エラー時は安全側に倒す
                Timestamp = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Sprint 3: VRAM圧迫度レベル計算
    /// </summary>
    private VramPressureLevel CalculateVramPressureLevel(double vramUsagePercent)
    {
        return vramUsagePercent switch
        {
            < 40 => VramPressureLevel.Low,
            < 60 => VramPressureLevel.Moderate, 
            < 75 => VramPressureLevel.High,
            < 90 => VramPressureLevel.Critical,
            _ => VramPressureLevel.Emergency
        };
    }

    /// <summary>
    /// Sprint 3: VRAM状況に基づく推奨アクション決定
    /// </summary>
    private VramAction DetermineVramAction(VramPressureLevel pressureLevel, double vramUsagePercent)
    {
        return pressureLevel switch
        {
            VramPressureLevel.Low => VramAction.ScaleUp,
            VramPressureLevel.Moderate => VramAction.Maintain,
            VramPressureLevel.High => VramAction.ScaleDown,
            VramPressureLevel.Critical => VramAction.FallbackToCpu,
            VramPressureLevel.Emergency => VramAction.EmergencyFallback,
            _ => VramAction.Maintain
        };
    }

    /// <summary>
    /// Sprint 3: VRAM監視結果
    /// </summary>
    private sealed record VramMonitoringResult
    {
        public double CurrentUsagePercent { get; init; }
        public long CurrentUsageMB { get; init; }
        public long TotalCapacityMB { get; init; }
        public long AvailableMB { get; init; }
        public VramPressureLevel PressureLevel { get; init; }
        public VramAction RecommendedAction { get; init; }
        public bool ShouldFallbackToCpu { get; init; }
        public bool OptimalForGpuProcessing { get; init; }
        public DateTime Timestamp { get; init; }
    }

    /// <summary>
    /// Sprint 3: VRAM圧迫度レベル
    /// </summary>
    private enum VramPressureLevel
    {
        Unknown,
        Low,        // < 40%
        Moderate,   // 40-60%
        High,       // 60-75%
        Critical,   // 75-90%
        Emergency   // > 90%
    }

    /// <summary>
    /// Sprint 3: VRAM状況に基づく推奨アクション
    /// </summary>
    private enum VramAction
    {
        ScaleUp,           // GPU処理増強
        Maintain,          // 現状維持
        ScaleDown,         // GPU処理削減
        FallbackToCpu,     // CPU切替推奨
        EmergencyFallback  // 緊急CPU切替
    }

    /// <summary>
    /// 処理に最適なリソース状況かどうか判定
    /// </summary>
    private bool IsOptimalForProcessing(ResourceStatus status, bool isOcrOperation)
    {
        // OCRの場合はより厳しい基準、翻訳はより緩い基準
        var cpuThreshold = isOcrOperation ? _settings.CpuHighThreshold - 10 : _settings.CpuHighThreshold;
        var memoryThreshold = isOcrOperation ? _settings.MemoryHighThreshold - 5 : _settings.MemoryHighThreshold;

        return status.CpuUsage < cpuThreshold &&
               status.MemoryUsage < memoryThreshold &&
               status.GpuUtilization < _settings.GpuHighThreshold &&
               status.VramUsage < _settings.VramHighThreshold;
    }


    /// <summary>
    /// 並列度減少（SemaphoreSlim再作成方式）
    /// Phase 4.1: メトリクス記録統合
    /// </summary>
    private async Task DecreaseParallelismAsync()
    {
        var status = await GetCurrentResourceStatusAsync(_disposalCts.Token).ConfigureAwait(false);
        
        lock (_semaphoreLock)
        {
            // 翻訳の並列度を優先的に削減
            var currentTranslation = _translationSemaphore.CurrentCount;
            if (currentTranslation > 1)
            {
                var newCount = Math.Max(1, currentTranslation - 1);
                RecreateSemaphore(ref _translationSemaphore, newCount, _settings.MaxTranslationParallelism);
                _logger.LogInformation("翻訳並列度減少: {Old} → {New}", currentTranslation, newCount);
                
                // Phase 4.1: リソース調整メトリクス記録
                RecordResourceAdjustmentMetrics("Translation", "DecreaseParallelism", currentTranslation, newCount, "High load detected", status);
                return;
            }

            // それでも不足ならOCRも削減
            var currentOcr = _ocrSemaphore.CurrentCount;
            if (currentOcr > 1 && _translationSemaphore.CurrentCount == 1)
            {
                var newCount = Math.Max(1, currentOcr - 1);
                RecreateSemaphore(ref _ocrSemaphore, newCount, _settings.MaxOcrParallelism);
                _logger.LogInformation("OCR並列度減少: {Old} → {New}", currentOcr, newCount);
                
                // Phase 4.1: リソース調整メトリクス記録
                RecordResourceAdjustmentMetrics("OCR", "DecreaseParallelism", currentOcr, newCount, "High load + Translation at minimum", status);
            }
        }

        // 少し待機してセマフォの状態を安定させる
        await Task.Delay(100, _disposalCts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// 並列度増加（段階的）
    /// Phase 4.1: メトリクス記録統合
    /// </summary>
    private async Task IncreaseParallelismAsync()
    {
        var status = await GetCurrentResourceStatusAsync(_disposalCts.Token).ConfigureAwait(false);
        
        lock (_semaphoreLock)
        {
            // OCRの並列度を優先的に回復
            var currentOcr = _ocrSemaphore.CurrentCount;
            if (currentOcr < _settings.MaxOcrParallelism)
            {
                var newCount = Math.Min(_settings.MaxOcrParallelism, currentOcr + 1);
                RecreateSemaphore(ref _ocrSemaphore, newCount, _settings.MaxOcrParallelism);
                _logger.LogInformation("OCR並列度増加: {Old} → {New}", currentOcr, newCount);
                
                // Phase 4.1: リソース調整メトリクス記録
                RecordResourceAdjustmentMetrics("OCR", "IncreaseParallelism", currentOcr, newCount, "Low load detected - OCR priority recovery", status);
                return;
            }

            // OCRが安定したら翻訳も増加
            var currentTranslation = _translationSemaphore.CurrentCount;
            if (currentTranslation < _settings.MaxTranslationParallelism &&
                _ocrSemaphore.CurrentCount >= 2)
            {
                var newCount = Math.Min(_settings.MaxTranslationParallelism, currentTranslation + 1);
                RecreateSemaphore(ref _translationSemaphore, newCount, _settings.MaxTranslationParallelism);
                _logger.LogInformation("翻訳並列度増加: {Old} → {New}", currentTranslation, newCount);
                
                // Phase 4.1: リソース調整メトリクス記録
                RecordResourceAdjustmentMetrics("Translation", "IncreaseParallelism", currentTranslation, newCount, "Low load + OCR stable", status);
            }
        }

        // 少し待機してセマフォの状態を安定させる
        await Task.Delay(100, _disposalCts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// セマフォ再作成（並列度変更のため）
    /// </summary>
    private void RecreateSemaphore(ref SemaphoreSlim semaphore, int newCount, int maxCount)
    {
        var oldSemaphore = semaphore;
        semaphore = new SemaphoreSlim(newCount, maxCount);

        // 古いセマフォの全待機者を解放（非同期で）
        Task.Run(async () =>
        {
            // 最大数までリリースを試行
            for (int i = 0; i < maxCount; i++)
            {
                try { oldSemaphore.Release(); }
                catch { break; }
            }

            // 少し待機してから解放
            await Task.Delay(200);
            oldSemaphore.Dispose();
        }, _disposalCts.Token);
    }

    /// <summary>
    /// Phase 3: 負荷トレンド分析
    /// </summary>
    private LoadTrend AnalyzeLoadTrend()
    {
        if (_recentStatusHistory.Count < 3)
            return LoadTrend.Stable;

        var snapshots = _recentStatusHistory.ToArray();
        var recentScores = snapshots.TakeLast(5).Select(s => s.CompositeScore).ToArray();
        
        if (recentScores.Length < 3)
            return LoadTrend.Stable;

        // 線形回帰による傾向分析
        var n = recentScores.Length;
        var xMean = (n - 1) / 2.0;
        var yMean = recentScores.Average();
        
        var numerator = 0.0;
        var denominator = 0.0;
        
        for (int i = 0; i < n; i++)
        {
            var x = i;
            var y = recentScores[i];
            numerator += (x - xMean) * (y - yMean);
            denominator += (x - xMean) * (x - xMean);
        }
        
        var slope = denominator != 0 ? numerator / denominator : 0.0;
        
        // ボラティリティ計算（標準偏差）
        var variance = recentScores.Select(s => Math.Pow(s - yMean, 2)).Average();
        var volatility = Math.Sqrt(variance);
        
        // トレンド判定
        const double trendThreshold = 2.0; // 傾きの閾値
        const double volatilityThreshold = 15.0; // ボラティリティ閾値
        
        if (volatility > volatilityThreshold)
            return LoadTrend.Volatile;
        
        if (slope > trendThreshold)
            return LoadTrend.Increasing;
        
        if (slope < -trendThreshold)
            return LoadTrend.Decreasing;
        
        return LoadTrend.Stable;
    }

    /// <summary>
    /// Phase 3: 高度なヒステリシス判定ロジック
    /// </summary>
    private (bool Increase, bool Decrease) ShouldAdjustParallelism(
        bool isHighLoad, bool isLowLoad, LoadTrend trend, DateTime now)
    {
        var timeSinceLastAdjustment = (now - _lastThresholdCrossTime).TotalSeconds;
        var timeSinceLastTrendChange = (now - _lastTrendChangeTime).TotalSeconds;
        
        // 高負荷時の即座対応（従来通り）
        if (isHighLoad)
        {
            // ただし、Volatileトレンド中は頻繁な調整を避ける
            if (trend == LoadTrend.Volatile && timeSinceLastAdjustment < _settings.HysteresisTimeoutSeconds * 2)
                return (false, false);
                
            return (false, true); // 減少
        }
        
        // 低負荷時の智能的判定
        if (isLowLoad)
        {
            var baseWaitTime = _settings.HysteresisTimeoutSeconds;
            var adjustedWaitTime = CalculateAdaptiveWaitTime(trend, baseWaitTime, timeSinceLastTrendChange);
            
            if (timeSinceLastAdjustment > adjustedWaitTime)
            {
                return (true, false); // 増加
            }
        }
        
        return (false, false); // 調整なし
    }

    /// <summary>
    /// Phase 3: トレンド適応型待機時間計算
    /// </summary>
    private double CalculateAdaptiveWaitTime(LoadTrend trend, double baseWaitTime, double timeSinceLastTrendChange)
    {
        return trend switch
        {
            LoadTrend.Stable => baseWaitTime, // 基本待機時間
            LoadTrend.Decreasing => Math.Max(baseWaitTime * 0.7, 2.0), // 下降トレンド：早めに増加
            LoadTrend.Increasing => baseWaitTime * 1.5, // 上昇トレンド：慎重に待機
            LoadTrend.Volatile => Math.Max(baseWaitTime * 2.0, Math.Min(timeSinceLastTrendChange * 0.5, baseWaitTime * 3.0)), // 不安定：大幅延長
            _ => baseWaitTime
        };
    }

    /// <summary>
    /// Phase 3: 設定変更時のコールバック処理（ホットリロード）
    /// </summary>
    private async void OnSettingsChanged(HybridResourceSettings newSettings, string? name)
    {
        try
        {
            var oldSettings = _settings;
            var differences = oldSettings.GetDifferences(newSettings);
            
            if (!differences.Any())
            {
                if (newSettings.EnableVerboseLogging)
                {
                    _logger.LogDebug("🔄 [PHASE3] 設定変更検出されましたが、重要な変更はありません");
                }
                return;
            }

            // 設定妥当性検証
            if (!newSettings.IsValid())
            {
                _logger.LogWarning("⚠️ [PHASE3] 無効な設定値が検出されました。変更を無視します: {InvalidSettings}", 
                    string.Join(", ", differences));
                return;
            }

            _logger.LogInformation("🔄 [PHASE3] 設定変更を適用中: {Changes}", 
                string.Join(", ", differences));

            // 設定を原子的に更新
            _settings = newSettings;

            // 重要な設定変更に対するアクション
            await ApplyDynamicSettingsChanges(oldSettings, newSettings);

            _logger.LogInformation("✅ [PHASE3] 設定変更が正常に適用されました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] 設定変更適用中にエラーが発生しました");
        }
    }

    /// <summary>
    /// Phase 3: 動的設定変更の適用処理
    /// </summary>
    private async Task ApplyDynamicSettingsChanges(HybridResourceSettings oldSettings, HybridResourceSettings newSettings)
    {
        // 並列度制限の変更
        if (oldSettings.MaxOcrParallelism != newSettings.MaxOcrParallelism ||
            oldSettings.MaxTranslationParallelism != newSettings.MaxTranslationParallelism)
        {
            await ApplyParallelismLimitChanges(oldSettings, newSettings);
        }

        // 閾値変更の適用
        if (Math.Abs(oldSettings.CpuHighThreshold - newSettings.CpuHighThreshold) > 0.1 ||
            Math.Abs(oldSettings.MemoryHighThreshold - newSettings.MemoryHighThreshold) > 0.1 ||
            Math.Abs(oldSettings.GpuHighThreshold - newSettings.GpuHighThreshold) > 0.1 ||
            Math.Abs(oldSettings.VramHighThreshold - newSettings.VramHighThreshold) > 0.1)
        {
            ApplyThresholdChanges(newSettings);
        }

        // ログレベル変更の即時適用
        if (oldSettings.EnableVerboseLogging != newSettings.EnableVerboseLogging)
        {
            _logger.LogInformation("🔄 [PHASE3] 詳細ログ設定変更: {OldValue} → {NewValue}",
                oldSettings.EnableVerboseLogging, newSettings.EnableVerboseLogging);
        }
    }

    /// <summary>
    /// Phase 3: 並列度制限の動的変更
    /// </summary>
    private async Task ApplyParallelismLimitChanges(HybridResourceSettings oldSettings, HybridResourceSettings newSettings)
    {
        lock (_semaphoreLock)
        {
            try
            {
                // OCR並列度制限の変更
                if (oldSettings.MaxOcrParallelism != newSettings.MaxOcrParallelism)
                {
                    var currentOcrCount = _ocrSemaphore.CurrentCount;
                    var newOcrSemaphore = new SemaphoreSlim(
                        Math.Min(currentOcrCount, newSettings.MaxOcrParallelism),
                        newSettings.MaxOcrParallelism);

                    _ocrSemaphore.Dispose();
                    _ocrSemaphore = newOcrSemaphore;
                    
                    _logger.LogInformation("🔄 [PHASE3] OCR並列度制限変更: {Old} → {New} (現在: {Current})",
                        oldSettings.MaxOcrParallelism, newSettings.MaxOcrParallelism, currentOcrCount);
                }

                // Translation並列度制限の変更
                if (oldSettings.MaxTranslationParallelism != newSettings.MaxTranslationParallelism)
                {
                    var currentTranslationCount = _translationSemaphore.CurrentCount;
                    var newTranslationSemaphore = new SemaphoreSlim(
                        Math.Min(currentTranslationCount, newSettings.MaxTranslationParallelism),
                        newSettings.MaxTranslationParallelism);

                    _translationSemaphore.Dispose();
                    _translationSemaphore = newTranslationSemaphore;
                    
                    _logger.LogInformation("🔄 [PHASE3] Translation並列度制限変更: {Old} → {New} (現在: {Current})",
                        oldSettings.MaxTranslationParallelism, newSettings.MaxTranslationParallelism, currentTranslationCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [PHASE3] 並列度制限変更中にエラーが発生しました");
                throw;
            }
        }
    }

    /// <summary>
    /// Phase 3: 閾値設定の動的変更
    /// </summary>
    private void ApplyThresholdChanges(HybridResourceSettings newSettings)
    {
        try
        {
            // Phase 3: 閾値設定の動的変更完了（_settingsから直接参照）
            
            _logger.LogInformation("🔄 [PHASE3] リソース閾値変更が適用されました: CPU:{CpuHigh}%, Memory:{MemoryHigh}%, GPU:{GpuHigh}%, VRAM:{VramHigh}%",
                newSettings.CpuHighThreshold, newSettings.MemoryHighThreshold, 
                newSettings.GpuHighThreshold, newSettings.VramHighThreshold);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3] 閾値設定変更中にエラーが発生しました");
            throw;
        }
    }
    
    /// <summary>
    /// Phase 4.1: リソース調整メトリクス記録ヘルパー
    /// </summary>
    private void RecordResourceAdjustmentMetrics(
        string componentName, 
        string adjustmentType, 
        int oldValue, 
        int newValue, 
        string reason, 
        ResourceStatus status)
    {
        if (_metricsCollector == null) return;
        
        try
        {
            var metrics = new ResourceAdjustmentMetrics
            {
                ComponentName = componentName,
                AdjustmentType = adjustmentType,
                OldValue = oldValue,
                NewValue = newValue,
                Reason = reason,
                CpuUsage = status.CpuUsage,
                MemoryUsage = status.MemoryUsage,
                GpuUtilization = status.GpuUtilization,
                VramUsage = status.VramUsage,
                Timestamp = DateTime.UtcNow
            };
            
            _metricsCollector.RecordResourceAdjustment(metrics);
            
            if (_settings.EnableVerboseLogging)
            {
                _logger.LogTrace("📊 [PHASE4.1] リソース調整メトリクス記録: {Component} {Type} {OldValue}→{NewValue}",
                    componentName, adjustmentType, oldValue, newValue);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [PHASE4.1] リソース調整メトリクス記録失敗 - 処理続行");
        }
    }

    public void Dispose()
    {
        if (_disposalCts.IsCancellationRequested)
            return;

        _disposalCts.Cancel();

        try
        {
            // Phase 3: 設定変更監視の停止
            _settingsChangeSubscription?.Dispose();
            
            _ocrSemaphore?.Dispose();
            _translationSemaphore?.Dispose();
            _ocrChannel?.Writer.TryComplete();
            _translationChannel?.Writer.TryComplete();
            _resourceMonitor?.Dispose();

            _logger.LogInformation("🔄 [PHASE3] HybridResourceManager正常終了（ホットリロード機能含む）");
            
            // Phase 4.1: メトリクスコレクターの終了処理
            _metricsCollector?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HybridResourceManager終了処理エラー");
        }
        finally
        {
            _disposalCts.Dispose();
        }
    }
}