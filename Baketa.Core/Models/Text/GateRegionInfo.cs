namespace Baketa.Core.Models.Text;

/// <summary>
/// [Issue #293] テキスト変化検知用の領域情報
/// </summary>
/// <remarks>
/// Geminiフィードバック反映: 呼び出し側でヒートマップ値を事前設定する方式。
/// IRoiManagerへの依存をTextChangeDetectionServiceに持ち込まず、
/// テスト時にモック不要とするための設計。
/// </remarks>
public sealed record GateRegionInfo
{
    /// <summary>
    /// 正規化X座標 (0.0-1.0)
    /// </summary>
    public float NormalizedX { get; init; }

    /// <summary>
    /// 正規化Y座標 (0.0-1.0)
    /// </summary>
    public float NormalizedY { get; init; }

    /// <summary>
    /// 正規化幅 (0.0-1.0)
    /// </summary>
    public float NormalizedWidth { get; init; }

    /// <summary>
    /// 正規化高さ (0.0-1.0)
    /// </summary>
    public float NormalizedHeight { get; init; }

    /// <summary>
    /// 事前計算されたヒートマップ値 (0.0-1.0)
    /// </summary>
    /// <remarks>
    /// 呼び出し側で IRoiManager から取得して設定。
    /// nullの場合はヒートマップ連携なしで動作。
    /// </remarks>
    public float? HeatmapValue { get; init; }

    /// <summary>
    /// OCR信頼度スコア (0.0-1.0)
    /// </summary>
    public float? ConfidenceScore { get; init; }

    /// <summary>
    /// 除外ゾーン内かどうか
    /// </summary>
    /// <remarks>
    /// 呼び出し側で IRoiManager.IsInExclusionZone() の結果を設定。
    /// </remarks>
    public bool IsInExclusionZone { get; init; }

    /// <summary>
    /// 領域の中心X座標を計算
    /// </summary>
    public float CenterX => NormalizedX + NormalizedWidth / 2f;

    /// <summary>
    /// 領域の中心Y座標を計算
    /// </summary>
    public float CenterY => NormalizedY + NormalizedHeight / 2f;

    /// <summary>
    /// 座標のみで作成（ヒートマップなし）
    /// </summary>
    public static GateRegionInfo FromCoordinates(
        float normalizedX,
        float normalizedY,
        float normalizedWidth = 0f,
        float normalizedHeight = 0f)
    {
        return new GateRegionInfo
        {
            NormalizedX = normalizedX,
            NormalizedY = normalizedY,
            NormalizedWidth = normalizedWidth,
            NormalizedHeight = normalizedHeight
        };
    }

    /// <summary>
    /// ヒートマップ値付きで作成
    /// </summary>
    public static GateRegionInfo WithHeatmap(
        float normalizedX,
        float normalizedY,
        float normalizedWidth,
        float normalizedHeight,
        float heatmapValue)
    {
        return new GateRegionInfo
        {
            NormalizedX = normalizedX,
            NormalizedY = normalizedY,
            NormalizedWidth = normalizedWidth,
            NormalizedHeight = normalizedHeight,
            HeatmapValue = heatmapValue
        };
    }
}
