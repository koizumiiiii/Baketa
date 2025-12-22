using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Models.ImageProcessing;
using Microsoft.Extensions.Logging;

// GDI+とCore.Memoryの名前空間競合を解決
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;
using GdiRectangle = System.Drawing.Rectangle;

namespace Baketa.Infrastructure.Imaging.ChangeDetection;

/// <summary>
/// 最適化Perceptual Hashサービス
/// P0: OpenCV SIMD最適化による4種類ハッシュアルゴリズム対応
/// Geminiフィードバック反映: WaveletHash追加、ゲーム特化最適化
/// 処理時間目標: <1ms (Stage1), <3ms (Stage2), <5ms (Stage3)
/// </summary>
public sealed class OptimizedPerceptualHashService : IPerceptualHashService
{
    private readonly ILogger<OptimizedPerceptualHashService> _logger;

    // ゲーム特化アルゴリズム最適化マッピング
    private static readonly Dictionary<ImageType, HashAlgorithmType> OptimalAlgorithms = new()
    {
        [ImageType.GameUI] = HashAlgorithmType.DifferenceHash,     // UI要素のエッジ変化に敏感
        [ImageType.GameScene] = HashAlgorithmType.WaveletHash,     // シーン変化に適した周波数解析
        [ImageType.Application] = HashAlgorithmType.AverageHash,   // 一般アプリは高速処理優先
        [ImageType.Unknown] = HashAlgorithmType.DifferenceHash     // デフォルト推奨
    };

    // SSIM計算用係数
    private const double C1 = 6.5025;      // (0.01 * 255)^2
    private const double C2 = 58.5225;     // (0.03 * 255)^2

    public OptimizedPerceptualHashService(ILogger<OptimizedPerceptualHashService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ComputeHash(IImage image, HashAlgorithmType algorithm)
    {
        ArgumentNullException.ThrowIfNull(image);

        try
        {
            // 🔥 Critical Fix: IImage -> Bitmap変換とリソース管理
            using var bitmap = ConvertToBitmap(image);

            return algorithm switch
            {
                HashAlgorithmType.AverageHash => ComputeAverageHashOptimized(bitmap),
                HashAlgorithmType.DifferenceHash => ComputeDifferenceHashOptimized(bitmap),
                HashAlgorithmType.PerceptualHash => ComputePerceptualHashOptimized(bitmap),
                HashAlgorithmType.WaveletHash => ComputeWaveletHashOptimized(bitmap),
                _ => ComputeDifferenceHashOptimized(bitmap) // デフォルト
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 ハッシュ計算エラー - Algorithm: {Algorithm}", algorithm);
            return "0000000000000000"; // エラー時の安全なフォールバック
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// [Issue #229] 画像の指定領域に対してハッシュを計算（グリッド分割用）
    /// 8x8ハッシュを使用して高速計算（64ビット）
    /// </summary>
    public string ComputeHashForRegion(IImage image, GdiRectangle region, HashAlgorithmType algorithm)
    {
        ArgumentNullException.ThrowIfNull(image);

        try
        {
            using var fullBitmap = ConvertToBitmap(image);

            // 領域の境界チェック
            var clampedRegion = ClampRegion(region, fullBitmap.Width, fullBitmap.Height);
            if (clampedRegion.Width <= 0 || clampedRegion.Height <= 0)
            {
                return "0000000000000000";
            }

            // 領域を切り出し
            using var regionBitmap = fullBitmap.Clone(clampedRegion, fullBitmap.PixelFormat);

            // 8x8ハッシュを計算（グリッド分割では軽量ハッシュが有効）
            return algorithm switch
            {
                HashAlgorithmType.DifferenceHash => ComputeDifferenceHash8x8(regionBitmap),
                HashAlgorithmType.AverageHash => ComputeAverageHash8x8(regionBitmap),
                _ => ComputeDifferenceHash8x8(regionBitmap) // デフォルトはDifferenceHash
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 領域ハッシュ計算エラー - Region: {Region}, Algorithm: {Algorithm}", region, algorithm);
            return "0000000000000000";
        }
    }

    /// <summary>
    /// [Issue #229] 領域を画像境界内に収める
    /// </summary>
    private static GdiRectangle ClampRegion(GdiRectangle region, int imageWidth, int imageHeight)
    {
        var x = Math.Max(0, Math.Min(region.X, imageWidth - 1));
        var y = Math.Max(0, Math.Min(region.Y, imageHeight - 1));
        var width = Math.Min(region.Width, imageWidth - x);
        var height = Math.Min(region.Height, imageHeight - y);

        return new GdiRectangle(x, y, width, height);
    }

    /// <summary>
    /// [Issue #229] 軽量8x8 DifferenceHash（グリッド分割用）
    /// 64ビットハッシュで高速計算
    /// </summary>
    private string ComputeDifferenceHash8x8(Bitmap bitmap)
    {
        const int size = 8;

        using var resized = new Bitmap(size + 1, size, GdiPixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(resized);

        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
        graphics.DrawImage(bitmap, 0, 0, size + 1, size);

        var lockData = resized.LockBits(new GdiRectangle(0, 0, size + 1, size),
            ImageLockMode.ReadOnly, GdiPixelFormat.Format24bppRgb);

        try
        {
            ulong hash = 0;
            var bitIndex = 0;
            var stride = lockData.Stride;

            unsafe
            {
                byte* ptr = (byte*)lockData.Scan0;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var leftOffset = y * stride + x * 3;
                        var rightOffset = y * stride + (x + 1) * 3;

                        var leftGray = (ptr[leftOffset] + ptr[leftOffset + 1] + ptr[leftOffset + 2]) / 3;
                        var rightGray = (ptr[rightOffset] + ptr[rightOffset + 1] + ptr[rightOffset + 2]) / 3;

                        if (leftGray > rightGray)
                        {
                            hash |= 1UL << bitIndex;
                        }
                        bitIndex++;
                    }
                }
            }

            return hash.ToString("X16"); // 64ビット → 16文字の16進数
        }
        finally
        {
            resized.UnlockBits(lockData);
        }
    }

    /// <summary>
    /// [Issue #229] 軽量8x8 AverageHash（グリッド分割用）
    /// 64ビットハッシュで高速計算
    /// </summary>
    private string ComputeAverageHash8x8(Bitmap bitmap)
    {
        const int size = 8;

        using var resized = new Bitmap(size, size, GdiPixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(resized);

        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.DrawImage(bitmap, 0, 0, size, size);

        var lockData = resized.LockBits(new GdiRectangle(0, 0, size, size),
            ImageLockMode.ReadOnly, GdiPixelFormat.Format24bppRgb);

        try
        {
            var stride = lockData.Stride;
            var totalBrightness = 0;
            var pixels = size * size;

            unsafe
            {
                byte* ptr = (byte*)lockData.Scan0;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var offset = y * stride + x * 3;
                        totalBrightness += (ptr[offset] + ptr[offset + 1] + ptr[offset + 2]) / 3;
                    }
                }
            }

            var averageBrightness = totalBrightness / pixels;
            ulong hash = 0;
            var bitIndex = 0;

            unsafe
            {
                byte* ptr = (byte*)lockData.Scan0;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var offset = y * stride + x * 3;
                        var brightness = (ptr[offset] + ptr[offset + 1] + ptr[offset + 2]) / 3;

                        if (brightness >= averageBrightness)
                        {
                            hash |= 1UL << bitIndex;
                        }
                        bitIndex++;
                    }
                }
            }

            return hash.ToString("X16"); // 64ビット → 16文字の16進数
        }
        finally
        {
            resized.UnlockBits(lockData);
        }
    }

    /// <inheritdoc />
    public float CompareHashes(string hash1, string hash2, HashAlgorithmType algorithm)
    {
        if (string.IsNullOrEmpty(hash1) || string.IsNullOrEmpty(hash2))
        {
            return 0.0f; // 完全に異なる
        }

        if (hash1 == hash2)
        {
            return 1.0f; // 完全一致
        }

        try
        {
            var hammingDistance = CalculateHammingDistance(hash1, hash2);
            var maxBits = hash1.Length * 4; // 16進数1文字=4bit

            // アルゴリズム別類似度調整
            var similarity = 1.0f - ((float)hammingDistance / maxBits);

            return algorithm switch
            {
                HashAlgorithmType.AverageHash => Math.Max(0f, similarity - 0.05f),      // 少し厳しく
                HashAlgorithmType.DifferenceHash => similarity,                          // 標準
                HashAlgorithmType.PerceptualHash => Math.Min(1f, similarity + 0.1f),   // 少し寛大に
                HashAlgorithmType.WaveletHash => AdjustWaveletSimilarity(similarity),   // 独自調整
                _ => similarity
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ハッシュ比較エラー - Hash1: {Hash1}, Hash2: {Hash2}",
                hash1?[..Math.Min(8, hash1.Length)], hash2?[..Math.Min(8, hash2.Length)]);
            return 0.0f;
        }
    }

    /// <inheritdoc />
    public HashAlgorithmType GetOptimalAlgorithm(ImageType imageType)
    {
        return OptimalAlgorithms.TryGetValue(imageType, out var algorithm)
            ? algorithm
            : HashAlgorithmType.DifferenceHash;
    }

    /// <inheritdoc />
    public int CalculateHammingDistance(string hash1, string hash2)
    {
        if (string.IsNullOrEmpty(hash1) || string.IsNullOrEmpty(hash2))
        {
            return int.MaxValue; // 完全に異なる
        }

        if (hash1.Length != hash2.Length)
        {
            return Math.Max(hash1.Length, hash2.Length) * 4; // 最大距離
        }

        try
        {
            var distance = 0;

            // 16進数文字単位での比較（高速化）
            for (int i = 0; i < hash1.Length; i++)
            {
                if (hash1[i] != hash2[i])
                {
                    // 16進数文字の差分ビット数を計算
                    var val1 = Convert.ToInt32(hash1[i].ToString(), 16);
                    var val2 = Convert.ToInt32(hash2[i].ToString(), 16);
                    distance += CountSetBits((byte)(val1 ^ val2));
                }
            }

            return distance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ハミング距離計算エラー");
            return int.MaxValue;
        }
    }

    /// <inheritdoc />
    public async Task<float> CalculateSSIMAsync(IImage image1, IImage image2)
    {
        ArgumentNullException.ThrowIfNull(image1);
        ArgumentNullException.ThrowIfNull(image2);

        return await Task.Run(() =>
        {
            try
            {
                // 🔥 Critical Fix: Bitmapリソース管理
                using var bitmap1 = ConvertToBitmap(image1);
                using var bitmap2 = ConvertToBitmap(image2);

                return CalculateSSIMOptimized(bitmap1, bitmap2);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 SSIM計算エラー");
                return 0.0f; // エラー時は類似性なしとする
            }
        });
    }

    #region Optimized Hash Implementations

    /// <summary>
    /// 最適化Average Hash計算（Stage 1専用）
    /// [Issue #230] 32x32ハッシュ対応 - テキスト変更検出精度向上
    /// [Gemini Review] ArrayPool導入でGC圧力軽減
    /// 目標: <3ms処理
    /// </summary>
    private string ComputeAverageHashOptimized(Bitmap bitmap)
    {
        // [Issue #230] 8x8 → 32x32に拡大（1024ビットハッシュ）
        const int size = 32;
        const int hashSize = 128; // 32x32 = 1024ビット = 128バイト

        using var resized = new Bitmap(size, size, GdiPixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(resized);

        // 高速リサイズ設定
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.DrawImage(bitmap, 0, 0, size, size);

        // 平均輝度の高速計算
        var lockData = resized.LockBits(new GdiRectangle(0, 0, size, size),
            ImageLockMode.ReadOnly, GdiPixelFormat.Format24bppRgb);

        // [Gemini Review] ArrayPoolでGC圧力軽減
        var hashBytes = ArrayPool<byte>.Shared.Rent(hashSize);
        try
        {
            // 配列をクリア（前回の値が残っている可能性）
            Array.Clear(hashBytes, 0, hashSize);

            var stride = lockData.Stride;
            var scan0 = lockData.Scan0;

            var totalBrightness = 0L; // 32x32では合計値が大きくなるためlong使用
            var pixels = size * size;

            unsafe
            {
                byte* ptr = (byte*)scan0;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var offset = y * stride + x * 3;
                        var brightness = (ptr[offset] + ptr[offset + 1] + ptr[offset + 2]) / 3;
                        totalBrightness += brightness;
                    }
                }
            }

            var averageBrightness = (int)(totalBrightness / pixels);
            var bitIndex = 0;

            unsafe
            {
                byte* ptr = (byte*)scan0;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var offset = y * stride + x * 3;
                        var brightness = (ptr[offset] + ptr[offset + 1] + ptr[offset + 2]) / 3;

                        if (brightness >= averageBrightness)
                        {
                            var byteIndex = bitIndex / 8;
                            var bitPosition = bitIndex % 8;
                            hashBytes[byteIndex] |= (byte)(1 << bitPosition);
                        }
                        bitIndex++;
                    }
                }
            }

            // 128バイト → 256文字の16進数文字列
            return Convert.ToHexString(hashBytes.AsSpan(0, hashSize));
        }
        finally
        {
            resized.UnlockBits(lockData);
            ArrayPool<byte>.Shared.Return(hashBytes);
        }
    }

    /// <summary>
    /// 最適化Difference Hash計算（Stage 1-2対応）
    /// [Issue #230] 32x32ハッシュ対応 - テキスト変更検出精度向上
    /// [Gemini Review] ArrayPool導入でGC圧力軽減
    /// 720p画面で1ブロック約40x22ピクセル、テキストボックス単位の変化を検出可能
    /// 目標: <5ms処理、エッジ検出最適化
    /// </summary>
    private string ComputeDifferenceHashOptimized(Bitmap bitmap)
    {
        // [Issue #230] 8x8 → 32x32に拡大（1024ビットハッシュ）
        const int size = 32;
        const int hashSize = 128; // 32x32 = 1024ビット = 128バイト

        using var resized = new Bitmap(size + 1, size, GdiPixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(resized);

        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
        graphics.DrawImage(bitmap, 0, 0, size + 1, size);

        var lockData = resized.LockBits(new GdiRectangle(0, 0, size + 1, size),
            ImageLockMode.ReadOnly, GdiPixelFormat.Format24bppRgb);

        // [Gemini Review] ArrayPoolでGC圧力軽減
        var hashBytes = ArrayPool<byte>.Shared.Rent(hashSize);
        try
        {
            // 配列をクリア（前回の値が残っている可能性）
            Array.Clear(hashBytes, 0, hashSize);

            var bitIndex = 0;
            var stride = lockData.Stride;

            unsafe
            {
                byte* ptr = (byte*)lockData.Scan0;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var leftOffset = y * stride + x * 3;
                        var rightOffset = y * stride + (x + 1) * 3;

                        // RGB -> 輝度変換（高速化）
                        var leftGray = (ptr[leftOffset] + ptr[leftOffset + 1] + ptr[leftOffset + 2]) / 3;
                        var rightGray = (ptr[rightOffset] + ptr[rightOffset + 1] + ptr[rightOffset + 2]) / 3;

                        if (leftGray > rightGray)
                        {
                            var byteIndex = bitIndex / 8;
                            var bitPosition = bitIndex % 8;
                            hashBytes[byteIndex] |= (byte)(1 << bitPosition);
                        }
                        bitIndex++;
                    }
                }
            }

            // 128バイト → 256文字の16進数文字列
            return Convert.ToHexString(hashBytes.AsSpan(0, hashSize));
        }
        finally
        {
            resized.UnlockBits(lockData);
            ArrayPool<byte>.Shared.Return(hashBytes);
        }
    }

    /// <summary>
    /// 最適化Perceptual Hash計算（Stage 2-3対応）
    /// 目標: <3ms処理、DCT近似による高精度
    /// </summary>
    private string ComputePerceptualHashOptimized(Bitmap bitmap)
    {
        const int size = 32; // pHashは通常32x32

        using var resized = new Bitmap(size, size, GdiPixelFormat.Format8bppIndexed);
        using var temp = new Bitmap(size, size, GdiPixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(temp);

        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        graphics.DrawImage(bitmap, 0, 0, size, size);

        // グレースケール変換（高速化）
        var grayData = ConvertToGrayscale(temp);

        // DCT近似（高速実装）
        var dctData = ApproximateDCT(grayData, size);

        // ハッシュ生成（上位64要素から）
        var median = CalculateMedian(dctData, 64);
        var hash = 0UL;

        for (int i = 0; i < 64; i++)
        {
            if (dctData[i] > median)
            {
                hash |= 1UL << i;
            }
        }

        return hash.ToString("X16");
    }

    /// <summary>
    /// 最適化Wavelet Hash計算（Stage 3専用）
    /// 目標: <5ms処理、ゲーム画面の周波数解析特化
    /// </summary>
    private string ComputeWaveletHashOptimized(Bitmap bitmap)
    {
        const int size = 16; // Waveletは16x16が適切

        using var resized = new Bitmap(size, size, GdiPixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(resized);

        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        graphics.DrawImage(bitmap, 0, 0, size, size);

        // グレースケール変換
        var grayData = ConvertToGrayscale(resized);

        // 簡易Haar Wavelet変換（2Dハール変換）
        var waveletCoeff = ApplyHaarWavelet2D(grayData, size);

        // 低周波成分からハッシュ生成（左上8x8領域）
        var hash = 0UL;
        var bitIndex = 0;
        var avgCoeff = waveletCoeff.Take(64).Average();

        for (int i = 0; i < 64 && bitIndex < 64; i++)
        {
            if (waveletCoeff[i] > avgCoeff)
            {
                hash |= 1UL << bitIndex;
            }
            bitIndex++;
        }

        return hash.ToString("X16");
    }

    #endregion

    #region SSIM Optimization

    /// <summary>
    /// 最適化SSIM計算
    /// 目標: <5ms処理、構造的類似性の高精度計算
    /// </summary>
    private float CalculateSSIMOptimized(Bitmap bitmap1, Bitmap bitmap2)
    {
        const int windowSize = 8; // 計算ウィンドウサイズ

        if (bitmap1.Width != bitmap2.Width || bitmap1.Height != bitmap2.Height)
        {
            // サイズ不一致時は小さい方にリサイズ
            var minWidth = Math.Min(bitmap1.Width, bitmap2.Width);
            var minHeight = Math.Min(bitmap1.Height, bitmap2.Height);

            using var resized1 = new Bitmap(minWidth, minHeight);
            using var resized2 = new Bitmap(minWidth, minHeight);
            using var g1 = Graphics.FromImage(resized1);
            using var g2 = Graphics.FromImage(resized2);

            g1.DrawImage(bitmap1, 0, 0, minWidth, minHeight);
            g2.DrawImage(bitmap2, 0, 0, minWidth, minHeight);

            return CalculateSSIMWindow(resized1, resized2, windowSize);
        }

        return CalculateSSIMWindow(bitmap1, bitmap2, windowSize);
    }

    /// <summary>
    /// ウィンドウベースSSIM計算
    /// </summary>
    private float CalculateSSIMWindow(Bitmap bitmap1, Bitmap bitmap2, int windowSize)
    {
        var width = bitmap1.Width;
        var height = bitmap1.Height;
        var ssimSum = 0.0;
        var windowCount = 0;

        // ウィンドウ単位でSSIM計算
        for (int y = 0; y <= height - windowSize; y += windowSize / 2)
        {
            for (int x = 0; x <= width - windowSize; x += windowSize / 2)
            {
                var window1 = ExtractWindow(bitmap1, x, y, windowSize);
                var window2 = ExtractWindow(bitmap2, x, y, windowSize);

                ssimSum += CalculateWindowSSIM(window1, window2);
                windowCount++;
            }
        }

        return windowCount > 0 ? (float)(ssimSum / windowCount) : 0.0f;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// IImage -> Bitmap変換（リソース管理に注意）
    /// 戻り値のBitmapは呼び出し側でusingブロックでの適切な破棄が必要
    /// 🔥 [Issue #230] LockPixelData()を使用してピクセルデータを正しくコピー
    /// 🔧 [Gemini Review] リソースリーク対策、Gray8パレット設定、RGBA→BGRAチャンネルスワップ追加
    /// </summary>
    private Bitmap ConvertToBitmap(IImage image)
    {
        Bitmap? bitmap = null;
        try
        {
            // 🔥 [Issue #230] LockPixelData()で生ピクセルデータを取得
            using var pixelLock = image.LockPixelData();
            var pixelData = pixelLock.Data;
            var stride = pixelLock.Stride;
            var pixelFormat = image.PixelFormat;

            // GDI+のPixelFormatに変換
            // 注: GDI+ Format32bppArgbはメモリ上でBGRA順序（Windows標準）
            var gdiPixelFormat = pixelFormat switch
            {
                ImagePixelFormat.Bgra32 => GdiPixelFormat.Format32bppArgb,
                ImagePixelFormat.Rgba32 => GdiPixelFormat.Format32bppArgb,
                ImagePixelFormat.Rgb24 => GdiPixelFormat.Format24bppRgb,
                ImagePixelFormat.Bgr24 => GdiPixelFormat.Format24bppRgb,
                ImagePixelFormat.Gray8 => GdiPixelFormat.Format8bppIndexed,
                _ => GdiPixelFormat.Format32bppArgb
            };

            bitmap = new Bitmap(image.Width, image.Height, gdiPixelFormat);

            // 🔧 [Gemini Review] Gray8の場合は明示的にグレースケールパレットを設定
            if (gdiPixelFormat == GdiPixelFormat.Format8bppIndexed)
            {
                var palette = bitmap.Palette;
                for (int i = 0; i < 256; i++)
                {
                    palette.Entries[i] = Color.FromArgb(i, i, i);
                }
                bitmap.Palette = palette;
            }

            var bitmapData = bitmap.LockBits(
                new GdiRectangle(0, 0, image.Width, image.Height),
                ImageLockMode.WriteOnly,
                gdiPixelFormat);

            try
            {
                unsafe
                {
                    var destPtr = (byte*)bitmapData.Scan0;
                    var destStride = bitmapData.Stride;
                    var bytesPerPixel = GetBytesPerPixel(pixelFormat);

                    fixed (byte* srcPtr = pixelData)
                    {
                        // 🔧 [Gemini Review] RGBA/RGB形式はR-Bチャンネルスワップが必要
                        // GDI+はBGRA/BGR順序を期待するため
                        var needsChannelSwap = pixelFormat == ImagePixelFormat.Rgba32 ||
                                               pixelFormat == ImagePixelFormat.Rgb24;

                        if (needsChannelSwap)
                        {
                            CopyWithChannelSwap(srcPtr, destPtr, image.Width, image.Height,
                                stride, destStride, bytesPerPixel);
                        }
                        else
                        {
                            // BGRA32, BGR24, Gray8は直接コピー
                            for (int y = 0; y < image.Height; y++)
                            {
                                var srcOffset = y * stride;
                                var destOffset = y * destStride;
                                var rowBytes = image.Width * bytesPerPixel;

                                Buffer.MemoryCopy(
                                    srcPtr + srcOffset,
                                    destPtr + destOffset,
                                    rowBytes,
                                    rowBytes);
                            }
                        }
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            // 🔧 [Gemini Review] 例外時のリソースリーク対策
            bitmap?.Dispose();
            _logger.LogError(ex, "💥 IImage->Bitmap変換エラー");
            // 最小サイズのダミーBitmapを返す（呼び出し側でDispose必要）
            return new Bitmap(1, 1, GdiPixelFormat.Format24bppRgb);
        }
    }

    /// <summary>
    /// 🔧 [Gemini Review] RGBA→BGRA / RGB→BGR チャンネルスワップ付きコピー
    /// GDI+はBGR(A)順序を期待するため、RGBA/RGB形式の場合はR-Bをスワップ
    /// </summary>
    private static unsafe void CopyWithChannelSwap(
        byte* src, byte* dest,
        int width, int height,
        int srcStride, int destStride,
        int bytesPerPixel)
    {
        for (int y = 0; y < height; y++)
        {
            var srcRow = src + y * srcStride;
            var destRow = dest + y * destStride;

            for (int x = 0; x < width; x++)
            {
                var i = x * bytesPerPixel;
                // R-B スワップ: RGBA → BGRA, RGB → BGR
                destRow[i] = srcRow[i + 2];     // Dest[B] = Src[R]
                destRow[i + 1] = srcRow[i + 1]; // Dest[G] = Src[G]
                destRow[i + 2] = srcRow[i];     // Dest[R] = Src[B]
                if (bytesPerPixel == 4)
                {
                    destRow[i + 3] = srcRow[i + 3]; // Dest[A] = Src[A]
                }
            }
        }
    }

    /// <summary>
    /// ImagePixelFormatからバイト/ピクセルを計算
    /// </summary>
    private static int GetBytesPerPixel(ImagePixelFormat pixelFormat) => pixelFormat switch
    {
        ImagePixelFormat.Bgra32 => 4,
        ImagePixelFormat.Rgba32 => 4,
        ImagePixelFormat.Rgb24 => 3,
        ImagePixelFormat.Bgr24 => 3,
        ImagePixelFormat.Gray8 => 1,
        _ => 4
    };

    /// <summary>
    /// グレースケール変換（unsafeポインタ最適化）
    /// Issue #195: GetPixel()のフォールバック変換を廃止、直接ポインタアクセス
    /// Note: LockBitsはFormat24bppRgbへの自動変換を行う（元フォーマットが異なる場合は変換コストが発生）
    /// </summary>
    private float[] ConvertToGrayscale(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var width = bitmap.Width;
        var height = bitmap.Height;

        if (width <= 0 || height <= 0)
        {
            return [];
        }

        var grayData = new float[width * height];

        // LockBitsはFormat24bppRgbを指定すると、元のフォーマットから自動変換する
        // 32bppArgb等からの変換時は内部でコピーが発生するが、GetPixel()よりは高速
        var lockData = bitmap.LockBits(
            new GdiRectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            GdiPixelFormat.Format24bppRgb);

        try
        {
            var stride = lockData.Stride;

            unsafe
            {
                byte* ptr = (byte*)lockData.Scan0;

                // 行ポインタを先に計算することで内部ループの乗算を削減
                for (int y = 0; y < height; y++)
                {
                    byte* row = ptr + (y * stride);
                    var rowOffset = y * width;

                    for (int x = 0; x < width; x++)
                    {
                        var pixelOffset = x * 3;
                        // BGR順序 (GdiPixelFormat.Format24bppRgb)
                        var b = row[pixelOffset];
                        var g = row[pixelOffset + 1];
                        var r = row[pixelOffset + 2];
                        grayData[rowOffset + x] = r * 0.299f + g * 0.587f + b * 0.114f;
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(lockData);
        }

        return grayData;
    }

    /// <summary>
    /// 近似DCT変換
    /// </summary>
    private float[] ApproximateDCT(float[] data, int size)
    {
        // 簡易DCT実装（実用的には OpenCV や専用ライブラリを使用推奨）
        var result = new float[size * size];

        for (int v = 0; v < size; v++)
        {
            for (int u = 0; u < size; u++)
            {
                var sum = 0.0;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        sum += data[y * size + x] *
                               Math.Cos((2 * x + 1) * u * Math.PI / (2 * size)) *
                               Math.Cos((2 * y + 1) * v * Math.PI / (2 * size));
                    }
                }

                result[v * size + u] = (float)sum;
            }
        }

        return result;
    }

    /// <summary>
    /// 簡易Haar Wavelet 2D変換
    /// </summary>
    private float[] ApplyHaarWavelet2D(float[] data, int size)
    {
        var result = new float[size * size];
        Array.Copy(data, result, data.Length);

        // 行方向変換
        for (int y = 0; y < size; y++)
        {
            ApplyHaarWavelet1D(result, y * size, size);
        }

        // 列方向変換
        var temp = new float[size];
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                temp[y] = result[y * size + x];
            }

            ApplyHaarWavelet1D(temp, 0, size);

            for (int y = 0; y < size; y++)
            {
                result[y * size + x] = temp[y];
            }
        }

        return result;
    }

    /// <summary>
    /// 1D Haar Wavelet変換
    /// </summary>
    private void ApplyHaarWavelet1D(float[] data, int start, int length)
    {
        if (length < 2) return;

        var temp = new float[length];
        var half = length / 2;

        // 低周波成分（平均）
        for (int i = 0; i < half; i++)
        {
            temp[i] = (data[start + i * 2] + data[start + i * 2 + 1]) / 2;
        }

        // 高周波成分（差分）
        for (int i = 0; i < half; i++)
        {
            temp[half + i] = (data[start + i * 2] - data[start + i * 2 + 1]) / 2;
        }

        Array.Copy(temp, 0, data, start, length);
    }

    /// <summary>
    /// 中央値計算
    /// </summary>
    private float CalculateMedian(float[] data, int count)
    {
        var sorted = data.Take(count).OrderBy(x => x).ToArray();
        var mid = count / 2;

        return count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2
            : sorted[mid];
    }

    /// <summary>
    /// ウィンドウ抽出（unsafeポインタ最適化）
    /// Issue #195: GetPixel()を廃止、直接ポインタアクセス
    /// Note: LockBitsはFormat24bppRgbへの自動変換を行う（元フォーマットが異なる場合は変換コストが発生）
    /// </summary>
    private float[] ExtractWindow(Bitmap bitmap, int x, int y, int size)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var window = new float[size * size];
        var width = bitmap.Width;
        var height = bitmap.Height;

        if (width <= 0 || height <= 0 || size <= 0)
        {
            return window;
        }

        // LockBitsはFormat24bppRgbを指定すると、元のフォーマットから自動変換する
        var lockData = bitmap.LockBits(
            new GdiRectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            GdiPixelFormat.Format24bppRgb);

        try
        {
            var stride = lockData.Stride;
            var index = 0;

            unsafe
            {
                byte* ptr = (byte*)lockData.Scan0;

                for (int wy = 0; wy < size; wy++)
                {
                    var py = Math.Min(y + wy, height - 1);
                    byte* row = ptr + (py * stride);

                    for (int wx = 0; wx < size; wx++)
                    {
                        var px = Math.Min(x + wx, width - 1);
                        var pixelOffset = px * 3;

                        // BGR順序 (GdiPixelFormat.Format24bppRgb)
                        var b = row[pixelOffset];
                        var g = row[pixelOffset + 1];
                        var r = row[pixelOffset + 2];
                        window[index++] = r * 0.299f + g * 0.587f + b * 0.114f;
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(lockData);
        }

        return window;
    }

    /// <summary>
    /// ウィンドウSSIM計算
    /// </summary>
    private double CalculateWindowSSIM(float[] window1, float[] window2)
    {
        var n = window1.Length;

        // 平均計算
        var mean1 = window1.Average();
        var mean2 = window2.Average();

        // 分散・共分散計算
        var variance1 = window1.Select(x => (x - mean1) * (x - mean1)).Average();
        var variance2 = window2.Select(x => (x - mean2) * (x - mean2)).Average();
        var covariance = window1.Zip(window2, (x1, x2) => (x1 - mean1) * (x2 - mean2)).Average();

        // SSIM計算
        var numerator = (2 * mean1 * mean2 + C1) * (2 * covariance + C2);
        var denominator = (mean1 * mean1 + mean2 * mean2 + C1) * (variance1 + variance2 + C2);

        return denominator > 0 ? numerator / denominator : 0.0;
    }

    /// <summary>
    /// Wavelet類似度調整
    /// </summary>
    private float AdjustWaveletSimilarity(float similarity)
    {
        // Waveletハッシュは周波数特性があるため、微調整
        return similarity > 0.5f ? (similarity - 0.5f) * 1.2f + 0.5f : similarity * 0.8f;
    }

    /// <summary>
    /// セットビット数カウント
    /// [Gemini Review] BitOperations.PopCount()でHARDWARE命令（POPCNT）を活用
    /// </summary>
    private static int CountSetBits(byte value)
    {
        return BitOperations.PopCount(value);
    }

    #endregion
}
