using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;
using Baketa.Core.Extensions;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Services.Imaging.Filters.OCR;

    /// <summary>
    /// OCR処理のための二値化フィルター
    /// </summary>
    public class OcrThresholdFilter : ImageFilterBase
    {
        private readonly ILogger<OcrThresholdFilter> _logger;

        /// <summary>
        /// 新しいOcrThresholdFilterを作成します
        /// </summary>
        /// <param name="logger">ロガー</param>
        public OcrThresholdFilter(ILogger<OcrThresholdFilter> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public override string Name => "OCR二値化";

        /// <inheritdoc/>
        public override string Description => "OCR処理のためにテキストを明確に分離する二値化フィルター";

        /// <inheritdoc/>
        public override FilterCategory Category => FilterCategory.Threshold;

        /// <inheritdoc/>
        protected override void InitializeDefaultParameters()
        {
            // OCRの二値化に特化したパラメータ
            RegisterParameter("Method", "AdaptiveGaussian"); // 二値化手法: "Global", "Otsu", "AdaptiveGaussian", "AdaptiveMean"
            RegisterParameter("ThresholdValue", 127);     // グローバル二値化のしきい値（0-255）
            RegisterParameter("MaxValue", 255);           // 二値化後の最大値
            RegisterParameter("BlockSize", 11);           // 適応的二値化のブロックサイズ（奇数、3以上）
            RegisterParameter("C", 2.0);                 // 適応的二値化の定数C
            RegisterParameter("Invert", false);           // 反転するかどうか（暗い背景の場合true）
            RegisterParameter("AutoThreshold", true);     // しきい値を自動的に調整するかどうか
        }

        /// <inheritdoc/>
        public override async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);
            _logger.LogDebug("OCR二値化フィルターを適用中...");

            try
            {
                // パラメータの取得
                string method = GetParameterValue<string>("Method");
                int thresholdValue = GetParameterValue<int>("ThresholdValue");
                int maxValue = GetParameterValue<int>("MaxValue");
                int blockSize = GetParameterValue<int>("BlockSize");
                double c = GetParameterValue<double>("C");
                bool invert = GetParameterValue<bool>("Invert");
                bool autoThreshold = GetParameterValue<bool>("AutoThreshold");

                // 入力画像がグレースケールでない場合は警告
                if (inputImage.Format != ImageFormat.Grayscale8)
                {
                    _logger.LogWarning("入力画像はグレースケールではありません。二値化の前にグレースケール変換を行うことをお勧めします。");
                    // オプションとして、ここでグレースケール変換を行うこともできますが、
                    // パイプラインの設計上、別のステップで行うべきです
                }

                // 自動しきい値調整が有効な場合、Otsu法を使用して最適なしきい値を計算
                if (autoThreshold && method == "Global")
                {
                    thresholdValue = await inputImage.CalculateOptimalThresholdAsync().ConfigureAwait(false);
                    _logger.LogDebug("自動しきい値計算: {Threshold}", thresholdValue);
                }

                IAdvancedImage binaryImage;
                
                switch (method)
                {
                    case "Global":
                        // グローバル二値化
                        binaryImage = await inputImage.ThresholdAsync(thresholdValue, maxValue, invert).ConfigureAwait(false);
                        _logger.LogDebug("グローバル二値化を適用しました (閾値:{Threshold}, 最大値:{MaxValue}, 反転:{Invert})",
                            thresholdValue, maxValue, invert);
                        break;

                    case "Otsu":
                        // Otsu法による最適な二値化
                        binaryImage = await inputImage.OtsuThresholdAsync(maxValue, invert).ConfigureAwait(false);
                        _logger.LogDebug("Otsu法による二値化を適用しました (最大値:{MaxValue}, 反転:{Invert})",
                            maxValue, invert);
                        break;

                    case "AdaptiveGaussian":
                        // ガウシアン適応的二値化
                        binaryImage = await inputImage.AdaptiveThresholdAsync(
                            maxValue,
                            "Gaussian",
                            blockSize,
                            c,
                            invert).ConfigureAwait(false);
                        _logger.LogDebug("ガウシアン適応的二値化を適用しました (ブロックサイズ:{BlockSize}, C:{C}, 反転:{Invert})",
                            blockSize, c, invert);
                        break;

                    case "AdaptiveMean":
                        // 平均適応的二値化
                        binaryImage = await inputImage.AdaptiveThresholdAsync(
                            maxValue,
                            "Mean",
                            blockSize,
                            c,
                            invert).ConfigureAwait(false);
                        _logger.LogDebug("平均適応的二値化を適用しました (ブロックサイズ:{BlockSize}, C:{C}, 反転:{Invert})",
                            blockSize, c, invert);
                        break;

                    default:
                        _logger.LogWarning("未知の二値化手法: {Method}", method);
                        binaryImage = inputImage; // 未知の手法の場合は変更なし
                        break;
                }

                return binaryImage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCR二値化フィルターの適用中にエラーが発生しました");
                throw;
            }
        }

        /// <inheritdoc/>
        public override ImageInfo GetOutputImageInfo(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);
            // 二値化後もフォーマットは変わらない（グレースケールのまま）
            return new ImageInfo
            {
                Width = inputImage.Width,
                Height = inputImage.Height,
                Format = ImageFormat.Grayscale8, // 二値化の結果は通常グレースケール
                Channels = 1
            };
        }
    }
