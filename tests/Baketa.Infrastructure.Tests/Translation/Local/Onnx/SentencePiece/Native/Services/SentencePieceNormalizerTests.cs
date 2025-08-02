using System;
using System.Globalization;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece.Native.Services;

/// <summary>
/// SentencePiece正規化サービスの単体テスト
/// </summary>
public class SentencePieceNormalizerTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<ILogger<SentencePieceNormalizer>> _mockLogger;
    private readonly SentencePieceNormalizer _normalizer;
    private bool _disposed;

    public SentencePieceNormalizerTests(ITestOutputHelper output)
    {
        _output = output;
        _mockLogger = new Mock<ILogger<SentencePieceNormalizer>>();
        _normalizer = new SentencePieceNormalizer(_mockLogger.Object);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("hello", "▁hello")]
    [InlineData("Hello World", "▁Hello▁World")]
    [InlineData("こんにちは", "▁こんにちは")]
    [InlineData("こんにちは 世界", "▁こんにちは▁世界")]
    public void Normalize_BasicTexts_ShouldAddPrefixSpaceSymbol(string input, string expected)
    {
        // Act
        var result = _normalizer.Normalize(input);

        // Assert
        result.Should().Be(expected);
        _output.WriteLine($"✓ '{input}' -> '{result}'");
    }

    [Theory]
    [InlineData("ｈｅｌｌｏ", "▁hello")] // 全角英字 -> 半角英字
    [InlineData("１２３", "▁123")] // 全角数字 -> 半角数字
    [InlineData("Ｈｅｌｌｏ　Ｗｏｒｌｄ", "▁Hello▁World")] // 全角空白 -> 半角空白
    [InlineData("café", "▁café")] // 結合文字の正規化
    public void Normalize_UnicodeCharacters_ShouldApplyNfkcNormalization(string input, string expected)
    {
        // Act
        var result = _normalizer.Normalize(input);

        // Assert
        result.Should().Be(expected);
        _output.WriteLine($"✓ NFKC: '{input}' -> '{result}'");
    }

    [Theory]
    [InlineData("hello\tworld", "▁hello▁world")] // タブ -> 空白
    [InlineData("hello\nworld", "▁hello▁world")] // 改行 -> 空白
    [InlineData("hello\rworld", "▁hello▁world")] // 復帰文字 -> 空白
    [InlineData("hello\u0000world", "▁helloworld")] // NULL文字 -> 除去
    [InlineData("hello\u001Fworld", "▁helloworld")] // 制御文字 -> 除去
    public void Normalize_ControlCharacters_ShouldBeProcessedCorrectly(string input, string expected)
    {
        // Act
        var result = _normalizer.Normalize(input);

        // Assert
        result.Should().Be(expected);
        _output.WriteLine($"✓ Control chars: '{input.Replace("\u0000", "\\0").Replace("\u001F", "\\u001F")}' -> '{result}'");
    }

    [Theory]
    [InlineData("hello   world", "▁hello▁world")] // 複数空白 -> 単一空白
    [InlineData("  hello  world  ", "▁hello▁world")] // 前後空白 + 複数空白
    [InlineData("hello\u3000world", "▁hello▁world")] // 全角空白 -> 半角空白
    [InlineData("hello\u00A0world", "▁hello▁world")] // ノーブレークスペース -> 空白
    public void Normalize_WhitespaceCharacters_ShouldBeNormalized(string input, string expected)
    {
        // Act
        var result = _normalizer.Normalize(input);

        // Assert
        result.Should().Be(expected);
        _output.WriteLine($"✓ Whitespace: '{input}' -> '{result}'");
    }

    [Theory]
    [InlineData("▁hello", "hello")]
    [InlineData("▁hello▁world", "hello world")]
    [InlineData("▁こんにちは▁世界", "こんにちは 世界")]
    [InlineData("▁", "")]
    [InlineData("", "")]
    public void RemovePrefixSpaceSymbol_ShouldRestoreOriginalSpaces(string input, string expected)
    {
        // Act
        var result = _normalizer.RemovePrefixSpaceSymbol(input);

        // Assert
        result.Should().Be(expected);
        _output.WriteLine($"✓ Remove prefix: '{input}' -> '{result}'");
    }

    [Fact]
    public void Normalize_WithCustomOptions_ShouldRespectSettings()
    {
        // Arrange
        var options = new SentencePieceNormalizationOptions
        {
            ApplyNfkcNormalization = false,
            RemoveControlCharacters = false,
            NormalizeWhitespace = false,
            AddPrefixSpace = false
        };
        using var customNormalizer = new SentencePieceNormalizer(_mockLogger.Object, options);
        const string input = "ｈｅｌｌｏ\tworld";

        // Act
        var result = customNormalizer.Normalize(input);

        // Assert
        result.Should().Be(input); // 正規化なしなので元のまま
        _output.WriteLine($"✓ No normalization: '{input}' -> '{result}'");
    }

    [Fact]
    public void Normalize_ComplexRealWorldText_ShouldHandleCorrectly()
    {
        // Arrange
        const string input = "Ｈｅｌｌｏ，　ｗｏｒｌｄ!\nこんにちは\t世界！";
        const string expected = "▁Hello,▁world!▁こんにちは▁世界！";

        // Act
        var result = _normalizer.Normalize(input);

        // Assert
        result.Should().Be(expected);
        _output.WriteLine($"✓ Complex text: '{input}' -> '{result}'");
    }

    [Fact]
    public void Normalize_EmojisAndSpecialCharacters_ShouldPreserveCorrectly()
    {
        // Arrange
        const string input = "Hello 😀 World! 🌍";
        const string expected = "▁Hello▁😀▁World!▁🌍";

        // Act
        var result = _normalizer.Normalize(input);

        // Assert
        result.Should().Be(expected);
        _output.WriteLine($"✓ Emojis: '{input}' -> '{result}'");
    }

    [Fact]
    public void GetOptions_ShouldReturnCorrectOptions()
    {
        // Act
        var options = _normalizer.GetOptions();

        // Assert
        options.Should().NotBeNull();
        options.ApplyNfkcNormalization.Should().BeTrue();
        options.RemoveControlCharacters.Should().BeTrue();
        options.NormalizeWhitespace.Should().BeTrue();
        options.AddPrefixSpace.Should().BeTrue();
    }

    [Fact]
    public void DefaultOptions_ShouldHaveCorrectSettings()
    {
        // Act
        var options = SentencePieceNormalizationOptions.Default;

        // Assert
        options.ApplyNfkcNormalization.Should().BeTrue();
        options.RemoveControlCharacters.Should().BeTrue();
        options.NormalizeWhitespace.Should().BeTrue();
        options.AddPrefixSpace.Should().BeTrue();
    }

    [Fact]
    public void OpusMtOptions_ShouldHaveCorrectSettings()
    {
        // Act
        var options = SentencePieceNormalizationOptions.OpusMt;

        // Assert
        options.ApplyNfkcNormalization.Should().BeTrue();
        options.RemoveControlCharacters.Should().BeTrue();
        options.NormalizeWhitespace.Should().BeTrue();
        options.AddPrefixSpace.Should().BeTrue();
    }

    [Fact]
    public void NoneOptions_ShouldDisableAllNormalization()
    {
        // Act
        var options = SentencePieceNormalizationOptions.None;

        // Assert
        options.ApplyNfkcNormalization.Should().BeFalse();
        options.RemoveControlCharacters.Should().BeFalse();
        options.NormalizeWhitespace.Should().BeFalse();
        options.AddPrefixSpace.Should().BeFalse();
    }

    [Fact]
    public void SpaceSymbol_ShouldBeCorrectUnicodeCharacter()
    {
        // Assert
        SentencePieceNormalizer.SpaceSymbol.Should().Be('\u2581');
        ((int)SentencePieceNormalizer.SpaceSymbol).Should().Be(0x2581);
        _output.WriteLine($"✓ Space symbol: '{SentencePieceNormalizer.SpaceSymbol}' (U+{(int)SentencePieceNormalizer.SpaceSymbol:X4})");
    }

    [Fact]
    public void Normalize_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        _normalizer.Dispose();

        // Act & Assert
        var action = () => _normalizer.Normalize("test");
        action.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void RemovePrefixSpaceSymbol_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        _normalizer.Dispose();

        // Act & Assert
        var action = () => _normalizer.RemovePrefixSpaceSymbol("▁test");
        action.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData("hello world", "▁hello▁world", "hello world")]
    [InlineData("ｈｅｌｌｏ　ｗｏｒｌｄ", "▁hello▁world", "hello world")]
    [InlineData("Hello\tWorld", "▁Hello▁World", "Hello World")]
    public void NormalizeAndReverse_ShouldBeConsistent(string original, string expectedNormalized, string expectedReversed)
    {
        // Act
        var normalized = _normalizer.Normalize(original);
        var reversed = _normalizer.RemovePrefixSpaceSymbol(normalized);

        // Assert
        normalized.Should().Be(expectedNormalized);
        reversed.Should().Be(expectedReversed);
        _output.WriteLine($"✓ Round trip: '{original}' -> '{normalized}' -> '{reversed}'");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _normalizer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}