using System.Drawing;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Events.Capture;

/// <summary>
/// ROI画像キャプチャ完了イベント
/// 複数ROI画像処理システム用 - Phase 2.5実装
/// </summary>
/// <remarks>
/// ROIBasedCaptureStrategyが複数のテキスト領域を検出した場合、
/// 各領域の高解像度部分画像とその絶対座標をペアにして発行される。
/// このイベントにより、各ROI画像を個別にOCR処理し、
/// 正しい絶対座標でオーバーレイ表示することが可能になる。
/// </remarks>
public class ROIImageCapturedEvent : EventBase
{
    /// <summary>
    /// ROI領域の高解像度部分画像
    /// </summary>
    public required IImage Image { get; init; }

    /// <summary>
    /// 元画像内での絶対座標（スクリーン座標またはウィンドウ座標）
    /// </summary>
    /// <remarks>
    /// この座標を使用して、OCR結果の相対座標を絶対座標に変換する:
    /// 絶対座標 = OCR相対座標 + AbsoluteRegion.Location
    /// </remarks>
    public required Rectangle AbsoluteRegion { get; init; }

    /// <summary>
    /// ROIインデックス（0始まり）
    /// </summary>
    public required int ROIIndex { get; init; }

    /// <summary>
    /// 総ROI数
    /// </summary>
    public required int TotalROIs { get; init; }

    /// <summary>
    /// イベント発行時刻
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <inheritdoc />
    public override string Name => "ROIImageCaptured";

    /// <inheritdoc />
    public override string Category => "Capture";
}
