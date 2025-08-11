using System;
using System.IO;
using System.Linq;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// ImprovedSentencePieceTokenizerのテスト
/// </summary>
public class ImprovedSentencePieceTokenizerTests : SentencePieceTestBase, IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<ImprovedSentencePieceTokenizer> _logger;
    private readonly string _testModelPath;
    private readonly string _tempDirectory;
    private bool _disposed;

    // テスト用の定数配列（CA1861警告対応）
    private static readonly int[][] TestTokenArrays =
    [
        [1, 2, 3],
        [100, 200, 300, 400],
        [1000],
        [5000, 10000, 15000]
    ];

    private static readonly int[] TestTokens = [123, 456, 789, 1000];
    private static readonly int[] TestTokensForDispose = [1, 2, 3];

    public ImprovedSentencePieceTokenizerTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = NullLogger<ImprovedSentencePieceTokenizer>.Instance;
        
        // テスト用の一時ディレクトリとファイルを作成
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BaketaImprovedTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDirectory);
        
        _testModelPath = Path.Combine(_tempDirectory, "improved-test-model.model");
        CreateTestModelFile(_testModelPath);
        
        _output.WriteLine($"🧪 改良版SentencePieceTokenizerテスト開始");
        _output.WriteLine($"📁 テストディレクトリ: {_tempDirectory}");
    }

    [Fact]
    public void Constructor_ValidModelPath_InitializesSuccessfully()
    {
        // Arrange & Act
        using var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);

        // Assert
        Assert.Equal(_testModelPath, tokenizer.ModelPath);
        Assert.Equal("ImprovedSentencePiece_improved-test-model", tokenizer.TokenizerId);
        Assert.Equal("Improved SentencePiece Tokenizer (improved-test-model)", tokenizer.Name);
        Assert.Equal(32000, tokenizer.VocabularySize);
        Assert.True(tokenizer.IsInitialized);
        
        _output.WriteLine($"✅ 基本プロパティの確認完了");
        _output.WriteLine($"   TokenizerId: {tokenizer.TokenizerId}");
        _output.WriteLine($"   Name: {tokenizer.Name}");
        _output.WriteLine($"   VocabularySize: {tokenizer.VocabularySize}");
        _output.WriteLine($"   IsRealSentencePieceAvailable: {tokenizer.IsRealSentencePieceAvailable}");
    }

    [Fact]
    public void Constructor_NonExistentModelPath_ThrowsInvalidOperationException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDirectory, "non-existent.model");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => 
            new ImprovedSentencePieceTokenizer(nonExistentPath, _logger));
        
        _output.WriteLine("✅ 存在しないファイルでの例外処理確認");
    }

    [Fact]
    public void IsRealSentencePieceAvailable_WithDummyModel_ReturnsFalse()
    {
        // Arrange
        using var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);

        // Act
        var isAvailable = tokenizer.IsRealSentencePieceAvailable;

        // Assert
        // ダミーモデルファイルを使用しているため、実際のSentencePieceは利用不可
        Assert.False(isAvailable);
        
        _output.WriteLine($"✅ SentencePiece利用可能性チェック: {isAvailable}");
    }

    [Fact]
    public void Tokenize_FallbackMode_ReturnsTokens()
    {
        // Arrange
        using var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);
        string[] testTexts =
        [
            "Hello World",
            "Test tokenization",
            "日本語テキスト",
            "Mixed English and 日本語"
        ];

        foreach (var text in testTexts)
        {
            // Act
            var tokens = tokenizer.Tokenize(text);

            // Assert
            Assert.NotNull(tokens);
            Assert.NotEmpty(tokens);
            Assert.All(tokens, token => Assert.True(token >= 0 && token < tokenizer.VocabularySize));
            
            _output.WriteLine($"✅ '{text}' → [{string.Join(", ", tokens)}] ({tokens.Length} tokens)");
        }
    }

    [Fact]
    public void Decode_FallbackMode_ReturnsText()
    {
        // Arrange
        using var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);

        foreach (var tokens in TestTokenArrays)
        {
            // Act
            var decoded = tokenizer.Decode(tokens);

            // Assert
            Assert.NotNull(decoded);
            Assert.NotEmpty(decoded);
            
            _output.WriteLine($"✅ [{string.Join(", ", tokens)}] → '{decoded}'");
        }
    }

    [Fact]
    public void RoundTrip_FallbackMode_MaintainsConsistency()
    {
        // Arrange
        using var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);
        string[] originalTexts =
        [
            "Hello",
            "World",
            "Test",
            "Tokenization"
        ];

        foreach (var originalText in originalTexts)
        {
            // Act
            var tokens = tokenizer.Tokenize(originalText);
            var decoded = tokenizer.Decode(tokens);

            // Assert
            Assert.NotNull(tokens);
            Assert.NotEmpty(tokens);
            Assert.NotNull(decoded);
            
            // フォールバック実装では完全な復元は期待できないが、
            // 基本的な一貫性を確認（同じ入力に対して同じ出力が得られること）
            var reTokenized = tokenizer.Tokenize(originalText);
            Assert.Equal(tokens, reTokenized);
            
            _output.WriteLine($"✅ '{originalText}' → {tokens.Length} tokens → '{decoded}' → 一貫性確認");
        }
    }

    [Fact]
    public void DecodeToken_SingleToken_ReturnsString()
    {
        // Arrange
        using var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);

        foreach (var token in TestTokens)
        {
            // Act
            var decoded = tokenizer.DecodeToken(token);

            // Assert
            Assert.NotNull(decoded);
            Assert.NotEmpty(decoded);
            
            _output.WriteLine($"✅ Token {token} → '{decoded}'");
        }
    }

    [Fact]
    public void GetSpecialTokens_Always_ReturnsValidTokens()
    {
        // Arrange
        using var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);

        // Act
        var specialTokens = tokenizer.GetSpecialTokens();

        // Assert
        Assert.NotNull(specialTokens);
        Assert.True(specialTokens.UnknownId >= 0);
        Assert.True(specialTokens.BeginOfSentenceId >= 0);
        Assert.True(specialTokens.EndOfSentenceId >= 0);
        Assert.True(specialTokens.PaddingId >= 0);
        
        _output.WriteLine($"✅ 特殊トークン:");
        _output.WriteLine($"   <unk>: {specialTokens.UnknownId}");
        _output.WriteLine($"   <s>: {specialTokens.BeginOfSentenceId}");
        _output.WriteLine($"   </s>: {specialTokens.EndOfSentenceId}");
        _output.WriteLine($"   <pad>: {specialTokens.PaddingId}");
    }

    [Fact]
    public void Tokenize_EmptyText_ReturnsEmptyArray()
    {
        // Arrange
        using var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);

        // Act
        var tokens = tokenizer.Tokenize(string.Empty);

        // Assert
        Assert.NotNull(tokens);
        Assert.Empty(tokens);
        
        _output.WriteLine("✅ 空文字列のトークン化確認");
    }

    [Fact]
    public void Tokenize_VeryLongText_ThrowsTokenizationException()
    {
        // Arrange
        using var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger, maxInputLength: 100);
        var longText = new string('A', 101);

        // Act & Assert
        var exception = Assert.Throws<TokenizationException>(() => tokenizer.Tokenize(longText));
        Assert.Contains("最大長", exception.Message, StringComparison.Ordinal);
        Assert.Equal(longText, exception.InputText);
        Assert.Equal("improved-test-model", exception.ModelName);
        
        _output.WriteLine("✅ 長すぎるテキストでの例外処理確認");
    }

    [Fact]
    public void Tokenize_NullText_ThrowsArgumentNullException()
    {
        // Arrange
        using var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => tokenizer.Tokenize(null!));
        
        _output.WriteLine("✅ null入力での例外処理確認");
    }

    [Fact]
    public void Decode_NullTokenArray_ThrowsArgumentNullException()
    {
        // Arrange
        using var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => tokenizer.Decode(null!));
        
        _output.WriteLine("✅ null配列でのデコード例外処理確認");
    }

    [Fact(Skip = "OPUS-MTモデルファイルが必要です。scripts/download_opus_mt_models.ps1を実行してモデルをダウンロードしてください。")]
    public void CompareWithRealImplementation_SameBehavior()
    {
        // Arrange
        using var improvedTokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);
        var realLogger = NullLogger<RealSentencePieceTokenizer>.Instance;
        using var realTokenizer = new RealSentencePieceTokenizer(_testModelPath, realLogger);
        
        string[] testTexts =
        [
            "Hello World",
            "Test comparison",
            "日本語テスト"
        ];

        foreach (var text in testTexts)
        {
            // Act
            var improvedTokens = improvedTokenizer.Tokenize(text);
            var realTokens = realTokenizer.Tokenize(text);
            
            var improvedDecoded = improvedTokenizer.Decode(improvedTokens);
            var realDecoded = realTokenizer.Decode(realTokens);

            // Assert - フォールバック実装同士なので同じ結果が期待される
            Assert.Equal(realTokens, improvedTokens);
            Assert.Equal(realDecoded, improvedDecoded);
            
            _output.WriteLine($"✅ 実装比較 '{text}': 同一結果確認");
        }
    }

    [Fact]
    public void PerformanceComparison_MeasuresLatency()
    {
        // Arrange
        using var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);
        var testText = "Performance test text for latency measurement";
        var iterations = 100;

        // Warmup
        for (int i = 0; i < 10; i++)
        {
            _ = tokenizer.Tokenize(testText);
        }

        // Act - 測定
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        for (int i = 0; i < iterations; i++)
        {
            _ = tokenizer.Tokenize(testText);
        }
        
        stopwatch.Stop();

        // Assert & Report
        var avgLatencyMs = (double)stopwatch.ElapsedMilliseconds / iterations;
        
        _output.WriteLine($"✅ パフォーマンス測定:");
        _output.WriteLine($"   総時間: {stopwatch.ElapsedMilliseconds}ms ({iterations} 回実行)");
        _output.WriteLine($"   平均レイテンシ: {avgLatencyMs:F2}ms");
        
        // フォールバック実装なので比較的高速であることを確認
        Assert.True(avgLatencyMs < 10, $"平均レイテンシが10msを超えています: {avgLatencyMs:F2}ms");
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);

        // Act & Assert
        tokenizer.Dispose();
        tokenizer.Dispose(); // 2回目の呼び出し
        
        _output.WriteLine("✅ 複数回Dispose呼び出し確認");
    }

    [Fact]
    public void OperationsAfterDispose_ThrowObjectDisposedException()
    {
        // Arrange
        var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);
        tokenizer.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => tokenizer.Tokenize("test"));
        Assert.Throws<ObjectDisposedException>(() => tokenizer.Decode(TestTokensForDispose));
        Assert.Throws<ObjectDisposedException>(() => tokenizer.DecodeToken(1));
        Assert.Throws<ObjectDisposedException>(() => tokenizer.GetSpecialTokens());
        
        _output.WriteLine("✅ Dispose後の操作例外処理確認");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("単一")]
    [InlineData("Hello World")]
    [InlineData("これは日本語のテストです")]
    [InlineData("Mixed 日本語 and English テキスト")]
    [InlineData("Numbers 123 and symbols !@#$%")]
    public void Tokenize_VariousInputs_HandlesGracefully(string input)
    {
        // Arrange
        using var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);

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
            Assert.All(tokens, token => Assert.True(token >= 0 && token < tokenizer.VocabularySize));
        }
        
        _output.WriteLine($"✅ 多様な入力テスト '{input}': {tokens.Length} tokens");
    }

    [Fact]
    public void EdgeCases_LargeTokenArrays_HandlesCorrectly()
    {
        // Arrange
        using var tokenizer = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);
        var largeTokenArray = Enumerable.Range(0, 1000).ToArray();

        // Act
        var decoded = tokenizer.Decode(largeTokenArray);

        // Assert
        Assert.NotNull(decoded);
        Assert.NotEmpty(decoded);
        
        _output.WriteLine($"✅ 大きなトークン配列テスト: {largeTokenArray.Length} tokens → {decoded.Length} chars");
    }

    [Fact]
    public void TokenizerFactory_CanCreateMultipleInstances()
    {
        // Arrange & Act
        ImprovedSentencePieceTokenizer[] tokenizers = new ImprovedSentencePieceTokenizer[5];
        
        try
        {
            for (int i = 0; i < tokenizers.Length; i++)
            {
                tokenizers[i] = new ImprovedSentencePieceTokenizer(_testModelPath, _logger);
            }

            // Assert
            for (int i = 0; i < tokenizers.Length; i++)
            {
                Assert.True(tokenizers[i].IsInitialized);
                
                // 各インスタンスが独立して動作することを確認
                var tokens = tokenizers[i].Tokenize($"Test {i}");
                Assert.NotNull(tokens);
                Assert.NotEmpty(tokens);
            }
            
            _output.WriteLine($"✅ 複数インスタンス作成テスト: {tokenizers.Length} instances");
        }
        finally
        {
            // リソースクリーンアップ
            foreach (var tokenizer in tokenizers)
            {
                tokenizer?.Dispose();
            }
        }
    }

    private static void CreateTestModelFile(string filePath)
    {
        var content = @"# Improved Test SentencePiece Model
trainer_spec {
  model_type: UNIGRAM
  vocab_size: 32000
}
normalizer_spec {
  name: ""nfkc""
  add_dummy_prefix: true
}
pieces { piece: ""<unk>"" score: 0 type: UNKNOWN }
pieces { piece: ""<s>"" score: 0 type: CONTROL }
pieces { piece: ""</s>"" score: 0 type: CONTROL }
pieces { piece: ""<pad>"" score: 0 type: CONTROL }
pieces { piece: ""Hello"" score: -1.0 type: NORMAL }
pieces { piece: ""World"" score: -1.1 type: NORMAL }
pieces { piece: ""Test"" score: -1.2 type: NORMAL }
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
                    // ファイル削除の失敗は無視
                }
                catch (UnauthorizedAccessException)
                {
                    // アクセス権限の問題も無視
                }
                
                _output.WriteLine("🏁 改良版SentencePieceTokenizerテスト完了");
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
