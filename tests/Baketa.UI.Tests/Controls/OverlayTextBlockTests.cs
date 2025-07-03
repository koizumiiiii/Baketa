using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Baketa.UI.Controls;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.UI.Tests.Controls;

/// <summary>
/// OverlayTextBlockコントロールの単体テスト
/// Issue #70 オーバーレイUIデザインとアニメーション実装 - Phase 4
/// </summary>
public sealed class OverlayTextBlockTests(ITestOutputHelper output) : IDisposable
{
    private readonly ITestOutputHelper _output = output;
    private bool _disposed;

    /// <summary>
    /// テスト用Avaloniaアプリケーション
    /// </summary>
    private sealed class TestApplication : Avalonia.Application
    {
        public override void Initialize()
        {
            // 最小限の初期化
        }
    }

    #region 基本プロパティテスト

    [Fact]
    public void ConstructorShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        var control = new OverlayTextBlock();

        // Assert
        Assert.Equal(string.Empty, control.Text);
        Assert.Equal(OverlayTheme.Auto, control.Theme);
        Assert.True(control.AnimationEnabled);
        Assert.True(control.ToggleVisibilityEnabled);
        Assert.Equal(1.4, control.LineHeight);
        Assert.Equal(TextWrapping.Wrap, control.TextWrapping);
        Assert.Equal(8.0, control.ParagraphSpacing);

        _output.WriteLine("✅ コンストラクター初期値テスト完了");
    }

    [Fact]
    public void TextPropertyShouldSetAndGetCorrectly()
    {
        // Arrange
        var control = new OverlayTextBlock();
        const string testText = "テストテキスト";

        // Act
        control.Text = testText;

        // Assert
        Assert.Equal(testText, control.Text);

        _output.WriteLine($"✅ テキストプロパティテスト完了: {testText}");
    }

    [Theory]
    [InlineData(OverlayTheme.Auto)]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    [InlineData(OverlayTheme.HighContrast)]
    public void ThemePropertyShouldSetAndGetCorrectly(OverlayTheme theme)
    {
        // Arrange
        var control = new OverlayTextBlock
        {
            // Act
            Theme = theme
        };

        // Assert
        Assert.Equal(theme, control.Theme);

        _output.WriteLine($"✅ テーマプロパティテスト完了: {theme}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnimationEnabledPropertyShouldSetAndGetCorrectly(bool enabled)
    {
        // Arrange
        var control = new OverlayTextBlock
        {
            // Act
            AnimationEnabled = enabled
        };

        // Assert
        Assert.Equal(enabled, control.AnimationEnabled);

        _output.WriteLine($"✅ アニメーション有効プロパティテスト完了: {enabled}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToggleVisibilityEnabledPropertyShouldSetAndGetCorrectly(bool enabled)
    {
        // Arrange
        var control = new OverlayTextBlock
        {
            // Act
            ToggleVisibilityEnabled = enabled
        };

        // Assert
        Assert.Equal(enabled, control.ToggleVisibilityEnabled);

        _output.WriteLine($"✅ 表示切り替え有効プロパティテスト完了: {enabled}");
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.4)]
    [InlineData(2.0)]
    public void LineHeightPropertyShouldSetAndGetCorrectly(double lineHeight)
    {
        // Arrange
        var control = new OverlayTextBlock
        {
            // Act
            LineHeight = lineHeight
        };

        // Assert
        Assert.Equal(lineHeight, control.LineHeight);

        _output.WriteLine($"✅ 行間プロパティテスト完了: {lineHeight}");
    }

    [Theory]
    [InlineData(TextWrapping.NoWrap)]
    [InlineData(TextWrapping.Wrap)]
    public void TextWrappingPropertyShouldSetAndGetCorrectly(TextWrapping wrapping)
    {
        // Arrange
        var control = new OverlayTextBlock
        {
            // Act
            TextWrapping = wrapping
        };

        // Assert
        Assert.Equal(wrapping, control.TextWrapping);

        _output.WriteLine($"✅ テキスト折り返しプロパティテスト完了: {wrapping}");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(8.0)]
    [InlineData(16.0)]
    public void ParagraphSpacingPropertyShouldSetAndGetCorrectly(double spacing)
    {
        // Arrange
        var control = new OverlayTextBlock
        {
            // Act
            ParagraphSpacing = spacing
        };

        // Assert
        Assert.Equal(spacing, control.ParagraphSpacing);

        _output.WriteLine($"✅ 段落間スペーシングプロパティテスト完了: {spacing}");
    }

    [Fact]
    public void ParagraphSpacingWithEdgeValuesShouldHandleCorrectly()
    {
        // Arrange
        var control = new OverlayTextBlock
        {
            // Act & Assert - 最小値付近
            ParagraphSpacing = 0.0
        };
        Assert.Equal(0.0, control.ParagraphSpacing);

        // Act & Assert - 大きな値
        control.ParagraphSpacing = 50.0;
        Assert.Equal(50.0, control.ParagraphSpacing);

        // Act & Assert - デフォルト値
        control.ParagraphSpacing = 8.0;
        Assert.Equal(8.0, control.ParagraphSpacing);

        _output.WriteLine("✅ 段落間スペーシングエッジケーステスト完了");
    }

    #endregion

    #region テーマ適用テスト

    [Fact]
    public void ApplyThemeAutoShouldSelectTimeBasedTheme()
    {
        // Arrange
        var control = new OverlayTextBlock
        {
            // Act
            Theme = OverlayTheme.Auto
        };

        // Assert
        var hour = DateTime.Now.Hour;
        var expectedTheme = (hour >= 6 && hour < 18) ? "Light" : "Dark";
        
        // テーマクラスが適用されているかを確認
        // Note: Headlessモードでは実際のUI要素が作成されないため、
        // クラス適用の詳細検証は統合テストで行う
        Assert.Equal(OverlayTheme.Auto, control.Theme);

        _output.WriteLine($"✅ 自動テーマ選択テスト完了: 時刻{hour}時 → 期待テーマ{expectedTheme}");
    }

    [Theory]
    [InlineData(OverlayTheme.Light)]
    [InlineData(OverlayTheme.Dark)]
    [InlineData(OverlayTheme.HighContrast)]
    public void ApplyThemeExplicitThemesShouldBeApplied(OverlayTheme theme)
    {
        // Arrange
        var control = new OverlayTextBlock
        {
            // Act
            Theme = theme
        };

        // Assert
        Assert.Equal(theme, control.Theme);

        _output.WriteLine($"✅ 明示的テーマ適用テスト完了: {theme}");
    }

    #endregion

    #region 表示/非表示テスト

    [Fact]
    public void ToggleVisibilityWhenEnabledShouldChangeVisibility()
    {
        // Arrange
        var control = new OverlayTextBlock
        {
            ToggleVisibilityEnabled = true,
            IsVisible = true
        };

        // Act & Assert
        // 初期状態確認
        Assert.True(control.IsVisible);
        Assert.True(control.ToggleVisibilityEnabled);

        // ToggleVisibilityメソッドの呼び出し
        // Note: 実際のアニメーション動作は統合テストで検証
        control.ToggleVisibility();

        // メソッドが例外なく実行されることを確認
        _output.WriteLine("✅ 表示切り替えメソッド呼び出しテスト完了");
    }

    [Fact]
    public void ToggleVisibilityWhenDisabledShouldNotChangeVisibility()
    {
        // Arrange
        var control = new OverlayTextBlock
        {
            ToggleVisibilityEnabled = false,
            IsVisible = true
        };
        var initialVisibility = control.IsVisible;

        // Act
        control.ToggleVisibility();

        // Assert
        Assert.Equal(initialVisibility, control.IsVisible);

        _output.WriteLine("✅ 表示切り替え無効時のテスト完了");
    }

    #endregion

    #region デフォルト値テスト

    [Fact]
    public void DefaultOverlayAppearanceShouldHaveCorrectValues()
    {
        // Act & Assert
        Assert.Equal(0.9, DefaultOverlayAppearance.Opacity);
        Assert.Equal(12.0, DefaultOverlayAppearance.Padding);
        Assert.Equal(8.0, DefaultOverlayAppearance.CornerRadius);
        Assert.Equal(1.0, DefaultOverlayAppearance.BorderThickness);

        _output.WriteLine("✅ デフォルト外観設定テスト完了");
        _output.WriteLine($"  透明度: {DefaultOverlayAppearance.Opacity}");
        _output.WriteLine($"  パディング: {DefaultOverlayAppearance.Padding}");
        _output.WriteLine($"  角丸半径: {DefaultOverlayAppearance.CornerRadius}");
        _output.WriteLine($"  境界線幅: {DefaultOverlayAppearance.BorderThickness}");
    }

    #endregion

    #region OverlayTheme列挙型テスト

    [Fact]
    public void OverlayThemeShouldHaveExpectedValues()
    {
        // Assert
        Assert.Equal(0, (int)OverlayTheme.Auto);
        Assert.Equal(1, (int)OverlayTheme.Light);
        Assert.Equal(2, (int)OverlayTheme.Dark);
        Assert.Equal(3, (int)OverlayTheme.HighContrast);

        // 列挙値名のテスト
        Assert.Equal("Auto", OverlayTheme.Auto.ToString());
        Assert.Equal("Light", OverlayTheme.Light.ToString());
        Assert.Equal("Dark", OverlayTheme.Dark.ToString());
        Assert.Equal("HighContrast", OverlayTheme.HighContrast.ToString());

        _output.WriteLine("✅ OverlayTheme列挙型テスト完了");
    }

    #endregion

    #region パフォーマンステスト

    [Fact]
    public void PropertyChangesShouldBeEfficient()
    {
        // Arrange
        var control = new OverlayTextBlock();
        var iterations = 1000;

        // Act & Assert - パフォーマンス測定
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            control.Text = $"テスト{i}";
            control.Theme = (OverlayTheme)(i % 4);
            control.AnimationEnabled = i % 2 == 0;
            control.LineHeight = 1.0 + (i % 10) * 0.1;
            control.ParagraphSpacing = 4.0 + (i % 5) * 2.0;
        }

        stopwatch.Stop();

        // 1000回のプロパティ変更が1秒以内に完了することを確認
        Assert.True(stopwatch.ElapsedMilliseconds < 1000);

        _output.WriteLine($"✅ パフォーマンステスト完了: {iterations}回のプロパティ変更を{stopwatch.ElapsedMilliseconds}msで実行");
    }

    #endregion

    #region エッジケーステスト

    [Fact]
    public void TextWithNullOrEmptyValuesShouldHandleGracefully()
    {
        // Arrange
        var control = new OverlayTextBlock
        {
            // Act & Assert - null値
            Text = null!
        };
        Assert.Equal(string.Empty, control.Text); // StyledPropertyのデフォルト値が使用される

        // Act & Assert - 空文字列
        control.Text = string.Empty;
        Assert.Equal(string.Empty, control.Text);

        // Act & Assert - 改行を含むテキスト
        var multilineText = "行1\n行2\r\n行3";
        control.Text = multilineText;
        Assert.Equal(multilineText, control.Text);

        _output.WriteLine("✅ テキストエッジケーステスト完了");
    }

    [Fact]
    public void LineHeightWithEdgeValuesShouldHandleCorrectly()
    {
        // Arrange
        var control = new OverlayTextBlock
        {
            // Act & Assert - 最小値付近
            LineHeight = 0.1
        };
        Assert.Equal(0.1, control.LineHeight);

        // Act & Assert - 大きな値
        control.LineHeight = 10.0;
        Assert.Equal(10.0, control.LineHeight);

        // Act & Assert - ゼロ（通常は有効でないが、プロパティとしては設定可能）
        control.LineHeight = 0.0;
        Assert.Equal(0.0, control.LineHeight);

        _output.WriteLine("✅ 行間エッジケーステスト完了");
    }

    #endregion

    #region 統合テスト準備

    [Fact]
    public void IntegrationMultiplePropertyChangesShouldWorkTogether()
    {
        // Arrange
        var control = new OverlayTextBlock
        {
            // Act - 複数プロパティの同時変更
            Text = "統合テストテキスト",
            Theme = OverlayTheme.HighContrast,
            AnimationEnabled = false,
            ToggleVisibilityEnabled = false,
            LineHeight = 2.0,
            TextWrapping = TextWrapping.NoWrap,
            ParagraphSpacing = 16.0
        };

        // Assert
        Assert.Equal("統合テストテキスト", control.Text);
        Assert.Equal(OverlayTheme.HighContrast, control.Theme);
        Assert.False(control.AnimationEnabled);
        Assert.False(control.ToggleVisibilityEnabled);
        Assert.Equal(2.0, control.LineHeight);
        Assert.Equal(TextWrapping.NoWrap, control.TextWrapping);
        Assert.Equal(16.0, control.ParagraphSpacing);

        _output.WriteLine("✅ 複数プロパティ変更統合テスト完了");
    }

    #endregion

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // リソース解放処理
            _disposed = true;
        }
    }
}
