using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Hashing;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Roi;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Models.ImageProcessing;
using Baketa.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Imaging.ChangeDetection;

/// <summary>
/// 拡張画像変化検知サービス
/// P0: 3段階フィルタリング対応（Stage 1: 90% → Stage 2: 8% → Stage 3: 2%）
/// OpenCV SIMD最適化による高速処理実装
/// Geminiフィードバック反映: Thread-safe, ゲーム特化最適化
/// </summary>
public sealed class EnhancedImageChangeDetectionService : IImageChangeDetectionService
{
    private readonly ILogger<EnhancedImageChangeDetectionService> _logger;
    private readonly IPerceptualHashService _perceptualHashService;
    private readonly IImageChangeMetricsService _metricsService;
    private readonly ImageChangeDetectionSettings _settings;
    private readonly LoggingSettings _loggingSettings;
    private readonly IRoiThresholdProvider _roiThresholdProvider; // [Issue #293] ROI動的閾値

    // スレッドセーフキャッシュ（コンテキスト別）
    private readonly ConcurrentDictionary<string, QuickHashCache> _quickHashCache = new();
    private readonly ConcurrentDictionary<string, CachedImageHash> _imageHashCache = new();

    // [Issue #229] グリッド分割ハッシュキャッシュ
    private readonly ConcurrentDictionary<string, GridHashCache> _gridHashCache = new();

    // [Issue #229] テキスト安定化待機状態
    private readonly ConcurrentDictionary<string, StabilizationState> _stabilizationStates = new();

    // 連続SUPPRESS検知カウンタ（1フレーム検出窓問題対策）
    private readonly ConcurrentDictionary<string, int> _consecutiveSuppressCount = new();
    private const int MaxConsecutiveSuppressBeforeForceDetect = 3;

    // パフォーマンス統計
    private readonly ConcurrentDictionary<int, List<TimeSpan>> _stageTimings = new()
    {
        [1] = [],
        [2] = [],
        [3] = []
    };

    private long _totalProcessed = 0;
    private long _stage1Filtered = 0;
    private long _stage2Filtered = 0;
    private long _stage3Processed = 0;

    // [Issue #229] テレメトリログ
    private readonly object _telemetryLock = new();
    private bool _telemetryInitialized = false;

    public EnhancedImageChangeDetectionService(
        ILogger<EnhancedImageChangeDetectionService> logger,
        IPerceptualHashService perceptualHashService,
        IImageChangeMetricsService metricsService,
        IConfiguration configuration,
        IRoiThresholdProvider roiThresholdProvider) // [Issue #293] ROI動的閾値（必須に変更）
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _perceptualHashService = perceptualHashService ?? throw new ArgumentNullException(nameof(perceptualHashService));
        _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
        _roiThresholdProvider = roiThresholdProvider ?? throw new ArgumentNullException(nameof(roiThresholdProvider)); // [Issue #293]

        // 設定外部化対応: ImageChangeDetection設定セクションから読み込み
        _settings = InitializeImageChangeDetectionSettings(configuration);
        _loggingSettings = InitializeLoggingSettings(configuration);

        // 🔍 [DIAGNOSTIC] 閾値設定確認ログ
        _logger.LogInformation("🔧 [CONFIG_DEBUG] Stage1SimilarityThreshold={Threshold:F4} (変化検知: similarity < {Threshold:F4})",
            _settings.Stage1SimilarityThreshold, _settings.Stage1SimilarityThreshold);

        // [Issue #229] グリッド分割設定ログ
        if (_settings.EnableGridPartitioning)
        {
            _logger.LogInformation("🔧 [Issue #229] グリッド分割ハッシュ有効: {Rows}x{Cols}={TotalBlocks}ブロック, 閾値={Threshold:F4}",
                _settings.GridRows, _settings.GridColumns, _settings.GridRows * _settings.GridColumns, _settings.GridBlockSimilarityThreshold);

            // [Issue #302] 下部ゾーン高感度化設定ログ
            if (_settings.EnableLowerZoneHighSensitivity)
            {
                _logger.LogInformation("🔧 [Issue #302] 下部ゾーン高感度化有効: 下部{Ratio:P0}に閾値={Threshold:F4}を適用",
                    _settings.LowerZoneRatio, _settings.LowerZoneSimilarityThreshold);
            }
        }

        // [Issue #229] テキスト安定化設定ログ
        if (_settings.EnableTextStabilization)
        {
            _logger.LogInformation("🔧 [Issue #229] テキスト安定化待機有効: DelayMs={DelayMs}, MaxWaitMs={MaxWaitMs}",
                _settings.TextStabilizationDelayMs, _settings.MaxStabilizationWaitMs);
        }

        // [Issue #293] ROI動的閾値設定ログ
        if (_settings.EnableRoiBasedThreshold)
        {
            _logger.LogInformation("🔧 [Issue #293] ROI動的閾値有効: ProviderEnabled={ProviderEnabled}",
                _roiThresholdProvider.IsEnabled);
        }
    }

    private static ImageChangeDetectionSettings InitializeImageChangeDetectionSettings(IConfiguration configuration)
    {
        try
        {
            if (configuration != null)
            {
                return new ImageChangeDetectionSettings
                {
                    Stage1SimilarityThreshold = configuration.GetValue<float>("ImageChangeDetection:Stage1SimilarityThreshold", 0.92f),
                    Stage2ChangePercentageThreshold = configuration.GetValue<float>("ImageChangeDetection:Stage2ChangePercentageThreshold", 0.05f),
                    Stage3SSIMThreshold = configuration.GetValue<float>("ImageChangeDetection:Stage3SSIMThreshold", 0.92f),
                    RegionSSIMThreshold = configuration.GetValue<float>("ImageChangeDetection:RegionSSIMThreshold", 0.95f),
                    EnableCaching = configuration.GetValue<bool>("ImageChangeDetection:EnableCaching", true),
                    MaxCacheSize = configuration.GetValue<int>("ImageChangeDetection:MaxCacheSize", 1000),
                    CacheExpirationMinutes = configuration.GetValue<int>("ImageChangeDetection:CacheExpirationMinutes", 30),
                    EnablePerformanceLogging = configuration.GetValue<bool>("ImageChangeDetection:EnablePerformanceLogging", true),
                    // [Issue #229] グリッド分割ハッシュ設定
                    EnableGridPartitioning = configuration.GetValue<bool>("ImageChangeDetection:EnableGridPartitioning", true),
                    GridRows = configuration.GetValue<int>("ImageChangeDetection:GridRows", 9),
                    GridColumns = configuration.GetValue<int>("ImageChangeDetection:GridColumns", 16),
                    GridBlockSimilarityThreshold = configuration.GetValue<float>("ImageChangeDetection:GridBlockSimilarityThreshold", 0.98f),
                    // [Issue #302] 下部ゾーン高感度化設定
                    EnableLowerZoneHighSensitivity = configuration.GetValue<bool>("ImageChangeDetection:EnableLowerZoneHighSensitivity", true),
                    LowerZoneSimilarityThreshold = configuration.GetValue<float>("ImageChangeDetection:LowerZoneSimilarityThreshold", 0.995f),
                    LowerZoneRatio = configuration.GetValue<float>("ImageChangeDetection:LowerZoneRatio", 0.25f),
                    // [Issue #229] テキスト安定化待機設定
                    EnableTextStabilization = configuration.GetValue<bool>("ImageChangeDetection:EnableTextStabilization", true),
                    TextStabilizationDelayMs = configuration.GetValue<int>("ImageChangeDetection:TextStabilizationDelayMs", 500),
                    MaxStabilizationWaitMs = configuration.GetValue<int>("ImageChangeDetection:MaxStabilizationWaitMs", 3000),
                    // [Issue #293] ROI動的閾値設定
                    EnableRoiBasedThreshold = configuration.GetValue<bool>("ImageChangeDetection:EnableRoiBasedThreshold", false),
                    RoiHighPriorityThresholdMultiplier = configuration.GetValue<float>("ImageChangeDetection:RoiHighPriorityThresholdMultiplier", 1.02f),
                    RoiLowPriorityThresholdMultiplier = configuration.GetValue<float>("ImageChangeDetection:RoiLowPriorityThresholdMultiplier", 0.98f)
                };
            }
        }
        catch (Exception)
        {
            // 設定取得失敗時はデフォルト値を使用
        }
        return ImageChangeDetectionSettings.CreateDevelopmentSettings();
    }

    private static LoggingSettings InitializeLoggingSettings(IConfiguration configuration)
    {
        try
        {
            if (configuration != null)
            {
                return new LoggingSettings
                {
                    DebugLogPath = configuration.GetValue<string>("Logging:DebugLogPath") ?? "debug_app_logs.txt",
                    EnableDebugFileLogging = configuration.GetValue<bool>("Logging:EnableDebugFileLogging", true),
                    MaxDebugLogFileSizeMB = configuration.GetValue<int>("Logging:MaxDebugLogFileSizeMB", 10),
                    DebugLogRetentionDays = configuration.GetValue<int>("Logging:DebugLogRetentionDays", 7)
                };
            }
        }
        catch (Exception)
        {
            // 設定取得失敗時はデフォルト値を使用
        }
        return LoggingSettings.CreateDevelopmentSettings();
    }

    /// <inheritdoc />
    public async Task<ImageChangeResult> DetectChangeAsync(
        IImage? previousImage,
        IImage currentImage,
        string contextId = "default",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentImage);
        Interlocked.Increment(ref _totalProcessed);

        _logger.LogDebug("🎯 [P0_CHANGE_DETECT] DetectChangeAsync開始 - ContextId: {ContextId}", contextId);

        var overallStopwatch = Stopwatch.StartNew();

        try
        {
            // 初回検知（前回画像なし）
            if (previousImage == null)
            {
                return await CreateFirstTimeResultAsync(currentImage, contextId, cancellationToken);
            }

            // [Issue #229] 新3段階アーキテクチャ（グリッド分割有効時）
            if (_settings.EnableGridPartitioning)
            {
                return await ExecuteNewArchitectureAsync(currentImage, contextId, overallStopwatch);
            }

            // レガシーアーキテクチャ（グリッド分割無効時）
            return await ExecuteLegacyArchitectureAsync(previousImage, currentImage, contextId, overallStopwatch, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 拡張画像変化検知エラー - Context: {ContextId}, 処理時間: {ElapsedMs}ms",
                contextId, overallStopwatch.ElapsedMilliseconds);

            // エラー時は安全側で変化ありとして処理継続
            return ImageChangeResult.CreateChanged("ERROR", "ERROR", 1.0f, HashAlgorithmType.AverageHash, overallStopwatch.Elapsed);
        }
    }

    /// <summary>
    /// [Issue #229] 新3段階アーキテクチャの実行
    /// Stage 1: Grid Quick Filter
    /// Stage 2: Change Validation (ノイズフィルタリング)
    /// Stage 3: Region Analysis
    /// + テキスト安定化待機（タイプライターエフェクト対応）
    /// </summary>
    private async Task<ImageChangeResult> ExecuteNewArchitectureAsync(
        IImage currentImage,
        string contextId,
        Stopwatch overallStopwatch)
    {
        // === Stage 1: Grid Quick Filter ===
        var stage1Result = await ExecuteNewStage1_GridQuickFilterAsync(currentImage, contextId);
        RecordStageTime(1, stage1Result.ProcessingTime);

        if (!stage1Result.HasPotentialChange)
        {
            Interlocked.Increment(ref _stage1Filtered);
            _logger.LogDebug("📊 [NewArch] Stage 1で除外 - Context: {ContextId}, MinSimilarity: {MinSim:F4}",
                contextId, stage1Result.MinSimilarity);

            // [Issue #229] テキスト安定化: 変化なし検出時の処理
            if (_settings.EnableTextStabilization)
            {
                var stabilizationResult = HandleStabilizationOnNoChange(contextId, overallStopwatch.Elapsed);
                if (stabilizationResult != null)
                {
                    return stabilizationResult;
                }
            }

            return ImageChangeResult.CreateNoChange(stage1Result.ProcessingTime, detectionStage: 1);
        }

        _logger.LogDebug("✅ [NewArch] Stage 1通過 - ChangedBlocks: {Count}, MinSimilarity: {MinSim:F4}",
            stage1Result.ChangedBlocks.Count, stage1Result.MinSimilarity);

        // === Stage 2: Change Validation ===
        var stage2Result = ExecuteNewStage2_ChangeValidation(stage1Result);
        RecordStageTime(2, stage2Result.ProcessingTime);

        if (!stage2Result.IsSignificantChange)
        {
            Interlocked.Increment(ref _stage2Filtered);
            _logger.LogDebug("📊 [NewArch] Stage 2で除外（ノイズ）- Context: {ContextId}, Reason: {Reason}",
                contextId, stage2Result.FilterReason ?? "Not significant");

            // [Issue #229] テキスト安定化: 変化なし検出時の処理
            if (_settings.EnableTextStabilization)
            {
                var stabilizationResult = HandleStabilizationOnNoChange(contextId, overallStopwatch.Elapsed);
                if (stabilizationResult != null)
                {
                    return stabilizationResult;
                }
            }

            return ImageChangeResult.CreateNoChange(overallStopwatch.Elapsed, detectionStage: 2);
        }

        _logger.LogDebug("✅ [NewArch] Stage 2通過 - Adjacent: {Adjacent}, EdgeOnly: {EdgeOnly}",
            stage2Result.HasAdjacentBlocks, stage2Result.IsEdgeOnlyChange);

        // === Stage 3: Region Analysis ===
        var stage3Result = ExecuteNewStage3_RegionAnalysis(stage2Result);
        RecordStageTime(3, stage3Result.ProcessingTime);
        Interlocked.Increment(ref _stage3Processed);

        _logger.LogDebug("✅ [NewArch] Stage 3完了 - Regions: {Count}, ChangePercentage: {Pct:F4}",
            stage3Result.ChangedRegions.Length, stage3Result.ChangePercentage);

        // [Issue #229] テキスト安定化: 変化検出時の処理
        if (_settings.EnableTextStabilization)
        {
            var suppressResult = HandleStabilizationOnChange(contextId, overallStopwatch.Elapsed);
            if (suppressResult != null)
            {
                return suppressResult; // OCR抑制（安定化待機中）
            }
        }

        // 最終結果を生成
        return ImageChangeResult.CreateChanged(
            "GRID",
            "GRID",
            stage3Result.ChangePercentage,
            HashAlgorithmType.DifferenceHash,
            overallStopwatch.Elapsed,
            detectionStage: 3,
            regions: stage3Result.ChangedRegions);
    }

    /// <summary>
    /// レガシーアーキテクチャの実行（グリッド分割無効時）
    /// </summary>
    private async Task<ImageChangeResult> ExecuteLegacyArchitectureAsync(
        IImage previousImage,
        IImage currentImage,
        string contextId,
        Stopwatch overallStopwatch,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("⚠️ [Legacy] グリッド分割無効 - レガシーアーキテクチャを使用");

        // Stage 1: 高速フィルタリング
        var quickResult = await ExecuteStage1QuickFilterAsync(previousImage, currentImage, contextId);
        RecordStageTime(1, quickResult.ProcessingTime);

        if (!quickResult.HasPotentialChange)
        {
            Interlocked.Increment(ref _stage1Filtered);
            return ImageChangeResult.CreateNoChange(quickResult.ProcessingTime, detectionStage: 1);
        }

        // Stage 1が初回検知の場合、Stage 2キャッシュクリア
        // 浮動小数点の等値比較を避けるため、小さな閾値で比較
        if (quickResult.MaxSimilarity <= float.Epsilon)
        {
            _imageHashCache.TryRemove(contextId, out _);
        }

        // Stage 2: 中精度検証
        var stage2Result = await ExecuteStage2MediumPrecisionAsync(previousImage, currentImage, contextId, cancellationToken);
        RecordStageTime(2, stage2Result.ProcessingTime);

        if (stage2Result.HasChanged)
        {
            return stage2Result;
        }

        Interlocked.Increment(ref _stage2Filtered);

        // Stage 3: 高精度解析
        var finalResult = await ExecuteStage3HighPrecisionAsync(previousImage, currentImage, contextId, stage2Result, cancellationToken);
        RecordStageTime(3, finalResult.ProcessingTime);
        Interlocked.Increment(ref _stage3Processed);

        return finalResult;
    }

    /// <inheritdoc />
    public async Task<QuickFilterResult> QuickFilterAsync(
        IImage? previousImage,
        IImage currentImage,
        string contextId = "default")
    {
        if (previousImage == null)
        {
            return new QuickFilterResult { HasPotentialChange = true, ProcessingTime = TimeSpan.Zero };
        }

        // [Issue #229] グリッド分割が有効な場合は局所変化検知を使用
        return _settings.EnableGridPartitioning
            ? await ExecuteStage1GridPartitioningAsync(previousImage, currentImage, contextId)
            : await ExecuteStage1QuickFilterAsync(previousImage, currentImage, contextId);
    }

    /// <inheritdoc />
    public async Task<ImageType> DetectImageTypeAsync(IImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        return await Task.Run(() =>
        {
            // 簡易画像タイプ判定（将来的にはMLベース判定に拡張）
            try
            {
                // 解像度ベース判定
                if (image.Width >= 1920 && image.Height >= 1080)
                {
                    return ImageType.GameScene; // フルスクリーンゲーム
                }

                if (image.Width < 800 || image.Height < 600)
                {
                    return ImageType.UIElement; // 小さいUI要素
                }

                return ImageType.GameUI; // 一般的なゲームUI
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "画像タイプ判定エラー - デフォルト値を返却");
                return ImageType.Unknown;
            }
        });
    }

    /// <inheritdoc />
    public async Task<RegionChangeResult[]> DetectRegionChangesAsync(
        IImage? previousImage,
        IImage currentImage,
        Rectangle[] regions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentImage);
        ArgumentNullException.ThrowIfNull(regions);

        if (previousImage == null || regions.Length == 0)
        {
            return [.. regions.Select(r => new RegionChangeResult(r, true, 0.0f))];
        }

        var results = new List<RegionChangeResult>();

        foreach (var region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // 領域別SSIM計算（簡易実装）
                var ssimScore = await _perceptualHashService.CalculateSSIMAsync(previousImage, currentImage);
                var hasChanged = ssimScore < _settings.RegionSSIMThreshold; // SSIM閾値（設定外部化）

                results.Add(new RegionChangeResult(region, hasChanged, ssimScore));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ROI変化検知エラー - Region: {Region}", region);
                results.Add(new RegionChangeResult(region, true, 0.0f)); // エラー時は変化ありとする
            }
        }

        return [.. results];
    }

    /// <inheritdoc />
    public void ClearCache(string? contextId = null)
    {
        if (contextId != null)
        {
            _quickHashCache.TryRemove(contextId, out _);
            _imageHashCache.TryRemove(contextId, out _);
            _gridHashCache.TryRemove(contextId, out _);
            _stabilizationStates.TryRemove(contextId, out _); // [Issue #229] 安定化状態もクリア
            _consecutiveSuppressCount.TryRemove(contextId, out _);
            _logger.LogDebug("🗑️ キャッシュクリア - Context: {ContextId}", contextId);
        }
        else
        {
            var quickCount = _quickHashCache.Count;
            var imageCount = _imageHashCache.Count;
            var gridCount = _gridHashCache.Count;
            var stabilizationCount = _stabilizationStates.Count; // [Issue #229]

            _quickHashCache.Clear();
            _imageHashCache.Clear();
            _gridHashCache.Clear();
            _stabilizationStates.Clear(); // [Issue #229] 安定化状態もクリア
            _consecutiveSuppressCount.Clear();

            _logger.LogInformation("🗑️ 全キャッシュクリア - Quick: {QuickCount}, Image: {ImageCount}, Grid: {GridCount}, Stabilization: {StabilizationCount}",
                quickCount, imageCount, gridCount, stabilizationCount);
        }
    }

    /// <inheritdoc />
    public ImageChangeDetectionStatistics GetStatistics()
    {
        var totalProcessed = Interlocked.Read(ref _totalProcessed);
        var stage1Filtered = Interlocked.Read(ref _stage1Filtered);
        var stage2Filtered = Interlocked.Read(ref _stage2Filtered);
        var stage3Processed = Interlocked.Read(ref _stage3Processed);

        return new ImageChangeDetectionStatistics
        {
            TotalProcessed = totalProcessed,
            Stage1Filtered = stage1Filtered,
            Stage2Filtered = stage2Filtered,
            Stage3Processed = stage3Processed,
            AverageStage1Time = CalculateAverageTime(1),
            AverageStage2Time = CalculateAverageTime(2),
            AverageStage3Time = CalculateAverageTime(3),
            CacheHitRate = CalculateCacheHitRate(),
            CurrentCacheSize = _quickHashCache.Count + _imageHashCache.Count,
            FilteringEfficiency = totalProcessed > 0 ? (float)stage1Filtered / totalProcessed : 0f
        };
    }

    /// <inheritdoc />
    [Obsolete("Use DetectChangeAsync(IImage, IImage, string, CancellationToken) instead")]
    public async Task<ImageChangeResult> DetectChangeAsync(
        byte[] previousImage,
        byte[] currentImage,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("⚠️ 廃止予定メソッド使用 - DetectChangeAsync(byte[], byte[])");

        // 既存互換性のため基本実装で処理（ILoggerの型変換）
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => { });
        var basicLogger = loggerFactory.CreateLogger<ImageChangeDetectionService>();
        var basicService = new ImageChangeDetectionService(basicLogger, _metricsService);
        return await basicService.DetectChangeAsync(previousImage, currentImage, cancellationToken);
    }

    #region Private Methods

    /// <summary>
    /// Stage 1: 高速フィルタリング実行
    /// 目標: 90%のフレームを<1msで除外
    /// </summary>
    private async Task<QuickFilterResult> ExecuteStage1QuickFilterAsync(IImage previousImage, IImage currentImage, string contextId)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var imageType = await DetectImageTypeAsync(currentImage);
            var optimalAlgorithm = _perceptualHashService.GetOptimalAlgorithm(imageType);

            // 高速Hashアルゴリズム選択（Stage 1専用）
            // 🔧 [Issue #230] DifferenceHashをデフォルトに変更
            // AverageHashは全体平均輝度を見るため、小さなテキスト変更に鈍感
            // DifferenceHashはエッジ変化（テキスト変更）に敏感
            var quickAlgorithm = optimalAlgorithm == HashAlgorithmType.AverageHash
                ? HashAlgorithmType.AverageHash
                : HashAlgorithmType.DifferenceHash;

            var currentHash = _perceptualHashService.ComputeHash(currentImage, quickAlgorithm);

            // キャッシュから前回Hash取得
            if (!_quickHashCache.TryGetValue(contextId, out var cachedHashes))
            {
                // 初回は潜在的変化ありとして次段階へ
                var newCache = new QuickHashCache(
                    quickAlgorithm == HashAlgorithmType.AverageHash ? currentHash : "",
                    quickAlgorithm == HashAlgorithmType.DifferenceHash ? currentHash : "",
                    DateTime.UtcNow);

                _quickHashCache.AddOrUpdate(contextId, newCache, (_, _) => newCache);

                return new QuickFilterResult
                {
                    HasPotentialChange = true,
                    AverageHash = newCache.AverageHash,
                    DifferenceHash = newCache.DifferenceHash,
                    ProcessingTime = stopwatch.Elapsed,
                    MaxSimilarity = 0.0f
                };
            }

            // ハッシュ比較
            var previousHash = quickAlgorithm == HashAlgorithmType.AverageHash
                ? cachedHashes.AverageHash
                : cachedHashes.DifferenceHash;

            var similarity = _perceptualHashService.CompareHashes(previousHash, currentHash, quickAlgorithm);

            // [Issue #230] 32x32ハッシュ対応 - 閾値ベースの変化検知
            var hasPotentialChange = similarity < _settings.Stage1SimilarityThreshold;

            // 🔍 P0システム動作確認用 - ハッシュ値デバッグログ
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var prevHashShort = string.IsNullOrEmpty(previousHash) ? "NULL" : string.Concat(previousHash.AsSpan(0, Math.Min(8, previousHash.Length)), "...");
                var currHashShort = string.IsNullOrEmpty(currentHash) ? "NULL" : string.Concat(currentHash.AsSpan(0, Math.Min(8, currentHash.Length)), "...");

                _logger.LogDebug("🔍 [P0_HASH_DEBUG] Algorithm: {Algorithm}, PrevHash: {PrevHash}, CurrHash: {CurrHash}, Similarity: {Similarity:F4}, HasChange: {HasChange}, ContextId: {ContextId}",
                    quickAlgorithm, prevHashShort, currHashShort, similarity, hasPotentialChange, contextId);
            }

            // キャッシュ更新
            var updatedCache = quickAlgorithm == HashAlgorithmType.AverageHash
                ? cachedHashes with { AverageHash = currentHash, Timestamp = DateTime.UtcNow }
                : cachedHashes with { DifferenceHash = currentHash, Timestamp = DateTime.UtcNow };

            _quickHashCache.AddOrUpdate(contextId, updatedCache, (_, _) => updatedCache);

            return new QuickFilterResult
            {
                HasPotentialChange = hasPotentialChange,
                AverageHash = updatedCache.AverageHash,
                DifferenceHash = updatedCache.DifferenceHash,
                ProcessingTime = stopwatch.Elapsed,
                MaxSimilarity = similarity
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stage 1高速フィルタエラー - Context: {ContextId}", contextId);
            return new QuickFilterResult
            {
                HasPotentialChange = true, // エラー時は次段階へ
                ProcessingTime = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// [Issue #229] Stage 1: グリッド分割ハッシュによる高速フィルタリング
    /// 画面を N×M ブロックに分割し、各ブロックのハッシュを比較
    /// いずれか1ブロックでも閾値を下回れば「変化あり」と判定
    /// </summary>
    private async Task<QuickFilterResult> ExecuteStage1GridPartitioningAsync(
        IImage previousImage,
        IImage currentImage,
        string contextId)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var rows = _settings.GridRows;
            var cols = _settings.GridColumns;
            var totalBlocks = rows * cols;

            // ブロックサイズ計算
            var blockWidth = currentImage.Width / cols;
            var blockHeight = currentImage.Height / rows;

            // 現在フレームの全ブロックハッシュ計算（並列化）
            var algorithm = HashAlgorithmType.DifferenceHash; // エッジ検出に有効

            // [Gemini Review] Task.WhenAllによる並列ハッシュ計算でパフォーマンス向上
            var hashTasks = Enumerable.Range(0, totalBlocks).Select(i => Task.Run(() =>
            {
                var row = i / cols;
                var col = i % cols;
                var region = new Rectangle(
                    col * blockWidth,
                    row * blockHeight,
                    blockWidth,
                    blockHeight);
                return _perceptualHashService.ComputeHashForRegion(currentImage, region, algorithm);
            }));
            var currentBlockHashes = await Task.WhenAll(hashTasks);

            // キャッシュから前回ハッシュ取得
            if (!_gridHashCache.TryGetValue(contextId, out var cachedGrid))
            {
                // 初回は潜在的変化ありとして次段階へ
                var newCache = new GridHashCache(currentBlockHashes, rows, cols, DateTime.UtcNow);
                _gridHashCache.AddOrUpdate(contextId, newCache, (_, _) => newCache);

                _logger.LogDebug("🔲 [Issue #229] グリッドハッシュ初回キャッシュ - Context: {ContextId}, Blocks: {Blocks}",
                    contextId, totalBlocks);

                return new QuickFilterResult
                {
                    HasPotentialChange = true,
                    ProcessingTime = stopwatch.Elapsed,
                    MaxSimilarity = 0.0f
                };
            }

            // グリッドサイズ変更チェック
            if (cachedGrid.Rows != rows || cachedGrid.Columns != cols)
            {
                var newCache = new GridHashCache(currentBlockHashes, rows, cols, DateTime.UtcNow);
                _gridHashCache.AddOrUpdate(contextId, newCache, (_, _) => newCache);

                _logger.LogDebug("🔲 [Issue #229] グリッドサイズ変更 - Context: {ContextId}, Old: {OldRows}x{OldCols}, New: {NewRows}x{NewCols}",
                    contextId, cachedGrid.Rows, cachedGrid.Columns, rows, cols);

                return new QuickFilterResult
                {
                    HasPotentialChange = true,
                    ProcessingTime = stopwatch.Elapsed,
                    MaxSimilarity = 0.0f
                };
            }

            // 全ブロック比較 - いずれか1ブロックでも閾値未満なら変化あり
            var minSimilarity = 1.0f;
            var changedBlockIndex = -1;
            var hasPotentialChange = false;

            // [Issue #302] ゾーン別類似度追跡
            var lowerZoneStartRow = (int)(rows * (1.0f - _settings.LowerZoneRatio));
            var upperZoneMin = 1.0f;
            var upperZoneMax = 0.0f;
            var lowerZoneMin = 1.0f;
            var lowerZoneMax = 0.0f;
            var detectedRow = -1;
            var detectedCol = -1;
            var detectedSimilarity = 0.0f;
            var detectedThreshold = 0.0f;

            for (int i = 0; i < totalBlocks; i++)
            {
                var row = i / cols;
                var col = i % cols;
                var similarity = _perceptualHashService.CompareHashes(
                    cachedGrid.BlockHashes[i],
                    currentBlockHashes[i],
                    algorithm);

                if (similarity < minSimilarity)
                {
                    minSimilarity = similarity;
                    changedBlockIndex = i;
                }

                // [Issue #302] ゾーン別min/max更新
                var isLowerZone = row >= lowerZoneStartRow;
                if (isLowerZone)
                {
                    if (similarity < lowerZoneMin) lowerZoneMin = similarity;
                    if (similarity > lowerZoneMax) lowerZoneMax = similarity;
                }
                else
                {
                    if (similarity < upperZoneMin) upperZoneMin = similarity;
                    if (similarity > upperZoneMax) upperZoneMax = similarity;
                }

                // [Issue #293] ROI動的閾値を適用（下部ゾーン高感度化 + ROI学習）
                var threshold = GetDynamicThreshold(row, col, rows, cols);

                // 早期終了: 閾値を下回ったブロックを発見
                if (similarity < threshold)
                {
                    detectedRow = row;
                    detectedCol = col;
                    detectedSimilarity = similarity;
                    detectedThreshold = threshold;
                    hasPotentialChange = true;
                    break;
                }
            }

            // [Issue #302] 早期終了しなかった場合、全ブロックの最小類似度と対応する閾値で判定
            if (!hasPotentialChange && changedBlockIndex >= 0)
            {
                var changedRow = changedBlockIndex / cols;
                var changedCol = changedBlockIndex % cols; // [Issue #293] 列情報も必要
                var threshold = GetDynamicThreshold(changedRow, changedCol, rows, cols); // [Issue #293] ROI動的閾値
                hasPotentialChange = minSimilarity < threshold;
            }

            // [Issue #302] ゾーン別類似度サマリーログ（Information レベル）
            // 初回比較時（max=0）はスキップ
            if (upperZoneMax > 0 || lowerZoneMax > 0)
            {
                _logger.LogInformation(
                    "📊 [Issue #302] グリッド類似度: 上部(行0-{UpperEnd})=[{UpperMin:F4}~{UpperMax:F4}]/閾値{UpperThreshold:F4}, 下部(行{LowerStart}-{LowerEnd})=[{LowerMin:F4}~{LowerMax:F4}]/閾値{LowerThreshold:F4} → {Result}",
                    lowerZoneStartRow - 1,
                    upperZoneMin,
                    upperZoneMax,
                    _settings.GridBlockSimilarityThreshold,
                    lowerZoneStartRow,
                    rows - 1,
                    lowerZoneMin,
                    lowerZoneMax,
                    _settings.LowerZoneSimilarityThreshold,
                    hasPotentialChange ? $"変化検出 Block[{detectedRow},{detectedCol}] {detectedSimilarity:F4}<{detectedThreshold:F4}" : "変化なし");
            }

            // キャッシュ更新
            var updatedCache = new GridHashCache(currentBlockHashes, rows, cols, DateTime.UtcNow);
            _gridHashCache.AddOrUpdate(contextId, updatedCache, (_, _) => updatedCache);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("🔲 [Issue #229] グリッド分割結果 - Context: {ContextId}, MinSimilarity: {MinSimilarity:F4}, HasChange: {HasChange}, ChangedBlock: [{Row},{Col}]",
                    contextId, minSimilarity, hasPotentialChange,
                    changedBlockIndex >= 0 ? changedBlockIndex / cols : -1,
                    changedBlockIndex >= 0 ? changedBlockIndex % cols : -1);
            }

            return new QuickFilterResult
            {
                HasPotentialChange = hasPotentialChange,
                ProcessingTime = stopwatch.Elapsed,
                MaxSimilarity = minSimilarity
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔲 [Issue #229] グリッド分割エラー - Context: {ContextId}", contextId);
            return new QuickFilterResult
            {
                HasPotentialChange = true, // エラー時は次段階へ
                ProcessingTime = stopwatch.Elapsed
            };
        }
    }

    #region [Issue #229] 新3段階アーキテクチャ

    #region [Issue #229] テキスト安定化待機ロジック

    /// <summary>
    /// [Issue #229] 変化検出時の安定化処理
    /// 変化を検出したが、テキストアニメーション中の可能性があるためOCRを抑制
    /// </summary>
    /// <param name="contextId">コンテキストID</param>
    /// <param name="elapsed">処理時間</param>
    /// <returns>OCR抑制する場合は"NoChange"結果、そうでなければnull（OCR実行許可）</returns>
    /// <remarks>
    /// [Gemini Review] スレッドセーフティのため、stateオブジェクトをロックして
    /// 読み取り→更新をアトミックに実行。
    /// </remarks>
    private ImageChangeResult? HandleStabilizationOnChange(string contextId, TimeSpan elapsed)
    {
        // GetOrAddで一度だけインスタンスを生成（スレッドセーフ）
        var state = _stabilizationStates.GetOrAdd(contextId, _ => StabilizationState.CreateIdle());
        var now = DateTime.UtcNow; // 判定基準時刻を統一

        lock (state) // stateオブジェクトをロックしてアトミック操作を保証
        {
            if (!state.IsInStabilization)
            {
                // [Issue #469] 最初の変化検出: OCR即許可 + 安定化モード開始
                // - OCRを即許可して初回翻訳を遅延なく実行（20秒遅延問題の回避）
                // - 同時に安定化モードに入り、テキスト表示完了後にOCR再実行を可能にする
                // - 初回OCR中にテキスト表示が進行した場合、安定化完了後に再OCRで完全テキスト取得
                state.EnterStabilization();

                _logger.LogDebug("🕐 [TextStabilization] 変化検出 - Context: {ContextId}（OCR許可、安定化モード開始）",
                    contextId);

                return null; // OCR実行許可
            }

            // [Issue #469] 安定化モード中に変化継続（タイプライターエフェクト等）
            // 初回OCRは既に許可済み → 追加OCRを抑制し、テキスト安定化を待つ
            // HandleStabilizationOnNoChangeでテキスト安定化を検出→再OCRトリガー
            state.UpdateLastChange();
            var sinceFirstChange = (now - state.FirstChangeTime).TotalMilliseconds;

            // ハードタイムアウト: OCR処理時間(~3.5s)を考慮して十分な猶予を設定
            // MaxStabilizationWaitMs(3000ms) * 2 = 6000ms
            // これはフォールバック用。通常はHandleStabilizationOnNoChangeで安定化検出→再OCR
            var hardTimeoutMs = _settings.MaxStabilizationWaitMs * 2;
            if (sinceFirstChange >= hardTimeoutMs)
            {
                state.Reset();

                _logger.LogDebug("⏰ [TextStabilization] ハードタイムアウト - Context: {ContextId}, 総経過: {TotalMs:F0}ms - OCR強制許可",
                    contextId, sinceFirstChange);

                return null; // フォールバック: OCR実行許可
            }

            _logger.LogDebug("🔄 [TextStabilization] 変化継続（OCR抑制）- Context: {ContextId}, 総経過: {Ms:F0}ms",
                contextId, sinceFirstChange);

            return ImageChangeResult.CreateNoChange(elapsed, detectionStage: 1);
        }
    }

    /// <summary>
    /// [Issue #229] 変化なし検出時の安定化処理
    /// 安定化モード中に変化なしを検出した場合、安定化完了を判定
    /// </summary>
    /// <param name="contextId">コンテキストID</param>
    /// <param name="elapsed">処理時間</param>
    /// <returns>安定化完了時は"Changed"結果（OCR実行トリガー）、そうでなければnull</returns>
    /// <remarks>
    /// [Gemini Review] スレッドセーフティのため、stateオブジェクトをロックして
    /// 読み取り→更新をアトミックに実行。
    /// </remarks>
    private ImageChangeResult? HandleStabilizationOnNoChange(string contextId, TimeSpan elapsed)
    {
        if (!_stabilizationStates.TryGetValue(contextId, out var state))
        {
            // 状態がない場合は通常処理
            return null;
        }

        var now = DateTime.UtcNow; // 判定基準時刻を統一

        lock (state) // stateオブジェクトをロックしてアトミック操作を保証
        {
            if (!state.IsInStabilization)
            {
                // 安定化モードでない場合は通常処理
                return null;
            }

            // [Issue #469] 安定化モード中に変化なしを検出
            // テキスト表示が完了した可能性 → 安定化判定
            var sinceLastChange = (now - state.LastChangeTime).TotalMilliseconds;
            var sinceFirstChange = (now - state.FirstChangeTime).TotalMilliseconds;

            if (state.HasStabilized(now, _settings.TextStabilizationDelayMs) || state.HasTimedOut(now, _settings.MaxStabilizationWaitMs))
            {
                // 安定化完了またはタイムアウト：再OCR実行トリガー
                state.Reset();

                _logger.LogInformation("✅ [TextStabilization] 安定化完了（再OCRトリガー）- Context: {ContextId}, " +
                    "総経過: {TotalMs:F0}ms, 最終変化から: {SinceLastMs:F0}ms",
                    contextId, sinceFirstChange, sinceLastChange);

                // 「変化あり」として返すことでOCRをトリガー
                return ImageChangeResult.CreateChanged(
                    "STABILIZED",
                    "STABILIZED",
                    0.01f, // 軽微な変化として報告
                    HashAlgorithmType.DifferenceHash,
                    elapsed,
                    detectionStage: 3);
            }

            // まだ安定化待機時間が経過していない
            _logger.LogDebug("⏳ [TextStabilization] 安定化待機中（変化なし）- Context: {ContextId}, " +
                "最終変化から: {SinceLastMs:F0}ms, 総経過: {TotalMs:F0}ms",
                contextId, sinceLastChange, sinceFirstChange);

            return null; // 通常の「変化なし」処理を続行
        }
    }

    #endregion

    /// <summary>
    /// [Issue #229] 新 Stage 1: Grid Quick Filter
    /// グリッド分割による高速フィルタリング（詳細結果を返す）
    /// [Gemini Review] チェックサムフォールバック追加 - ハッシュ衝突時の検出漏れを防止
    /// </summary>
    private async Task<GridChangeDetectionResult> ExecuteNewStage1_GridQuickFilterAsync(
        IImage currentImage,
        string contextId)
    {
        var stopwatch = Stopwatch.StartNew();
        var rows = _settings.GridRows;
        var cols = _settings.GridColumns;
        var totalBlocks = rows * cols;

        try
        {
            var blockWidth = currentImage.Width / cols;
            var blockHeight = currentImage.Height / rows;
            var algorithm = HashAlgorithmType.DifferenceHash;

            // [Issue #229] 画像チェックサム計算（フォールバック用）
            var currentChecksum = CalculateImageChecksum(currentImage);
            // [Issue #436] ロバストチェックサム計算（GPUノイズ耐性フォールバック用）
            var currentRobustChecksum = CalculateRobustImageChecksum(currentImage);

            // 並列ハッシュ計算（ブロック情報も保持）
            var hashTasks = Enumerable.Range(0, totalBlocks).Select(i => Task.Run(() =>
            {
                var row = i / cols;
                var col = i % cols;
                var region = new Rectangle(col * blockWidth, row * blockHeight, blockWidth, blockHeight);
                var hash = _perceptualHashService.ComputeHashForRegion(currentImage, region, algorithm);
                return (Index: i, Row: row, Col: col, Hash: hash, Region: region);
            }));
            var blockResults = await Task.WhenAll(hashTasks);

            // キャッシュ確認
            if (!_gridHashCache.TryGetValue(contextId, out var cachedGrid) ||
                cachedGrid.Rows != rows || cachedGrid.Columns != cols)
            {
                // 初回またはサイズ変更
                var newCache = new GridHashCache(
                    blockResults.OrderBy(b => b.Index).Select(b => b.Hash).ToArray(),
                    rows, cols, DateTime.UtcNow, currentChecksum, currentRobustChecksum);
                _gridHashCache.AddOrUpdate(contextId, newCache, (_, _) => newCache);

                _logger.LogDebug("🔲 [NewStage1] 初回キャッシュ作成 - Context: {ContextId}, Blocks: {Blocks}, Checksum: {Checksum}", contextId, totalBlocks, currentChecksum);

                // 初回は全ブロック変化として扱う
                return new GridChangeDetectionResult
                {
                    ProcessingTime = stopwatch.Elapsed,
                    ChangedBlocks = blockResults.Select(b => new BlockChangeInfo(b.Index, b.Row, b.Col, 0f, b.Region)).ToList(),
                    TotalBlocks = totalBlocks,
                    GridRows = rows,
                    GridColumns = cols,
                    MinSimilarity = 0f,
                    MostChangedBlockIndex = 0
                };
            }

            // 全ブロック比較（早期終了なし、全て収集）
            var changedBlocks = new List<BlockChangeInfo>();
            var minSimilarity = 1.0f;
            var mostChangedIndex = -1;

            // [Issue #302] ゾーン別類似度追跡
            var lowerZoneStartRow = (int)(rows * (1.0f - _settings.LowerZoneRatio));
            var upperZoneMin = 1.0f;
            var upperZoneMax = 0.0f;
            var lowerZoneMin = 1.0f;
            var lowerZoneMax = 0.0f;

            foreach (var block in blockResults)
            {
                var similarity = _perceptualHashService.CompareHashes(
                    cachedGrid.BlockHashes[block.Index],
                    block.Hash,
                    algorithm);

                if (similarity < minSimilarity)
                {
                    minSimilarity = similarity;
                    mostChangedIndex = block.Index;
                }

                // [Issue #302] ゾーン別min/max更新
                var isLowerZone = block.Row >= lowerZoneStartRow;
                if (isLowerZone)
                {
                    if (similarity < lowerZoneMin) lowerZoneMin = similarity;
                    if (similarity > lowerZoneMax) lowerZoneMax = similarity;
                }
                else
                {
                    if (similarity < upperZoneMin) upperZoneMin = similarity;
                    if (similarity > upperZoneMax) upperZoneMax = similarity;
                }

                // [Issue #293] ROI動的閾値を適用（下部ゾーン高感度化 + ROI学習）
                var threshold = GetDynamicThreshold(block.Row, block.Col, rows, cols);
                if (similarity < threshold)
                {
                    changedBlocks.Add(new BlockChangeInfo(block.Index, block.Row, block.Col, similarity, block.Region));
                }
            }

            // [Issue #302] ゾーン別類似度サマリーログ
            // 初回比較時（max=0）はスキップ
            if (upperZoneMax > 0 || lowerZoneMax > 0)
            {
                var firstChanged = changedBlocks.FirstOrDefault();
                _logger.LogInformation(
                    "📊 [Issue #302] グリッド類似度: 上部(行0-{UpperEnd})=[{UpperMin:F4}~{UpperMax:F4}]/閾値{UpperThreshold:F4}, 下部(行{LowerStart}-{LowerEnd})=[{LowerMin:F4}~{LowerMax:F4}]/閾値{LowerThreshold:F4} → {Result}",
                    lowerZoneStartRow - 1,
                    upperZoneMin,
                    upperZoneMax,
                    _settings.GridBlockSimilarityThreshold,
                    lowerZoneStartRow,
                    rows - 1,
                    lowerZoneMin,
                    lowerZoneMax,
                    _settings.LowerZoneSimilarityThreshold,
                    changedBlocks.Count > 0 ? $"変化検出 {changedBlocks.Count}ブロック (Block[{firstChanged.Row},{firstChanged.Col}] {firstChanged.Similarity:F4})" : "変化なし");
            }

            // [Issue #229][Gemini Review] チェックサムフォールバック検出
            // ハッシュが同一でもチェックサムが異なれば変化ありと判定
            var checksumChanged = currentChecksum != cachedGrid.ImageChecksum;
            var skipCacheUpdate = false; // SUPPRESS時はキャッシュ更新をスキップ（1フレーム検出窓問題対策）
            if (changedBlocks.Count == 0 && checksumChanged && minSimilarity < 0.999f)
            {
                // 通常フォールバック: ハッシュ類似度が十分低い → テキスト変化の可能性が高い
                _logger.LogInformation("🔄 [NewStage1_FALLBACK] チェックサムフォールバック発動 - ハッシュ同一だが画像変化検出 (Cached: {Cached:X16}, Current: {Current:X16})",
                    cachedGrid.ImageChecksum, currentChecksum);

                // テキスト領域（下部）を優先的に変化ブロックとして追加
                var textRow = rows - 1; // [Issue #397] 最下行（動的: 16x9ならRow=8）
                for (int col = 0; col < cols; col++)
                {
                    var blockIndex = textRow * cols + col;
                    var block = blockResults.First(b => b.Index == blockIndex);
                    changedBlocks.Add(new BlockChangeInfo(block.Index, block.Row, block.Col, FallbackSimilarityThreshold, block.Region));
                }
                minSimilarity = FallbackSimilarityThreshold; // フォールバック検出時の仮の類似度
                mostChangedIndex = textRow * cols;
            }
            else if (changedBlocks.Count == 0 && checksumChanged && minSimilarity >= 0.999f)
            {
                // [Issue #436] ハッシュ完全一致 + チェックサム不一致
                // ロバストチェックサム（量子化SUM）でGPUノイズとテキスト変化を判別
                var robustDiff = Math.Abs(currentRobustChecksum - cachedGrid.RobustImageChecksum);

                if (robustDiff > RobustChecksumDiffThreshold)
                {
                    // テキスト変化を検出 → フォールバック発動
                    _logger.LogInformation(
                        "🔄 [NewStage1_ROBUST_FALLBACK] ロバストチェックサムで変化検出 - Diff: {Diff}, Threshold: {Threshold}, MinSim: {MinSim:F4}",
                        robustDiff, RobustChecksumDiffThreshold, minSimilarity);

                    var textRow = rows - 1;
                    for (int col = 0; col < cols; col++)
                    {
                        var blockIndex = textRow * cols + col;
                        var block = blockResults.First(b => b.Index == blockIndex);
                        changedBlocks.Add(new BlockChangeInfo(block.Index, block.Row, block.Col, FallbackSimilarityThreshold, block.Region));
                    }
                    minSimilarity = FallbackSimilarityThreshold;
                    mostChangedIndex = textRow * cols;
                }
                else
                {
                    // GPUノイズ → 抑制候補（連続SUPPRESS検知で強制検出に昇格）
                    var suppressCount = _consecutiveSuppressCount.AddOrUpdate(contextId, 1, (_, count) => count + 1);

                    if (suppressCount >= MaxConsecutiveSuppressBeforeForceDetect)
                    {
                        // 連続SUPPRESS限界超過 → 実際の変化と判断して強制検出
                        _logger.LogInformation(
                            "🔄 [NewStage1_SUPPRESS_OVERRIDE] 連続SUPPRESS {Count}回で強制変化検出 - RobustDiff: {Diff}, Threshold: {Threshold}, Context: {ContextId}",
                            suppressCount, robustDiff, RobustChecksumDiffThreshold, contextId);

                        var textRow = rows - 1;
                        for (int col = 0; col < cols; col++)
                        {
                            var blockIndex = textRow * cols + col;
                            var block = blockResults.First(b => b.Index == blockIndex);
                            changedBlocks.Add(new BlockChangeInfo(block.Index, block.Row, block.Col, FallbackSimilarityThreshold, block.Region));
                        }
                        minSimilarity = FallbackSimilarityThreshold;
                        mostChangedIndex = textRow * cols;
                        _consecutiveSuppressCount.TryRemove(contextId, out _);
                        // キャッシュ更新あり（新しい参照フレームへ移行）
                    }
                    else
                    {
                        // SUPPRESS継続 → キャッシュ更新をスキップして参照フレームを維持
                        skipCacheUpdate = true;
                        _logger.LogDebug(
                            "🛡️ [NewStage1_FALLBACK_SUPPRESSED] ロバストチェックサム差分が閾値以下 - Diff: {Diff}, Threshold: {Threshold}, MinSim: {MinSim:F4}, SuppressCount: {SuppressCount}/{Max}, Context: {ContextId}",
                            robustDiff, RobustChecksumDiffThreshold, minSimilarity, suppressCount, MaxConsecutiveSuppressBeforeForceDetect, contextId);
                    }
                }
            }

            // 変化検出時 or チェックサム未変化時は連続SUPPRESSカウンターをリセット
            if (changedBlocks.Count > 0 || !checksumChanged)
            {
                _consecutiveSuppressCount.TryRemove(contextId, out _);
            }

            // キャッシュ更新（SUPPRESS時はスキップして参照フレームを維持）
            if (!skipCacheUpdate)
            {
                var updatedCache = new GridHashCache(
                    blockResults.OrderBy(b => b.Index).Select(b => b.Hash).ToArray(),
                    rows, cols, DateTime.UtcNow, currentChecksum, currentRobustChecksum);
                _gridHashCache.AddOrUpdate(contextId, updatedCache, (_, _) => updatedCache);
            }

            // 🔍 [DIAGNOSTIC] MinSimilarity=1.0000の場合、詳細ログ出力
            if (minSimilarity >= 0.9999f)
            {
                // [Issue #397] テキスト領域が含まれる最終行ブロックのハッシュ値を確認
                var lastRow = rows - 1;
                var lastRowBlock0 = blockResults.FirstOrDefault(b => b.Row == lastRow && b.Col == 0);
                if (lastRowBlock0.Hash != null)
                {
                    var cachedHash = cachedGrid.BlockHashes[lastRowBlock0.Index];
                    var currentHash = lastRowBlock0.Hash;
                    // ハッシュの先頭8文字を比較用に出力
                    var cachedShort = cachedHash.Length > 8 ? cachedHash[..8] : cachedHash;
                    var currentShort = currentHash.Length > 8 ? currentHash[..8] : currentHash;
                    _logger.LogDebug("🔍 [NewStage1_DIAG] MinSim=1.0 - Block[{LastRow},0] CachedHash={Cached}..., CurrentHash={Current}..., CacheAge={Age:F1}s",
                        lastRow, cachedShort, currentShort, (DateTime.UtcNow - cachedGrid.Timestamp).TotalSeconds);
                }

                // 🔍 [DIAGNOSTIC] 画像バイト単位のチェックサム比較
                // キャプチャ層で同一画像が返されていないか確認
                try
                {
                    var imageMemory = currentImage.GetImageMemory();
                    var imageArray = imageMemory.ToArray();
                    var headChecksum = 0;
                    // 先頭2000バイトと末尾2000バイトのチェックサム
                    var headLimit = Math.Min(imageArray.Length, 2000);
                    var tailStart = Math.Max(0, imageArray.Length - 2000);
                    for (int i = 0; i < headLimit; i++)
                    {
                        headChecksum += imageArray[i];
                    }
                    var tailChecksum = 0;
                    for (int i = tailStart; i < imageArray.Length; i++)
                    {
                        tailChecksum += imageArray[i];
                    }
                    _logger.LogDebug("🔍 [NewStage1_DIAG] HeadChecksum={HeadSum}, TailChecksum={TailSum}, ImageSize={Width}x{Height}, TotalBytes={Total}",
                        headChecksum, tailChecksum, currentImage.Width, currentImage.Height, imageArray.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("🔍 [NewStage1_DIAG] ImageChecksum計算失敗: {Error}", ex.Message);
                }
            }

            _logger.LogDebug("🔲 [NewStage1] 完了 - Context: {ContextId}, ChangedBlocks: {Count}, MinSimilarity: {MinSim:F4}",
                contextId, changedBlocks.Count, minSimilarity);

            return new GridChangeDetectionResult
            {
                ProcessingTime = stopwatch.Elapsed,
                ChangedBlocks = changedBlocks,
                TotalBlocks = totalBlocks,
                GridRows = rows,
                GridColumns = cols,
                MinSimilarity = minSimilarity,
                MostChangedBlockIndex = mostChangedIndex
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔲 [NewStage1] エラー - Context: {ContextId}", contextId);
            return new GridChangeDetectionResult
            {
                ProcessingTime = stopwatch.Elapsed,
                TotalBlocks = totalBlocks,
                GridRows = rows,
                GridColumns = cols
            };
        }
    }

    /// <summary>
    /// [Issue #229] 新 Stage 2: Change Validation
    /// ノイズフィルタリング - カーソル点滅、軽微なアニメーションを除外
    /// </summary>
    private ChangeValidationResult ExecuteNewStage2_ChangeValidation(GridChangeDetectionResult stage1Result)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!stage1Result.HasPotentialChange)
        {
            return new ChangeValidationResult
            {
                ProcessingTime = stopwatch.Elapsed,
                IsSignificantChange = false,
                FilterReason = "No changed blocks",
                ChangedBlockCount = 0,
                Stage1Result = stage1Result
            };
        }

        var changedBlocks = stage1Result.ChangedBlocks;
        var rows = stage1Result.GridRows;
        var cols = stage1Result.GridColumns;

        // 隣接ブロック判定
        bool hasAdjacentBlocks = HasAdjacentChangedBlocks(changedBlocks, cols);

        // 端ブロック判定（グリッドの外周）
        bool isEdgeOnlyChange = changedBlocks.All(b => IsEdgeBlock(b.Row, b.Col, rows, cols));

        // ノイズ判定ロジック
        bool isNoise = false;
        string? filterReason = null;

        if (changedBlocks.Count == 1)
        {
            var block = changedBlocks[0];
            // [Issue #319] 下端（row == rows - 1）はテキスト領域の可能性が高いため除外
            // ゲームのテキストは画面下部に表示されることが多く、テキスト送りの変化を
            // ノイズ扱いすると40秒以上の遅延が発生する
            bool isBottomRow = block.Row == rows - 1;

            // 単一ブロック + 端（下端を除く）+ 軽微な変化 → ノイズ（カーソル点滅など）
            if (IsEdgeBlock(block.Row, block.Col, rows, cols) && block.Similarity > 0.90f && !isBottomRow)
            {
                isNoise = true;
                var position = IsCornerBlock(block.Row, block.Col, rows, cols) ? "Corner" : "Edge";
                filterReason = $"Single edge block with minor change (similarity: {block.Similarity:F4}, position: {position})";

                // [Issue #229] テレメトリ: 潜在的false negative のデータ収集
                // 将来のオプションE/F判断のための専用CSVログ
                WriteTelemetryLog(position, block.Row, block.Col, block.Similarity, rows, cols);
            }
            else if (isBottomRow && IsEdgeBlock(block.Row, block.Col, rows, cols) && block.Similarity > 0.90f)
            {
                // [Code Review] 下端ブロックはノイズ判定から除外されたことをログ
                _logger.LogDebug(
                    "[Issue #319] 下端ブロックはノイズ判定から除外: Row={Row}, Col={Col}, Similarity={Similarity:F4}",
                    block.Row, block.Col, block.Similarity);
            }
        }

        // 有意な変化判定
        bool isSignificant = !isNoise && (
            changedBlocks.Count >= 2 ||           // 複数ブロック変化
            hasAdjacentBlocks ||                  // 隣接ブロック変化（テキストの可能性高）
            !isEdgeOnlyChange ||                  // 中央ブロック含む
            changedBlocks.Any(b => b.Similarity < 0.85f)  // 大きな変化
        );

        _logger.LogDebug("🔲 [NewStage2] 検証完了 - ChangedBlocks: {Count}, Adjacent: {Adjacent}, EdgeOnly: {EdgeOnly}, IsNoise: {IsNoise}, IsSignificant: {IsSignificant}",
            changedBlocks.Count, hasAdjacentBlocks, isEdgeOnlyChange, isNoise, isSignificant);

        return new ChangeValidationResult
        {
            ProcessingTime = stopwatch.Elapsed,
            IsSignificantChange = isSignificant,
            FilterReason = filterReason,
            ChangedBlockCount = changedBlocks.Count,
            HasAdjacentBlocks = hasAdjacentBlocks,
            IsEdgeOnlyChange = isEdgeOnlyChange,
            Stage1Result = stage1Result
        };
    }

    /// <summary>
    /// [Issue #229] 新 Stage 3: Region Analysis
    /// 変化領域の特定（将来的なOCR最適化用）
    /// </summary>
    private RegionAnalysisResult ExecuteNewStage3_RegionAnalysis(ChangeValidationResult stage2Result)
    {
        var stopwatch = Stopwatch.StartNew();

        if (stage2Result.Stage1Result == null || !stage2Result.IsSignificantChange)
        {
            return new RegionAnalysisResult
            {
                ProcessingTime = stopwatch.Elapsed,
                ChangedRegions = [],
                TotalChangedArea = 0,
                ChangePercentage = 0f
            };
        }

        var changedBlocks = stage2Result.Stage1Result.ChangedBlocks;

        // 変化ブロックの領域を収集
        var regions = changedBlocks.Select(b => b.Region).ToArray();

        // 総面積計算
        var totalArea = regions.Sum(r => r.Width * r.Height);

        _logger.LogDebug("🔲 [NewStage3] 領域分析完了 - Regions: {Count}, TotalArea: {Area}px",
            regions.Length, totalArea);

        return new RegionAnalysisResult
        {
            ProcessingTime = stopwatch.Elapsed,
            ChangedRegions = regions,
            TotalChangedArea = totalArea,
            ChangePercentage = stage2Result.Stage1Result.MinSimilarity > 0
                ? 1.0f - stage2Result.Stage1Result.MinSimilarity
                : 1.0f
        };
    }

    /// <summary>
    /// 隣接ブロックが存在するかチェック（8方向：上下左右＋斜め）
    /// [Gemini Review] 斜め方向の隣接も検出するように修正
    /// </summary>
    private static bool HasAdjacentChangedBlocks(IReadOnlyList<BlockChangeInfo> changedBlocks, int cols)
    {
        if (changedBlocks.Count < 2) return false;

        var blockSet = changedBlocks.Select(b => (b.Row, b.Col)).ToHashSet();

        foreach (var block in changedBlocks)
        {
            // 8方向（上下左右＋斜め）をチェック
            for (int r = -1; r <= 1; r++)
            {
                for (int c = -1; c <= 1; c++)
                {
                    if (r == 0 && c == 0) continue; // 自身はスキップ
                    if (blockSet.Contains((block.Row + r, block.Col + c)))
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 端ブロック（グリッド外周）かどうか判定
    /// </summary>
    private static bool IsEdgeBlock(int row, int col, int rows, int cols)
    {
        return row == 0 || row == rows - 1 || col == 0 || col == cols - 1;
    }

    /// <summary>
    /// [Issue #229] 角ブロック（グリッド四隅）かどうか判定
    /// テレメトリおよび将来のオプションE実装用
    /// </summary>
    private static bool IsCornerBlock(int row, int col, int rows, int cols)
    {
        return (row == 0 || row == rows - 1) && (col == 0 || col == cols - 1);
    }

    /// <summary>
    /// [Issue #229] テレメトリログをCSVファイルに出力
    /// Stage 2でノイズ判定された潜在的false negativeのデータを収集
    /// </summary>
    private void WriteTelemetryLog(string position, int row, int col, float similarity, int rows, int cols)
    {
        if (!_loggingSettings.EnableTelemetryLogging)
            return;

        try
        {
            var telemetryPath = _loggingSettings.GetFullTelemetryLogPath();

            lock (_telemetryLock)
            {
                // CSVヘッダー初期化（ファイルが存在しない場合）
                if (!_telemetryInitialized)
                {
                    if (!File.Exists(telemetryPath))
                    {
                        File.WriteAllText(telemetryPath, "Timestamp,Position,Row,Col,Similarity,GridRows,GridCols\n");
                    }
                    _telemetryInitialized = true;
                }

                // CSVデータ追記
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff},{position},{row},{col},{similarity:F4},{rows},{cols}\n";
                File.AppendAllText(telemetryPath, line);
            }
        }
        catch (Exception ex)
        {
            // テレメトリ書き込み失敗は警告のみ（メイン処理に影響させない）
            _logger.LogWarning(ex, "📊 [Stage2_Telemetry] テレメトリログ書き込み失敗");
        }
    }

    #endregion

    /// <summary>
    /// Stage 2: 中精度検証実行（レガシー）
    /// 目標: 8%のフレームを<3msで処理
    /// </summary>
    private async Task<ImageChangeResult> ExecuteStage2MediumPrecisionAsync(
        IImage previousImage,
        IImage currentImage,
        string contextId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("🔥 [STAGE2_ENTRY] Stage 2メソッド開始 - ContextId: {ContextId}", contextId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var imageType = await DetectImageTypeAsync(currentImage);
            var algorithm = _perceptualHashService.GetOptimalAlgorithm(imageType);

            var currentHash = _perceptualHashService.ComputeHash(currentImage, algorithm);

            // キャッシュから前回Hash取得・更新
            string previousHash = "";
            if (_imageHashCache.TryGetValue(contextId, out var cachedHash))
            {
                previousHash = cachedHash.Hash;
            }

            var newCachedHash = new CachedImageHash(currentHash, DateTime.UtcNow, algorithm);
            _imageHashCache.AddOrUpdate(contextId, newCachedHash, (_, _) => newCachedHash);

            if (string.IsNullOrEmpty(previousHash))
            {
                _logger.LogDebug("🔥 [STAGE2_FIRSTTIME] 初回検知 - Algo: {Algorithm}, HasChanged: true", algorithm);
                return ImageChangeResult.CreateFirstTime(currentHash, algorithm, stopwatch.Elapsed);
            }

            // 中精度比較（ハミング距離ベース）
            // [Issue #230] 32x32ハッシュ対応: 1024ビット正規化
            var hammingDistance = _perceptualHashService.CalculateHammingDistance(previousHash, currentHash);
            var maxBits = Math.Max(previousHash.Length, currentHash.Length) * 4; // 16進数1文字=4bit
            var changePercentage = maxBits > 0 ? hammingDistance / (float)maxBits : 0f;
            var hasChanged = changePercentage >= _settings.Stage2ChangePercentageThreshold; // Stage2変化率閾値（設定外部化）

            _logger.LogDebug("🔥 [STAGE2_COMPARE] HammingDist: {HammingDist}, MaxBits: {MaxBits}, ChangeRate: {ChangeRate:F4}, Threshold: {Threshold:F4}, HasChange: {HasChange}",
                hammingDistance, maxBits, changePercentage, _settings.Stage2ChangePercentageThreshold, hasChanged);

            return hasChanged
                ? ImageChangeResult.CreateChanged(previousHash, currentHash, changePercentage, algorithm, stopwatch.Elapsed, detectionStage: 2)
                : ImageChangeResult.CreateNoChange(stopwatch.Elapsed, detectionStage: 2);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stage 2中精度検証エラー - Context: {ContextId}", contextId);
            return ImageChangeResult.CreateChanged("ERROR", "ERROR", 1.0f, HashAlgorithmType.DifferenceHash, stopwatch.Elapsed, detectionStage: 2);
        }
    }

    /// <summary>
    /// Stage 3: 高精度解析実行
    /// 目標: 2%のフレームを<5msで精密解析
    /// </summary>
    private async Task<ImageChangeResult> ExecuteStage3HighPrecisionAsync(
        IImage previousImage,
        IImage currentImage,
        string contextId,
        ImageChangeResult stage2Result,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // SSIM構造的類似性計算
            var ssimScore = await _perceptualHashService.CalculateSSIMAsync(previousImage, currentImage);
            var hasChanged = ssimScore < _settings.Stage3SSIMThreshold; // Stage3 SSIM高精度閾値（設定外部化）

            // ROI解析（変化領域特定）
            var changeRegions = hasChanged
                ? await DetectChangeRegionsAsync(previousImage, currentImage, cancellationToken)
                : Array.Empty<Rectangle>();

            var finalChangePercentage = hasChanged
                ? Math.Max(stage2Result.ChangePercentage, 1.0f - ssimScore)
                : 0.0f;

            var result = new ImageChangeResult
            {
                HasChanged = hasChanged,
                ChangePercentage = finalChangePercentage,
                ChangedRegions = changeRegions,
                ProcessingTime = stopwatch.Elapsed,
                AlgorithmUsed = stage2Result.AlgorithmUsed,
                PreviousHash = stage2Result.PreviousHash,
                CurrentHash = stage2Result.CurrentHash,
                DetectionStage = 3,
                SSIMScore = ssimScore,
                AdditionalMetrics = new Dictionary<string, object>
                {
                    ["Stage2ChangePercentage"] = stage2Result.ChangePercentage,
                    ["ChangeRegionCount"] = changeRegions.Length,
                    ["ImageType"] = await DetectImageTypeAsync(currentImage)
                }
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stage 3高精度解析エラー - Context: {ContextId}", contextId);
            return stage2Result with
            {
                ProcessingTime = stopwatch.Elapsed,
                DetectionStage = 3,
                AdditionalMetrics = new Dictionary<string, object> { ["Stage3Error"] = ex.Message }
            };
        }
    }

    /// <summary>
    /// 初回検知結果を作成
    /// </summary>
    private async Task<ImageChangeResult> CreateFirstTimeResultAsync(IImage currentImage, string contextId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var imageType = await DetectImageTypeAsync(currentImage);
            var algorithm = _perceptualHashService.GetOptimalAlgorithm(imageType);
            var currentHash = _perceptualHashService.ComputeHash(currentImage, algorithm);

            // キャッシュ初期化
            var cachedHash = new CachedImageHash(currentHash, DateTime.UtcNow, algorithm);
            _imageHashCache.AddOrUpdate(contextId, cachedHash, (_, _) => cachedHash);

            return ImageChangeResult.CreateFirstTime(currentHash, algorithm, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初回検知結果作成エラー - Context: {ContextId}", contextId);
            return ImageChangeResult.CreateFirstTime("ERROR", HashAlgorithmType.AverageHash, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// 変化領域を検出（簡易実装）
    /// </summary>
    private async Task<Rectangle[]> DetectChangeRegionsAsync(IImage previousImage, IImage currentImage, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            try
            {
                // [Issue #397] 設定値ベースのグリッド領域分割検出
                var regions = new List<Rectangle>();
                var gridCols = _settings.GridColumns;
                var gridRows = _settings.GridRows;

                var regionWidth = currentImage.Width / gridCols;
                var regionHeight = currentImage.Height / gridRows;

                for (int y = 0; y < gridRows; y++)
                {
                    for (int x = 0; x < gridCols; x++)
                    {
                        regions.Add(new Rectangle(
                            x * regionWidth,
                            y * regionHeight,
                            regionWidth,
                            regionHeight));
                    }
                }

                return [.. regions.Take(gridCols)]; // 最大1行分の領域
            }
            catch
            {
                return Array.Empty<Rectangle>();
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 段階別処理時間を記録
    /// </summary>
    private void RecordStageTime(int stage, TimeSpan time)
    {
        if (_stageTimings.TryGetValue(stage, out var timings))
        {
            lock (timings)
            {
                timings.Add(time);
                // 最新100件のみ保持
                if (timings.Count > 100)
                {
                    timings.RemoveAt(0);
                }
            }
        }
    }

    /// <summary>
    /// 段階別平均処理時間を計算
    /// </summary>
    private TimeSpan CalculateAverageTime(int stage)
    {
        if (!_stageTimings.TryGetValue(stage, out var timings) || !timings.Any())
        {
            return TimeSpan.Zero;
        }

        lock (timings)
        {
            var averageTicks = timings.Select(t => t.Ticks).Average();
            return TimeSpan.FromTicks((long)averageTicks);
        }
    }

    /// <summary>
    /// キャッシュヒット率を計算
    /// </summary>
    private float CalculateCacheHitRate()
    {
        var totalProcessed = Interlocked.Read(ref _totalProcessed);
        var cacheSize = _quickHashCache.Count + _imageHashCache.Count;

        return totalProcessed > 0 ? Math.Min(1.0f, (float)cacheSize / totalProcessed) : 0f;
    }

    // [Issue #229][Gemini Review] チェックサム計算用定数
    private const int ChecksumSampleSize = 2000;
    private const float FallbackSimilarityThreshold = 0.95f;

    // [Issue #436] ロバストチェックサム差分閾値
    // カーソルノイズ: ~1,800（256サンプル × 最大差7）
    // テキスト変化: ~30,000+（数千サンプル × 平均差3-4）
    private const long RobustChecksumDiffThreshold = 5000;

    /// <summary>
    /// [Issue #293/#302統合] ROI統合動的閾値を取得
    /// </summary>
    /// <param name="row">グリッド行インデックス</param>
    /// <param name="col">グリッド列インデックス</param>
    /// <param name="totalRows">グリッド総行数</param>
    /// <param name="totalCols">グリッド総列数</param>
    /// <returns>適用すべき閾値</returns>
    /// <remarks>
    /// [Issue #302統合] ROI動的閾値が有効な場合:
    /// - 静的ゾーン閾値（EnableLowerZoneHighSensitivity）をバイパス
    /// - 一律のGridBlockSimilarityThresholdをベースにROI乗数を適用
    /// - ROI学習結果のみに基づいて閾値を決定
    ///
    /// ROI動的閾値が無効な場合:
    /// - 従来のGetThresholdForRow()（静的ゾーン閾値）を使用
    /// </remarks>
    private float GetDynamicThreshold(int row, int col, int totalRows, int totalCols)
    {
        // [Issue #302統合] ROI動的閾値が有効な場合は静的ゾーンロジックをバイパス
        if (_settings.EnableRoiBasedThreshold && _roiThresholdProvider.IsEnabled)
        {
            // ROI動的閾値: 一律のGridBlockSimilarityThresholdをベースにROI乗数を適用
            return _roiThresholdProvider.GetThresholdForCell(
                row, col, totalRows, totalCols, _settings.GridBlockSimilarityThreshold);
        }

        // [フォールバック] 従来の静的ゾーン閾値（ROI動的閾値が無効な場合）
        return _settings.GetThresholdForRow(row, totalRows);
    }

    /// <summary>
    /// [Issue #229][Gemini Review] 画像のチェックサムを計算
    /// ハッシュが衝突した場合のフォールバック検出用
    /// XxHash64を使用して衝突耐性を向上（単純加算より堅牢）
    /// 高速計算のため、画像全体ではなくサンプリングポイントを使用
    /// </summary>
    /// <param name="image">対象画像</param>
    /// <returns>チェックサム値（XxHash64）</returns>
    private long CalculateImageChecksum(IImage image)
    {
        try
        {
            var imageMemory = image.GetImageMemory();
            var imageSpan = imageMemory.Span;

            if (imageSpan.IsEmpty) return 0;

            var xxHash = new XxHash64();

            // 先頭サンプル
            var headLength = Math.Min(imageSpan.Length, ChecksumSampleSize);
            xxHash.Append(imageSpan[..headLength]);

            // 中央サンプル（重複を避けるため、十分な長さがある場合のみ）
            if (imageSpan.Length > ChecksumSampleSize * 3)
            {
                var midStart = imageSpan.Length / 2 - ChecksumSampleSize / 2;
                var midLength = Math.Min(ChecksumSampleSize, imageSpan.Length - midStart);
                xxHash.Append(imageSpan.Slice(midStart, midLength));
            }

            // 末尾サンプル（テキスト領域を含む可能性が高い）
            var tailStart = Math.Max(headLength, imageSpan.Length - ChecksumSampleSize);
            if (tailStart < imageSpan.Length)
            {
                xxHash.Append(imageSpan[tailStart..]);
            }

            return (long)xxHash.GetCurrentHashAsUInt64();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "チェックサム計算エラー - デフォルト値を返却");
            return 0;
        }
    }

    /// <summary>
    /// [Issue #436] ロバストチェックサム（量子化SUM）を計算
    /// GPUキャプチャノイズ（カーソル点滅等）に耐性のある変化検知用。
    /// 画像全体を16バイト間隔でサンプリングし、各バイトを3bit量子化(>>5, 8段階)した合計値を返す。
    /// SUM方式のため、局所的なノイズ（カーソル ~256サンプル）は閾値以下に収まり、
    /// テキスト変化（数千サンプル）は閾値を大きく超える。
    /// </summary>
    private long CalculateRobustImageChecksum(IImage image)
    {
        try
        {
            var imageMemory = image.GetImageMemory();
            var imageSpan = imageMemory.Span;

            if (imageSpan.IsEmpty) return 0;

            long sum = 0;
            for (int i = 0; i < imageSpan.Length; i += 16)
            {
                sum += imageSpan[i] >> 5; // 3bit量子化: 0-255 → 0-7
            }

            return sum;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ロバストチェックサム計算エラー");
            return 0;
        }
    }

    #endregion
}

/// <summary>
/// [Issue #229] テキスト安定化待機状態
/// </summary>
/// <remarks>
/// テキストアニメーション（タイプライター効果）検知後の安定化待機を管理。
/// - FirstChangeTime: 安定化モード開始時刻
/// - LastChangeTime: 最後の変化検知時刻
/// - IsInStabilization: 安定化待機モード中かどうか
///
/// [Gemini Review] スレッドセーフティのため record から class に変更。
/// 各インスタンスをロック対象として使用可能に。
/// </remarks>
internal sealed class StabilizationState
{
    /// <summary>
    /// 安定化モード開始時刻
    /// </summary>
    public DateTime FirstChangeTime { get; private set; }

    /// <summary>
    /// 最後の変化検知時刻
    /// </summary>
    public DateTime LastChangeTime { get; private set; }

    /// <summary>
    /// 安定化待機モード中かどうか
    /// </summary>
    public bool IsInStabilization { get; private set; }

    private StabilizationState()
    {
        FirstChangeTime = DateTime.MinValue;
        LastChangeTime = DateTime.MinValue;
        IsInStabilization = false;
    }

    /// <summary>
    /// アイドル状態のインスタンスを作成
    /// </summary>
    public static StabilizationState CreateIdle() => new();

    /// <summary>
    /// 安定化モード開始
    /// </summary>
    public void EnterStabilization()
    {
        var now = DateTime.UtcNow;
        FirstChangeTime = now;
        LastChangeTime = now;
        IsInStabilization = true;
    }

    /// <summary>
    /// 変化検知時刻を更新
    /// </summary>
    public void UpdateLastChange()
    {
        LastChangeTime = DateTime.UtcNow;
    }

    /// <summary>
    /// 安定化モード終了（アイドル状態へリセット）
    /// </summary>
    public void Reset()
    {
        IsInStabilization = false;
        FirstChangeTime = DateTime.MinValue;
        LastChangeTime = DateTime.MinValue;
    }

    /// <summary>
    /// 安定化待機時間が経過したか確認
    /// </summary>
    /// <param name="now">判定基準時刻</param>
    /// <param name="delayMs">安定化待機時間（ミリ秒）</param>
    /// <returns>安定化完了の場合true</returns>
    public bool HasStabilized(DateTime now, int delayMs) =>
        (now - LastChangeTime).TotalMilliseconds >= delayMs;

    /// <summary>
    /// 最大待機時間を超過したか確認
    /// </summary>
    /// <param name="now">判定基準時刻</param>
    /// <param name="maxWaitMs">最大待機時間（ミリ秒）</param>
    /// <returns>タイムアウトの場合true</returns>
    public bool HasTimedOut(DateTime now, int maxWaitMs) =>
        (now - FirstChangeTime).TotalMilliseconds >= maxWaitMs;
}
