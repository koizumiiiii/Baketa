using Baketa.Core.Abstractions.Roi;
using Baketa.Core.Models.Roi;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#if NET8_0_OR_GREATER
using TimeProvider = System.TimeProvider;
#endif

namespace Baketa.Application.Services.Learning;

/// <summary>
/// 学習スケジューラ実装
/// </summary>
/// <remarks>
/// Issue #293 Phase 10: 学習駆動型投機的OCR
/// ROI学習の進捗を監視し、投機的OCRの実行頻度を動的に調整します。
/// </remarks>
public sealed class LearningScheduler : ILearningScheduler
{
    private readonly IRoiManager? _roiManager;
    private readonly IOptionsMonitor<SpeculativeOcrSettings> _settingsMonitor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LearningScheduler> _logger;

    private readonly object _lock = new();
    private LearningProgress _progress;
    private DateTimeOffset _lastExecutionTime = DateTimeOffset.MinValue;
    private DateTimeOffset _phaseTransitionTime;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <remarks>
    /// Null許容パラメータの設計意図:
    /// - roiManager はオプショナル依存
    /// - nullの場合、GetRoiMetrics()は(0, 0.0f)を返し、学習は進捗しない
    /// - ROI機能が無効化されている環境でもスケジューラは正常に動作する
    /// </remarks>
    public LearningScheduler(
        IRoiManager? roiManager,
        IOptionsMonitor<SpeculativeOcrSettings> settingsMonitor,
        ILogger<LearningScheduler> logger)
        : this(roiManager, settingsMonitor, TimeProvider.System, logger)
    {
    }

    /// <summary>
    /// テスト用コンストラクタ（TimeProviderを注入可能）
    /// </summary>
    internal LearningScheduler(
        IRoiManager? roiManager,
        IOptionsMonitor<SpeculativeOcrSettings> settingsMonitor,
        TimeProvider timeProvider,
        ILogger<LearningScheduler> logger)
    {
        _roiManager = roiManager;
        _settingsMonitor = settingsMonitor ?? throw new ArgumentNullException(nameof(settingsMonitor));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _progress = LearningProgress.CreateInitial();
        _phaseTransitionTime = _timeProvider.GetUtcNow();

        _logger.LogInformation("📚 [Phase 10] LearningScheduler初期化完了");
    }

    private SpeculativeOcrSettings Settings => _settingsMonitor.CurrentValue;

    /// <inheritdoc />
    public LearningProgress CurrentProgress
    {
        get
        {
            lock (_lock)
            {
                return _progress;
            }
        }
    }

    /// <inheritdoc />
    public LearningPhase CurrentPhase
    {
        get
        {
            lock (_lock)
            {
                return _progress.Phase;
            }
        }
    }

    /// <inheritdoc />
    public bool IsLearningComplete
    {
        get
        {
            lock (_lock)
            {
                return _progress.IsLearningComplete;
            }
        }
    }

    /// <inheritdoc />
    public TimeSpan GetNextExecutionInterval()
    {
        var phase = CurrentPhase;
        return phase switch
        {
            LearningPhase.Initial => Settings.InitialPhaseInterval,
            LearningPhase.Learning => Settings.LearningPhaseInterval,
            LearningPhase.Maintenance => Settings.MaintenancePhaseInterval,
            _ => Settings.LearningPhaseInterval
        };
    }

    /// <inheritdoc />
    public bool ShouldExecuteNow()
    {
        if (!Settings.EnableBackgroundLearning)
        {
            return false;
        }

        var interval = GetNextExecutionInterval();
        var elapsed = _timeProvider.GetUtcNow() - _lastExecutionTime;

        return elapsed >= interval;
    }

    /// <inheritdoc />
    public void OnOcrCompleted(int detectionCount, int highConfidenceCount = 0)
    {
        lock (_lock)
        {
            var now = _timeProvider.GetUtcNow();
            _lastExecutionTime = now;

            // 進捗更新
            var newTotalDetections = _progress.TotalDetectionCount + 1;
            var newTotalRegions = _progress.TotalTextRegionsDetected + detectionCount;

            // RoiManagerから高信頼度領域数とカバー率を取得
            var (roiRegionCount, heatmapCoverage) = GetRoiMetrics();

            var oldPhase = _progress.Phase;
            var newPhase = DeterminePhase(roiRegionCount, newTotalDetections, heatmapCoverage);

            _progress = _progress with
            {
                TotalDetectionCount = newTotalDetections,
                TotalTextRegionsDetected = newTotalRegions,
                HighConfidenceRegionCount = roiRegionCount,
                HeatmapCoverage = heatmapCoverage,
                Phase = newPhase,
                LastUpdatedAt = now.UtcDateTime,
                CompletedAt = newPhase == LearningPhase.Maintenance && oldPhase != LearningPhase.Maintenance
                    ? now.UtcDateTime
                    : _progress.CompletedAt
            };

            // フェーズ遷移ログ
            if (oldPhase != newPhase)
            {
                _phaseTransitionTime = now;
                _logger.LogInformation(
                    "📚 [Phase 10] 学習フェーズ遷移: {OldPhase} → {NewPhase} " +
                    "(ROI領域={RegionCount}, 検出回数={DetectionCount}, カバー率={Coverage:P0})",
                    oldPhase, newPhase, roiRegionCount, newTotalDetections, heatmapCoverage);
            }
            else if (Settings.EnableDetailedLogging)
            {
                _logger.LogDebug(
                    "📚 [Phase 10] 学習進捗更新: Phase={Phase}, ROI領域={RegionCount}, " +
                    "検出回数={DetectionCount}/{MinDetections}, カバー率={Coverage:P0}/{MinCoverage:P0}",
                    newPhase, roiRegionCount, newTotalDetections, Settings.MinTotalDetections,
                    heatmapCoverage, Settings.MinHeatmapCoverage);
            }
        }
    }

    /// <inheritdoc />
    public void ResetForNewProfile()
    {
        lock (_lock)
        {
            _progress = LearningProgress.CreateInitial();
            _lastExecutionTime = DateTimeOffset.MinValue;
            _phaseTransitionTime = _timeProvider.GetUtcNow();

            _logger.LogInformation("📚 [Phase 10] プロファイル切り替え - 学習状態リセット");
        }
    }

    /// <inheritdoc />
    public void RestoreProgress(LearningProgress progress)
    {
        lock (_lock)
        {
            _progress = progress;
            _phaseTransitionTime = _timeProvider.GetUtcNow();

            _logger.LogInformation(
                "📚 [Phase 10] 学習進捗復元: Phase={Phase}, 検出回数={DetectionCount}, カバー率={Coverage:P0}",
                progress.Phase, progress.TotalDetectionCount, progress.HeatmapCoverage);
        }
    }

    /// <summary>
    /// 学習フェーズを判定
    /// </summary>
    private LearningPhase DeterminePhase(int highConfidenceRegionCount, int totalDetections, float heatmapCoverage)
    {
        // 維持モード条件: すべての閾値を満たす
        if (highConfidenceRegionCount >= Settings.MinHighConfidenceRegions &&
            totalDetections >= Settings.MinTotalDetections &&
            heatmapCoverage >= Settings.MinHeatmapCoverage)
        {
            return LearningPhase.Maintenance;
        }

        // 学習中条件: いずれかの閾値を部分的に満たす
        if (totalDetections >= Settings.MinTotalDetections / 3 ||
            heatmapCoverage >= Settings.MinHeatmapCoverage / 2)
        {
            return LearningPhase.Learning;
        }

        // 初期フェーズ
        return LearningPhase.Initial;
    }

    /// <summary>
    /// RoiManagerからメトリクスを取得
    /// </summary>
    private (int regionCount, float coverage) GetRoiMetrics()
    {
        if (_roiManager == null || !_roiManager.IsEnabled)
        {
            return (0, 0.0f);
        }

        try
        {
            // GetAllRegions()から高信頼度領域をフィルタ
            var allRegions = _roiManager.GetAllRegions();
            var highConfidenceRegions = allRegions
                .Where(r => r.ConfidenceLevel == RoiConfidenceLevel.High)
                .ToList();
            var regionCount = highConfidenceRegions.Count;

            // カバー率計算モデル:
            // カバー率は、高信頼度ROI領域の数と累積検出回数の加重和として推定する。
            // 理由: 実際のヒートマップカバー率を正確に計算するにはIRoiManagerの拡張が必要だが、
            //       現時点では「高信頼度領域が多い」「検出回数が多い」ほど学習が進んでいると
            //       みなす簡易モデルで十分な精度を得られる。
            // 式: coverage = min(1.0, regionCount × 0.2 + detectionCount × 0.01)
            // 例: 高信頼度領域3つ + 検出40回 = 0.6 + 0.4 = 100%
            var coverage = regionCount > 0 && _progress.TotalDetectionCount > 0
                ? Math.Min(1.0f, regionCount * Settings.CoverageRegionContribution + _progress.TotalDetectionCount * Settings.CoverageDetectionContribution)
                : 0.0f;

            return (regionCount, coverage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📚 [Phase 10] RoiManagerメトリクス取得エラー");
            return (0, 0.0f);
        }
    }
}
