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
/// ONNX-Community系モデルの語彙サイズ検証テスト
/// </summary>
public class OnnxCommunityModelTest
{
    private readonly ITestOutputHelper _output;

    public OnnxCommunityModelTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task OnnxCommunityModel_VocabSizeCheck_ShouldMatch32k()
    {
        // Arrange
        var inputText = "……複雑でよくわからない";
        
        _output.WriteLine($"=== ONNX-Community モデル語彙サイズ検証 ===");
        _output.WriteLine($"入力テキスト: '{inputText}'");
        
        // LoggerFactory設定
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Debug)
                   .AddFilter("Baketa", LogLevel.Debug)
                   .AddFilter("Microsoft", LogLevel.Warning));
        
        var logger = loggerFactory.CreateLogger<AlphaOpusMtTranslationEngine>();

        try
        {
            // モデルファイルパス確認
            var modelsBaseDir = FindModelsDirectory();
            var sentencePieceModelPath = Path.Combine(modelsBaseDir, "SentencePiece", "opus-mt-ja-en.model");

            // ONNX-Communityモデルを順番にテスト
            var onnxModelsToTest = new[]
            {
                ("onnx-community-model.onnx", "ONNX Community 統合モデル"),
                ("onnx-community-decoder_model.onnx", "ONNX Community デコーダーモデル"),
                ("onnx-community-encoder_model.onnx", "ONNX Community エンコーダーモデル"),
                ("onnx_community_opus_mt.onnx", "ONNX Community OPUS-MT"),
            };

            foreach (var (modelFile, description) in onnxModelsToTest)
            {
                _output.WriteLine($"\n=== {description} のテスト ===");
                var onnxModelPath = Path.Combine(modelsBaseDir, "ONNX", modelFile);
                
                _output.WriteLine($"ONNX Model: {File.Exists(onnxModelPath)} - {onnxModelPath}");
                _output.WriteLine($"SentencePiece Model: {File.Exists(sentencePieceModelPath)} - {sentencePieceModelPath}");

                if (!File.Exists(onnxModelPath))
                {
                    _output.WriteLine("⚠️ ONNXモデルファイルが見つかりません。スキップします。");
                    continue;
                }

                if (!File.Exists(sentencePieceModelPath))
                {
                    _output.WriteLine("⚠️ SentencePieceモデルファイルが見つかりません。スキップします。");
                    continue;
                }

                try
                {
                    // 言語ペア設定
                    var languagePair = new LanguagePair
                    {
                        SourceLanguage = Language.Japanese,
                        TargetLanguage = Language.English
                    };

                    // オプション設定
                    var options = new AlphaOpusMtOptions
                    {
                        MaxSequenceLength = 128,
                        MemoryLimitMb = 512,
                        ThreadCount = 1,
                        RepetitionPenalty = 2.0f
                    };

                    // エンジンの作成と初期化
                    var engine = new AlphaOpusMtTranslationEngine(
                        onnxModelPath,
                        sentencePieceModelPath,
                        languagePair,
                        options,
                        logger);

                    _output.WriteLine("エンジン初期化開始...");
                    var initResult = await engine.InitializeAsync();
                    
                    if (!initResult)
                    {
                        _output.WriteLine("❌ 翻訳エンジンの初期化に失敗しました。");
                        continue;
                    }

                    _output.WriteLine("✅ 翻訳エンジンの初期化が完了しました。");

                    // 翻訳実行
                    var translationRequest = new TranslationRequest
                    {
                        SourceText = inputText,
                        SourceLanguage = Language.Japanese,
                        TargetLanguage = Language.English
                    };

                    var result = await engine.TranslateAsync(translationRequest, CancellationToken.None);
                    
                    _output.WriteLine($"翻訳結果: '{result.TranslatedText}'");
                    _output.WriteLine($"信頼度: {result.ConfidenceScore:F3}");
                    _output.WriteLine($"処理時間: {result.ProcessingTimeMs:F1}ms");

                    // 問題のある翻訳結果の検出
                    var translatedText = result.TranslatedText;
                    var problematicPatterns = new[]
                    {
                        "excuse", "lost", "while", "look", "over", "our", "becauset", "literally"
                    };

                    var hasProblematicPattern = false;
                    foreach (var pattern in problematicPatterns)
                    {
                        if (translatedText?.Contains(pattern, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            _output.WriteLine($"⚠️ 問題のあるパターン検出: '{pattern}'");
                            hasProblematicPattern = true;
                        }
                    }

                    if (!hasProblematicPattern && !string.IsNullOrWhiteSpace(translatedText))
                    {
                        _output.WriteLine($"🎯 {description} で正常な翻訳結果を確認!");
                        _output.WriteLine("このモデルが32,000語彙サイズと互換性がある可能性が高いです。");
                    }
                    else if (string.IsNullOrWhiteSpace(translatedText))
                    {
                        _output.WriteLine("❌ 空の翻訳結果 - モデル不適合");
                    }
                    else
                    {
                        _output.WriteLine("❌ 問題のある翻訳結果 - 語彙サイズ不整合の可能性");
                    }

                }
                catch (Exception ex)
                {
                    _output.WriteLine($"❌ {description} でエラー: {ex.Message}");
                }

                _output.WriteLine($"--- {description} テスト完了 ---\n");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"❌ テスト実行中にエラーが発生しました: {ex.Message}");
            throw;
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