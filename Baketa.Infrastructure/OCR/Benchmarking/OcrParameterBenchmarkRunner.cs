using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.Imaging;
using Baketa.Infrastructure.OCR.PaddleOCR.Enhancement;
using Microsoft.Extensions.Logging;
using Sdcb.PaddleOCR;

namespace Baketa.Infrastructure.OCR.Benchmarking;

/// <summary>
/// OCRパラメータの最適化効果を測定するベンチマークランナー
/// </summary>
public sealed class OcrParameterBenchmarkRunner(
    IOcrBenchmark benchmark,
    AdvancedPaddleOcrOptimizer optimizer,
    ILogger<OcrParameterBenchmarkRunner> logger)
{
    private readonly IOcrBenchmark _benchmark = benchmark ?? throw new ArgumentNullException(nameof(benchmark));
    private readonly AdvancedPaddleOcrOptimizer _optimizer = optimizer ?? throw new ArgumentNullException(nameof(optimizer));
    private readonly ILogger<OcrParameterBenchmarkRunner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Phase 1: PaddleOCRパラメータ最適化の効果測定
    /// </summary>
    public async Task<ParameterOptimizationResult> RunParameterOptimizationBenchmarkAsync(
        IOcrEngine baselineEngine,
        IEnumerable<TestCase> testCases)
    {
        _logger.LogInformation("PaddleOCRパラメータ最適化ベンチマーク開始");

        var results = new List<OptimizationMethodResult>();

        // ベースライン測定
        var baselineResults = await MeasureOptimizationMethodAsync(
            "ベースライン（デフォルト設定）",
            baselineEngine,
            testCases,
            null).ConfigureAwait(false);
        results.Add(baselineResults);

        // 各最適化手法の測定
        var optimizationMethods = new[]
        {
            ("小さい文字最適化", (Action<PaddleOcrAll>)_optimizer.ApplySmallTextOptimization),
            ("高精度処理最適化", (Action<PaddleOcrAll>)_optimizer.ApplyHighPrecisionOptimization),
            ("高速処理最適化", (Action<PaddleOcrAll>)_optimizer.ApplyFastProcessingOptimization),
            ("日本語特化最適化", (Action<PaddleOcrAll>)_optimizer.ApplyJapaneseOptimization)
        };

        foreach (var (methodName, optimizationMethod) in optimizationMethods)
        {
            var result = await MeasureOptimizationMethodAsync(
                methodName,
                baselineEngine,
                testCases,
                optimizationMethod).ConfigureAwait(false);
            results.Add(result);
        }

        // 結果分析
        var bestMethod = results.OrderByDescending(r => r.AverageAccuracy).First();
        var improvementSummary = GenerateImprovementSummary(baselineResults, results.Skip(1));

        var finalResult = new ParameterOptimizationResult(
            results,
            bestMethod,
            improvementSummary);

        _logger.LogInformation("PaddleOCRパラメータ最適化ベンチマーク完了 - 最適手法: {BestMethod}", bestMethod.MethodName);

        return finalResult;
    }

    /// <summary>
    /// 特定の最適化手法の効果を測定
    /// </summary>
    private async Task<OptimizationMethodResult> MeasureOptimizationMethodAsync(
        string methodName,
        IOcrEngine ocrEngine,
        IEnumerable<TestCase> testCases,
        Action<PaddleOcrAll>? optimizationMethod)
    {
        _logger.LogInformation("最適化手法測定開始: {MethodName}", methodName);

        // PaddleOcrAllインスタンスの取得（リフレクション使用）
        var paddleOcrAll = GetPaddleOcrAllInstance(ocrEngine);

        // 最適化パラメータの適用
        if (optimizationMethod != null && paddleOcrAll != null)
        {
            try
            {
                optimizationMethod(paddleOcrAll);
                _logger.LogInformation("最適化パラメータ適用完了: {MethodName}", methodName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "最適化パラメータ適用エラー: {MethodName}", methodName);
            }
        }

        // ベンチマーク実行
        var benchmarkResult = await _benchmark.RunBenchmarkSuiteAsync(
            $"OCR_Parameter_Optimization_{methodName}",
            testCases,
            ocrEngine).ConfigureAwait(false);

        // 詳細分析
        var characterAccuracy = CalculateCharacterAccuracy(benchmarkResult.Results);
        var processingSpeed = CalculateProcessingSpeed(benchmarkResult.Results);
        var errorAnalysis = AnalyzeErrors(benchmarkResult.Results);

        return new OptimizationMethodResult(
            methodName,
            benchmarkResult.AverageAccuracy,
            benchmarkResult.TotalAccuracy,
            characterAccuracy,
            benchmarkResult.AverageProcessingTime,
            processingSpeed,
            benchmarkResult.Results.Count,
            errorAnalysis,
            benchmarkResult.Results);
    }

    /// <summary>
    /// OCRエンジンからPaddleOcrAllインスタンスを取得
    /// </summary>
    private PaddleOcrAll? GetPaddleOcrAllInstance(IOcrEngine ocrEngine)
    {
        try
        {
            // リフレクションを使用してPaddleOcrAllインスタンスを取得
            var engineType = ocrEngine.GetType();
            var paddleOcrField = engineType.GetField("_paddleOcrAll",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (paddleOcrField != null)
            {
                return paddleOcrField.GetValue(ocrEngine) as PaddleOcrAll;
            }

            // 他の可能性のあるフィールド名を試す
            var alternativeFields = new[] { "_ocrEngine", "_paddleOcr", "ocrEngine", "paddleOcr" };
            foreach (var fieldName in alternativeFields)
            {
                var field = engineType.GetField(fieldName,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    var value = field.GetValue(ocrEngine);
                    if (value is PaddleOcrAll paddleOcrAll)
                    {
                        return paddleOcrAll;
                    }
                }
            }

            _logger.LogWarning("PaddleOcrAllインスタンスが見つかりません");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PaddleOcrAllインスタンス取得エラー");
            return null;
        }
    }

    /// <summary>
    /// 文字レベルの精度を計算
    /// </summary>
    private double CalculateCharacterAccuracy(IReadOnlyList<BenchmarkResult> results)
    {
        var totalCharacters = results.Sum(r => r.CharacterCount);
        var totalCorrect = results.Sum(r => r.CorrectCharacters);
        return totalCharacters > 0 ? (double)totalCorrect / totalCharacters : 0.0;
    }

    /// <summary>
    /// 処理速度を計算（文字/秒）
    /// </summary>
    private double CalculateProcessingSpeed(IReadOnlyList<BenchmarkResult> results)
    {
        var totalCharacters = results.Sum(r => r.CharacterCount);
        var totalTime = results.Sum(r => r.ProcessingTime.TotalSeconds);
        return totalTime > 0 ? totalCharacters / totalTime : 0.0;
    }

    /// <summary>
    /// エラーパターンを分析
    /// </summary>
    private ErrorAnalysis AnalyzeErrors(IReadOnlyList<BenchmarkResult> results)
    {
        var allErrors = results.SelectMany(r => r.ErrorDetails).ToList();
        var commonErrors = allErrors.GroupBy(e => e)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        var totalErrors = allErrors.Count;
        var uniqueErrors = allErrors.Distinct().Count();

        return new ErrorAnalysis(totalErrors, uniqueErrors, commonErrors);
    }

    /// <summary>
    /// 改善概要を生成
    /// </summary>
    private string GenerateImprovementSummary(
        OptimizationMethodResult baseline,
        IEnumerable<OptimizationMethodResult> optimizedResults)
    {
        var improvements = optimizedResults.Select(result => new
        {
            Method = result.MethodName,
            AccuracyImprovement = result.AverageAccuracy - baseline.AverageAccuracy,
            SpeedChange = result.ProcessingSpeed - baseline.ProcessingSpeed,
            CharacterAccuracyImprovement = result.CharacterAccuracy - baseline.CharacterAccuracy
        }).OrderByDescending(i => i.AccuracyImprovement).ToList();

        var summary = new List<string>
        {
            $"ベースライン精度: {baseline.AverageAccuracy:F2}%"
        };

        foreach (var improvement in improvements)
        {
            var accuracyChange = improvement.AccuracyImprovement > 0 ?
                $"+{improvement.AccuracyImprovement * 100:F2}%" :
                $"{improvement.AccuracyImprovement * 100:F2}%";
            var speedChange = improvement.SpeedChange > 0 ?
                $"+{improvement.SpeedChange:F1}文字/秒" :
                $"{improvement.SpeedChange:F1}文字/秒";

            summary.Add($"{improvement.Method}: 精度{accuracyChange}, 速度{speedChange}");
        }

        return string.Join("\n", summary);
    }

    /// <summary>
    /// テスト用のサンプル画像を生成
    /// </summary>
    public static IEnumerable<TestCase> CreateSampleTestCases()
    {
        // 実際のテストケースは外部から提供されるが、デモ用のサンプルを作成
        var testCases = new List<TestCase>();

        // 日本語・英語混在テキストのサンプル
        var sampleTexts = new[]
        {
            "オンボーディング（魔法体験）の設計",
            "単体テスト",
            "EXPLAIN でボトルネック確認",
            "データベース接続エラー",
            "API応答時間の最適化",
            "ユーザー認証システム",
            "クラウドインフラ構築",
            "レスポンシブデザイン対応"
        };

        foreach (var text in sampleTexts)
        {
            // 実際の実装では、テキストから画像を生成するか、
            // 既存の画像ファイルを使用する
            // ここではプレースホルダーを使用
            var testCase = new TestCase(
                $"Sample_{text}",
                CreatePlaceholderImage(text),
                text);
            testCases.Add(testCase);
        }

        return testCases;
    }

    /// <summary>
    /// プレースホルダー画像を作成（実際の実装では実際の画像を使用）
    /// </summary>
    private static PlaceholderImage CreatePlaceholderImage(string text)
    {
        // 実際の実装では、テキストから画像を生成するか、
        // 既存の画像ファイルを読み込む
        // ここではプレースホルダーを返す
        return new PlaceholderImage(text);
    }
}

/// <summary>
/// プレースホルダー画像クラス
/// </summary>
public sealed class PlaceholderImage(string text) : IImage
{
    public int Width => 800;
    public int Height => 100;
    public ImageFormat Format => ImageFormat.Png;

    /// <summary>
    /// PixelFormat property for IImage extension
    /// </summary>
    public ImagePixelFormat PixelFormat => ImagePixelFormat.Rgba32;

    /// <summary>
    /// GetImageMemory method for IImage extension
    /// </summary>
    public ReadOnlyMemory<byte> GetImageMemory()
    {
        return new ReadOnlyMemory<byte>(Array.Empty<byte>());
    }

    /// <summary>
    /// 🔥 [PHASE5.2G-A] LockPixelData (PlaceholderImage is test-only, not supported)
    /// </summary>
    public PixelDataLock LockPixelData() => throw new NotSupportedException("PlaceholderImage does not support LockPixelData");

    public void Dispose() { }

    public Task<byte[]> ToByteArrayAsync()
    {
        // 実際の実装では画像データを返す
        return Task.FromResult(Array.Empty<byte>());
    }

    public IImage Clone()
    {
        return new PlaceholderImage(text);
    }

    public Task<IImage> ResizeAsync(int width, int height)
    {
        return Task.FromResult<IImage>(new PlaceholderImage(text));
    }
}

/// <summary>
/// パラメータ最適化結果
/// </summary>
public record ParameterOptimizationResult(
    IReadOnlyList<OptimizationMethodResult> Results,
    OptimizationMethodResult BestMethod,
    string ImprovementSummary);

/// <summary>
/// 最適化手法の結果
/// </summary>
public record OptimizationMethodResult(
    string MethodName,
    double AverageAccuracy,
    double TotalAccuracy,
    double CharacterAccuracy,
    TimeSpan AverageProcessingTime,
    double ProcessingSpeed,
    int TestCount,
    ErrorAnalysis ErrorAnalysis,
    IReadOnlyList<BenchmarkResult> DetailedResults);

/// <summary>
/// エラー分析結果
/// </summary>
public record ErrorAnalysis(
    int TotalErrors,
    int UniqueErrors,
    IReadOnlyDictionary<string, int> CommonErrors);
