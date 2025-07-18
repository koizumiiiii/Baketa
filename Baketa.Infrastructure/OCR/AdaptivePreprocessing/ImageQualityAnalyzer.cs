using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using System.Diagnostics;

namespace Baketa.Infrastructure.OCR.AdaptivePreprocessing;

/// <summary>
/// 画像品質分析の実装クラス
/// </summary>
public class ImageQualityAnalyzer : IImageQualityAnalyzer
{
    private readonly ILogger<ImageQualityAnalyzer> _logger;

    public ImageQualityAnalyzer(ILogger<ImageQualityAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 画像の品質指標を分析します
    /// </summary>
    public async Task<ImageQualityMetrics> AnalyzeAsync(IAdvancedImage image)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("画像品質分析開始: {Width}x{Height}", image.Width, image.Height);

        try
        {
            // 並列で各指標を計算
            var contrastTask = CalculateContrastAsync(image);
            var brightnessTask = CalculateBrightnessAsync(image);
            var noiseTask = CalculateNoiseAsync(image);
            var sharpnessTask = CalculateSharpnessAsync(image);

            await Task.WhenAll(contrastTask, brightnessTask, noiseTask, sharpnessTask);

            var contrast = await contrastTask;
            var brightness = await brightnessTask;
            var noise = await noiseTask;
            var sharpness = await sharpnessTask;

            // 総合品質スコアを計算
            var overallQuality = CalculateOverallQuality(contrast, brightness, noise, sharpness);

            var metrics = new ImageQualityMetrics
            {
                Contrast = contrast,
                Brightness = brightness,
                NoiseLevel = noise,
                Sharpness = sharpness,
                Width = image.Width,
                Height = image.Height,
                OverallQuality = overallQuality
            };

            _logger.LogInformation(
                "画像品質分析完了: C={Contrast:F3}, B={Brightness:F3}, N={Noise:F3}, S={Sharpness:F3}, Q={Quality:F3} ({ElapsedMs}ms)",
                contrast, brightness, noise, sharpness, overallQuality, sw.ElapsedMilliseconds);

            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "画像品質分析中にエラーが発生しました");
            return CreateFallbackMetrics(image);
        }
    }

    /// <summary>
    /// 画像のコントラスト値を計算します
    /// </summary>
    public async Task<double> CalculateContrastAsync(IAdvancedImage image)
    {
        return await Task.Run(() =>
        {
            try
            {
                // 簡易的なコントラスト計算（標準偏差ベース）
                var pixels = GetImagePixelsAsGrayscale(image);
                if (pixels.Length == 0) return 0.5; // デフォルト値

                var mean = pixels.Average(p => (double)p);
                var variance = pixels.Select(p => Math.Pow(p - mean, 2)).Average();
                var stdDev = Math.Sqrt(variance);
                
                // 標準偏差を0-1の範囲に正規化（最大値を128と仮定）
                return Math.Min(stdDev / 128.0, 1.0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "コントラスト計算でエラーが発生しました");
                return 0.5; // デフォルト値
            }
        });
    }

    /// <summary>
    /// 画像の明度値を計算します
    /// </summary>
    public async Task<double> CalculateBrightnessAsync(IAdvancedImage image)
    {
        return await Task.Run(() =>
        {
            try
            {
                var pixels = GetImagePixelsAsGrayscale(image);
                if (pixels.Length == 0) return 0.5; // デフォルト値

                var averageBrightness = pixels.Average(p => (double)p);
                return averageBrightness / 255.0; // 0-1の範囲に正規化
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "明度計算でエラーが発生しました");
                return 0.5; // デフォルト値
            }
        });
    }

    /// <summary>
    /// 画像のノイズレベルを計算します
    /// </summary>
    public async Task<double> CalculateNoiseAsync(IAdvancedImage image)
    {
        return await Task.Run(() =>
        {
            try
            {
                // ラプラシアンフィルタを使用した簡易ノイズ検出
                var pixels = GetImagePixelsAsGrayscale(image);
                if (pixels.Length == 0) return 0.1; // デフォルト値

                var width = image.Width;
                var height = image.Height;
                var laplacianSum = 0.0;
                var count = 0;

                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        var center = pixels[y * width + x];
                        var neighbors = new[]
                        {
                            pixels[(y-1) * width + x],     // 上
                            pixels[(y+1) * width + x],     // 下
                            pixels[y * width + (x-1)],     // 左
                            pixels[y * width + (x+1)]      // 右
                        };

                        var laplacian = Math.Abs(4 * center - neighbors.Sum(n => (double)n));
                        laplacianSum += laplacian;
                        count++;
                    }
                }

                if (count == 0) return 0.1;

                var averageLaplacian = laplacianSum / count;
                // ノイズレベルを0-1の範囲に正規化
                return Math.Min(averageLaplacian / 512.0, 1.0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ノイズ計算でエラーが発生しました");
                return 0.1; // デフォルト値
            }
        });
    }

    /// <summary>
    /// テキスト密度を分析します
    /// </summary>
    public async Task<TextDensityMetrics> AnalyzeTextDensityAsync(IAdvancedImage image)
    {
        return await Task.Run(() =>
        {
            try
            {
                var pixels = GetImagePixelsAsGrayscale(image);
                if (pixels.Length == 0)
                {
                    return CreateDefaultTextDensityMetrics();
                }

                var width = image.Width;
                var height = image.Height;

                // エッジ検出（Sobel演算子）
                var edgeCount = 0;
                var totalPixels = width * height;

                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        var gx = CalculateSobelX(pixels, x, y, width);
                        var gy = CalculateSobelY(pixels, x, y, width);
                        var magnitude = Math.Sqrt(gx * gx + gy * gy);

                        if (magnitude > 30) // エッジ閾値
                        {
                            edgeCount++;
                        }
                    }
                }

                var edgeDensity = (double)edgeCount / totalPixels;

                // テキストサイズ推定（簡易版）
                var estimatedTextSize = EstimateTextSize(edgeDensity, width, height);
                
                // テキスト領域割合の推定
                var textAreaRatio = Math.Min(edgeDensity * 3.0, 1.0); // 経験的補正

                return new TextDensityMetrics
                {
                    EdgeDensity = edgeDensity,
                    EstimatedTextSize = estimatedTextSize,
                    TextAreaRatio = textAreaRatio,
                    EstimatedCharacterSpacing = estimatedTextSize * 0.1,
                    EstimatedLineSpacing = estimatedTextSize * 1.2,
                    TextOrientation = 0.0 // 水平テキストを仮定
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "テキスト密度分析でエラーが発生しました");
                return CreateDefaultTextDensityMetrics();
            }
        });
    }

    /// <summary>
    /// シャープネス値を計算します
    /// </summary>
    private async Task<double> CalculateSharpnessAsync(IAdvancedImage image)
    {
        return await Task.Run(() =>
        {
            try
            {
                var pixels = GetImagePixelsAsGrayscale(image);
                if (pixels.Length == 0) return 0.5;

                var width = image.Width;
                var height = image.Height;
                var laplacianVariance = 0.0;
                var count = 0;

                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        var center = pixels[y * width + x];
                        var laplacian = 
                            -center * 8 +
                            pixels[(y-1) * width + (x-1)] + pixels[(y-1) * width + x] + pixels[(y-1) * width + (x+1)] +
                            pixels[y * width + (x-1)] + pixels[y * width + (x+1)] +
                            pixels[(y+1) * width + (x-1)] + pixels[(y+1) * width + x] + pixels[(y+1) * width + (x+1)];

                        laplacianVariance += laplacian * laplacian;
                        count++;
                    }
                }

                if (count == 0) return 0.5;

                var variance = laplacianVariance / count;
                return Math.Min(Math.Sqrt(variance) / 1000.0, 1.0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "シャープネス計算でエラーが発生しました");
                return 0.5;
            }
        });
    }

    /// <summary>
    /// 総合品質スコアを計算します
    /// </summary>
    private double CalculateOverallQuality(double contrast, double brightness, double noise, double sharpness)
    {
        // 重み付き平均で総合スコアを計算
        var weightContrast = 0.3;
        var weightBrightness = 0.2;
        var weightNoise = 0.3; // ノイズは逆相関（少ない方が良い）
        var weightSharpness = 0.2;

        var qualityScore = 
            contrast * weightContrast +
            (brightness > 0.5 ? 1.0 - Math.Abs(brightness - 0.5) * 2 : brightness * 2) * weightBrightness +
            (1.0 - noise) * weightNoise +
            sharpness * weightSharpness;

        return Math.Max(0.0, Math.Min(1.0, qualityScore));
    }

    /// <summary>
    /// 画像ピクセルをグレースケールで取得します
    /// </summary>
    private byte[] GetImagePixelsAsGrayscale(IAdvancedImage image)
    {
        try
        {
            // 簡易実装：画像データから擬似的にグレースケール値を生成
            var imageDataTask = image.ToByteArrayAsync();
            var imageData = imageDataTask.GetAwaiter().GetResult();
            var pixelCount = image.Width * image.Height;
            var grayscalePixels = new byte[pixelCount];

            if (imageData.Length >= pixelCount)
            {
                for (int i = 0; i < pixelCount; i++)
                {
                    grayscalePixels[i] = (byte)(imageData[i % imageData.Length] % 256);
                }
            }
            else
            {
                // データが不足している場合はパターンで補完
                for (int i = 0; i < pixelCount; i++)
                {
                    grayscalePixels[i] = (byte)((i % 255) + 1);
                }
            }

            return grayscalePixels;
        }
        catch
        {
            // エラー時はデフォルトパターンを返す
            var pixelCount = image.Width * image.Height;
            var grayscalePixels = new byte[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                grayscalePixels[i] = 128; // 中間グレー
            }
            return grayscalePixels;
        }
    }

    /// <summary>
    /// Sobel演算子のX方向成分を計算
    /// </summary>
    private double CalculateSobelX(byte[] pixels, int x, int y, int width)
    {
        return
            -1 * pixels[(y-1) * width + (x-1)] + 1 * pixels[(y-1) * width + (x+1)] +
            -2 * pixels[y * width + (x-1)] + 2 * pixels[y * width + (x+1)] +
            -1 * pixels[(y+1) * width + (x-1)] + 1 * pixels[(y+1) * width + (x+1)];
    }

    /// <summary>
    /// Sobel演算子のY方向成分を計算
    /// </summary>
    private double CalculateSobelY(byte[] pixels, int x, int y, int width)
    {
        return
            -1 * pixels[(y-1) * width + (x-1)] + -2 * pixels[(y-1) * width + x] + -1 * pixels[(y-1) * width + (x+1)] +
             1 * pixels[(y+1) * width + (x-1)] +  2 * pixels[(y+1) * width + x] +  1 * pixels[(y+1) * width + (x+1)];
    }

    /// <summary>
    /// テキストサイズを推定します
    /// </summary>
    private double EstimateTextSize(double edgeDensity, int width, int height)
    {
        // 経験的な推定式
        var baseSize = Math.Min(width, height) * 0.05; // 画像サイズの5%を基準
        var densityFactor = Math.Max(0.5, Math.Min(2.0, 1.0 / Math.Max(edgeDensity, 0.01)));
        return baseSize * densityFactor;
    }

    /// <summary>
    /// フォールバック用の品質指標を作成
    /// </summary>
    private ImageQualityMetrics CreateFallbackMetrics(IAdvancedImage image)
    {
        return new ImageQualityMetrics
        {
            Contrast = 0.5,
            Brightness = 0.5,
            NoiseLevel = 0.1,
            Sharpness = 0.5,
            Width = image.Width,
            Height = image.Height,
            OverallQuality = 0.5
        };
    }

    /// <summary>
    /// デフォルトのテキスト密度指標を作成
    /// </summary>
    private TextDensityMetrics CreateDefaultTextDensityMetrics()
    {
        return new TextDensityMetrics
        {
            EdgeDensity = 0.05,
            EstimatedTextSize = 16.0,
            TextAreaRatio = 0.3,
            EstimatedCharacterSpacing = 1.6,
            EstimatedLineSpacing = 19.2,
            TextOrientation = 0.0
        };
    }
}