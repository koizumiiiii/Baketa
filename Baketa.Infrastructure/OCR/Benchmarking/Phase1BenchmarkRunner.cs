using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.PaddleOCR.Enhancement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Benchmarking;

/// <summary>
/// Phase 1: PaddleOCRパラメータ最適化の実行とレポート生成
/// </summary>
public class Phase1BenchmarkRunner(IServiceProvider serviceProvider, ILogger<Phase1BenchmarkRunner> logger)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<Phase1BenchmarkRunner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Phase 1のベンチマークを実行
    /// </summary>
    public async Task<Phase1BenchmarkReport> RunAsync()
    {
        _logger.LogInformation("=== Phase 1: PaddleOCRパラメータ最適化ベンチマーク開始 ===");

        try
        {
            // 必要なサービスを取得
            var ocrEngine = _serviceProvider.GetRequiredService<IOcrEngine>();
            var benchmarkRunner = _serviceProvider.GetRequiredService<OcrParameterBenchmarkRunner>();
            var testCaseGenerator = _serviceProvider.GetRequiredService<TestCaseGenerator>();

            // OCRエンジンの初期化
            await InitializeOcrEngineAsync(ocrEngine).ConfigureAwait(false);

            // テストケースの生成
            var testCases = await GenerateTestCasesAsync(testCaseGenerator).ConfigureAwait(false);

            // ベンチマークの実行
            var optimizationResult = await benchmarkRunner.RunParameterOptimizationBenchmarkAsync(
                ocrEngine, testCases).ConfigureAwait(false);

            // レポートの生成
            var report = GenerateReport(optimizationResult);

            // レポートの出力
            await OutputReportAsync(report).ConfigureAwait(false);

            _logger.LogInformation("=== Phase 1: PaddleOCRパラメータ最適化ベンチマーク完了 ===");

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Phase 1ベンチマーク実行エラー");
            throw;
        }
    }

    /// <summary>
    /// OCRエンジンの初期化
    /// </summary>
    private async Task InitializeOcrEngineAsync(IOcrEngine ocrEngine)
    {
        _logger.LogInformation("OCRエンジン初期化開始");

        if (!ocrEngine.IsInitialized)
        {
            var settings = new OcrEngineSettings
            {
                Language = "jpn",
                DetectionThreshold = 0.3,
                RecognitionThreshold = 0.3,
                ModelName = "standard",
                MaxDetections = 100
            };

            var initialized = await ocrEngine.InitializeAsync(settings).ConfigureAwait(false);
            if (!initialized)
            {
                throw new InvalidOperationException("OCRエンジンの初期化に失敗しました");
            }
        }

        _logger.LogInformation("OCRエンジン初期化完了: {EngineName} v{Version}",
            ocrEngine.EngineName, ocrEngine.EngineVersion);
    }

    /// <summary>
    /// テストケースの生成
    /// </summary>
    private async Task<List<TestCase>> GenerateTestCasesAsync(TestCaseGenerator generator)
    {
        _logger.LogInformation("テストケース生成開始");

        var testCases = new List<TestCase>();

        // 日本語・英語混在テキストのテストケース
        var mixedTextCases = await generator.GenerateJapaneseMixedTextTestCasesAsync().ConfigureAwait(false);
        testCases.AddRange(mixedTextCases);

        // 誤認識パターンのテストケース
        var errorPatternCases = await generator.GenerateErrorPatternTestCasesAsync().ConfigureAwait(false);
        testCases.AddRange(errorPatternCases);

        // スクリーンショットのテストケース（存在する場合）
        var screenshotDirectory = "test_screenshots";
        if (System.IO.Directory.Exists(screenshotDirectory))
        {
            var screenshotCases = await generator.GenerateFromScreenshotsAsync(screenshotDirectory).ConfigureAwait(false);
            testCases.AddRange(screenshotCases);
        }

        _logger.LogInformation("テストケース生成完了: {Count}件", testCases.Count);
        return testCases;
    }

    /// <summary>
    /// ベンチマーク結果からレポートを生成
    /// </summary>
    private Phase1BenchmarkReport GenerateReport(ParameterOptimizationResult optimizationResult)
    {
        _logger.LogInformation("Phase 1レポート生成開始");

        var bestMethod = optimizationResult.BestMethod;
        var baselineResult = optimizationResult.Results.First(r => r.MethodName.Contains("ベースライン"));

        // 各手法の詳細分析
        var methodAnalyses = optimizationResult.Results.Select(result => new MethodAnalysis
        {
            MethodName = result.MethodName,
            AverageAccuracy = result.AverageAccuracy,
            TotalAccuracy = result.TotalAccuracy,
            CharacterAccuracy = result.CharacterAccuracy,
            AverageProcessingTime = result.AverageProcessingTime,
            ProcessingSpeed = result.ProcessingSpeed,
            TestCount = result.TestCount,
            AccuracyImprovement = result.AverageAccuracy - baselineResult.AverageAccuracy,
            SpeedChange = result.ProcessingSpeed - baselineResult.ProcessingSpeed,
            ErrorReduction = baselineResult.ErrorAnalysis.TotalErrors - result.ErrorAnalysis.TotalErrors,
            TopErrors = [.. result.ErrorAnalysis.CommonErrors.Take(5)]
        }).ToList();

        // 推奨設定の決定
        var recommendations = GenerateRecommendations(methodAnalyses);

        var report = new Phase1BenchmarkReport
        {
            ExecutionTime = DateTime.Now,
            BaselineAccuracy = baselineResult.AverageAccuracy,
            BestMethodName = bestMethod.MethodName,
            BestMethodAccuracy = bestMethod.AverageAccuracy,
            AccuracyImprovement = bestMethod.AverageAccuracy - baselineResult.AverageAccuracy,
            SpeedChange = bestMethod.ProcessingSpeed - baselineResult.ProcessingSpeed,
            TotalTestCases = methodAnalyses.First().TestCount,
            MethodAnalyses = methodAnalyses,
            ImprovementSummary = optimizationResult.ImprovementSummary,
            Recommendations = recommendations
        };

        _logger.LogInformation("Phase 1レポート生成完了");
        return report;
    }

    /// <summary>
    /// 推奨設定を生成
    /// </summary>
    private List<string> GenerateRecommendations(List<MethodAnalysis> methodAnalyses)
    {
        var recommendations = new List<string>();

        // 精度改善の推奨
        var bestAccuracy = methodAnalyses.OrderByDescending(m => m.AverageAccuracy).First();
        if (bestAccuracy.AccuracyImprovement > 0.05)
        {
            recommendations.Add($"精度改善: {bestAccuracy.MethodName}を使用すると{bestAccuracy.AccuracyImprovement * 100:F1}%の精度向上が期待できます");
        }

        // 速度改善の推奨
        var bestSpeed = methodAnalyses.OrderByDescending(m => m.ProcessingSpeed).First();
        if (bestSpeed.SpeedChange > 5)
        {
            recommendations.Add($"速度改善: {bestSpeed.MethodName}を使用すると{bestSpeed.SpeedChange:F1}文字/秒の速度向上が期待できます");
        }

        // バランスの推奨
        var balancedMethod = methodAnalyses
            .Where(m => m.AccuracyImprovement > 0.01 && m.SpeedChange > -5)
            .OrderByDescending(m => m.AverageAccuracy)
            .FirstOrDefault();

        if (balancedMethod != null)
        {
            recommendations.Add($"バランス推奨: {balancedMethod.MethodName}が精度と速度のバランスが最適です");
        }

        // エラー削減の推奨
        var bestErrorReduction = methodAnalyses.OrderByDescending(m => m.ErrorReduction).First();
        if (bestErrorReduction.ErrorReduction > 0)
        {
            recommendations.Add($"エラー削減: {bestErrorReduction.MethodName}を使用すると{bestErrorReduction.ErrorReduction}個のエラーが削減されます");
        }

        return recommendations;
    }

    /// <summary>
    /// レポートの出力
    /// </summary>
    private async Task OutputReportAsync(Phase1BenchmarkReport report)
    {
        _logger.LogInformation("Phase 1レポート出力開始");

        // コンソール出力
        Console.WriteLine("=== Phase 1: PaddleOCRパラメータ最適化結果 ===");
        Console.WriteLine($"実行時間: {report.ExecutionTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"テストケース数: {report.TotalTestCases}");
        Console.WriteLine($"ベースライン精度: {report.BaselineAccuracy * 100:F2}%");
        Console.WriteLine($"最適手法: {report.BestMethodName}");
        Console.WriteLine($"最適精度: {report.BestMethodAccuracy * 100:F2}%");
        Console.WriteLine($"精度改善: {report.AccuracyImprovement * 100:F2}%");
        Console.WriteLine($"速度変化: {report.SpeedChange:F1}文字/秒");
        Console.WriteLine();

        Console.WriteLine("=== 各手法の詳細結果 ===");
        foreach (var analysis in report.MethodAnalyses)
        {
            Console.WriteLine($"{analysis.MethodName}:");
            Console.WriteLine($"  精度: {analysis.AverageAccuracy * 100:F2}% (改善: {analysis.AccuracyImprovement * 100:F2}%)");
            Console.WriteLine($"  速度: {analysis.ProcessingSpeed:F1}文字/秒 (変化: {analysis.SpeedChange:F1}文字/秒)");
            Console.WriteLine($"  処理時間: {analysis.AverageProcessingTime.TotalMilliseconds:F1}ms");
            Console.WriteLine($"  エラー削減: {analysis.ErrorReduction}個");
            Console.WriteLine();
        }

        Console.WriteLine("=== 推奨設定 ===");
        foreach (var recommendation in report.Recommendations)
        {
            Console.WriteLine($"• {recommendation}");
        }
        Console.WriteLine();

        Console.WriteLine("=== 改善概要 ===");
        Console.WriteLine(report.ImprovementSummary);

        // ファイル出力
        var reportJson = System.Text.Json.JsonSerializer.Serialize(report, JsonOptions);

        var reportFileName = $"phase1_benchmark_report_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        await System.IO.File.WriteAllTextAsync(reportFileName, reportJson).ConfigureAwait(false);

        _logger.LogInformation("Phase 1レポート出力完了: {FileName}", reportFileName);
    }
}

/// <summary>
/// Phase 1ベンチマークレポート
/// </summary>
public class Phase1BenchmarkReport
{
    public DateTime ExecutionTime { get; set; }
    public double BaselineAccuracy { get; set; }
    public string BestMethodName { get; set; } = string.Empty;
    public double BestMethodAccuracy { get; set; }
    public double AccuracyImprovement { get; set; }
    public double SpeedChange { get; set; }
    public int TotalTestCases { get; set; }
    public List<MethodAnalysis> MethodAnalyses { get; set; } = [];
    public string ImprovementSummary { get; set; } = string.Empty;
    public List<string> Recommendations { get; set; } = [];
}

/// <summary>
/// 手法分析結果
/// </summary>
public class MethodAnalysis
{
    public string MethodName { get; set; } = string.Empty;
    public double AverageAccuracy { get; set; }
    public double TotalAccuracy { get; set; }
    public double CharacterAccuracy { get; set; }
    public TimeSpan AverageProcessingTime { get; set; }
    public double ProcessingSpeed { get; set; }
    public int TestCount { get; set; }
    public double AccuracyImprovement { get; set; }
    public double SpeedChange { get; set; }
    public int ErrorReduction { get; set; }
    public List<KeyValuePair<string, int>> TopErrors { get; set; } = [];
}
