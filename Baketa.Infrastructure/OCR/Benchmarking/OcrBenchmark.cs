using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Benchmarking;

/// <summary>
/// OCR精度とパフォーマンスのベンチマーク測定実装
/// </summary>
public class OcrBenchmark(ILogger<OcrBenchmark> logger) : IOcrBenchmark
{
    private readonly ILogger<OcrBenchmark> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// OCR処理の精度とパフォーマンスを測定
    /// </summary>
    public async Task<BenchmarkResult> MeasureAsync(string testName, IImage testImage, string expectedText, IOcrEngine ocrEngine)
    {
        _logger.LogInformation("ベンチマーク開始: {TestName}", testName);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // OCR処理実行
            var ocrResult = await ocrEngine.RecognizeAsync(testImage).ConfigureAwait(false);
            stopwatch.Stop();

            var recognizedText = ocrResult.Text;

            // 精度計算
            var accuracyScore = CalculateAccuracy(expectedText, recognizedText);
            var (correctChars, incorrectChars, errorDetails) = AnalyzeErrors(expectedText, recognizedText);

            var result = new BenchmarkResult(
                testName,
                recognizedText,
                accuracyScore,
                stopwatch.Elapsed,
                expectedText.Length,
                correctChars,
                incorrectChars,
                errorDetails);

            _logger.LogInformation("ベンチマーク完了: {TestName} - 精度: {Accuracy:F2}%, 処理時間: {ProcessingTime}ms",
                testName, accuracyScore * 100, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "ベンチマーク中にエラーが発生: {TestName}", testName);

            return new BenchmarkResult(
                testName,
                $"エラー: {ex.Message}",
                0.0,
                stopwatch.Elapsed,
                expectedText.Length,
                0,
                expectedText.Length,
                [$"例外: {ex.Message}"]);
        }
    }

    /// <summary>
    /// 複数のテストケースでベンチマークを実行
    /// </summary>
    public async Task<BenchmarkSummary> RunBenchmarkSuiteAsync(string suiteName, IEnumerable<TestCase> testCases, IOcrEngine ocrEngine)
    {
        _logger.LogInformation("ベンチマークスイート開始: {SuiteName}", suiteName);

        var results = new List<BenchmarkResult>();
        var totalStopwatch = Stopwatch.StartNew();

        foreach (var testCase in testCases)
        {
            var result = await MeasureAsync(testCase.Name, testCase.Image, testCase.ExpectedText, ocrEngine).ConfigureAwait(false);
            results.Add(result);
        }

        totalStopwatch.Stop();

        var averageAccuracy = results.Average(r => r.AccuracyScore);
        var averageProcessingTime = TimeSpan.FromMilliseconds(results.Average(r => r.ProcessingTime.TotalMilliseconds));
        var totalAccuracy = results.Sum(r => r.CorrectCharacters) / (double)results.Sum(r => r.CharacterCount);

        var summary = new BenchmarkSummary(
            suiteName,
            results,
            averageAccuracy,
            averageProcessingTime,
            totalAccuracy,
            totalStopwatch.Elapsed);

        _logger.LogInformation("ベンチマークスイート完了: {SuiteName} - 平均精度: {AverageAccuracy:F2}%, 総合精度: {TotalAccuracy:F2}%",
            suiteName, averageAccuracy * 100, totalAccuracy * 100);

        return summary;
    }

    /// <summary>
    /// 2つのOCRエンジンの性能を比較
    /// </summary>
    public async Task<ComparisonResult> CompareEnginesAsync(string testName, IImage testImage, string expectedText,
        IOcrEngine baselineEngine, IOcrEngine improvedEngine)
    {
        _logger.LogInformation("OCRエンジン比較開始: {TestName}", testName);

        var baselineTask = MeasureAsync($"{testName}_Baseline", testImage, expectedText, baselineEngine);
        var improvedTask = MeasureAsync($"{testName}_Improved", testImage, expectedText, improvedEngine);

        var results = await Task.WhenAll(baselineTask, improvedTask).ConfigureAwait(false);
        var baselineResult = results[0];
        var improvedResult = results[1];

        var accuracyImprovement = improvedResult.AccuracyScore - baselineResult.AccuracyScore;
        var processingTimeChange = improvedResult.ProcessingTime - baselineResult.ProcessingTime;

        var improvementSummary = GenerateImprovementSummary(accuracyImprovement, processingTimeChange);

        var comparisonResult = new ComparisonResult(
            testName,
            baselineResult,
            improvedResult,
            accuracyImprovement,
            processingTimeChange,
            improvementSummary);

        _logger.LogInformation("OCRエンジン比較完了: {TestName} - 精度改善: {AccuracyImprovement:F2}%, 処理時間変化: {ProcessingTimeChange}ms",
            testName, accuracyImprovement * 100, processingTimeChange.TotalMilliseconds);

        return comparisonResult;
    }

    /// <summary>
    /// 文字レベルの精度を計算
    /// </summary>
    private double CalculateAccuracy(string expected, string actual)
    {
        if (string.IsNullOrEmpty(expected) && string.IsNullOrEmpty(actual))
            return 1.0;

        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual))
            return 0.0;

        // レーベンシュタイン距離を使用した精度計算
        var distance = CalculateLevenshteinDistance(expected, actual);
        var maxLength = Math.Max(expected.Length, actual.Length);

        return Math.Max(0.0, 1.0 - (double)distance / maxLength);
    }

    /// <summary>
    /// レーベンシュタイン距離を計算
    /// </summary>
    private int CalculateLevenshteinDistance(string s1, string s2)
    {
        var matrix = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= s2.Length; j++)
            matrix[0, j] = j;

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

    /// <summary>
    /// エラー詳細を分析
    /// </summary>
    private (int correctChars, int incorrectChars, IReadOnlyList<string> errorDetails) AnalyzeErrors(string expected, string actual)
    {
        var errors = new List<string>();
        var correctChars = 0;
        var incorrectChars = 0;

        var minLength = Math.Min(expected.Length, actual.Length);

        // 文字単位で比較
        for (int i = 0; i < minLength; i++)
        {
            if (expected[i] == actual[i])
            {
                correctChars++;
            }
            else
            {
                incorrectChars++;
                errors.Add($"位置{i}: 期待「{expected[i]}」→実際「{actual[i]}」");
            }
        }

        // 長さの違いを処理
        if (expected.Length > actual.Length)
        {
            incorrectChars += expected.Length - actual.Length;
            errors.Add($"文字不足: {expected.Length - actual.Length}文字");
        }
        else if (actual.Length > expected.Length)
        {
            incorrectChars += actual.Length - expected.Length;
            errors.Add($"文字過多: {actual.Length - expected.Length}文字");
        }

        return (correctChars, incorrectChars, errors);
    }

    /// <summary>
    /// 改善概要を生成
    /// </summary>
    private string GenerateImprovementSummary(double accuracyImprovement, TimeSpan processingTimeChange)
    {
        var summary = new List<string>();

        if (accuracyImprovement > 0.01)
            summary.Add($"精度向上: +{accuracyImprovement * 100:F2}%");
        else if (accuracyImprovement < -0.01)
            summary.Add($"精度低下: {accuracyImprovement * 100:F2}%");
        else
            summary.Add("精度変化なし");

        if (processingTimeChange.TotalMilliseconds > 10)
            summary.Add($"処理時間増加: +{processingTimeChange.TotalMilliseconds:F1}ms");
        else if (processingTimeChange.TotalMilliseconds < -10)
            summary.Add($"処理時間短縮: {processingTimeChange.TotalMilliseconds:F1}ms");
        else
            summary.Add("処理時間変化なし");

        return string.Join(", ", summary);
    }
}
