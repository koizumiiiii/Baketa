using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;
using Baketa.Core.Extensions;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Services.Imaging.Filters.OCR;

/// <summary>
/// OCR処理のためのモルフォロジー（形態学的）処理フィルター
/// </summary>
/// <remarks>
/// 新しいOcrMorphologyFilterを作成します
/// </remarks>
/// <param name="logger">ロガー</param>
public class OcrMorphologyFilter(ILogger<OcrMorphologyFilter> logger) : ImageFilterBase
    {
        private readonly ILogger<OcrMorphologyFilter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public override string Name => "OCRモルフォロジー処理";

        /// <inheritdoc/>
        public override string Description => "OCR処理のためのテキスト形状を調整するモルフォロジー処理フィルター";

        /// <inheritdoc/>
        public override FilterCategory Category => FilterCategory.Morphology;

        /// <inheritdoc/>
        protected override void InitializeDefaultParameters()
        {
            // OCRのモルフォロジー処理に特化したパラメータ
            RegisterParameter("Operation", "Close"); // 操作: "Dilate", "Erode", "Open", "Close", "TopHat", "BlackHat", "Gradient"
            RegisterParameter("KernelShape", "Rectangle"); // カーネル形状: "Rectangle", "Ellipse", "Cross"
            RegisterParameter("KernelSize", 3);      // カーネルサイズ（奇数、3以上）
            RegisterParameter("Iterations", 1);      // 繰り返し回数
            RegisterParameter("TextMode", "Normal"); // テキストモード: "Normal", "Thin", "Thick", "Separated", "Connected"
        }

        /// <inheritdoc/>
        public override async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);
            _logger.LogDebug("OCRモルフォロジー処理フィルターを適用中...");

            try
            {
                // パラメータの取得
                string operation = GetParameterValue<string>("Operation");
                string kernelShape = GetParameterValue<string>("KernelShape");
                int kernelSize = GetParameterValue<int>("KernelSize");
                int iterations = GetParameterValue<int>("Iterations");
                string textMode = GetParameterValue<string>("TextMode");

                // テキストモードに基づいてパラメータを調整
                AdjustParametersForTextMode(ref operation, ref kernelSize, ref iterations, textMode);

                IAdvancedImage resultImage;
                
                // モルフォロジー操作を適用
                switch (operation)
                {
                    case "Dilate":
                        // 膨張 - テキストを太くする
                        resultImage = await inputImage.DilateAsync(kernelShape, kernelSize, iterations).ConfigureAwait(false);
                        _logger.LogDebug("膨張操作を適用しました (形状:{Shape}, サイズ:{Size}, 繰り返し:{Iterations})",
                            kernelShape, kernelSize, iterations);
                        break;

                    case "Erode":
                        // 収縮 - テキストを細くする
                        resultImage = await inputImage.ErodeAsync(kernelShape, kernelSize, iterations).ConfigureAwait(false);
                        _logger.LogDebug("収縮操作を適用しました (形状:{Shape}, サイズ:{Size}, 繰り返し:{Iterations})",
                            kernelShape, kernelSize, iterations);
                        break;

                    case "Open":
                        // オープニング（収縮→膨張） - ノイズ除去に効果的
                        resultImage = await inputImage.MorphOpenAsync(kernelShape, kernelSize, iterations).ConfigureAwait(false);
                        _logger.LogDebug("オープニング操作を適用しました (形状:{Shape}, サイズ:{Size}, 繰り返し:{Iterations})",
                            kernelShape, kernelSize, iterations);
                        break;

                    case "Close":
                        // クロージング（膨張→収縮） - 文字の穴を埋める
                        resultImage = await inputImage.MorphCloseAsync(kernelShape, kernelSize, iterations).ConfigureAwait(false);
                        _logger.LogDebug("クロージング操作を適用しました (形状:{Shape}, サイズ:{Size}, 繰り返し:{Iterations})",
                            kernelShape, kernelSize, iterations);
                        break;

                    case "TopHat":
                        // トップハット変換 - 明るい部分を強調
                        resultImage = await inputImage.MorphTopHatAsync(kernelShape, kernelSize).ConfigureAwait(false);
                        _logger.LogDebug("トップハット変換を適用しました (形状:{Shape}, サイズ:{Size})",
                            kernelShape, kernelSize);
                        break;

                    case "BlackHat":
                        // ブラックハット変換 - 暗い部分を強調
                        resultImage = await inputImage.MorphBlackHatAsync(kernelShape, kernelSize).ConfigureAwait(false);
                        _logger.LogDebug("ブラックハット変換を適用しました (形状:{Shape}, サイズ:{Size})",
                            kernelShape, kernelSize);
                        break;

                    case "Gradient":
                        // モルフォロジー勾配 - エッジを強調
                        resultImage = await inputImage.MorphGradientAsync(kernelShape, kernelSize).ConfigureAwait(false);
                        _logger.LogDebug("モルフォロジー勾配を適用しました (形状:{Shape}, サイズ:{Size})",
                            kernelShape, kernelSize);
                        break;

                    default:
                        _logger.LogWarning("未知のモルフォロジー操作: {Operation}", operation);
                        resultImage = inputImage; // 未知の操作の場合は変更なし
                        break;
                }

                return resultImage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCRモルフォロジー処理フィルターの適用中にエラーが発生しました");
                throw;
            }
        }

        /// <summary>
        /// テキストモードに基づいてパラメータを調整します
        /// </summary>
        private void AdjustParametersForTextMode(
            ref string operation, ref int kernelSize, ref int iterations, string textMode)
        {
            switch (textMode)
            {
                case "Thin":
                    // テキストを細くする（細字化）
                    if (operation != "Erode" && operation != "Open")
                    {
                        _logger.LogDebug("テキスト細字化モードのため、操作を'Erode'に変更します");
                        operation = "Erode";
                    }
                    // カーネルサイズと繰り返し回数を適切に調整
                    kernelSize = Math.Max(3, kernelSize);
                    iterations = Math.Max(1, iterations);
                    break;

                case "Thick":
                    // テキストを太くする（太字化）
                    if (operation != "Dilate" && operation != "Close")
                    {
                        _logger.LogDebug("テキスト太字化モードのため、操作を'Dilate'に変更します");
                        operation = "Dilate";
                    }
                    // カーネルサイズと繰り返し回数を適切に調整
                    kernelSize = Math.Max(3, kernelSize);
                    iterations = Math.Max(1, iterations);
                    break;

                case "Separated":
                    // テキストを分離する（テキスト間の間隔を広げる）
                    if (operation != "Erode")
                    {
                        _logger.LogDebug("テキスト分離モードのため、操作を'Erode'に変更します");
                        operation = "Erode";
                    }
                    // カーネルサイズと繰り返し回数を大きめに設定
                    kernelSize = Math.Max(5, kernelSize);
                    iterations = Math.Max(2, iterations);
                    break;

                case "Connected":
                    // テキストを連結する（近接テキストをつなげる）
                    if (operation != "Dilate" && operation != "Close")
                    {
                        _logger.LogDebug("テキスト連結モードのため、操作を'Close'に変更します");
                        operation = "Close";
                    }
                    // カーネルサイズと繰り返し回数を適切に調整
                    kernelSize = Math.Max(5, kernelSize);
                    iterations = Math.Max(1, iterations);
                    break;

                case "Normal":
                default:
                    // デフォルト設定の維持
                    break;
            }
        }

        /// <inheritdoc/>
        public override ImageInfo GetOutputImageInfo(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);
            // モルフォロジー処理はフォーマットを変更しないが、
            // 理想的にはグレースケールまたは二値化された画像に適用すべき
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
