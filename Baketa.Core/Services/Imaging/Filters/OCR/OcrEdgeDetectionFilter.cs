using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;
using Baketa.Core.Extensions;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Services.Imaging.Filters.OCR;

/// <summary>
/// OCR処理のためのエッジ検出フィルター
/// </summary>
/// <remarks>
/// 新しいOcrEdgeDetectionFilterを作成します
/// </remarks>
/// <param name="logger">ロガー</param>
public class OcrEdgeDetectionFilter(ILogger<OcrEdgeDetectionFilter> logger) : ImageFilterBase
{
    private readonly ILogger<OcrEdgeDetectionFilter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public override string Name => "OCRエッジ検出";

    /// <inheritdoc/>
    public override string Description => "OCR処理のためのテキスト輪郭を強調するエッジ検出フィルター";

    /// <inheritdoc/>
    public override FilterCategory Category => FilterCategory.EdgeDetection;

    /// <inheritdoc/>
    protected override void InitializeDefaultParameters()
    {
        // OCRのエッジ検出に特化したパラメータ
        RegisterParameter("Method", "Canny"); // 手法: "Sobel", "Canny", "Laplacian", "Scharr"
        RegisterParameter("LowThreshold", 50); // Cannyの低しきい値（0-255）
        RegisterParameter("HighThreshold", 150); // Cannyの高しきい値（0-255）
        RegisterParameter("ApertureSize", 3); // アパーチャサイズ（奇数、3以上）
        RegisterParameter("L2Gradient", false); // L2ノルムを使用するかどうか（Canny用）
        RegisterParameter("EnhanceTextEdges", true); // テキストエッジを強調するかどうか
        RegisterParameter("TextModeOnly", true); // テキスト検出モードのみを使用するかどうか
    }

    /// <inheritdoc/>
    public override async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
    {
        ArgumentNullException.ThrowIfNull(inputImage);
        _logger.LogDebug("OCRエッジ検出フィルターを適用中...");

        try
        {
            // パラメータの取得
            string method = GetParameterValue<string>("Method");
            int lowThreshold = GetParameterValue<int>("LowThreshold");
            int highThreshold = GetParameterValue<int>("HighThreshold");
            int apertureSize = GetParameterValue<int>("ApertureSize");
            bool l2Gradient = GetParameterValue<bool>("L2Gradient");
            bool enhanceTextEdges = GetParameterValue<bool>("EnhanceTextEdges");
            bool textModeOnly = GetParameterValue<bool>("TextModeOnly");

            // 入力画像がグレースケールでない場合は警告
            if (inputImage.Format != ImageFormat.Grayscale8)
            {
                _logger.LogWarning("入力画像はグレースケールではありません。エッジ検出の前にグレースケール変換を行うことをお勧めします。");
                // オプションとして、ここでグレースケール変換を行うこともできますが、
                // パイプラインの設計上、別のステップで行うべきです
            }

            IAdvancedImage edgeImage;

            switch (method)
            {
                case "Sobel":
                    // Sobelエッジ検出 - X方向とY方向の勾配を計算
                    var sobelX = await inputImage.SobelAsync(1, 0, apertureSize).ConfigureAwait(false);
                    var sobelY = await inputImage.SobelAsync(0, 1, apertureSize).ConfigureAwait(false);

                    // X方向とY方向の勾配を結合
                    edgeImage = await sobelX.CombineGradientsAsync(sobelY).ConfigureAwait(false);

                    _logger.LogDebug("Sobelエッジ検出を適用しました (アパーチャサイズ:{ApertureSize})", apertureSize);
                    break;

                case "Canny":
                    // Cannyエッジ検出 - 最も一般的で効果的なエッジ検出
                    edgeImage = await inputImage.CannyAsync(
                        lowThreshold,
                        highThreshold,
                        apertureSize,
                        l2Gradient).ConfigureAwait(false);

                    _logger.LogDebug("Cannyエッジ検出を適用しました (低閾値:{LowThreshold}, 高閾値:{HighThreshold}, アパーチャサイズ:{ApertureSize}, L2勾配:{L2Gradient})",
                        lowThreshold, highThreshold, apertureSize, l2Gradient);
                    break;

                case "Laplacian":
                    // Laplacianエッジ検出 - 2次微分を使用
                    edgeImage = await inputImage.LaplacianAsync(apertureSize).ConfigureAwait(false);

                    _logger.LogDebug("Laplacianエッジ検出を適用しました (アパーチャサイズ:{ApertureSize})", apertureSize);
                    break;

                case "Scharr":
                    // Scharrエッジ検出 - Sobelの改良版
                    var scharrX = await inputImage.ScharrAsync(1, 0).ConfigureAwait(false);
                    var scharrY = await inputImage.ScharrAsync(0, 1).ConfigureAwait(false);

                    // X方向とY方向の勾配を結合
                    edgeImage = await scharrX.CombineGradientsAsync(scharrY).ConfigureAwait(false);

                    _logger.LogDebug("Scharrエッジ検出を適用しました");
                    break;

                default:
                    _logger.LogWarning("未知のエッジ検出手法: {Method}", method);
                    return inputImage; // 未知の手法の場合は変更なし
            }

            // テキストエッジの強調が有効な場合
            if (enhanceTextEdges)
            {
                if (textModeOnly)
                {
                    // テキスト検出モードのみ - テキスト特性に合わせた特殊処理
                    edgeImage = await EnhanceTextEdgesOnlyAsync(edgeImage).ConfigureAwait(false);
                    _logger.LogDebug("テキスト特化のエッジ強調を適用しました");
                }
                else
                {
                    // 一般的なエッジ強調 - OCRに役立つが、すべてのエッジを強調
                    edgeImage = await EnhanceAllEdgesAsync(edgeImage).ConfigureAwait(false);
                    _logger.LogDebug("一般的なエッジ強調を適用しました");
                }
            }

            return edgeImage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCRエッジ検出フィルターの適用中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// テキスト特化のエッジ強調処理
    /// </summary>
    private static async Task<IAdvancedImage> EnhanceTextEdgesOnlyAsync(IAdvancedImage edgeImage)
    {
        // ヒストグラム解析によるテキストエッジ特定
        var textEdgesMask = await edgeImage.DetectTextLikeEdgesAsync().ConfigureAwait(false);

        // マスクを使用してテキストエッジのみを強調
        var enhancedEdges = await edgeImage.EnhanceMaskedRegionsAsync(textEdgesMask, 1.5).ConfigureAwait(false);

        return enhancedEdges;
    }

    /// <summary>
    /// すべてのエッジを強調する処理
    /// </summary>
    private static async Task<IAdvancedImage> EnhanceAllEdgesAsync(IAdvancedImage edgeImage)
    {
        // シンプルなコントラスト強調でエッジを強調
        var enhancedEdges = await edgeImage.AdjustContrastAsync(1.5, 0).ConfigureAwait(false);

        return enhancedEdges;
    }

    /// <inheritdoc/>
    public override ImageInfo GetOutputImageInfo(IAdvancedImage inputImage)
    {
        ArgumentNullException.ThrowIfNull(inputImage);

        // エッジ検出の結果はグレースケール画像
        return new ImageInfo
        {
            Width = inputImage.Width,
            Height = inputImage.Height,
            Format = ImageFormat.Grayscale8,
            Channels = 1
        };
    }
}
