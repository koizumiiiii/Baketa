using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation;

/// <summary>
/// 単一SentencePieceモデルの詳細分析テスト
/// </summary>
public class SingleModelDetailedAnalysisTest
{
    private readonly ITestOutputHelper _output;

    public SingleModelDetailedAnalysisTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void HuggingFaceTargetModel_DetailedAnalysis_ShouldRevealActualVocabSize()
    {
        _output.WriteLine("=== HuggingFace target.spm 詳細分析 ===");
        
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Debug));
        
        var logger = loggerFactory.CreateLogger<OpusMtNativeTokenizer>();
        var modelPath = @"E:\dev\Baketa\Models\HuggingFace\opus-mt-ja-en\target.spm";
        
        try
        {
            _output.WriteLine($"ファイルパス: {modelPath}");
            
            var fileInfo = new FileInfo(modelPath);
            _output.WriteLine($"ファイルサイズ: {fileInfo.Length:N0} bytes");
            _output.WriteLine($"最終更新: {fileInfo.LastWriteTime}");
            
            // SentencePieceトークナイザーを作成
            var tokenizer = new OpusMtNativeTokenizer(modelPath, "HuggingFace Target", logger);
            
            _output.WriteLine($"✅ 実際の語彙サイズ: {tokenizer.VocabularySize:N0}");
            
            // 特殊トークンID確認
            var bosId = tokenizer.GetSpecialTokenId("BOS");
            var eosId = tokenizer.GetSpecialTokenId("EOS");
            var unkId = tokenizer.GetSpecialTokenId("UNK");
            var padId = tokenizer.GetSpecialTokenId("PAD");
            
            _output.WriteLine($"特殊トークン - BOS: {bosId}, EOS: {eosId}, UNK: {unkId}, PAD: {padId}");
            
            // ONNXモデルとの一致度チェック
            const int expectedVocabSize = 60716;
            var difference = Math.Abs(tokenizer.VocabularySize - expectedVocabSize);
            var matchPercentage = (1.0 - (double)difference / expectedVocabSize) * 100;
            
            _output.WriteLine($"期待語彙サイズ: {expectedVocabSize:N0}");
            _output.WriteLine($"実際語彙サイズ: {tokenizer.VocabularySize:N0}");
            _output.WriteLine($"差分: {difference:N0}");
            _output.WriteLine($"ONNX一致度: {matchPercentage:F2}%");
            
            if (tokenizer.VocabularySize == expectedVocabSize)
            {
                _output.WriteLine("🎯 PERFECT MATCH! このモデルが正解です");
            }
            else if (difference < 1000)
            {
                _output.WriteLine("✅ 非常に近い - 使用可能な候補");
            }
            else
            {
                _output.WriteLine($"❌ 大きな差 - このモデルでは語彙サイズ不一致が発生");
            }
            
            // テストトークン化
            var testTexts = new[] { "テスト", "Hello", "こんにちは", "翻訳" };
            
            foreach (var testText in testTexts)
            {
                var tokens = tokenizer.Tokenize(testText);
                var maxTokenId = tokens.Length > 0 ? tokens.Max() : 0;
                
                _output.WriteLine($"テスト '{testText}' -> [{string.Join(", ", tokens)}] (最大ID: {maxTokenId})");
                
                if (tokens.Any(t => t >= tokenizer.VocabularySize))
                {
                    _output.WriteLine($"⚠️ 警告: 語彙範囲外のトークンID検出 (語彙サイズ: {tokenizer.VocabularySize})");
                }
            }
            
            // ファイルが期待されるサイズかチェック
            _output.WriteLine($"\n=== ファイルサイズ分析 ===");
            _output.WriteLine($"現在のファイルサイズ: {fileInfo.Length:N0} bytes");
            
            // HuggingFace公式のtarget.spmは802KBと報告されている
            const long expectedFileSize = 802 * 1024; // 802KB
            var fileSizeDiff = Math.Abs(fileInfo.Length - expectedFileSize);
            var fileSizeMatch = fileSizeDiff < (expectedFileSize * 0.1); // 10%以内なら一致とみなす
            
            _output.WriteLine($"期待ファイルサイズ: {expectedFileSize:N0} bytes (802KB)");
            _output.WriteLine($"サイズ差分: {fileSizeDiff:N0} bytes");
            _output.WriteLine($"ファイルサイズ一致: {(fileSizeMatch ? "✅" : "❌")}");
            
            if (!fileSizeMatch)
            {
                _output.WriteLine("⚠️ ファイルサイズが期待値と大きく異なります。古いまたは不完全なモデルファイルの可能性があります。");
            }
            
            // 結論
            _output.WriteLine($"\n=== 分析結論 ===");
            if (tokenizer.VocabularySize == expectedVocabSize)
            {
                _output.WriteLine("✅ このモデルは正しい語彙サイズを持ちます");
            }
            else
            {
                _output.WriteLine("❌ このモデルは期待される語彙サイズと一致しません");
                _output.WriteLine("💡 新しいバージョンのモデルファイルのダウンロードが必要です");
            }

        }
        catch (Exception ex)
        {
            _output.WriteLine($"❌ エラー: {ex.Message}");
            if (ex.InnerException != null)
            {
                _output.WriteLine($"   詳細: {ex.InnerException.Message}");
            }
            throw;
        }
    }
}