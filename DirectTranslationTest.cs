using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Microsoft.Extensions.Logging;

namespace Baketa;

/// <summary>
/// 直接翻訳テストプログラム
/// </summary>
public class DirectTranslationTest
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== 直接翻訳テスト開始 ===");
        Console.WriteLine();

        // ロガーの設定
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<AlphaOpusMtTranslationEngine>();

        // テスト対象のテキスト
        string testText = "……複雑でよくわからない";
        Console.WriteLine($"翻訳対象テキスト: {testText}");
        Console.WriteLine();

        try
        {
            // モデルファイルパスの設定
            var modelsBaseDir = @"E:\dev\Baketa\Models";
            var onnxModelPath = Path.Combine(modelsBaseDir, "ONNX", "opus-mt-ja-en.onnx");
            var sentencePieceModelPath = Path.Combine(modelsBaseDir, "SentencePiece", "opus-mt-ja-en.model");

            Console.WriteLine($"ONNXモデル: {onnxModelPath}");
            Console.WriteLine($"SentencePieceモデル: {sentencePieceModelPath}");

            // ファイル存在確認
            if (!File.Exists(onnxModelPath))
            {
                Console.WriteLine("❌ ONNXモデルファイルが見つかりません");
                return;
            }

            if (!File.Exists(sentencePieceModelPath))
            {
                Console.WriteLine("❌ SentencePieceモデルファイルが見つかりません");
                return;
            }

            Console.WriteLine("✅ 必要なモデルファイルが確認されました");
            Console.WriteLine();

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
                RepetitionPenalty = 3.0f, // 緊急修正後の強化されたペナルティ
                NumBeams = 1,
                LengthPenalty = 1.0f,
                MaxGenerationLength = 20 // 緊急修正後の短縮された長さ
            };

            Console.WriteLine("=== 翻訳エンジン初期化中 ===");

            // エンジン作成
            var engine = new AlphaOpusMtTranslationEngine(
                onnxModelPath,
                sentencePieceModelPath,
                languagePair,
                options,
                logger);

            // 初期化
            Console.WriteLine("エンジン初期化...");
            var initResult = await engine.InitializeAsync();
            
            if (!initResult)
            {
                Console.WriteLine("❌ エンジンの初期化に失敗しました");
                return;
            }

            Console.WriteLine("✅ エンジン初期化完了");
            Console.WriteLine();

            Console.WriteLine("=== 翻訳実行 ===");

            // 翻訳リクエスト作成
            var request = TranslationRequest.Create(
                testText,
                Language.Japanese,
                Language.English);

            // 処理時間測定開始
            var startTime = DateTime.Now;
            Console.WriteLine($"開始時刻: {startTime:HH:mm:ss.fff}");

            // キャンセレーショントークン（60秒タイムアウト）
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            // 翻訳実行
            Console.WriteLine("翻訳実行中...");
            var response = await engine.TranslateAsync(request, cts.Token);

            // 処理時間測定終了
            var endTime = DateTime.Now;
            var duration = endTime - startTime;

            Console.WriteLine();
            Console.WriteLine("=== 翻訳結果 ===");
            Console.WriteLine($"成功: {response.IsSuccess}");
            Console.WriteLine($"入力: '{request.SourceText}'");
            Console.WriteLine($"出力: '{response.TranslatedText}'");
            Console.WriteLine($"処理時間: {duration.TotalSeconds:F2}秒 ({duration.TotalMilliseconds:F0}ms)");
            Console.WriteLine($"終了時刻: {endTime:HH:mm:ss.fff}");

            if (!response.IsSuccess && response.Error != null)
            {
                Console.WriteLine($"エラー: {response.Error.Message}");
                Console.WriteLine($"エラータイプ: {response.Error.ErrorType}");
            }

            Console.WriteLine();
            Console.WriteLine("=== 結果分析 ===");

            if (response.IsSuccess)
            {
                AnalyzeTranslationResult(response.TranslatedText, duration);
            }
            else
            {
                Console.WriteLine("❌ 翻訳に失敗しました");
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 例外が発生しました: {ex.Message}");
            Console.WriteLine($"スタックトレース: {ex.StackTrace}");
        }

        Console.WriteLine();
        Console.WriteLine("=== テスト完了 ===");
        Console.WriteLine("何かキーを押してください...");
        Console.ReadKey();
    }

    private static void AnalyzeTranslationResult(string translatedText, TimeSpan duration)
    {
        // 処理時間チェック（30秒以内）
        bool timeOk = duration.TotalSeconds <= 30;
        Console.WriteLine($"⏱️  処理時間: {(timeOk ? "✅" : "❌")} ({duration.TotalSeconds:F2}秒)");

        // 出力長チェック（500文字以内）
        bool lengthOk = translatedText.Length <= 500;
        Console.WriteLine($"📏 出力長: {(lengthOk ? "✅" : "❌")} ({translatedText.Length}文字)");

        // 繰り返しパターンチェック
        bool noRepetition = !HasRepetitivePattern(translatedText);
        Console.WriteLine($"🔄 繰り返し回避: {(noRepetition ? "✅" : "❌")}");

        // 適切なキーワードチェック
        var expectedKeywords = new[] { "complex", "complicated", "confusing", "difficult", "understand", "don't", "know", "unclear", "hard" };
        var foundKeywords = expectedKeywords.Where(kw => 
            translatedText.ToLowerInvariant().Contains(kw)).ToArray();
            
        bool hasKeywords = foundKeywords.Length > 0;
        Console.WriteLine($"🔤 関連キーワード: {(hasKeywords ? "✅" : "❌")} ({foundKeywords.Length}個)");

        if (foundKeywords.Length > 0)
        {
            Console.WriteLine($"   見つかったキーワード: {string.Join(", ", foundKeywords)}");
        }

        // 無限ループ系パターンチェック
        bool noInfiniteLoop = !HasInfiniteLoopPattern(translatedText);
        Console.WriteLine($"🚫 無限ループ回避: {(noInfiniteLoop ? "✅" : "❌")}");

        // 総合評価
        bool overall = timeOk && lengthOk && noRepetition && noInfiniteLoop;
        Console.WriteLine();
        Console.WriteLine($"🎯 総合評価: {(overall ? "✅ 成功" : "❌ 問題あり")}");

        if (hasKeywords)
        {
            Console.WriteLine($"🎉 翻訳品質: 適切な英語キーワードを含む高品質な翻訳");
        }
    }

    private static bool HasRepetitivePattern(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 10) return false;

        var uniqueWords = words.Distinct().Count();
        var repetitionRatio = (double)uniqueWords / words.Length;
        
        return repetitionRatio <= 0.3;
    }

    private static bool HasInfiniteLoopPattern(string text)
    {
        var lowerText = text.ToLowerInvariant();
        
        // 既知の問題パターン
        if (lowerText.Contains("lost centuries"))
            return true;
        if (lowerText.Contains("excuse lost"))
            return true;
        if (text.Contains("you you you"))
            return true;
            
        return false;
    }
}