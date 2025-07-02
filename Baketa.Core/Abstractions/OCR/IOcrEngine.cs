using System.Drawing;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// OCR処理の進捗状況を表すクラス
/// </summary>
public class OcrProgress(double progress, string status)
{
    /// <summary>
    /// 進捗率（0.0～1.0）
    /// </summary>
    public double Progress { get; } = Math.Clamp(progress, 0.0, 1.0);

    /// <summary>
    /// 現在の処理ステータス
    /// </summary>
    public string Status { get; } = status ?? string.Empty;
}

/// <summary>
/// OCR結果のテキスト領域情報
/// </summary>
public class OcrTextRegion(
    string text,
    Rectangle bounds,
    double confidence,
    Point[]? contour = null,
    TextDirection direction = TextDirection.Horizontal)
{
    /// <summary>
    /// 認識されたテキスト
    /// </summary>
    public string Text { get; } = text ?? string.Empty;

    /// <summary>
    /// テキスト領域の境界矩形（元画像座標系）
    /// </summary>
    public Rectangle Bounds { get; } = bounds;

    /// <summary>
    /// 認識信頼度（0.0～1.0）
    /// </summary>
    public double Confidence { get; } = Math.Clamp(confidence, 0.0, 1.0);

    /// <summary>
    /// テキスト領域の詳細な輪郭点（オプション）
    /// </summary>
    public Point[]? Contour { get; } = contour;

    /// <summary>
    /// テキストの推定方向（将来の方向分類モデル用）
    /// </summary>
    public TextDirection Direction { get; } = direction;
}

/// <summary>
/// テキストの方向（将来拡張用）
/// </summary>
public enum TextDirection
{
    /// <summary>
    /// 水平（左から右）
    /// </summary>
    Horizontal,
    
    /// <summary>
    /// 垂直（上から下）
    /// </summary>
    Vertical,
    
    /// <summary>
    /// 180度回転
    /// </summary>
    Rotated180,
    
    /// <summary>
    /// 不明
    /// </summary>
    Unknown
}

/// <summary>
/// OCR結果を表すクラス
/// </summary>
public class OcrResultCollection(
    IReadOnlyList<OcrTextRegion> textRegions,
    IImage sourceImage,
    TimeSpan processingTime,
    string languageCode,
    Rectangle? regionOfInterest = null)
{
    /// <summary>
    /// 認識されたテキスト領域のリスト
    /// </summary>
    public IReadOnlyList<OcrTextRegion> TextRegions { get; } = textRegions ?? throw new ArgumentNullException(nameof(textRegions));

    /// <summary>
    /// 処理対象の画像（または指定されたROI）
    /// </summary>
    public IImage SourceImage { get; } = sourceImage ?? throw new ArgumentNullException(nameof(sourceImage));

    /// <summary>
    /// 指定されたROI（画像全体の場合はnull）
    /// </summary>
    public Rectangle? RegionOfInterest { get; } = regionOfInterest;

    /// <summary>
    /// OCR処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; } = processingTime;

    /// <summary>
    /// 使用された言語コード
    /// </summary>
    public string LanguageCode { get; } = languageCode ?? throw new ArgumentNullException(nameof(languageCode));

    /// <summary>
    /// 画像内のすべてのテキストを結合（改行区切り）
    /// </summary>
    public string Text => string.Join(Environment.NewLine, TextRegions.Select(r => r.Text));
    
    /// <summary>
    /// 有効なテキストが検出されているかどうか
    /// </summary>
    public bool HasText => TextRegions.Count > 0 && TextRegions.Any(r => !string.IsNullOrWhiteSpace(r.Text));
}

/// <summary>
/// OCRエンジンの設定
/// </summary>
public class OcrEngineSettings
{
    /// <summary>
    /// 認識する言語コード
    /// </summary>
    public string Language { get; set; } = "jpn"; // デフォルトは日本語
    
    /// <summary>
    /// テキスト検出の信頼度閾値（0.0～1.0）
    /// </summary>
    public double DetectionThreshold { get; set; } = 0.3;
    
    /// <summary>
    /// テキスト認識の信頼度閾値（0.0～1.0）
    /// </summary>
    public double RecognitionThreshold { get; set; } = 0.5;
    
    /// <summary>
    /// 使用するモデル名
    /// </summary>
    public string ModelName { get; set; } = "standard";
    
    /// <summary>
    /// 最大テキスト検出数
    /// </summary>
    public int MaxDetections { get; set; } = 100;
    
    /// <summary>
    /// 方向分類を使用するか（将来拡張用）
    /// </summary>
    public bool UseDirectionClassification { get; set; }
    
    /// <summary>
    /// GPU使用設定（将来拡張用）
    /// </summary>
    public bool UseGpu { get; set; }
    
    /// <summary>
    /// GPUデバイスID（将来拡張用）
    /// </summary>
    public int GpuDeviceId { get; set; }
    
    /// <summary>
    /// マルチスレッド処理を有効にするか
    /// </summary>
    public bool EnableMultiThread { get; set; }
    
    /// <summary>
    /// マルチスレッド時のワーカー数
    /// </summary>
    public int WorkerCount { get; set; } = 2;

    /// <summary>
    /// 設定の妥当性を検証する
    /// </summary>
    /// <returns>妥当性チェック結果</returns>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Language))
            return false;
            
        if (DetectionThreshold < 0.0 || DetectionThreshold > 1.0)
            return false;
            
        if (RecognitionThreshold < 0.0 || RecognitionThreshold > 1.0)
            return false;
            
        if (string.IsNullOrWhiteSpace(ModelName))
            return false;
            
        if (MaxDetections < 1 || MaxDetections > 1000)
            return false;
            
        if (GpuDeviceId < 0)
            return false;
            
        if (WorkerCount < 1 || WorkerCount > 10)
            return false;
            
        return true;
    }

    /// <summary>
    /// 設定のクローンを作成する
    /// </summary>
    /// <returns>設定のコピー</returns>
    public OcrEngineSettings Clone()
    {
        return new OcrEngineSettings
        {
            Language = Language,
            DetectionThreshold = DetectionThreshold,
            RecognitionThreshold = RecognitionThreshold,
            ModelName = ModelName,
            MaxDetections = MaxDetections,
            UseDirectionClassification = UseDirectionClassification,
            UseGpu = UseGpu,
            GpuDeviceId = GpuDeviceId,
            EnableMultiThread = EnableMultiThread,
            WorkerCount = WorkerCount
        };
    }
}

/// <summary>
/// OCRエンジンの例外
/// </summary>
public class OcrException : Exception
{
    public OcrException() { }
    
    public OcrException(string message) : base(message) { }
    
    public OcrException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// OCRエンジンインターフェース
/// </summary>
public interface IOcrEngine : IDisposable
{
    /// <summary>
    /// OCRエンジンの名前
    /// </summary>
    string EngineName { get; }
    
    /// <summary>
    /// OCRエンジンのバージョン
    /// </summary>
    string EngineVersion { get; }
    
    /// <summary>
    /// エンジンが初期化済みかどうか
    /// </summary>
    bool IsInitialized { get; }
    
    /// <summary>
    /// 現在の言語設定
    /// </summary>
    string? CurrentLanguage { get; }

    /// <summary>
    /// OCRエンジンを初期化します
    /// </summary>
    /// <param name="settings">エンジン設定（省略時はデフォルト設定）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>初期化が成功した場合はtrue</returns>
    Task<bool> InitializeAsync(OcrEngineSettings? settings = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 画像からテキストを認識します
    /// </summary>
    /// <param name="image">画像</param>
    /// <param name="progressCallback">進捗通知コールバック（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>OCR結果</returns>
    Task<OcrResultCollection> RecognizeAsync(
        IImage image,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 画像の指定領域からテキストを認識します（ゲームOCR最重要機能）
    /// </summary>
    /// <param name="image">画像</param>
    /// <param name="regionOfInterest">認識領域（nullの場合は画像全体）</param>
    /// <param name="progressCallback">進捗通知コールバック（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>OCR結果</returns>
    Task<OcrResultCollection> RecognizeAsync(
        IImage image,
        Rectangle? regionOfInterest,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// OCRエンジンの設定を取得します
    /// </summary>
    /// <returns>現在の設定</returns>
    OcrEngineSettings GetSettings();
    
    /// <summary>
    /// OCRエンジンの設定を適用します
    /// </summary>
    /// <param name="settings">設定</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task ApplySettingsAsync(OcrEngineSettings settings, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 使用可能な言語のリストを取得します
    /// </summary>
    /// <returns>言語コードのリスト</returns>
    IReadOnlyList<string> GetAvailableLanguages();
    
    /// <summary>
    /// 使用可能なモデルのリストを取得します
    /// </summary>
    /// <returns>モデル名のリスト</returns>
    IReadOnlyList<string> GetAvailableModels();
    
    /// <summary>
    /// 指定言語のモデルが利用可能かを確認します
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>利用可能な場合はtrue</returns>
    Task<bool> IsLanguageAvailableAsync(string languageCode, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// エンジンのパフォーマンス統計を取得
    /// </summary>
    /// <returns>パフォーマンス統計</returns>
    OcrPerformanceStats GetPerformanceStats();
}

/// <summary>
/// OCRエンジンのパフォーマンス統計
/// </summary>
public class OcrPerformanceStats
{
    /// <summary>
    /// 処理した画像の総数
    /// </summary>
    public int TotalProcessedImages { get; init; }
    
    /// <summary>
    /// 平均処理時間（ミリ秒）
    /// </summary>
    public double AverageProcessingTimeMs { get; init; }
    
    /// <summary>
    /// 最小処理時間（ミリ秒）
    /// </summary>
    public double MinProcessingTimeMs { get; init; }
    
    /// <summary>
    /// 最大処理時間（ミリ秒）
    /// </summary>
    public double MaxProcessingTimeMs { get; init; }
    
    /// <summary>
    /// エラー回数
    /// </summary>
    public int ErrorCount { get; init; }
    
    /// <summary>
    /// 成功率（0.0～1.0）
    /// </summary>
    public double SuccessRate { get; init; }
    
    /// <summary>
    /// 統計開始時刻
    /// </summary>
    public DateTime StartTime { get; init; }
    
    /// <summary>
    /// 最終更新時刻
    /// </summary>
    public DateTime LastUpdateTime { get; init; }
}
