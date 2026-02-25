using Baketa.Core.Abstractions.Text;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Text.ChangeDetection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Text.ChangeDetection;

/// <summary>
/// [Issue #409] OCR末尾ノイズ正規化のテスト
/// </summary>
public class TextChangeDetectionServiceOcrNoiseTests
{
    private readonly Mock<ILogger<TextChangeDetectionService>> _loggerMock = new();

    private TextChangeDetectionService CreateService(RoiGatekeeperSettings? settings = null)
    {
        settings ??= new RoiGatekeeperSettings
        {
            Enabled = true,
            AlwaysTranslateFirstText = true,
            SkipIdenticalText = true,
            SkipEmptyText = true,
            MinTextLength = 2
        };

        var optionsMock = new Mock<IOptions<RoiGatekeeperSettings>>();
        optionsMock.Setup(o => o.Value).Returns(settings);

        return new TextChangeDetectionService(_loggerMock.Object, optionsMock.Object);
    }

    [Theory]
    [InlineData("テスト●", "テスト")]
    [InlineData("テスト◎", "テスト")]
    [InlineData("テスト★☆", "テスト")]
    [InlineData("テスト※", "テスト")]
    [InlineData("テスト●◎", "テスト")]
    public void NormalizeOcrNoise_RemovesTrailingDecorationChars(string input, string expected)
    {
        var result = TextChangeDetectionService.NormalizeOcrNoise(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("<b>テスト</b>", "テスト")]
    [InlineData("テスト<br/>結果", "テスト結果")]
    [InlineData("<span class='x'>Hello</span>", "Hello")]
    public void NormalizeOcrNoise_RemovesHtmlTags(string input, string expected)
    {
        var result = TextChangeDetectionService.NormalizeOcrNoise(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("テスト  ", "テスト")]
    [InlineData("テスト\t", "テスト")]
    public void NormalizeOcrNoise_TrimsTrailingWhitespace(string input, string expected)
    {
        var result = TextChangeDetectionService.NormalizeOcrNoise(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void NormalizeOcrNoise_EmptyString_ReturnsEmpty()
    {
        TextChangeDetectionService.NormalizeOcrNoise("").Should().Be("");
        TextChangeDetectionService.NormalizeOcrNoise(null!).Should().BeNull();
    }

    [Fact]
    public void NormalizeOcrNoise_NoNoise_ReturnsUnchanged()
    {
        TextChangeDetectionService.NormalizeOcrNoise("正常なテキスト").Should().Be("正常なテキスト");
    }

    [Fact]
    public async Task DetectChangeWithGateAsync_TrailingNoiseDifference_ReturnsSameText()
    {
        // Arrange: 末尾●の有無だけが異なるテキストはSameTextと判定されるべき
        var service = CreateService();
        const string sourceId = "test_zone";

        // Act: 初回テキスト登録
        var firstResult = await service.DetectChangeWithGateAsync("テストテキスト●", sourceId);
        firstResult.ShouldTranslate.Should().BeTrue();

        // Act: 末尾●なしで再検知 → SameTextになるべき
        var secondResult = await service.DetectChangeWithGateAsync("テストテキスト", sourceId);
        secondResult.Decision.Should().Be(GateDecision.SameText);
        secondResult.ShouldTranslate.Should().BeFalse();
    }

    [Fact]
    public async Task DetectChangeWithGateAsync_TrailingDecoration_DoesNotAffectLevenshtein()
    {
        // Arrange: 末尾装飾の揺れがLevenshtein距離に影響しないことを確認
        var service = CreateService();
        const string sourceId = "test_zone";

        // 初回
        await service.DetectChangeWithGateAsync("Hello World●◎", sourceId);

        // 末尾装飾が変わっただけ → SameText
        var result = await service.DetectChangeWithGateAsync("Hello World★☆", sourceId);
        result.Decision.Should().Be(GateDecision.SameText);
    }
}
