namespace Baketa.Core.Abstractions.UI.Overlays;

/// <summary>
/// オーバーレイの表示位置とサイズを表すモデルクラス
/// 画面上の座標とウィンドウサイズを保持
/// </summary>
public sealed class OverlayPosition
{
    /// <summary>
    /// X座標（ピクセル、画面左上が原点）
    /// </summary>
    public required int X { get; init; }

    /// <summary>
    /// Y座標（ピクセル、画面左上が原点）
    /// </summary>
    public required int Y { get; init; }

    /// <summary>
    /// 幅（ピクセル）
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// 高さ（ピクセル）
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// モニターID（マルチモニター対応、オプション）
    /// </summary>
    public string? MonitorId { get; init; }

    /// <summary>
    /// Z-Order（ウィンドウの重なり順序、オプション）
    /// </summary>
    public int? ZOrder { get; init; }

    /// <summary>
    /// 画面座標が絶対座標か相対座標か（オプション）
    /// </summary>
    public bool IsAbsolutePosition { get; init; } = true;
}
