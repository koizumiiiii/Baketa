using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Infrastructure.OCR.PostProcessing.NgramModels;

namespace Baketa.Infrastructure.OCR.PostProcessing;

/// <summary>
/// N-gramモデルベースのOCR後処理プロセッサ
/// </summary>
public sealed class NgramOcrPostProcessor : IOcrPostProcessor
{
    private readonly ILogger<NgramOcrPostProcessor> _logger;
    private readonly INgramModel _ngramModel;
    private readonly Dictionary<string, List<string>> _confusionMatrix;
    private readonly double _correctionThreshold;
    
    public NgramOcrPostProcessor(
        ILogger<NgramOcrPostProcessor> logger,
        INgramModel ngramModel,
        double correctionThreshold = 0.3)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ngramModel = ngramModel ?? throw new ArgumentNullException(nameof(ngramModel));
        _correctionThreshold = correctionThreshold;
        _confusionMatrix = InitializeConfusionMatrix();
    }
    
    /// <summary>
    /// OCR結果テキストの後処理
    /// </summary>
    public async Task<string> ProcessAsync(string rawText, float confidence)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return rawText;
        
        _logger.LogDebug("N-gramベース後処理開始: {Text} (信頼度: {Confidence})", rawText, confidence);
        
        var correctedText = await Task.Run(() => CorrectText(rawText)).ConfigureAwait(false);
        
        _logger.LogDebug("N-gramベース後処理完了: {OriginalText} -> {CorrectedText}", rawText, correctedText);
        
        return correctedText;
    }
    
    /// <summary>
    /// OCR結果テキストの後処理（信頼度なし）
    /// </summary>
    public async Task<string> ProcessAsync(string ocrText)
    {
        return await ProcessAsync(ocrText, 1.0f).ConfigureAwait(false);
    }
    
    /// <summary>
    /// 複数のOCR結果テキストを並行処理
    /// </summary>
    public async Task<IEnumerable<string>> ProcessBatchAsync(IEnumerable<string> ocrTexts)
    {
        var tasks = ocrTexts.Select(text => ProcessAsync(text));
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }
    
    /// <summary>
    /// よくある誤認識パターンを修正
    /// </summary>
    public string CorrectCommonErrors(string text)
    {
        return CorrectText(text);
    }
    
    /// <summary>
    /// 後処理統計を取得
    /// </summary>
    public PostProcessingStats GetStats()
    {
        return new PostProcessingStats
        {
            TotalProcessed = 0, // 統計追跡機能は将来実装
            CorrectionsApplied = 0,
            TopCorrectionPatterns = []
        };
    }
    
    /// <summary>
    /// テキストを修正
    /// </summary>
    private string CorrectText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        
        var corrections = new List<TextCorrection>();
        
        // 各位置での修正候補を検討
        for (int i = 0; i < text.Length; i++)
        {
            var correction = FindBestCorrection(text, i);
            if (correction != null)
            {
                corrections.Add(correction);
            }
        }
        
        // 重複する修正を除去し、最適な修正を選択
        var finalCorrections = SelectBestCorrections(corrections);
        
        // 修正を適用
        return ApplyCorrections(text, finalCorrections);
    }
    
    /// <summary>
    /// 指定位置での最適な修正を探索
    /// </summary>
    private TextCorrection? FindBestCorrection(string text, int position)
    {
        var currentChar = text[position].ToString(System.Globalization.CultureInfo.InvariantCulture);
        
        // 混同行列から候補文字を取得
        if (!_confusionMatrix.TryGetValue(currentChar, out var candidates))
            return null;
        var bestCorrection = (string.Empty, double.MinValue);
        
        foreach (var candidate in candidates)
        {
            var modifiedText = text[..position] + candidate + text[(position + 1)..];
            var originalLikelihood = CalculateLocalLikelihood(text, position);
            var candidateLikelihood = CalculateLocalLikelihood(modifiedText, position);
            
            var improvement = candidateLikelihood - originalLikelihood;
            
            if (improvement > bestCorrection.MinValue && improvement > _correctionThreshold)
            {
                bestCorrection = (candidate, improvement);
            }
        }
        
        if (bestCorrection.MinValue > double.MinValue)
        {
            return new TextCorrection(position, 1, bestCorrection.Empty, bestCorrection.MinValue);
        }
        
        return null;
    }
    
    /// <summary>
    /// 局所的な尤度を計算
    /// </summary>
    private double CalculateLocalLikelihood(string text, int position)
    {
        var startPos = Math.Max(0, position - 2);
        var endPos = Math.Min(text.Length, position + 3);
        var localText = text[startPos..endPos];
        
        return _ngramModel.CalculateLikelihood(localText);
    }
    
    /// <summary>
    /// 最適な修正の組み合わせを選択
    /// </summary>
    private List<TextCorrection> SelectBestCorrections(List<TextCorrection> corrections)
    {
        // 重複する修正を除去（より高いスコアの修正を優先）
        var nonOverlapping = new List<TextCorrection>();
        var sortedCorrections = corrections.OrderByDescending(c => c.Score).ToList();
        
        foreach (var correction in sortedCorrections)
        {
            var overlaps = nonOverlapping.Any(existing => 
                correction.Position < existing.Position + existing.Length &&
                correction.Position + correction.Length > existing.Position);
            
            if (!overlaps)
            {
                nonOverlapping.Add(correction);
            }
        }
        
        return [.. nonOverlapping.OrderBy(c => c.Position)];
    }
    
    /// <summary>
    /// 修正をテキストに適用
    /// </summary>
    private string ApplyCorrections(string text, List<TextCorrection> corrections)
    {
        if (corrections.Count == 0)
            return text;
        
        var result = text;
        var offset = 0;
        
        foreach (var correction in corrections)
        {
            var adjustedPosition = correction.Position + offset;
            var before = result[..adjustedPosition];
            var after = result[(adjustedPosition + correction.Length)..];
            
            result = before + correction.ReplacementText + after;
            offset += correction.ReplacementText.Length - correction.Length;
            
            _logger.LogDebug("修正適用: 位置{Position} '{Original}' -> '{Corrected}' (スコア: {Score:F3})", 
                correction.Position, 
                text.Substring(correction.Position, correction.Length), 
                correction.ReplacementText, 
                correction.Score);
        }
        
        return result;
    }
    
    /// <summary>
    /// 混同行列を初期化
    /// </summary>
    private Dictionary<string, List<string>> InitializeConfusionMatrix()
    {
        return new Dictionary<string, List<string>>
        {
            // 日本語漢字の混同パターン
            ["車"] = ["単"],
            ["単"] = ["車"],
            ["役"] = ["設"],
            ["設"] = ["役"],
            ["恐"] = ["設"],
            ["院"] = ["魔"],
            ["魔"] = ["院"],
            ["勝"] = ["験"],
            ["験"] = ["勝"],
            ["体"] = ["体"],
            ["計"] = ["計"],
            
            // ひらがな・カタカナの混同パターン
            ["ツ"] = ["ツ", "シ"],
            ["シ"] = ["シ", "ツ"],
            ["ソ"] = ["ソ", "ン"],
            ["ン"] = ["ン", "ソ"],
            ["デ"] = ["デ", "ア"],
            ["ア"] = ["ア", "デ"],
            ["イ"] = ["イ", "ィ"],
            ["ィ"] = ["ィ", "イ"],
            ["グ"] = ["グ", "ク"],
            ["ク"] = ["ク", "グ"],
            
            // 英数字の混同パターン
            ["0"] = ["O", "o"],
            ["O"] = ["0", "o"],
            ["o"] = ["0", "O"],
            ["1"] = ["l", "I"],
            ["l"] = ["1", "I"],
            ["I"] = ["1", "l"],
            ["8"] = ["B"],
            ["B"] = ["8"],
            ["5"] = ["S"],
            ["S"] = ["5"],
            
            // 形状が似ている文字
            ["未"] = ["末"],
            ["末"] = ["未"],
            ["人"] = ["八"],
            ["八"] = ["人"],
            ["日"] = ["目"],
            ["目"] = ["日"],
            ["土"] = ["士"],
            ["士"] = ["土"],
        };
    }
    
    /// <summary>
    /// 日本語文字学習用のサンプルデータを生成
    /// </summary>
    public static IEnumerable<string> GetJapaneseSampleTexts()
    {
        return
        [
            // 技術用語
            "単体テスト",
            "設計書",
            "データベース",
            "プログラム",
            "システム",
            "ソフトウェア",
            "アルゴリズム",
            "インターフェース",
            "オブジェクト指向",
            "フレームワーク",
            
            // 業務用語
            "プロジェクト管理",
            "要件定義",
            "仕様書",
            "テストケース",
            "バグ修正",
            "コードレビュー",
            "デプロイメント",
            "メンテナンス",
            "運用保守",
            "品質保証",
            
            // 一般的な文章
            "これは日本語のテストです",
            "システムの動作確認を行います",
            "データの整合性をチェックします",
            "エラーが発生した場合の対処方法",
            "ユーザーインターフェースの改善",
            "パフォーマンスの最適化",
            "セキュリティの強化",
            "バックアップの作成",
            "ログの解析",
            "監視システムの構築",
            
            // 混在テキスト
            "APIの応答時間",
            "SQLクエリの最適化",
            "HTTPステータスコード",
            "JSONデータの解析",
            "XMLファイルの処理",
            "REST APIの設計",
            "WebSocketの実装",
            "OAuth認証",
            "SSL証明書",
            "DNS設定",
        ];
    }
}

/// <summary>
/// テキスト修正情報
/// </summary>
internal sealed class TextCorrection(int position, int length, string replacementText, double score)
{
    public int Position { get; } = position;
    public int Length { get; } = length;
    public string ReplacementText { get; } = replacementText ?? throw new ArgumentNullException(nameof(replacementText));
    public double Score { get; } = score;
}
