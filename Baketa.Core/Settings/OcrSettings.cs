namespace Baketa.Core.Settings;

/// <summary>
/// OCR設定クラス
/// 光学文字認識エンジンの設定を管理
/// </summary>
public sealed class OcrSettings
{
    /// <summary>
    /// OCR機能の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "OCR", "OCR有効", 
        Description = "光学文字認識機能を有効にします")]
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// OCR機能の有効化（別名）
    /// </summary>
    public bool EnableOcr
    {
        get => IsEnabled;
        set => IsEnabled = value;
    }
    
    /// <summary>
    /// 自動最適化の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "OCR", "自動最適化", 
        Description = "OCR処理を自動的に最適化します（推奨）")]
    public bool AutoOptimizationEnabled { get; set; } = true;
    
    /// <summary>
    /// OCRエンジンの種類
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "OCR", "OCRエンジン", 
        Description = "使用するOCRエンジンの種類", 
        ValidValues = [OcrEngine.PaddleOCR, OcrEngine.Tesseract, OcrEngine.WindowsOCR])]
    public OcrEngine Engine { get; set; } = OcrEngine.PaddleOCR;
    
    /// <summary>
    /// 認識言語の設定
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "OCR", "認識言語", 
        Description = "OCRで認識する言語", 
        ValidValues = ["ja", "en", "zh", "ko", "multi"])]
    public string RecognitionLanguage { get; set; } = "ja";
    
    /// <summary>
    /// 認識言語（別名）
    /// </summary>
    public string Language
    {
        get => RecognitionLanguage;
        set => RecognitionLanguage = value;
    }
    
    /// <summary>
    /// 認識信頼度の閾値（0.0-1.0）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "認識信頼度閾値", 
        Description = "この値以下の信頼度の結果は破棄されます", 
        MinValue = 0.0, 
        MaxValue = 1.0)]
    public double ConfidenceThreshold { get; set; } = 0.7;
    
    /// <summary>
    /// 画像前処理の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "画像前処理", 
        Description = "OCR精度向上のための画像前処理を実行します")]
    public bool EnableImagePreprocessing { get; set; } = true;
    
    /// <summary>
    /// グレースケール変換
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "グレースケール変換", 
        Description = "画像をグレースケールに変換して処理速度を向上させます")]
    public bool ConvertToGrayscale { get; set; } = true;
    
    /// <summary>
    /// 画像二値化の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "二値化処理", 
        Description = "画像を白黒に二値化してテキスト認識を向上させます")]
    public bool EnableBinarization { get; set; } = true;
    
    /// <summary>
    /// 二値化閾値（0-255）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "二値化閾値", 
        Description = "二値化処理の閾値（自動の場合は0）", 
        MinValue = 0, 
        MaxValue = 255)]
    public int BinarizationThreshold { get; set; } = 0; // 0 = 自動
    
    /// <summary>
    /// ノイズ除去の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "ノイズ除去", 
        Description = "画像のノイズを除去してOCR精度を向上させます")]
    public bool EnableNoiseReduction { get; set; } = true;
    
    /// <summary>
    /// コントラスト強調
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "コントラスト強調", 
        Description = "画像のコントラストを強調してテキストを鮮明にします")]
    public bool EnhanceContrast { get; set; } = true;
    
    /// <summary>
    /// エッジ強調
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "エッジ強調", 
        Description = "文字の輪郭を強調してOCR精度を向上させます")]
    public bool EnhanceEdges { get; set; } = false;
    
    /// <summary>
    /// 画像拡大率
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "画像拡大率", 
        Description = "OCR処理前の画像拡大倍率", 
        MinValue = 1.0, 
        MaxValue = 4.0)]
    public double ImageScaleFactor { get; set; } = 2.0;
    
    /// <summary>
    /// 並列処理の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "並列処理", 
        Description = "複数の画像を同時に処理して高速化します")]
    public bool EnableParallelProcessing { get; set; } = true;
    
    /// <summary>
    /// 最大並列処理数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "最大並列数", 
        Description = "同時に処理する最大スレッド数", 
        MinValue = 1, 
        MaxValue = 16)]
    public int MaxParallelThreads { get; set; } = 4;
    
    /// <summary>
    /// テキスト領域検出の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "テキスト領域検出", 
        Description = "テキストがある領域のみを検出してOCR処理を最適化します")]
    public bool EnableTextAreaDetection { get; set; } = true;
    
    /// <summary>
    /// テキストフィルタリングの有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "テキストフィルタリング", 
        Description = "不要な文字やノイズをフィルタリングします")]
    public bool EnableTextFiltering { get; set; } = true;
    
    /// <summary>
    /// 最小テキスト行高さ
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "最小行高さ", 
        Description = "認識する最小のテキスト行高さ", 
        Unit = "px", 
        MinValue = 5, 
        MaxValue = 100)]
    public int MinTextLineHeight { get; set; } = 12;
    
    /// <summary>
    /// 最大テキスト行高さ
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "最大行高さ", 
        Description = "認識する最大のテキスト行高さ", 
        Unit = "px", 
        MinValue = 10, 
        MaxValue = 500)]
    public int MaxTextLineHeight { get; set; } = 100;
    
    /// <summary>
    /// OCRタイムアウト時間
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "タイムアウト時間", 
        Description = "OCR処理のタイムアウト時間", 
        Unit = "秒", 
        MinValue = 1, 
        MaxValue = 60)]
    public int TimeoutSeconds { get; set; } = 10;
    
    /// <summary>
    /// 詳細ログ出力の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "OCR", "詳細ログ", 
        Description = "OCR処理の詳細ログを出力します（開発者向け）")]
    public bool EnableVerboseLogging { get; set; } = false;
    
    /// <summary>
    /// OCR結果の保存
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "OCR", "結果保存", 
        Description = "OCR結果をファイルに保存します（開発者向け）")]
    public bool SaveOcrResults { get; set; } = false;
    
    /// <summary>
    /// 処理済み画像の保存
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "OCR", "処理済み画像保存", 
        Description = "前処理後の画像を保存します（開発者向け）")]
    public bool SaveProcessedImages { get; set; } = false;
    
    /// <summary>
    /// テキスト領域検出設定
    /// </summary>
    public TextDetectionSettings TextDetectionSettings { get; set; } = new();
    
    /// <summary>
    /// アンサンブル検出設定
    /// </summary>
    public EnsembleSettings TextDetectionEnsemble { get; set; } = new();
    
    /// <summary>
    /// 設定のクローンを作成します
    /// </summary>
    /// <returns>クローンされた設定</returns>
    public OcrSettings Clone()
    {
        return new OcrSettings
        {
            IsEnabled = IsEnabled,
            AutoOptimizationEnabled = AutoOptimizationEnabled,
            Engine = Engine,
            RecognitionLanguage = RecognitionLanguage,
            ConfidenceThreshold = ConfidenceThreshold,
            EnableImagePreprocessing = EnableImagePreprocessing,
            ConvertToGrayscale = ConvertToGrayscale,
            EnableBinarization = EnableBinarization,
            BinarizationThreshold = BinarizationThreshold,
            EnableNoiseReduction = EnableNoiseReduction,
            EnhanceContrast = EnhanceContrast,
            EnhanceEdges = EnhanceEdges,
            ImageScaleFactor = ImageScaleFactor,
            EnableParallelProcessing = EnableParallelProcessing,
            MaxParallelThreads = MaxParallelThreads,
            EnableTextAreaDetection = EnableTextAreaDetection,
            EnableTextFiltering = EnableTextFiltering,
            MinTextLineHeight = MinTextLineHeight,
            MaxTextLineHeight = MaxTextLineHeight,
            TimeoutSeconds = TimeoutSeconds,
            EnableVerboseLogging = EnableVerboseLogging,
            SaveOcrResults = SaveOcrResults,
            SaveProcessedImages = SaveProcessedImages,
            TextDetectionSettings = TextDetectionSettings,
            TextDetectionEnsemble = TextDetectionEnsemble
        };
    }
}

/// <summary>
/// テキスト領域検出設定
/// </summary>
public class TextDetectionSettings
{
    /// <summary>
    /// 強制使用する検出器名（空の場合は自動選択）
    /// </summary>
    public string ForcedDetectorName { get; set; } = string.Empty;
}

/// <summary>
/// アンサンブル検出設定
/// </summary>
public class EnsembleSettings
{
    /// <summary>
    /// アンサンブル検出の有効化
    /// </summary>
    public bool EnableEnsemble { get; set; }

    /// <summary>
    /// 使用する検出器名のリスト
    /// </summary>
    public List<string> DetectorNames { get; set; } = ["adaptive", "fast"];
    
    /// <summary>
    /// 重複判定の閾値
    /// </summary>
    public double OverlapThreshold { get; set; } = 0.3;
    
    /// <summary>
    /// 最小投票数
    /// </summary>
    public int MinVotes { get; set; } = 2;
}

/// <summary>
/// OCRエンジンの種類
/// </summary>
public enum OcrEngine
{
    /// <summary>
    /// PaddleOCR（推奨）
    /// </summary>
    PaddleOCR,
    
    /// <summary>
    /// Tesseract OCR
    /// </summary>
    Tesseract,
    
    /// <summary>
    /// Windows標準OCR
    /// </summary>
    WindowsOCR
}
