using Avalonia.Media;
using Baketa.UI.Settings;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.UI.Tests.Settings;

/// <summary>
/// DefaultFontSettingsの単体テスト
/// Issue #70 オーバーレイUIデザインとアニメーション実装 - Phase 4
/// </summary>
public sealed class DefaultFontSettingsTests
{
    private readonly ITestOutputHelper _output;

    public DefaultFontSettingsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region 基本値テスト

    [Fact]
    public void FamilyShouldReturnYuGothicUI()
    {
        // Act
        var family = DefaultFontSettings.Family;

        // Assert
        Assert.Equal("Yu Gothic UI", family);

        _output.WriteLine($"✅ フォントファミリーテスト完了: {family}");
    }

    [Fact]
    public void SizeShouldReturn16()
    {
        // Act
        var size = DefaultFontSettings.Size;

        // Assert
        Assert.Equal(16.0, size);

        _output.WriteLine($"✅ フォントサイズテスト完了: {size}");
    }

    [Fact]
    public void WeightShouldReturnNormal()
    {
        // Act
        var weight = DefaultFontSettings.Weight;

        // Assert
        Assert.Equal(FontWeight.Normal, weight);

        _output.WriteLine($"✅ フォントウェイトテスト完了: {weight}");
    }

    [Fact]
    public void LineHeightShouldReturn1Point4()
    {
        // Act
        var lineHeight = DefaultFontSettings.LineHeight;

        // Assert
        Assert.Equal(1.4, lineHeight);

        _output.WriteLine($"✅ 行間テスト完了: {lineHeight}");
    }

    #endregion

    #region 一貫性テスト

    [Fact]
    public void AllPropertiesShouldReturnConsistentValues()
    {
        // Act - 複数回取得して一貫性を確認
        var family1 = DefaultFontSettings.Family;
        var family2 = DefaultFontSettings.Family;
        var size1 = DefaultFontSettings.Size;
        var size2 = DefaultFontSettings.Size;
        var weight1 = DefaultFontSettings.Weight;
        var weight2 = DefaultFontSettings.Weight;
        var lineHeight1 = DefaultFontSettings.LineHeight;
        var lineHeight2 = DefaultFontSettings.LineHeight;

        // Assert
        Assert.Equal(family1, family2);
        Assert.Equal(size1, size2);
        Assert.Equal(weight1, weight2);
        Assert.Equal(lineHeight1, lineHeight2);

        _output.WriteLine("✅ プロパティ一貫性テスト完了");
        _output.WriteLine($"  ファミリー: {family1}");
        _output.WriteLine($"  サイズ: {size1}");
        _output.WriteLine($"  ウェイト: {weight1}");
        _output.WriteLine($"  行間: {lineHeight1}");
    }

    #endregion

    #region Issue #69連携テスト

    [Fact]
    public void FontSettingsShouldMatchIssue69Requirements()
    {
        // Issue #69の統一フォント設定要件の確認
        
        // Assert - フォントファミリーはYu Gothic UI固定
        Assert.Equal("Yu Gothic UI", DefaultFontSettings.Family);
        
        // Assert - フォントサイズは16px固定
        Assert.Equal(16.0, DefaultFontSettings.Size);
        
        // Assert - フォントウェイトはNormal固定
        Assert.Equal(FontWeight.Normal, DefaultFontSettings.Weight);
        
        // Assert - 行間は1.4固定
        Assert.Equal(1.4, DefaultFontSettings.LineHeight);

        _output.WriteLine("✅ Issue #69連携要件テスト完了");
        _output.WriteLine("  - Yu Gothic UI 16px Normal固定設定確認済み");
        _output.WriteLine("  - 行間1.4設定確認済み");
    }

    #endregion

    #region 型安全性テスト

    [Fact]
    public void FontWeightShouldBeValidAvaloniaFontWeight()
    {
        // Act
        var weight = DefaultFontSettings.Weight;

        // Assert - 有効なFontWeight値である
        Assert.InRange(weight, FontWeight.Thin, FontWeight.UltraBlack);
        
        // Assert - Normal値である
        Assert.Equal(FontWeight.Normal, weight);

        _output.WriteLine($"✅ FontWeight型安全性テスト完了: {weight}");
    }

    [Fact]
    public void NumericValuesShouldBeValid()
    {
        // Act
        var size = DefaultFontSettings.Size;
        var lineHeight = DefaultFontSettings.LineHeight;

        // Assert - 正の値である
        Assert.True(size > 0);
        Assert.True(lineHeight > 0);
        
        // Assert - 実用的な範囲内である
        Assert.InRange(size, 8.0, 72.0);
        Assert.InRange(lineHeight, 1.0, 3.0);

        _output.WriteLine($"✅ 数値妥当性テスト完了:");
        _output.WriteLine($"  サイズ: {size} (8-72の範囲内)");
        _output.WriteLine($"  行間: {lineHeight} (1.0-3.0の範囲内)");
    }

    #endregion

    #region パフォーマンステスト

    [Fact]
    public void PropertyAccessShouldBeEfficient()
    {
        // Arrange
        var iterations = 10000;

        // Act - パフォーマンス測定
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            _ = DefaultFontSettings.Family;
            _ = DefaultFontSettings.Size;
            _ = DefaultFontSettings.Weight;
            _ = DefaultFontSettings.LineHeight;
        }

        stopwatch.Stop();

        // Assert - 10000回のアクセスが50ms以内に完了することを確認
        Assert.InRange(stopwatch.ElapsedMilliseconds, 0, 50);

        _output.WriteLine($"✅ パフォーマンステスト完了: {iterations}回のプロパティアクセスを{stopwatch.ElapsedMilliseconds}msで実行");
    }

    #endregion

    #region 文字列妥当性テスト

    [Fact]
    public void FamilyShouldBeNonEmptyString()
    {
        // Act
        var family = DefaultFontSettings.Family;

        // Assert
        Assert.NotNull(family);
        Assert.NotEmpty(family);
        Assert.False(string.IsNullOrWhiteSpace(family));

        _output.WriteLine($"✅ フォントファミリー文字列妥当性テスト完了: '{family}'");
    }

    [Fact]
    public void FamilyShouldContainValidCharacters()
    {
        // Act
        var family = DefaultFontSettings.Family;

        // Assert - 制御文字が含まれていない
        Assert.DoesNotContain('\0', family);
        Assert.DoesNotContain('\n', family);
        Assert.DoesNotContain('\r', family);
        Assert.DoesNotContain('\t', family);

        // Assert - 通常のフォント名として使用可能な文字のみ
        Assert.Matches(@"^[a-zA-Z0-9\s\-_]+$", family);

        _output.WriteLine($"✅ フォントファミリー文字妥当性テスト完了: '{family}'");
    }

    #endregion

    #region 設計要件確認テスト

    [Fact]
    public void DefaultFontSettingsShouldBeStaticClass()
    {
        // Act - リフレクションを使用してクラス情報を取得
        var type = typeof(DefaultFontSettings);

        // Assert - staticクラスである
        Assert.True(type.IsAbstract);
        Assert.True(type.IsSealed);

        // Assert - パブリックコンストラクターが存在しない
        var constructors = type.GetConstructors();
        Assert.Empty(constructors);

        _output.WriteLine("✅ DefaultFontSettingsクラス設計テスト完了:");
        _output.WriteLine("  - staticクラス設計確認済み");
        _output.WriteLine("  - インスタンス化不可確認済み");
    }

    [Fact]
    public void PropertiesShouldBeReadOnlyAndStatic()
    {
        // Act - リフレクションを使用してプロパティ情報を取得
        var type = typeof(DefaultFontSettings);
        var properties = type.GetProperties();

        // Assert - すべてのプロパティがstaticでread-only
        foreach (var property in properties)
        {
            Assert.NotNull(property.GetMethod);
            Assert.True(property.GetMethod.IsStatic, $"プロパティ {property.Name} はstaticである必要があります");
            Assert.Null(property.SetMethod); // set accessorが存在しない = read-only
        }

        _output.WriteLine($"✅ プロパティ設計テスト完了: {properties.Length}個のプロパティがすべてstatic read-only");
    }

    #endregion
}
