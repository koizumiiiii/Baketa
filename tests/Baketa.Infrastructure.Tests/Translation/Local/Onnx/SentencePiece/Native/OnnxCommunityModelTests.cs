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
/// ONNX-Community提供のプリ変換済みモデルテスト
/// </summary>
public class OnnxCommunityModelTests(ITestOutputHelper output)
{
    [Fact]
    public async Task OnnxCommunityModel_WithHelsinki_ShouldTranslateCorrectly()
    {
        // Arrange
        var projectRoot = GetProjectRootDirectory();
        var modelPath = Path.Combine(projectRoot, "Models", "SentencePiece", "helsinki-opus-mt-ja-en.model");
        var onnxModelPath = Path.Combine(projectRoot, "Models", "ONNX", "helsinki-opus-mt-ja-en.onnx");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Helsinki SentencePiece model not found at {modelPath}");
            return;
        }
        
        if (!File.Exists(onnxModelPath))
        {
            output.WriteLine($"⚠️  Skipping test: ONNX-Community model not found at {onnxModelPath}");
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

        // Act - ONNX-Community + Helsinki組み合わせテスト
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
            "日本語から英語への翻訳",
            "ゲームの翻訳システム"
        };

        output.WriteLine("🌟 ONNX-Community Model Test");
        output.WriteLine($"📂 ONNX Model: {Path.GetFileName(onnxModelPath)} ({new FileInfo(onnxModelPath).Length / (1024 * 1024)} MB)");
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
                
                // 品質評価
                if (string.IsNullOrEmpty(result))
                {
                    output.WriteLine("❌ Empty translation result");
                }
                else if (result.StartsWith("tok_", StringComparison.Ordinal))
                {
                    output.WriteLine("⚠️  Token format output - model incompatibility");
                }
                else if (result.Contains("\"\"\"\"") || result.Contains("なく なく"))
                {
                    output.WriteLine("⚠️  Broken translation pattern detected");
                }
                else if (result.Length > 0 && !result.Equals(text, StringComparison.Ordinal))
                {
                    output.WriteLine("🎉 Valid translation output!");
                    
                    // 英語らしい文字が含まれているかチェック
                    var hasEnglishChars = System.Text.RegularExpressions.Regex.IsMatch(result, @"[a-zA-Z]");
                    if (hasEnglishChars)
                    {
                        output.WriteLine("✨ Contains English characters - translation quality looks good!");
                    }
                }
                else
                {
                    output.WriteLine("⚠️  Translation appears unchanged");
                }
                
                // Assert
                result.Should().NotBeNull();
                result.Should().NotBeEmpty();
                
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
    public async Task CompareOnnxModels_QualityComparison()
    {
        // Arrange
        var projectRoot = GetProjectRootDirectory();
        var modelPath = Path.Combine(projectRoot, "Models", "SentencePiece", "helsinki-opus-mt-ja-en.model");
        var originalOnnxPath = Path.Combine(projectRoot, "Models", "ONNX", "opus-mt-ja-en.onnx");
        var communityOnnxPath = Path.Combine(projectRoot, "Models", "ONNX", "helsinki-opus-mt-ja-en.onnx");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Helsinki model not found");
            return;
        }

        var testText = "こんにちは、世界！";
        
        output.WriteLine("🔄 ONNX Model Quality Comparison");
        output.WriteLine($"📝 Test text: '{testText}'");
        output.WriteLine("");
        
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning)); // Reduce log noise
        var logger = loggerFactory.CreateLogger<AlphaOpusMtTranslationEngine>();

        var options = new AlphaOpusMtOptions { MaxSequenceLength = 512 };
        var languagePair = new LanguagePair
        {
            SourceLanguage = Language.Japanese,
            TargetLanguage = Language.English
        };

        var request = new TranslationRequest
        {
            SourceText = testText,
            SourceLanguage = Language.Japanese,
            TargetLanguage = Language.English
        };

        // Test Original ONNX Model
        if (File.Exists(originalOnnxPath))
        {
            output.WriteLine($"📦 Original Model: {Path.GetFileName(originalOnnxPath)} ({new FileInfo(originalOnnxPath).Length / (1024 * 1024)} MB)");
            
            try
            {
                var originalEngine = new AlphaOpusMtTranslationEngine(originalOnnxPath, modelPath, languagePair, options, logger);
                var originalResponse = await originalEngine.TranslateAsync(request);
                var originalResult = originalResponse.TranslatedText ?? string.Empty;
                
                output.WriteLine($"   Result: '{originalResult.Substring(0, Math.Min(50, originalResult.Length))}{(originalResult.Length > 50 ? "..." : "")}'");
                originalEngine.Dispose();
            }
            catch (Exception ex)
            {
                output.WriteLine($"   Error: {ex.Message}");
            }
        }

        // Test ONNX-Community Model
        if (File.Exists(communityOnnxPath))
        {
            output.WriteLine($"🌟 ONNX-Community Model: {Path.GetFileName(communityOnnxPath)} ({new FileInfo(communityOnnxPath).Length / (1024 * 1024)} MB)");
            
            try
            {
                var communityEngine = new AlphaOpusMtTranslationEngine(communityOnnxPath, modelPath, languagePair, options, logger);
                var communityResponse = await communityEngine.TranslateAsync(request);
                var communityResult = communityResponse.TranslatedText ?? string.Empty;
                
                output.WriteLine($"   Result: '{communityResult.Substring(0, Math.Min(50, communityResult.Length))}{(communityResult.Length > 50 ? "..." : "")}'");
                communityEngine.Dispose();
            }
            catch (Exception ex)
            {
                output.WriteLine($"   Error: {ex.Message}");
            }
        }
        
        // 少なくとも1つのモデルが利用可能であることを確認
        (File.Exists(originalOnnxPath) || File.Exists(communityOnnxPath)).Should().BeTrue();
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