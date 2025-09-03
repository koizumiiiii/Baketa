using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Models.ImageProcessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Baketa.Infrastructure.Imaging.ChangeDetection;

/// <summary>
/// 画像変化検知サービスの基本実装（レガシー互換性）
/// Difference Hash（dHash）によるPerceptual Hash実装
/// 注意: 新規実装では EnhancedImageChangeDetectionService を使用推奨
/// </summary>
public sealed class ImageChangeDetectionService : IImageChangeDetectionService
{
    private readonly ILogger<ImageChangeDetectionService> _logger;
    private readonly IImageChangeMetricsService _metricsService;
    
    // レガシー設定用デフォルト値
    private readonly HashAlgorithmType _defaultAlgorithm = HashAlgorithmType.DifferenceHash;
    private readonly float _changeThreshold = 0.1f;

    public ImageChangeDetectionService(
        ILogger<ImageChangeDetectionService> logger,
        IImageChangeMetricsService metricsService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            var algorithm = _defaultAlgorithm;

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
            var hasChanged = IsSignificantChange(changePercentage, _changeThreshold);

            var result = hasChanged 
                ? ImageChangeResult.CreateChanged(previousHash, currentHash, changePercentage, algorithm, stopwatch.Elapsed)
                : ImageChangeResult.CreateNoChange(stopwatch.Elapsed);

            // メトリクス記録
            if (hasChanged)
            {
                _metricsService.RecordOcrExecuted(changePercentage, stopwatch.Elapsed);
            }
            else
            {
                _metricsService.RecordOcrSkipped(changePercentage, stopwatch.Elapsed);
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
            return ImageChangeResult.CreateChanged("ERROR", "ERROR", 1.0f, _defaultAlgorithm, stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public async Task<ImageChangeResult> DetectChangeAsync(
        IImage? previousImage, 
        IImage currentImage, 
        string contextId = "default",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentImage);
        
        if (previousImage == null)
        {
            // 初回検知の場合
            var currentHash = GeneratePerceptualHash(await ConvertImageToByteArrayAsync(currentImage), _defaultAlgorithm);
            return ImageChangeResult.CreateFirstTime(currentHash, _defaultAlgorithm, TimeSpan.Zero);
        }
        
        // IImage -> byte[] 変換して既存メソッドを呼び出し
        var prevBytes = await ConvertImageToByteArrayAsync(previousImage);
        var currBytes = await ConvertImageToByteArrayAsync(currentImage);
        
        return await DetectChangeAsync(prevBytes, currBytes, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<QuickFilterResult> QuickFilterAsync(
        IImage? previousImage, 
        IImage currentImage, 
        string contextId = "default")
    {
        if (previousImage == null)
        {
            return QuickFilterResult.PotentialChange;
        }
        
        // 簡易実装：基本的な変化検知結果から判定
        var changeResult = await DetectChangeAsync(previousImage, currentImage, contextId);
        
        return new QuickFilterResult
        {
            HasPotentialChange = changeResult.HasChanged,
            ProcessingTime = changeResult.ProcessingTime,
            MaxSimilarity = 1.0f - changeResult.ChangePercentage
        };
    }

    /// <inheritdoc />
    public async Task<ImageType> DetectImageTypeAsync(IImage image)
    {
        return await Task.FromResult(ImageType.Unknown); // 簡易実装
    }

    /// <inheritdoc />
    public async Task<RegionChangeResult[]> DetectRegionChangesAsync(
        IImage? previousImage,
        IImage currentImage,
        Rectangle[] regions,
        CancellationToken cancellationToken = default)
    {
        if (previousImage == null || regions.Length == 0)
        {
            return regions.Select(r => new RegionChangeResult(r, true, 0.0f)).ToArray();
        }
        
        // 簡易実装：全体の変化検知結果を各領域に適用
        var changeResult = await DetectChangeAsync(previousImage, currentImage, "default", cancellationToken);
        var similarity = 1.0f - changeResult.ChangePercentage;
        
        return regions.Select(r => new RegionChangeResult(r, changeResult.HasChanged, similarity)).ToArray();
    }

    /// <inheritdoc />
    public void ClearCache(string? contextId = null)
    {
        // レガシー実装ではキャッシュなし
        _logger.LogDebug("ClearCache called (no-op in legacy implementation)");
    }

    /// <inheritdoc />
    public ImageChangeDetectionStatistics GetStatistics()
    {
        // レガシー実装では統計なし
        return new ImageChangeDetectionStatistics
        {
            TotalProcessed = 0,
            Stage1Filtered = 0,
            Stage2Filtered = 0,
            Stage3Processed = 0,
            AverageStage1Time = TimeSpan.Zero,
            AverageStage2Time = TimeSpan.Zero,
            AverageStage3Time = TimeSpan.Zero,
            CacheHitRate = 0f,
            CurrentCacheSize = 0,
            FilteringEfficiency = 0f
        };
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

    /// <summary>
    /// 変化率から有意な変化かを判定
    /// </summary>
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

    /// <summary>
    /// IImageをbyte配列に変換
    /// </summary>
    private static async Task<byte[]> ConvertImageToByteArrayAsync(IImage image)
    {
        try
        {
            var imageData = await image.ToByteArrayAsync().ConfigureAwait(false);
            return imageData ?? Array.Empty<byte>();
        }
        catch (Exception)
        {
            return Array.Empty<byte>();
        }
    }
}