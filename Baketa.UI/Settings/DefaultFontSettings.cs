using Avalonia.Media;

namespace Baketa.UI.Settings;

/// <summary>
/// アプリケーション統一フォント設定
/// </summary>
public static class DefaultFontSettings
{
    /// <summary>フォントファミリー（固定）</summary>
    public static string Family => "Yu Gothic UI";
    
    /// <summary>フォントサイズ（固定）</summary>
    public static double Size => 16.0;
    
    /// <summary>フォントウェイト（固定）</summary>
    public static FontWeight Weight => FontWeight.Normal;
    
    /// <summary>行間（固定）</summary>
    public static double LineHeight => 1.4;
}
