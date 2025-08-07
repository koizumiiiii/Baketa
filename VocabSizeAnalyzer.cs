using System;
using System.IO;
using System.Threading.Tasks;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
using Microsoft.Extensions.Logging;

namespace Baketa;

/// <summary>
/// 各SentencePieceモデルの語彙サイズを分析するツール
/// </summary>
public class VocabSizeAnalyzer
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== SentencePiece モデル語彙サイズ分析 ===");
        
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Information));
        
        var logger = loggerFactory.CreateLogger<OpusMtNativeTokenizer>();

        var modelsToAnalyze = new[]
        {
            // 現在使用中のモデル
            (@"E:\dev\Baketa\Models\SentencePiece\opus-mt-ja-en.model", "現在のソース"),
            (@"E:\dev\Baketa\Models\Official_Helsinki\target.spm", "現在のターゲット"),
            
            // HuggingFaceモデル
            (@"E:\dev\Baketa\Models\HuggingFace\opus-mt-ja-en\source.spm", "HuggingFace ソース"),
            (@"E:\dev\Baketa\Models\HuggingFace\opus-mt-ja-en\target.spm", "HuggingFace ターゲット"),
            
            // その他のモデル
            (@"E:\dev\Baketa\Models\Official_Helsinki\source.spm", "Official Helsinki ソース"),
            (@"E:\dev\Baketa\Models\SentencePiece\opus-mt-ja-en-official.model", "Official SentencePiece"),
            (@"E:\dev\Baketa\Models\SentencePiece\helsinki-opus-mt-ja-en.model", "Helsinki SentencePiece")
        };

        Console.WriteLine($"分析対象モデル数: {modelsToAnalyze.Length}");
        Console.WriteLine($"目標語彙サイズ: 60,716 (ONNXモデルと一致)");
        Console.WriteLine();

        foreach (var (modelPath, description) in modelsToAnalyze)
        {
            await AnalyzeModel(modelPath, description, logger);
            Console.WriteLine();
        }

        Console.WriteLine("=== 分析完了 ===");
        Console.WriteLine("語彙サイズ60,716に最も近いモデルを特定してください。");
        Console.ReadLine();
    }

    private static async Task AnalyzeModel(string modelPath, string description, ILogger logger)
    {
        try
        {
            Console.WriteLine($"=== {description} ===");
            Console.WriteLine($"パス: {modelPath}");
            
            if (!File.Exists(modelPath))
            {
                Console.WriteLine("❌ ファイルが存在しません");
                return;
            }

            var fileInfo = new FileInfo(modelPath);
            Console.WriteLine($"ファイルサイズ: {fileInfo.Length:N0} bytes");
            
            // SentencePieceトークナイザーを作成
            var tokenizer = new OpusMtNativeTokenizer(modelPath, logger);
            
            Console.WriteLine($"✅ 語彙サイズ: {tokenizer.VocabularySize:N0}");
            
            // 特殊トークンID確認
            var bosId = tokenizer.GetSpecialTokenId("BOS");
            var eosId = tokenizer.GetSpecialTokenId("EOS");
            var unkId = tokenizer.GetSpecialTokenId("UNK");
            var padId = tokenizer.GetSpecialTokenId("PAD");
            
            Console.WriteLine($"特殊トークン - BOS: {bosId}, EOS: {eosId}, UNK: {unkId}, PAD: {padId}");
            
            // ONNXモデルとの一致度チェック
            const int targetVocabSize = 60716;
            var difference = Math.Abs(tokenizer.VocabularySize - targetVocabSize);
            var matchPercentage = (1.0 - (double)difference / targetVocabSize) * 100;
            
            Console.WriteLine($"ONNX一致度: {matchPercentage:F2}% (差分: {difference:N0})");
            
            if (tokenizer.VocabularySize == targetVocabSize)
            {
                Console.WriteLine("🎯 PERFECT MATCH! このモデルを使用すべきです");
            }
            else if (difference < 1000)
            {
                Console.WriteLine("✅ 非常に近い - 使用可能な候補");
            }
            else if (difference < 10000)
            {
                Console.WriteLine("⚠️ やや差がある - 要検討");
            }
            else
            {
                Console.WriteLine("❌ 大きな差 - 使用非推奨");
            }
            
            // テストトークン化
            var testText = "テスト";
            var tokens = tokenizer.Encode(testText);
            Console.WriteLine($"テスト '{testText}' -> [{string.Join(", ", tokens)}]");

        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ エラー: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   詳細: {ex.InnerException.Message}");
            }
        }
    }
}