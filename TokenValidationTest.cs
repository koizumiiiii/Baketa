using System;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Microsoft.Extensions.Logging;

namespace Baketa;

public class TokenValidationTest
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== BOS/EOS Token ID 検証テスト ===");

        // ロガーの設定
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<AlphaOpusMtTranslationEngine>();

        try
        {
            // モデルファイルパスの設定
            var modelsBaseDir = @"E:\dev\Baketa\Models";
            var onnxModelPath = Path.Combine(modelsBaseDir, "ONNX", "opus-mt-ja-en.onnx");
            var sentencePieceModelPath = Path.Combine(modelsBaseDir, "SentencePiece", "opus-mt-ja-en.model");

            Console.WriteLine($"ONNX Model Path: {onnxModelPath}");
            Console.WriteLine($"SentencePiece Model Path: {sentencePieceModelPath}");
            Console.WriteLine($"ONNX Model Exists: {File.Exists(onnxModelPath)}");
            Console.WriteLine($"SentencePiece Model Exists: {File.Exists(sentencePieceModelPath)}");

            if (!File.Exists(onnxModelPath) || !File.Exists(sentencePieceModelPath))
            {
                Console.WriteLine("❌ 必要なモデルファイルが見つかりません");
                return;
            }

            // 言語ペア設定
            var languagePair = new LanguagePair
            {
                SourceLanguage = Language.Japanese,
                TargetLanguage = Language.English
            };

            var options = new AlphaOpusMtOptions
            {
                MaxSequenceLength = 128,
                MemoryLimitMb = 300,
                ThreadCount = 1,
                RepetitionPenalty = 2.0f,
                NumBeams = 1,
                LengthPenalty = 1.0f,
                MaxGenerationLength = 10
            };

            // エンジンの作成と初期化
            Console.WriteLine("エンジン作成中...");
            var engine = new AlphaOpusMtTranslationEngine(
                onnxModelPath,
                sentencePieceModelPath,
                languagePair,
                options,
                logger);

            Console.WriteLine("エンジン初期化中...");
            var initResult = await engine.InitializeAsync();
            
            if (!initResult)
            {
                Console.WriteLine("❌ エンジンの初期化に失敗");
                return;
            }

            Console.WriteLine("✅ エンジン初期化完了");
            Console.WriteLine("初期化ログでBOS/EOS Token IDが表示されているはずです。");
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ エラー: {ex.Message}");
            Console.WriteLine($"スタックトレース: {ex.StackTrace}");
        }

        Console.WriteLine("\nテスト完了。Enterキーで終了...");
        Console.ReadLine();
    }
}