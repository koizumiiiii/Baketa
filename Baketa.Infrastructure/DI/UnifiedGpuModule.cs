using Baketa.Core.Abstractions.DI;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.DI;
using Baketa.Infrastructure.OCR.GPU;
using Baketa.Infrastructure.OCR.GPU.Providers;
using Baketa.Infrastructure.OCR.Benchmarking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Baketa.Infrastructure.DI;

/// <summary>
/// 統合GPU最適化システム DIモジュール
/// Phase 4: OpenVINO/DirectML/統合GPU最適化/パフォーマンス測定
/// </summary>
public sealed class UnifiedGpuModule : ServiceModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        // 設定登録（ServiceProviderからIConfigurationを取得）
        RegisterSettings(services);
        
        // Execution Provider Factories登録
        RegisterExecutionProviderFactories(services);
        
        // 統合GPU最適化システム
        RegisterUnifiedGpuOptimizer(services);
        
        // パフォーマンス測定システム
        RegisterBenchmarkingSystem(services);

        // 統合システム初期化
        RegisterSystemInitialization(services);
    }

    private static void RegisterSettings(IServiceCollection services)
    {
        // 設定をファクトリーパターンで登録
        services.AddSingleton<OpenVINOSettings>(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return configuration.GetSection("OpenVINO").Get<OpenVINOSettings>() ?? new OpenVINOSettings();
        });

        services.AddSingleton<DirectMLSettings>(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return configuration.GetSection("DirectML").Get<DirectMLSettings>() ?? new DirectMLSettings();
        });

        services.AddSingleton<UnifiedGpuSettings>(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return configuration.GetSection("UnifiedGpu").Get<UnifiedGpuSettings>() ?? new UnifiedGpuSettings();
        });

        services.AddSingleton<BenchmarkSettings>(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return configuration.GetSection("GpuBenchmark").Get<BenchmarkSettings>() ?? new BenchmarkSettings();
        });
    }

    private static void RegisterExecutionProviderFactories(IServiceCollection services)
    {
        // CPU Provider Factory（フォールバック保証）
        services.AddSingleton<IExecutionProviderFactory, CpuExecutionProviderFactory>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<CpuExecutionProviderFactory>>();
            return new CpuExecutionProviderFactory(logger);
        });

        // OpenVINO Provider Factory
        services.AddSingleton<IExecutionProviderFactory, OpenVINOExecutionProviderFactory>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<OpenVINOExecutionProviderFactory>>();
            var settings = serviceProvider.GetRequiredService<OpenVINOSettings>();
            return new OpenVINOExecutionProviderFactory(logger, settings);
        });

        // DirectML Provider Factory
        services.AddSingleton<IExecutionProviderFactory, DirectMLExecutionProviderFactory>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<DirectMLExecutionProviderFactory>>();
            var settings = serviceProvider.GetRequiredService<DirectMLSettings>();
            return new DirectMLExecutionProviderFactory(logger, settings);
        });

        // CUDA Provider Factory（既存のCUDA実装があれば統合）
        // services.AddSingleton<IExecutionProviderFactory, CudaExecutionProviderFactory>();

        // TensorRT Provider Factory（将来実装）
        // services.AddSingleton<IExecutionProviderFactory, TensorRTExecutionProviderFactory>();
    }

    private static void RegisterUnifiedGpuOptimizer(IServiceCollection services)
    {
        services.AddSingleton<IUnifiedGpuOptimizer, UnifiedGpuOptimizer>(serviceProvider =>
        {
            var providerFactories = serviceProvider.GetServices<IExecutionProviderFactory>();
            var environmentDetector = serviceProvider.GetRequiredService<IGpuEnvironmentDetector>();
            var logger = serviceProvider.GetRequiredService<ILogger<UnifiedGpuOptimizer>>();
            
            return new UnifiedGpuOptimizer(providerFactories, environmentDetector, logger);
        });
    }

    private static void RegisterBenchmarkingSystem(IServiceCollection services)
    {
        services.AddSingleton<IUnifiedGpuBenchmarkRunner, UnifiedGpuBenchmarkRunner>(serviceProvider =>
        {
            var gpuOptimizer = serviceProvider.GetRequiredService<IUnifiedGpuOptimizer>();
            var environmentDetector = serviceProvider.GetRequiredService<IGpuEnvironmentDetector>();
            var logger = serviceProvider.GetRequiredService<ILogger<UnifiedGpuBenchmarkRunner>>();
            
            return new UnifiedGpuBenchmarkRunner(gpuOptimizer, environmentDetector, logger);
        });

        // ベンチマーク結果レポーター
        services.AddSingleton<IGpuBenchmarkReporter, GpuBenchmarkReporter>();
    }

    private static void RegisterSystemInitialization(IServiceCollection services)
    {
        // 統合GPUシステム初期化サービス
        services.AddSingleton<UnifiedGpuInitializer>(serviceProvider =>
        {
            var optimizer = serviceProvider.GetRequiredService<IUnifiedGpuOptimizer>();
            var benchmarkRunner = serviceProvider.GetRequiredService<IUnifiedGpuBenchmarkRunner>();
            var settings = serviceProvider.GetRequiredService<UnifiedGpuSettings>();
            var logger = serviceProvider.GetRequiredService<ILogger<UnifiedGpuInitializer>>();
            
            return new UnifiedGpuInitializer(optimizer, benchmarkRunner, settings, logger);
        });
    }

}

/// <summary>
/// 統合GPU最適化設定
/// </summary>
public sealed record UnifiedGpuSettings
{
    public bool Enabled { get; init; } = true;
    public bool EnableAutoSelection { get; init; } = true;
    public bool EnableFallback { get; init; } = true;
    public bool EnableBenchmarkOnStartup { get; init; } = false;
    public int ProviderCacheExpirationMinutes { get; init; } = 60;
    public bool EnableDetailedLogging { get; init; } = false;
    public Dictionary<string, int> ProviderPriorities { get; init; } = new();
}

/// <summary>
/// 統合GPUシステム初期化サービス
/// </summary>
public sealed class UnifiedGpuInitializer(
    IUnifiedGpuOptimizer optimizer,
    IUnifiedGpuBenchmarkRunner benchmarkRunner,
    UnifiedGpuSettings settings,
    ILogger<UnifiedGpuInitializer> logger)
{
    private readonly IUnifiedGpuOptimizer _optimizer = optimizer ?? throw new ArgumentNullException(nameof(optimizer));
    private readonly IUnifiedGpuBenchmarkRunner _benchmarkRunner = benchmarkRunner ?? throw new ArgumentNullException(nameof(benchmarkRunner));
    private readonly UnifiedGpuSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ILogger<UnifiedGpuInitializer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Unified GPU optimization is disabled");
            return;
        }

        _logger.LogInformation("🚀 Initializing Unified GPU Optimization System");

        // 最適プロバイダー選択
        var optimalProvider = await _optimizer.SelectOptimalProviderAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("🎯 Selected optimal provider: {Provider}", optimalProvider.Type);

        // 起動時ベンチマーク実行（設定有効時）
        if (_settings.EnableBenchmarkOnStartup)
        {
            _logger.LogInformation("📊 Running startup benchmark");
            await RunStartupBenchmarkAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("✅ Unified GPU System initialization completed");
    }

    private async Task RunStartupBenchmarkAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 簡易ベンチマーク実行
            var testImages = CreateMockTestImages(5); // 5枚の簡易テスト画像
            var benchmarkSettings = new BenchmarkSettings
            {
                EnableWarmup = true,
                MonitorMemoryUsage = false,
                EnableParallelExecution = false
            };

            var result = await _benchmarkRunner.RunComprehensiveBenchmarkAsync(
                testImages, benchmarkSettings, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("📈 Startup benchmark completed. Optimal: {Provider} ({Time}ms avg)",
                result.OptimalProvider, 
                result.ProviderResults.FirstOrDefault(r => r.ProviderType == result.OptimalProvider)?.AverageInferenceTimeMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Startup benchmark failed");
        }
    }

    private static IReadOnlyList<IImage> CreateMockTestImages(int count)
    {
        // モック画像作成（実際の実装では適切なテスト画像を使用）
        return Enumerable.Range(0, count)
            .Select(_ => new MockImage())
            .ToList();
    }
}

/// <summary>
/// モックImage（ベンチマーク用）
/// </summary>
internal sealed class MockImage : IImage
{
    public int Width => 800;
    public int Height => 600;
    public ImageFormat Format => ImageFormat.Rgb24;
    
    /// <summary>
    /// PixelFormat property for IImage extension
    /// </summary>
    public ImagePixelFormat PixelFormat => ImagePixelFormat.Rgb24;
    
    /// <summary>
    /// GetImageMemory method for IImage extension
    /// </summary>
    public ReadOnlyMemory<byte> GetImageMemory()
    {
        return new ReadOnlyMemory<byte>(new byte[Width * Height * 3]);
    }
    
    public async Task<byte[]> ToByteArrayAsync() => await Task.FromResult(new byte[Width * Height * 3]);
    public IImage Clone() => new MockImage();
    public async Task<IImage> ResizeAsync(int width, int height) => await Task.FromResult(new MockImage());
    public void Dispose() { }
}

/// <summary>
/// GPUベンチマークレポーター（インターフェース）
/// </summary>
public interface IGpuBenchmarkReporter
{
    Task GenerateReportAsync(UnifiedGpuBenchmarkReport report, string outputPath);
    Task<string> GenerateMarkdownReportAsync(UnifiedGpuBenchmarkReport report);
}

/// <summary>
/// GPUベンチマークレポーター実装
/// </summary>
public sealed class GpuBenchmarkReporter : IGpuBenchmarkReporter
{
    public async Task GenerateReportAsync(UnifiedGpuBenchmarkReport report, string outputPath)
    {
        var markdownReport = await GenerateMarkdownReportAsync(report).ConfigureAwait(false);
        await File.WriteAllTextAsync(outputPath, markdownReport).ConfigureAwait(false);
    }

    public async Task<string> GenerateMarkdownReportAsync(UnifiedGpuBenchmarkReport report)
    {
        var markdown = $@"# GPU Performance Benchmark Report

**Generated**: {report.Timestamp:yyyy-MM-dd HH:mm:ss} UTC
**Environment**: {report.Environment.GpuName} ({report.Environment.AvailableMemoryMB}MB)
**Test Images**: {report.TestImageCount}
**Total Execution Time**: {report.TotalExecutionTimeMs:F2}ms

## 🏆 Performance Results

| Provider | Status | Avg Time (ms) | Throughput (img/sec) | Speedup |
|----------|--------|---------------|---------------------|---------|
";
        
        foreach (var result in report.ProviderResults)
        {
            var status = result.IsSuccessful ? "✅" : "❌";
            var avgTime = result.IsSuccessful ? $"{result.AverageInferenceTimeMs:F2}" : "Failed";
            var throughput = result.IsSuccessful ? $"{result.ThroughputImagesPerSecond:F1}" : "-";
            var speedup = report.PerformanceGains.TryGetValue(result.ProviderType.ToString(), out var gain) ? $"{gain:F1}x" : "-";
            
            markdown += $"| {result.ProviderType} | {status} | {avgTime} | {throughput} | {speedup} |\n";
        }

        markdown += $@"

## 🎯 Optimal Provider

**{report.OptimalProvider}** selected as optimal provider for this environment.

## 📊 Detailed Metrics

";

        foreach (var result in report.ProviderResults.Where(r => r.IsSuccessful))
        {
            markdown += $@"
### {result.ProviderType}

- **Average**: {result.AverageInferenceTimeMs:F2}ms
- **Median**: {result.MedianInferenceTimeMs:F2}ms
- **Min/Max**: {result.MinInferenceTimeMs:F2}ms / {result.MaxInferenceTimeMs:F2}ms
- **P95/P99**: {result.P95InferenceTimeMs:F2}ms / {result.P99InferenceTimeMs:F2}ms
- **Std Dev**: {result.StandardDeviationMs:F2}ms
- **Peak Memory**: {result.PeakMemoryUsageMB}MB

";
        }

        return await Task.FromResult(markdown);
    }
}
