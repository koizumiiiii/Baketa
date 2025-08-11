using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.Benchmarking;
using System.Diagnostics;

namespace Baketa.Infrastructure.OCR.AdaptivePreprocessing;

/// <summary>
/// 適応的前処理のベンチマーククラス
/// </summary>
public class AdaptivePreprocessingBenchmark(
    IAdaptivePreprocessingParameterOptimizer parameterOptimizer,
    TestCaseGenerator testCaseGenerator,
    ILogger<AdaptivePreprocessingBenchmark> logger)
{

    /// <summary>
    /// 適応的前処理の包括的ベンチマークを実行
    /// </summary>
    public async Task<AdaptivePreprocessingBenchmarkResult> RunComprehensiveBenchmarkAsync(IOcrEngine ocrEngine)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("適応的前処理ベンチマーク開始");

        try
        {
            // テストケース生成
            var testCases = await GenerateTestCasesAsync().ConfigureAwait(false);
            logger.LogInformation("テストケース生成完了: {Count}件", testCases.Count);

            // ベンチマーク実行
            var results = new List<AdaptiveTestResult>();
            var baselineResults = new List<BaselineTestResult>();

            foreach (var testCase in testCases)
            {
                logger.LogDebug("テストケース実行: {Name}", testCase.Name);

                // ベースライン（従来手法）での実行
                var baselineResult = await RunBaselineTestAsync(ocrEngine, testCase).ConfigureAwait(false);
                baselineResults.Add(baselineResult);

                // 適応的前処理での実行
                var adaptiveResult = await RunAdaptiveTestAsync(ocrEngine, testCase).ConfigureAwait(false);
                results.Add(adaptiveResult);
            }

            // 結果分析
            var analysis = AnalyzeResults(results, baselineResults);

            var benchmarkResult = new AdaptivePreprocessingBenchmarkResult
            {
                TestResults = results,
                BaselineResults = baselineResults,
                Analysis = analysis,
                TotalTestCases = testCases.Count,
                TotalBenchmarkTimeMs = sw.ElapsedMilliseconds,
                BenchmarkDate = DateTime.UtcNow
            };

            logger.LogInformation(
                "適応的前処理ベンチマーク完了: {TestCases}件, 平均改善={Improvement:F2}%, 実行時間={TimeMs}ms",
                testCases.Count, analysis.AverageImprovementPercentage, sw.ElapsedMilliseconds);

            return benchmarkResult;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "適応的前処理ベンチマーク中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// 特定の画像品質レベルでのベンチマーク
    /// </summary>
    public async Task<AdaptiveQualityBenchmarkResult> RunQualitySpecificBenchmarkAsync(
        IOcrEngine ocrEngine, 
        ImageQualityLevel qualityLevel)
    {
        logger.LogInformation("品質特化ベンチマーク開始: {QualityLevel}", qualityLevel);

        var testCases = await GenerateQualitySpecificTestCasesAsync(qualityLevel).ConfigureAwait(false);
        var results = new List<AdaptiveTestResult>();

        foreach (var testCase in testCases)
        {
            var result = await RunAdaptiveTestAsync(ocrEngine, testCase).ConfigureAwait(false);
            results.Add(result);
        }

        var qualityAnalysis = AnalyzeQualitySpecificResults(results, qualityLevel);

        return new AdaptiveQualityBenchmarkResult
        {
            QualityLevel = qualityLevel,
            TestResults = results,
            QualityAnalysis = qualityAnalysis,
            TestCaseCount = testCases.Count
        };
    }

    /// <summary>
    /// パフォーマンスベンチマーク（速度重視）
    /// </summary>
    public async Task<AdaptivePerformanceBenchmarkResult> RunPerformanceBenchmarkAsync(IOcrEngine ocrEngine)
    {
        logger.LogInformation("パフォーマンスベンチマーク開始");

        var testCases = await GeneratePerformanceTestCasesAsync().ConfigureAwait(false);
        var performanceResults = new List<PerformanceTestResult>();

        foreach (var testCase in testCases)
        {
            var result = await RunPerformanceTestAsync(ocrEngine, testCase).ConfigureAwait(false);
            performanceResults.Add(result);
        }

        var performanceAnalysis = AnalyzePerformanceResults(performanceResults);

        return new AdaptivePerformanceBenchmarkResult
        {
            PerformanceResults = performanceResults,
            PerformanceAnalysis = performanceAnalysis,
            TestCaseCount = testCases.Count
        };
    }

    #region Test Case Generation

    private async Task<List<TestCase>> GenerateTestCasesAsync()
    {
        var testCases = new List<TestCase>();

        // 基本的なエラーパターンテストケース
        var errorPatternCases = await testCaseGenerator.GenerateErrorPatternTestCasesAsync().ConfigureAwait(false);
        testCases.AddRange([.. errorPatternCases]);

        // 小文字テキストテストケース
        var smallTextCases = await GenerateSmallTextTestCasesAsync().ConfigureAwait(false);
        testCases.AddRange(smallTextCases);

        // 低品質画像テストケース
        var lowQualityCases = await GenerateLowQualityTestCasesAsync().ConfigureAwait(false);
        testCases.AddRange(lowQualityCases);

        // 高ノイズテストケース
        var noisyCases = await GenerateNoisyTestCasesAsync().ConfigureAwait(false);
        testCases.AddRange(noisyCases);

        return testCases;
    }

    private async Task<List<TestCase>> GenerateSmallTextTestCasesAsync()
    {
        var testCases = new List<TestCase>();
        var textSamples = new[] { "単体テスト", "車体テスト", "統合テスト", "αテスト", "βテスト" };
        var fontSizes = new[] { 8, 10, 12, 14 };

        foreach (var text in textSamples)
        {
            foreach (var fontSize in fontSizes)
            {
                var image = await testCaseGenerator.GenerateSmallTextImageAsync(text, fontSize).ConfigureAwait(false);
                testCases.Add(new TestCase($"SmallText_{text}_{fontSize}px", image, text));
            }
        }

        return testCases;
    }

    private async Task<List<TestCase>> GenerateLowQualityTestCasesAsync()
    {
        var testCases = new List<TestCase>();
        var qualityParameters = new[]
        {
            new { Name = "LowContrast", Contrast = 0.2, Brightness = 0.5, Noise = 0.1 },
            new { Name = "Dark", Contrast = 0.5, Brightness = 0.2, Noise = 0.1 },
            new { Name = "Bright", Contrast = 0.5, Brightness = 0.8, Noise = 0.1 },
            new { Name = "Noisy", Contrast = 0.5, Brightness = 0.5, Noise = 0.4 }
        };

        foreach (var param in qualityParameters)
        {
            var image = await testCaseGenerator.GenerateLowQualityImageAsync(
                "テスト文字列", param.Contrast, param.Brightness, param.Noise).ConfigureAwait(false);
            
            testCases.Add(new TestCase($"LowQuality_{param.Name}", image, "テスト文字列"));
        }

        return testCases;
    }

    private async Task<List<TestCase>> GenerateNoisyTestCasesAsync()
    {
        var testCases = new List<TestCase>();
        var noiseTypes = new[] { "Gaussian", "Salt&Pepper", "Speckle" };
        var noiseLevels = new[] { 0.1, 0.3, 0.5 };

        foreach (var noiseType in noiseTypes)
        {
            foreach (var noiseLevel in noiseLevels)
            {
                var image = await testCaseGenerator.GenerateNoisyImageAsync(
                    "ノイズテスト", noiseType, noiseLevel).ConfigureAwait(false);
                
                testCases.Add(new TestCase($"Noisy_{noiseType}_{noiseLevel:F1}", image, "ノイズテスト"));
            }
        }

        return testCases;
    }

    private async Task<List<TestCase>> GenerateQualitySpecificTestCasesAsync(ImageQualityLevel qualityLevel)
    {
        return qualityLevel switch
        {
            ImageQualityLevel.Low => await GenerateLowQualityTestCasesAsync().ConfigureAwait(false),
            ImageQualityLevel.Medium => [.. (await testCaseGenerator.GenerateErrorPatternTestCasesAsync().ConfigureAwait(false))],
            ImageQualityLevel.High => await GenerateHighQualityTestCasesAsync().ConfigureAwait(false),
            _ => [.. (await testCaseGenerator.GenerateErrorPatternTestCasesAsync().ConfigureAwait(false))]
        };
    }

    private async Task<List<TestCase>> GenerateHighQualityTestCasesAsync()
    {
        var testCases = new List<TestCase>();
        var texts = new[] { "高品質テスト", "クリアテキスト", "鮮明文字" };

        foreach (var text in texts)
        {
            var image = await testCaseGenerator.GenerateHighQualityImageAsync(text).ConfigureAwait(false);
            testCases.Add(new TestCase($"HighQuality_{text}", image, text));
        }

        return testCases;
    }

    private async Task<List<TestCase>> GeneratePerformanceTestCasesAsync()
    {
        var testCases = new List<TestCase>();
        var imageSizes = new[] { 
            new { Width = 640, Height = 480 },
            new { Width = 1280, Height = 720 },
            new { Width = 1920, Height = 1080 }
        };

        foreach (var size in imageSizes)
        {
            var image = await testCaseGenerator.GeneratePerformanceTestImageAsync(
                $"パフォーマンステスト {size.Width}x{size.Height}", size.Width, size.Height).ConfigureAwait(false);
            
            testCases.Add(new TestCase($"Performance_{size.Width}x{size.Height}", image, 
                $"パフォーマンステスト {size.Width}x{size.Height}"));
        }

        return testCases;
    }

    #endregion

    #region Test Execution

    private async Task<BaselineTestResult> RunBaselineTestAsync(IOcrEngine ocrEngine, TestCase testCase)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            var result = await ocrEngine.RecognizeAsync(testCase.Image).ConfigureAwait(false);
            
            return new BaselineTestResult
            {
                TestCaseName = testCase.Name,
                ExecutionTimeMs = sw.ElapsedMilliseconds,
                DetectedRegions = result.TextRegions.Count,
                ExtractedText = string.Join(" ", result.TextRegions.Select(r => r.Text)),
                Confidence = result.TextRegions.Any() ? result.TextRegions.Average(r => r.Confidence) : 0.0,
                Success = result.TextRegions.Count > 0
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ベースラインテスト実行エラー: {TestCase}", testCase.Name);
            return new BaselineTestResult
            {
                TestCaseName = testCase.Name,
                ExecutionTimeMs = sw.ElapsedMilliseconds,
                DetectedRegions = 0,
                ExtractedText = "",
                Confidence = 0.0,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<AdaptiveTestResult> RunAdaptiveTestAsync(IOcrEngine ocrEngine, TestCase testCase)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            // IImageをIAdvancedImageに変換
            var imageBytes = await testCase.Image.ToByteArrayAsync().ConfigureAwait(false);
            var advancedImage = new Core.Services.Imaging.AdvancedImage(imageBytes, testCase.Image.Width, testCase.Image.Height, 
                testCase.Image.Format == Core.Abstractions.Imaging.ImageFormat.Png 
                    ? Core.Abstractions.Imaging.ImageFormat.Png 
                    : Core.Abstractions.Imaging.ImageFormat.Rgb24);
            
            // 適応的前処理パラメータを最適化
            var optimizationResult = await parameterOptimizer.OptimizeWithDetailsAsync(advancedImage).ConfigureAwait(false);
            var optimizationTime = sw.ElapsedMilliseconds;

            // 適応的OCRエンジンでOCR実行
            var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole());
            var adaptiveEngine = new AdaptiveOcrEngine(ocrEngine, parameterOptimizer, 
                loggerFactory.CreateLogger<AdaptiveOcrEngine>());
            
            var result = await adaptiveEngine.RecognizeAsync(testCase.Image, progressCallback: null, cancellationToken: default).ConfigureAwait(false);
            var totalTime = sw.ElapsedMilliseconds;

            return new AdaptiveTestResult
            {
                TestCaseName = testCase.Name,
                OptimizationResult = optimizationResult,
                ExecutionTimeMs = totalTime,
                OptimizationTimeMs = optimizationTime,
                OcrTimeMs = totalTime - optimizationTime,
                DetectedRegions = result.TextRegions.Count,
                ExtractedText = string.Join(" ", result.TextRegions.Select(r => r.Text)),
                Confidence = result.TextRegions.Any() ? result.TextRegions.Average(r => r.Confidence) : 0.0,
                Success = result.TextRegions.Count > 0,
                ExpectedText = testCase.ExpectedText
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "適応的テスト実行エラー: {TestCase}", testCase.Name);
            return new AdaptiveTestResult
            {
                TestCaseName = testCase.Name,
                ExecutionTimeMs = sw.ElapsedMilliseconds,
                DetectedRegions = 0,
                ExtractedText = "",
                Confidence = 0.0,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<PerformanceTestResult> RunPerformanceTestAsync(IOcrEngine ocrEngine, TestCase testCase)
    {
        var iterations = 5;
        var executionTimes = new List<long>();
        var optimizationTimes = new List<long>();

        // IImageをIAdvancedImageに変換
        var imageBytes = await testCase.Image.ToByteArrayAsync().ConfigureAwait(false);
        var advancedImage = new Core.Services.Imaging.AdvancedImage(imageBytes, testCase.Image.Width, testCase.Image.Height, 
            testCase.Image.Format == Core.Abstractions.Imaging.ImageFormat.Png 
                ? Core.Abstractions.Imaging.ImageFormat.Png 
                : Core.Abstractions.Imaging.ImageFormat.Rgb24);

        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            
            await parameterOptimizer.OptimizeWithDetailsAsync(advancedImage).ConfigureAwait(false);
            var optimizationTime = sw.ElapsedMilliseconds;
            
            await ocrEngine.RecognizeAsync(testCase.Image).ConfigureAwait(false);
            var totalTime = sw.ElapsedMilliseconds;

            optimizationTimes.Add(optimizationTime);
            executionTimes.Add(totalTime);
        }

        return new PerformanceTestResult
        {
            TestCaseName = testCase.Name,
            AverageExecutionTimeMs = executionTimes.Average(),
            AverageOptimizationTimeMs = optimizationTimes.Average(),
            MinExecutionTimeMs = executionTimes.Min(),
            MaxExecutionTimeMs = executionTimes.Max(),
            Iterations = iterations
        };
    }

    #endregion

    #region Result Analysis

    private AdaptiveBenchmarkAnalysis AnalyzeResults(
        List<AdaptiveTestResult> adaptiveResults, 
        List<BaselineTestResult> baselineResults)
    {
        var pairedResults = adaptiveResults
            .Join(baselineResults, a => a.TestCaseName, b => b.TestCaseName, (a, b) => new { Adaptive = a, Baseline = b })
            .ToList();

        var improvements = pairedResults
            .Where(p => p.Baseline.Success && p.Adaptive.Success)
            .Select(p => (p.Adaptive.DetectedRegions - p.Baseline.DetectedRegions) / (double)Math.Max(p.Baseline.DetectedRegions, 1))
            .ToList();

        var confidenceImprovements = pairedResults
            .Where(p => p.Baseline.Success && p.Adaptive.Success)
            .Select(p => p.Adaptive.Confidence - p.Baseline.Confidence)
            .ToList();

        return new AdaptiveBenchmarkAnalysis
        {
            TotalTestCases = adaptiveResults.Count,
            SuccessfulTestCases = adaptiveResults.Count(r => r.Success),
            AverageImprovementPercentage = improvements.Count > 0 ? improvements.Average() * 100 : 0,
            AverageConfidenceImprovement = confidenceImprovements.Count > 0 ? confidenceImprovements.Average() : 0,
            AverageOptimizationTimeMs = adaptiveResults.Average(r => r.OptimizationTimeMs),
            AverageExecutionTimeMs = adaptiveResults.Average(r => r.ExecutionTimeMs),
            OptimizationStrategies = adaptiveResults
                .GroupBy(r => r.OptimizationResult?.OptimizationStrategy ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    private QualitySpecificAnalysis AnalyzeQualitySpecificResults(
        List<AdaptiveTestResult> results, 
        ImageQualityLevel qualityLevel)
    {
        return new QualitySpecificAnalysis
        {
            QualityLevel = qualityLevel,
            SuccessRate = results.Count > 0 ? results.Count(r => r.Success) / (double)results.Count : 0,
            AverageConfidence = results.Where(r => r.Success).Select(r => r.Confidence).DefaultIfEmpty(0).Average(),
            AverageOptimizationTime = results.Average(r => r.OptimizationTimeMs),
            MostCommonStrategy = results
                .GroupBy(r => r.OptimizationResult?.OptimizationStrategy ?? "Unknown")
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key ?? "Unknown"
        };
    }

    private PerformanceAnalysis AnalyzePerformanceResults(List<PerformanceTestResult> results)
    {
        return new PerformanceAnalysis
        {
            AverageExecutionTime = results.Average(r => r.AverageExecutionTimeMs),
            AverageOptimizationTime = results.Average(r => r.AverageOptimizationTimeMs),
            OptimizationOverhead = results.Average(r => r.AverageOptimizationTimeMs / r.AverageExecutionTimeMs),
            FastestTestCase = results.OrderBy(r => r.AverageExecutionTimeMs).FirstOrDefault()?.TestCaseName ?? "None",
            SlowestTestCase = results.OrderByDescending(r => r.AverageExecutionTimeMs).FirstOrDefault()?.TestCaseName ?? "None"
        };
    }

    #endregion
}

#region Result Classes

public enum ImageQualityLevel
{
    Low,
    Medium,
    High
}

public enum TestDifficulty
{
    Easy,
    Medium,
    Hard
}

public record AdaptivePreprocessingBenchmarkResult
{
    public List<AdaptiveTestResult> TestResults { get; init; } = [];
    public List<BaselineTestResult> BaselineResults { get; init; } = [];
    public AdaptiveBenchmarkAnalysis Analysis { get; init; } = new();
    public int TotalTestCases { get; init; }
    public long TotalBenchmarkTimeMs { get; init; }
    public DateTime BenchmarkDate { get; init; }
}

public record AdaptiveQualityBenchmarkResult
{
    public ImageQualityLevel QualityLevel { get; init; }
    public List<AdaptiveTestResult> TestResults { get; init; } = [];
    public QualitySpecificAnalysis QualityAnalysis { get; init; } = new();
    public int TestCaseCount { get; init; }
}

public record AdaptivePerformanceBenchmarkResult
{
    public List<PerformanceTestResult> PerformanceResults { get; init; } = [];
    public PerformanceAnalysis PerformanceAnalysis { get; init; } = new();
    public int TestCaseCount { get; init; }
}

public record AdaptiveTestResult
{
    public string TestCaseName { get; init; } = "";
    public AdaptivePreprocessingResult? OptimizationResult { get; init; }
    public long ExecutionTimeMs { get; init; }
    public long OptimizationTimeMs { get; init; }
    public long OcrTimeMs { get; init; }
    public int DetectedRegions { get; init; }
    public string ExtractedText { get; init; } = "";
    public double Confidence { get; init; }
    public bool Success { get; init; }
    public string ExpectedText { get; init; } = "";
    public string? ErrorMessage { get; init; }
}

public record BaselineTestResult
{
    public string TestCaseName { get; init; } = "";
    public long ExecutionTimeMs { get; init; }
    public int DetectedRegions { get; init; }
    public string ExtractedText { get; init; } = "";
    public double Confidence { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

public record PerformanceTestResult
{
    public string TestCaseName { get; init; } = "";
    public double AverageExecutionTimeMs { get; init; }
    public double AverageOptimizationTimeMs { get; init; }
    public long MinExecutionTimeMs { get; init; }
    public long MaxExecutionTimeMs { get; init; }
    public int Iterations { get; init; }
}

public record AdaptiveBenchmarkAnalysis
{
    public int TotalTestCases { get; init; }
    public int SuccessfulTestCases { get; init; }
    public double AverageImprovementPercentage { get; init; }
    public double AverageConfidenceImprovement { get; init; }
    public double AverageOptimizationTimeMs { get; init; }
    public double AverageExecutionTimeMs { get; init; }
    public Dictionary<string, int> OptimizationStrategies { get; init; } = [];
}

public record QualitySpecificAnalysis
{
    public ImageQualityLevel QualityLevel { get; init; }
    public double SuccessRate { get; init; }
    public double AverageConfidence { get; init; }
    public double AverageOptimizationTime { get; init; }
    public string MostCommonStrategy { get; init; } = "";
}

public record PerformanceAnalysis
{
    public double AverageExecutionTime { get; init; }
    public double AverageOptimizationTime { get; init; }
    public double OptimizationOverhead { get; init; }
    public string FastestTestCase { get; init; } = "";
    public string SlowestTestCase { get; init; } = "";
}

#endregion
