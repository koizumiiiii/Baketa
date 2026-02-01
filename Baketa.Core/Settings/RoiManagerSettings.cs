namespace Baketa.Core.Settings;

/// <summary>
/// ROI Manager設定
/// </summary>
/// <remarks>
/// ROI（Region of Interest）管理システムの設定を管理します。
/// テキスト出現位置の学習、動的閾値制御に関するパラメータを含みます。
/// </remarks>
public sealed record RoiManagerSettings
{
    /// <summary>
    /// ROI管理機能を有効化
    /// </summary>
    /// <remarks>
    /// falseの場合、ROI学習と動的閾値は無効になります。
    /// デフォルト: false（既存動作との互換性を維持）
    /// </remarks>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// 自動学習を有効化
    /// </summary>
    /// <remarks>
    /// テキスト検出結果から自動的にROI領域を学習します。
    /// </remarks>
    public bool AutoLearningEnabled { get; init; } = true;

    /// <summary>
    /// ヒートマップの行数（垂直分割数）
    /// </summary>
    /// <remarks>
    /// デフォルト: 16（1920x1080で約67ピクセル単位）
    /// 推奨範囲: 8-32
    /// </remarks>
    public int HeatmapRows { get; init; } = 16;

    /// <summary>
    /// ヒートマップの列数（水平分割数）
    /// </summary>
    /// <remarks>
    /// デフォルト: 16（1920x1080で約120ピクセル単位）
    /// 推奨範囲: 8-32
    /// </remarks>
    public int HeatmapColumns { get; init; } = 16;

    /// <summary>
    /// 学習率
    /// </summary>
    /// <remarks>
    /// 指数移動平均の学習率。高いほど新しいデータを重視。
    /// デフォルト: 0.1（10%）
    /// 推奨範囲: 0.01-0.3
    /// </remarks>
    public float LearningRate { get; init; } = 0.1f;

    /// <summary>
    /// 減衰率
    /// </summary>
    /// <remarks>
    /// 定期的に適用される減衰率。古いデータの影響を減少。
    /// デフォルト: 0.01（1%）
    /// 推奨範囲: 0.001-0.05
    /// </remarks>
    public float DecayRate { get; init; } = 0.01f;

    /// <summary>
    /// 減衰間隔（秒）
    /// </summary>
    /// <remarks>
    /// この間隔で減衰を適用します。
    /// デフォルト: 60秒
    /// </remarks>
    public int DecayIntervalSeconds { get; init; } = 60;

    /// <summary>
    /// ROI領域生成の最小信頼度閾値
    /// </summary>
    /// <remarks>
    /// この閾値以上のヒートマップ値を持つ領域をROIとして認識。
    /// デフォルト: 0.3（30%）
    /// 推奨範囲: 0.2-0.5
    /// </remarks>
    public float MinConfidenceForRegion { get; init; } = 0.3f;

    /// <summary>
    /// 高信頼度と判定するヒートマップ値の閾値
    /// </summary>
    /// <remarks>
    /// この閾値以上で高信頼度ROIとして扱います。
    /// デフォルト: 0.7（70%）
    /// 推奨範囲: 0.5-0.9
    /// </remarks>
    public float HighConfidenceThreshold { get; init; } = 0.7f;

    /// <summary>
    /// ROI領域の最小サイズ（正規化、0.0-1.0）
    /// </summary>
    /// <remarks>
    /// これより小さい領域はROIとして認識しません。
    /// デフォルト: 0.02（画面の2%）
    /// </remarks>
    public float MinRegionSize { get; init; } = 0.02f;

    /// <summary>
    /// ROI領域の最大数
    /// </summary>
    /// <remarks>
    /// プロファイルあたりの最大ROI領域数。
    /// デフォルト: 50
    /// </remarks>
    public int MaxRegionsPerProfile { get; init; } = 50;

    /// <summary>
    /// プロファイルの最大保持期間（日）
    /// </summary>
    /// <remarks>
    /// この期間アクセスがないプロファイルは自動削除対象。
    /// デフォルト: 90日
    /// </remarks>
    public int ProfileMaxAgeDays { get; init; } = 90;

    /// <summary>
    /// 自動保存間隔（秒）
    /// </summary>
    /// <remarks>
    /// この間隔で学習データを自動保存。
    /// デフォルト: 300秒（5分）
    /// </remarks>
    public int AutoSaveIntervalSeconds { get; init; } = 300;

    /// <summary>
    /// ROIベースの動的閾値を有効化
    /// </summary>
    /// <remarks>
    /// ROI領域ごとに異なる変化検知閾値を適用。
    /// デフォルト: false（既存動作との互換性を維持）
    /// </remarks>
    public bool EnableDynamicThreshold { get; init; } = false;

    /// <summary>
    /// 高ヒートマップ領域に適用する閾値調整係数
    /// </summary>
    /// <remarks>
    /// 基準閾値に乗算。1.0より大きいと厳しく、小さいと緩く。
    /// デフォルト: 1.05（5%厳しく）
    /// 推奨範囲: 0.95-1.1
    /// </remarks>
    public float HighHeatmapThresholdMultiplier { get; init; } = 1.05f;

    /// <summary>
    /// 低ヒートマップ領域に適用する閾値調整係数
    /// </summary>
    /// <remarks>
    /// 基準閾値に乗算。
    /// デフォルト: 0.95（5%緩く）
    /// 推奨範囲: 0.9-1.0
    /// </remarks>
    public float LowHeatmapThresholdMultiplier { get; init; } = 0.95f;

    #region [Issue #293] 部分OCR設定

    /// <summary>
    /// 部分OCR機能を有効化
    /// </summary>
    /// <remarks>
    /// 変化領域のみOCRを実行し、処理時間を短縮します。
    /// デフォルト: true（有効）
    /// </remarks>
    public bool EnablePartialOcr { get; init; } = true;

    /// <summary>
    /// 隣接領域結合のマージン（ピクセル）
    /// </summary>
    /// <remarks>
    /// 2つの変化領域がこのマージン内で接している場合、1つに結合します。
    /// デフォルト: 5ピクセル
    /// 推奨範囲: 0-20
    /// </remarks>
    public int AdjacencyMargin { get; init; } = 5;

    /// <summary>
    /// 部分OCRの最小幅（ピクセル）
    /// </summary>
    /// <remarks>
    /// これより小さい領域はOCR対象外となります。
    /// デフォルト: 100ピクセル
    /// 推奨範囲: 50-200
    /// </remarks>
    public int MinPartialOcrWidth { get; init; } = 100;

    /// <summary>
    /// 部分OCRの最小高さ（ピクセル）
    /// </summary>
    /// <remarks>
    /// これより小さい領域はOCR対象外となります。
    /// デフォルト: 50ピクセル
    /// 推奨範囲: 30-100
    /// </remarks>
    public int MinPartialOcrHeight { get; init; } = 50;

    /// <summary>
    /// 部分OCRフォールバック閾値（カバー率）
    /// </summary>
    /// <remarks>
    /// 変化領域が画面のこの割合以上を占める場合、全画面OCRにフォールバック。
    /// デフォルト: 0.7（70%）
    /// 推奨範囲: 0.5-0.9
    /// </remarks>
    public float MaxPartialOcrCoverageRatio { get; init; } = 0.7f;

    /// <summary>
    /// 部分OCRの最大結合領域数
    /// </summary>
    /// <remarks>
    /// 結合後の領域数がこの値を超える場合、全画面OCRにフォールバック。
    /// デフォルト: 5
    /// 推奨範囲: 3-10
    /// </remarks>
    public int MaxMergedRegions { get; init; } = 5;

    #endregion

    #region [Issue #324] ROI優先監視モード設定

    /// <summary>
    /// [Issue #324] 高信頼度と判定するための最小検出回数
    /// </summary>
    /// <remarks>
    /// この回数以上検出された領域のみを高信頼度として扱います。
    /// デフォルト: 5回
    /// 推奨範囲: 3-20
    /// </remarks>
    public int MinDetectionCountForHighConfidence { get; init; } = 5;

    /// <summary>
    /// [Issue #324] 学習完了に必要な高信頼度領域の最小数
    /// </summary>
    /// <remarks>
    /// この数以上の高信頼度領域が存在する場合、学習完了とみなします。
    /// デフォルト: 1
    /// 推奨範囲: 1-5
    /// </remarks>
    public int MinHighConfidenceRegionsForComplete { get; init; } = 1;

    /// <summary>
    /// [Issue #324] 学習完了に必要な最小学習セッション数
    /// </summary>
    /// <remarks>
    /// この回数以上の学習セッションが完了した場合、学習完了とみなします。
    /// デフォルト: 3
    /// 推奨範囲: 2-10
    /// </remarks>
    public int MinLearningSessionsForComplete { get; init; } = 3;

    #endregion

    #region [Issue #354] 負の強化・自動除外設定

    /// <summary>
    /// [Issue #354] 連続missでスコアリセットする閾値
    /// </summary>
    /// <remarks>
    /// この回数連続でmissした場合、該当セルのスコアを0にリセット。
    /// デフォルト: 3回
    /// 推奨範囲: 2-5
    /// </remarks>
    public int ConsecutiveMissThresholdForReset { get; init; } = 3;

    /// <summary>
    /// [Issue #354] 自動除外ゾーン登録の閾値
    /// </summary>
    /// <remarks>
    /// この回数連続でmissした場合、該当領域を除外ゾーンに自動登録。
    /// デフォルト: 5回
    /// 推奨範囲: 3-10
    /// </remarks>
    public int ConsecutiveMissThresholdForExclusion { get; init; } = 5;

    /// <summary>
    /// [Issue #354] 負の強化を有効化
    /// </summary>
    /// <remarks>
    /// falseの場合、missは記録されるがスコアリセット・自動除外は行わない。
    /// デフォルト: true
    /// </remarks>
    public bool EnableNegativeReinforcement { get; init; } = true;

    /// <summary>
    /// [Issue #354] 自動除外ゾーンの有効化
    /// </summary>
    /// <remarks>
    /// falseの場合、連続missでも自動除外は行わない。
    /// デフォルト: true
    /// </remarks>
    public bool EnableAutoExclusionZone { get; init; } = true;

    #endregion

    #region [Issue #369] シードプロファイル設定

    /// <summary>
    /// [Issue #369] シードプロファイル機能を有効化
    /// </summary>
    /// <remarks>
    /// 新規ゲーム検出時に、王道レイアウトのシードプロファイルを適用します。
    /// これにより初回起動時から部分OCRが利用可能になり、OCR処理時間を短縮できます。
    /// デフォルト: true
    /// </remarks>
    public bool EnableSeedProfile { get; init; } = true;

    /// <summary>
    /// [Issue #369] シードプロファイルの初期検出回数
    /// </summary>
    /// <remarks>
    /// シード領域に設定する仮想的な検出回数。
    /// 高い値を設定するとシード領域が「信頼できる」と判定されやすくなります。
    /// デフォルト: 50回
    /// 推奨範囲: 20-100
    /// </remarks>
    public int SeedProfileInitialDetectionCount { get; init; } = 50;

    /// <summary>
    /// [Issue #369] シードプロファイルの初期信頼度スコア
    /// </summary>
    /// <remarks>
    /// シード領域に設定する初期信頼度スコア。
    /// デフォルト: 0.6（60%）
    /// 推奨範囲: 0.5-0.8
    /// </remarks>
    public float SeedProfileInitialConfidenceScore { get; init; } = 0.6f;

    #endregion

    /// <summary>
    /// 設定値の妥当性を検証
    /// </summary>
    public bool IsValid()
    {
        return HeatmapRows is > 0 and <= 64
            && HeatmapColumns is > 0 and <= 64
            && LearningRate is > 0.0f and <= 1.0f
            && DecayRate is >= 0.0f and <= 1.0f
            && DecayIntervalSeconds > 0
            && MinConfidenceForRegion is >= 0.0f and <= 1.0f
            && HighConfidenceThreshold is >= 0.0f and <= 1.0f
            && MinConfidenceForRegion <= HighConfidenceThreshold
            && MinRegionSize is > 0.0f and < 1.0f
            && MaxRegionsPerProfile > 0
            && ProfileMaxAgeDays > 0
            && AutoSaveIntervalSeconds > 0
            && HighHeatmapThresholdMultiplier > 0.0f
            && LowHeatmapThresholdMultiplier > 0.0f
            // [Issue #293] 部分OCR設定の検証
            && AdjacencyMargin >= 0
            && MinPartialOcrWidth > 0
            && MinPartialOcrHeight > 0
            && MaxPartialOcrCoverageRatio is > 0.0f and <= 1.0f
            && MaxMergedRegions > 0
            // [Issue #324] ROI優先監視モード設定の検証
            && MinDetectionCountForHighConfidence > 0
            && MinHighConfidenceRegionsForComplete > 0
            && MinLearningSessionsForComplete > 0
            // [Issue #354] 負の強化・自動除外設定の検証
            && ConsecutiveMissThresholdForReset > 0
            && ConsecutiveMissThresholdForExclusion > 0
            && ConsecutiveMissThresholdForReset <= ConsecutiveMissThresholdForExclusion
            // [Issue #369] シードプロファイル設定の検証
            && SeedProfileInitialDetectionCount > 0
            && SeedProfileInitialConfidenceScore is >= 0.0f and <= 1.0f;
    }

    /// <summary>
    /// デフォルト設定を作成
    /// </summary>
    public static RoiManagerSettings CreateDefault()
    {
        return new RoiManagerSettings();
    }

    /// <summary>
    /// 高感度設定を作成（学習が速い）
    /// </summary>
    public static RoiManagerSettings CreateHighSensitivity()
    {
        return new RoiManagerSettings
        {
            Enabled = true,
            AutoLearningEnabled = true,
            LearningRate = 0.2f,
            DecayRate = 0.005f,
            MinConfidenceForRegion = 0.2f,
            HighConfidenceThreshold = 0.6f,
            EnableDynamicThreshold = true,
            HighHeatmapThresholdMultiplier = 1.08f,
            LowHeatmapThresholdMultiplier = 0.92f
        };
    }

    /// <summary>
    /// 安定性重視設定を作成（学習が遅い、ノイズに強い）
    /// </summary>
    public static RoiManagerSettings CreateStable()
    {
        return new RoiManagerSettings
        {
            Enabled = true,
            AutoLearningEnabled = true,
            LearningRate = 0.05f,
            DecayRate = 0.02f,
            MinConfidenceForRegion = 0.4f,
            HighConfidenceThreshold = 0.8f,
            EnableDynamicThreshold = true,
            HighHeatmapThresholdMultiplier = 1.03f,
            LowHeatmapThresholdMultiplier = 0.97f
        };
    }
}
