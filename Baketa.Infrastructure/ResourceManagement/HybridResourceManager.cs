using System.Threading.Channels;
using Baketa.Core.Abstractions.Common;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.Monitoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.ResourceManagement;

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
    private readonly ResourceThresholds _thresholds;
    
    // === GPU環境検出（動的VRAM容量対応） ===
    private readonly IGpuEnvironmentDetector? _gpuEnvironmentDetector;
    private long _actualTotalVramMB = 8192; // デフォルトフォールバック値

    // === ヒステリシス制御 ===
    private DateTime _lastThresholdCrossTime = DateTime.UtcNow;

    // === 設定 ===
    private readonly HybridResourceSettings _settings;
    private readonly ILogger<HybridResourceManager> _logger;

    // === 状態管理 ===
    private bool _isInitialized = false;
    private readonly CancellationTokenSource _disposalCts = new();

    public HybridResourceManager(
        IResourceMonitor resourceMonitor,
        IOptions<HybridResourceSettings> settings,
        ILogger<HybridResourceManager> logger,
        IGpuEnvironmentDetector? gpuEnvironmentDetector = null)
    {
        ArgumentNullException.ThrowIfNull(resourceMonitor);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _resourceMonitor = resourceMonitor;
        _settings = settings.Value;
        _logger = logger;
        _gpuEnvironmentDetector = gpuEnvironmentDetector;

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

        // 閾値設定（外部化可能）
        _thresholds = new ResourceThresholds
        {
            CpuLowThreshold = _settings.CpuLowThreshold,
            CpuHighThreshold = _settings.CpuHighThreshold,
            MemoryLowThreshold = _settings.MemoryLowThreshold,
            MemoryHighThreshold = _settings.MemoryHighThreshold,
            GpuLowThreshold = _settings.GpuLowThreshold,
            GpuHighThreshold = _settings.GpuHighThreshold,
            VramLowThreshold = _settings.VramLowThreshold,
            VramHighThreshold = _settings.VramHighThreshold
        };

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
                    _thresholds.CpuLowThreshold, _thresholds.CpuHighThreshold,
                    _thresholds.MemoryLowThreshold, _thresholds.MemoryHighThreshold);
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
    public async Task AdjustParallelismAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.EnableDynamicParallelism)
            return;

        var status = await GetCurrentResourceStatusAsync(cancellationToken).ConfigureAwait(false);

        // 全リソースの負荷評価
        var isHighLoad = status.CpuUsage > _thresholds.CpuHighThreshold ||
                        status.MemoryUsage > _thresholds.MemoryHighThreshold ||
                        status.GpuUtilization > _thresholds.GpuHighThreshold ||
                        status.VramUsage > _thresholds.VramHighThreshold;

        var isLowLoad = status.CpuUsage < _thresholds.CpuLowThreshold &&
                       status.MemoryUsage < _thresholds.MemoryLowThreshold &&
                       status.GpuUtilization < _thresholds.GpuLowThreshold &&
                       status.VramUsage < _thresholds.VramLowThreshold;

        var now = DateTime.UtcNow;

        // 高負荷時: 即座に並列度減少
        if (isHighLoad)
        {
            await DecreaseParallelismAsync().ConfigureAwait(false);
            _lastThresholdCrossTime = now;
            _logger.LogWarning("高負荷検出 - 並列度を減少: CPU={Cpu:F1}%, Memory={Memory:F1}%, GPU={Gpu:F1}%, VRAM={Vram:F1}%",
                status.CpuUsage, status.MemoryUsage, status.GpuUtilization, status.VramUsage);
        }
        // 低負荷時: ヒステリシス期間経過後に並列度増加
        else if (isLowLoad &&
                (now - _lastThresholdCrossTime).TotalSeconds > _settings.HysteresisTimeoutSeconds)
        {
            await IncreaseParallelismAsync().ConfigureAwait(false);
            _lastThresholdCrossTime = now;
            _logger.LogInformation("低負荷継続 - 並列度を増加: CPU={Cpu:F1}%, Memory={Memory:F1}%, GPU={Gpu:F1}%, VRAM={Vram:F1}%",
                status.CpuUsage, status.MemoryUsage, status.GpuUtilization, status.VramUsage);
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
    private async Task<int> CalculateDynamicCooldownAsync(CancellationToken cancellationToken)
    {
        var status = await GetCurrentResourceStatusAsync(cancellationToken).ConfigureAwait(false);

        // リソース使用率に基づくクールダウン計算
        // 高負荷時ほど長いクールダウン
        var cpuFactor = Math.Max(0, (status.CpuUsage - 50) / 30.0);      // 50-80% → 0-1
        var memoryFactor = Math.Max(0, (status.MemoryUsage - 60) / 25.0); // 60-85% → 0-1
        var gpuFactor = Math.Max(0, (status.GpuUtilization - 40) / 35.0); // 40-75% → 0-1
        var vramFactor = Math.Max(0, (status.VramUsage - 50) / 30.0);     // 50-80% → 0-1

        var maxFactor = Math.Max(Math.Max(cpuFactor, memoryFactor), Math.Max(gpuFactor, vramFactor));

        // 0-500ms の範囲でクールダウン
        return (int)(maxFactor * _settings.MaxCooldownMs);
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
    /// 処理に最適なリソース状況かどうか判定
    /// </summary>
    private bool IsOptimalForProcessing(ResourceStatus status, bool isOcrOperation)
    {
        // OCRの場合はより厳しい基準、翻訳はより緩い基準
        var cpuThreshold = isOcrOperation ? _thresholds.CpuHighThreshold - 10 : _thresholds.CpuHighThreshold;
        var memoryThreshold = isOcrOperation ? _thresholds.MemoryHighThreshold - 5 : _thresholds.MemoryHighThreshold;

        return status.CpuUsage < cpuThreshold &&
               status.MemoryUsage < memoryThreshold &&
               status.GpuUtilization < _thresholds.GpuHighThreshold &&
               status.VramUsage < _thresholds.VramHighThreshold;
    }


    /// <summary>
    /// 並列度減少（SemaphoreSlim再作成方式）
    /// </summary>
    private async Task DecreaseParallelismAsync()
    {
        lock (_semaphoreLock)
        {
            // 翻訳の並列度を優先的に削減
            var currentTranslation = _translationSemaphore.CurrentCount;
            if (currentTranslation > 1)
            {
                var newCount = Math.Max(1, currentTranslation - 1);
                RecreateSemaphore(ref _translationSemaphore, newCount, _settings.MaxTranslationParallelism);
                _logger.LogInformation("翻訳並列度減少: {Old} → {New}", currentTranslation, newCount);
                return;
            }

            // それでも不足ならOCRも削減
            var currentOcr = _ocrSemaphore.CurrentCount;
            if (currentOcr > 1 && _translationSemaphore.CurrentCount == 1)
            {
                var newCount = Math.Max(1, currentOcr - 1);
                RecreateSemaphore(ref _ocrSemaphore, newCount, _settings.MaxOcrParallelism);
                _logger.LogInformation("OCR並列度減少: {Old} → {New}", currentOcr, newCount);
            }
        }

        // 少し待機してセマフォの状態を安定させる
        await Task.Delay(100, _disposalCts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// 並列度増加（段階的）
    /// </summary>
    private async Task IncreaseParallelismAsync()
    {
        lock (_semaphoreLock)
        {
            // OCRの並列度を優先的に回復
            var currentOcr = _ocrSemaphore.CurrentCount;
            if (currentOcr < _settings.MaxOcrParallelism)
            {
                var newCount = Math.Min(_settings.MaxOcrParallelism, currentOcr + 1);
                RecreateSemaphore(ref _ocrSemaphore, newCount, _settings.MaxOcrParallelism);
                _logger.LogInformation("OCR並列度増加: {Old} → {New}", currentOcr, newCount);
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

    public void Dispose()
    {
        if (_disposalCts.IsCancellationRequested)
            return;

        _disposalCts.Cancel();

        try
        {
            _ocrSemaphore?.Dispose();
            _translationSemaphore?.Dispose();
            _ocrChannel?.Writer.TryComplete();
            _translationChannel?.Writer.TryComplete();
            _resourceMonitor?.Dispose();

            _logger.LogInformation("HybridResourceManager正常終了");
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