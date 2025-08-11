using System;
using System.IO;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// RealSentencePieceTokenizerの正規化機能の単体テスト
/// </summary>
public class RealSentencePieceTokenizerNormalizationTests : SentencePieceTestBase, IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<ILogger<RealSentencePieceTokenizer>> _mockLogger;
    private readonly string _tempModelPath;
    private bool _disposed;

    public RealSentencePieceTokenizerNormalizationTests(ITestOutputHelper output)
    {
        _output = output;
        _mockLogger = new Mock<ILogger<RealSentencePieceTokenizer>>();
        
        // テスト用の一時モデルファイルを作成
        _tempModelPath = Path.GetTempFileName();
        File.WriteAllBytes(_tempModelPath, [0x1A, 0x02, 0x08, 0x01]); // 最小限のProtobufヘッダー
    }

    [Theory]
    [InlineData("hello", "▁hello")]
    [InlineData("Hello World", "▁Hello▁World")]
    [InlineData("こんにちは", "▁こんにちは")]
    [InlineData("こんにちは 世界", "▁こんにちは▁世界")]
    public void NormalizeText_BasicTexts_ShouldAddPrefixSpaceSymbol(string input, string expected)
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_tempModelPath, _mockLogger.Object);

        // Act
        var result = tokenizer.NormalizeText(input);

        // Assert
        result.Should().Be(expected);
        _output.WriteLine($"✓ Basic normalization: '{input}' -> '{result}'");
    }

    [Theory]
    [InlineData("ｈｅｌｌｏ", "▁hello")] // 全角英字 -> 半角英字
    [InlineData("１２３", "▁123")] // 全角数字 -> 半角数字
    [InlineData("Ｈｅｌｌｏ　Ｗｏｒｌｄ", "▁Hello▁World")] // 全角空白 -> 半角空白
    public void NormalizeText_UnicodeCharacters_ShouldApplyNfkcNormalization(string input, string expected)
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_tempModelPath, _mockLogger.Object);

        // Act
        var result = tokenizer.NormalizeText(input);

        // Assert
        result.Should().Be(expected);
        _output.WriteLine($"✓ NFKC normalization: '{input}' -> '{result}'");
    }

    [Theory]
    [InlineData("hello\tworld", "▁hello▁world")] // タブ -> 空白
    [InlineData("hello\nworld", "▁hello▁world")] // 改行 -> 空白
    [InlineData("hello\rworld", "▁hello▁world")] // 復帰文字 -> 空白
    [InlineData("hello\u0000world", "▁helloworld")] // NULL文字 -> 除去
    public void NormalizeText_ControlCharacters_ShouldBeProcessedCorrectly(string input, string expected)
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_tempModelPath, _mockLogger.Object);

        // Act
        var result = tokenizer.NormalizeText(input);

        // Assert
        result.Should().Be(expected);
        _output.WriteLine($"✓ Control char normalization: '{input.Replace("\u0000", "\\0")}' -> '{result}'");
    }

    [Theory(Skip = "OPUS-MTモデルファイルが必要です。scripts/download_opus_mt_models.ps1を実行してモデルをダウンロードしてください。")]
    [InlineData("hello   world", "▁hello▁world")] // 複数空白 -> 単一空白
    [InlineData("  hello  world  ", "▁hello▁world")] // 前後空白 + 複数空白
    [InlineData("hello\u3000world", "▁hello▁world")] // 全角空白 -> 半角空白
    public void NormalizeText_WhitespaceCharacters_ShouldBeNormalized(string input, string expected)
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_tempModelPath, _mockLogger.Object);

        // Act
        var result = tokenizer.NormalizeText(input);

        // Assert
        result.Should().Be(expected);
        _output.WriteLine($"✓ Whitespace normalization: '{input}' -> '{result}'");
    }

    [Fact]
    public void ValidateNormalization_WithoutParameters_ShouldReturnTrue()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_tempModelPath, _mockLogger.Object);

        // Act
        var result = tokenizer.ValidateNormalization();

        // Assert
        result.Should().BeTrue();
        _output.WriteLine("✓ Normalization validation (parameterless) passed");
    }

    [Theory]
    [InlineData("hello", "▁hello")]
    [InlineData("ｈｅｌｌｏ", "▁hello")]
    [InlineData("hello\tworld", "▁hello▁world")]
    public void ValidateNormalization_WithParameters_ShouldValidateCorrectly(string input, string expected)
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_tempModelPath, _mockLogger.Object);

        // Act
        var result = tokenizer.ValidateNormalization(input, expected);

        // Assert
        result.Should().BeTrue();
        _output.WriteLine($"✓ Normalization validation: '{input}' == '{expected}'");
    }

    [Fact]
    public void ValidateNormalization_WithIncorrectExpected_ShouldReturnFalse()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_tempModelPath, _mockLogger.Object);

        // Act
        var result = tokenizer.ValidateNormalization("hello", "wrong_result");

        // Assert
        result.Should().BeFalse();
        _output.WriteLine("✓ Normalization validation correctly failed for wrong expected result");
    }

    [Fact]
    public void Tokenize_WithNormalization_ShouldApplyNormalizationBeforeTokenizing()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_tempModelPath, _mockLogger.Object);
        const string input = "ｈｅｌｌｏ　ｗｏｒｌｄ"; // 全角文字

        // Act
        var tokens = tokenizer.Tokenize(input);

        // Assert
        tokens.Should().NotBeNull();
        tokens.Length.Should().BeGreaterThan(0);
        _output.WriteLine($"✓ Tokenization with normalization: '{input}' -> [{string.Join(", ", tokens)}]");
    }

    [Fact(Skip = "OPUS-MTモデルファイルが必要です。scripts/download_opus_mt_models.ps1を実行してモデルをダウンロードしてください。")]
    public void NormalizeText_ComplexRealWorldText_ShouldHandleCorrectly()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_tempModelPath, _mockLogger.Object);
        const string input = "Ｈｅｌｌｏ，　ｗｏｒｌｄ!\nこんにちは\t世界！";
        const string expected = "▁Hello,▁world!▁こんにちは▁世界！";

        // Act
        var result = tokenizer.NormalizeText(input);

        // Assert
        result.Should().Be(expected);
        _output.WriteLine($"✓ Complex text normalization: '{input}' -> '{result}'");
    }

    [Fact]
    public void NormalizeText_EmojisAndSpecialCharacters_ShouldPreserveCorrectly()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_tempModelPath, _mockLogger.Object);
        const string input = "Hello 😀 World! 🌍";
        const string expected = "▁Hello▁😀▁World!▁🌍";

        // Act
        var result = tokenizer.NormalizeText(input);

        // Assert
        result.Should().Be(expected);
        _output.WriteLine($"✓ Emoji normalization: '{input}' -> '{result}'");
    }

    [Fact]
    public void UnknownTokenId_ShouldReturnValidId()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_tempModelPath, _mockLogger.Object);

        // Act
        var unknownId = tokenizer.UnknownTokenId;

        // Assert
        unknownId.Should().BeGreaterThanOrEqualTo(0);
        _output.WriteLine($"✓ Unknown token ID: {unknownId}");
    }

    [Fact]
    public void NormalizeText_EmptyString_ShouldReturnEmpty()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_tempModelPath, _mockLogger.Object);

        // Act
        var result = tokenizer.NormalizeText("");

        // Assert
        result.Should().Be("");
        _output.WriteLine("✓ Empty string normalization handled correctly");
    }

    [Fact]
    public void NormalizeText_NullString_ShouldReturnEmpty()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_tempModelPath, _mockLogger.Object);

        // Act
        var result = tokenizer.NormalizeText(null!);

        // Assert
        result.Should().Be("");
        _output.WriteLine("✓ Null string normalization handled correctly");
    }

    [Fact]
    public void NormalizeText_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var tokenizer = new RealSentencePieceTokenizer(_tempModelPath, _mockLogger.Object);
        tokenizer.Dispose();

        // Act & Assert
        var action = () => tokenizer.NormalizeText("test");
        action.Should().Throw<ObjectDisposedException>();
        _output.WriteLine("✓ Disposed tokenizer correctly throws ObjectDisposedException");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (File.Exists(_tempModelPath))
        {
            try
            {
                File.Delete(_tempModelPath);
            }
            catch
            {
                // テスト環境での一時ファイル削除失敗は無視
            }
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}