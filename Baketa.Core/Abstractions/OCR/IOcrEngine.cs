using System.Drawing;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// OCR処理のフェーズ
/// </summary>
public enum OcrPhase
{
    /// <summary>
    /// 初期化フェーズ
    /// </summary>
    Initializing = 0,
    
    /// <summary>
    /// 前処理フェーズ（画像処理）
    /// </summary>
    Preprocessing = 1,
    
    /// <summary>
    /// テキスト検出フェーズ
    /// </summary>
    TextDetection = 2,
    
    /// <summary>
    /// テキスト認識フェーズ
    /// </summary>
    TextRecognition = 3,
    
    /// <summary>
    /// 後処理フェーズ
    /// </summary>
    PostProcessing = 4,
    
    /// <summary>
    /// 完了
    /// </summary>
    Completed = 5
}

/// <summary>
/// OCR処理の進捗状況を表すクラス
/// </summary>
public class OcrProgress(double progress, string status)
{
    /// <summary>
    /// 進捗率（0.0～1.0）
    /// </summary>
    public double Progress { get; init; } = Math.Clamp(progress, 0.0, 1.0);

    /// <summary>
    /// 現在の処理ステータス
    /// </summary>
    public string Status { get; init; } = status ?? string.Empty;

    /// <summary>
    /// 現在の処理フェーズ
    /// </summary>
    public OcrPhase Phase { get; init; } = OcrPhase.Initializing;
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
public class OcrResults(
    IReadOnlyList<OcrTextRegion> textRegions,
    IImage sourceImage,
    TimeSpan processingTime,
    string languageCode,
    Rectangle? regionOfInterest = null,
    string? mergedText = null)
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
    /// 画像内のすべてのテキストを結合
    /// 高度なテキスト結合アルゴリズムが適用されている場合はその結果、そうでなければ改行区切り結合
    /// </summary>
    public string Text => mergedText ?? string.Join(Environment.NewLine, TextRegions.Select(r => r.Text));
    
    /// <summary>
    /// 有効なテキストが検出されているかどうか
    /// </summary>
    public bool HasText => TextRegions.Count > 0 && TextRegions.Any(r => !string.IsNullOrWhiteSpace(r.Text));

    /// <summary>
    /// レイアウト情報を活用してテキストをグループ化して結合
    /// 文章のまとまりを保持した結合テキストを返す
    /// </summary>
    /// <param name="preserveParagraphs">段落区切りを保持するか</param>
    /// <param name="sameLineThreshold">同じ行と判定する閾値</param>
    /// <param name="paragraphSeparationThreshold">段落区切りと判定する閾値</param>
    /// <returns>グループ化されたテキスト</returns>
    public string GetGroupedText(bool preserveParagraphs = true, double sameLineThreshold = 0.5, double paragraphSeparationThreshold = 1.5)
    {
        if (!HasText)
            return string.Empty;

        // 簡易版のグループ化ロジック（Infrastructure層の依存関係を避けるため）
        var sortedRegions = TextRegions
            .OrderBy(r => r.Bounds.Y)
            .ThenBy(r => r.Bounds.X)
            .ToList();

        var lines = new List<List<OcrTextRegion>>();
        var currentLine = new List<OcrTextRegion>();

        foreach (var region in sortedRegions)
        {
            if (currentLine.Count == 0)
            {
                currentLine.Add(region);
                continue;
            }

            var lastRegion = currentLine.Last();
            var verticalDistance = Math.Abs(region.Bounds.Y - lastRegion.Bounds.Y);
            var averageHeight = (region.Bounds.Height + lastRegion.Bounds.Height) / 2.0;

            if (verticalDistance <= averageHeight * sameLineThreshold)
            {
                currentLine.Add(region);
            }
            else
            {
                if (currentLine.Count > 0)
                {
                    lines.Add(currentLine);
                }
                currentLine = [region];
            }
        }

        if (currentLine.Count > 0)
        {
            lines.Add(currentLine);
        }

        if (!preserveParagraphs)
        {
            // 行単位で結合
            return string.Join(Environment.NewLine, lines.Select(line => GetLineText(line)));
        }

        // 段落単位でグループ化
        var paragraphs = new List<List<List<OcrTextRegion>>>();
        var currentParagraph = new List<List<OcrTextRegion>>();

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            
            if (currentParagraph.Count == 0)
            {
                currentParagraph.Add(line);
                continue;
            }

            if (i > 0)
            {
                var previousLine = lines[i - 1];
                var currentLineTop = line.Min(r => r.Bounds.Y);
                var previousLineBottom = previousLine.Max(r => r.Bounds.Bottom);
                var verticalGap = currentLineTop - previousLineBottom;
                var averageLineHeight = (GetLineHeight(line) + GetLineHeight(previousLine)) / 2.0;

                if (verticalGap >= averageLineHeight * paragraphSeparationThreshold)
                {
                    if (currentParagraph.Count > 0)
                    {
                        paragraphs.Add(currentParagraph);
                    }
                    currentParagraph = [line];
                    continue;
                }
            }

            currentParagraph.Add(line);
        }

        if (currentParagraph.Count > 0)
        {
            paragraphs.Add(currentParagraph);
        }

        // 段落を2つの改行で区切る
        return string.Join(Environment.NewLine + Environment.NewLine, 
            paragraphs.Select(p => string.Join(Environment.NewLine, p.Select(GetLineText))));
    }

    private static string GetLineText(List<OcrTextRegion> line)
    {
        if (line.Count == 0)
            return string.Empty;

        if (line.Count == 1)
            return line[0].Text;

        // 横方向に並んだテキストを適切な間隔で結合
        var sortedLine = line.OrderBy(r => r.Bounds.X).ToList();
        var result = new List<string>();
        var averageCharWidth = sortedLine.Average(r => r.Bounds.Width / Math.Max(1, r.Text.Length));

        for (int i = 0; i < sortedLine.Count; i++)
        {
            result.Add(sortedLine[i].Text);

            if (i < sortedLine.Count - 1)
            {
                var currentRegion = sortedLine[i];
                var nextRegion = sortedLine[i + 1];
                var horizontalGap = nextRegion.Bounds.Left - currentRegion.Bounds.Right;

                // 文字幅の0.3倍以上の間隔がある場合はスペースを挿入
                if (horizontalGap >= averageCharWidth * 0.3)
                {
                    result.Add(" ");
                }
            }
        }

        return string.Join("", result);
    }

    private static double GetLineHeight(List<OcrTextRegion> line)
    {
        return line.Count > 0 ? line.Average(r => r.Bounds.Height) : 0;
    }
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
    /// より低い値で広範囲のテキスト領域を検出
    /// </summary>
    public double DetectionThreshold { get; set; } = 0.6;
    
    /// <summary>
    /// テキスト認識の信頼度閾値（0.0～1.0）
    /// より低い値で文字結合を促進し、完全なフレーズ認識を向上
    /// </summary>
    public double RecognitionThreshold { get; set; } = 0.3;
    
    /// <summary>
    /// 使用するモデル名
    /// </summary>
    public string ModelName { get; set; } = "standard";
    
    /// <summary>
    /// 最大テキスト検出数
    /// </summary>
    public int MaxDetections { get; set; } = 200;
    
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
    /// GPU最大メモリ使用量（MB）- ゲーム競合回避用
    /// </summary>
    public int MaxGpuMemoryMB { get; set; } = 2048; // デフォルト2GB
    
    /// <summary>
    /// GPUメモリ使用量監視を有効にするか
    /// </summary>
    public bool EnableGpuMemoryMonitoring { get; set; } = true;
    
    /// <summary>
    /// マルチスレッド処理を有効にするか
    /// </summary>
    public bool EnableMultiThread { get; set; }
    
    /// <summary>
    /// マルチスレッド時のワーカー数
    /// </summary>
    public int WorkerCount { get; set; } = 2;
    
    /// <summary>
    /// 言語モデルを使用するか（PaddleOCR use_lm=True）
    /// </summary>
    public bool UseLanguageModel { get; set; }
    
    /// <summary>
    /// 前処理を有効にするか
    /// </summary>
    public bool EnablePreprocessing { get; set; } = true;

    /// <summary>
    /// ハイブリッドモードを有効にするか（V3高速検出 + V5高精度認識）
    /// </summary>
    public bool EnableHybridMode { get; set; }

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
            
        if (MaxGpuMemoryMB < 128 || MaxGpuMemoryMB > 16384) // 128MB～16GB
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
            MaxGpuMemoryMB = MaxGpuMemoryMB,
            EnableGpuMemoryMonitoring = EnableGpuMemoryMonitoring,
            EnableMultiThread = EnableMultiThread,
            WorkerCount = WorkerCount,
            UseLanguageModel = UseLanguageModel,
            EnablePreprocessing = EnablePreprocessing,
            EnableHybridMode = EnableHybridMode
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
    /// エンジンのウォームアップを実行（初回実行時の遅延を解消）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>ウォームアップが成功した場合はtrue</returns>
    Task<bool> WarmupAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 画像からテキストを認識します
    /// </summary>
    /// <param name="image">画像</param>
    /// <param name="progressCallback">進捗通知コールバック（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>OCR結果</returns>
    Task<OcrResults> RecognizeAsync(
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
    Task<OcrResults> RecognizeAsync(
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
    
    /// <summary>
    /// 進行中のOCRタイムアウト処理をキャンセル
    /// 翻訳結果が表示された際に呼び出されます
    /// </summary>
    void CancelCurrentOcrTimeout();
    
    /// <summary>
    /// テキスト検出のみを実行（認識処理をスキップ）
    /// AdaptiveTileStrategy等での高速テキスト領域検出用
    /// </summary>
    /// <param name="image">画像</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>検出されたテキスト領域（テキスト内容は空またはダミー）</returns>
    Task<OcrResults> DetectTextRegionsAsync(
        IImage image,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 連続失敗回数を取得（診断・フォールバック判定用）
    /// </summary>
    /// <returns>連続失敗回数</returns>
    int GetConsecutiveFailureCount();

    /// <summary>
    /// 失敗カウンタをリセット（緊急時復旧用）
    /// </summary>
    void ResetFailureCounter();
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
