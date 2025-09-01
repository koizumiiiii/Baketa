using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Baketa.Infrastructure.Imaging.ChangeDetection;

/// <summary>
/// 画像変化検知サービスの実装
/// Difference Hash（dHash）によるPerceptual Hash実装
/// Phase 1: OCR処理最適化システム
/// </summary>
public sealed class ImageChangeDetectionService : IImageChangeDetectionService
{
    private readonly ILogger<ImageChangeDetectionService> _logger;
    private readonly IOptionsMonitor<ImageChangeDetectionSettings> _options;
    private readonly IImageChangeMetricsService _metricsService;

    public ImageChangeDetectionService(
        ILogger<ImageChangeDetectionService> logger,
        IOptionsMonitor<ImageChangeDetectionSettings> options,
        IImageChangeMetricsService metricsService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _metricsService = metricsService ?? throw new ArgumentNullException(nameof(metricsService));
    }

    /// <inheritdoc />
    public async Task<ImageChangeResult> DetectChangeAsync(
        byte[] previousImage, 
        byte[] currentImage, 
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            ArgumentNullException.ThrowIfNull(previousImage, nameof(previousImage));
            ArgumentNullException.ThrowIfNull(currentImage, nameof(currentImage));

            var settings = _options.CurrentValue;
            var algorithm = settings.DefaultAlgorithm;

            // 非同期でPerceptual Hash生成
            var (previousHash, currentHash) = await Task.Run(() =>
            {
                var prevHash = GeneratePerceptualHash(previousImage, algorithm);
                var currHash = GeneratePerceptualHash(currentImage, algorithm);
                return (prevHash, currHash);
            }, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            // ハミング距離計算
            var changePercentage = CalculateHammingDistancePercentage(previousHash, currentHash);
            var hasChanged = IsSignificantChange(changePercentage, settings.ChangeThreshold);

            var result = new ImageChangeResult
            {
                HasChanged = hasChanged,
                ChangePercentage = changePercentage,
                PreviousHash = previousHash,
                CurrentHash = currentHash,
                ProcessingTime = stopwatch.Elapsed,
                AlgorithmUsed = algorithm
            };

            // メトリクス記録（設定で有効な場合）
            if (settings.EnableMetrics)
            {
                if (hasChanged)
                {
                    _metricsService.RecordOcrExecuted(changePercentage, stopwatch.Elapsed);
                }
                else
                {
                    _metricsService.RecordOcrSkipped(changePercentage, stopwatch.Elapsed);
                }
            }

            _logger.LogDebug("🔄 画像変化検知: {HasChanged}, 変化率: {ChangePercentage:F1}%, 処理時間: {ProcessingTimeMs}ms",
                hasChanged, changePercentage * 100, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "💥 画像変化検知エラー: 処理時間 {ProcessingTimeMs}ms", stopwatch.ElapsedMilliseconds);
            
            // エラー時はデフォルトで変化ありとして処理を継続
            return new ImageChangeResult
            {
                HasChanged = true,
                ChangePercentage = 1.0f,
                PreviousHash = "ERROR",
                CurrentHash = "ERROR",
                ProcessingTime = stopwatch.Elapsed,
                AlgorithmUsed = _options.CurrentValue.DefaultAlgorithm
            };
        }
    }

    /// <inheritdoc />
    public string GeneratePerceptualHash(byte[] imageData, HashAlgorithmType algorithm = HashAlgorithmType.DifferenceHash)
    {
        ArgumentNullException.ThrowIfNull(imageData);

        try
        {
            using var ms = new MemoryStream(imageData);
            using var originalBitmap = new Bitmap(ms);
            
            return algorithm switch
            {
                HashAlgorithmType.AverageHash => GenerateAverageHash(originalBitmap),
                HashAlgorithmType.DifferenceHash => GenerateDifferenceHash(originalBitmap),
                HashAlgorithmType.PerceptualHash => GeneratePerceptualHashAdvanced(originalBitmap),
                _ => GenerateDifferenceHash(originalBitmap) // デフォルト
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 Perceptual Hash生成エラー: アルゴリズム {Algorithm}", algorithm);
            return "00000000"; // エラー時のフォールバック
        }
    }

    /// <inheritdoc />
    public bool IsSignificantChange(ImageChangeResult result, float threshold = 0.1f)
    {
        return IsSignificantChange(result.ChangePercentage, threshold);
    }

    private static bool IsSignificantChange(float changePercentage, float threshold)
    {
        return changePercentage >= threshold;
    }

    /// <summary>
    /// Difference Hash（dHash）生成
    /// エッジ変化に敏感で、ゲーム画面の変化検知に適している
    /// </summary>
    private static string GenerateDifferenceHash(Bitmap bitmap)
    {
        const int size = 8;
        
        // 9x8のグレースケール画像にリサイズ（横の差分を計算するため）
        using var resized = new Bitmap(size + 1, size, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(resized);
        
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
        graphics.DrawImage(bitmap, 0, 0, size + 1, size);

        var hash = 0UL;
        var bitIndex = 0;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var leftPixel = resized.GetPixel(x, y);
                var rightPixel = resized.GetPixel(x + 1, y);
                
                var leftGray = (leftPixel.R + leftPixel.G + leftPixel.B) / 3;
                var rightGray = (rightPixel.R + rightPixel.G + rightPixel.B) / 3;

                if (leftGray > rightGray)
                {
                    hash |= 1UL << bitIndex;
                }
                bitIndex++;
            }
        }

        return hash.ToString("X16"); // 64bit -> 16桁16進数
    }

    /// <summary>
    /// Average Hash（aHash）生成
    /// 高速だが精度は低い
    /// </summary>
    private static string GenerateAverageHash(Bitmap bitmap)
    {
        const int size = 8;
        
        using var resized = new Bitmap(size, size, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(resized);
        
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
        graphics.DrawImage(bitmap, 0, 0, size, size);

        // 平均輝度計算
        var totalBrightness = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var pixel = resized.GetPixel(x, y);
                totalBrightness += (pixel.R + pixel.G + pixel.B) / 3;
            }
        }
        var averageBrightness = totalBrightness / (size * size);

        // ハッシュ生成
        var hash = 0UL;
        var bitIndex = 0;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var pixel = resized.GetPixel(x, y);
                var brightness = (pixel.R + pixel.G + pixel.B) / 3;
                
                if (brightness >= averageBrightness)
                {
                    hash |= 1UL << bitIndex;
                }
                bitIndex++;
            }
        }

        return hash.ToString("X16");
    }

    /// <summary>
    /// Perceptual Hash（pHash）生成
    /// 高精度だが処理コストが高い
    /// </summary>
    private static string GeneratePerceptualHashAdvanced(Bitmap bitmap)
    {
        // 簡易実装：実際のpHashはDCT変換を使用するが、
        // ここでは16x16の拡張版dHashで代用
        const int size = 16;
        
        using var resized = new Bitmap(size + 1, size, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(resized);
        
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        graphics.DrawImage(bitmap, 0, 0, size + 1, size);

        var hashBytes = new byte[32]; // 256bit
        var bitIndex = 0;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var leftPixel = resized.GetPixel(x, y);
                var rightPixel = resized.GetPixel(x + 1, y);
                
                var leftGray = (leftPixel.R + leftPixel.G + leftPixel.B) / 3;
                var rightGray = (rightPixel.R + rightPixel.G + rightPixel.B) / 3;

                if (leftGray > rightGray)
                {
                    hashBytes[bitIndex / 8] |= (byte)(1 << (bitIndex % 8));
                }
                bitIndex++;
            }
        }

        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// 2つのハッシュ間のハミング距離を変化率として計算
    /// </summary>
    private static float CalculateHammingDistancePercentage(string hash1, string hash2)
    {
        if (string.IsNullOrEmpty(hash1) || string.IsNullOrEmpty(hash2) || hash1.Length != hash2.Length)
        {
            return 1.0f; // 完全に異なるとして扱う
        }

        var diffCount = 0;
        var totalBits = hash1.Length * 4; // 16進数1文字 = 4bit

        try
        {
            var value1 = Convert.FromHexString(hash1);
            var value2 = Convert.FromHexString(hash2);

            for (int i = 0; i < Math.Min(value1.Length, value2.Length); i++)
            {
                var xor = (byte)(value1[i] ^ value2[i]);
                diffCount += CountSetBits(xor);
            }

            return (float)diffCount / totalBits;
        }
        catch
        {
            return 1.0f; // 変換エラー時は異なるとして扱う
        }
    }

    /// <summary>
    /// バイト値の立っているビット数をカウント（ポピュレーションカウント）
    /// </summary>
    private static int CountSetBits(byte value)
    {
        var count = 0;
        while (value != 0)
        {
            count++;
            value &= (byte)(value - 1); // 最下位の1ビットをクリア
        }
        return count;
    }
}