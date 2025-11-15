namespace Baketa.Core.Abstractions.UI.Overlays;

/// <summary>
/// オーバーレイの表示内容を表すモデルクラス
/// 翻訳テキスト、スタイル情報を保持
/// </summary>
public sealed class OverlayContent
{
    /// <summary>
    /// 表示するテキスト（翻訳結果）
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// フォントファミリー名
    /// </summary>
    public string? FontFamily { get; init; }

    /// <summary>
    /// フォントサイズ（ピクセル）
    /// </summary>
    public double? FontSize { get; init; }

    /// <summary>
    /// テキストの前景色 (ARGB形式の文字列、例: "#FFFFFFFF")
    /// </summary>
    public string? ForegroundColor { get; init; }

    /// <summary>
    /// 背景色 (ARGB形式の文字列、例: "#80000000")
    /// </summary>
    public string? BackgroundColor { get; init; }

    /// <summary>
    /// テキスト配置（Left, Center, Right）
    /// </summary>
    public string? TextAlignment { get; init; }

    /// <summary>
    /// 余白（ピクセル）
    /// </summary>
    public double? Padding { get; init; }

    /// <summary>
    /// 元の言語テキスト（オプション、デバッグ用）
    /// </summary>
    public string? OriginalText { get; init; }
}
