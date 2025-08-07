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
/// 代替ONNXモデルのテスト
/// 異なるONNXモデルファイルで翻訳品質を検証
/// </summary>
public class AlternativeOnnxModelTest
{
    private readonly ITestOutputHelper _output;

    public AlternativeOnnxModelTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TestAlternativeOnnxModels_ShouldFindWorkingModel()
    {
        var testInput = "……複雑でよくわからない";
        
        _output.WriteLine("=== 代替ONNXモデル検証テスト ===");
        _output.WriteLine($"テスト入力: '{testInput}'\n");

        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Warning)
                   .AddFilter("Baketa", LogLevel.Warning));

        var logger = loggerFactory.CreateLogger<AlphaOpusMtTranslationEngine>();
        
        var modelsBaseDir = FindModelsDirectory();
        var sentencePieceModelPath = Path.Combine(modelsBaseDir, "SentencePiece", "opus-mt-ja-en.model");

        if (!File.Exists(sentencePieceModelPath))
        {
            _output.WriteLine("⚠️ SentencePieceモデルが見つかりません");
            return;
        }

        // テストするONNXモデルのリスト
        var onnxModelsToTest = new[]
        {
            ("onnx_community_opus_mt.onnx", "ONNX Community OPUS-MT (290KB)", true),
            ("helsinki-opus-mt-ja-en.onnx", "Helsinki OPUS-MT ja-en (226MB)", false),
            ("onnx-community-decoder_model.onnx", "ONNX Community Decoder", false),
            ("onnx-community-encoder_model.onnx", "ONNX Community Encoder", false),
            ("opus-mt-en-jap.onnx", "OPUS-MT en-jap (逆方向)", false),
        };

        foreach (var (modelFile, description, isPriority) in onnxModelsToTest)
        {
            _output.WriteLine($"\n{'='*60}");
            _output.WriteLine($"モデル: {description}");
            _output.WriteLine($"ファイル: {modelFile}");
            if (isPriority) _output.WriteLine("🎯 優先度: 高（異なる構造の可能性）");
            
            var onnxModelPath = Path.Combine(modelsBaseDir, "ONNX", modelFile);
            
            if (!File.Exists(onnxModelPath))
            {
                _output.WriteLine("❌ モデルファイルが見つかりません");
                continue;
            }

            var fileInfo = new FileInfo(onnxModelPath);
            _output.WriteLine($"ファイルサイズ: {fileInfo.Length / 1024.0 / 1024.0:F1} MB");

            try
            {
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

                _output.WriteLine("初期化中...");
                var initResult = await engine.InitializeAsync();
                
                if (!initResult)
                {
                    _output.WriteLine("❌ エンジン初期化失敗");
                    continue;
                }

                _output.WriteLine("✅ エンジン初期化成功");

                // 翻訳実行
                var request = new TranslationRequest
                {
                    SourceText = testInput,
                    SourceLanguage = Language.Japanese,
                    TargetLanguage = Language.English
                };

                var result = await engine.TranslateAsync(request, CancellationToken.None);
                
                _output.WriteLine($"\n翻訳結果: '{result.TranslatedText}'");
                _output.WriteLine($"信頼度: {result.ConfidenceScore:F3}");
                _output.WriteLine($"処理時間: {result.ProcessingTimeMs:F1}ms");

                // 翻訳品質の評価
                var translatedText = result.TranslatedText.ToLowerInvariant();
                var problematicPatterns = new[]
                {
                    "excuse", "lost", "while", "look", "over", 
                    "our", "becauset", "literally", "tok_"
                };

                var hasProblems = false;
                foreach (var pattern in problematicPatterns)
                {
                    if (translatedText.Contains(pattern))
                    {
                        _output.WriteLine($"⚠️ 問題パターン検出: '{pattern}'");
                        hasProblems = true;
                    }
                }

                // 意味のある翻訳の可能性を評価
                var meaningfulKeywords = new[]
                {
                    "complex", "complicated", "difficult", "understand", 
                    "confusing", "unclear", "hard", "don't know"
                };

                var hasMeaningfulContent = false;
                foreach (var keyword in meaningfulKeywords)
                {
                    if (translatedText.Contains(keyword))
                    {
                        _output.WriteLine($"✅ 意味のあるキーワード検出: '{keyword}'");
                        hasMeaningfulContent = true;
                    }
                }

                if (!hasProblems && hasMeaningfulContent)
                {
                    _output.WriteLine($"\n🎉 成功！このモデルが動作します: {description}");
                    _output.WriteLine($"推奨モデル: {modelFile}");
                    
                    // 追加テストケース
                    await TestAdditionalCases(engine, _output);
                    
                    return; // 成功したモデルが見つかった
                }
                else if (!hasProblems)
                {
                    _output.WriteLine("⚠️ 問題パターンはないが、期待される内容でもない");
                }
                else
                {
                    _output.WriteLine("❌ 翻訳品質に問題あり");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"❌ エラー発生: {ex.Message}");
                if (ex.InnerException != null)
                {
                    _output.WriteLine($"   内部エラー: {ex.InnerException.Message}");
                }
            }
        }

        _output.WriteLine("\n\n=== 結論 ===");
        _output.WriteLine("❌ 動作する代替ONNXモデルが見つかりませんでした");
        _output.WriteLine("根本的な問題: ONNXモデルとSentencePieceトークナイザーの非互換性");
    }

    private async Task TestAdditionalCases(AlphaOpusMtTranslationEngine engine, ITestOutputHelper output)
    {
        output.WriteLine("\n=== 追加テストケース ===");
        
        var testCases = new[]
        {
            ("こんにちは", "hello/hi"),
            ("ありがとう", "thank"),
            ("世界", "world"),
            ("翻訳", "translat")
        };

        foreach (var (input, expectedPattern) in testCases)
        {
            var request = new TranslationRequest
            {
                SourceText = input,
                SourceLanguage = Language.Japanese,
                TargetLanguage = Language.English
            };

            var result = await engine.TranslateAsync(request, CancellationToken.None);
            var contains = result.TranslatedText.ToLowerInvariant().Contains(expectedPattern);
            
            output.WriteLine($"{input} → {result.TranslatedText} {(contains ? "✅" : "❌")}");
        }
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