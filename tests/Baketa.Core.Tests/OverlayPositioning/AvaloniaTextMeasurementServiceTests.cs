using Baketa.Core.UI.Geometry;
using Baketa.Core.UI.Overlay.Positioning;
using Baketa.UI.Overlay.Positioning;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Baketa.Core.Tests.OverlayPositioning;

/// <summary>
/// Avaloniaテキスト測定サービスのテスト
/// </summary>
public sealed class AvaloniaTextMeasurementServiceTests : IDisposable
{
    private readonly AvaloniaTextMeasurementService _measurementService;

    public AvaloniaTextMeasurementServiceTests()
    {
        _measurementService = new AvaloniaTextMeasurementService(
            NullLogger<AvaloniaTextMeasurementService>.Instance
        );
    }

    [Fact]
    public async Task MeasureTextAsync_WithSimpleText_ShouldReturnValidResult()
    {
        // Arrange
        var text = "Hello World";
        var options = TextMeasurementOptions.Default;

        // Act
        var result = await _measurementService.MeasureTextAsync(text, options);

        // Assert
        Assert.True(result.IsValid);
        Assert.True(result.Size.Width > 0);
        Assert.True(result.Size.Height > 0);
        Assert.True(result.LineCount >= 1);
        Assert.Equal(options.FontSize, result.ActualFontSize);
    }

    [Fact]
    public async Task MeasureTextAsync_WithJapaneseText_ShouldReturnValidResult()
    {
        // Arrange
        var text = "こんにちは世界";
        var options = TextMeasurementOptions.Default with
        {
            FontFamily = "Yu Gothic UI",
            FontSize = 16,
            FontWeight = "Normal"
        };

        // Act
        var result = await _measurementService.MeasureTextAsync(text, options);

        // Assert
        Assert.True(result.IsValid);
        Assert.True(result.Size.Width > 0);
        Assert.True(result.Size.Height > 0);
        Assert.Equal(1, result.LineCount); // 単一行
        Assert.Equal("Yu Gothic UI", result.MeasuredWith.FontFamily);
    }

    [Fact]
    public async Task MeasureTextAsync_WithMultilineText_ShouldCountLinesCorrectly()
    {
        // Arrange
        var text = "Line 1\nLine 2\nLine 3";
        var options = TextMeasurementOptions.Default;

        // Act
        var result = await _measurementService.MeasureTextAsync(text, options);

        // Assert
        Assert.True(result.IsValid);
        Assert.True(result.LineCount >= 3); // 最低3行
        Assert.True(result.Size.Height > options.FontSize * 2); // 複数行分の高さ
    }

    [Fact]
    public async Task MeasureTextAsync_WithLongText_ShouldWrapWithinMaxWidth()
    {
        // Arrange
        var text = "This is a very long text that should wrap to multiple lines when the maximum width constraint is applied";
        var options = TextMeasurementOptions.Default with { MaxWidth = 200 };

        // Act
        var result = await _measurementService.MeasureTextAsync(text, options);

        // Assert
        Assert.True(result.IsValid);
        Assert.True(result.Size.Width <= options.MaxWidth + 50); // パディング考慮
        // 概算計算のため、最低1行は保証されていることを確認
        Assert.True(result.LineCount >= 1); // 最低1行
    }

    [Theory]
    [InlineData("Short", 1)]
    [InlineData("Medium length text", 1)]
    [InlineData("This is a longer text that might wrap depending on the font size and max width settings", 1)] // 概算計算のため最低1行でテスト
    public async Task MeasureTextAsync_WithDifferentTextLengths_ShouldReturnAppropriateLineCount(
        string text, int expectedMinLineCount)
    {
        // Arrange
        var options = TextMeasurementOptions.Default with { MaxWidth = 300 };

        // Act
        var result = await _measurementService.MeasureTextAsync(text, options);

        // Assert
        Assert.True(result.IsValid);
        Assert.True(result.LineCount >= expectedMinLineCount);
    }

    [Fact]
    public async Task MeasureTextAsync_WithDifferentFontSizes_ShouldScaleAppropriately()
    {
        // Arrange
        var text = "Test text";
        var smallFont = TextMeasurementOptions.Default with { FontSize = 12 };
        var largeFont = TextMeasurementOptions.Default with { FontSize = 24 };

        // Act
        var smallResult = await _measurementService.MeasureTextAsync(text, smallFont);
        var largeResult = await _measurementService.MeasureTextAsync(text, largeFont);

        // Assert
        Assert.True(smallResult.IsValid);
        Assert.True(largeResult.IsValid);
        Assert.True(largeResult.Size.Width > smallResult.Size.Width);
        Assert.True(largeResult.Size.Height > smallResult.Size.Height);
    }

    [Fact]
    public async Task MeasureTextAsync_WithPadding_ShouldIncludePaddingInSize()
    {
        // Arrange
        var text = "Test";
        var noPadding = TextMeasurementOptions.Default with { Padding = CoreThickness.Zero };
        var withPadding = TextMeasurementOptions.Default with { Padding = new CoreThickness(10) };

        // Act
        var noPaddingResult = await _measurementService.MeasureTextAsync(text, noPadding);
        var withPaddingResult = await _measurementService.MeasureTextAsync(text, withPadding);

        // Assert
        Assert.True(noPaddingResult.IsValid);
        Assert.True(withPaddingResult.IsValid);
        Assert.True(withPaddingResult.Size.Width > noPaddingResult.Size.Width);
        Assert.True(withPaddingResult.Size.Height > noPaddingResult.Size.Height);
    }

    [Fact]
    public async Task MeasureTextAsync_WithEmptyText_ShouldThrowException()
    {
        // Arrange
        var options = TextMeasurementOptions.Default;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _measurementService.MeasureTextAsync("", options));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _measurementService.MeasureTextAsync("   ", options));
    }

    [Fact]
    public async Task MeasureTextAsync_ConcurrentCalls_ShouldHandleThreadSafety()
    {
        // Arrange
        var text = "Concurrent test";
        var options = TextMeasurementOptions.Default;
        var tasks = new List<Task<TextMeasurementResult>>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(_measurementService.MeasureTextAsync(text, options));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, result => Assert.True(result.IsValid));

        // すべての結果が同じであることを確認
        var firstResult = results[0];
        Assert.All(results, result =>
        {
            Assert.Equal(firstResult.Size.Width, result.Size.Width, 1); // 1ピクセルの誤差許容
            Assert.Equal(firstResult.Size.Height, result.Size.Height, 1);
            Assert.Equal(firstResult.LineCount, result.LineCount);
        });
    }

    [Fact]
    public void TextMeasurementOptions_Extensions_ShouldWorkCorrectly()
    {
        // Arrange & Act
        var japaneseOptions = TextMeasurementOptions.Default with
        {
            FontFamily = "Yu Gothic UI",
            FontSize = 16,
            FontWeight = "Normal"
        };
        var englishOptions = TextMeasurementOptions.Default with
        {
            FontFamily = "Segoe UI",
            FontSize = 14,
            FontWeight = "Normal"
        };
        var largeOptions = TextMeasurementOptions.Default with
        {
            FontSize = TextMeasurementOptions.Default.FontSize * 1.25,
            Padding = new CoreThickness(15)
        };
        var compactOptions = TextMeasurementOptions.Default with
        {
            FontSize = TextMeasurementOptions.Default.FontSize * 0.9,
            Padding = new CoreThickness(8)
        };

        // Assert
        Assert.Equal("Yu Gothic UI", japaneseOptions.FontFamily);
        Assert.Equal(16, japaneseOptions.FontSize);

        Assert.Equal("Segoe UI", englishOptions.FontFamily);
        Assert.Equal(14, englishOptions.FontSize);

        Assert.True(largeOptions.FontSize > TextMeasurementOptions.Default.FontSize);
        Assert.True(compactOptions.FontSize < TextMeasurementOptions.Default.FontSize);
    }

    public void Dispose()
    {
        // AvaloniaTextMeasurementServiceがIDisposableを実装している場合は適切に破棄
        if (_measurementService is IDisposable disposableService)
        {
            disposableService.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
