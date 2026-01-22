using System;

namespace Baketa.Core.Abstractions.Roi;

/// <summary>
/// ROIベースのGatekeeper（翻訳Gate）のインターフェース
/// </summary>
/// <remarks>
/// テキスト変化を検知した後、Cloud AI翻訳を実行するかどうかを判定します。
/// 相対閾値を使用して、短文・長文で異なる判定基準を適用し、
/// 不要なAPI呼び出しを削減してトークンを節約します。
/// </remarks>
public interface IRoiGatekeeper
{
    /// <summary>
    /// Gatekeeperが有効かどうか
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// 翻訳を実行すべきかどうかを判定
    /// </summary>
    /// <param name="previousText">前回のテキスト</param>
    /// <param name="currentText">今回のテキスト</param>
    /// <param name="region">テキストのROI領域情報（オプション）</param>
    /// <returns>判定結果</returns>
    GatekeeperDecision ShouldTranslate(
        string? previousText,
        string currentText,
        GatekeeperRegionInfo? region = null);

    /// <summary>
    /// 翻訳結果を報告（学習用）
    /// </summary>
    /// <param name="decision">判定結果</param>
    /// <param name="wasSuccessful">翻訳が成功したか</param>
    /// <param name="tokensUsed">使用したトークン数</param>
    void ReportTranslationResult(GatekeeperDecision decision, bool wasSuccessful, int tokensUsed);

    /// <summary>
    /// 統計をリセット
    /// </summary>
    void ResetStatistics();

    /// <summary>
    /// 現在の統計を取得
    /// </summary>
    GatekeeperStatistics GetStatistics();
}

/// <summary>
/// Gatekeeperの判定結果
/// </summary>
public sealed record GatekeeperDecision
{
    /// <summary>
    /// 翻訳を実行すべきかどうか
    /// </summary>
    public bool ShouldTranslate { get; init; }

    /// <summary>
    /// 判定理由
    /// </summary>
    public GatekeeperReason Reason { get; init; }

    /// <summary>
    /// 変化率（0.0-1.0）
    /// </summary>
    /// <remarks>
    /// Levenshtein距離ベースの文字列変化率。
    /// 短文では高い変化率が要求される。
    /// </remarks>
    public float ChangeRatio { get; init; }

    /// <summary>
    /// 適用された閾値
    /// </summary>
    public float AppliedThreshold { get; init; }

    /// <summary>
    /// 前回のテキスト長
    /// </summary>
    public int PreviousTextLength { get; init; }

    /// <summary>
    /// 今回のテキスト長
    /// </summary>
    public int CurrentTextLength { get; init; }

    /// <summary>
    /// 判定に要した時間
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// 判定時刻
    /// </summary>
    public DateTime DecisionAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 判定ID（デバッグ用）
    /// </summary>
    public string DecisionId { get; init; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// 翻訳を実行すべき判定を作成
    /// </summary>
    public static GatekeeperDecision Allow(GatekeeperReason reason, float changeRatio, float appliedThreshold)
    {
        return new GatekeeperDecision
        {
            ShouldTranslate = true,
            Reason = reason,
            ChangeRatio = changeRatio,
            AppliedThreshold = appliedThreshold
        };
    }

    /// <summary>
    /// 翻訳をスキップすべき判定を作成
    /// </summary>
    public static GatekeeperDecision Deny(GatekeeperReason reason, float changeRatio, float appliedThreshold)
    {
        return new GatekeeperDecision
        {
            ShouldTranslate = false,
            Reason = reason,
            ChangeRatio = changeRatio,
            AppliedThreshold = appliedThreshold
        };
    }
}

/// <summary>
/// Gatekeeper判定理由
/// </summary>
public enum GatekeeperReason
{
    /// <summary>
    /// 初回テキスト（前回テキストなし）
    /// </summary>
    FirstText,

    /// <summary>
    /// 十分な変化がある
    /// </summary>
    SufficientChange,

    /// <summary>
    /// 変化が不十分
    /// </summary>
    InsufficientChange,

    /// <summary>
    /// 除外ゾーン内
    /// </summary>
    InExclusionZone,

    /// <summary>
    /// テキストが空
    /// </summary>
    EmptyText,

    /// <summary>
    /// 同一テキスト
    /// </summary>
    IdenticalText,

    /// <summary>
    /// Gatekeeperが無効
    /// </summary>
    GatekeeperDisabled,

    /// <summary>
    /// 強制許可（設定による）
    /// </summary>
    ForcedAllow,

    /// <summary>
    /// 長文の変化
    /// </summary>
    LongTextChange,

    /// <summary>
    /// 短文の変化
    /// </summary>
    ShortTextChange,

    /// <summary>
    /// 大幅な長さ変化
    /// </summary>
    SignificantLengthChange
}

/// <summary>
/// Gatekeeperに渡すROI領域情報
/// </summary>
public sealed record GatekeeperRegionInfo
{
    /// <summary>
    /// 正規化X座標（0.0-1.0）
    /// </summary>
    public float NormalizedX { get; init; }

    /// <summary>
    /// 正規化Y座標（0.0-1.0）
    /// </summary>
    public float NormalizedY { get; init; }

    /// <summary>
    /// 正規化幅（0.0-1.0）
    /// </summary>
    public float NormalizedWidth { get; init; }

    /// <summary>
    /// 正規化高さ（0.0-1.0）
    /// </summary>
    public float NormalizedHeight { get; init; }

    /// <summary>
    /// 領域のヒートマップ値（オプション）
    /// </summary>
    public float? HeatmapValue { get; init; }

    /// <summary>
    /// 領域の信頼度スコア（オプション）
    /// </summary>
    public float? ConfidenceScore { get; init; }

    /// <summary>
    /// 除外ゾーン内かどうか
    /// </summary>
    public bool IsInExclusionZone { get; init; }
}

/// <summary>
/// Gatekeeper統計
/// </summary>
public sealed record GatekeeperStatistics
{
    /// <summary>
    /// 総判定回数
    /// </summary>
    public long TotalDecisions { get; init; }

    /// <summary>
    /// 許可した回数
    /// </summary>
    public long AllowedCount { get; init; }

    /// <summary>
    /// 拒否した回数
    /// </summary>
    public long DeniedCount { get; init; }

    /// <summary>
    /// 許可率
    /// </summary>
    public float AllowRate => TotalDecisions > 0 ? (float)AllowedCount / TotalDecisions : 0.0f;

    /// <summary>
    /// 節約されたトークン数（推定）
    /// </summary>
    public long EstimatedTokensSaved { get; init; }

    /// <summary>
    /// 実際に使用されたトークン数
    /// </summary>
    public long ActualTokensUsed { get; init; }

    /// <summary>
    /// 平均変化率
    /// </summary>
    public float AverageChangeRatio { get; init; }

    /// <summary>
    /// 最後の判定時刻
    /// </summary>
    public DateTime? LastDecisionAt { get; init; }

    /// <summary>
    /// 統計開始時刻
    /// </summary>
    public DateTime StatisticsStartedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 理由別の判定回数
    /// </summary>
    public System.Collections.Generic.IReadOnlyDictionary<GatekeeperReason, long> DecisionsByReason { get; init; }
        = new System.Collections.Generic.Dictionary<GatekeeperReason, long>();
}
