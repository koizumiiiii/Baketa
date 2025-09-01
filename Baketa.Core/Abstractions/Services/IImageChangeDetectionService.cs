// using Baketa.Core.Imaging; // 不要なusing削除

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// 画像変化検知サービスのインターフェース
/// Perceptual Hashベースの高速画像変化検知機能
/// Phase 1: OCR処理最適化システム
/// </summary>
public interface IImageChangeDetectionService
{
    /// <summary>
    /// 2つの画像データ間の変化を検知します
    /// </summary>
    /// <param name="previousImage">前回の画像データ</param>
    /// <param name="currentImage">現在の画像データ</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>変化検知の結果</returns>
    Task<ImageChangeResult> DetectChangeAsync(
        byte[] previousImage, 
        byte[] currentImage, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 画像データからPerceptual Hashを生成します
    /// </summary>
    /// <param name="imageData">画像データ</param>
    /// <param name="algorithm">使用するハッシュアルゴリズム</param>
    /// <returns>生成されたハッシュ文字列</returns>
    string GeneratePerceptualHash(
        byte[] imageData, 
        HashAlgorithmType algorithm = HashAlgorithmType.DifferenceHash);

    /// <summary>
    /// 変化が有意かどうかを判定します
    /// </summary>
    /// <param name="result">変化検知結果</param>
    /// <param name="threshold">判定しきい値 (0.0-1.0)</param>
    /// <returns>有意な変化があるかどうか</returns>
    bool IsSignificantChange(ImageChangeResult result, float threshold = 0.1f);
}

/// <summary>
/// Perceptual Hashアルゴリズムの種類
/// </summary>
public enum HashAlgorithmType
{
    /// <summary>
    /// Average Hash - 高速、基本的な変化検知 (8x8 = 64bit)
    /// </summary>
    AverageHash,
    
    /// <summary>
    /// Difference Hash - エッジ変化に敏感 (8x8 = 64bit) - 推奨
    /// </summary>
    DifferenceHash,
    
    /// <summary>
    /// Perceptual Hash - 最も精密、処理コスト高 (32x32 = 1024bit)
    /// </summary>
    PerceptualHash
}

/// <summary>
/// 画像変化検知の結果
/// </summary>
public class ImageChangeResult
{
    /// <summary>
    /// 画像に変化があったかどうか
    /// </summary>
    public bool HasChanged { get; init; }
    
    /// <summary>
    /// 変化の割合 (0.0-1.0)
    /// </summary>
    public float ChangePercentage { get; init; }
    
    /// <summary>
    /// 前回画像のハッシュ値
    /// </summary>
    public string PreviousHash { get; init; } = string.Empty;
    
    /// <summary>
    /// 現在画像のハッシュ値
    /// </summary>
    public string CurrentHash { get; init; } = string.Empty;
    
    /// <summary>
    /// 処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }
    
    /// <summary>
    /// 使用されたハッシュアルゴリズム
    /// </summary>
    public HashAlgorithmType AlgorithmUsed { get; init; }
}

/// <summary>
/// 画像変化検知の設定
/// </summary>
public class ImageChangeDetectionSettings
{
    /// <summary>
    /// デフォルトのハッシュアルゴリズム
    /// </summary>
    public HashAlgorithmType DefaultAlgorithm { get; set; } = HashAlgorithmType.DifferenceHash;
    
    /// <summary>
    /// 変化判定のしきい値 (0.0-1.0)
    /// </summary>
    public float ChangeThreshold { get; set; } = 0.1f;
    
    /// <summary>
    /// 機能の有効・無効
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// メトリクス収集の有効・無効
    /// </summary>
    public bool EnableMetrics { get; set; } = true;
}