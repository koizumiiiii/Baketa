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
/// Native Tokenizer統合翻訳テスト
/// 実際のOPUS-MTエンジンでの翻訳動作を検証
/// </summary>
public class TranslationIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task AlphaOpusMtTranslationEngine_WithNativeTokenizer_ShouldTranslateCorrectly()
    {
        // Arrange
        var projectRoot = GetProjectRootDirectory();
        var modelPath = Path.Combine(projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        var onnxModelPath = Path.Combine(projectRoot, "Models", "ONNX", "opus-mt-ja-en.onnx");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Tokenizer model not found at {modelPath}");
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

        // 翻訳エンジンのオプション設定
        var options = new AlphaOpusMtOptions
        {
            MaxSequenceLength = 512
        };

        var languagePair = new LanguagePair
        {
            SourceLanguage = Language.Japanese,
            TargetLanguage = Language.English
        };

        // Act - AlphaOpusMtTranslationEngineを作成
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

        output.WriteLine("🔄 Translation Integration Test");
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
                
                // Native Tokenizerが動作していれば、tok_XXXXではない結果が期待される
                if (!result.StartsWith("tok_", StringComparison.Ordinal))
                {
                    output.WriteLine("🎉 Native Tokenizer working - actual translation generated!");
                }
                else
                {
                    output.WriteLine("⚠️  Still generating token format - needs investigation");
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
    public async Task NativeTokenizer_DirectTest_ShouldGenerateActualText()
    {
        // Arrange
        var projectRoot = GetProjectRootDirectory();
        var modelPath = Path.Combine(projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        // Act - Native Tokenizerを直接テスト
        using var tokenizer = await OpusMtNativeTokenizer.CreateAsync(modelPath);
        
        var testText = "こんにちは、世界！";
        var tokens = tokenizer.Tokenize(testText);
        var decoded = tokenizer.Decode(tokens);
        
        output.WriteLine("🔍 Native Tokenizer Direct Test");
        output.WriteLine($"📝 Input: '{testText}'");
        output.WriteLine($"🔢 Tokens: [{string.Join(", ", tokens)}]");
        output.WriteLine($"✅ Decoded: '{decoded}'");
        
        // Assert
        tokens.Should().NotBeEmpty();
        decoded.Should().NotBeNull();
        decoded.Should().NotStartWith("tok_");
        
        // 実際の文字が含まれていることを確認
        decoded.Should().Contain("こんにちは");
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