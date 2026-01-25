namespace Baketa.Core.Models.Roi;

/// <summary>
/// 学習進捗モデル
/// </summary>
/// <remarks>
/// Issue #293 Phase 10: 学習駆動型投機的OCR
/// ROI学習の進捗状況を表します。
/// </remarks>
public sealed record LearningProgress
{
    /// <summary>
    /// 高信頼度ROI領域の数
    /// </summary>
    public int HighConfidenceRegionCount { get; init; }

    /// <summary>
    /// 総検出回数（OCR実行回数）
    /// </summary>
    public int TotalDetectionCount { get; init; }

    /// <summary>
    /// 総検出テキスト領域数
    /// </summary>
    public int TotalTextRegionsDetected { get; init; }

    /// <summary>
    /// ヒートマップカバー率（0.0-1.0）
    /// </summary>
    /// <remarks>
    /// ROIとして認識された領域が、テキスト検出された全領域をどれだけカバーしているか
    /// </remarks>
    public float HeatmapCoverage { get; init; }

    /// <summary>
    /// 現在の学習フェーズ
    /// </summary>
    public LearningPhase Phase { get; init; } = LearningPhase.Initial;

    /// <summary>
    /// 学習が完了しているか（維持モードに移行済み）
    /// </summary>
    public bool IsLearningComplete => Phase == LearningPhase.Maintenance;

    /// <summary>
    /// 学習開始時刻
    /// </summary>
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 最終更新時刻
    /// </summary>
    public DateTime LastUpdatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 学習完了時刻（null = 未完了）
    /// </summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// 学習の進捗率（0.0-1.0）
    /// </summary>
    /// <param name="settings">学習完了判定の設定</param>
    public float GetProgressRate(int minHighConfidenceRegions, int minTotalDetections, float minHeatmapCoverage)
    {
        var regionProgress = minHighConfidenceRegions > 0
            ? Math.Min(1.0f, (float)HighConfidenceRegionCount / minHighConfidenceRegions)
            : 1.0f;

        var detectionProgress = minTotalDetections > 0
            ? Math.Min(1.0f, (float)TotalDetectionCount / minTotalDetections)
            : 1.0f;

        var coverageProgress = minHeatmapCoverage > 0
            ? Math.Min(1.0f, HeatmapCoverage / minHeatmapCoverage)
            : 1.0f;

        // 3つの指標の平均
        return (regionProgress + detectionProgress + coverageProgress) / 3.0f;
    }

    /// <summary>
    /// デフォルトの初期状態を作成
    /// </summary>
    public static LearningProgress CreateInitial() => new()
    {
        HighConfidenceRegionCount = 0,
        TotalDetectionCount = 0,
        TotalTextRegionsDetected = 0,
        HeatmapCoverage = 0.0f,
        Phase = LearningPhase.Initial,
        StartedAt = DateTime.UtcNow,
        LastUpdatedAt = DateTime.UtcNow
    };
}

/// <summary>
/// 学習フェーズ
/// </summary>
public enum LearningPhase
{
    /// <summary>
    /// 初期フェーズ（学習データなし、積極的にOCR実行）
    /// </summary>
    Initial,

    /// <summary>
    /// 学習中（データ蓄積中、通常頻度でOCR実行）
    /// </summary>
    Learning,

    /// <summary>
    /// 維持モード（学習完了、低頻度でチェック継続）
    /// </summary>
    Maintenance
}
