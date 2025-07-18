using System.Threading.Tasks;

namespace Baketa.Infrastructure.OCR.PostProcessing;

/// <summary>
/// OCR結果の後処理を行うインターフェース
/// </summary>
public interface IOcrPostProcessor
{
    /// <summary>
    /// OCR認識結果のテキストを後処理して精度を向上させる
    /// </summary>
    /// <param name="rawText">OCRで認識された生のテキスト</param>
    /// <param name="confidence">認識信頼度（0.0～1.0）</param>
    /// <returns>後処理されたテキスト</returns>
    Task<string> ProcessAsync(string rawText, float confidence);
    
    /// <summary>
    /// よくある誤認識パターンを修正
    /// </summary>
    /// <param name="text">入力テキスト</param>
    /// <returns>修正されたテキスト</returns>
    string CorrectCommonErrors(string text);
    
    /// <summary>
    /// 後処理統計を取得
    /// </summary>
    /// <returns>修正回数などの統計情報</returns>
    PostProcessingStats GetStats();
}

/// <summary>
/// 後処理統計情報
/// </summary>
public sealed class PostProcessingStats
{
    /// <summary>
    /// 処理した文字列の総数
    /// </summary>
    public int TotalProcessed { get; init; }
    
    /// <summary>
    /// 修正が適用された回数
    /// </summary>
    public int CorrectionsApplied { get; init; }
    
    /// <summary>
    /// 最も多く修正されたパターンのトップ5
    /// </summary>
    public Dictionary<string, int> TopCorrectionPatterns { get; init; } = [];
}
