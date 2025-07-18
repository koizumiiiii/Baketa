using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;

namespace Baketa.Infrastructure.OCR.Benchmarking;

/// <summary>
/// OCR精度とパフォーマンスのベンチマーク測定インターフェース
/// </summary>
public interface IOcrBenchmark
{
    /// <summary>
    /// OCR処理の精度とパフォーマンスを測定
    /// </summary>
    Task<BenchmarkResult> MeasureAsync(string testName, IImage testImage, string expectedText, IOcrEngine ocrEngine);
    
    /// <summary>
    /// 複数のテストケースでベンチマークを実行
    /// </summary>
    Task<BenchmarkSummary> RunBenchmarkSuiteAsync(string suiteName, IEnumerable<TestCase> testCases, IOcrEngine ocrEngine);
    
    /// <summary>
    /// 2つのOCRエンジンの性能を比較
    /// </summary>
    Task<ComparisonResult> CompareEnginesAsync(string testName, IImage testImage, string expectedText, 
        IOcrEngine baselineEngine, IOcrEngine improvedEngine);
}

/// <summary>
/// テストケース
/// </summary>
public record TestCase(string Name, IImage Image, string ExpectedText);

/// <summary>
/// 単一ベンチマーク結果
/// </summary>
public record BenchmarkResult(
    string TestName,
    string RecognizedText,
    double AccuracyScore,
    TimeSpan ProcessingTime,
    int CharacterCount,
    int CorrectCharacters,
    int IncorrectCharacters,
    IReadOnlyList<string> ErrorDetails);

/// <summary>
/// ベンチマーク概要
/// </summary>
public record BenchmarkSummary(
    string SuiteName,
    IReadOnlyList<BenchmarkResult> Results,
    double AverageAccuracy,
    TimeSpan AverageProcessingTime,
    double TotalAccuracy,
    TimeSpan TotalProcessingTime);

/// <summary>
/// エンジン比較結果
/// </summary>
public record ComparisonResult(
    string TestName,
    BenchmarkResult BaselineResult,
    BenchmarkResult ImprovedResult,
    double AccuracyImprovement,
    TimeSpan ProcessingTimeChange,
    string ImprovementSummary);