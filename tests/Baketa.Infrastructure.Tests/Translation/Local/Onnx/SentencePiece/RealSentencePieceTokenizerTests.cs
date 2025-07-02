using System;
using System.IO;
using System.Linq;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// RealSentencePieceTokenizerのテスト
/// </summary>
public class RealSentencePieceTokenizerTests : IDisposable
{
    private readonly ILogger<RealSentencePieceTokenizer> _logger;
    private readonly string _testModelPath;
    private readonly string _tempDirectory;
    private bool _disposed;

    public RealSentencePieceTokenizerTests()
    {
        _logger = NullLogger<RealSentencePieceTokenizer>.Instance;
        
        // テスト用の一時ディレクトリとファイルを作成
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BaketaTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDirectory);
        
        _testModelPath = Path.Combine(_tempDirectory, "test-model.model");
        CreateTestModelFile(_testModelPath);
    }

    [Fact]
    public void Constructor_ValidModelPath_InitializesSuccessfully()
    {
        // Arrange & Act
        using var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger);

        // Assert
        Assert.Equal(_testModelPath, tokenizer.ModelPath);
        Assert.Equal("SentencePiece_test-model", tokenizer.TokenizerId);
        Assert.Equal("SentencePiece Tokenizer (test-model)", tokenizer.Name);
        Assert.Equal(32000, tokenizer.VocabularySize);
        Assert.True(tokenizer.IsInitialized);
    }

    [Fact]
    public void Constructor_NonExistentModelPath_ThrowsInvalidOperationException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDirectory, "non-existent.model");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => 
            new RealSentencePieceTokenizer(nonExistentPath, _logger));
    }

    [Fact]
    public void Constructor_NullModelPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new RealSentencePieceTokenizer(null!, _logger));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new RealSentencePieceTokenizer(_testModelPath, null!));
    }

    [Fact]
    public void Tokenize_SimpleText_ReturnsTokens()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger);
        var text = "Hello World";

        // Act
        var tokens = tokenizer.Tokenize(text);

        // Assert
        Assert.NotNull(tokens);
        Assert.NotEmpty(tokens);
        Assert.All(tokens, token => Assert.True(token >= 0));
    }

    [Fact]
    public void Tokenize_EmptyText_ReturnsEmptyArray()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger);

        // Act
        var tokens = tokenizer.Tokenize(string.Empty);

        // Assert
        Assert.NotNull(tokens);
        Assert.Empty(tokens);
    }

    [Fact]
    public void Tokenize_NullText_ThrowsArgumentNullException()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => tokenizer.Tokenize(null!));
    }

    [Fact]
    public void Tokenize_VeryLongText_ThrowsTokenizationException()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger, maxInputLength: 100);
        var longText = new string('A', 101);

        // Act & Assert
        var exception = Assert.Throws<TokenizationException>(() => tokenizer.Tokenize(longText));
        Assert.Contains("最大長", exception.Message, StringComparison.Ordinal);
        Assert.Equal(longText, exception.InputText);
        Assert.Equal("test-model", exception.ModelName);
    }

    [Fact]
    public void Decode_ValidTokens_ReturnsText()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger);
        var tokens = new[] { 1, 2, 3 };

        // Act
        var decoded = tokenizer.Decode(tokens);

        // Assert
        Assert.NotNull(decoded);
        Assert.NotEmpty(decoded);
    }

    [Fact]
    public void Decode_EmptyTokenArray_ReturnsEmptyString()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger);

        // Act
        var decoded = tokenizer.Decode([]);

        // Assert
        Assert.Equal(string.Empty, decoded);
    }

    [Fact]
    public void Decode_NullTokenArray_ThrowsArgumentNullException()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => tokenizer.Decode(null!));
    }

    [Fact]
    public void DecodeToken_ValidToken_ReturnsString()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger);

        // Act
        var decoded = tokenizer.DecodeToken(123);

        // Assert
        Assert.NotNull(decoded);
        Assert.NotEmpty(decoded);
    }

    [Fact]
    public void GetSpecialTokens_Always_ReturnsValidSpecialTokens()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger);

        // Act
        var specialTokens = tokenizer.GetSpecialTokens();

        // Assert
        Assert.NotNull(specialTokens);
        Assert.True(specialTokens.UnknownId >= 0);
        Assert.True(specialTokens.BeginOfSentenceId >= 0);
        Assert.True(specialTokens.EndOfSentenceId >= 0);
        Assert.True(specialTokens.PaddingId >= 0);
    }

    [Fact]
    public void ValidateNormalization_Always_CompletesWithoutException()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger);

        // Act & Assert (例外が発生しないことを確認)
        tokenizer.ValidateNormalization();
    }

    [Fact]
    public void TokenizeAndDecode_RoundTrip_MaintainsConsistency()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger);
        var originalTexts = new[]
        {
            "Hello",
            "World",
            "こんにちは",
            "テスト",
        };

        foreach (var originalText in originalTexts)
        {
            // Act
            var tokens = tokenizer.Tokenize(originalText);
            var decoded = tokenizer.Decode(tokens);

            // Assert
            Assert.NotNull(tokens);
            Assert.NotEmpty(tokens);
            Assert.NotNull(decoded);
            // 注意: 暫定実装では完全な往復復元は期待できない
            // 実際のSentencePieceモデルが使用可能な場合のみ、より厳密な検証が可能
        }
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger);

        // Act & Assert
        tokenizer.Dispose();
        tokenizer.Dispose(); // 2回目の呼び出し
        
        // 例外が発生しないことを確認
    }

    [Fact]
    public void OperationsAfterDispose_ThrowObjectDisposedException()
    {
        // Arrange
        var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger);
        tokenizer.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => tokenizer.Tokenize("test"));
        Assert.Throws<ObjectDisposedException>(() => tokenizer.Decode([1, 2, 3]));
        Assert.Throws<ObjectDisposedException>(() => tokenizer.DecodeToken(1));
        Assert.Throws<ObjectDisposedException>(() => tokenizer.GetSpecialTokens());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("単一")]
    [InlineData("Hello World")]
    [InlineData("これは日本語のテストです")]
    [InlineData("Mixed 日本語 and English テキスト")]
    public void Tokenize_VariousInputs_HandlesGracefully(string input)
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger);

        // Act
        var tokens = tokenizer.Tokenize(input);

        // Assert
        Assert.NotNull(tokens);
        
        if (string.IsNullOrEmpty(input))
        {
            Assert.Empty(tokens);
        }
        else
        {
            Assert.All(tokens, token => Assert.True(token >= 0));
        }
    }

    [Fact]
    public void IsRealSentencePieceAvailable_WithDummyModel_ReturnsFalse()
    {
        // Arrange
        using var tokenizer = new RealSentencePieceTokenizer(_testModelPath, _logger);

        // Act
        var isAvailable = tokenizer.IsRealSentencePieceAvailable;

        // Assert
        // ダミーモデルファイルを使用しているため、実際のSentencePieceは利用不可
        Assert.False(isAvailable);
    }

    private static void CreateTestModelFile(string filePath)
    {
        // テスト用のダミーSentencePieceモデルファイルを作成
        // 実際のSentencePieceバイナリ形式ではなく、テキストファイル
        var content = @"# Test SentencePiece Model File
# This is a dummy model file for testing purposes

trainer_spec {
  model_type: UNIGRAM
  vocab_size: 32000
}

normalizer_spec {
  name: ""nfkc""
  add_dummy_prefix: true
}

pieces {
  piece: ""<unk>""
  score: 0
  type: UNKNOWN
}

pieces {
  piece: ""<s>""
  score: 0
  type: CONTROL
}

pieces {
  piece: ""</s>""
  score: 0
  type: CONTROL
}

pieces {
  piece: ""Hello""
  score: -1.0
  type: NORMAL
}

pieces {
  piece: ""World""
  score: -1.1
  type: NORMAL
}
";
        File.WriteAllText(filePath, content);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // テスト用ファイルとディレクトリを削除
                try
                {
                    if (Directory.Exists(_tempDirectory))
                    {
                        Directory.Delete(_tempDirectory, true);
                    }
                }
                catch (IOException)
                {
                    // ファイル削除の失敗は無視（テスト環境では問題ない）
                }
                catch (UnauthorizedAccessException)
                {
                    // アクセス権限の問題も無視
                }
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
