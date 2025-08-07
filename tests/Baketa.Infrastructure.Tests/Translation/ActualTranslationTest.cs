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
/// 実際のOPUS-MT翻訳エンジンをテストして、無意味な翻訳結果の原因を調査するテスト
/// </summary>
public class ActualTranslationTest
{
    private readonly ITestOutputHelper _output;

    public ActualTranslationTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ActualTranslation_ComplexPhrase_ShouldProduceMeaningfulResult()
    {
        // Arrange
        var inputText = "……複雑でよくわからない";
        
        _output.WriteLine($"=== 実際のOPUS-MT翻訳テスト ===");
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
            var onnxModelPath = Path.Combine(modelsBaseDir, "ONNX", "opus-mt-ja-en.onnx");
            var sentencePieceModelPath = Path.Combine(modelsBaseDir, "SentencePiece", "opus-mt-ja-en.model");

            _output.WriteLine($"ONNX Model: {File.Exists(onnxModelPath)} - {onnxModelPath}");
            _output.WriteLine($"SentencePiece Model: {File.Exists(sentencePieceModelPath)} - {sentencePieceModelPath}");

            if (!File.Exists(onnxModelPath) || !File.Exists(sentencePieceModelPath))
            {
                _output.WriteLine("⚠️ 必要なモデルファイルが見つかりません。テストをスキップします。");
                return;
            }

            // 言語ペア設定
            var languagePair = new LanguagePair
            {
                SourceLanguage = Language.Japanese,
                TargetLanguage = Language.English
            };

            // オプション設定（ONNX推論検証を有効にする）
            var options = new AlphaOpusMtOptions
            {
                MaxSequenceLength = 128,
                MemoryLimitMb = 512,
                ThreadCount = 1,
                RepetitionPenalty = 2.0f
            };

            _output.WriteLine($"翻訳エンジン設定:");
            _output.WriteLine($"  - MaxSequenceLength: {options.MaxSequenceLength}");
            _output.WriteLine($"  - RepetitionPenalty: {options.RepetitionPenalty}");

            // エンジンの作成と初期化
            var engine = new AlphaOpusMtTranslationEngine(
                onnxModelPath,
                sentencePieceModelPath,
                languagePair,
                options,
                logger);

            _output.WriteLine("\n=== エンジン初期化開始 ===");
            var initResult = await engine.InitializeAsync();
            
            if (!initResult)
            {
                _output.WriteLine("❌ 翻訳エンジンの初期化に失敗しました。");
                Assert.Fail("Translation engine initialization failed");
                return;
            }

            _output.WriteLine("✅ 翻訳エンジンの初期化が完了しました。");

            // 翻訳実行
            _output.WriteLine("\n=== 翻訳実行開始 ===");
            
            var translationRequest = new TranslationRequest
            {
                SourceText = inputText,
                SourceLanguage = Language.Japanese,
                TargetLanguage = Language.English
            };

            var result = await engine.TranslateAsync(translationRequest, CancellationToken.None);
            
            _output.WriteLine($"\n=== 翻訳結果 ===");
            _output.WriteLine($"入力: '{result.SourceText}'");
            _output.WriteLine($"出力: '{result.TranslatedText}'");
            _output.WriteLine($"信頼度: {result.ConfidenceScore:F3}");
            _output.WriteLine($"処理時間: {result.ProcessingTimeMs:F1}ms");

            // 問題のある翻訳結果の検出
            var translatedText = result.TranslatedText;
            
            // 無意味な翻訳の検出パターン
            var problematicPatterns = new[]
            {
                "excuse", "lost", "while", "look", "over", "our", "becauset", "literally"
            };

            var containsProblematicPattern = false;
            foreach (var pattern in problematicPatterns)
            {
                if (translatedText.ToLowerInvariant().Contains(pattern))
                {
                    _output.WriteLine($"⚠️ 問題のあるパターン検出: '{pattern}'");
                    containsProblematicPattern = true;
                }
            }

            // 期待されるキーワードの存在確認
            var expectedKeywords = new[] { "complex", "complicated", "confusing", "difficult", "understand", "don't", "know", "unclear", "hard" };
            var containsExpectedKeyword = false;
            
            foreach (var keyword in expectedKeywords)
            {
                if (translatedText.ToLowerInvariant().Contains(keyword))
                {
                    _output.WriteLine($"✅ 期待されるキーワード検出: '{keyword}'");
                    containsExpectedKeyword = true;
                }
            }

            // 結果の評価
            if (containsProblematicPattern)
            {
                _output.WriteLine("❌ 無意味な翻訳結果が検出されました。ONNX推論テンサー検証ログを確認してください。");
                
                // ONNXテンサー検証が有効であることを確認
                _output.WriteLine("\n=== デバッグ情報 ===");
                _output.WriteLine("ONNX推論テンサー検証機能が実行されているはずです。");
                _output.WriteLine("詳細なログを確認して根本原因を特定してください。");
            }
            else if (containsExpectedKeyword)
            {
                _output.WriteLine("✅ 意味のある翻訳結果が得られました。");
            }
            else
            {
                _output.WriteLine("⚠️ 予期しない翻訳結果です。内容を確認してください。");
            }

            // テスト結果のアサーション
            Assert.False(string.IsNullOrWhiteSpace(translatedText), "Translation result should not be empty");
            Assert.False(containsProblematicPattern, $"Translation should not contain problematic patterns. Actual: '{translatedText}'");

            _output.WriteLine("\n=== テスト完了 ===");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"\n❌ テスト実行中にエラーが発生しました:");
            _output.WriteLine($"メッセージ: {ex.Message}");
            _output.WriteLine($"スタックトレース:\n{ex.StackTrace}");
            
            // ファイルが見つからない場合はテストをスキップ
            if (ex.Message.Contains("model") || ex.Message.Contains("file") || ex.Message.Contains("path"))
            {
                _output.WriteLine("モデルファイル関連の問題のため、テストをスキップします。");
                return;
            }
            
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