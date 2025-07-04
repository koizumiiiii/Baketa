using Xunit;
using Avalonia.Media;
using Baketa.Core.Settings;
using Baketa.UI.Converters;
using Baketa.UI.Tests.Infrastructure;
using System;
using System.Globalization;

namespace Baketa.UI.Tests.Converters;

/// <summary>
/// SettingsConvertersのテストクラス
/// </summary>
public sealed class SettingsConvertersTests : AvaloniaTestBase
{
    #region UiSizeToStringConverterTests

    /// <summary>
    /// UiSizeが正しく日本語文字列に変換されることをテスト
    /// </summary>
    [Theory]
    [InlineData(UiSize.Small, "小（コンパクト）")]
    [InlineData(UiSize.Medium, "中（標準）")]
    [InlineData(UiSize.Large, "大（見やすさ重視）")]
    public void UiSizeToStringConverter_Convert_ReturnsCorrectString(UiSize input, string expected)
    {
        // Arrange
        var converter = UiSizeToStringConverter.Instance;

        // Act
        var result = converter.Convert(input, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// UiSizeToStringConverterでnull値を渡した場合のテスト
    /// </summary>
    [Fact]
    public void UiSizeToStringConverter_Convert_WithNull_ReturnsNull()
    {
        // Arrange
        var converter = UiSizeToStringConverter.Instance;

        // Act
        var result = converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// UiSizeToStringConverterで無効な値を渡した場合のテスト
    /// </summary>
    [Fact]
    public void UiSizeToStringConverter_Convert_WithInvalidValue_ReturnsToString()
    {
        // Arrange
        var converter = UiSizeToStringConverter.Instance;
        var invalidValue = 999;

        // Act
        var result = converter.Convert(invalidValue, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("999", result);
    }

    /// <summary>
    /// UiSizeToStringConverterのConvertBackが例外をスローすることをテスト
    /// </summary>
    [Fact]
    public void UiSizeToStringConverter_ConvertBack_ThrowsNotSupportedException()
    {
        // Arrange
        var converter = UiSizeToStringConverter.Instance;

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => 
            converter.ConvertBack("小（コンパクト）", typeof(UiSize), null, CultureInfo.InvariantCulture));
    }

    #endregion

    #region BoolToStatusColorConverterTests

    /// <summary>
    /// BoolToStatusColorConverterが正しい色を返すことをテスト
    /// </summary>
    [Theory(Skip = "ハングアップ問題のため一時的に無効化")]
    [InlineData(true)]   // 変更あり：オレンジ
    [InlineData(false)]  // 変更なし：グリーン
    public void BoolToStatusColorConverter_Convert_ReturnsCorrectBrush(bool hasChanges)
    {
        // Arrange
        var converter = BoolToStatusColorConverter.Instance;

        // Act
        var result = RunOnUIThread(() => converter.Convert(hasChanges, typeof(Brush), null, CultureInfo.InvariantCulture));

        // Assert
        Assert.IsType<SolidColorBrush>(result);
        var brush = result as SolidColorBrush;
        
        if (hasChanges)
        {
            // オレンジ色の確認
            Assert.Equal(Color.FromRgb(255, 165, 0), brush!.Color);
        }
        else
        {
            // グリーン色の確認
            Assert.Equal(Color.FromRgb(34, 139, 34), brush!.Color);
        }
    }

    /// <summary>
    /// BoolToStatusColorConverterでnull値を渡した場合のテスト
    /// </summary>
    [Fact]
    public void BoolToStatusColorConverter_Convert_WithNull_ReturnsGrayBrush()
    {
        // Arrange
        var converter = BoolToStatusColorConverter.Instance;

        // Act
        var result = RunOnUIThread(() => converter.Convert(null, typeof(Brush), null, CultureInfo.InvariantCulture));

        // Assert
        Assert.IsType<SolidColorBrush>(result);
        var brush = result as SolidColorBrush;
        Assert.Equal(Color.FromRgb(128, 128, 128), brush!.Color); // null は不明な状態としてグレー
    }

    /// <summary>
    /// BoolToStatusColorConverterのConvertBackが例外をスローすることをテスト
    /// </summary>
    [Fact(Skip = "ハングアップ問題のため一時的に無効化")]
    public void BoolToStatusColorConverter_ConvertBack_ThrowsNotSupportedException()
    {
        // Arrange
        var converter = BoolToStatusColorConverter.Instance;
        
        // Act & Assert - UIスレッドでブラシ作成とテストを同時実行
        RunOnUIThread(() =>
        {
            var brush = new SolidColorBrush(Color.FromRgb(255, 165, 0));
            Assert.Throws<NotSupportedException>(() => 
                converter.ConvertBack(brush, typeof(bool), null, CultureInfo.InvariantCulture));
        });
    }

    #endregion

    #region UiThemeToStringConverterTests

    /// <summary>
    /// UiThemeが正しく日本語文字列に変換されることをテスト
    /// </summary>
    [Theory]
    [InlineData(UiTheme.Light, "ライト")]
    [InlineData(UiTheme.Dark, "ダーク")]
    [InlineData(UiTheme.Auto, "自動（システム設定に従う）")]
    public void UiThemeToStringConverter_Convert_ReturnsCorrectString(UiTheme input, string expected)
    {
        // Arrange
        var converter = UiThemeToStringConverter.Instance;

        // Act
        var result = converter.Convert(input, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// UiThemeToStringConverterでnull値を渡した場合のテスト
    /// </summary>
    [Fact]
    public void UiThemeToStringConverter_Convert_WithNull_ReturnsNull()
    {
        // Arrange
        var converter = UiThemeToStringConverter.Instance;

        // Act
        var result = converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// UiThemeToStringConverterで無効な値を渡した場合のテスト
    /// </summary>
    [Fact]
    public void UiThemeToStringConverter_Convert_WithInvalidValue_ReturnsToString()
    {
        // Arrange
        var converter = UiThemeToStringConverter.Instance;
        var invalidValue = 999;

        // Act
        var result = converter.Convert(invalidValue, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("999", result);
    }

    /// <summary>
    /// UiThemeToStringConverterのConvertBackが例外をスローすることをテスト
    /// </summary>
    [Fact]
    public void UiThemeToStringConverter_ConvertBack_ThrowsNotSupportedException()
    {
        // Arrange
        var converter = UiThemeToStringConverter.Instance;

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => 
            converter.ConvertBack("ライト", typeof(UiTheme), null, CultureInfo.InvariantCulture));
    }

    #endregion

    #region BoolToAdvancedSettingsTextConverterTests

    /// <summary>
    /// BoolToAdvancedSettingsTextConverterが正しいテキストを返すことをテスト
    /// </summary>
    [Theory]
    [InlineData(true, "基本設定に戻す")]
    [InlineData(false, "詳細設定を表示")]
    public void BoolToAdvancedSettingsTextConverter_Convert_ReturnsCorrectText(bool input, string expected)
    {
        // Arrange
        var converter = BoolToAdvancedSettingsTextConverter.Instance;

        // Act
        var result = converter.Convert(input, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// BoolToAdvancedSettingsTextConverterでnull値を渡した場合のテスト
    /// </summary>
    [Fact]
    public void BoolToAdvancedSettingsTextConverter_Convert_WithNull_ReturnsCorrectDefault()
    {
        // Arrange
        var converter = BoolToAdvancedSettingsTextConverter.Instance;

        // Act
        var result = converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("詳細設定を表示", result); // null は false として扱われる
    }

    /// <summary>
    /// BoolToAdvancedSettingsTextConverterのConvertBackが例外をスローすることをテスト
    /// </summary>
    [Fact]
    public void BoolToAdvancedSettingsTextConverter_ConvertBack_ThrowsNotSupportedException()
    {
        // Arrange
        var converter = BoolToAdvancedSettingsTextConverter.Instance;

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => 
            converter.ConvertBack("基本設定に戻す", typeof(bool), null, CultureInfo.InvariantCulture));
    }

    #endregion

    #region BoolToExpandIconConverterTests

    /// <summary>
    /// BoolToExpandIconConverterが正しいアイコンパスを返すことをテスト
    /// </summary>
    [Theory]
    [InlineData(true, "M7.41,15.41L12,10.83L16.59,15.41L18,14L12,8L6,14L7.41,15.41Z")]  // 上向き矢印
    [InlineData(false, "M7.41,8.58L12,13.17L16.59,8.58L18,10L12,16L6,10L7.41,8.58Z")]   // 下向き矢印
    public void BoolToExpandIconConverter_Convert_ReturnsCorrectPath(bool input, string expected)
    {
        // Arrange
        var converter = BoolToExpandIconConverter.Instance;

        // Act
        var result = converter.Convert(input, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// BoolToExpandIconConverterでnull値を渡した場合のテスト
    /// </summary>
    [Fact]
    public void BoolToExpandIconConverter_Convert_WithNull_ReturnsDownArrow()
    {
        // Arrange
        var converter = BoolToExpandIconConverter.Instance;

        // Act
        var result = converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("M7.41,8.58L12,13.17L16.59,8.58L18,10L12,16L6,10L7.41,8.58Z", result); // null は false として扱われ下向き矢印
    }

    /// <summary>
    /// BoolToExpandIconConverterのConvertBackが例外をスローすることをテスト
    /// </summary>
    [Fact]
    public void BoolToExpandIconConverter_ConvertBack_ThrowsNotSupportedException()
    {
        // Arrange
        var converter = BoolToExpandIconConverter.Instance;

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => 
            converter.ConvertBack("M7.41,15.41L12,10.83L16.59,15.41L18,14L12,8L6,14L7.41,15.41Z", typeof(bool), null, CultureInfo.InvariantCulture));
    }

    #endregion

    #region シングルトンインスタンステスト

    /// <summary>
    /// すべてのコンバーターでシングルトンインスタンスが正しく機能することをテスト
    /// </summary>
    [Fact]
    public void AllConverters_SingletonInstance_AreSameReference()
    {
        // Arrange & Act
        var uiSizeConverter1 = UiSizeToStringConverter.Instance;
        var uiSizeConverter2 = UiSizeToStringConverter.Instance;
        
        var uiThemeConverter1 = UiThemeToStringConverter.Instance;
        var uiThemeConverter2 = UiThemeToStringConverter.Instance;
        
        var boolToTextConverter1 = BoolToAdvancedSettingsTextConverter.Instance;
        var boolToTextConverter2 = BoolToAdvancedSettingsTextConverter.Instance;
        
        var boolToIconConverter1 = BoolToExpandIconConverter.Instance;
        var boolToIconConverter2 = BoolToExpandIconConverter.Instance;
        
        var boolToColorConverter1 = BoolToStatusColorConverter.Instance;
        var boolToColorConverter2 = BoolToStatusColorConverter.Instance;

        // Assert
        Assert.Same(uiSizeConverter1, uiSizeConverter2);
        Assert.Same(uiThemeConverter1, uiThemeConverter2);
        Assert.Same(boolToTextConverter1, boolToTextConverter2);
        Assert.Same(boolToIconConverter1, boolToIconConverter2);
        Assert.Same(boolToColorConverter1, boolToColorConverter2);
    }

    #endregion
}
