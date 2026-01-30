using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Models.Roi;

namespace Baketa.Core.Abstractions.Roi;

/// <summary>
/// ROI学習エンジンのインターフェース
/// </summary>
/// <remarks>
/// テキスト検出パターンを学習し、ヒートマップと高信頼度ROI領域を生成します。
/// </remarks>
public interface IRoiLearningEngine
{
    /// <summary>
    /// 学習が有効かどうか
    /// </summary>
    bool IsLearningEnabled { get; set; }

    /// <summary>
    /// 現在のヒートマップデータを取得
    /// </summary>
    RoiHeatmapData? CurrentHeatmap { get; }

    /// <summary>
    /// テキスト検出を記録
    /// </summary>
    /// <param name="normalizedBounds">検出されたテキストの正規化矩形</param>
    /// <param name="confidence">検出信頼度</param>
    /// <param name="weight">[Issue #354] 重み（クラウド検証済み=2、ローカルのみ=1）</param>
    void RecordDetection(NormalizedRect normalizedBounds, float confidence, int weight = 1);

    /// <summary>
    /// 複数のテキスト検出を一括記録
    /// </summary>
    /// <param name="detections">検出結果のコレクション</param>
    void RecordDetections(IEnumerable<(NormalizedRect bounds, float confidence)> detections);

    /// <summary>
    /// [Issue #354] 重み付きで複数のテキスト検出を一括記録
    /// </summary>
    /// <param name="detections">検出結果のコレクション（weight付き）</param>
    void RecordDetectionsWithWeight(IEnumerable<(NormalizedRect bounds, float confidence, int weight)> detections);

    /// <summary>
    /// テキスト非検出を記録（ネガティブサンプル）
    /// </summary>
    /// <param name="normalizedBounds">非検出領域の正規化矩形</param>
    void RecordNoDetection(NormalizedRect normalizedBounds);

    /// <summary>
    /// [Issue #354] テキスト検出missを記録（検出されたがテキストではなかった）
    /// </summary>
    /// <param name="normalizedBounds">miss領域の正規化矩形</param>
    /// <returns>自動除外すべき領域のリスト（閾値を超えた場合）</returns>
    IReadOnlyList<NormalizedRect> RecordMiss(NormalizedRect normalizedBounds);

    /// <summary>
    /// 現在の学習データからROI領域を生成
    /// </summary>
    /// <param name="minConfidence">最小信頼度閾値</param>
    /// <param name="minHeatmapValue">最小ヒートマップ値閾値</param>
    /// <returns>生成されたROI領域のコレクション</returns>
    IReadOnlyList<RoiRegion> GenerateRegions(float minConfidence = 0.5f, float minHeatmapValue = 0.3f);

    /// <summary>
    /// ヒートマップに減衰を適用
    /// </summary>
    /// <remarks>
    /// 古い学習データの影響を徐々に減少させます。
    /// </remarks>
    void ApplyDecay();

    /// <summary>
    /// 学習データをリセット
    /// </summary>
    void Reset();

    /// <summary>
    /// 指定した座標のヒートマップ値を取得
    /// </summary>
    /// <param name="normalizedX">正規化X座標（0.0-1.0）</param>
    /// <param name="normalizedY">正規化Y座標（0.0-1.0）</param>
    /// <returns>ヒートマップ値（0.0-1.0）</returns>
    float GetHeatmapValueAt(float normalizedX, float normalizedY);

    /// <summary>
    /// 学習状態をエクスポート
    /// </summary>
    /// <returns>ヒートマップデータ</returns>
    RoiHeatmapData ExportHeatmap();

    /// <summary>
    /// 学習状態をインポート
    /// </summary>
    /// <param name="heatmapData">ヒートマップデータ</param>
    void ImportHeatmap(RoiHeatmapData heatmapData);

    /// <summary>
    /// 学習統計を取得
    /// </summary>
    RoiLearningStatistics GetStatistics();
}

/// <summary>
/// ROI学習統計
/// </summary>
public sealed record RoiLearningStatistics
{
    /// <summary>
    /// 総サンプル数
    /// </summary>
    public long TotalSamples { get; init; }

    /// <summary>
    /// ポジティブサンプル数（テキスト検出）
    /// </summary>
    public long PositiveSamples { get; init; }

    /// <summary>
    /// ネガティブサンプル数（テキスト非検出）
    /// </summary>
    public long NegativeSamples { get; init; }

    /// <summary>
    /// 高ヒートマップ値セル数
    /// </summary>
    public int HighValueCellCount { get; init; }

    /// <summary>
    /// 平均ヒートマップ値
    /// </summary>
    public float AverageHeatmapValue { get; init; }

    /// <summary>
    /// 最大ヒートマップ値
    /// </summary>
    public float MaxHeatmapValue { get; init; }

    /// <summary>
    /// 最後の学習時刻
    /// </summary>
    public DateTime? LastLearningAt { get; init; }

    /// <summary>
    /// 学習開始時刻
    /// </summary>
    public DateTime? LearningStartedAt { get; init; }
}
