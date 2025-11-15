namespace Baketa.Infrastructure.OCR.PaddleOCR.Models;

/// <summary>
/// PaddleOCRモデルバージョンの列挙
/// </summary>
public enum PaddleOcrModelVersion
{
    /// <summary>
    /// V3モデル - 最高速度、基本精度
    /// 用途: 高速ROI検出、リアルタイム処理
    /// </summary>
    V3 = 3,

    /// <summary>
    /// V4モデル - バランス型、標準精度
    /// 用途: 一般的なOCR処理、バックアップ
    /// </summary>
    V4 = 4,

    /// <summary>
    /// V5モデル（現在の3.0.1） - 最高精度、低速
    /// 用途: 詳細なテキスト認識、翻訳対象
    /// </summary>
    V5 = 5
}

/// <summary>
/// OCR処理モードの定義
/// </summary>
public enum OcrProcessingMode
{
    /// <summary>
    /// 高速検出モード - V3でROI検出のみ
    /// </summary>
    FastDetection,

    /// <summary>
    /// 高精度認識モード - V5で詳細認識
    /// </summary>
    HighQuality,

    /// <summary>
    /// 適応的モード - 状況に応じて自動切り替え
    /// </summary>
    Adaptive,

    /// <summary>
    /// ハイブリッドモード - V3検出 → V5認識
    /// </summary>
    Hybrid
}

/// <summary>
/// ハイブリッドOCR設定
/// </summary>
public sealed record HybridOcrSettings
{
    /// <summary>
    /// 高速検出用モデル
    /// </summary>
    public PaddleOcrModelVersion FastDetectionModel { get; init; } = PaddleOcrModelVersion.V3;

    /// <summary>
    /// 高精度認識用モデル
    /// </summary>
    public PaddleOcrModelVersion HighQualityModel { get; init; } = PaddleOcrModelVersion.V5;

    /// <summary>
    /// 画像品質閾値（この値以下ならV5のみ使用）
    /// </summary>
    public double ImageQualityThreshold { get; init; } = 0.6;

    /// <summary>
    /// 検出領域数閾値（この値以上ならハイブリッド処理）
    /// </summary>
    public int RegionCountThreshold { get; init; } = 5;

    /// <summary>
    /// タイムアウト設定（ミリ秒）
    /// </summary>
    public int FastDetectionTimeoutMs { get; init; } = 500;
    public int HighQualityTimeoutMs { get; init; } = 3000;
}
