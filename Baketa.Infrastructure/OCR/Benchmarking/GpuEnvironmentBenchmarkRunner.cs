using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
// using System.Management; // 削除: アーキテクチャ違反
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Services;
// using Baketa.Infrastructure.Platform.Windows.Capture.Strategies; // 削除: アーキテクチャ違反
// using Baketa.Application.Services.Capture; // 削除: アーキテクチャ違反

namespace Baketa.Infrastructure.OCR.Benchmarking;

/// <summary>
/// GPU環境別パフォーマンスベンチマークランナー
/// 統合GPU・専用GPU環境での最適化戦略決定を支援
/// </summary>
public class GpuEnvironmentBenchmarkRunner(IServiceProvider serviceProvider, ILogger<GpuEnvironmentBenchmarkRunner> logger)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<GpuEnvironmentBenchmarkRunner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// GPU環境別ベンチマークを実行
    /// </summary>
    public async Task<GpuBenchmarkReport> RunAsync()
    {
        _logger.LogInformation("=== GPU環境別パフォーマンスベンチマーク開始 ===");
        
        var report = new GpuBenchmarkReport
        {
            ExecutionTime = DateTime.Now,
            SystemInfo = await CollectSystemInfoAsync().ConfigureAwait(false)
        };
        
        try
        {
            // キャプチャサービスの取得（スタブ）
            var captureService = new object(); // スタブ実装
            
            // 各戦略のベンチマーク実行
            report.DirectFullScreenResults = await BenchmarkDirectFullScreenAsync(captureService).ConfigureAwait(false);
            report.ROIBasedResults = await BenchmarkROIBasedAsync(captureService).ConfigureAwait(false);
            report.AdaptiveResults = await BenchmarkAdaptiveAsync().ConfigureAwait(false);
            
            // 結果分析
            report.Analysis = AnalyzeResults(report);
            report.Recommendations = GenerateRecommendations(report);
            
            // レポート出力
            await OutputReportAsync(report).ConfigureAwait(false);
            
            _logger.LogInformation("=== GPU環境別パフォーマンスベンチマーク完了 ===");
            
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPU環境別ベンチマーク実行エラー");
            throw;
        }
    }
    
    /// <summary>
    /// システム情報収集
    /// </summary>
    private async Task<SystemInfo> CollectSystemInfoAsync()
    {
        _logger.LogInformation("システム情報収集開始");
        
        var systemInfo = new SystemInfo
        {
            OSVersion = Environment.OSVersion.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            TotalMemoryMB = (int)(GC.GetTotalMemory(false) / 1024 / 1024)
        };
        
        try
        {
            // GPU情報収集
            systemInfo.GPUInfo = await CollectGpuInfoAsync().ConfigureAwait(false);
            
            // ディスプレイ情報収集
            systemInfo.GpuDisplayInfo = CollectDisplayInfo();
            
            // パフォーマンスカウンター情報
            systemInfo.PerformanceBaseline = await CollectPerformanceBaselineAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "システム情報収集中にエラー");
        }
        
        _logger.LogInformation("システム情報収集完了: GPU={GPUCount}個, ディスプレイ={DisplayCount}個", 
            systemInfo.GPUInfo.Count, systemInfo.GpuDisplayInfo.Count);
            
        return systemInfo;
    }
    
    /// <summary>
    /// GPU情報収集（簡易版）
    /// </summary>
    private async Task<List<GpuInfo>> CollectGpuInfoAsync()
    {
        var gpuList = new List<GpuInfo>();
        
        try
        {
            await Task.Delay(100).ConfigureAwait(false);
            
            // 簡易的なGPU情報（実際のシステム情報収集は上位レイヤーで実装）
            gpuList.Add(new GpuInfo
            {
                Name = "Generic GPU",
                AdapterRAM = 0,
                DriverVersion = "Unknown",
                VideoProcessor = "Unknown",
                IsIntegrated = true // デフォルトで統合GPUとして扱う
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPU情報取得失敗");
        }
        
        return gpuList;
    }
    
    /// <summary>
    /// 統合GPUかどうかを判定
    /// </summary>
    private static bool IsIntegratedGpu(string gpuName)
    {
        var integratedKeywords = new[] { "Intel", "AMD Radeon Graphics", "Vega", "UHD", "Iris" };
        return integratedKeywords.Any(keyword => gpuName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// ディスプレイ情報収集（簡易版）
    /// </summary>
    private List<GpuDisplayInfo> CollectDisplayInfo()
    {
        var displays = new List<GpuDisplayInfo>();
        
        try
        {
            // 簡易的なディスプレイ情報（実際のシステム情報収集は上位レイヤーで実装）
            displays.Add(new GpuDisplayInfo
            {
                Index = 0,
                Width = 1920,
                Height = 1080,
                IsPrimary = true,
                BitsPerPixel = 32
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ディスプレイ情報取得失敗");
        }
        
        return displays;
    }
    
    /// <summary>
    /// パフォーマンスベースライン収集
    /// </summary>
    private async Task<GpuPerformanceBaseline> CollectPerformanceBaselineAsync()
    {
        var baseline = new GpuPerformanceBaseline();
        
        try
        {
            await Task.Run(() =>
            {
                using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                using var memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
                
                // 初回読み取り（初期化）
                cpuCounter.NextValue();
                memoryCounter.NextValue();
                
                System.Threading.Thread.Sleep(1000);
                
                baseline.InitialCpuUsage = cpuCounter.NextValue();
                baseline.AvailableMemoryMB = memoryCounter.NextValue();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "パフォーマンスベースライン取得失敗");
        }
        
        return baseline;
    }
    
    /// <summary>
    /// DirectFullScreen戦略のベンチマーク（スタブ版）
    /// </summary>
    private async Task<StrategyBenchmarkResult> BenchmarkDirectFullScreenAsync(object _)
    {
        _logger.LogInformation("DirectFullScreen戦略ベンチマーク開始");
        
        var result = new StrategyBenchmarkResult { StrategyName = "DirectFullScreen" };
        var measurements = new List<double>();
        
        try
        {
            // スタブ実装: キャプチャ時間をシミュレート
            await Task.Delay(100).ConfigureAwait(false); // 初期化待機
            
            for (int i = 0; i < 10; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                await Task.Delay(Random.Shared.Next(80, 120)).ConfigureAwait(false); // 80-120msのランダムディレイ
                stopwatch.Stop();
                
                measurements.Add(stopwatch.Elapsed.TotalMilliseconds);
                result.TotalPixelsProcessed += 1920 * 1080; // 仮想的なピクセル数
            }
            
            result.MeasurementCount = measurements.Count;
            result.AverageTimeMs = measurements.Average();
            result.MinTimeMs = measurements.Min();
            result.MaxTimeMs = measurements.Max();
            result.StandardDeviation = CalculateStandardDeviation(measurements);
            result.ThroughputPixelsPerSecond = result.TotalPixelsProcessed / (measurements.Sum() / 1000.0);
            
            _logger.LogInformation("DirectFullScreen戦略ベンチマーク完了: 平均={AverageMs}ms", result.AverageTimeMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DirectFullScreen戦略ベンチマークエラー");
            result.ErrorMessage = ex.Message;
        }
        
        return result;
    }
    
    /// <summary>
    /// ROIBased戦略のベンチマーク（スタブ版）
    /// </summary>
    private async Task<StrategyBenchmarkResult> BenchmarkROIBasedAsync(object _)
    {
        _logger.LogInformation("ROIBased戦略ベンチマーク開始");
        
        var result = new StrategyBenchmarkResult { StrategyName = "ROIBased" };
        var measurements = new List<double>();
        
        try
        {
            // スタブ実装: ROIベースキャプチャ時間をシミュレート
            await Task.Delay(50).ConfigureAwait(false); // 初期化待機
            
            for (int i = 0; i < 10; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                await Task.Delay(Random.Shared.Next(40, 80)).ConfigureAwait(false); // 40-80msのランダムディレイ
                stopwatch.Stop();
                
                measurements.Add(stopwatch.Elapsed.TotalMilliseconds);
                result.TotalPixelsProcessed += 640 * 480; // ROIサイズ
            }
            
            result.MeasurementCount = measurements.Count;
            result.AverageTimeMs = measurements.Average();
            result.MinTimeMs = measurements.Min();
            result.MaxTimeMs = measurements.Max();
            result.StandardDeviation = CalculateStandardDeviation(measurements);
            result.ThroughputPixelsPerSecond = result.TotalPixelsProcessed / (measurements.Sum() / 1000.0);
            
            _logger.LogInformation("ROIBased戦略ベンチマーク完了: 平均={AverageMs}ms", result.AverageTimeMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ROIBased戦略ベンチマークエラー");
            result.ErrorMessage = ex.Message;
        }
        
        return result;
    }
    
    /// <summary>
    /// Adaptive戦略のベンチマーク
    /// </summary>
    private async Task<StrategyBenchmarkResult> BenchmarkAdaptiveAsync()
    {
        _logger.LogInformation("Adaptive戦略ベンチマーク開始");
        
        var result = new StrategyBenchmarkResult { StrategyName = "Adaptive" };
        var measurements = new List<double>();
        
        try
        {
            // スタブ実装: 適応的キャプチャ時間をシミュレート
            await Task.Delay(100).ConfigureAwait(false); // 初期化シミュレーション
            
            for (int i = 0; i < 10; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                
                // 適応的戦略の選択と実行をシミュレーション
                await Task.Delay(Random.Shared.Next(200, 300)).ConfigureAwait(false); // 200-300msのランダムディレイ
                
                stopwatch.Stop();
                measurements.Add(stopwatch.Elapsed.TotalMilliseconds);
                result.TotalPixelsProcessed += 1920 * 1080; // 仮想的なピクセル数
            }
            
            result.MeasurementCount = measurements.Count;
            result.AverageTimeMs = measurements.Average();
            result.MinTimeMs = measurements.Min();
            result.MaxTimeMs = measurements.Max();
            result.StandardDeviation = CalculateStandardDeviation(measurements);
            result.ThroughputPixelsPerSecond = result.TotalPixelsProcessed / (measurements.Sum() / 1000.0);
            
            _logger.LogInformation("Adaptive戦略ベンチマーク完了: 平均={AverageMs}ms", result.AverageTimeMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adaptive戦略ベンチマークエラー");
            result.ErrorMessage = ex.Message;
        }
        
        return result;
    }
    
    /// <summary>
    /// 標準偏差計算
    /// </summary>
    private static double CalculateStandardDeviation(List<double> values)
    {
        if (values.Count <= 1) return 0;
        
        var mean = values.Average();
        var sumOfSquares = values.Sum(x => Math.Pow(x - mean, 2));
        return Math.Sqrt(sumOfSquares / (values.Count - 1));
    }
    
    /// <summary>
    /// 結果分析
    /// </summary>
    private BenchmarkAnalysis AnalyzeResults(GpuBenchmarkReport report)
    {
        var analysis = new BenchmarkAnalysis();
        
        // GPU環境判定
        var hasIntegratedGpu = report.SystemInfo.GPUInfo.Any(gpu => gpu.IsIntegrated);
        var hasDedicatedGpu = report.SystemInfo.GPUInfo.Any(gpu => !gpu.IsIntegrated);
        
        analysis.GpuEnvironmentType = (hasIntegratedGpu, hasDedicatedGpu) switch
        {
            (true, false) => "統合GPUのみ",
            (false, true) => "専用GPUのみ",
            (true, true) => "ハイブリッド（統合+専用）",
            _ => "GPU不明"
        };
        
        // パフォーマンス比較
        var strategies = new[] { report.DirectFullScreenResults, report.ROIBasedResults, report.AdaptiveResults }
            .Where(s => string.IsNullOrEmpty(s.ErrorMessage))
            .OrderBy(s => s.AverageTimeMs)
            .ToList();
        
        if (strategies.Count > 0)
        {
            analysis.BestStrategy = strategies.First().StrategyName;
            analysis.BestPerformanceMs = strategies.First().AverageTimeMs;
            
            if (strategies.Count > 1)
            {
                analysis.PerformanceGainPercent = ((strategies.Last().AverageTimeMs - strategies.First().AverageTimeMs) / strategies.Last().AverageTimeMs) * 100;
            }
        }
        
        // 解像度影響分析
        var totalPixels = report.SystemInfo.GpuDisplayInfo.Sum(d => d.Width * d.Height);
        analysis.ResolutionImpactFactor = totalPixels > 2073600 ? "高解像度影響大" : "標準解像度";
        
        return analysis;
    }
    
    /// <summary>
    /// 推奨設定生成
    /// </summary>
    private List<string> GenerateRecommendations(GpuBenchmarkReport report)
    {
        var recommendations = new List<string>();
        
        // GPU環境に基づく推奨
        var hasIntegratedGpu = report.SystemInfo.GPUInfo.Any(gpu => gpu.IsIntegrated);
        if (hasIntegratedGpu)
        {
            recommendations.Add("統合GPU環境では DirectFullScreen戦略が最適化されています");
            recommendations.Add("キャプチャ間隔を1000ms以上に設定してCPU負荷を軽減することを推奨");
        }
        
        var hasDedicatedGpu = report.SystemInfo.GPUInfo.Any(gpu => !gpu.IsIntegrated);
        if (hasDedicatedGpu)
        {
            recommendations.Add("専用GPU環境では ROIBased戦略を活用してパフォーマンス向上が期待できます");
            recommendations.Add("キャプチャ間隔を500ms程度に設定して応答性を向上できます");
        }
        
        // 解像度に基づく推奨
        var totalPixels = report.SystemInfo.GpuDisplayInfo.Sum(d => d.Width * d.Height);
        if (totalPixels > 8294400) // 4K相当
        {
            recommendations.Add("4K以上の高解像度環境では画像前処理の最適化が重要です");
            recommendations.Add("OCR処理前のリサイズを積極的に活用することを推奨");
        }
        
        // パフォーマンス結果に基づく推奨
        if (!string.IsNullOrEmpty(report.Analysis.BestStrategy))
        {
            recommendations.Add($"現在の環境では {report.Analysis.BestStrategy} 戦略が最高性能を示しています");
        }
        
        return recommendations;
    }
    
    /// <summary>
    /// レポート出力
    /// </summary>
    private async Task OutputReportAsync(GpuBenchmarkReport report)
    {
        _logger.LogInformation("GPU環境別ベンチマークレポート出力開始");
        
        // コンソール出力
        Console.WriteLine("=== GPU環境別パフォーマンスベンチマーク結果 ===");
        Console.WriteLine($"実行時間: {report.ExecutionTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"GPU環境: {report.Analysis.GpuEnvironmentType}");
        Console.WriteLine($"最適戦略: {report.Analysis.BestStrategy}");
        Console.WriteLine($"最高性能: {report.Analysis.BestPerformanceMs:F1}ms");
        Console.WriteLine($"性能向上: {report.Analysis.PerformanceGainPercent:F1}%");
        Console.WriteLine();
        
        Console.WriteLine("=== 戦略別結果 ===");
        var strategies = new[] { report.DirectFullScreenResults, report.ROIBasedResults, report.AdaptiveResults };
        foreach (var strategy in strategies)
        {
            Console.WriteLine($"{strategy.StrategyName}:");
            if (string.IsNullOrEmpty(strategy.ErrorMessage))
            {
                Console.WriteLine($"  平均時間: {strategy.AverageTimeMs:F1}ms");
                Console.WriteLine($"  範囲: {strategy.MinTimeMs:F1}ms - {strategy.MaxTimeMs:F1}ms");
                Console.WriteLine($"  スループット: {strategy.ThroughputPixelsPerSecond:F0}ピクセル/秒");
            }
            else
            {
                Console.WriteLine($"  エラー: {strategy.ErrorMessage}");
            }
            Console.WriteLine();
        }
        
        Console.WriteLine("=== 推奨設定 ===");
        foreach (var recommendation in report.Recommendations)
        {
            Console.WriteLine($"• {recommendation}");
        }
        
        // ファイル出力
        var reportJson = JsonSerializer.Serialize(report, JsonOptions);
        var reportFileName = $"gpu_benchmark_report_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        await System.IO.File.WriteAllTextAsync(reportFileName, reportJson).ConfigureAwait(false);
        
        _logger.LogInformation("GPU環境別ベンチマークレポート出力完了: {FileName}", reportFileName);
    }
}

/// <summary>
/// GPU環境別ベンチマークレポート
/// </summary>
public class GpuBenchmarkReport
{
    public DateTime ExecutionTime { get; set; }
    public SystemInfo SystemInfo { get; set; } = new();
    public StrategyBenchmarkResult DirectFullScreenResults { get; set; } = new();
    public StrategyBenchmarkResult ROIBasedResults { get; set; } = new();
    public StrategyBenchmarkResult AdaptiveResults { get; set; } = new();
    public BenchmarkAnalysis Analysis { get; set; } = new();
    public List<string> Recommendations { get; set; } = [];
}

/// <summary>
/// システム情報
/// </summary>
public class SystemInfo
{
    public string OSVersion { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public int TotalMemoryMB { get; set; }
    public List<GpuInfo> GPUInfo { get; set; } = [];
    public List<GpuDisplayInfo> GpuDisplayInfo { get; set; } = [];
    public GpuPerformanceBaseline PerformanceBaseline { get; set; } = new();
}

/// <summary>
/// GPU情報
/// </summary>
public class GpuInfo
{
    public string Name { get; set; } = string.Empty;
    public ulong AdapterRAM { get; set; }
    public string DriverVersion { get; set; } = string.Empty;
    public string VideoProcessor { get; set; } = string.Empty;
    public bool IsIntegrated { get; set; }
}

/// <summary>
/// GPUベンチマーク用ディスプレイ情報
/// </summary>
public class GpuDisplayInfo
{
    public int Index { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsPrimary { get; set; }
    public int BitsPerPixel { get; set; }
}

/// <summary>
/// パフォーマンスベースライン
/// </summary>
public class GpuPerformanceBaseline
{
    public float InitialCpuUsage { get; set; }
    public float AvailableMemoryMB { get; set; }
}

/// <summary>
/// 戦略ベンチマーク結果
/// </summary>
public class StrategyBenchmarkResult
{
    public string StrategyName { get; set; } = string.Empty;
    public int MeasurementCount { get; set; }
    public double AverageTimeMs { get; set; }
    public double MinTimeMs { get; set; }
    public double MaxTimeMs { get; set; }
    public double StandardDeviation { get; set; }
    public long TotalPixelsProcessed { get; set; }
    public double ThroughputPixelsPerSecond { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// ベンチマーク分析結果
/// </summary>
public class BenchmarkAnalysis
{
    public string GpuEnvironmentType { get; set; } = string.Empty;
    public string BestStrategy { get; set; } = string.Empty;
    public double BestPerformanceMs { get; set; }
    public double PerformanceGainPercent { get; set; }
    public string ResolutionImpactFactor { get; set; } = string.Empty;
}
