using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;
using Baketa.Core.Extensions;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Services.Imaging.Filters.OCR;

/// <summary>
/// OCR処理のためのコントラスト強調フィルター
/// </summary>
/// <remarks>
/// 新しいOcrContrastEnhancementFilterを作成します
/// </remarks>
/// <param name="logger">ロガー</param>
public class OcrContrastEnhancementFilter(ILogger<OcrContrastEnhancementFilter> logger) : ImageFilterBase
    {
        private readonly ILogger<OcrContrastEnhancementFilter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public override string Name => "OCRコントラスト強調";

        /// <inheritdoc/>
        public override string Description => "OCR処理のためにテキストとバックグラウンドのコントラストを強調します";

        /// <inheritdoc/>
        public override FilterCategory Category => FilterCategory.ColorAdjustment;

        /// <inheritdoc/>
        protected override void InitializeDefaultParameters()
        {
            // OCRのコントラスト強調に特化したパラメータ
            RegisterParameter("Alpha", 1.5);        // コントラスト係数（1.0が元の画像、>1.0でコントラスト増加）
            RegisterParameter("Beta", 0.0);         // 明るさ調整（0が元の画像）
            RegisterParameter("AdaptiveMethod", "CLAHE"); // 適応的コントラスト強調手法: "CLAHE", "Standard", "None"
            RegisterParameter("ClipLimit", 2.0);    // CLAHEのクリップ制限（特定範囲を超える変化を制限）
            RegisterParameter("TileGridSize", 8);   // CLAHEのタイルサイズ
            RegisterParameter("TextEnhanceMode", "Auto"); // テキスト強調モード: "Auto", "Light", "Dark", "None"
        }

        /// <inheritdoc/>
        public override async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);
            _logger.LogDebug("OCRコントラスト強調フィルターを適用中...");

            try
            {
                // パラメータの取得
                double alpha = GetParameterValue<double>("Alpha");
                double beta = GetParameterValue<double>("Beta");
                string adaptiveMethod = GetParameterValue<string>("AdaptiveMethod");
                double clipLimit = GetParameterValue<double>("ClipLimit");
                int tileGridSize = GetParameterValue<int>("TileGridSize");
                string textEnhanceMode = GetParameterValue<string>("TextEnhanceMode");

                // 1. まず標準的なコントラスト強調を適用
                var enhancedImage = await inputImage.AdjustContrastAsync(alpha, beta).ConfigureAwait(false);
                _logger.LogDebug("標準コントラスト強調を適用しました (Alpha:{Alpha}, Beta:{Beta})", alpha, beta);

                // 2. 適応的コントラスト強調手法の適用
                if (adaptiveMethod != "None")
                {
                    switch (adaptiveMethod)
                    {
                        case "CLAHE":
                            // CLAHE (Contrast Limited Adaptive Histogram Equalization)
                            enhancedImage = await enhancedImage.ApplyAdaptiveContrastAsync(
                                clipLimit,
                                tileGridSize).ConfigureAwait(false);
                            _logger.LogDebug("CLAHE適応的コントラスト強調を適用しました (ClipLimit:{ClipLimit}, TileSize:{TileSize})",
                                clipLimit, tileGridSize);
                            break;

                        case "Standard":
                            // 標準的なヒストグラム平坦化
                            enhancedImage = await enhancedImage.EqualizeHistogramAsync().ConfigureAwait(false);
                            _logger.LogDebug("標準ヒストグラム平坦化を適用しました");
                            break;

                        default:
                            _logger.LogWarning("未知の適応的コントラスト強調手法: {Method}", adaptiveMethod);
                            break;
                    }
                }

                // 3. テキスト強調モードの適用（OCR専用の処理）
                if (textEnhanceMode != "None")
                {
                    switch (textEnhanceMode)
                    {
                        case "Auto":
                            // 画像の特性に基づいて自動的に適切なテキスト強調を適用
                            // 平均輝度によってLightかDarkを判断
                            double avgBrightness = await enhancedImage.GetAverageBrightnessAsync().ConfigureAwait(false);
                            if (avgBrightness > 127)
                            {
                                // 明るい背景に暗いテキスト
                                enhancedImage = await ApplyDarkTextEnhancementAsync(enhancedImage).ConfigureAwait(false);
                            }
                            else
                            {
                                // 暗い背景に明るいテキスト
                                enhancedImage = await ApplyLightTextEnhancementAsync(enhancedImage).ConfigureAwait(false);
                            }
                            _logger.LogDebug("自動テキスト強調を適用しました (平均輝度: {AvgBrightness})", avgBrightness);
                            break;

                        case "Light":
                            // 明るいテキスト専用の強調（暗い背景向け）
                            enhancedImage = await ApplyLightTextEnhancementAsync(enhancedImage).ConfigureAwait(false);
                            _logger.LogDebug("明るいテキスト強調を適用しました");
                            break;

                        case "Dark":
                            // 暗いテキスト専用の強調（明るい背景向け）
                            enhancedImage = await ApplyDarkTextEnhancementAsync(enhancedImage).ConfigureAwait(false);
                            _logger.LogDebug("暗いテキスト強調を適用しました");
                            break;

                        default:
                            _logger.LogWarning("未知のテキスト強調モード: {Mode}", textEnhanceMode);
                            break;
                    }
                }

                return enhancedImage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCRコントラスト強調フィルターの適用中にエラーが発生しました");
                throw;
            }
        }

        /// <summary>
        /// 明るいテキスト（暗い背景）向けの強調処理
        /// </summary>
        private static async Task<IAdvancedImage> ApplyLightTextEnhancementAsync(IAdvancedImage image)
        {
            // 明るいテキストを強調するカスタム処理
            // この実装はプレースホルダーであり、実際にはIAdvancedImageの拡張によって
            // 提供される必要があります
            return await image.EnhanceLightTextAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 暗いテキスト（明るい背景）向けの強調処理
        /// </summary>
        private static async Task<IAdvancedImage> ApplyDarkTextEnhancementAsync(IAdvancedImage image)
        {
            // 暗いテキストを強調するカスタム処理
            // この実装はプレースホルダーであり、実際にはIAdvancedImageの拡張によって
            // 提供される必要があります
            return await image.EnhanceDarkTextAsync().ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override ImageInfo GetOutputImageInfo(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);
            
            // コントラスト強調はフォーマットを変更しない
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
