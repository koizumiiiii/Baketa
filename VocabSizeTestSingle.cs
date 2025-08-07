using System;
using System.Threading.Tasks;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
using Microsoft.Extensions.Logging;

namespace Baketa;

/// <summary>
/// 単一モデルの語彙サイズを詳細にテストするツール
/// </summary>
public class VocabSizeTestSingle
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== HuggingFace target.spm 詳細分析 ===");
        
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Debug));
        
        var logger = loggerFactory.CreateLogger<OpusMtNativeTokenizer>();
        var modelPath = @"E:\dev\Baketa\Models\HuggingFace\opus-mt-ja-en\target.spm";
        
        try
        {
            Console.WriteLine($"ファイルパス: {modelPath}");
            
            var fileInfo = new System.IO.FileInfo(modelPath);
            Console.WriteLine($"ファイルサイズ: {fileInfo.Length:N0} bytes");
            Console.WriteLine($"最終更新: {fileInfo.LastWriteTime}");
            
            // SentencePieceトークナイザーを作成
            var tokenizer = new OpusMtNativeTokenizer(modelPath, "HuggingFace Target", logger);
            
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
                Console.WriteLine("🎯 PERFECT MATCH! このモデルが正解です");
            }
            else if (difference < 1000)
            {
                Console.WriteLine("✅ 非常に近い - 使用可能な候補");
            }
            else
            {
                Console.WriteLine($"❌ 大きな差 - 期待: {targetVocabSize:N0}, 実際: {tokenizer.VocabularySize:N0}");
            }
            
            // テストトークン化
            var testText = "テスト";
            var tokens = tokenizer.Tokenize(testText);
            Console.WriteLine($"テスト '{testText}' -> [{string.Join(", ", tokens)}]");
            
            // 語彙範囲の確認
            Console.WriteLine($"最大トークンID: {tokens.Max()}");
            Console.WriteLine($"語彙サイズ範囲: 0 - {tokenizer.VocabularySize - 1}");
            
            if (tokens.Any(t => t >= tokenizer.VocabularySize))
            {
                Console.WriteLine("⚠️ 警告: 語彙範囲外のトークンIDが検出されました");
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ エラー: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   詳細: {ex.InnerException.Message}");
            }
        }
        
        Console.WriteLine("\n分析完了。Enterキーで終了...");
        Console.ReadLine();
    }
}