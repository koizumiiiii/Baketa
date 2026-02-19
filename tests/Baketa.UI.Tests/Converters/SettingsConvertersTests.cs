using System;
using System.Globalization;
using Avalonia.Media;
using Baketa.Core.Settings;
using Baketa.UI.Converters;
using Baketa.UI.Resources;
using Baketa.UI.Tests.Infrastructure;
using Xunit;

namespace Baketa.UI.Tests.Converters;

/// <summary>
/// SettingsConvertersのテストクラス
/// リソース文字列を使用するコンバーターのテストではStrings定数で期待値を参照
/// </summary>
public sealed class SettingsConvertersTests : AvaloniaTestBase
{
    #region UiSizeToStringConverterTests

    /// <summary>
    /// UiSizeが正しくリソース文字列に変換されることをテスト
    /// </summary>
    [Fact]
    public void UiSizeToStringConverter_Convert_Small_ReturnsCorrectString()
    {
        var converter = UiSizeToStringConverter.Instance;
        var result = converter.Convert(UiSize.Small, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(Strings.Size_Small, result);
    }

    [Fact]
    public void UiSizeToStringConverter_Convert_Medium_ReturnsCorrectString()
    {
        var converter = UiSizeToStringConverter.Instance;
        var result = converter.Convert(UiSize.Medium, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(Strings.Size_Medium, result);
    }

    [Fact]
    public void UiSizeToStringConverter_Convert_Large_ReturnsCorrectString()
    {
        var converter = UiSizeToStringConverter.Instance;
        var result = converter.Convert(UiSize.Large, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(Strings.Size_Large, result);
    }

    /// <summary>
    /// UiSizeToStringConverterでnull値を渡した場合のテスト
    /// </summary>
    [Fact]
    public void UiSizeToStringConverter_Convert_WithNull_ReturnsNull()
    {
        var converter = UiSizeToStringConverter.Instance;
        var result = converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Null(result);
    }

    /// <summary>
    /// UiSizeToStringConverterで無効な値を渡した場合のテスト
    /// </summary>
    [Fact]
    public void UiSizeToStringConverter_Convert_WithInvalidValue_ReturnsToString()
    {
        var converter = UiSizeToStringConverter.Instance;
        var result = converter.Convert(999, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal("999", result);
    }

    /// <summary>
    /// UiSizeToStringConverterのConvertBackが例外をスローすることをテスト
    /// </summary>
    [Fact]
    public void UiSizeToStringConverter_ConvertBack_ThrowsNotSupportedException()
    {
        var converter = UiSizeToStringConverter.Instance;
        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(Strings.Size_Small, typeof(UiSize), null, CultureInfo.InvariantCulture));
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
        var converter = BoolToStatusColorConverter.Instance;
        var result = RunOnUIThread(() => converter.Convert(hasChanges, typeof(Brush), null, CultureInfo.InvariantCulture));

        Assert.IsType<SolidColorBrush>(result);
        var brush = result as SolidColorBrush;

        if (hasChanges)
            Assert.Equal(Color.FromRgb(255, 165, 0), brush!.Color);
        else
            Assert.Equal(Color.FromRgb(34, 139, 34), brush!.Color);
    }

    /// <summary>
    /// BoolToStatusColorConverterでnull値を渡した場合のテスト
    /// </summary>
    [Fact]
    public void BoolToStatusColorConverter_Convert_WithNull_ReturnsGrayBrush()
    {
        var converter = BoolToStatusColorConverter.Instance;
        var result = RunOnUIThread(() => converter.Convert(null, typeof(Brush), null, CultureInfo.InvariantCulture));

        Assert.IsType<SolidColorBrush>(result);
        var brush = result as SolidColorBrush;
        Assert.Equal(Color.FromRgb(128, 128, 128), brush!.Color);
    }

    /// <summary>
    /// BoolToStatusColorConverterのConvertBackが例外をスローすることをテスト
    /// </summary>
    [Fact(Skip = "ハングアップ問題のため一時的に無効化")]
    public void BoolToStatusColorConverter_ConvertBack_ThrowsNotSupportedException()
    {
        var converter = BoolToStatusColorConverter.Instance;
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
    /// UiThemeが正しくリソース文字列に変換されることをテスト
    /// </summary>
    [Fact]
    public void UiThemeToStringConverter_Convert_Light_ReturnsCorrectString()
    {
        var converter = UiThemeToStringConverter.Instance;
        var result = converter.Convert(UiTheme.Light, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(Strings.Theme_Light, result);
    }

    [Fact]
    public void UiThemeToStringConverter_Convert_Dark_ReturnsCorrectString()
    {
        var converter = UiThemeToStringConverter.Instance;
        var result = converter.Convert(UiTheme.Dark, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(Strings.Theme_Dark, result);
    }

    [Fact]
    public void UiThemeToStringConverter_Convert_Auto_ReturnsCorrectString()
    {
        var converter = UiThemeToStringConverter.Instance;
        var result = converter.Convert(UiTheme.Auto, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(Strings.Theme_Auto, result);
    }

    /// <summary>
    /// UiThemeToStringConverterでnull値を渡した場合のテスト
    /// </summary>
    [Fact]
    public void UiThemeToStringConverter_Convert_WithNull_ReturnsNull()
    {
        var converter = UiThemeToStringConverter.Instance;
        var result = converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Null(result);
    }

    /// <summary>
    /// UiThemeToStringConverterで無効な値を渡した場合のテスト
    /// </summary>
    [Fact]
    public void UiThemeToStringConverter_Convert_WithInvalidValue_ReturnsToString()
    {
        var converter = UiThemeToStringConverter.Instance;
        var result = converter.Convert(999, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal("999", result);
    }

    /// <summary>
    /// UiThemeToStringConverterのConvertBackが例外をスローすることをテスト
    /// </summary>
    [Fact]
    public void UiThemeToStringConverter_ConvertBack_ThrowsNotSupportedException()
    {
        var converter = UiThemeToStringConverter.Instance;
        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(Strings.Theme_Light, typeof(UiTheme), null, CultureInfo.InvariantCulture));
    }

    #endregion

    #region BoolToAdvancedSettingsTextConverterTests

    /// <summary>
    /// BoolToAdvancedSettingsTextConverterが正しいテキストを返すことをテスト
    /// </summary>
    [Fact]
    public void BoolToAdvancedSettingsTextConverter_Convert_True_ReturnsHideText()
    {
        var converter = BoolToAdvancedSettingsTextConverter.Instance;
        var result = converter.Convert(true, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(Strings.Settings_Advanced_Hide, result);
    }

    [Fact]
    public void BoolToAdvancedSettingsTextConverter_Convert_False_ReturnsShowText()
    {
        var converter = BoolToAdvancedSettingsTextConverter.Instance;
        var result = converter.Convert(false, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(Strings.Settings_Advanced_Show, result);
    }

    /// <summary>
    /// BoolToAdvancedSettingsTextConverterでnull値を渡した場合のテスト
    /// </summary>
    [Fact]
    public void BoolToAdvancedSettingsTextConverter_Convert_WithNull_ReturnsShowText()
    {
        var converter = BoolToAdvancedSettingsTextConverter.Instance;
        var result = converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(Strings.Settings_Advanced_Show, result);
    }

    /// <summary>
    /// BoolToAdvancedSettingsTextConverterのConvertBackが例外をスローすることをテスト
    /// </summary>
    [Fact]
    public void BoolToAdvancedSettingsTextConverter_ConvertBack_ThrowsNotSupportedException()
    {
        var converter = BoolToAdvancedSettingsTextConverter.Instance;
        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(Strings.Settings_Advanced_Hide, typeof(bool), null, CultureInfo.InvariantCulture));
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
        var converter = BoolToExpandIconConverter.Instance;
        var result = converter.Convert(input, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// BoolToExpandIconConverterでnull値を渡した場合のテスト
    /// </summary>
    [Fact]
    public void BoolToExpandIconConverter_Convert_WithNull_ReturnsDownArrow()
    {
        var converter = BoolToExpandIconConverter.Instance;
        var result = converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal("M7.41,8.58L12,13.17L16.59,8.58L18,10L12,16L6,10L7.41,8.58Z", result);
    }

    /// <summary>
    /// BoolToExpandIconConverterのConvertBackが例外をスローすることをテスト
    /// </summary>
    [Fact]
    public void BoolToExpandIconConverter_ConvertBack_ThrowsNotSupportedException()
    {
        var converter = BoolToExpandIconConverter.Instance;
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
        Assert.Same(UiSizeToStringConverter.Instance, UiSizeToStringConverter.Instance);
        Assert.Same(UiThemeToStringConverter.Instance, UiThemeToStringConverter.Instance);
        Assert.Same(BoolToAdvancedSettingsTextConverter.Instance, BoolToAdvancedSettingsTextConverter.Instance);
        Assert.Same(BoolToExpandIconConverter.Instance, BoolToExpandIconConverter.Instance);
        Assert.Same(BoolToStatusColorConverter.Instance, BoolToStatusColorConverter.Instance);
    }

    #endregion
}
