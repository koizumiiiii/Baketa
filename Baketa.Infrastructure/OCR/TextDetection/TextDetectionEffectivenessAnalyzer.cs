using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.OCR.TextDetection;
using Baketa.Core.Extensions;
using Microsoft.Extensions.Logging;
using Rectangle = System.Drawing.Rectangle;
using TextDetectionMethodAlias = Baketa.Core.Abstractions.OCR.TextDetection.TextDetectionMethod;
using TextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;

namespace Baketa.Infrastructure.OCR.TextDetection;

/// <summary>
/// テキスト領域検出高度化の効果測定・分析システム
/// 検出精度、処理速度、OCR結果品質の包括的評価
/// </summary>
public sealed class TextDetectionEffectivenessAnalyzer : IDisposable
{
    private readonly ILogger<TextDetectionEffectivenessAnalyzer> _logger;
    private readonly Dictionary<string, ITextRegionDetector> _detectors;
    private readonly List<EffectivenessMeasurement> _measurements = [];

    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private bool _disposed;

    public TextDetectionEffectivenessAnalyzer(
        ILogger<TextDetectionEffectivenessAnalyzer> logger,
        IEnumerable<ITextRegionDetector> detectors)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _detectors = detectors.ToDictionary(d => d.Name.ToLowerInvariant(), d => d);

        _logger.LogInformation("テキスト領域検出効果測定システム初期化: 対象検出器数={DetectorCount}", _detectors.Count);
    }

    /// <summary>
    /// 包括的効果測定の実行
    /// </summary>
    public async Task<EffectivenessReport> MeasureComprehensiveEffectivenessAsync(
        List<TestCase> testCases,
        IOcrEngine? ocrEngine = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("包括的効果測定開始: テストケース数={TestCaseCount}", testCases.Count);

        var report = new EffectivenessReport
        {
            ExecutionTime = DateTime.Now,
            TestCaseCount = testCases.Count,
            DetectorCount = _detectors.Count
        };

        try
        {
            // 1. 検出精度測定
            report.DetectionAccuracyResults = await MeasureDetectionAccuracyAsync(testCases, cancellationToken).ConfigureAwait(false);

            // 2. 処理速度測定
            report.PerformanceResults = await MeasurePerformanceAsync(testCases, cancellationToken).ConfigureAwait(false);

            // 3. OCR結果品質測定（OCRエンジンが提供されている場合）
            if (ocrEngine != null)
            {
                report.OcrQualityResults = await MeasureOcrQualityAsync(testCases, ocrEngine, cancellationToken).ConfigureAwait(false);
            }

            // 4. 適応効果測定
            report.AdaptationEffectResults = await MeasureAdaptationEffectAsync(testCases, cancellationToken).ConfigureAwait(false);

            // 5. 総合分析
            report.ComprehensiveAnalysis = AnalyzeOverallEffectiveness(report);

            // 6. 改善提案生成
            report.ImprovementSuggestions = GenerateImprovementSuggestions(report);

            await SaveReportAsync(report).ConfigureAwait(false);

            _logger.LogInformation("包括的効果測定完了: 総合スコア={OverallScore:F2}",
                report.ComprehensiveAnalysis.OverallEffectivenessScore);

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "効果測定中にエラーが発生");
            throw;
        }
    }

    /// <summary>
    /// 検出精度測定
    /// Precision, Recall, F1-Scoreを計算
    /// </summary>
    private async Task<Dictionary<string, DetectionAccuracyMetrics>> MeasureDetectionAccuracyAsync(
        List<TestCase> testCases,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("検出精度測定開始");

        var results = new Dictionary<string, DetectionAccuracyMetrics>();

        foreach (var (detectorName, detector) in _detectors)
        {
            var metrics = new DetectionAccuracyMetrics { DetectorName = detectorName };

            foreach (var testCase in testCases)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var detectedRegions = await detector.DetectRegionsAsync(testCase.Image, cancellationToken).ConfigureAwait(false);

                    // Ground Truthとの比較
                    var accuracy = CalculateAccuracy(detectedRegions, testCase.GroundTruthRegions);
                    metrics.AccuracyMeasurements.Add(accuracy);

                    _logger.LogTrace("精度測定完了: {DetectorName}, ケース={TestCaseId}, Precision={Precision:F3}",
                        detectorName, testCase.Id, accuracy.Precision);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "検出精度測定エラー: {DetectorName}, ケース={TestCaseId}", detectorName, testCase.Id);
                    metrics.ErrorCount++;
                }
            }

            // 平均値計算
            if (metrics.AccuracyMeasurements.Count > 0)
            {
                metrics.AveragePrecision = metrics.AccuracyMeasurements.Average(a => a.Precision);
                metrics.AverageRecall = metrics.AccuracyMeasurements.Average(a => a.Recall);
                metrics.AverageF1Score = metrics.AccuracyMeasurements.Average(a => a.F1Score);
            }

            results[detectorName] = metrics;
        }

        _logger.LogDebug("検出精度測定完了: 検出器数={DetectorCount}", results.Count);
        return results;
    }

    /// <summary>
    /// 処理速度測定
    /// </summary>
    private async Task<Dictionary<string, PerformanceMetrics>> MeasurePerformanceAsync(
        List<TestCase> testCases,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("処理速度測定開始");

        var results = new Dictionary<string, PerformanceMetrics>();

        foreach (var (detectorName, detector) in _detectors)
        {
            var metrics = new PerformanceMetrics { DetectorName = detectorName };
            var processingTimes = new List<double>();

            foreach (var testCase in testCases)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    var regions = await detector.DetectRegionsAsync(testCase.Image, cancellationToken).ConfigureAwait(false);
                    stopwatch.Stop();

                    var processingTime = stopwatch.Elapsed.TotalMilliseconds;
                    processingTimes.Add(processingTime);

                    // ピクセル当たりの処理時間計算
                    var pixelCount = testCase.Image.Width * testCase.Image.Height;
                    var timePerPixel = processingTime / pixelCount;

                    metrics.DetailedMeasurements.Add(new PerformanceMeasurement
                    {
                        TestCaseId = testCase.Id,
                        ProcessingTimeMs = processingTime,
                        RegionCount = regions.Count,
                        ImageSize = new Size(testCase.Image.Width, testCase.Image.Height),
                        TimePerPixelMs = timePerPixel
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "処理速度測定エラー: {DetectorName}, ケース={TestCaseId}", detectorName, testCase.Id);
                }
            }

            // 統計値計算
            if (processingTimes.Count > 0)
            {
                metrics.AverageProcessingTimeMs = processingTimes.Average();
                metrics.MinProcessingTimeMs = processingTimes.Min();
                metrics.MaxProcessingTimeMs = processingTimes.Max();
                metrics.StandardDeviation = CalculateStandardDeviation(processingTimes);

                // パフォーマンス安定性指標（変動係数）
                metrics.StabilityIndex = metrics.StandardDeviation / metrics.AverageProcessingTimeMs;
            }

            results[detectorName] = metrics;
        }

        _logger.LogDebug("処理速度測定完了: 検出器数={DetectorCount}", results.Count);
        return results;
    }

    /// <summary>
    /// OCR結果品質測定
    /// </summary>
    private async Task<Dictionary<string, OcrQualityMetrics>> MeasureOcrQualityAsync(
        List<TestCase> testCases,
        IOcrEngine ocrEngine,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("OCR品質測定開始");

        var results = new Dictionary<string, OcrQualityMetrics>();

        foreach (var (detectorName, detector) in _detectors)
        {
            var metrics = new OcrQualityMetrics { DetectorName = detectorName };

            foreach (var testCase in testCases.Where(tc => !string.IsNullOrEmpty(tc.ExpectedText)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // テキスト領域検出
                    var regions = await detector.DetectRegionsAsync(testCase.Image, cancellationToken).ConfigureAwait(false);

                    var ocrResults = new List<string>();

                    // 各領域でOCR実行
                    foreach (var region in regions)
                    {
                        if (region.ProcessedImage != null)
                        {
                            // IOcrEngineの正しいメソッドを使用（仮実装）
                            var ocrResults2 = await ocrEngine.RecognizeAsync(region.ProcessedImage, null, cancellationToken).ConfigureAwait(false);
                            ocrResults.Add(ocrResults2.Text ?? string.Empty);
                        }
                    }

                    // OCR結果の品質評価
                    var combinedResult = string.Join(" ", ocrResults);
                    var quality = CalculateTextSimilarity(combinedResult, testCase.ExpectedText);

                    metrics.QualityMeasurements.Add(new OcrQualityMeasurement
                    {
                        TestCaseId = testCase.Id,
                        RecognizedText = combinedResult,
                        ExpectedText = testCase.ExpectedText,
                        SimilarityScore = quality.SimilarityScore,
                        CharacterAccuracy = quality.CharacterAccuracy,
                        WordAccuracy = quality.WordAccuracy
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "OCR品質測定エラー: {DetectorName}, ケース={TestCaseId}", detectorName, testCase.Id);
                }
            }

            // 平均品質スコア計算
            if (metrics.QualityMeasurements.Count > 0)
            {
                metrics.AverageSimilarityScore = metrics.QualityMeasurements.Average(q => q.SimilarityScore);
                metrics.AverageCharacterAccuracy = metrics.QualityMeasurements.Average(q => q.CharacterAccuracy);
                metrics.AverageWordAccuracy = metrics.QualityMeasurements.Average(q => q.WordAccuracy);
            }

            results[detectorName] = metrics;
        }

        _logger.LogDebug("OCR品質測定完了: 検出器数={DetectorCount}", results.Count);
        return results;
    }

    /// <summary>
    /// 適応効果測定
    /// 同じ検出器を複数回実行して学習効果を測定
    /// </summary>
    private async Task<Dictionary<string, AdaptationEffectMetrics>> MeasureAdaptationEffectAsync(
        List<TestCase> testCases,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("適応効果測定開始");

        var results = new Dictionary<string, AdaptationEffectMetrics>();

        // 適応的検出器のみを対象とする
        var adaptiveDetectors = _detectors.Where(d => d.Value.Method == TextDetectionMethodAlias.Adaptive);

        foreach (var kvp in adaptiveDetectors)
        {
            var detectorName = kvp.Key;
            var detector = kvp.Value;
            var metrics = new AdaptationEffectMetrics { DetectorName = detectorName };

            // 複数回実行して学習効果を測定
            const int iterations = 10;
            var iterationResults = new List<IterationResult>();

            for (int iteration = 1; iteration <= iterations; iteration++)
            {
                var iterationResult = new IterationResult { Iteration = iteration };
                var processingTimes = new List<double>();
                var regionCounts = new List<int>();

                foreach (var testCase in testCases)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var stopwatch = Stopwatch.StartNew();
                        var regions = await detector.DetectRegionsAsync(testCase.Image, cancellationToken).ConfigureAwait(false);
                        stopwatch.Stop();

                        processingTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
                        regionCounts.Add(regions.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "適応効果測定エラー: {DetectorName}, 反復={Iteration}", detectorName, iteration);
                    }
                }

                if (processingTimes.Count > 0)
                {
                    iterationResult.AverageProcessingTime = processingTimes.Average();
                    iterationResult.AverageRegionCount = regionCounts.Average();
                    iterationResult.ProcessingTimeStability = CalculateStandardDeviation(processingTimes);
                }

                iterationResults.Add(iterationResult);

                // 適応間隔を考慮した待機
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }

            // 適応効果の分析
            if (iterationResults.Count > 1)
            {
                var firstHalf = iterationResults.Take(iterations / 2).ToList();
                var secondHalf = iterationResults.Skip(iterations / 2).ToList();

                metrics.InitialAverageTime = firstHalf.Average(r => r.AverageProcessingTime);
                metrics.FinalAverageTime = secondHalf.Average(r => r.AverageProcessingTime);
                metrics.TimeImprovementPercent = ((metrics.InitialAverageTime - metrics.FinalAverageTime) / metrics.InitialAverageTime) * 100;

                metrics.InitialRegionCount = firstHalf.Average(r => r.AverageRegionCount);
                metrics.FinalRegionCount = secondHalf.Average(r => r.AverageRegionCount);

                metrics.StabilityImprovement = firstHalf.Average(r => r.ProcessingTimeStability) -
                                             secondHalf.Average(r => r.ProcessingTimeStability);
            }

            metrics.IterationResults = iterationResults;
            results[detectorName] = metrics;
        }

        _logger.LogDebug("適応効果測定完了: 適応検出器数={AdaptiveDetectorCount}", results.Count);
        return results;
    }

    /// <summary>
    /// 総合効果分析
    /// </summary>
    private ComprehensiveAnalysis AnalyzeOverallEffectiveness(EffectivenessReport report)
    {
        var analysis = new ComprehensiveAnalysis();

        // 最高性能検出器の特定
        if (report.PerformanceResults.Count > 0)
        {
            var fastestDetector = report.PerformanceResults
                .OrderBy(p => p.Value.AverageProcessingTimeMs)
                .First();
            analysis.FastestDetector = fastestDetector.Key;
            analysis.FastestDetectorTime = fastestDetector.Value.AverageProcessingTimeMs;
        }

        // 最高精度検出器の特定
        if (report.DetectionAccuracyResults.Count > 0)
        {
            var mostAccurateDetector = report.DetectionAccuracyResults
                .OrderByDescending(a => a.Value.AverageF1Score)
                .First();
            analysis.MostAccurateDetector = mostAccurateDetector.Key;
            analysis.HighestF1Score = mostAccurateDetector.Value.AverageF1Score;
        }

        // 適応効果の評価
        if (report.AdaptationEffectResults.Count > 0)
        {
            var bestAdaptation = report.AdaptationEffectResults
                .OrderByDescending(a => a.Value.TimeImprovementPercent)
                .FirstOrDefault();

            if (bestAdaptation.Value != null)
            {
                analysis.BestAdaptiveDetector = bestAdaptation.Key;
                analysis.MaxAdaptationImprovement = bestAdaptation.Value.TimeImprovementPercent;
            }
        }

        // 総合効果スコア計算（精度40% + 速度30% + 適応性30%）
        analysis.OverallEffectivenessScore = CalculateOverallScore(report);

        // 効果の評価レベル
        analysis.EffectivenessLevel = analysis.OverallEffectivenessScore switch
        {
            >= 0.9 => "Excellent",
            >= 0.8 => "Very Good",
            >= 0.7 => "Good",
            >= 0.6 => "Fair",
            _ => "Needs Improvement"
        };

        return analysis;
    }

    /// <summary>
    /// 改善提案生成
    /// </summary>
    private List<string> GenerateImprovementSuggestions(EffectivenessReport report)
    {
        var suggestions = new List<string>();

        // 精度改善提案
        if (report.DetectionAccuracyResults.Count > 0)
        {
            var avgF1 = report.DetectionAccuracyResults.Average(r => r.Value.AverageF1Score);
            if (avgF1 < 0.8)
            {
                suggestions.Add($"検出精度改善が必要: 平均F1スコア={avgF1:F3} < 0.8。パラメータ調整または訓練データ追加を検討");
            }
        }

        // 速度改善提案
        if (report.PerformanceResults.Count > 0)
        {
            var avgTime = report.PerformanceResults.Average(r => r.Value.AverageProcessingTimeMs);
            if (avgTime > 1000)
            {
                suggestions.Add($"処理速度改善が必要: 平均処理時間={avgTime:F1}ms > 1000ms。並列処理またはアルゴリズム最適化を検討");
            }
        }

        // 適応効果改善提案
        if (report.AdaptationEffectResults.Count > 0)
        {
            var avgImprovement = report.AdaptationEffectResults.Average(r => r.Value.TimeImprovementPercent);
            if (avgImprovement < 10)
            {
                suggestions.Add($"適応効果が限定的: 平均改善率={avgImprovement:F1}% < 10%。学習アルゴリズムの見直しが必要");
            }
        }

        // OCR品質改善提案
        if (report.OcrQualityResults?.Count > 0)
        {
            var avgAccuracy = report.OcrQualityResults.Average(r => r.Value.AverageCharacterAccuracy);
            if (avgAccuracy < 0.9)
            {
                suggestions.Add($"OCR品質改善が必要: 平均文字精度={avgAccuracy:F3} < 0.9。前処理フィルターの追加を検討");
            }
        }

        // 安定性改善提案
        var unstableDetectors = report.PerformanceResults
            .Where(r => r.Value.StabilityIndex > 0.3)
            .Select(r => r.Key)
            .ToList();

        if (unstableDetectors.Count > 0)
        {
            suggestions.Add($"処理時間が不安定な検出器: {string.Join(", ", unstableDetectors)}。パラメータ固定または初期化改善を検討");
        }

        return suggestions;
    }

    #region Helper Methods

    private AccuracyMeasurement CalculateAccuracy(IReadOnlyList<TextRegion> detected, List<Rectangle> groundTruth)
    {
        if (groundTruth.Count == 0)
            return new AccuracyMeasurement { Precision = 1.0, Recall = 1.0, F1Score = 1.0 };

        var truePositives = 0;
        var overlapThreshold = 0.5;

        foreach (var detectedRegion in detected)
        {
            foreach (var gtRegion in groundTruth)
            {
                var overlap = CalculateOverlap(detectedRegion.Bounds, gtRegion);
                if (overlap >= overlapThreshold)
                {
                    truePositives++;
                    break;
                }
            }
        }

        var precision = detected.Count > 0 ? (double)truePositives / detected.Count : 0;
        var recall = (double)truePositives / groundTruth.Count;
        var f1Score = precision + recall > 0 ? 2 * (precision * recall) / (precision + recall) : 0;

        return new AccuracyMeasurement
        {
            Precision = precision,
            Recall = recall,
            F1Score = f1Score,
            TruePositives = truePositives,
            FalsePositives = detected.Count - truePositives,
            FalseNegatives = groundTruth.Count - truePositives
        };
    }

    private double CalculateOverlap(Rectangle rect1, Rectangle rect2)
    {
        var intersection = Rectangle.Intersect(rect1, rect2);
        if (intersection.IsEmpty) return 0.0;

        var unionArea = rect1.Width * rect1.Height + rect2.Width * rect2.Height - intersection.Width * intersection.Height;
        return unionArea > 0 ? (double)(intersection.Width * intersection.Height) / unionArea : 0.0;
    }

    private TextSimilarity CalculateTextSimilarity(string recognized, string expected)
    {
        if (string.IsNullOrEmpty(expected))
            return new TextSimilarity { SimilarityScore = 1.0, CharacterAccuracy = 1.0, WordAccuracy = 1.0 };

        // 文字レベル精度（編集距離ベース）
        var charAccuracy = 1.0 - (double)CalculateLevenshteinDistance(recognized, expected) / Math.Max(recognized.Length, expected.Length);

        // 単語レベル精度
        var recognizedWords = recognized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var expectedWords = expected.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matchingWords = recognizedWords.Intersect(expectedWords).Count();
        var wordAccuracy = expectedWords.Length > 0 ? (double)matchingWords / expectedWords.Length : 1.0;

        // 総合類似度スコア
        var similarityScore = (charAccuracy * 0.7 + wordAccuracy * 0.3);

        return new TextSimilarity
        {
            SimilarityScore = similarityScore,
            CharacterAccuracy = charAccuracy,
            WordAccuracy = wordAccuracy
        };
    }

    private int CalculateLevenshteinDistance(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
        if (string.IsNullOrEmpty(s2)) return s1.Length;

        var matrix = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++) matrix[i, 0] = i;
        for (int j = 0; j <= s2.Length; j++) matrix[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[s1.Length, s2.Length];
    }

    private double CalculateStandardDeviation(List<double> values)
    {
        if (values.Count <= 1) return 0;
        var mean = values.Average();
        var sumOfSquares = values.Sum(x => Math.Pow(x - mean, 2));
        return Math.Sqrt(sumOfSquares / (values.Count - 1));
    }

    private double CalculateOverallScore(EffectivenessReport report)
    {
        var accuracyScore = report.DetectionAccuracyResults.Count > 0
            ? report.DetectionAccuracyResults.Average(r => r.Value.AverageF1Score)
            : 0.0;

        var speedScore = report.PerformanceResults.Count > 0
            ? Math.Max(0, (2000 - report.PerformanceResults.Average(r => r.Value.AverageProcessingTimeMs)) / 2000)
            : 0.0;

        var adaptationScore = report.AdaptationEffectResults.Count > 0
            ? Math.Max(0, report.AdaptationEffectResults.Average(r => r.Value.TimeImprovementPercent) / 50.0)
            : 0.0;

        return accuracyScore * 0.4 + speedScore * 0.3 + adaptationScore * 0.3;
    }

    private async Task SaveReportAsync(EffectivenessReport report)
    {
        try
        {
            var reportDir = "effectiveness_reports";
            Directory.CreateDirectory(reportDir);

            var fileName = $"text_detection_effectiveness_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var filePath = Path.Combine(reportDir, fileName);

            var json = JsonSerializer.Serialize(report, ReportJsonOptions);
            await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);

            _logger.LogInformation("効果測定レポート保存完了: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "効果測定レポート保存エラー");
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _logger.LogInformation("テキスト領域検出効果測定システムをクリーンアップ");

        GC.SuppressFinalize(this);
    }
}

#region Data Models

/// <summary>
/// テストケース
/// </summary>
public class TestCase
{
    public string Id { get; set; } = string.Empty;
    public IAdvancedImage Image { get; set; } = null!;
    public List<Rectangle> GroundTruthRegions { get; set; } = [];
    public string ExpectedText { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = [];
}

/// <summary>
/// 効果測定レポート
/// </summary>
public class EffectivenessReport
{
    public DateTime ExecutionTime { get; set; }
    public int TestCaseCount { get; set; }
    public int DetectorCount { get; set; }

    public Dictionary<string, DetectionAccuracyMetrics> DetectionAccuracyResults { get; set; } = [];
    public Dictionary<string, PerformanceMetrics> PerformanceResults { get; set; } = [];
    public Dictionary<string, OcrQualityMetrics>? OcrQualityResults { get; set; }
    public Dictionary<string, AdaptationEffectMetrics> AdaptationEffectResults { get; set; } = [];

    public ComprehensiveAnalysis ComprehensiveAnalysis { get; set; } = new();
    public List<string> ImprovementSuggestions { get; set; } = [];
}

/// <summary>
/// 検出精度指標
/// </summary>
public class DetectionAccuracyMetrics
{
    public string DetectorName { get; set; } = string.Empty;
    public List<AccuracyMeasurement> AccuracyMeasurements { get; set; } = [];
    public double AveragePrecision { get; set; }
    public double AverageRecall { get; set; }
    public double AverageF1Score { get; set; }
    public int ErrorCount { get; set; }
}

/// <summary>
/// 精度測定結果
/// </summary>
public class AccuracyMeasurement
{
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double F1Score { get; set; }
    public int TruePositives { get; set; }
    public int FalsePositives { get; set; }
    public int FalseNegatives { get; set; }
}

/// <summary>
/// パフォーマンス指標
/// </summary>
public class PerformanceMetrics
{
    public string DetectorName { get; set; } = string.Empty;
    public double AverageProcessingTimeMs { get; set; }
    public double MinProcessingTimeMs { get; set; }
    public double MaxProcessingTimeMs { get; set; }
    public double StandardDeviation { get; set; }
    public double StabilityIndex { get; set; }
    public List<PerformanceMeasurement> DetailedMeasurements { get; set; } = [];
}

/// <summary>
/// パフォーマンス測定詳細
/// </summary>
public class PerformanceMeasurement
{
    public string TestCaseId { get; set; } = string.Empty;
    public double ProcessingTimeMs { get; set; }
    public int RegionCount { get; set; }
    public Size ImageSize { get; set; }
    public double TimePerPixelMs { get; set; }
}

/// <summary>
/// OCR品質指標
/// </summary>
public class OcrQualityMetrics
{
    public string DetectorName { get; set; } = string.Empty;
    public List<OcrQualityMeasurement> QualityMeasurements { get; set; } = [];
    public double AverageSimilarityScore { get; set; }
    public double AverageCharacterAccuracy { get; set; }
    public double AverageWordAccuracy { get; set; }
}

/// <summary>
/// OCR品質測定結果
/// </summary>
public class OcrQualityMeasurement
{
    public string TestCaseId { get; set; } = string.Empty;
    public string RecognizedText { get; set; } = string.Empty;
    public string ExpectedText { get; set; } = string.Empty;
    public double SimilarityScore { get; set; }
    public double CharacterAccuracy { get; set; }
    public double WordAccuracy { get; set; }
}

/// <summary>
/// テキスト類似度
/// </summary>
public class TextSimilarity
{
    public double SimilarityScore { get; set; }
    public double CharacterAccuracy { get; set; }
    public double WordAccuracy { get; set; }
}

/// <summary>
/// 適応効果指標
/// </summary>
public class AdaptationEffectMetrics
{
    public string DetectorName { get; set; } = string.Empty;
    public double InitialAverageTime { get; set; }
    public double FinalAverageTime { get; set; }
    public double TimeImprovementPercent { get; set; }
    public double InitialRegionCount { get; set; }
    public double FinalRegionCount { get; set; }
    public double StabilityImprovement { get; set; }
    public List<IterationResult> IterationResults { get; set; } = [];
}

/// <summary>
/// 反復結果
/// </summary>
public class IterationResult
{
    public int Iteration { get; set; }
    public double AverageProcessingTime { get; set; }
    public double AverageRegionCount { get; set; }
    public double ProcessingTimeStability { get; set; }
}

/// <summary>
/// 総合分析結果
/// </summary>
public class ComprehensiveAnalysis
{
    public string FastestDetector { get; set; } = string.Empty;
    public double FastestDetectorTime { get; set; }
    public string MostAccurateDetector { get; set; } = string.Empty;
    public double HighestF1Score { get; set; }
    public string BestAdaptiveDetector { get; set; } = string.Empty;
    public double MaxAdaptationImprovement { get; set; }
    public double OverallEffectivenessScore { get; set; }
    public string EffectivenessLevel { get; set; } = string.Empty;
}

/// <summary>
/// 効果測定データポイント
/// </summary>
public class EffectivenessMeasurement
{
    public DateTime Timestamp { get; set; }
    public string DetectorName { get; set; } = string.Empty;
    public string TestCaseId { get; set; } = string.Empty;
    public double ProcessingTimeMs { get; set; }
    public double AccuracyScore { get; set; }
    public int RegionCount { get; set; }
}

#endregion
