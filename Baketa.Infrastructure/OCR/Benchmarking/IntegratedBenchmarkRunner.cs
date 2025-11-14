using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Benchmarking;

/// <summary>
/// 統合ベンチマークシステムランナー
/// GPU・回帰・大画面テストを統合実行し、包括的なパフォーマンス分析を提供
/// </summary>
public class IntegratedBenchmarkRunner(IServiceProvider serviceProvider, ILogger<IntegratedBenchmarkRunner> logger)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<IntegratedBenchmarkRunner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _reportDirectory = "benchmarks/integrated";

    /// <summary>
    /// 統合ベンチマークを実行
    /// </summary>
    public async Task<IntegratedBenchmarkReport> RunAllAsync(BenchmarkOptions? options = null)
    {
        _logger.LogInformation("=== 統合ベンチマークシステム開始 ===");

        options ??= new BenchmarkOptions();

        var report = new IntegratedBenchmarkReport
        {
            ExecutionTime = DateTime.Now,
            Options = options
        };

        try
        {
            // ディレクトリ準備
            await PrepareDirectoriesAsync().ConfigureAwait(false);

            // 各ベンチマークの実行
            if (options.RunGpuBenchmark)
            {
                report.GpuBenchmarkReport = await RunGpuBenchmarkAsync().ConfigureAwait(false);
            }

            if (options.RunRegressionTest)
            {
                report.RegressionTestReport = await RunRegressionTestAsync().ConfigureAwait(false);
            }

            if (options.RunLargeScreenTest)
            {
                report.LargeScreenReport = await RunLargeScreenTestAsync().ConfigureAwait(false);
            }

            if (options.RunOcrParameterBenchmark)
            {
                report.Phase1BenchmarkReport = await RunPhase1BenchmarkAsync().ConfigureAwait(false);
            }

            // 統合分析
            report.IntegratedAnalysis = PerformIntegratedAnalysis(report);
            report.ComprehensiveRecommendations = GenerateComprehensiveRecommendations(report);

            // 結果出力
            await OutputIntegratedReportAsync(report).ConfigureAwait(false);

            // ファイル保存
            await SaveIntegratedReportAsync(report).ConfigureAwait(false);

            _logger.LogInformation("=== 統合ベンチマークシステム完了 ===");

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "統合ベンチマーク実行エラー");
            throw;
        }
    }

    /// <summary>
    /// GPU環境別ベンチマーク実行
    /// </summary>
    private async Task<GpuBenchmarkReport?> RunGpuBenchmarkAsync()
    {
        _logger.LogInformation("GPU環境別ベンチマーク実行開始");

        try
        {
            var gpuRunner = _serviceProvider.GetRequiredService<GpuEnvironmentBenchmarkRunner>();
            var result = await gpuRunner.RunAsync().ConfigureAwait(false);

            _logger.LogInformation("GPU環境別ベンチマーク完了");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPU環境別ベンチマークエラー");
            return null;
        }
    }

    /// <summary>
    /// 回帰テスト実行
    /// </summary>
    private async Task<RegressionTestReport?> RunRegressionTestAsync()
    {
        _logger.LogInformation("回帰テスト実行開始");

        try
        {
            var regressionRunner = _serviceProvider.GetRequiredService<RegressionTestRunner>();
            var result = await regressionRunner.RunAsync().ConfigureAwait(false);

            _logger.LogInformation("回帰テスト完了");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "回帰テストエラー");
            return null;
        }
    }

    /// <summary>
    /// 大画面パフォーマンステスト実行
    /// </summary>
    private async Task<LargeScreenPerformanceReport?> RunLargeScreenTestAsync()
    {
        _logger.LogInformation("大画面パフォーマンステスト実行開始");

        try
        {
            var largeScreenRunner = _serviceProvider.GetRequiredService<LargeScreenPerformanceRunner>();
            var result = await largeScreenRunner.RunAsync().ConfigureAwait(false);

            _logger.LogInformation("大画面パフォーマンステスト完了");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "大画面パフォーマンステストエラー");
            return null;
        }
    }

    /// <summary>
    /// Phase1 OCRパラメータベンチマーク実行
    /// </summary>
    private async Task<Phase1BenchmarkReport?> RunPhase1BenchmarkAsync()
    {
        _logger.LogInformation("Phase1 OCRパラメータベンチマーク実行開始");

        try
        {
            var phase1Runner = _serviceProvider.GetRequiredService<Phase1BenchmarkRunner>();
            var result = await phase1Runner.RunAsync().ConfigureAwait(false);

            _logger.LogInformation("Phase1 OCRパラメータベンチマーク完了");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Phase1 OCRパラメータベンチマークエラー");
            return null;
        }
    }

    /// <summary>
    /// ディレクトリ準備
    /// </summary>
    private async Task PrepareDirectoriesAsync()
    {
        await Task.Run(() =>
        {
            Directory.CreateDirectory(_reportDirectory);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 統合分析実行
    /// </summary>
    private IntegratedAnalysis PerformIntegratedAnalysis(IntegratedBenchmarkReport report)
    {
        _logger.LogInformation("統合分析実行開始");

        var analysis = new IntegratedAnalysis();

        try
        {
            // 環境総合情報
            analysis.EnvironmentSummary = BuildEnvironmentSummary(report);

            // パフォーマンス総合評価
            analysis.OverallPerformanceRating = CalculateOverallPerformanceRating(report);

            // ボトルネック分析
            analysis.BottleneckAnalysis = IdentifyBottlenecks(report);

            // 最適化優先度
            analysis.OptimizationPriorities = DetermineOptimizationPriorities(report);

            // リソース使用量分析
            analysis.ResourceUtilization = AnalyzeResourceUtilization(report);

            // スケーラビリティ分析
            analysis.ScalabilityAssessment = AssessScalability(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "統合分析エラー");
        }

        _logger.LogInformation("統合分析完了: 総合評価={OverallRating}", analysis.OverallPerformanceRating);
        return analysis;
    }

    /// <summary>
    /// 環境総合情報構築
    /// </summary>
    private string BuildEnvironmentSummary(IntegratedBenchmarkReport report)
    {
        var summary = "";

        if (report.GpuBenchmarkReport != null)
        {
            var gpuInfo = report.GpuBenchmarkReport.SystemInfo.GPUInfo;
            var gpuTypes = string.Join(", ", gpuInfo.ConvertAll(g => g.IsIntegrated ? "統合GPU" : "専用GPU"));
            summary += $"GPU環境: {gpuTypes}; ";
        }

        if (report.LargeScreenReport != null)
        {
            var displayConfig = report.LargeScreenReport.DisplayConfiguration;
            summary += $"ディスプレイ: {displayConfig.TotalDisplays}台, 最大{displayConfig.MaxWidth}x{displayConfig.MaxHeight}; ";
        }

        if (report.RegressionTestReport != null)
        {
            var testEnv = report.RegressionTestReport.TestEnvironment;
            summary += $"OS: {testEnv.OSVersion}, CPU: {testEnv.ProcessorCount}コア";
        }

        return summary;
    }

    /// <summary>
    /// 総合パフォーマンス評価計算
    /// </summary>
    private string CalculateOverallPerformanceRating(IntegratedBenchmarkReport report)
    {
        var scores = new List<int>();

        // GPUベンチマークスコア
        if (report.GpuBenchmarkReport?.DirectFullScreenResults != null)
        {
            var captureTime = report.GpuBenchmarkReport.DirectFullScreenResults.AverageTimeMs;
            var score = captureTime < 100 ? 90 : captureTime < 300 ? 70 : 50;
            scores.Add(score);
        }

        // 回帰テストスコア
        if (report.RegressionTestReport?.RegressionAnalysis != null)
        {
            var score = report.RegressionTestReport.RegressionAnalysis.IsRegression ? 30 : 85;
            scores.Add(score);
        }

        // 大画面パフォーマンススコア
        if (report.LargeScreenReport?.Analysis != null)
        {
            var score = report.LargeScreenReport.Analysis.BestPerformanceScore;
            scores.Add(score);
        }

        if (scores.Count == 0) return "評価不可";

        var averageScore = scores.Sum() / scores.Count;

        return averageScore switch
        {
            >= 85 => "優秀",
            >= 70 => "良好",
            >= 50 => "普通",
            _ => "改善必要"
        };
    }

    /// <summary>
    /// ボトルネック識別
    /// </summary>
    private List<string> IdentifyBottlenecks(IntegratedBenchmarkReport report)
    {
        var bottlenecks = new List<string>();

        // GPUパフォーマンスボトルネック
        if (report.GpuBenchmarkReport?.DirectFullScreenResults?.AverageTimeMs > 300)
        {
            bottlenecks.Add("キャプチャ処理: GPUパフォーマンスが低い");
        }

        // 回帰テストボトルネック
        if (report.RegressionTestReport?.RegressionAnalysis.IsRegression == true)
        {
            bottlenecks.Add("パフォーマンス回帰: 最近の変更で性能が悪化");
        }

        // OCRパフォーマンスボトルネック
        if (report.RegressionTestReport?.CurrentResults.OcrPerformance.AverageTimeMs > 5000)
        {
            bottlenecks.Add("OCR処理: 処理時間が長すぎる");
        }

        // メモリボトルネック
        if (report.LargeScreenReport?.MemoryScalingResults.SystemMemoryLimitGB < 8)
        {
            bottlenecks.Add("メモリ容量: システムメモリが不足");
        }

        return bottlenecks;
    }

    /// <summary>
    /// 最適化優先度決定
    /// </summary>
    private List<string> DetermineOptimizationPriorities(IntegratedBenchmarkReport report)
    {
        var priorities = new List<string>();

        // 高優先度の最適化
        if (report.RegressionTestReport?.CurrentResults.OcrPerformance.AverageTimeMs > 3000)
        {
            priorities.Add("高優先度: OCRエンジンのパラメータ最適化");
        }

        if (report.GpuBenchmarkReport?.DirectFullScreenResults?.AverageTimeMs > 200)
        {
            priorities.Add("高優先度: キャプチャ方式の最適化 (ROIベースキャプチャの導入)");
        }

        // 中優先度の最適化
        if (report.LargeScreenReport?.DisplayConfiguration.HasHighDPI == true)
        {
            priorities.Add("中優先度: 高DPI環境対応の強化");
        }

        if (report.LargeScreenReport?.DisplayConfiguration.HasUltraWide == true)
        {
            priorities.Add("中優先度: ウルトラワイドディスプレイ最適化");
        }

        // 低優先度の最適化
        priorities.Add("低優先度: UIレスポンシブ性の向上");
        priorities.Add("低優先度: メモリ使用量の最適化");

        return priorities;
    }

    /// <summary>
    /// リソース使用量分析
    /// </summary>
    private string AnalyzeResourceUtilization(IntegratedBenchmarkReport report)
    {
        var analysis = "";

        if (report.RegressionTestReport?.CurrentResults.MemoryUsage != null)
        {
            var memoryUsage = report.RegressionTestReport.CurrentResults.MemoryUsage;
            var memoryUtilizationMB = memoryUsage.AfterGCBytes / (1024 * 1024);
            analysis += $"\u30e1\u30e2\u30ea\u4f7f\u7528\u91cf: {memoryUtilizationMB}MB; ";
        }

        if (report.GpuBenchmarkReport?.SystemInfo.ProcessorCount != null)
        {
            var cpuCores = report.GpuBenchmarkReport.SystemInfo.ProcessorCount;
            analysis += $"CPUコア数: {cpuCores}; ";
        }

        return analysis;
    }

    /// <summary>
    /// スケーラビリティ評価
    /// </summary>
    private string AssessScalability(IntegratedBenchmarkReport report)
    {
        if (report.LargeScreenReport?.Analysis != null)
        {
            var analysis = report.LargeScreenReport.Analysis;

            if (analysis.BestPerformanceScore >= 80)
                return "高いスケーラビリティ: 大画面環境でも良好なパフォーマンス";

            if (analysis.BestPerformanceScore >= 60)
                return "中程度のスケーラビリティ: 一部制約あり";

            return "低いスケーラビリティ: 大画面環境での最適化が必要";
        }

        return "スケーラビリティ未評価";
    }

    /// <summary>
    /// 包括的推奨事項生成
    /// </summary>
    private List<string> GenerateComprehensiveRecommendations(IntegratedBenchmarkReport report)
    {
        var recommendations = new List<string>();

        // 各ベンチマークの推奨事項を統合
        if (report.GpuBenchmarkReport?.Recommendations != null)
        {
            recommendations.AddRange(report.GpuBenchmarkReport.Recommendations);
        }

        if (report.LargeScreenReport?.Recommendations != null)
        {
            recommendations.AddRange(report.LargeScreenReport.Recommendations);
        }

        if (report.Phase1BenchmarkReport?.Recommendations != null)
        {
            recommendations.AddRange(report.Phase1BenchmarkReport.Recommendations);
        }

        // 統合的な推奨事項を追加
        recommendations.Add("継続的なパフォーマンス監視: ベースライン管理システムの定期実行");
        recommendations.Add("環境別最適化: GPU・解像度に応じた動的設定変更の実装");
        recommendations.Add("ユーザーエクスペリエンス向上: パフォーマンス設定の自動調整機能");

        return recommendations;
    }

    /// <summary>
    /// 統合レポート出力
    /// </summary>
    private async Task OutputIntegratedReportAsync(IntegratedBenchmarkReport report)
    {
        _logger.LogInformation("統合ベンチマークレポート出力開始");

        Console.WriteLine("=== 統合ベンチマークシステム 総合結果 ===");
        Console.WriteLine($"実行時間: {report.ExecutionTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"環境概要: {report.IntegratedAnalysis.EnvironmentSummary}");
        Console.WriteLine($"総合評価: {report.IntegratedAnalysis.OverallPerformanceRating}");
        Console.WriteLine($"スケーラビリティ: {report.IntegratedAnalysis.ScalabilityAssessment}");
        Console.WriteLine();

        if (report.IntegratedAnalysis.BottleneckAnalysis.Count > 0)
        {
            Console.WriteLine("🔴 検出されたボトルネック:");
            foreach (var bottleneck in report.IntegratedAnalysis.BottleneckAnalysis)
            {
                Console.WriteLine($"  • {bottleneck}");
            }
            Console.WriteLine();
        }

        Console.WriteLine("📊 最適化優先度:");
        foreach (var priority in report.IntegratedAnalysis.OptimizationPriorities)
        {
            Console.WriteLine($"  • {priority}");
        }
        Console.WriteLine();

        Console.WriteLine("💡 包括的推奨事項:");
        foreach (var recommendation in report.ComprehensiveRecommendations.Take(10)) // 上位10件のみ表示
        {
            Console.WriteLine($"  • {recommendation}");
        }

        Console.WriteLine();
        Console.WriteLine($"📝 詳細レポートはファイルに保存されました");

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 統合レポート保存
    /// </summary>
    private async Task SaveIntegratedReportAsync(IntegratedBenchmarkReport report)
    {
        var reportFile = Path.Combine(_reportDirectory, $"integrated_benchmark_report_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        var json = JsonSerializer.Serialize(report, JsonOptions);

        await File.WriteAllTextAsync(reportFile, json).ConfigureAwait(false);

        _logger.LogInformation("統合ベンチマークレポート保存完了: {ReportFile}", reportFile);
    }
}

/// <summary>
/// ベンチマークオプション
/// </summary>
public class BenchmarkOptions
{
    public bool RunGpuBenchmark { get; set; } = true;
    public bool RunRegressionTest { get; set; } = true;
    public bool RunLargeScreenTest { get; set; } = true;
    public bool RunOcrParameterBenchmark { get; set; } // 時間がかかるためデフォルトは無効(false)
}

/// <summary>
/// 統合ベンチマークレポート
/// </summary>
public class IntegratedBenchmarkReport
{
    public DateTime ExecutionTime { get; set; }
    public BenchmarkOptions Options { get; set; } = new();
    public GpuBenchmarkReport? GpuBenchmarkReport { get; set; }
    public RegressionTestReport? RegressionTestReport { get; set; }
    public LargeScreenPerformanceReport? LargeScreenReport { get; set; }
    public Phase1BenchmarkReport? Phase1BenchmarkReport { get; set; }
    public IntegratedAnalysis IntegratedAnalysis { get; set; } = new();
    public List<string> ComprehensiveRecommendations { get; set; } = [];
}

/// <summary>
/// 統合分析結果
/// </summary>
public class IntegratedAnalysis
{
    public string EnvironmentSummary { get; set; } = string.Empty;
    public string OverallPerformanceRating { get; set; } = string.Empty;
    public List<string> BottleneckAnalysis { get; set; } = [];
    public List<string> OptimizationPriorities { get; set; } = [];
    public string ResourceUtilization { get; set; } = string.Empty;
    public string ScalabilityAssessment { get; set; } = string.Empty;
}
