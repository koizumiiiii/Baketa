using System.Drawing;

namespace Baketa.Infrastructure.OCR.PostProcessing;

/// <summary>
/// チャンク近接度分析のコンテキスト情報
/// 文字サイズから自動的に適切な閾値を計算
/// </summary>
public sealed class ProximityContext
{
    /// <summary>
    /// 平均文字高さ（ピクセル）
    /// </summary>
    public double AverageCharHeight { get; init; }

    /// <summary>
    /// 平均文字幅（ピクセル）
    /// 一般的な文字の縦横比（0.6）から推定
    /// </summary>
    public double AverageCharWidth { get; init; }

    /// <summary>
    /// 垂直方向の閾値（ピクセル）
    /// 同じ段落と判定する最大行間距離
    /// </summary>
    public double VerticalThreshold { get; init; }

    /// <summary>
    /// 水平方向の閾値（ピクセル）
    /// 同じ行内で連続したテキストと判定する最大文字間距離
    /// </summary>
    public double HorizontalThreshold { get; init; }

    /// <summary>
    /// 最小文字高さ（ピクセル）
    /// ノイズ除去用
    /// </summary>
    public double MinCharHeight { get; init; }

    /// <summary>
    /// 最大文字高さ（ピクセル）
    /// 異常値除去用
    /// </summary>
    public double MaxCharHeight { get; init; }

    /// <summary>
    /// デフォルトコンテキスト（フォールバック用）
    /// </summary>
    public static ProximityContext Default => new()
    {
        AverageCharHeight = 20,
        AverageCharWidth = 12,
        VerticalThreshold = 24,    // 20 * 1.2
        HorizontalThreshold = 36,  // 12 * 3
        MinCharHeight = 8,
        MaxCharHeight = 100
    };

    /// <summary>
    /// 2つの矩形が同一行にあるかを判定
    /// </summary>
    public bool IsSameLine(Rectangle a, Rectangle b)
    {
        var centerA = a.Y + a.Height / 2.0;
        var centerB = b.Y + b.Height / 2.0;

        // Y座標の中心が文字高さの半分以内なら同一行
        return Math.Abs(centerA - centerB) < AverageCharHeight * 0.5;
    }

    /// <summary>
    /// 垂直方向のギャップを計算
    /// </summary>
    public double GetVerticalGap(Rectangle a, Rectangle b)
    {
        if (a.Bottom <= b.Top)
            return b.Top - a.Bottom;
        if (b.Bottom <= a.Top)
            return a.Top - b.Bottom;
        return 0; // 垂直方向で重なっている
    }

    /// <summary>
    /// 水平方向のギャップを計算
    /// </summary>
    public double GetHorizontalGap(Rectangle a, Rectangle b)
    {
        if (a.Right <= b.Left)
            return b.Left - a.Right;
        if (b.Right <= a.Left)
            return a.Left - b.Right;
        return 0; // 水平方向で重なっている
    }
}
