using System.Drawing;

namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// OCR処理結果の統合モデル
/// 各種OCRエンジンの結果を統一した形式で表現
/// Issue #143 Week 3 Phase 2: パフォーマンス最適化統合
/// </summary>
public class OcrResult
{
    /// <summary>
    /// 検出されたテキスト一覧
    /// </summary>
    public IReadOnlyList<DetectedText> DetectedTexts { get; init; } = Array.Empty<DetectedText>();
    
    /// <summary>
    /// 処理が成功したかどうか
    /// </summary>
    public bool IsSuccessful { get; init; }
    
    /// <summary>
    /// 処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }
    
    /// <summary>
    /// エラーメッセージ（失敗時）
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>
    /// 追加メタデータ
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
    
    /// <summary>
    /// 全体の信頼度スコア
    /// </summary>
    public double OverallConfidence => DetectedTexts.Any() ? 
        DetectedTexts.Average(t => t.Confidence) : 0.0;
    
    /// <summary>
    /// 検出されたテキスト総数
    /// </summary>
    public int TextCount => DetectedTexts.Count;
    
    /// <summary>
    /// 全テキストを結合した文字列
    /// </summary>
    public string CombinedText => string.Join(" ", DetectedTexts.Select(t => t.Text));
    
    /// <summary>
    /// 高信頼度テキスト（閾値以上）
    /// </summary>
    /// <param name="threshold">信頼度閾値（デフォルト: 0.8）</param>
    /// <returns>高信頼度テキスト一覧</returns>
    public IEnumerable<DetectedText> GetHighConfidenceTexts(double threshold = 0.8)
    {
        return DetectedTexts.Where(t => t.Confidence >= threshold);
    }
    
    /// <summary>
    /// 特定の言語のテキストを取得
    /// </summary>
    /// <param name="language">言語コード（例: "ja", "en"）</param>
    /// <returns>指定言語のテキスト一覧</returns>
    public IEnumerable<DetectedText> GetTextsByLanguage(string language)
    {
        return DetectedTexts.Where(t => 
            string.Equals(t.Language, language, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// 指定範囲内のテキストを取得
    /// </summary>
    /// <param name="region">検索範囲</param>
    /// <returns>範囲内のテキスト一覧</returns>
    public IEnumerable<DetectedText> GetTextsInRegion(Rectangle region)
    {
        return DetectedTexts.Where(t => region.IntersectsWith(t.BoundingBox));
    }
}

/// <summary>
/// 検出されたテキスト情報
/// </summary>
public class DetectedText
{
    /// <summary>
    /// 検出されたテキスト
    /// </summary>
    public string Text { get; init; } = string.Empty;
    
    /// <summary>
    /// 検出信頼度（0.0-1.0）
    /// </summary>
    public double Confidence { get; init; }
    
    /// <summary>
    /// テキストの境界ボックス
    /// </summary>
    public Rectangle BoundingBox { get; init; }
    
    /// <summary>
    /// 検出言語
    /// </summary>
    public string? Language { get; init; }
    
    /// <summary>
    /// 使用された処理手法
    /// </summary>
    public OptimizationTechnique ProcessingTechnique { get; init; }
    
    /// <summary>
    /// 個別処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }
    
    /// <summary>
    /// テキストの詳細領域（4つの角の座標）
    /// </summary>
    public PointF[]? DetailedRegion { get; init; }
    
    /// <summary>
    /// 回転角度（度）
    /// </summary>
    public float Angle { get; init; }
    
    /// <summary>
    /// フォント情報（推定）
    /// </summary>
    public FontInfo? EstimatedFont { get; init; }
    
    /// <summary>
    /// 追加メタデータ
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
    
    /// <summary>
    /// テキストが高品質かどうか
    /// </summary>
    public bool IsHighQuality => Confidence >= 0.8 && Text.Length >= 2;
    
    /// <summary>
    /// テキストが数値かどうか
    /// </summary>
    public bool IsNumeric => double.TryParse(Text, out _);
    
    /// <summary>
    /// テキストがアルファベットかどうか
    /// </summary>
    public bool IsAlphabetic => Text.All(c => char.IsLetter(c) || char.IsWhiteSpace(c));
    
    /// <summary>
    /// テキストが日本語を含むかどうか
    /// </summary>
    public bool ContainsJapanese => Text.Any(c => 
        (c >= '\u3040' && c <= '\u309F') || // ひらがな
        (c >= '\u30A0' && c <= '\u30FF') || // カタカナ
        (c >= '\u4E00' && c <= '\u9FAF'));  // 漢字
}

/// <summary>
/// フォント情報
/// </summary>
public class FontInfo
{
    /// <summary>
    /// 推定フォントサイズ
    /// </summary>
    public float EstimatedSize { get; init; }
    
    /// <summary>
    /// 太字かどうか
    /// </summary>
    public bool IsBold { get; init; }
    
    /// <summary>
    /// 斜体かどうか
    /// </summary>
    public bool IsItalic { get; init; }
    
    /// <summary>
    /// 推定フォント名
    /// </summary>
    public string? EstimatedFontName { get; init; }
}

/// <summary>
/// 最適化技術の種類
/// </summary>
public enum OptimizationTechnique
{
    /// <summary>
    /// 最適化なし（CPU基本処理）
    /// </summary>
    None,
    
    /// <summary>
    /// GPU加速のみ
    /// </summary>
    GpuOnly,
    
    /// <summary>
    /// ROIのみ
    /// </summary>
    RoiOnly,
    
    /// <summary>
    /// GPU + ROI統合
    /// </summary>
    GpuRoiIntegrated,
    
    /// <summary>
    /// TDR保護付きGPU
    /// </summary>
    GpuWithTdrProtection,
    
    /// <summary>
    /// 完全統合最適化
    /// </summary>
    FullyIntegrated,
    
    /// <summary>
    /// CPUフォールバック
    /// </summary>
    CpuFallback
}