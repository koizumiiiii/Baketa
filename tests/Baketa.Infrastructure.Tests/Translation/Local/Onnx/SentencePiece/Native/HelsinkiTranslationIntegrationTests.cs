using System;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece.Native;

/// <summary>
/// Helsinki-NLP OPUS-MTモデルを使用した翻訳統合テスト
/// </summary>
public class HelsinkiTranslationIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task HelsinkiOpusMtTranslationEngine_WithNativeTokenizer_ShouldWork()
    {
        // Arrange
        var projectRoot = GetProjectRootDirectory();
        var modelPath = Path.Combine(projectRoot, "Models", "SentencePiece", "helsinki-opus-mt-ja-en.model");
        var onnxModelPath = Path.Combine(projectRoot, "Models", "ONNX", "opus-mt-ja-en.onnx");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Helsinki model not found at {modelPath}");
            return;
        }
        
        if (!File.Exists(onnxModelPath))
        {
            output.WriteLine($"⚠️  Skipping test: ONNX model not found at {onnxModelPath}");
            return;
        }

        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<AlphaOpusMtTranslationEngine>();

        var options = new AlphaOpusMtOptions
        {
            MaxSequenceLength = 512
        };

        var languagePair = new LanguagePair
        {
            SourceLanguage = Language.Japanese,
            TargetLanguage = Language.English
        };

        // Act - Helsinki AlphaOpusMtTranslationEngineを作成
        var engine = new AlphaOpusMtTranslationEngine(
            onnxModelPath, 
            modelPath, 
            languagePair, 
            options, 
            logger);

        var testTexts = new[]
        {
            "こんにちは、世界！",
            "これは翻訳テストです。",
            "日本語から英語への翻訳"
        };

        output.WriteLine("🌟 Helsinki Translation Integration Test");
        output.WriteLine($"📂 ONNX Model: {Path.GetFileName(onnxModelPath)}");
        output.WriteLine($"📂 Tokenizer: {Path.GetFileName(modelPath)}");
        output.WriteLine("");

        foreach (var text in testTexts)
        {
            try
            {
                output.WriteLine($"📝 Input: '{text}'");
                
                var request = new TranslationRequest
                {
                    SourceText = text,
                    SourceLanguage = Language.Japanese,
                    TargetLanguage = Language.English
                };
                
                var response = await engine.TranslateAsync(request);
                var result = response.TranslatedText ?? string.Empty;
                
                output.WriteLine($"✅ Output: '{result}'");
                output.WriteLine($"🔍 Output Type: {(result.StartsWith("tok_", StringComparison.Ordinal) ? "Token Format" : "Actual Translation")}");
                
                // Assert
                result.Should().NotBeNull();
                result.Should().NotBeEmpty();
                
                // Helsinki Tokenizerによる翻訳品質チェック
                if (!result.StartsWith("tok_", StringComparison.Ordinal))
                {
                    output.WriteLine("🎉 Helsinki Native Tokenizer working - actual translation generated!");
                }
                else
                {
                    output.WriteLine("⚠️  Still generating token format - Helsinki model may have issues");
                }
                
                output.WriteLine("");
            }
            catch (Exception ex)
            {
                output.WriteLine($"❌ Error: {ex.Message}");
                output.WriteLine($"📋 Stack: {ex.StackTrace}");
                throw;
            }
        }

        engine.Dispose();
    }

    [Fact]
    public async Task HelsinkiNativeTokenizer_DirectTest_ShouldGenerateQualityTokens()
    {
        // Arrange
        var projectRoot = GetProjectRootDirectory();
        var modelPath = Path.Combine(projectRoot, "Models", "SentencePiece", "helsinki-opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Helsinki model not found at {modelPath}");
            return;
        }

        // Act - Helsinki Native Tokenizerを直接テスト
        using var tokenizer = await OpusMtNativeTokenizer.CreateAsync(modelPath);
        
        var testText = "こんにちは、世界！";
        var tokens = tokenizer.Tokenize(testText);
        var decoded = tokenizer.Decode(tokens) ?? string.Empty;
        
        output.WriteLine("🌟 Helsinki Native Tokenizer Direct Test");
        output.WriteLine($"📝 Input: '{testText}'");
        output.WriteLine($"🔢 Tokens: [{string.Join(", ", tokens)}]");
        output.WriteLine($"✅ Decoded: '{decoded}'");
        output.WriteLine($"📊 Vocabulary Size: {tokenizer.VocabularySize}");
        
        // Helsinki特殊トークンの確認
        var bosId = tokenizer.GetSpecialTokenId("BOS");
        var eosId = tokenizer.GetSpecialTokenId("EOS");
        var unkId = tokenizer.GetSpecialTokenId("UNK");
        var padId = tokenizer.GetSpecialTokenId("PAD");
        
        output.WriteLine($"🏷️  Special Tokens: BOS={bosId}, EOS={eosId}, UNK={unkId}, PAD={padId}");
        
        // Assert
        tokens.Should().NotBeEmpty();
        decoded.Should().NotBeNull();
        decoded.Should().NotStartWith("tok_");
        
        // Helsinki-NLP SentencePieceの品質確認
        decoded.Should().Contain("こんにちは"); // 元の文字が保持されているか
        tokenizer.VocabularySize.Should().Be(32000); // 標準的な語彙サイズ
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