using System;
using System.Threading.Tasks;
using System.Drawing;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;
using Baketa.Core.Extensions;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Services.Imaging.Filters.OCR
{
    /// <summary>
    /// OCR処理のためのノイズ除去フィルター
    /// </summary>
    public class OcrNoiseReductionFilter : ImageFilterBase
    {
        private readonly ILogger<OcrNoiseReductionFilter> _logger;

        /// <summary>
        /// 新しいOcrNoiseReductionFilterを作成します
        /// </summary>
        /// <param name="logger">ロガー</param>
        public OcrNoiseReductionFilter(ILogger<OcrNoiseReductionFilter> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public override string Name => "OCRノイズ除去";

        /// <inheritdoc/>
        public override string Description => "OCR処理のためのテキスト認識精度を向上させるノイズ除去フィルター";

        /// <inheritdoc/>
        public override FilterCategory Category => FilterCategory.Blur;

        /// <inheritdoc/>
        protected override void InitializeDefaultParameters()
        {
            // OCRのノイズ除去に特化したパラメータ
            RegisterParameter("Method", "Bilateral"); // ノイズ除去手法: "Gaussian", "Median", "Bilateral", "NonLocalMeans", "Custom"
            RegisterParameter("KernelSize", 5);      // カーネルサイズ（奇数、3以上）
            RegisterParameter("SigmaColor", 75.0);   // Bilateral filterの色彩シグマ
            RegisterParameter("SigmaSpace", 75.0);   // Bilateral filterの空間シグマ
            RegisterParameter("PreserveEdges", true); // エッジを保持するかどうか
            RegisterParameter("StrengthFactor", 1.0); // 強度係数（1.0が標準）
            RegisterParameter("TextAreaOnly", false); // テキスト領域のみに適用するかどうか
        }

        /// <inheritdoc/>
        public override async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);
            _logger.LogDebug("OCRノイズ除去フィルターを適用中...");

            try
            {
                // パラメータの取得
                string method = GetParameterValue<string>("Method");
                int kernelSize = GetParameterValue<int>("KernelSize");
                double sigmaColor = GetParameterValue<double>("SigmaColor");
                double sigmaSpace = GetParameterValue<double>("SigmaSpace");
                bool preserveEdges = GetParameterValue<bool>("PreserveEdges");
                double strengthFactor = GetParameterValue<double>("StrengthFactor");
                bool textAreaOnly = GetParameterValue<bool>("TextAreaOnly");

                // テキスト領域のみに適用する場合
                IAdvancedImage resultImage;
                if (textAreaOnly)
                {
                    // テキスト領域を検出
                    var textRegions = await inputImage.DetectTextRegionsAsync().ConfigureAwait(false);
                    
                    if (textRegions.Count == 0)
                    {
                        _logger.LogWarning("テキスト領域が検出されませんでした。画像全体にノイズ除去を適用します。");
                        // テキスト領域が検出されない場合は、画像全体に適用
                        resultImage = await ApplyNoiseReductionMethodAsync(inputImage, method, kernelSize, 
                            sigmaColor, sigmaSpace, preserveEdges, strengthFactor).ConfigureAwait(false);
                    }
                    else
                    {
                        // 元の画像をクローン
                        var clonedImage = inputImage.Clone();
                        resultImage = clonedImage as IAdvancedImage
                            ?? throw new InvalidOperationException("Clone結果をIAdvancedImageにキャストできませんでした");
                        
                        // 各テキスト領域に対して処理
                        foreach (var region in textRegions)
                        {
                            // 領域を切り出し
                            var croppedImage = await inputImage.CropAsync(region).ConfigureAwait(false);
                            var regionImage = croppedImage as IAdvancedImage
                                ?? throw new InvalidOperationException("Crop結果をIAdvancedImageにキャストできませんでした");
                            
                            // ノイズ除去処理を適用
                            var processedRegion = await ApplyNoiseReductionMethodAsync(regionImage, method, kernelSize, 
                                sigmaColor, sigmaSpace, preserveEdges, strengthFactor).ConfigureAwait(false);
                            
                            // 処理済み領域を元の画像に戻す
                            await resultImage.ReplaceRegionAsync(region, processedRegion).ConfigureAwait(false);
                        }
                        
                        _logger.LogDebug("{Count}個のテキスト領域にノイズ除去を適用しました", textRegions.Count);
                    }
                }
                else
                {
                    // 画像全体に適用
                    resultImage = await ApplyNoiseReductionMethodAsync(inputImage, method, kernelSize, 
                        sigmaColor, sigmaSpace, preserveEdges, strengthFactor).ConfigureAwait(false);
                }

                return resultImage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCRノイズ除去フィルターの適用中にエラーが発生しました");
                throw;
            }
        }

        /// <summary>
        /// 指定された手法でノイズ除去を適用します
        /// </summary>
        private async Task<IAdvancedImage> ApplyNoiseReductionMethodAsync(
            IAdvancedImage image, string method, int kernelSize, 
            double sigmaColor, double sigmaSpace, bool preserveEdges, double strengthFactor)
        {
            IAdvancedImage resultImage;
            
            switch (method)
            {
                case "Gaussian":
                    // ガウシアンぼかし
                    double sigma = kernelSize / 5.0 * strengthFactor; // カーネルサイズからシグマを計算
                    resultImage = await image.GaussianBlurAsync(kernelSize, sigma).ConfigureAwait(false);
                    _logger.LogDebug("ガウシアンぼかしを適用しました (カーネルサイズ:{KernelSize}, シグマ:{Sigma})",
                        kernelSize, sigma);
                    break;

                case "Median":
                    // メディアンフィルタ - 塩コショウノイズに効果的
                    resultImage = await image.MedianBlurAsync(kernelSize).ConfigureAwait(false);
                    _logger.LogDebug("メディアンフィルタを適用しました (カーネルサイズ:{KernelSize})", kernelSize);
                    break;

                case "Bilateral":
                    // バイラテラルフィルタ - エッジを保持しながらノイズ除去
                    resultImage = await image.BilateralFilterAsync(
                        d: kernelSize,
                        sigmaColor: sigmaColor * strengthFactor,
                        sigmaSpace: sigmaSpace * strengthFactor).ConfigureAwait(false);
                    _logger.LogDebug("バイラテラルフィルタを適用しました (D:{D}, SigmaColor:{SigmaColor}, SigmaSpace:{SigmaSpace})",
                        kernelSize, sigmaColor * strengthFactor, sigmaSpace * strengthFactor);
                    break;

                case "NonLocalMeans":
                    // Non-Local Meansフィルタ - 高品質なノイズ除去
                    resultImage = await image.NonLocalMeansFilterAsync(
                        h: 10 * strengthFactor,
                        templateWindowSize: kernelSize,
                        searchWindowSize: kernelSize * 2 + 1).ConfigureAwait(false);
                    _logger.LogDebug("Non-Local Meansフィルタを適用しました (H:{H}, TemplateSize:{TemplateSize}, SearchSize:{SearchSize})",
                        10 * strengthFactor, kernelSize, kernelSize * 2 + 1);
                    break;

                case "Custom":
                    // OCR特化のカスタムノイズ除去
                    // エッジを保持しながらテキストノイズを特に除去する特殊処理
                    if (preserveEdges)
                    {
                        // エッジ保持＋テキスト特化のノイズ除去
                        resultImage = await image.CustomOcrNoiseReductionAsync(
                            strength: strengthFactor,
                            preserveEdges: true).ConfigureAwait(false);
                        _logger.LogDebug("エッジ保持型カスタムOCRノイズ除去を適用しました (強度:{Strength})", strengthFactor);
                    }
                    else
                    {
                        // テキスト特化のノイズ除去（エッジ保持なし）
                        resultImage = await image.CustomOcrNoiseReductionAsync(
                            strength: strengthFactor,
                            preserveEdges: false).ConfigureAwait(false);
                        _logger.LogDebug("標準カスタムOCRノイズ除去を適用しました (強度:{Strength})", strengthFactor);
                    }
                    break;

                default:
                    _logger.LogWarning("未知のノイズ除去手法: {Method}", method);
                    resultImage = image; // 未知の手法の場合は変更なし
                    break;
            }
            
            return resultImage;
        }

        /// <inheritdoc/>
        public override ImageInfo GetOutputImageInfo(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);
            // ノイズ除去はフォーマットを変更しない
            return new ImageInfo
            {
                Width = inputImage.Width,
                Height = inputImage.Height,
                Format = inputImage.Format,
                Channels = GetChannelCount(inputImage.Format)
            };
        }

        /// <summary>
        /// フォーマットからチャネル数を取得
        /// </summary>
        private static int GetChannelCount(ImageFormat format)
        {
            return format switch
            {
                ImageFormat.Rgb24 => 3,
                ImageFormat.Rgba32 => 4,
                ImageFormat.Grayscale8 => 1,
                _ => throw new ArgumentException($"未サポートの画像フォーマット: {format}")
            };
        }
    }
}
