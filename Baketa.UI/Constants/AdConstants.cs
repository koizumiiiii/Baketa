namespace Baketa.UI.Constants;

/// <summary>
/// 広告ウィンドウに関する定数を一元管理するクラス
/// </summary>
public static class AdConstants
{
    /// <summary>
    /// 広告ウィンドウの論理幅（DIPピクセル）
    /// IAB標準 "Medium Rectangle" 300x250
    /// </summary>
    public const double Width = 300;

    /// <summary>
    /// 広告ウィンドウの論理高さ（DIPピクセル）
    /// IAB標準 "Medium Rectangle" 300x250
    /// </summary>
    public const double Height = 250;

    /// <summary>
    /// 画面端からのマージン（ピクセル）
    /// </summary>
    public const int ScreenMargin = 10;
}
