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
    public OverlayTheme Theme { get; set; } = OverlayTheme.Auto;

    /// <summary>
    /// 表示切り替え有効フラグ
    /// </summary>
    public bool ToggleVisibilityEnabled { get; set; } = true;

    /// <summary>
    /// 行間
    /// </summary>
    public double LineHeight { get; set; } = 1.4;

    /// <summary>
    /// テキスト折り返し
    /// </summary>
    public TextWrapping TextWrapping { get; set; } = TextWrapping.Wrap;

    /// <summary>
    /// 段落間スペーシング
    /// </summary>
    public double ParagraphSpacing { get; set; } = 8.0;

    /// <summary>
    /// 表示切り替えメソッド（Mock実装）
    /// </summary>
    public void ToggleVisibility()
    {
        // Mock実装：実際の処理は行わない
        // テスト環境では単純に完了とみなす
    }
}
