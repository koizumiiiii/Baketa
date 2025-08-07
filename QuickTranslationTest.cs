using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Microsoft.Extensions.Logging;

namespace Baketa;

public class QuickTranslationTest
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== 入力正規化効果テスト ===");

        // ロガーの設定
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<AlphaOpusMtTranslationEngine>();

        // テスト対象のテキスト
        string originalText = "……複雑でよくわからない";
        Console.WriteLine($"元のテキスト: '{originalText}'");

        try
        {
            // モデルファイルパスの設定
            var modelsBaseDir = @"E:\dev\Baketa\Models";
            var onnxModelPath = Path.Combine(modelsBaseDir, "ONNX", "opus-mt-ja-en.onnx");
            var sentencePieceModelPath = Path.Combine(modelsBaseDir, "SentencePiece", "opus-mt-ja-en.model");

            // 言語ペア設定
            var languagePair = new LanguagePair
            {
                SourceLanguage = Language.Japanese,
                TargetLanguage = Language.English
            };

            var options = new AlphaOpusMtOptions
            {
                MaxSequenceLength = 256,
                MemoryLimitMb = 300,
                ThreadCount = 2,
                RepetitionPenalty = 3.0f,
                NumBeams = 1,
                LengthPenalty = 1.0f,
                MaxGenerationLength = 15 // 短縮
            };

            // エンジン作成
            var engine = new AlphaOpusMtTranslationEngine(
                onnxModelPath,
                sentencePieceModelPath,
                languagePair,
                options,
                logger);

            // 初期化
            Console.WriteLine("エンジン初期化中...");
            var initResult = await engine.InitializeAsync();
            
            if (!initResult)
            {
                Console.WriteLine("❌ エンジンの初期化に失敗");
                return;
            }

            Console.WriteLine("✅ エンジン初期化完了");
            
            // 翻訳リクエスト作成
            var request = TranslationRequest.Create(
                originalText,
                Language.Japanese,
                Language.English);

            // 翻訳実行
            Console.WriteLine("翻訳実行中...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var response = await engine.TranslateAsync(request, cts.Token);

            // 結果表示
            Console.WriteLine("\n=== 翻訳結果 ===");
            Console.WriteLine($"成功: {response.IsSuccess}");
            Console.WriteLine($"入力: '{request.SourceText}'");
            Console.WriteLine($"出力: '{response.TranslatedText}'");
            
            if (!response.IsSuccess && response.Error != null)
            {
                Console.WriteLine($"エラー: {response.Error.Message}");
            }

            // 品質分析
            if (response.IsSuccess && !string.IsNullOrEmpty(response.TranslatedText))
            {
                Console.WriteLine("\n=== 品質分析 ===");
                var translatedLower = response.TranslatedText.ToLowerInvariant();
                
                // 期待される単語の確認
                var expectedKeywords = new[] { "complex", "complicated", "difficult", "understand", "don't", "know", "unclear", "hard", "confusing" };
                var foundKeywords = Array.FindAll(expectedKeywords, kw => translatedLower.Contains(kw));
                
                Console.WriteLine($"適切なキーワード: {foundKeywords.Length}個 [{string.Join(", ", foundKeywords)}]");
                
                // 不適切なパターンの確認
                var problematicPatterns = new[] { "excuse", "lost", "while", "look", "over", "becauset" };
                var foundProblems = Array.FindAll(problematicPatterns, p => translatedLower.Contains(p));
                
                Console.WriteLine($"問題のあるパターン: {foundProblems.Length}個 [{string.Join(", ", foundProblems)}]");
                
                // 総合評価
                bool isGoodTranslation = foundKeywords.Length > 0 && foundProblems.Length == 0;
                Console.WriteLine($"翻訳品質: {(isGoodTranslation ? "✅ 良好" : "❌ 問題あり")}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ エラー: {ex.Message}");
        }

        Console.WriteLine("\nテスト完了。Enterキーで終了...");
        Console.ReadLine();
    }
}