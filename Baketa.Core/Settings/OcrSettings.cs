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
    /// この値以上の信頼度を持つ結果のみ翻訳にまわされます
    /// </summary>
    /// <remarks>
    /// デフォルト: 0.70（特殊記号を含む日本語テキストも翻訳対象とする）
    /// - 0.90以上: 高品質結果のみ（誤認識を厳しく除外）
    /// - 0.70-0.90: バランス重視（推奨）
    /// - 0.70未満: 低品質結果も含む（誤訳リスク増加）
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "認識信頼度閾値",
        Description = "この値未満の信頼度の結果は破棄されます（0.70-0.90推奨）",
        MinValue = 0.0,
        MaxValue = 1.0)]
    public double ConfidenceThreshold { get; set; } = 0.70;

    /// <summary>
    /// テキスト検出の閾値（0.0-1.0）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "検出感度閾値",
        Description = "テキスト検出の感度調整（低い値ほど高感度）",
        MinValue = 0.0,
        MaxValue = 1.0)]
    public double DetectionThreshold { get; set; } = 0.6;

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
    public int BinarizationThreshold { get; set; } // 0 = 自動

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
    public bool EnhanceEdges { get; set; } = true;

    /// <summary>
    /// 画像拡大率
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "画像拡大率",
        Description = "OCR処理前の画像拡大倍率",
        MinValue = 1.0,
        MaxValue = 4.0)]
    public double ImageScaleFactor { get; set; } = 3.0;

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
    /// StickyROI最大並列処理数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "ROI並列処理数",
        Description = "StickyROI機能の最大並列処理数",
        MinValue = 1,
        MaxValue = 16)]
    public int MaxParallelRois { get; set; } = 4;

    /// <summary>
    /// OCRエンジン最大同時実行数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "OCR同時実行数",
        Description = "OCRエンジンの最大同時実行数（スレッドセーフティ制御）",
        MinValue = 1,
        MaxValue = 8)]
    public int MaxConcurrentOcrRequests { get; set; } = 1;

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
    public bool EnableVerboseLogging { get; set; }

    /// <summary>
    /// OCR結果の保存
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "OCR", "結果保存",
        Description = "OCR結果をファイルに保存します（開発者向け）")]
    public bool SaveOcrResults { get; set; }

    /// <summary>
    /// 処理済み画像の保存
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "OCR", "処理済み画像保存",
        Description = "前処理後の画像を保存します（開発者向け）")]
    public bool SaveProcessedImages { get; set; }

    /// <summary>
    /// ゲーム特化前処理の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "ゲーム特化前処理",
        Description = "ゲーム画面に最適化された前処理パイプラインを使用します")]
    public bool EnableGameSpecificPreprocessing { get; set; } = true;

    /// <summary>
    /// デフォルトゲームテキストプロファイル
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "デフォルトゲームプロファイル",
        Description = "使用するデフォルトのゲームテキストプロファイル")]
    public string DefaultGameTextProfile { get; set; } = "Auto";

    /// <summary>
    /// カスタムゲーム前処理パラメータ
    /// </summary>
    public GamePreprocessingSettings GamePreprocessingSettings { get; set; } = new();

    /// <summary>
    /// 言語モデルを使用するか（PaddleOCR use_lm=True）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "言語モデル使用",
        Description = "PaddleOCRの言語モデルを使用してOCR精度を向上させます")]
    public bool UseLanguageModel { get; set; }

    /// <summary>
    /// GPU加速の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "GPU加速",
        Description = "GPU（DirectML/CUDA/TensorRT）を使用したOCR高速化を有効にします")]
    public bool EnableGpuAcceleration { get; set; } = true;

    /// <summary>
    /// ONNX Runtimeプロバイダー優先順位
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "実行プロバイダー",
        Description = "ONNX Runtime実行プロバイダーの優先順位",
        ValidValues = ["Auto", "DirectML", "CUDA", "TensorRT", "OpenVINO", "CPU"])]
    public string OnnxExecutionProvider { get; set; } = "Auto";

    /// <summary>
    /// OCRモデルパス（ONNX形式）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "ONNXモデルパス",
        Description = "使用するOCR ONNX モデルファイルのパス")]
    public string OnnxModelPath { get; set; } = string.Empty;

    /// <summary>
    /// TDR保護の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "TDR保護",
        Description = "GPU Timeout Detection and Recovery保護を有効にします")]
    public bool EnableTdrProtection { get; set; } = true;

    /// <summary>
    /// GPU推論タイムアウト（ミリ秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "OCR", "GPU推論タイムアウト",
        Description = "GPU推論処理のタイムアウト時間",
        Unit = "ms",
        MinValue = 100,
        MaxValue = 30000)]
    public int GpuInferenceTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// テキスト領域検出設定
    /// </summary>
    public TextDetectionSettings TextDetectionSettings { get; set; } = new();

    /// <summary>
    /// アンサンブル検出設定
    /// </summary>
    public EnsembleSettings TextDetectionEnsemble { get; set; } = new();

    /// <summary>
    /// GPU設定
    /// </summary>
    public GpuOcrSettings GpuSettings { get; set; } = new();

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
            DetectionThreshold = DetectionThreshold,
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
            MaxParallelRois = MaxParallelRois, // Improvement: 新規追加設定
            MaxConcurrentOcrRequests = MaxConcurrentOcrRequests, // Improvement: 新規追加設定
            EnableTextAreaDetection = EnableTextAreaDetection,
            EnableTextFiltering = EnableTextFiltering,
            MinTextLineHeight = MinTextLineHeight,
            MaxTextLineHeight = MaxTextLineHeight,
            TimeoutSeconds = TimeoutSeconds,
            EnableVerboseLogging = EnableVerboseLogging,
            SaveOcrResults = SaveOcrResults,
            SaveProcessedImages = SaveProcessedImages,
            UseLanguageModel = UseLanguageModel,
            EnableGpuAcceleration = EnableGpuAcceleration,
            OnnxExecutionProvider = OnnxExecutionProvider,
            OnnxModelPath = OnnxModelPath,
            EnableTdrProtection = EnableTdrProtection,
            GpuInferenceTimeoutMs = GpuInferenceTimeoutMs,
            TextDetectionSettings = TextDetectionSettings,
            TextDetectionEnsemble = TextDetectionEnsemble,
            GpuSettings = GpuSettings.Clone(),
            EnableGameSpecificPreprocessing = EnableGameSpecificPreprocessing,
            DefaultGameTextProfile = DefaultGameTextProfile,
            GamePreprocessingSettings = GamePreprocessingSettings.Clone()
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

/// <summary>
/// ゲーム特化前処理設定
/// </summary>
public class GamePreprocessingSettings
{
    /// <summary>
    /// DarkBackgroundプロファイルパラメータ
    /// </summary>
    public GameProfileParameters DarkBackground { get; set; } = new()
    {
        BlockSize = 7,
        C = 1.2,
        CLAHEClipLimit = 4.0,
        BilateralSigmaColor = 80.0,
        GaussianSigma = 0.3
    };

    /// <summary>
    /// LightBackgroundプロファイルパラメータ
    /// </summary>
    public GameProfileParameters LightBackground { get; set; } = new()
    {
        BlockSize = 9,
        C = 2.0,
        CLAHEClipLimit = 2.5,
        BilateralSigmaColor = 60.0,
        GaussianSigma = 0.7
    };

    /// <summary>
    /// LowContrastプロファイルパラメータ
    /// </summary>
    public GameProfileParameters LowContrast { get; set; } = new()
    {
        BlockSize = 5,
        C = 1.0,
        CLAHEClipLimit = 5.0,
        BilateralSigmaColor = 100.0,
        GaussianSigma = 0.2
    };

    /// <summary>
    /// MultiFontプロファイルパラメータ
    /// </summary>
    public GameProfileParameters MultiFont { get; set; } = new()
    {
        BlockSize = 11,
        C = 1.8,
        EnableDynamicBlockSize = true,
        CLAHEClipLimit = 3.5,
        BilateralSigmaColor = 70.0,
        GaussianSigma = 0.4
    };

    /// <summary>
    /// UIOverlayプロファイルパラメータ
    /// </summary>
    public GameProfileParameters UIOverlay { get; set; } = new()
    {
        BlockSize = 13,
        C = 2.5,
        CLAHEClipLimit = 4.5,
        BilateralSigmaColor = 90.0,
        GaussianSigma = 0.8
    };

    /// <summary>
    /// A/Bテストの有効化
    /// </summary>
    public bool EnableAbTesting { get; set; }

    /// <summary>
    /// 設定のクローンを作成
    /// </summary>
    public GamePreprocessingSettings Clone()
    {
        return new GamePreprocessingSettings
        {
            DarkBackground = DarkBackground.Clone(),
            LightBackground = LightBackground.Clone(),
            LowContrast = LowContrast.Clone(),
            MultiFont = MultiFont.Clone(),
            UIOverlay = UIOverlay.Clone(),
            EnableAbTesting = EnableAbTesting
        };
    }
}

/// <summary>
/// ゲームプロファイルパラメータ
/// </summary>
public class GameProfileParameters
{
    /// <summary>AdaptiveThreshold blockSize (奇数のみ)</summary>
    public int BlockSize { get; set; } = 7;

    /// <summary>AdaptiveThreshold c パラメータ</summary>
    public double C { get; set; } = 1.5;

    /// <summary>動的blockSize調整の有効化</summary>
    public bool EnableDynamicBlockSize { get; set; } = true;

    /// <summary>CLAHEクリップリミット</summary>
    public double CLAHEClipLimit { get; set; } = 3.0;

    /// <summary>バイラテラルフィルタのsigmaColor</summary>
    public double BilateralSigmaColor { get; set; } = 75.0;

    /// <summary>ガウシアンブラーのsigma</summary>
    public double GaussianSigma { get; set; } = 0.5;

    /// <summary>
    /// パラメータのクローンを作成
    /// </summary>
    public GameProfileParameters Clone()
    {
        return new GameProfileParameters
        {
            BlockSize = BlockSize,
            C = C,
            EnableDynamicBlockSize = EnableDynamicBlockSize,
            CLAHEClipLimit = CLAHEClipLimit,
            BilateralSigmaColor = BilateralSigmaColor,
            GaussianSigma = GaussianSigma
        };
    }
}

/// <summary>
/// GPU OCR設定
/// </summary>
public class GpuOcrSettings
{
    /// <summary>
    /// 検出モデルパス
    /// </summary>
    public string DetectionModelPath { get; set; } = @"models\paddle_ocr\detection\ch_PP-OCRv4_det_infer.onnx";

    /// <summary>
    /// 認識モデルパス
    /// </summary>
    public string RecognitionModelPath { get; set; } = @"models\paddle_ocr\recognition\ch_PP-OCRv4_rec_infer.onnx";

    /// <summary>
    /// 言語識別モデルパス
    /// </summary>
    public string LanguageIdentificationModelPath { get; set; } = @"models\paddle_ocr\cls\ch_ppocr_mobile_v2.0_cls_infer.onnx";

    /// <summary>
    /// CPUスレッド数（CPU実行時）
    /// </summary>
    public int CpuThreadCount { get; set; } = 4;

    /// <summary>
    /// GPU Device ID
    /// </summary>
    public int GpuDeviceId { get; set; }

    /// <summary>
    /// バッチサイズ
    /// </summary>
    public int BatchSize { get; set; } = 1;

    /// <summary>
    /// ウォームアップの有効化
    /// </summary>
    public bool EnableWarmup { get; set; } = true;

    /// <summary>
    /// ウォームアップ実行回数
    /// </summary>
    public int WarmupIterations { get; set; } = 3;

    /// <summary>
    /// メモリ最適化の有効化
    /// </summary>
    public bool EnableMemoryOptimization { get; set; } = true;

    /// <summary>
    /// ONNX最適化レベル
    /// </summary>
    public string OnnxOptimizationLevel { get; set; } = "All";

    /// <summary>
    /// DirectML最適化の有効化
    /// </summary>
    public bool EnableDirectMLOptimization { get; set; } = true;

    /// <summary>
    /// CUDA最適化の有効化
    /// </summary>
    public bool EnableCudaOptimization { get; set; } = true;

    /// <summary>
    /// 設定のクローンを作成
    /// </summary>
    public GpuOcrSettings Clone()
    {
        return new GpuOcrSettings
        {
            DetectionModelPath = DetectionModelPath,
            RecognitionModelPath = RecognitionModelPath,
            LanguageIdentificationModelPath = LanguageIdentificationModelPath,
            CpuThreadCount = CpuThreadCount,
            GpuDeviceId = GpuDeviceId,
            BatchSize = BatchSize,
            EnableWarmup = EnableWarmup,
            WarmupIterations = WarmupIterations,
            EnableMemoryOptimization = EnableMemoryOptimization,
            OnnxOptimizationLevel = OnnxOptimizationLevel,
            EnableDirectMLOptimization = EnableDirectMLOptimization,
            EnableCudaOptimization = EnableCudaOptimization
        };
    }
}
