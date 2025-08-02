using System;
using System.IO;
using System.Threading.Tasks;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece.Native;

/// <summary>
/// Helsinki-NLP OPUS-MTモデルとの適合性テスト
/// 最新のモデルペアによる正常動作を検証
/// </summary>
public class HelsinkiModelCompatibilityTests(ITestOutputHelper output)
{
    [Fact]
    public async Task HelsinkiOpusMtNativeTokenizer_ShouldLoadCorrectly()
    {
        // Arrange
        var projectRoot = GetProjectRootDirectory();
        var helsinkiModelPath = Path.Combine(projectRoot, "Models", "SentencePiece", "helsinki-opus-mt-ja-en.model");
        
        if (!File.Exists(helsinkiModelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Helsinki model not found at {helsinkiModelPath}");
            return;
        }

        // Act
        using var tokenizer = await OpusMtNativeTokenizer.CreateAsync(helsinkiModelPath);
        
        // Assert
        tokenizer.Should().NotBeNull();
        tokenizer.IsInitialized.Should().BeTrue();
        tokenizer.VocabularySize.Should().BeGreaterThan(0);
        
        output.WriteLine($"✅ Helsinki model loaded successfully");
        output.WriteLine($"📊 Vocabulary size: {tokenizer.VocabularySize}");
        output.WriteLine($"🏷️  Tokenizer name: {tokenizer.Name}");
    }

    [Fact]
    public async Task HelsinkiOpusMtNativeTokenizer_ShouldTokenizeCorrectly()
    {
        // Arrange
        var projectRoot = GetProjectRootDirectory();
        var helsinkiModelPath = Path.Combine(projectRoot, "Models", "SentencePiece", "helsinki-opus-mt-ja-en.model");
        
        if (!File.Exists(helsinkiModelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Helsinki model not found at {helsinkiModelPath}");
            return;
        }

        using var tokenizer = await OpusMtNativeTokenizer.CreateAsync(helsinkiModelPath);
        
        var testCases = new[]
        {
            "こんにちは、世界！",
            "これは翻訳テストです。",
            "日本語から英語への翻訳",
            "ゲームの翻訳オーバーレイ",
            "設定画面を開いてください。"
        };

        foreach (var testText in testCases)
        {
            output.WriteLine($"\n📝 Testing: '{testText}'");
            
            // Act
            var tokens = tokenizer.Tokenize(testText);
            var decoded = tokenizer.Decode(tokens) ?? string.Empty;
            
            // Assert
            tokens.Should().NotBeEmpty();
            decoded.Should().NotBeEmpty();
            decoded.Should().NotStartWith("tok_");
            
            output.WriteLine($"🔢 Tokens: [{string.Join(", ", tokens.Take(10))}]{(tokens.Length > 10 ? "..." : "")}");
            output.WriteLine($"📤 Decoded: '{decoded}'");
            output.WriteLine($"✅ Token count: {tokens.Length}");
            
            // Helsinki-NLP SentencePieceは高品質なトークン化を提供するはず
            tokens.Length.Should().BeGreaterThan(0);
            tokens.Length.Should().BeLessOrEqualTo(testText.Length * 3); // 合理的な上限
        }
    }

    [Fact]
    public async Task HelsinkiOpusMtNativeTokenizer_SpecialTokens_ShouldBeCorrect()
    {
        // Arrange
        var projectRoot = GetProjectRootDirectory();
        var helsinkiModelPath = Path.Combine(projectRoot, "Models", "SentencePiece", "helsinki-opus-mt-ja-en.model");
        
        if (!File.Exists(helsinkiModelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Helsinki model not found at {helsinkiModelPath}");
            return;
        }

        using var tokenizer = await OpusMtNativeTokenizer.CreateAsync(helsinkiModelPath);
        
        // Act & Assert
        var bosId = tokenizer.GetSpecialTokenId("BOS");
        var eosId = tokenizer.GetSpecialTokenId("EOS");
        var unkId = tokenizer.GetSpecialTokenId("UNK");
        var padId = tokenizer.GetSpecialTokenId("PAD");
        
        output.WriteLine($"🏷️  Special Token IDs:");
        output.WriteLine($"   BOS: {bosId}");
        output.WriteLine($"   EOS: {eosId}");
        output.WriteLine($"   UNK: {unkId}");
        output.WriteLine($"   PAD: {padId}");
        
        // Helsinki-NLP OPUS-MTの特殊トークンID検証
        bosId.Should().BeGreaterOrEqualTo(0);
        eosId.Should().BeGreaterOrEqualTo(0);
        unkId.Should().BeGreaterOrEqualTo(0);
        padId.Should().BeGreaterOrEqualTo(0);
        
        // 各特殊トークンIDは互いに異なるべき
        var tokenIds = new[] { bosId, eosId, unkId, padId };
        tokenIds.Distinct().Should().HaveCount(tokenIds.Length);
    }

    [Fact]
    public async Task HelsinkiVsOriginal_ModelComparison()
    {
        // Arrange
        var projectRoot = GetProjectRootDirectory();
        var helsinkiModelPath = Path.Combine(projectRoot, "Models", "SentencePiece", "helsinki-opus-mt-ja-en.model");
        var originalModelPath = Path.Combine(projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        var testText = "こんにちは、世界！";
        
        output.WriteLine("🔄 Model Comparison Test");
        output.WriteLine($"📝 Test text: '{testText}'");
        output.WriteLine("");
        
        if (File.Exists(helsinkiModelPath))
        {
            using var helsinkiTokenizer = await OpusMtNativeTokenizer.CreateAsync(helsinkiModelPath);
            var helsinkiTokens = helsinkiTokenizer.Tokenize(testText);
            var helsinkiDecoded = helsinkiTokenizer.Decode(helsinkiTokens) ?? string.Empty;
            
            output.WriteLine($"🌟 Helsinki Model:");
            output.WriteLine($"   Vocabulary: {helsinkiTokenizer.VocabularySize}");
            output.WriteLine($"   Tokens: [{string.Join(", ", helsinkiTokens.Take(10))}...]");
            output.WriteLine($"   Decoded: '{helsinkiDecoded}'");
            output.WriteLine("");
        }
        else
        {
            output.WriteLine("⚠️  Helsinki model not found");
        }
        
        if (File.Exists(originalModelPath))
        {
            using var originalTokenizer = await OpusMtNativeTokenizer.CreateAsync(originalModelPath);
            var originalTokens = originalTokenizer.Tokenize(testText);
            var originalDecoded = originalTokenizer.Decode(originalTokens) ?? string.Empty;
            
            output.WriteLine($"📦 Original Model:");
            output.WriteLine($"   Vocabulary: {originalTokenizer.VocabularySize}");
            output.WriteLine($"   Tokens: [{string.Join(", ", originalTokens.Take(10))}...]");
            output.WriteLine($"   Decoded: '{originalDecoded}'");
        }
        else
        {
            output.WriteLine("⚠️  Original model not found");
        }
        
        // 少なくとも1つのモデルが利用可能であることを確認
        (File.Exists(helsinkiModelPath) || File.Exists(originalModelPath)).Should().BeTrue();
    }
    
    private static string GetProjectRootDirectory()
    {
        var currentDir = Directory.GetCurrentDirectory();
        while (currentDir != null && !File.Exists(Path.Combine(currentDir, "Baketa.sln")))
        {
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }
        return currentDir ?? throw new DirectoryNotFoundException("Project root not found");
    }
}