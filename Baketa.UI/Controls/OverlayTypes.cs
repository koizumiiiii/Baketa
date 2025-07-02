namespace Baketa.UI.Controls;

/// <summary>
/// オーバーレイテーマプリセット
/// </summary>
public enum OverlayTheme
{
    /// <summary>自動選択</summary>
    Auto,
    /// <summary>ライトテーマ</summary>
    Light,
    /// <summary>ダークテーマ</summary>
    Dark,
    /// <summary>高コントラストテーマ</summary>
    HighContrast
}

/// <summary>
/// オーバーレイ外観設定の定数
/// </summary>
public static class DefaultOverlayAppearance
{
    /// <summary>デフォルト不透明度</summary>
    public const double Opacity = 0.9;
    /// <summary>デフォルトパディング</summary>
    public const double Padding = 12.0;
    /// <summary>デフォルト角丸半径</summary>
    public const double CornerRadius = 8.0;
    /// <summary>デフォルト枠線幅</summary>
    public const double BorderThickness = 1.0;
}
