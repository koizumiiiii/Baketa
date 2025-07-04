using Avalonia.Media;
using Baketa.UI.Controls;

namespace Baketa.UI.Tests.Mocks;

/// <summary>
/// OverlayTextBlockのテスト用Mock実装
/// UI要素の実際の作成を回避してテストのハングを防ぐ
/// </summary>
public sealed class MockOverlayTextBlock
{
    // プロパティのバッキングフィールド
    private string _text = string.Empty;
    private OverlayTheme _theme = OverlayTheme.Auto;
    private bool _toggleVisibilityEnabled = true;
    private double _lineHeight = 1.4;
    private TextWrapping _textWrapping = TextWrapping.Wrap;
    private double _paragraphSpacing = 8.0;

    /// <summary>
    /// テキスト
    /// </summary>
    public string Text
    {
        get => _text;
        set => _text = value ?? string.Empty;
    }

    /// <summary>
    /// テーマ
    /// </summary>
    public OverlayTheme Theme
    {
        get => _theme;
        set => _theme = value;
    }

    /// <summary>
    /// 表示切り替え有効フラグ
    /// </summary>
    public bool ToggleVisibilityEnabled
    {
        get => _toggleVisibilityEnabled;
        set => _toggleVisibilityEnabled = value;
    }

    /// <summary>
    /// 行間
    /// </summary>
    public double LineHeight
    {
        get => _lineHeight;
        set => _lineHeight = value;
    }

    /// <summary>
    /// テキスト折り返し
    /// </summary>
    public TextWrapping TextWrapping
    {
        get => _textWrapping;
        set => _textWrapping = value;
    }

    /// <summary>
    /// 段落間スペーシング
    /// </summary>
    public double ParagraphSpacing
    {
        get => _paragraphSpacing;
        set => _paragraphSpacing = value;
    }

    /// <summary>
    /// 表示切り替えメソッド（Mock実装）
    /// </summary>
    public void ToggleVisibility()
    {
        // Mock実装：実際の処理は行わない
        // テスト環境では単純に完了とみなす
    }
}