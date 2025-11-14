using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
// using Baketa.Core.Abstractions.Services; // 削除: アーキテクチャ違反
// using Baketa.Core.Abstractions.OCR; // 削除: アーキテクチャ違反

namespace Baketa.Infrastructure.OCR.Benchmarking;

/// <summary>
/// 大画面環境でのパフォーマンステストランナー
/// 4K、ウルトラワイド、マルチディスプレイ環境での性能検証
/// </summary>
public class LargeScreenPerformanceRunner(IServiceProvider serviceProvider, ILogger<LargeScreenPerformanceRunner> logger)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<LargeScreenPerformanceRunner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // サポートする解像度種別
    private static readonly List<ResolutionProfile> SupportedResolutions = [
        new ResolutionProfile { Name = "Full HD", Width = 1920, Height = 1080, Category = "Standard" },
        new ResolutionProfile { Name = "WQHD", Width = 2560, Height = 1440, Category = "HighDef" },
        new ResolutionProfile { Name = "4K UHD", Width = 3840, Height = 2160, Category = "UltraHighDef" },
        new ResolutionProfile { Name = "5K", Width = 5120, Height = 2880, Category = "UltraHighDef" },
        new ResolutionProfile { Name = "8K UHD", Width = 7680, Height = 4320, Category = "UltraHighDef" },
        new ResolutionProfile { Name = "UltraWide QHD", Width = 3440, Height = 1440, Category = "UltraWide" },
        new ResolutionProfile { Name = "UltraWide 4K", Width = 3840, Height = 1600, Category = "UltraWide" },
        new ResolutionProfile { Name = "Super UltraWide", Width = 5120, Height = 1440, Category = "UltraWide" }
    ];

    /// <summary>
    /// 大画面パフォーマンステストを実行
    /// </summary>
    public async Task<LargeScreenPerformanceReport> RunAsync()
    {
        _logger.LogInformation("=== 大画面パフォーマンステスト開始 ===");

        var report = new LargeScreenPerformanceReport
        {
            ExecutionTime = DateTime.Now,
            DisplayConfiguration = await AnalyzeDisplayConfigurationAsync().ConfigureAwait(false)
        };

        try
        {
            // 現在のディスプレイ設定に基づいてテスト
            report.CurrentDisplayResults = await TestCurrentDisplayAsync().ConfigureAwait(false);

            // シミュレートされた解像度でのテスト
            report.SimulatedResolutionResults = await TestSimulatedResolutionsAsync().ConfigureAwait(false);

            // メモリスケーリングテスト
            report.MemoryScalingResults = await TestMemoryScalingAsync().ConfigureAwait(false);

            // 結果分析
            report.Analysis = AnalyzeResults(report);
            report.Recommendations = GenerateRecommendations(report);

            // レポート出力
            await OutputReportAsync(report).ConfigureAwait(false);

            _logger.LogInformation("=== 大画面パフォーマンステスト完了 ===");

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "大画面パフォーマンステスト実行エラー");
            throw;
        }
    }

    /// <summary>
    /// ディスプレイ設定分析
    /// </summary>
    private async Task<DisplayConfiguration> AnalyzeDisplayConfigurationAsync()
    {
        _logger.LogInformation("ディスプレイ設定分析開始");

        await Task.Delay(100).ConfigureAwait(false); // 初期化待機

        var config = new DisplayConfiguration();

        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;

            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                var displayInfo = new LargeScreenDisplayInfo
                {
                    Index = i,
                    Width = screen.Bounds.Width,
                    Height = screen.Bounds.Height,
                    IsPrimary = screen.Primary,
                    BitsPerPixel = screen.BitsPerPixel,
                    TotalPixels = screen.Bounds.Width * screen.Bounds.Height
                };

                // 解像度カテゴリ判定
                displayInfo.Category = ClassifyResolution(displayInfo.Width, displayInfo.Height);

                config.Displays.Add(displayInfo);
            }

            // 総合情報計算
            config.TotalDisplays = config.Displays.Count;
            config.TotalPixels = config.Displays.Sum(d => d.TotalPixels);
            config.MaxWidth = config.Displays.Max(d => d.Width);
            config.MaxHeight = config.Displays.Max(d => d.Height);
            config.HasHighDPI = config.Displays.Any(d => d.Category == "UltraHighDef");
            config.HasUltraWide = config.Displays.Any(d => d.Category == "UltraWide");

            _logger.LogInformation("ディスプレイ設定分析完了: {DisplayCount}台, 最大={MaxResolution}, 総ピクセル={TotalPixels:N0}",
                config.TotalDisplays, $"{config.MaxWidth}x{config.MaxHeight}", config.TotalPixels);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ディスプレイ設定取得エラー");
        }

        return config;
    }

    /// <summary>
    /// 解像度カテゴリ判定
    /// </summary>
    private string ClassifyResolution(int width, int height)
    {
        var totalPixels = width * height;
        var aspectRatio = (double)width / height;

        // アスペクト比でウルトラワイドを判定
        if (aspectRatio > 2.5)
            return "UltraWide";

        // ピクセル数で判定
        return totalPixels switch
        {
            <= 2073600 => "Standard",    // 1920x1080以下
            <= 3686400 => "HighDef",     // 2560x1440程度
            _ => "UltraHighDef"           // 4K以上
        };
    }

    /// <summary>
    /// 現在のディスプレイでのテスト
    /// </summary>
    private async Task<List<DisplayTestResult>> TestCurrentDisplayAsync()
    {
        _logger.LogInformation("現在のディスプレイテスト開始");

        var results = new List<DisplayTestResult>();
        // スタブ実装: サービスは使用しないためコメントアウト
        // var captureService = new object();
        // var ocrEngine = new object();

        var screens = System.Windows.Forms.Screen.AllScreens;

        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var result = new DisplayTestResult
            {
                DisplayIndex = i,
                Width = screen.Bounds.Width,
                Height = screen.Bounds.Height,
                Category = ClassifyResolution(screen.Bounds.Width, screen.Bounds.Height)
            };

            try
            {
                // スタブ実装: パフォーマンステストをシミュレート
                result.CaptureMetrics = await SimulateCapturePerformanceAsync(screen.Bounds).ConfigureAwait(false);
                result.OcrMetrics = await SimulateOcrPerformanceAsync(screen.Bounds).ConfigureAwait(false);
                result.MemoryMetrics = await MeasureMemoryUsageAsync(screen.Bounds).ConfigureAwait(false);

                _logger.LogInformation("ディスプレイ{Index} ({Resolution}) テスト完了: キャプチャ={CaptureMs}ms, OCR={OcrMs}ms",
                    i, $"{screen.Bounds.Width}x{screen.Bounds.Height}",
                    result.CaptureMetrics.AverageTimeMs, result.OcrMetrics?.AverageTimeMs ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ディスプレイ{Index}テストエラー", i);
                result.ErrorMessage = ex.Message;
            }

            results.Add(result);
        }

        _logger.LogInformation("現在のディスプレイテスト完了: {DisplayCount}台テスト", results.Count);
        return results;
    }

    /// <summary>
    /// シミュレートされた解像度でのテスト
    /// </summary>
    private async Task<List<SimulatedResolutionResult>> TestSimulatedResolutionsAsync()
    {
        _logger.LogInformation("シミュレート解像度テスト開始");

        var results = new List<SimulatedResolutionResult>();
        // スタブ実装: キャプチャサービスの代わりにシミュレーションを使用

        foreach (var resolution in SupportedResolutions)
        {
            var result = new SimulatedResolutionResult
            {
                ResolutionName = resolution.Name,
                Width = resolution.Width,
                Height = resolution.Height,
                Category = resolution.Category
            };

            try
            {
                // 指定解像度でのキャプチャシミュレーション
                var simulatedBounds = new Rectangle(0, 0, resolution.Width, resolution.Height);
                result.CaptureMetrics = await SimulateCapturePerformanceForResolutionAsync(simulatedBounds).ConfigureAwait(false);

                // メモリスケーリング理論値計算
                result.EstimatedMemoryMB = CalculateEstimatedMemoryUsage(resolution.Width, resolution.Height);

                // パフォーマンススコア計算
                result.PerformanceScore = CalculatePerformanceScore(result);

                _logger.LogInformation("解像度 {ResolutionName} テスト完了: スコア={Score}, メモリ={MemoryMB}MB",
                    resolution.Name, result.PerformanceScore, result.EstimatedMemoryMB);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解像度 {ResolutionName} テストエラー", resolution.Name);
                result.ErrorMessage = ex.Message;
            }

            results.Add(result);
        }

        _logger.LogInformation("シミュレート解像度テスト完了: {ResolutionCount}種類テスト", results.Count);
        return results;
    }

    /// <summary>
    /// メモリスケーリングテスト
    /// </summary>
    private async Task<MemoryScalingResult> TestMemoryScalingAsync()
    {
        _logger.LogInformation("メモリスケーリングテスト開始");

        await Task.Delay(50).ConfigureAwait(false); // 非同期メソッドのためのawait追加
        var result = new MemoryScalingResult();

        try
        {
            // 各解像度でのメモリ使用量理論値計算
            foreach (var resolution in SupportedResolutions)
            {
                var memoryData = new ResolutionMemoryData
                {
                    ResolutionName = resolution.Name,
                    Width = resolution.Width,
                    Height = resolution.Height,
                    EstimatedImageMemoryMB = CalculateImageMemoryUsage(resolution.Width, resolution.Height),
                    EstimatedOcrMemoryMB = CalculateOcrMemoryUsage(resolution.Width, resolution.Height),
                    EstimatedTotalMemoryMB = CalculateEstimatedMemoryUsage(resolution.Width, resolution.Height)
                };

                result.ResolutionMemoryData.Add(memoryData);
            }

            // メモリスケーリング率計算
            var baseResolution = result.ResolutionMemoryData.First(r => r.ResolutionName == "Full HD");

            foreach (var data in result.ResolutionMemoryData)
            {
                data.MemoryScalingFactor = data.EstimatedTotalMemoryMB / baseResolution.EstimatedTotalMemoryMB;
            }

            // システムメモリ制約分析
            result.SystemMemoryLimitGB = Environment.WorkingSet / (1024 * 1024 * 1024);
            result.RecommendedMaxResolution = DetermineRecommendedMaxResolution(result.ResolutionMemoryData, result.SystemMemoryLimitGB);

            _logger.LogInformation("メモリスケーリングテスト完了: 推奨最大解像度={MaxResolution}", result.RecommendedMaxResolution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メモリスケーリングテストエラー");
        }

        return result;
    }

    /// <summary>
    /// キャプチャパフォーマンス測定（スタブ版）
    /// </summary>
    private async Task<PerformanceMetrics> SimulateCapturePerformanceAsync(Rectangle bounds)
    {
        var measurements = new List<double>();

        // スタブ実装: 解像度に基づいたシミュレーション
        await Task.Delay(50).ConfigureAwait(false);

        for (int i = 0; i < 5; i++)
        {
            var stopwatch = Stopwatch.StartNew();

            // 解像度に比例した処理時間をシミュレート
            var pixelCount = bounds.Width * bounds.Height;
            var baseTime = 100.0; // 1920x1080のベースタイム
            var scalingFactor = (double)pixelCount / (1920 * 1080);
            var simulatedTime = (int)(baseTime * Math.Sqrt(scalingFactor));

            await Task.Delay(Random.Shared.Next(simulatedTime * 8 / 10, simulatedTime * 12 / 10)).ConfigureAwait(false);
            stopwatch.Stop();

            measurements.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        return new PerformanceMetrics
        {
            AverageTimeMs = measurements.Average(),
            MinTimeMs = measurements.Min(),
            MaxTimeMs = measurements.Max(),
            StandardDeviation = CalculateStandardDeviation(measurements),
            MeasurementCount = measurements.Count
        };
    }

    /// <summary>
    /// OCRパフォーマンス測定（スタブ版Ｉ
    /// </summary>
    private async Task<PerformanceMetrics?> SimulateOcrPerformanceAsync(Rectangle bounds)
    {
        try
        {
            var measurements = new List<double>();

            // スタブ実装: OCR処理時間をシミュレート
            await Task.Delay(100).ConfigureAwait(false);

            for (int i = 0; i < 3; i++)
            {
                var stopwatch = Stopwatch.StartNew();

                // 解像度に比例したOCR処理時間をシミュレート
                var pixelCount = bounds.Width * bounds.Height;
                var baseTime = 3000.0; // 1920x1080のOCRベースタイム
                var scalingFactor = (double)pixelCount / (1920 * 1080);
                var simulatedTime = (int)(baseTime * scalingFactor);

                await Task.Delay(Random.Shared.Next(simulatedTime * 8 / 10, simulatedTime * 12 / 10)).ConfigureAwait(false);
                stopwatch.Stop();

                measurements.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            return new PerformanceMetrics
            {
                AverageTimeMs = measurements.Average(),
                MinTimeMs = measurements.Min(),
                MaxTimeMs = measurements.Max(),
                StandardDeviation = CalculateStandardDeviation(measurements),
                MeasurementCount = measurements.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OCRパフォーマンス測定エラー");
            return null;
        }
    }

    /// <summary>
    /// メモリ使用量測定
    /// </summary>
    private async Task<MemoryMetrics> MeasureMemoryUsageAsync(Rectangle bounds)
    {
        await Task.Delay(100).ConfigureAwait(false); // GC安定化待機

        var beforeGC = GC.GetTotalMemory(false);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var afterGC = GC.GetTotalMemory(false);

        return new MemoryMetrics
        {
            BeforeGCBytes = beforeGC,
            AfterGCBytes = afterGC,
            EstimatedImageSizeBytes = bounds.Width * bounds.Height * 4, // BGRA
            WorkingSetBytes = Environment.WorkingSet
        };
    }

    /// <summary>
    /// キャプチャパフォーマンスシミュレーション（解像度用）
    /// </summary>
    private async Task<PerformanceMetrics> SimulateCapturePerformanceForResolutionAsync(Rectangle bounds)
    {
        // 解像度ベースの理論値計算
        await Task.Delay(10).ConfigureAwait(false); // シミュレーション待機

        var pixelCount = bounds.Width * bounds.Height;
        var baseTimeMs = 100.0; // 1920x1080のベースタイム
        var scalingFactor = (double)pixelCount / (1920 * 1080);

        // ピクセル数に比例して処理時間が増加すると仮定
        var estimatedTimeMs = baseTimeMs * Math.Sqrt(scalingFactor); // 平方根スケーリング

        return new PerformanceMetrics
        {
            AverageTimeMs = estimatedTimeMs,
            MinTimeMs = estimatedTimeMs * 0.9,
            MaxTimeMs = estimatedTimeMs * 1.2,
            StandardDeviation = estimatedTimeMs * 0.1,
            MeasurementCount = 1
        };
    }

    /// <summary>
    /// テスト用画像生成
    /// </summary>
    private System.Drawing.Bitmap GenerateTestImage(int width, int height)
    {
        var bitmap = new System.Drawing.Bitmap(width, height);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);

        graphics.Clear(System.Drawing.Color.White);

        // 解像度に応じたフォントサイズ調整
        var fontSize = Math.Max(12, Math.Min(48, width / 80));
        using var font = new System.Drawing.Font("Arial", fontSize);

        graphics.DrawString($"テスト用テキスト Test Text {width}x{height}",
            font, System.Drawing.Brushes.Black, new System.Drawing.PointF(50, 50));

        return bitmap;
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
    /// 推定メモリ使用量計算
    /// </summary>
    private double CalculateEstimatedMemoryUsage(int width, int height)
    {
        var imageMemory = CalculateImageMemoryUsage(width, height);
        var ocrMemory = CalculateOcrMemoryUsage(width, height);
        return imageMemory + ocrMemory;
    }

    /// <summary>
    /// 画像メモリ使用量計算
    /// </summary>
    private double CalculateImageMemoryUsage(int width, int height)
    {
        // BGRA (4バイト/ピクセル) + バッファ用
        var bytesPerPixel = 4;
        var bufferMultiplier = 2.0; // ダブルバッファ等
        return (width * height * bytesPerPixel * bufferMultiplier) / (1024 * 1024);
    }

    /// <summary>
    /// OCRメモリ使用量計算
    /// </summary>
    private double CalculateOcrMemoryUsage(int width, int height)
    {
        // OCR処理に必要なメモリ(内部バッファ、中間結果等)
        var baseOcrMemory = 50.0; // ベースメモリ (MB)
        var pixelScalingFactor = (double)(width * height) / (1920 * 1080);
        return baseOcrMemory * Math.Sqrt(pixelScalingFactor);
    }

    /// <summary>
    /// パフォーマンススコア計算
    /// </summary>
    private int CalculatePerformanceScore(SimulatedResolutionResult result)
    {
        // 100点満点でスコア計算
        var timeScore = Math.Max(0, 100 - (result.CaptureMetrics.AverageTimeMs / 10));
        var memoryScore = Math.Max(0, 100 - (result.EstimatedMemoryMB / 20));

        return (int)((timeScore + memoryScore) / 2);
    }

    /// <summary>
    /// 推奨最大解像度決定
    /// </summary>
    private string DetermineRecommendedMaxResolution(List<ResolutionMemoryData> memoryData, long systemMemoryGB)
    {
        var maxMemoryMB = systemMemoryGB * 1024 * 0.8; // システムメモリの80%を上限とする

        var suitableResolutions = memoryData
            .Where(r => r.EstimatedTotalMemoryMB <= maxMemoryMB)
            .OrderByDescending(r => r.Width * r.Height)
            .ToList();

        return suitableResolutions.FirstOrDefault()?.ResolutionName ?? "Full HD";
    }

    /// <summary>
    /// 結果分析
    /// </summary>
    private LargeScreenAnalysis AnalyzeResults(LargeScreenPerformanceReport report)
    {
        var analysis = new LargeScreenAnalysis();

        // 現在のディスプレイ分析
        if (report.CurrentDisplayResults.Count > 0)
        {
            analysis.PrimaryDisplayCategory = report.CurrentDisplayResults.First(r => r.DisplayIndex == 0)?.Category ?? "Unknown";
            analysis.AverageCaptureTimeMs = report.CurrentDisplayResults.Average(r => r.CaptureMetrics.AverageTimeMs);
            analysis.AverageOcrTimeMs = report.CurrentDisplayResults
                .Where(r => r.OcrMetrics != null)
                .Average(r => r.OcrMetrics!.AverageTimeMs);
        }

        // シミュレーション結果分析
        if (report.SimulatedResolutionResults.Count > 0)
        {
            var bestPerformance = report.SimulatedResolutionResults
                .Where(r => string.IsNullOrEmpty(r.ErrorMessage))
                .OrderByDescending(r => r.PerformanceScore)
                .FirstOrDefault();

            analysis.BestPerformanceResolution = bestPerformance?.ResolutionName ?? "Unknown";
            analysis.BestPerformanceScore = bestPerformance?.PerformanceScore ?? 0;
        }

        // メモリ制約分析
        analysis.MemoryConstrainedResolution = report.MemoryScalingResults.RecommendedMaxResolution;

        return analysis;
    }

    /// <summary>
    /// 推奨設定生成
    /// </summary>
    private List<string> GenerateRecommendations(LargeScreenPerformanceReport report)
    {
        var recommendations = new List<string>();

        // ディスプレイ設定に基づく推奨
        if (report.DisplayConfiguration.HasHighDPI)
        {
            recommendations.Add("高DPI環境ではキャプチャ間隔を長めに設定することを推奨");
            recommendations.Add("OCR処理前の画像リサイズを積極的に活用してパフォーマンスを向上");
        }

        if (report.DisplayConfiguration.HasUltraWide)
        {
            recommendations.Add("ウルトラワイド環境ではROI（関心領域）ベースのキャプチャが効果的");
        }

        if (report.DisplayConfiguration.TotalDisplays > 1)
        {
            recommendations.Add("マルチディスプレイ環境ではプライマリディスプレイのみをターゲットにすることを推奨");
        }

        // パフォーマンス結果に基づく推奨
        if (report.Analysis.AverageCaptureTimeMs > 300)
        {
            recommendations.Add("キャプチャ時間が長いため、解像度を下げるかキャプチャ間隔を延ばすことを推奨");
        }

        if (report.Analysis.AverageOcrTimeMs > 5000)
        {
            recommendations.Add("OCR処理時間が長いため、バッチ処理の最適化が必要");
        }

        // メモリ制約に基づく推奨
        recommendations.Add($"現在のシステムでは{report.MemoryScalingResults.RecommendedMaxResolution}が推奨最大解像度です");

        return recommendations;
    }

    /// <summary>
    /// レポート出力
    /// </summary>
    private async Task OutputReportAsync(LargeScreenPerformanceReport report)
    {
        _logger.LogInformation("大画面パフォーマンステストレポート出力開始");

        // コンソール出力
        Console.WriteLine("=== 大画面パフォーマンステスト結果 ===");
        Console.WriteLine($"実行時間: {report.ExecutionTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"ディスプレイ数: {report.DisplayConfiguration.TotalDisplays}");
        Console.WriteLine($"最大解像度: {report.DisplayConfiguration.MaxWidth}x{report.DisplayConfiguration.MaxHeight}");
        Console.WriteLine($"総ピクセル数: {report.DisplayConfiguration.TotalPixels:N0}");
        Console.WriteLine($"プライマリカテゴリ: {report.Analysis.PrimaryDisplayCategory}");
        Console.WriteLine();

        Console.WriteLine("=== パフォーマンス結果 ===");
        Console.WriteLine($"平均キャプチャ時間: {report.Analysis.AverageCaptureTimeMs:F1}ms");
        Console.WriteLine($"平均OCR時間: {report.Analysis.AverageOcrTimeMs:F1}ms");
        Console.WriteLine($"最高性能解像度: {report.Analysis.BestPerformanceResolution}");
        Console.WriteLine($"パフォーマンススコア: {report.Analysis.BestPerformanceScore}");
        Console.WriteLine($"メモリ制約解像度: {report.Analysis.MemoryConstrainedResolution}");
        Console.WriteLine();

        Console.WriteLine("=== 推奨設定 ===");
        foreach (var recommendation in report.Recommendations)
        {
            Console.WriteLine($"• {recommendation}");
        }

        // ファイル出力
        var reportJson = JsonSerializer.Serialize(report, JsonOptions);
        var reportFileName = $"large_screen_performance_report_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        await System.IO.File.WriteAllTextAsync(reportFileName, reportJson).ConfigureAwait(false);

        _logger.LogInformation("大画面パフォーマンステストレポート出力完了: {FileName}", reportFileName);
    }
}

// データクラス群

/// <summary>
/// 解像度プロファイル
/// </summary>
public class ResolutionProfile
{
    public string Name { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string Category { get; set; } = string.Empty;
}

/// <summary>
/// 大画面パフォーマンスレポート
/// </summary>
public class LargeScreenPerformanceReport
{
    public DateTime ExecutionTime { get; set; }
    public DisplayConfiguration DisplayConfiguration { get; set; } = new();
    public List<DisplayTestResult> CurrentDisplayResults { get; set; } = [];
    public List<SimulatedResolutionResult> SimulatedResolutionResults { get; set; } = [];
    public MemoryScalingResult MemoryScalingResults { get; set; } = new();
    public LargeScreenAnalysis Analysis { get; set; } = new();
    public List<string> Recommendations { get; set; } = [];
}

/// <summary>
/// ディスプレイ設定
/// </summary>
public class DisplayConfiguration
{
    public int TotalDisplays { get; set; }
    public long TotalPixels { get; set; }
    public int MaxWidth { get; set; }
    public int MaxHeight { get; set; }
    public bool HasHighDPI { get; set; }
    public bool HasUltraWide { get; set; }
    public List<LargeScreenDisplayInfo> Displays { get; set; } = [];
}

/// <summary>
/// 大画面テスト用ディスプレイ情報
/// </summary>
public class LargeScreenDisplayInfo
{
    public int Index { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsPrimary { get; set; }
    public int BitsPerPixel { get; set; }
    public long TotalPixels { get; set; }
    public string Category { get; set; } = string.Empty;
}

/// <summary>
/// ディスプレイテスト結果
/// </summary>
public class DisplayTestResult
{
    public int DisplayIndex { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Category { get; set; } = string.Empty;
    public PerformanceMetrics CaptureMetrics { get; set; } = new();
    public PerformanceMetrics? OcrMetrics { get; set; }
    public MemoryMetrics MemoryMetrics { get; set; } = new();
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// シミュレート解像度結果
/// </summary>
public class SimulatedResolutionResult
{
    public string ResolutionName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string Category { get; set; } = string.Empty;
    public PerformanceMetrics CaptureMetrics { get; set; } = new();
    public double EstimatedMemoryMB { get; set; }
    public int PerformanceScore { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// メモリスケーリング結果
/// </summary>
public class MemoryScalingResult
{
    public List<ResolutionMemoryData> ResolutionMemoryData { get; set; } = [];
    public long SystemMemoryLimitGB { get; set; }
    public string RecommendedMaxResolution { get; set; } = string.Empty;
}

/// <summary>
/// 解像度別メモリデータ
/// </summary>
public class ResolutionMemoryData
{
    public string ResolutionName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public double EstimatedImageMemoryMB { get; set; }
    public double EstimatedOcrMemoryMB { get; set; }
    public double EstimatedTotalMemoryMB { get; set; }
    public double MemoryScalingFactor { get; set; }
}

/// <summary>
/// パフォーマンスメトリクス
/// </summary>
public class PerformanceMetrics
{
    public double AverageTimeMs { get; set; }
    public double MinTimeMs { get; set; }
    public double MaxTimeMs { get; set; }
    public double StandardDeviation { get; set; }
    public int MeasurementCount { get; set; }
}

/// <summary>
/// メモリメトリクス
/// </summary>
public class MemoryMetrics
{
    public long BeforeGCBytes { get; set; }
    public long AfterGCBytes { get; set; }
    public long EstimatedImageSizeBytes { get; set; }
    public long WorkingSetBytes { get; set; }
}

/// <summary>
/// 大画面分析結果
/// </summary>
public class LargeScreenAnalysis
{
    public string PrimaryDisplayCategory { get; set; } = string.Empty;
    public double AverageCaptureTimeMs { get; set; }
    public double AverageOcrTimeMs { get; set; }
    public string BestPerformanceResolution { get; set; } = string.Empty;
    public int BestPerformanceScore { get; set; }
    public string MemoryConstrainedResolution { get; set; } = string.Empty;
}
