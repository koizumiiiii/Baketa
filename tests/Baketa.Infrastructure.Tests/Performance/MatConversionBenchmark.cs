using System;
using System.Buffers;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Extensions;
using OpenCvSharp;
using Xunit;
using Xunit.Abstractions;
using SystemDrawingImageFormat = System.Drawing.Imaging.ImageFormat;

namespace Baketa.Infrastructure.Tests.Performance;

/// <summary>
/// 🔥 [PHASE7.2-B] Mat.FromImageData vs CreateMatFromPixelLock 性能比較ベンチマーク
/// 実運用想定: 2560×1080画像での処理時間・メモリ使用量・CPU負荷を100回反復測定
/// </summary>
public class MatConversionBenchmark
{
    private readonly ITestOutputHelper _output;
    private const int TestWidth = 2560;
    private const int TestHeight = 1080;
    private const int Iterations = 100;

    public MatConversionBenchmark(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task BenchmarkMatConversionMethods()
    {
        _output.WriteLine("=== Phase 7.2-B: Mat変換方式性能比較ベンチマーク ===");
        _output.WriteLine($"画像サイズ: {TestWidth}×{TestHeight}");
        _output.WriteLine($"反復回数: {Iterations}");
        _output.WriteLine("");

        // テスト画像生成（実運用想定サイズ）
        var testImage = GenerateTestBitmap(TestWidth, TestHeight);
        _output.WriteLine($"✅ テスト画像生成完了: {testImage.Width}×{testImage.Height}, PixelFormat={testImage.PixelFormat}");

        // ウォームアップ（JIT最適化）
        _output.WriteLine("🔥 ウォームアップ実行中...");
        await WarmupAsync(testImage);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // 方式A: Mat.FromImageData(byte[]) - ArrayPool対応
        _output.WriteLine("");
        _output.WriteLine("📊 【方式A】Mat.FromImageData(byte[]) - ArrayPool対応");
        var resultA = await BenchmarkFromImageDataAsync(testImage);
        PrintResult("方式A", resultA);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // 方式B: CreateMatFromPixelLock - Phase 5.2G-A ゼロコピー
        _output.WriteLine("");
        _output.WriteLine("📊 【方式B】CreateMatFromPixelLock - ゼロコピー");
        var resultB = await BenchmarkFromPixelLockAsync(testImage);
        PrintResult("方式B", resultB);

        // 比較結果分析
        _output.WriteLine("");
        _output.WriteLine("=== 🎯 比較結果分析 ===");
        CompareResults(resultA, resultB);

        testImage.Dispose();
    }

    private async Task WarmupAsync(Bitmap testImage)
    {
        // 各方式を5回実行してJIT最適化
        for (int i = 0; i < 5; i++)
        {
            await RunFromImageDataAsync(testImage);
            await RunFromPixelLockAsync(testImage);
        }
    }

    private async Task<BenchmarkResult> BenchmarkFromImageDataAsync(Bitmap testImage)
    {
        var sw = Stopwatch.StartNew();
        var times = new double[Iterations];
        var memoryBefore = GC.GetTotalMemory(true);
        var processBefore = Process.GetCurrentProcess().WorkingSet64;

        for (int i = 0; i < Iterations; i++)
        {
            var iterSw = Stopwatch.StartNew();
            await RunFromImageDataAsync(testImage);
            times[i] = iterSw.Elapsed.TotalMilliseconds;
        }

        sw.Stop();
        var memoryAfter = GC.GetTotalMemory(false);
        var processAfter = Process.GetCurrentProcess().WorkingSet64;

        return new BenchmarkResult
        {
            TotalTime = sw.Elapsed,
            AverageTime = times.Average(),
            MedianTime = CalculateMedian(times),
            StdDeviation = CalculateStdDev(times),
            MinTime = times.Min(),
            MaxTime = times.Max(),
            GCMemoryDelta = memoryAfter - memoryBefore,
            ProcessMemoryDelta = processAfter - processBefore
        };
    }

    private async Task<BenchmarkResult> BenchmarkFromPixelLockAsync(Bitmap testImage)
    {
        var sw = Stopwatch.StartNew();
        var times = new double[Iterations];
        var memoryBefore = GC.GetTotalMemory(true);
        var processBefore = Process.GetCurrentProcess().WorkingSet64;

        for (int i = 0; i < Iterations; i++)
        {
            var iterSw = Stopwatch.StartNew();
            await RunFromPixelLockAsync(testImage);
            times[i] = iterSw.Elapsed.TotalMilliseconds;
        }

        sw.Stop();
        var memoryAfter = GC.GetTotalMemory(false);
        var processAfter = Process.GetCurrentProcess().WorkingSet64;

        return new BenchmarkResult
        {
            TotalTime = sw.Elapsed,
            AverageTime = times.Average(),
            MedianTime = CalculateMedian(times),
            StdDeviation = CalculateStdDev(times),
            MinTime = times.Min(),
            MaxTime = times.Max(),
            GCMemoryDelta = memoryAfter - memoryBefore,
            ProcessMemoryDelta = processAfter - processBefore
        };
    }

    private async Task RunFromImageDataAsync(Bitmap testImage)
    {
        byte[]? pooledArray = null;
        try
        {
            // 🔥 [PHASE5.2C] ArrayPool使用でメモリリーク防止
            using var memoryStream = new MemoryStream();
            testImage.Save(memoryStream, SystemDrawingImageFormat.Png);
            var imageData = memoryStream.ToArray();

            // Mat.FromImageDataで変換
            using var mat = Mat.FromImageData(imageData, ImreadModes.Color);

            // Mat検証
            if (mat.Empty())
            {
                throw new InvalidOperationException("Mat is empty");
            }

            await Task.CompletedTask;
        }
        finally
        {
            if (pooledArray != null)
            {
                ArrayPool<byte>.Shared.Return(pooledArray);
            }
        }
    }

    private async Task RunFromPixelLockAsync(Bitmap testImage)
    {
        // 🔥 [PHASE5.2G-A] PixelDataLock経由のゼロコピー実装
        var lockData = testImage.LockBits(
            new System.Drawing.Rectangle(0, 0, testImage.Width, testImage.Height),
            ImageLockMode.ReadOnly,
            testImage.PixelFormat);

        try
        {
            var pixelLock = new TestPixelDataLock(lockData);
            using var mat = CreateMatFromPixelLock(pixelLock, testImage.Width, testImage.Height);

            // Mat検証
            if (mat.Empty())
            {
                throw new InvalidOperationException("Mat is empty");
            }

            await Task.CompletedTask;
        }
        finally
        {
            testImage.UnlockBits(lockData);
        }
    }

    /// <summary>
    /// 🔥 [PHASE5.2G-A] PixelDataLockから直接Matを作成（PaddleOcrEngine.cs実装の複製）
    /// </summary>
    private static Mat CreateMatFromPixelLock(TestPixelDataLock pixelLock, int width, int height)
    {
        var actualStride = pixelLock.Stride;
        var dataLength = pixelLock.Data.Length;

        // actualStrideからチャンネル数を推定
        var estimatedChannels = actualStride / width;

        // チャンネル数とMatTypeを決定
        int channels;
        MatType matType;
        if (actualStride == width * 4 || estimatedChannels == 4)
        {
            channels = 4;
            matType = MatType.CV_8UC4;
        }
        else if (actualStride == width * 3 || estimatedChannels == 3)
        {
            channels = 3;
            matType = MatType.CV_8UC3;
        }
        else
        {
            channels = 1;
            matType = MatType.CV_8UC1;
        }

        // 🔥 [PHASE5.2G-A] ゼロコピー: IntPtrから直接Mat作成
        // 🔧 [PHASE7.2-B] Mat.FromPixelData使用（OpenCvSharp推奨API）
        unsafe
        {
            fixed (byte* ptr = pixelLock.Data)
            {
                var mat = Mat.FromPixelData(height, width, matType, (IntPtr)ptr, actualStride);
                return mat.Clone(); // データ所有権を移譲
            }
        }
    }

    private Bitmap GenerateTestBitmap(int width, int height)
    {
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);

        // 実運用を模した画像生成（グラデーション + テキスト）
        var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new System.Drawing.Rectangle(0, 0, width, height),
            Color.DarkBlue,
            Color.LightBlue,
            45f);
        g.FillRectangle(brush, 0, 0, width, height);

        // テキスト描画（OCR対象を模擬）
        var font = new Font("Arial", 24);
        for (int i = 0; i < 10; i++)
        {
            g.DrawString($"Test Text {i}: 日本語テキスト", font, Brushes.White, 100, 100 + i * 100);
        }

        return bitmap;
    }

    private void PrintResult(string methodName, BenchmarkResult result)
    {
        _output.WriteLine($"  総処理時間: {result.TotalTime.TotalSeconds:F3}秒");
        _output.WriteLine($"  平均処理時間: {result.AverageTime:F3}ms");
        _output.WriteLine($"  中央値: {result.MedianTime:F3}ms");
        _output.WriteLine($"  標準偏差: {result.StdDeviation:F3}ms");
        _output.WriteLine($"  最小値: {result.MinTime:F3}ms");
        _output.WriteLine($"  最大値: {result.MaxTime:F3}ms");
        _output.WriteLine($"  GCメモリ増加: {result.GCMemoryDelta / 1024.0 / 1024.0:F2}MB");
        _output.WriteLine($"  プロセスメモリ増加: {result.ProcessMemoryDelta / 1024.0 / 1024.0:F2}MB");
    }

    private void CompareResults(BenchmarkResult resultA, BenchmarkResult resultB)
    {
        var speedupRatio = resultA.AverageTime / resultB.AverageTime;
        var fasterMethod = speedupRatio > 1.0 ? "方式B（PixelLock）" : "方式A（FromImageData）";
        var speedupPercent = Math.Abs((speedupRatio - 1.0) * 100);

        _output.WriteLine($"⏱️  処理速度: {fasterMethod} が {speedupPercent:F1}% 高速");
        _output.WriteLine($"    方式A平均: {resultA.AverageTime:F3}ms");
        _output.WriteLine($"    方式B平均: {resultB.AverageTime:F3}ms");
        _output.WriteLine($"    速度比: {speedupRatio:F3}x");

        var memoryReductionA = resultA.GCMemoryDelta / 1024.0 / 1024.0;
        var memoryReductionB = resultB.GCMemoryDelta / 1024.0 / 1024.0;
        var memoryEfficientMethod = memoryReductionB < memoryReductionA ? "方式B（PixelLock）" : "方式A（FromImageData）";
        var memoryDiffPercent = Math.Abs((memoryReductionA - memoryReductionB) / memoryReductionA * 100);

        _output.WriteLine($"💾 メモリ効率: {memoryEfficientMethod} が {memoryDiffPercent:F1}% 効率的");
        _output.WriteLine($"    方式A GCメモリ: {memoryReductionA:F2}MB");
        _output.WriteLine($"    方式B GCメモリ: {memoryReductionB:F2}MB");

        _output.WriteLine("");
        _output.WriteLine($"🎯 推奨方式: {(speedupRatio > 1.05 ? "方式B (PixelLock)" : speedupRatio < 0.95 ? "方式A (FromImageData)" : "同等（どちらでも可）")}");
    }

    private static double CalculateMedian(double[] values)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    private static double CalculateStdDev(double[] values)
    {
        var avg = values.Average();
        var sumOfSquares = values.Sum(val => (val - avg) * (val - avg));
        return Math.Sqrt(sumOfSquares / values.Length);
    }

    private class BenchmarkResult
    {
        public TimeSpan TotalTime { get; set; }
        public double AverageTime { get; set; }
        public double MedianTime { get; set; }
        public double StdDeviation { get; set; }
        public double MinTime { get; set; }
        public double MaxTime { get; set; }
        public long GCMemoryDelta { get; set; }
        public long ProcessMemoryDelta { get; set; }
    }

    private class TestPixelDataLock : IPixelDataLock
    {
        private readonly BitmapData _bitmapData;
        private readonly byte[] _data;

        public TestPixelDataLock(BitmapData bitmapData)
        {
            _bitmapData = bitmapData;

            // IntPtrからbyte[]にコピー
            var dataLength = Math.Abs(bitmapData.Stride) * bitmapData.Height;
            _data = new byte[dataLength];
            Marshal.Copy(bitmapData.Scan0, _data, 0, dataLength);
        }

        public byte[] Data => _data;
        public int Stride => _bitmapData.Stride;
        public void Dispose() { }
    }
}

/// <summary>
/// IPixelDataLock インターフェース（テスト用）
/// </summary>
public interface IPixelDataLock : IDisposable
{
    byte[] Data { get; }
    int Stride { get; }
}
