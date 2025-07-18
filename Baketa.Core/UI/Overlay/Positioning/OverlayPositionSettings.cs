using Baketa.Core.UI.Geometry;

namespace Baketa.Core.UI.Overlay.Positioning;

/// <summary>
/// オーバーレイ位置設定
/// </summary>
public sealed record OverlayPositionSettings
{
    /// <summary>
    /// 翻訳用の標準設定
    /// </summary>
    public static OverlayPositionSettings ForTranslation => new()
    {
        PositionMode = OverlayPositionMode.OcrRegionBased,
        SizeMode = OverlaySizeMode.ContentBased,
        MaxSize = new(1200, 800),
        MinSize = new(200, 60),
        PositionOffset = new(0, 5)
    };

    /// <summary>
    /// デバッグ用の設定
    /// </summary>
    public static OverlayPositionSettings ForDebug => new()
    {
        PositionMode = OverlayPositionMode.Fixed,
        SizeMode = OverlaySizeMode.Fixed,
        FixedPosition = new(100, 100),
        FixedSize = new(600, 100),
        MaxSize = new(1200, 800),
        MinSize = new(200, 60)
    };

    /// <summary>
    /// 配置モード
    /// </summary>
    public OverlayPositionMode PositionMode { get; init; } = OverlayPositionMode.OcrRegionBased;

    /// <summary>
    /// サイズモード
    /// </summary>
    public OverlaySizeMode SizeMode { get; init; } = OverlaySizeMode.ContentBased;

    /// <summary>
    /// 固定位置
    /// </summary>
    public CorePoint FixedPosition { get; init; } = new(100, 100);

    /// <summary>
    /// 固定サイズ
    /// </summary>
    public CoreSize FixedSize { get; init; } = new(600, 100);

    /// <summary>
    /// 位置オフセット
    /// </summary>
    public CoreVector PositionOffset { get; init; } = CoreVector.Zero;

    /// <summary>
    /// 最大サイズ
    /// </summary>
    public CoreSize MaxSize { get; init; } = new(1200, 800);

    /// <summary>
    /// 最小サイズ
    /// </summary>
    public CoreSize MinSize { get; init; } = new(200, 60);
}
