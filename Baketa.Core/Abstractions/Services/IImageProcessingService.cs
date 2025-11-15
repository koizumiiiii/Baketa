using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// 画像処理サービスインターフェース
/// </summary>
public interface IImageProcessingService
{
    /// <summary>
    /// 画像を最適化します
    /// </summary>
    /// <param name="image">元の画像</param>
    /// <param name="options">最適化オプション</param>
    /// <returns>最適化された画像</returns>
    Task<IImage> OptimizeImageAsync(IImage image, ImageOptimizationOptions options);

    /// <summary>
    /// 2つの画像間の差分を検出します
    /// </summary>
    /// <param name="image1">1つ目の画像</param>
    /// <param name="image2">2つ目の画像</param>
    /// <param name="threshold">差分検出の閾値 (0.0-1.0)</param>
    /// <returns>差分領域のリスト</returns>
    Task<IReadOnlyCollection<Rectangle>> DetectDifferencesAsync(IImage image1, IImage image2, float threshold = 0.05f);

    /// <summary>
    /// 画像からテキスト領域を検出します
    /// </summary>
    /// <param name="image">処理する画像</param>
    /// <returns>テキスト領域の可能性が高い領域のリスト</returns>
    Task<IReadOnlyCollection<Rectangle>> DetectTextAreasAsync(IImage image);

    /// <summary>
    /// 画像からオブジェクトを検出します
    /// </summary>
    /// <param name="image">処理する画像</param>
    /// <returns>検出されたオブジェクトのリスト</returns>
    Task<IReadOnlyCollection<DetectedObject>> DetectObjectsAsync(IImage image);

    /// <summary>
    /// 画像を指定した領域でクロップします
    /// </summary>
    /// <param name="image">元の画像</param>
    /// <param name="region">切り出す領域</param>
    /// <returns>切り出された画像</returns>
    Task<IImage> CropImageAsync(IImage image, Rectangle region);

    /// <summary>
    /// 複数の画像を結合します
    /// </summary>
    /// <param name="images">結合する画像のリスト</param>
    /// <param name="direction">結合方向</param>
    /// <returns>結合された画像</returns>
    Task<IImage> CombineImagesAsync(IReadOnlyCollection<IImage> images, CombineDirection direction);
}

/// <summary>
/// 検出されたオブジェクト
/// </summary>
public class DetectedObject
{
    /// <summary>
    /// オブジェクトのバウンディングボックス
    /// </summary>
    public Rectangle Bounds { get; set; }

    /// <summary>
    /// オブジェクトの種類
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// 検出の信頼度 (0.0-1.0)
    /// </summary>
    public float Confidence { get; set; }
}

/// <summary>
/// 画像最適化オプション
/// </summary>
public class ImageOptimizationOptions
{
    /// <summary>
    /// 明るさ調整 (-1.0 - 1.0)
    /// </summary>
    public float Brightness { get; set; }

    /// <summary>
    /// コントラスト調整 (0.0 - 2.0, デフォルトは1.0)
    /// </summary>
    public float Contrast { get; set; }

    /// <summary>
    /// シャープネス (0.0 - 1.0)
    /// </summary>
    public float Sharpness { get; set; }

    /// <summary>
    /// ノイズ除去 (0.0 - 1.0)
    /// </summary>
    public float NoiseReduction { get; set; }

    /// <summary>
    /// 2値化閾値 (0 - 255, 0の場合は2値化を行わない)
    /// </summary>
    public int BinarizationThreshold { get; set; }

    /// <summary>
    /// 適応的2値化を使用するかどうか
    /// </summary>
    public bool UseAdaptiveThreshold { get; set; }

    /// <summary>
    /// テキスト検出最適化
    /// </summary>
    public bool OptimizeForText { get; set; }

    /// <summary>
    /// カラー処理モード
    /// </summary>
    public ColorProcessingMode ColorMode { get; set; }
}

/// <summary>
/// カラー処理モード
/// </summary>
public enum ColorProcessingMode
{
    /// <summary>
    /// 元の色を保持
    /// </summary>
    Preserve = 0,

    /// <summary>
    /// グレースケール
    /// </summary>
    Grayscale = 1,

    /// <summary>
    /// 2値化（白黒）
    /// </summary>
    Binary = 2,

    /// <summary>
    /// カラー反転
    /// </summary>
    Invert = 3
}

/// <summary>
/// 画像結合方向
/// </summary>
public enum CombineDirection
{
    /// <summary>
    /// 水平方向（左から右）
    /// </summary>
    Horizontal = 0,

    /// <summary>
    /// 垂直方向（上から下）
    /// </summary>
    Vertical = 1,

    /// <summary>
    /// グリッド配置
    /// </summary>
    Grid = 2
}
