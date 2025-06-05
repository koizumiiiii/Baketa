using System.Threading.Tasks;
using System;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;
using Baketa.Core.Extensions;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Services.Imaging.Filters.OCR;

    /// <summary>
    /// OCR処理に最適化されたグレースケールフィルター
    /// </summary>
    public class OcrGrayscaleFilter : ImageFilterBase
    {
        private readonly ILogger<OcrGrayscaleFilter> _logger;

        /// <summary>
        /// 新しいOcrGrayscaleFilterを作成します
        /// </summary>
        /// <param name="logger">ロガー</param>
        public OcrGrayscaleFilter(ILogger<OcrGrayscaleFilter> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public override string Name => "OCRグレースケール";

        /// <inheritdoc/>
        public override string Description => "OCR処理に最適化されたグレースケール変換フィルター";

        /// <inheritdoc/>
        public override FilterCategory Category => FilterCategory.ColorAdjustment;

        /// <inheritdoc/>
        protected override void InitializeDefaultParameters()
        {
            // OCRに最適化されたグレースケール変換パラメータ
            RegisterParameter("RedWeight", 0.3);      // 赤チャンネルの重み
            RegisterParameter("GreenWeight", 0.59);   // 緑チャンネルの重み（テキスト検出に重要）
            RegisterParameter("BlueWeight", 0.11);    // 青チャンネルの重み
            RegisterParameter("EnhanceContrast", true); // コントラスト強調するかどうか
        }

        /// <inheritdoc/>
        public override async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);
            _logger.LogDebug("OCRグレースケールフィルターを適用中...");

            try
            {
                // 重みをパラメータから取得
                double redWeight = GetParameterValue<double>("RedWeight");
                double greenWeight = GetParameterValue<double>("GreenWeight");
                double blueWeight = GetParameterValue<double>("BlueWeight");
                bool enhanceContrast = GetParameterValue<bool>("EnhanceContrast");

                // カスタム重み付きグレースケール変換を実行
                // 通常のグレースケール変換とは異なり、テキスト検出に最適化された
                // 重み付けを使用します
                IAdvancedImage grayImage;
                
                if (inputImage.Format == ImageFormat.Grayscale8)
                {
                    // 既にグレースケールの場合はそのまま
                    grayImage = inputImage;
                    _logger.LogDebug("入力画像は既にグレースケールのため、変換をスキップします");
                }
                else
                {
                    // カスタム重み付きグレースケール変換を実行
                    // 通常、IAdvancedImageにはWeightedGrayscaleのような
                    // メソッドが実装されているはずですが、現在はプレースホルダーとします
                    grayImage = await inputImage.ToGrayscaleAsync(redWeight, greenWeight, blueWeight).ConfigureAwait(false);
                    _logger.LogDebug("入力画像をカスタム重み付きグレースケールに変換しました (R:{RedWeight}, G:{GreenWeight}, B:{BlueWeight})",
                        redWeight, greenWeight, blueWeight);
                }

                // コントラスト強調が有効な場合
                if (enhanceContrast)
                {
                    // コントラスト強調 - テキスト検出向けの特殊な処理
                    // 通常このステップは別のフィルター（コントラスト強調フィルター）で行うべきですが、
                    // ここでは特定のOCRユースケースに特化した処理を組み込んでいます
                    grayImage = await grayImage.EnhanceContrastAsync(0.5, 1.5).ConfigureAwait(false); // 例: 特定のパラメータでコントラスト強調
                    _logger.LogDebug("OCR用にコントラスト強調を適用しました");
                }

                return grayImage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCRグレースケールフィルターの適用中にエラーが発生しました");
                throw;
            }
        }

        /// <inheritdoc/>
        public override ImageInfo GetOutputImageInfo(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);
            
            return new ImageInfo
            {
                Width = inputImage.Width,
                Height = inputImage.Height,
                Format = ImageFormat.Grayscale8,
                Channels = 1
            };
        }
    }
