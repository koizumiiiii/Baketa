using System;
using System.IO;
using System.Threading.Tasks;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation;

/// <summary>
/// 各SentencePieceモデルの語彙サイズを分析するテスト
/// </summary>
public class VocabSizeAnalysisTest
{
    private readonly ITestOutputHelper _output;

    public VocabSizeAnalysisTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AnalyzeAllSentencePieceModels_FindCorrectTargetTokenizer()
    {
        _output.WriteLine("=== SentencePiece モデル語彙サイズ分析 ===");
        
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Information));

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
            (@"E:\dev\Baketa\Models\SentencePiece\helsinki-opus-mt-ja-en.model", "Helsinki SentencePiece"),
            
            // 追加候補：英日モデル（逆方向）
            (@"E:\dev\Baketa\Models\SentencePiece\opus-mt-en-ja.model", "英日ソース"),
            (@"E:\dev\Baketa\Models\HuggingFace\opus-mt-en-ja\pytorch_model.bin", "HuggingFace 英日模型"),
            
            // 追加候補：その他のSentencePieceモデル
            (@"E:\dev\Baketa\Models\SentencePiece\opus-mt-en-jap.model", "英日本語モデル"),
            (@"E:\dev\Baketa\Models\SentencePiece\opus-mt-zh-en.model", "中英モデル"),
            (@"E:\dev\Baketa\Models\SentencePiece\opus-mt-tc-big-zh-ja.model", "中日大型モデル"),
            (@"E:\dev\Baketa\Models\SentencePiece\test-ja-en.model", "テスト日英モデル"),
            (@"E:\dev\Baketa\Models\SentencePiece\test-en-ja.model", "テスト英日モデル")
        };

        _output.WriteLine($"分析対象モデル数: {modelsToAnalyze.Length}");
        _output.WriteLine($"目標語彙サイズ: 60,716 (ONNXモデルと一致)");
        _output.WriteLine("");

        string? bestMatchPath = null;
        int bestMatchVocabSize = 0;
        double bestMatchScore = 0.0;

        foreach (var (modelPath, description) in modelsToAnalyze)
        {
            var analysisResult = AnalyzeModel(modelPath, description, loggerFactory);
            
            if (analysisResult.Success && analysisResult.MatchScore > bestMatchScore)
            {
                bestMatchPath = modelPath;
                bestMatchVocabSize = analysisResult.VocabSize;
                bestMatchScore = analysisResult.MatchScore;
            }
            
            _output.WriteLine("");
        }

        _output.WriteLine("=== 分析結果サマリー ===");
        if (bestMatchPath != null)
        {
            _output.WriteLine($"🎯 最適なターゲットTokenizer:");
            _output.WriteLine($"パス: {bestMatchPath}");
            _output.WriteLine($"語彙サイズ: {bestMatchVocabSize:N0}");
            _output.WriteLine($"一致度: {bestMatchScore:F2}%");
        }
        else
        {
            _output.WriteLine("❌ 適切なTokenizerが見つかりませんでした");
        }

        await Task.CompletedTask;
    }

    private AnalysisResult AnalyzeModel(string modelPath, string description, ILoggerFactory loggerFactory)
    {
        try
        {
            _output.WriteLine($"=== {description} ===");
            _output.WriteLine($"パス: {modelPath}");
            
            if (!File.Exists(modelPath))
            {
                _output.WriteLine("❌ ファイルが存在しません");
                return new AnalysisResult { Success = false };
            }

            var fileInfo = new FileInfo(modelPath);
            _output.WriteLine($"ファイルサイズ: {fileInfo.Length:N0} bytes");
            
            // SentencePieceトークナイザーを作成
            var logger = loggerFactory.CreateLogger<OpusMtNativeTokenizer>();
            var tokenizer = new OpusMtNativeTokenizer(modelPath, "Vocab Analysis", logger);
            
            _output.WriteLine($"✅ 語彙サイズ: {tokenizer.VocabularySize:N0}");
            
            // 特殊トークンID確認
            var bosId = tokenizer.GetSpecialTokenId("BOS");
            var eosId = tokenizer.GetSpecialTokenId("EOS");
            var unkId = tokenizer.GetSpecialTokenId("UNK");
            var padId = tokenizer.GetSpecialTokenId("PAD");
            
            _output.WriteLine($"特殊トークン - BOS: {bosId}, EOS: {eosId}, UNK: {unkId}, PAD: {padId}");
            
            // ONNXモデルとの一致度チェック
            const int targetVocabSize = 60716;
            var difference = Math.Abs(tokenizer.VocabularySize - targetVocabSize);
            var matchPercentage = (1.0 - (double)difference / targetVocabSize) * 100;
            
            _output.WriteLine($"ONNX一致度: {matchPercentage:F2}% (差分: {difference:N0})");
            
            if (tokenizer.VocabularySize == targetVocabSize)
            {
                _output.WriteLine("🎯 PERFECT MATCH! このモデルを使用すべきです");
            }
            else if (difference < 1000)
            {
                _output.WriteLine("✅ 非常に近い - 使用可能な候補");
            }
            else if (difference < 10000)
            {
                _output.WriteLine("⚠️ やや差がある - 要検討");
            }
            else
            {
                _output.WriteLine("❌ 大きな差 - 使用非推奨");
            }
            
            // テストトークン化
            var testText = "テスト";
            var tokens = tokenizer.Tokenize(testText);
            _output.WriteLine($"テスト '{testText}' -> [{string.Join(", ", tokens)}]");

            return new AnalysisResult
            {
                Success = true,
                VocabSize = tokenizer.VocabularySize,
                MatchScore = matchPercentage,
                ModelPath = modelPath
            };

        }
        catch (Exception ex)
        {
            _output.WriteLine($"❌ エラー: {ex.Message}");
            if (ex.InnerException != null)
            {
                _output.WriteLine($"   詳細: {ex.InnerException.Message}");
            }
            return new AnalysisResult { Success = false };
        }
    }

    private record AnalysisResult
    {
        public bool Success { get; init; }
        public int VocabSize { get; init; }
        public double MatchScore { get; init; }
        public string ModelPath { get; init; } = string.Empty;
    }
}