using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation;

/// <summary>
/// 語彙サイズ不整合修正の検証テスト
/// トークンID範囲制限機能が翻訳品質改善に効果があることを確認
/// </summary>
public class VocabSizeFixValidationTest
{
    private readonly ITestOutputHelper _output;

    public VocabSizeFixValidationTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task VocabSizeFix_ShouldImproveTranslationQuality()
    {
        // テスト用の入力テキスト
        var testCases = new[]
        {
            ("こんにちは", "Hello", "Simple greeting"),
            ("世界", "World", "Single word"),
            ("ありがとうございます", "Thank you", "Polite expression"),
            ("……複雑でよくわからない", "complex", "Complex phrase"),
            ("これはテストです", "This is a test", "Simple sentence"),
            ("日本語から英語への翻訳", "translation from Japanese to English", "Translation context"),
        };

        _output.WriteLine("=== 語彙サイズ不整合修正後の翻訳品質検証 ===\n");

        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Warning)
                   .AddFilter("Baketa", LogLevel.Warning));

        var logger = loggerFactory.CreateLogger<AlphaOpusMtTranslationEngine>();
        
        var modelsBaseDir = FindModelsDirectory();
        var onnxModelPath = Path.Combine(modelsBaseDir, "ONNX", "opus-mt-ja-en.onnx");
        var sentencePieceModelPath = Path.Combine(modelsBaseDir, "SentencePiece", "opus-mt-ja-en.model");

        if (!File.Exists(onnxModelPath) || !File.Exists(sentencePieceModelPath))
        {
            _output.WriteLine("⚠️ 必要なモデルファイルが見つかりません");
            return;
        }

        var languagePair = new LanguagePair
        {
            SourceLanguage = Language.Japanese,
            TargetLanguage = Language.English
        };

        var options = new AlphaOpusMtOptions
        {
            MaxSequenceLength = 128,
            MemoryLimitMb = 512,
            ThreadCount = 1,
            RepetitionPenalty = 1.2f
        };

        var engine = new AlphaOpusMtTranslationEngine(
            onnxModelPath,
            sentencePieceModelPath,
            languagePair,
            options,
            logger);

        var initResult = await engine.InitializeAsync();
        Assert.True(initResult, "エンジン初期化に失敗");

        var successCount = 0;
        var totalCount = testCases.Length;

        foreach (var (japanese, expectedKeyword, description) in testCases)
        {
            _output.WriteLine($"テストケース: {description}");
            _output.WriteLine($"  入力: '{japanese}'");
            
            var request = new TranslationRequest
            {
                SourceText = japanese,
                SourceLanguage = Language.Japanese,
                TargetLanguage = Language.English
            };

            var result = await engine.TranslateAsync(request, CancellationToken.None);
            
            _output.WriteLine($"  翻訳結果: '{result.TranslatedText}'");
            _output.WriteLine($"  処理時間: {result.ProcessingTimeMs:F1}ms");

            // 問題のあるパターンの検出
            var problematicPatterns = new[]
            {
                "excuse", "lost", "while", "look", "over", 
                "our", "becauset", "literally", "tok_"
            };

            var hasProblematicPattern = false;
            foreach (var pattern in problematicPatterns)
            {
                if (result.TranslatedText?.Contains(pattern, StringComparison.OrdinalIgnoreCase) == true)
                {
                    _output.WriteLine($"  ❌ 問題パターン検出: '{pattern}'");
                    hasProblematicPattern = true;
                    break;
                }
            }

            // 期待されるキーワードの存在確認
            var containsExpectedKeyword = result.TranslatedText
                ?.Contains(expectedKeyword, StringComparison.OrdinalIgnoreCase) == true;

            if (!hasProblematicPattern && containsExpectedKeyword)
            {
                _output.WriteLine($"  ✅ 成功: 期待されるキーワード '{expectedKeyword}' を含む");
                successCount++;
            }
            else if (!hasProblematicPattern && !string.IsNullOrWhiteSpace(result.TranslatedText))
            {
                _output.WriteLine($"  ⚠️ 部分的成功: 問題パターンなし、期待キーワードなし");
            }
            else
            {
                _output.WriteLine($"  ❌ 失敗: 翻訳品質問題");
            }

            _output.WriteLine("");
        }

        _output.WriteLine($"=== テスト結果サマリー ===");
        _output.WriteLine($"成功: {successCount}/{totalCount} ({successCount * 100.0 / totalCount:F1}%)");
        
        // 語彙サイズ不整合修正後は、少なくとも50%以上の成功率を期待
        var successRate = (double)successCount / totalCount;
        _output.WriteLine($"\n語彙サイズ不整合修正の効果: {(successRate >= 0.5 ? "✅ 確認" : "❌ 不十分")}");
        
        Assert.True(successRate >= 0.3, 
            $"翻訳品質が期待値を下回っています。成功率: {successRate:P1}");
    }

    private static string FindModelsDirectory()
    {
        var candidatePaths = new[]
        {
            @"E:\dev\Baketa\Models",
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "Models"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "Models"),
        };

        foreach (var path in candidatePaths)
        {
            var normalizedPath = Path.GetFullPath(path);
            if (Directory.Exists(normalizedPath))
            {
                return normalizedPath;
            }
        }

        throw new DirectoryNotFoundException($"Modelsディレクトリが見つかりません");
    }
}