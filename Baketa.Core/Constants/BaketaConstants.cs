namespace Baketa.Core.Constants;

/// <summary>
/// Baketa全体で使用される定数定義
/// マジックナンバー排除とハードコーディング値の外部化
/// </summary>
public static class BaketaConstants
{
    /// <summary>
    /// OCR処理関連の定数
    /// </summary>
    public static class Ocr
    {
        /// <summary>
        /// デフォルト信頼度しきい値（0.7以上のみ翻訳対象）
        /// 特殊記号を含む日本語テキストも翻訳対象とするためのバランス設定
        /// </summary>
        public const double DefaultConfidenceThreshold = 0.70;

        /// <summary>
        /// 高信頼度しきい値（確実なテキストと判定）
        /// </summary>
        public const float HighConfidenceThreshold = 0.9f;

        /// <summary>
        /// デフォルトタイムアウト（ミリ秒）
        /// </summary>
        public const int DefaultTimeoutMs = 30000; // 30秒

        /// <summary>
        /// 評価間隔（ミリ秒）
        /// </summary>
        public const int EvaluationIntervalMs = 30000; // 30秒間隔

        /// <summary>
        /// 再処理しきい値
        /// </summary>
        public const float ReprocessingThreshold = 0.7f;

        /// <summary>
        /// SentencePieceトークンIDの範囲
        /// </summary>
        public const int TokenIdBase = 1000;
        public const int TokenIdMax = 30999;
        public const int TokenIdRange = 30000;
    }

    /// <summary>
    /// 画像処理関連の定数
    /// </summary>
    public static class ImageProcessing
    {
        /// <summary>
        /// デフォルトDPIしきい値
        /// </summary>
        public const int DefaultDpiThreshold = 150;

        /// <summary>
        /// コントラスト調整係数
        /// </summary>
        public const double ContrastAdjustmentFactor = 1.2;

        /// <summary>
        /// Cannyエッジ検出のしきい値
        /// </summary>
        public const int CannyThreshold1 = 50;
        public const int CannyThreshold2 = 150;

        /// <summary>
        /// ガウシアンシグマ値
        /// </summary>
        public const double DefaultGaussianSigma = 0.7;

        /// <summary>
        /// ガンマ補正値
        /// </summary>
        public const double GammaCorrection = 0.7;

        /// <summary>
        /// カラーマスキング強度
        /// </summary>
        public const float ColorMaskingStrength = 0.7f;

        /// <summary>
        /// シャープニング強度
        /// </summary>
        public const double SharpenStrength = 1.2;
        public const double SharpenNegativeWeight = -0.2;
        public const int SharpenOffset = 0;
    }

    /// <summary>
    /// テキスト検出関連の定数
    /// </summary>
    public static class TextDetection
    {
        /// <summary>
        /// テキスト特性スコア
        /// </summary>
        public const float TextCharacteristicScore = 0.7f;

        /// <summary>
        /// コントラストスコア
        /// </summary>
        public const float ContrastScore = 0.7f;

        /// <summary>
        /// 段落区切りしきい値（高さの倍数）
        /// </summary>
        public const float ParagraphBreakThreshold = 1.2f;

        /// <summary>
        /// 短い行のしきい値
        /// </summary>
        public const float ShortLineThreshold = 0.7f;

        /// <summary>
        /// 同一行判定しきい値
        /// </summary>
        public const float SameLineThreshold = 0.7f;

        /// <summary>
        /// 信頼度重み付け係数
        /// </summary>
        public const float ConfidenceWeightCurrent = 0.7f;
        public const float ConfidenceWeightPrevious = 0.3f;

        /// <summary>
        /// 精度計算の重み
        /// </summary>
        public const double CharacterAccuracyWeight = 0.7;
        public const double WordAccuracyWeight = 0.3;

        /// <summary>
        /// テキスト領域の最小サイズ
        /// </summary>
        public const int MinTextRegionWidth = 150;
        public const float MinAspectRatio = 3.0f;
    }

    /// <summary>
    /// 翻訳処理関連の定数
    /// </summary>
    public static class Translation
    {
        /// <summary>
        /// 信頼度しきい値（0.9以上のみ翻訳対象）
        /// </summary>
        public const float ConfidenceThreshold = 0.9f;

        /// <summary>
        /// 成功報酬倍率
        /// </summary>
        public const double SuccessRewardMultiplier = 1.2;

        /// <summary>
        /// フォールバック倍率
        /// </summary>
        public const double FallbackMultiplier = 0.7;

        /// <summary>
        /// エンジン役割別重み
        /// </summary>
        public const double PrimaryEngineWeight = 1.2;
        public const double SpecializedEngineWeight = 1.2;
        public const double FallbackEngineWeight = 0.7;
    }

    /// <summary>
    /// バッチ処理関連の定数
    /// </summary>
    public static class BatchProcessing
    {
        /// <summary>
        /// 基本タイムアウト（ミリ秒）
        /// NLLB-200初回モデルロード時間を考慮して120秒に設定
        /// </summary>
        public const int BaseTimeoutMs = 120000; // 120秒

        /// <summary>
        /// OCR処理比率
        /// </summary>
        public const double OcrProcessingRatio = 0.70;

        /// <summary>
        /// 中解像度での処理比率
        /// </summary>
        public const double MidResolutionProcessingRatio = 0.7;

        /// <summary>
        /// タイムアウト倍率
        /// </summary>
        public const double TimeoutMultiplier = 1.2;
    }

    /// <summary>
    /// UI・レイアウト関連の定数
    /// </summary>
    public static class Layout
    {
        /// <summary>
        /// テキスト間隔係数
        /// </summary>
        public const double LineSpacingMultiplier = 1.2;

        /// <summary>
        /// UI要素間隔
        /// </summary>
        public const int ElementSpacing = 150;
        public const int BaseXOffset = 50;

        /// <summary>
        /// フォント重み付け確率
        /// </summary>
        public const double BoldFontProbability = 0.7;
    }

    /// <summary>
    /// パフォーマンス・品質関連の定数
    /// </summary>
    public static class Performance
    {
        /// <summary>
        /// 品質レベル判定しきい値
        /// </summary>
        public const double GoodQualityThreshold = 0.7;

        /// <summary>
        /// 複雑度判定しきい値
        /// </summary>
        public const double ComplexityThreshold = 0.7;

        /// <summary>
        /// 明度調整しきい値
        /// </summary>
        public const double BrightnessThreshold = 0.7;

        /// <summary>
        /// コントラスト調整しきい値
        /// </summary>
        public const double ContrastOptimizationThreshold = 1.2;

        /// <summary>
        /// 最適化信頼度の下限
        /// </summary>
        public const double MinOptimizationConfidence = 0.7;
    }

    /// <summary>
    /// 認証・セキュリティ関連の定数
    /// </summary>
    public static class Authentication
    {
        /// <summary>
        /// 認証処理の遅延時間（ミリ秒）
        /// </summary>
        public const int AuthenticationDelayMs = 150;
    }

    /// <summary>
    /// テスト・デバッグ関連の定数
    /// </summary>
    public static class Testing
    {
        /// <summary>
        /// テストデータサンプル値
        /// </summary>
        public const int SampleHp = 150;
        public const int SampleMaxHp = 200;
        public const int SampleMp = 80;
        public const int SampleMaxMp = 100;

        /// <summary>
        /// テスト用信頼度
        /// </summary>
        public const float TestConfidence = 0.7f;

        /// <summary>
        /// ベンチマーク用しきい値
        /// </summary>
        public const double BenchmarkThreshold = 0.7;
    }
}
