using System.Drawing;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Models.ImageProcessing;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// Perceptual Hash（知覚ハッシュ）サービスインターフェース
/// P0: 画像変化検知システム - 4種類のアルゴリズム対応
/// OpenCV SIMD最適化による高速処理
/// </summary>
public interface IPerceptualHashService
{
    /// <summary>
    /// 指定されたアルゴリズムでハッシュを計算
    /// </summary>
    /// <param name="image">対象画像</param>
    /// <param name="algorithm">ハッシュアルゴリズム</param>
    /// <returns>64bitハッシュ文字列</returns>
    string ComputeHash(IImage image, HashAlgorithmType algorithm);

    /// <summary>
    /// [Issue #229] 画像の指定領域に対してハッシュを計算（グリッド分割用）
    /// </summary>
    /// <param name="image">対象画像</param>
    /// <param name="region">ハッシュ計算対象の矩形領域</param>
    /// <param name="algorithm">ハッシュアルゴリズム</param>
    /// <returns>領域のハッシュ文字列（64bit）</returns>
    string ComputeHashForRegion(IImage image, Rectangle region, HashAlgorithmType algorithm);

    /// <summary>
    /// 2つのハッシュを比較して類似度を取得
    /// </summary>
    /// <param name="hash1">ハッシュ1</param>
    /// <param name="hash2">ハッシュ2</param>
    /// <param name="algorithm">使用されたアルゴリズム</param>
    /// <returns>類似度（0.0-1.0、1.0が完全一致）</returns>
    float CompareHashes(string hash1, string hash2, HashAlgorithmType algorithm);

    /// <summary>
    /// 画像タイプに応じた最適アルゴリズムを取得
    /// </summary>
    /// <param name="imageType">画像タイプ</param>
    /// <returns>推奨アルゴリズム</returns>
    HashAlgorithmType GetOptimalAlgorithm(ImageType imageType);

    /// <summary>
    /// ハミング距離を計算（ハッシュ比較用）
    /// </summary>
    /// <param name="hash1">ハッシュ1（64bit文字列）</param>
    /// <param name="hash2">ハッシュ2（64bit文字列）</param>
    /// <returns>ハミング距離（0-64）</returns>
    int CalculateHammingDistance(string hash1, string hash2);

    /// <summary>
    /// 構造的類似性指数（SSIM）を計算
    /// 高精度分析用（Stage 3）
    /// </summary>
    /// <param name="image1">画像1</param>
    /// <param name="image2">画像2</param>
    /// <returns>SSIM値（0.0-1.0、1.0が完全一致）</returns>
    Task<float> CalculateSSIMAsync(IImage image1, IImage image2);
}
