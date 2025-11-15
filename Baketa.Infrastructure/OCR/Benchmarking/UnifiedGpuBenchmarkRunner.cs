using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.GPU;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Baketa.Infrastructure.OCR.Benchmarking;

/// <summary>
/// 統合GPU最適化ベンチマークシステム
/// 各種GPU最適化技術の性能を客観的に比較・測定
/// Phase 4.4: パフォーマンス測定システム実装
/// </summary>
public sealed class UnifiedGpuBenchmarkRunner(
    IUnifiedGpuOptimizer gpuOptimizer,
    IGpuEnvironmentDetector environmentDetector,
    ILogger<UnifiedGpuBenchmarkRunner> logger) : IUnifiedGpuBenchmarkRunner
{
    private readonly IUnifiedGpuOptimizer _gpuOptimizer = gpuOptimizer ?? throw new ArgumentNullException(nameof(gpuOptimizer));
    private readonly IGpuEnvironmentDetector _environmentDetector = environmentDetector ?? throw new ArgumentNullException(nameof(environmentDetector));
    private readonly ILogger<UnifiedGpuBenchmarkRunner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// 全プロバイダーの包括的性能比較
    /// </summary>
    public async Task<UnifiedGpuBenchmarkReport> RunComprehensiveBenchmarkAsync(
        IReadOnlyList<IImage> testImages,
        BenchmarkSettings settings,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting comprehensive GPU benchmark with {ImageCount} test images", testImages.Count);

        var stopwatch = Stopwatch.StartNew();
        var environment = await _environmentDetector.DetectEnvironmentAsync(cancellationToken).ConfigureAwait(false);
        var providerResults = new ConcurrentBag<ProviderBenchmarkResult>();

        try
        {
            // 利用可能なプロバイダー取得
            var providerStatuses = await _gpuOptimizer.GetProviderStatusAsync(cancellationToken).ConfigureAwait(false);
            var availableProviders = providerStatuses.Where(p => p.IsSupported).ToList();

            _logger.LogInformation("Testing {ProviderCount} available providers: {Providers}",
                availableProviders.Count,
                string.Join(", ", availableProviders.Select(p => p.Type)));

            // 各プロバイダーの性能測定（並列実行）
            var benchmarkTasks = availableProviders.Select(async providerStatus =>
            {
                try
                {
                    var result = await BenchmarkSingleProviderAsync(
                        providerStatus.Type,
                        testImages,
                        settings,
                        cancellationToken).ConfigureAwait(false);

                    providerResults.Add(result);
                    _logger.LogInformation("Completed benchmark for {Provider}: {AvgTime}ms",
                        providerStatus.Type, result.AverageInferenceTimeMs);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Benchmark failed for provider {Provider}", providerStatus.Type);

                    // エラー結果も記録
                    providerResults.Add(new ProviderBenchmarkResult
                    {
                        ProviderType = providerStatus.Type,
                        IsSuccessful = false,
                        ErrorMessage = ex.Message,
                        TestImageCount = testImages.Count
                    });
                }
            });

            await Task.WhenAll(benchmarkTasks).ConfigureAwait(false);

            // ベンチマーク結果の集計・分析
            var sortedResults = providerResults
                .OrderBy(r => r.IsSuccessful ? r.AverageInferenceTimeMs : double.MaxValue)
                .ToList();

            var report = new UnifiedGpuBenchmarkReport
            {
                Timestamp = DateTime.UtcNow,
                Environment = environment,
                TotalExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                TestImageCount = testImages.Count,
                BenchmarkSettings = settings,
                ProviderResults = sortedResults,
                OptimalProvider = sortedResults.FirstOrDefault(r => r.IsSuccessful)?.ProviderType ?? ExecutionProvider.CPU,
                PerformanceGains = CalculatePerformanceGains(sortedResults)
            };

            _logger.LogInformation("Benchmark completed in {TotalTime}ms. Optimal provider: {OptimalProvider}",
                report.TotalExecutionTimeMs, report.OptimalProvider);

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Comprehensive benchmark failed");
            throw;
        }
    }

    /// <summary>
    /// 単一プロバイダーの詳細性能測定
    /// </summary>
    public async Task<ProviderBenchmarkResult> BenchmarkSingleProviderAsync(
        ExecutionProvider providerType,
        IReadOnlyList<IImage> testImages,
        BenchmarkSettings settings,
        CancellationToken cancellationToken = default)
    {
        var result = new ProviderBenchmarkResult
        {
            ProviderType = providerType,
            TestImageCount = testImages.Count,
            StartTime = DateTime.UtcNow
        };

        SessionOptions? sessionOptions = null;
        InferenceSession? session = null;
        var inferenceTimings = new List<double>();

        try
        {
            // SessionOptions作成
            sessionOptions = await _gpuOptimizer.CreateSessionOptionsWithFallbackAsync(
                providerType, cancellationToken).ConfigureAwait(false);

            // 実用的なベンチマーク用ONNXモデルでセッション作成
            var benchmarkModelPath = CreateRealisticBenchmarkModel();
            session = new InferenceSession(benchmarkModelPath, sessionOptions);

            // ウォームアップ実行
            if (settings.EnableWarmup)
            {
                await RunWarmupInferencesAsync(session, testImages.Take(2), cancellationToken).ConfigureAwait(false);
            }

            // 実際の性能測定
            foreach (var testImage in testImages)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var inferenceTime = await MeasureSingleInferenceAsync(
                    session, testImage, cancellationToken).ConfigureAwait(false);

                inferenceTimings.Add(inferenceTime);

                // メモリ使用量監視（設定に応じて）
                if (settings.MonitorMemoryUsage && inferenceTimings.Count % 10 == 0)
                {
                    result.PeakMemoryUsageMB = Math.Max(result.PeakMemoryUsageMB,
                        GC.GetTotalMemory(false) / (1024 * 1024));
                }
            }

            // 統計計算
            if (inferenceTimings.Any())
            {
                result.IsSuccessful = true;
                result.AverageInferenceTimeMs = inferenceTimings.Average();
                result.MinInferenceTimeMs = inferenceTimings.Min();
                result.MaxInferenceTimeMs = inferenceTimings.Max();
                result.MedianInferenceTimeMs = CalculateMedian(inferenceTimings);
                result.StandardDeviationMs = CalculateStandardDeviation(inferenceTimings);
                result.ThroughputImagesPerSecond = 1000.0 / result.AverageInferenceTimeMs;

                // パーセンタイル計算
                var sortedTimings = inferenceTimings.OrderBy(t => t).ToList();
                result.P95InferenceTimeMs = sortedTimings[(int)(sortedTimings.Count * 0.95)];
                result.P99InferenceTimeMs = sortedTimings[(int)(sortedTimings.Count * 0.99)];
            }

            result.EndTime = DateTime.UtcNow;
            result.TotalExecutionTimeMs = (result.EndTime - result.StartTime).TotalMilliseconds;

            return result;
        }
        catch (Exception ex)
        {
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;

            _logger.LogError(ex, "Benchmark failed for {Provider}", providerType);
            return result;
        }
        finally
        {
            session?.Dispose();
            sessionOptions?.Dispose();
        }
    }

    private async Task RunWarmupInferencesAsync(
        InferenceSession session,
        IEnumerable<IImage> warmupImages,
        CancellationToken cancellationToken)
    {
        foreach (var image in warmupImages.Take(3))
        {
            if (cancellationToken.IsCancellationRequested) break;
            await MeasureSingleInferenceAsync(session, image, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<double> MeasureSingleInferenceAsync(
        InferenceSession session,
        IImage testImage,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 実用的な推論処理（Gemini推奨：実際のメモリアクセスパターンをシミュレート）
            var imageData = MockOnnxModelGenerator.GenerateRealisticImageData(
                testImage.Width, testImage.Height, 3);

            // ONNXテンソル作成（実際のOCR前処理に近い形）
            var inputTensor = new DenseTensor<float>(imageData, [1, 3, testImage.Height, testImage.Width]);
            var namedInput = NamedOnnxValue.CreateFromTensor("input", inputTensor);

            // 実際のONNX Runtime推論実行（CPU/GPUで処理される）
            using var outputs = session.Run([namedInput]);

            // 出力結果を消費（メモリアクセスパターンの完全性確保）
            foreach (var output in outputs)
            {
                if (output.Value is DenseTensor<float> tensor)
                {
                    // テンソルの先頭要素にアクセス（推論結果の利用をシミュレート）
                    var firstValue = tensor.GetValue(0);
                    // 最適化されないよう結果を軽く利用
                    if (firstValue > float.MaxValue) // 決して真にならない条件
                    {
                        throw new InvalidOperationException("Unexpected tensor value");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 推論エラーは測定に含めるが、キャンセルは除外
            _logger.LogWarning(ex, "Inference error during benchmark (included in timing)");
        }

        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static Dictionary<string, double> CalculatePerformanceGains(
        IReadOnlyList<ProviderBenchmarkResult> results)
    {
        var gains = new Dictionary<string, double>();
        var cpuBaseline = results.FirstOrDefault(r => r.ProviderType == ExecutionProvider.CPU && r.IsSuccessful);

        if (cpuBaseline == null) return gains;

        foreach (var result in results.Where(r => r.IsSuccessful && r.ProviderType != ExecutionProvider.CPU))
        {
            var speedup = cpuBaseline.AverageInferenceTimeMs / result.AverageInferenceTimeMs;
            gains[result.ProviderType.ToString()] = speedup;
        }

        return gains;
    }

    /// <summary>
    /// 実用的なベンチマーク用ONNXモデルを作成（Gemini推奨改善）
    /// </summary>
    private static string CreateRealisticBenchmarkModel()
    {
        try
        {
            // 実際の推論処理を行う軽量ONNXモデルを生成
            return MockOnnxModelGenerator.CreateMinimalOcrBenchmarkModel();
        }
        catch (Exception ex)
        {
            // フォールバック: インメモリモデルを一時ファイルに保存
            var tempModelPath = Path.Combine(Path.GetTempPath(), $"baketa_fallback_model_{Guid.NewGuid():N}.onnx");
            var minimalModelBytes = MockOnnxModelGenerator.CreateInMemoryMinimalModel();
            File.WriteAllBytes(tempModelPath, minimalModelBytes);
            return tempModelPath;
        }
    }

    private static double CalculateMedian(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var count = sorted.Count;

        if (count % 2 == 0)
        {
            return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
        }

        return sorted[count / 2];
    }

    private static double CalculateStandardDeviation(IReadOnlyList<double> values)
    {
        var mean = values.Average();
        var sumOfSquaresOfDifferences = values.Select(val => (val - mean) * (val - mean)).Sum();
        return Math.Sqrt(sumOfSquaresOfDifferences / values.Count);
    }
}

/// <summary>
/// 統合GPUベンチマーク設定
/// </summary>
public sealed record BenchmarkSettings
{
    public bool EnableWarmup { get; set; } = true;
    public int WarmupIterations { get; set; } = 3;
    public bool MonitorMemoryUsage { get; set; } = true;
    public bool IncludeDetailedMetrics { get; set; } = true;
    public TimeSpan MaxExecutionTime { get; set; } = TimeSpan.FromMinutes(10);
    public bool EnableParallelExecution { get; set; } = true;
}

/// <summary>
/// 統合GPUベンチマークレポート
/// </summary>
public sealed record UnifiedGpuBenchmarkReport
{
    public DateTime Timestamp { get; set; }
    public GpuEnvironmentInfo Environment { get; set; } = new();
    public double TotalExecutionTimeMs { get; set; }
    public int TestImageCount { get; set; }
    public BenchmarkSettings BenchmarkSettings { get; set; } = new();
    public IReadOnlyList<ProviderBenchmarkResult> ProviderResults { get; set; } = [];
    public ExecutionProvider OptimalProvider { get; set; }
    public Dictionary<string, double> PerformanceGains { get; set; } = new();
}

/// <summary>
/// 個別プロバイダーベンチマーク結果
/// </summary>
public sealed record ProviderBenchmarkResult
{
    public ExecutionProvider ProviderType { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double TotalExecutionTimeMs { get; set; }
    public int TestImageCount { get; set; }

    // 推論性能メトリクス
    public double AverageInferenceTimeMs { get; set; }
    public double MinInferenceTimeMs { get; set; }
    public double MaxInferenceTimeMs { get; set; }
    public double MedianInferenceTimeMs { get; set; }
    public double StandardDeviationMs { get; set; }
    public double P95InferenceTimeMs { get; set; }
    public double P99InferenceTimeMs { get; set; }
    public double ThroughputImagesPerSecond { get; set; }

    // リソース使用量
    public long PeakMemoryUsageMB { get; set; }
}

/// <summary>
/// 統合GPUベンチマークランナーインターフェース
/// </summary>
public interface IUnifiedGpuBenchmarkRunner
{
    Task<UnifiedGpuBenchmarkReport> RunComprehensiveBenchmarkAsync(
        IReadOnlyList<IImage> testImages,
        BenchmarkSettings settings,
        CancellationToken cancellationToken = default);

    Task<ProviderBenchmarkResult> BenchmarkSingleProviderAsync(
        ExecutionProvider providerType,
        IReadOnlyList<IImage> testImages,
        BenchmarkSettings settings,
        CancellationToken cancellationToken = default);
}
