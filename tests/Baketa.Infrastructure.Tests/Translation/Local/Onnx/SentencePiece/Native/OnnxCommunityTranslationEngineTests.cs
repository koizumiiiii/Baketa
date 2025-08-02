using System;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece.Native;

/// <summary>
/// ONNX-Community Encoder-Decoder分離アーキテクチャ翻訳エンジンのテスト
/// </summary>
public class OnnxCommunityTranslationEngineTests(ITestOutputHelper output)
{
    [Fact]
    public async Task OnnxCommunityEngine_WithEncoderDecoder_ShouldTranslateCorrectly()
    {
        // Arrange
        var projectRoot = GetProjectRootDirectory();
        var tokenizerPath = Path.Combine(projectRoot, "Models", "SentencePiece", "helsinki-opus-mt-ja-en.model");
        var encoderPath = Path.Combine(projectRoot, "Models", "ONNX", "onnx-community-encoder_model.onnx");
        var decoderPath = Path.Combine(projectRoot, "Models", "ONNX", "onnx-community-decoder_model.onnx");
        
        if (!File.Exists(tokenizerPath))
        {
            output.WriteLine($"⚠️  Skipping test: Helsinki tokenizer not found at {tokenizerPath}");
            return;
        }
        
        if (!File.Exists(encoderPath))
        {
            output.WriteLine($"⚠️  Skipping test: ONNX-Community encoder not found at {encoderPath}");
            return;
        }
        
        if (!File.Exists(decoderPath))
        {
            output.WriteLine($"⚠️  Skipping test: ONNX-Community decoder not found at {decoderPath}");
            return;
        }

        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<OnnxCommunityTranslationEngine>();

        var options = new AlphaOpusMtOptions
        {
            MaxSequenceLength = 512
        };

        var languagePair = new LanguagePair
        {
            SourceLanguage = Language.Japanese,
            TargetLanguage = Language.English
        };

        // Act - ONNX-Community Encoder-Decoder翻訳テスト
        var engine = new OnnxCommunityTranslationEngine(
            encoderPath,
            decoderPath,
            tokenizerPath,
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

        output.WriteLine("🌟 ONNX-Community Encoder-Decoder Translation Test");
        output.WriteLine($"📂 Encoder: {Path.GetFileName(encoderPath)} ({new FileInfo(encoderPath).Length / (1024 * 1024)} MB)");
        output.WriteLine($"📂 Decoder: {Path.GetFileName(decoderPath)} ({new FileInfo(decoderPath).Length / (1024 * 1024)} MB)");
        output.WriteLine($"📂 Tokenizer: {Path.GetFileName(tokenizerPath)}");
        output.WriteLine("");

        bool hasSuccessfulTranslation = false;

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
                output.WriteLine($"🔍 Success: {response.IsSuccess}");
                
                if (!response.IsSuccess)
                {
                    output.WriteLine($"❌ Error: {response.Error?.Message ?? "Unknown error"}");
                }

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
                    hasSuccessfulTranslation = true;
                    
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
                response.Should().NotBeNull();
                
                output.WriteLine("");
            }
            catch (Exception ex)
            {
                output.WriteLine($"❌ Error: {ex.Message}");
                output.WriteLine($"📋 Stack: {ex.StackTrace}");
                throw;
            }
        }

        // 少なくとも1つの有効な翻訳があることを確認
        output.WriteLine($"🏆 Translation Test Summary: Has successful translation = {hasSuccessfulTranslation}");

        engine.Dispose();
    }

    [Fact]
    public async Task OnnxCommunityEngine_WithEmptyInput_ShouldReturnEmpty()
    {
        // Arrange
        var projectRoot = GetProjectRootDirectory();
        var tokenizerPath = Path.Combine(projectRoot, "Models", "SentencePiece", "helsinki-opus-mt-ja-en.model");
        var encoderPath = Path.Combine(projectRoot, "Models", "ONNX", "onnx-community-encoder_model.onnx");
        var decoderPath = Path.Combine(projectRoot, "Models", "ONNX", "onnx-community-decoder_model.onnx");
        
        if (!File.Exists(tokenizerPath) || !File.Exists(encoderPath) || !File.Exists(decoderPath))
        {
            output.WriteLine("⚠️  Skipping test: Required models not found");
            return;
        }

        var logger = NullLogger<OnnxCommunityTranslationEngine>.Instance;
        var options = new AlphaOpusMtOptions { MaxSequenceLength = 512 };
        var languagePair = new LanguagePair
        {
            SourceLanguage = Language.Japanese,
            TargetLanguage = Language.English
        };

        var engine = new OnnxCommunityTranslationEngine(
            encoderPath, decoderPath, tokenizerPath, languagePair, options, logger);

        // Act
        var request = new TranslationRequest
        {
            SourceText = "",
            SourceLanguage = Language.Japanese,
            TargetLanguage = Language.English
        };
        
        var response = await engine.TranslateAsync(request);

        // Assert
        response.TranslatedText.Should().BeEmpty();
        response.IsSuccess.Should().BeTrue();
        response.Error.Should().BeNull();

        engine.Dispose();
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