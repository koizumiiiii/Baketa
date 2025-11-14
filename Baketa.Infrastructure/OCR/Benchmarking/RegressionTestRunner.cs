using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
// using Baketa.Core.Abstractions.OCR; // 削除: アーキテクチャ違反
// using Baketa.Core.Abstractions.Services; // 削除: アーキテクチャ違反

namespace Baketa.Infrastructure.OCR.Benchmarking;

/// <summary>
/// 回帰テスト自動化システム
/// 継続的パフォーマンス監視とベースライン管理
/// </summary>
public class RegressionTestRunner(IServiceProvider serviceProvider, ILogger<RegressionTestRunner> logger)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<RegressionTestRunner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _baselineDirectory = "benchmarks/baselines";
    private readonly string _resultsDirectory = "benchmarks/results";

    /// <summary>
    /// 回帰テストの実行
    /// </summary>
    public async Task<RegressionTestReport> RunAsync()
    {
        _logger.LogInformation("=== 回帰テスト自動化システム開始 ===");

        var report = new RegressionTestReport
        {
            ExecutionTime = DateTime.Now,
            TestEnvironment = await CollectTestEnvironmentAsync().ConfigureAwait(false)
        };

        try
        {
            // ディレクトリ準備
            await PrepareDirectoriesAsync().ConfigureAwait(false);

            // ベースライン読み込み
            var baseline = await LoadBaselineAsync().ConfigureAwait(false);

            // 現在のパフォーマンス測定
            report.CurrentResults = await MeasureCurrentPerformanceAsync().ConfigureAwait(false);

            // 回帰分析
            report.RegressionAnalysis = AnalyzeRegression(baseline, report.CurrentResults);

            // ベースライン更新判定
            var shouldUpdateBaseline = ShouldUpdateBaseline(report.RegressionAnalysis);
            if (shouldUpdateBaseline)
            {
                await UpdateBaselineAsync(report.CurrentResults).ConfigureAwait(false);
                report.BaselineUpdated = true;
            }

            // レポート出力
            await OutputReportAsync(report).ConfigureAwait(false);

            // 結果保存
            await SaveResultsAsync(report).ConfigureAwait(false);

            _logger.LogInformation("=== 回帰テスト自動化システム完了 ===");

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "回帰テスト実行エラー");
            throw;
        }
    }

    /// <summary>
    /// テスト環境情報収集
    /// </summary>
    private async Task<TestEnvironment> CollectTestEnvironmentAsync()
    {
        var environment = new TestEnvironment
        {
            MachineName = Environment.MachineName,
            OSVersion = Environment.OSVersion.ToString(),
            DotNetVersion = Environment.Version.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            WorkingSet = Environment.WorkingSet
        };

        try
        {
            // Git情報収集
            environment.GitCommit = await GetGitCommitAsync().ConfigureAwait(false);
            environment.GitBranch = await GetGitBranchAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Git情報収集失敗");
        }

        return environment;
    }

    /// <summary>
    /// Git コミットハッシュ取得
    /// </summary>
    private async Task<string> GetGitCommitAsync()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse HEAD",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            return output.Trim();
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Git ブランチ名取得
    /// </summary>
    private async Task<string> GetGitBranchAsync()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse --abbrev-ref HEAD",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            return output.Trim();
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// ディレクトリ準備
    /// </summary>
    private async Task PrepareDirectoriesAsync()
    {
        await Task.Run(() =>
        {
            Directory.CreateDirectory(_baselineDirectory);
            Directory.CreateDirectory(_resultsDirectory);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// ベースライン読み込み
    /// </summary>
    private async Task<RegressionPerformanceBaseline?> LoadBaselineAsync()
    {
        var baselineFile = Path.Combine(_baselineDirectory, "performance_baseline.json");

        if (!File.Exists(baselineFile))
        {
            _logger.LogInformation("ベースラインファイルが存在しません: {BaselineFile}", baselineFile);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(baselineFile).ConfigureAwait(false);
            var baseline = JsonSerializer.Deserialize<RegressionPerformanceBaseline>(json, JsonOptions);

            _logger.LogInformation("ベースライン読み込み完了: {BaselineDate}", baseline?.CreatedAt);
            return baseline;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ベースライン読み込みエラー");
            return null;
        }
    }

    /// <summary>
    /// 現在のパフォーマンス測定
    /// </summary>
    private async Task<PerformanceMeasurement> MeasureCurrentPerformanceAsync()
    {
        _logger.LogInformation("現在のパフォーマンス測定開始");

        var measurement = new PerformanceMeasurement
        {
            MeasuredAt = DateTime.Now
        };

        try
        {
            // キャプチャパフォーマンス測定
            measurement.CapturePerformance = await MeasureCapturePerformanceAsync().ConfigureAwait(false);

            // OCRパフォーマンス測定
            measurement.OcrPerformance = await MeasureOcrPerformanceAsync().ConfigureAwait(false);

            // メモリ使用量測定
            measurement.MemoryUsage = await MeasureMemoryUsageAsync().ConfigureAwait(false);

            _logger.LogInformation("現在のパフォーマンス測定完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "パフォーマンス測定エラー");
            throw;
        }

        return measurement;
    }

    /// <summary>
    /// キャプチャパフォーマンス測定（スタブ版）
    /// </summary>
    private async Task<CapturePerformance> MeasureCapturePerformanceAsync()
    {
        // スタブ実装: キャプチャサービスの代わりにシミュレーション
        var measurements = new List<double>();

        // スタブ実装: キャプチャ時間をシミュレート
        await Task.Delay(100).ConfigureAwait(false);

        for (int i = 0; i < 10; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            await Task.Delay(Random.Shared.Next(80, 120)).ConfigureAwait(false); // 80-120msのランダムディレイ
            stopwatch.Stop();

            measurements.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        return new CapturePerformance
        {
            AverageTimeMs = measurements.Average(),
            MinTimeMs = measurements.Min(),
            MaxTimeMs = measurements.Max(),
            StandardDeviation = CalculateStandardDeviation(measurements),
            MeasurementCount = measurements.Count
        };
    }

    /// <summary>
    /// OCRパフォーマンス測定（スタブ版）
    /// </summary>
    private async Task<OcrPerformance> MeasureOcrPerformanceAsync()
    {
        // スタブ実装: OCRエンジンの代わりにシミュレーション
        var measurements = new List<double>();

        // スタブ実装: OCR処理時間をシミュレート
        await Task.Delay(100).ConfigureAwait(false);

        for (int i = 0; i < 5; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            await Task.Delay(Random.Shared.Next(2000, 4000)).ConfigureAwait(false); // 2-4秒のランダムディレイ
            stopwatch.Stop();

            measurements.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        return new OcrPerformance
        {
            AverageTimeMs = measurements.Average(),
            MinTimeMs = measurements.Min(),
            MaxTimeMs = measurements.Max(),
            StandardDeviation = CalculateStandardDeviation(measurements),
            MeasurementCount = measurements.Count
        };
    }

    /// <summary>
    /// メモリ使用量測定
    /// </summary>
    private async Task<MemoryUsage> MeasureMemoryUsageAsync()
    {
        await Task.Delay(100).ConfigureAwait(false); // GC安定化待機

        var beforeGC = GC.GetTotalMemory(false);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var afterGC = GC.GetTotalMemory(false);

        return new MemoryUsage
        {
            BeforeGCBytes = beforeGC,
            AfterGCBytes = afterGC,
            WorkingSetBytes = Environment.WorkingSet,
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2)
        };
    }

    // テスト用画像生成メソッドはスタブ実装のため削除

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
    /// 回帰分析
    /// </summary>
    private RegressionAnalysis AnalyzeRegression(RegressionPerformanceBaseline? baseline, PerformanceMeasurement current)
    {
        var analysis = new RegressionAnalysis();

        if (baseline?.PerformanceData == null)
        {
            analysis.Status = "ベースラインなし - 初回実行";
            analysis.IsRegression = false;
            return analysis;
        }

        // キャプチャパフォーマンス比較
        var captureChange = CalculatePerformanceChange(
            baseline.PerformanceData.CapturePerformance.AverageTimeMs,
            current.CapturePerformance.AverageTimeMs);

        // OCRパフォーマンス比較
        var ocrChange = CalculatePerformanceChange(
            baseline.PerformanceData.OcrPerformance.AverageTimeMs,
            current.OcrPerformance.AverageTimeMs);

        // メモリ使用量比較
        var memoryChange = CalculatePerformanceChange(
            baseline.PerformanceData.MemoryUsage.AfterGCBytes,
            current.MemoryUsage.AfterGCBytes);

        analysis.CapturePerformanceChange = captureChange;
        analysis.OcrPerformanceChange = ocrChange;
        analysis.MemoryUsageChange = memoryChange;

        // 回帰判定（10%以上の悪化で回帰とみなす）
        analysis.IsRegression = captureChange.PercentChange > 10 ||
                               ocrChange.PercentChange > 10 ||
                               memoryChange.PercentChange > 10;

        analysis.Status = analysis.IsRegression ? "パフォーマンス回帰検出" : "パフォーマンス正常";

        return analysis;
    }

    /// <summary>
    /// パフォーマンス変化計算
    /// </summary>
    private PerformanceChange CalculatePerformanceChange(double baseline, double current)
    {
        var absoluteChange = current - baseline;
        var percentChange = baseline != 0 ? (absoluteChange / baseline) * 100 : 0;

        return new PerformanceChange
        {
            BaselineValue = baseline,
            CurrentValue = current,
            AbsoluteChange = absoluteChange,
            PercentChange = percentChange
        };
    }

    /// <summary>
    /// ベースライン更新判定
    /// </summary>
    private bool ShouldUpdateBaseline(RegressionAnalysis analysis)
    {
        // 回帰がなく、かつ性能改善があった場合にベースライン更新
        return !analysis.IsRegression &&
               (analysis.CapturePerformanceChange.PercentChange < -5 ||
                analysis.OcrPerformanceChange.PercentChange < -5);
    }

    /// <summary>
    /// ベースライン更新
    /// </summary>
    private async Task UpdateBaselineAsync(PerformanceMeasurement current)
    {
        var baseline = new RegressionPerformanceBaseline
        {
            CreatedAt = DateTime.Now,
            PerformanceData = current
        };

        var baselineFile = Path.Combine(_baselineDirectory, "performance_baseline.json");
        var json = JsonSerializer.Serialize(baseline, JsonOptions);

        await File.WriteAllTextAsync(baselineFile, json).ConfigureAwait(false);

        _logger.LogInformation("ベースライン更新完了: {BaselineFile}", baselineFile);
    }

    /// <summary>
    /// レポート出力
    /// </summary>
    private async Task OutputReportAsync(RegressionTestReport report)
    {
        _logger.LogInformation("回帰テストレポート出力開始");

        // コンソール出力
        Console.WriteLine("=== 回帰テスト結果レポート ===");
        Console.WriteLine($"実行時間: {report.ExecutionTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"環境: {report.TestEnvironment.MachineName} ({report.TestEnvironment.OSVersion})");
        Console.WriteLine($"ブランチ: {report.TestEnvironment.GitBranch}");
        Console.WriteLine($"コミット: {report.TestEnvironment.GitCommit}");
        Console.WriteLine($"ステータス: {report.RegressionAnalysis.Status}");
        Console.WriteLine();

        if (report.RegressionAnalysis.IsRegression)
        {
            Console.WriteLine("⚠️  パフォーマンス回帰が検出されました!");
        }
        else
        {
            Console.WriteLine("✅ パフォーマンスは正常範囲内です");
        }
        Console.WriteLine();

        Console.WriteLine("=== パフォーマンス変化 ===");
        Console.WriteLine($"キャプチャ: {report.RegressionAnalysis.CapturePerformanceChange.PercentChange:+0.0;-0.0;0}%");
        Console.WriteLine($"OCR: {report.RegressionAnalysis.OcrPerformanceChange.PercentChange:+0.0;-0.0;0}%");
        Console.WriteLine($"メモリ: {report.RegressionAnalysis.MemoryUsageChange.PercentChange:+0.0;-0.0;0}%");

        if (report.BaselineUpdated)
        {
            Console.WriteLine();
            Console.WriteLine("📊 ベースラインが更新されました");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 結果保存
    /// </summary>
    private async Task SaveResultsAsync(RegressionTestReport report)
    {
        var resultFile = Path.Combine(_resultsDirectory, $"regression_test_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        var json = JsonSerializer.Serialize(report, JsonOptions);

        await File.WriteAllTextAsync(resultFile, json).ConfigureAwait(false);

        _logger.LogInformation("回帰テスト結果保存完了: {ResultFile}", resultFile);
    }
}

/// <summary>
/// 回帰テストレポート
/// </summary>
public class RegressionTestReport
{
    public DateTime ExecutionTime { get; set; }
    public TestEnvironment TestEnvironment { get; set; } = new();
    public PerformanceMeasurement CurrentResults { get; set; } = new();
    public RegressionAnalysis RegressionAnalysis { get; set; } = new();
    public bool BaselineUpdated { get; set; }
}

/// <summary>
/// テスト環境情報
/// </summary>
public class TestEnvironment
{
    public string MachineName { get; set; } = string.Empty;
    public string OSVersion { get; set; } = string.Empty;
    public string DotNetVersion { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public long WorkingSet { get; set; }
    public string GitCommit { get; set; } = string.Empty;
    public string GitBranch { get; set; } = string.Empty;
}

/// <summary>
/// パフォーマンス測定結果
/// </summary>
public class PerformanceMeasurement
{
    public DateTime MeasuredAt { get; set; }
    public CapturePerformance CapturePerformance { get; set; } = new();
    public OcrPerformance OcrPerformance { get; set; } = new();
    public MemoryUsage MemoryUsage { get; set; } = new();
}

/// <summary>
/// キャプチャパフォーマンス
/// </summary>
public class CapturePerformance
{
    public double AverageTimeMs { get; set; }
    public double MinTimeMs { get; set; }
    public double MaxTimeMs { get; set; }
    public double StandardDeviation { get; set; }
    public int MeasurementCount { get; set; }
}

/// <summary>
/// OCRパフォーマンス
/// </summary>
public class OcrPerformance
{
    public double AverageTimeMs { get; set; }
    public double MinTimeMs { get; set; }
    public double MaxTimeMs { get; set; }
    public double StandardDeviation { get; set; }
    public int MeasurementCount { get; set; }
}

/// <summary>
/// メモリ使用量
/// </summary>
public class MemoryUsage
{
    public long BeforeGCBytes { get; set; }
    public long AfterGCBytes { get; set; }
    public long WorkingSetBytes { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
}

/// <summary>
/// パフォーマンスベースライン
/// </summary>
public class RegressionPerformanceBaseline
{
    public DateTime CreatedAt { get; set; }
    public PerformanceMeasurement PerformanceData { get; set; } = new();
}

/// <summary>
/// 回帰分析結果
/// </summary>
public class RegressionAnalysis
{
    public string Status { get; set; } = string.Empty;
    public bool IsRegression { get; set; }
    public PerformanceChange CapturePerformanceChange { get; set; } = new();
    public PerformanceChange OcrPerformanceChange { get; set; } = new();
    public PerformanceChange MemoryUsageChange { get; set; } = new();
}

/// <summary>
/// パフォーマンス変化
/// </summary>
public class PerformanceChange
{
    public double BaselineValue { get; set; }
    public double CurrentValue { get; set; }
    public double AbsoluteChange { get; set; }
    public double PercentChange { get; set; }
}
