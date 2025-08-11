using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Infrastructure.OCR.PostProcessing;
using Baketa.Infrastructure.OCR.PostProcessing.NgramModels;

namespace Baketa.Infrastructure.OCR.Benchmarking;

/// <summary>
/// N-gramベース後処理のベンチマーク実行クラス
/// </summary>
public class NgramPostProcessingBenchmark(
    ILogger<NgramPostProcessingBenchmark> logger,
    NgramTrainingService trainingService)
{
    private readonly ILogger<NgramPostProcessingBenchmark> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly NgramTrainingService _trainingService = trainingService ?? throw new ArgumentNullException(nameof(trainingService));

    /// <summary>
    /// N-gramベース後処理の包括的ベンチマーク
    /// </summary>
    public async Task<NgramBenchmarkResult> RunComprehensiveBenchmarkAsync()
    {
        _logger.LogInformation("N-gramベース後処理の包括的ベンチマーク開始");
        
        var results = new List<PostProcessingMethodResult>();
        
        // 1. 辞書ベースのみ
        var dictionaryResult = await BenchmarkDictionaryOnlyAsync().ConfigureAwait(false);
        results.Add(dictionaryResult);
        
        // 2. N-gramベースのみ
        var ngramResult = await BenchmarkNgramOnlyAsync().ConfigureAwait(false);
        results.Add(ngramResult);
        
        // 3. ハイブリッド (N-gram → Dictionary)
        var hybridNgramFirstResult = await BenchmarkHybridNgramFirstAsync().ConfigureAwait(false);
        results.Add(hybridNgramFirstResult);
        
        // 4. ハイブリッド (Dictionary → N-gram)
        var hybridDictionaryFirstResult = await BenchmarkHybridDictionaryFirstAsync().ConfigureAwait(false);
        results.Add(hybridDictionaryFirstResult);
        
        // 最適手法の決定
        var bestMethod = results.OrderByDescending(r => r.AccuracyScore).First();
        
        var benchmarkResult = new NgramBenchmarkResult(
            results,
            bestMethod,
            GenerateRecommendations(results));
        
        _logger.LogInformation("N-gramベース後処理ベンチマーク完了 - 最適手法: {BestMethod}", bestMethod.MethodName);
        
        return benchmarkResult;
    }
    
    /// <summary>
    /// 辞書ベースのみのベンチマーク
    /// </summary>
    private async Task<PostProcessingMethodResult> BenchmarkDictionaryOnlyAsync()
    {
        _logger.LogInformation("辞書ベースのみのベンチマーク開始");
        
        var dictionaryProcessor = new JapaneseOcrPostProcessor(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JapaneseOcrPostProcessor>.Instance);
        var testCases = GetTestCases();
        var results = new List<PostProcessingTestResult>();
        
        var startTime = DateTime.UtcNow;
        
        foreach (var testCase in testCases)
        {
            var processedText = await dictionaryProcessor.ProcessAsync(testCase.Input, 1.0f).ConfigureAwait(false);
            var accuracy = CalculateAccuracy(testCase.Expected, processedText);
            
            results.Add(new PostProcessingTestResult(
                testCase.Input,
                testCase.Expected,
                processedText,
                accuracy,
                testCase.Input != processedText));
        }
        
        var totalTime = DateTime.UtcNow - startTime;
        var averageAccuracy = results.Average(r => r.Accuracy);
        var correctionRate = results.Count(r => r.WasCorrected) / (double)results.Count;
        
        return new PostProcessingMethodResult(
            "辞書ベースのみ",
            averageAccuracy,
            correctionRate,
            totalTime,
            results);
    }
    
    /// <summary>
    /// N-gramベースのみのベンチマーク
    /// </summary>
    private async Task<PostProcessingMethodResult> BenchmarkNgramOnlyAsync()
    {
        _logger.LogInformation("N-gramベースのみのベンチマーク開始");
        
        var ngramModel = await _trainingService.LoadJapaneseBigramModelAsync().ConfigureAwait(false);
        var ngramProcessor = new NgramOcrPostProcessor(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NgramOcrPostProcessor>.Instance, 
            ngramModel);
        var testCases = GetTestCases();
        var results = new List<PostProcessingTestResult>();
        
        var startTime = DateTime.UtcNow;
        
        foreach (var testCase in testCases)
        {
            var processedText = await ngramProcessor.ProcessAsync(testCase.Input, 1.0f).ConfigureAwait(false);
            var accuracy = CalculateAccuracy(testCase.Expected, processedText);
            
            results.Add(new PostProcessingTestResult(
                testCase.Input,
                testCase.Expected,
                processedText,
                accuracy,
                testCase.Input != processedText));
        }
        
        var totalTime = DateTime.UtcNow - startTime;
        var averageAccuracy = results.Average(r => r.Accuracy);
        var correctionRate = results.Count(r => r.WasCorrected) / (double)results.Count;
        
        return new PostProcessingMethodResult(
            "N-gramベースのみ",
            averageAccuracy,
            correctionRate,
            totalTime,
            results);
    }
    
    /// <summary>
    /// ハイブリッド (N-gram → Dictionary) のベンチマーク
    /// </summary>
    private async Task<PostProcessingMethodResult> BenchmarkHybridNgramFirstAsync()
    {
        _logger.LogInformation("ハイブリッド (N-gram → Dictionary) のベンチマーク開始");
        
        var factory = new HybridOcrPostProcessorFactory(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HybridOcrPostProcessorFactory>.Instance, 
            _trainingService);
        var hybridProcessor = await factory.CreateAsync().ConfigureAwait(false);
        var testCases = GetTestCases();
        var results = new List<PostProcessingTestResult>();
        
        var startTime = DateTime.UtcNow;
        
        foreach (var testCase in testCases)
        {
            var processedText = await hybridProcessor.ProcessAsync(testCase.Input, 1.0f).ConfigureAwait(false);
            var accuracy = CalculateAccuracy(testCase.Expected, processedText);
            
            results.Add(new PostProcessingTestResult(
                testCase.Input,
                testCase.Expected,
                processedText,
                accuracy,
                testCase.Input != processedText));
        }
        
        var totalTime = DateTime.UtcNow - startTime;
        var averageAccuracy = results.Average(r => r.Accuracy);
        var correctionRate = results.Count(r => r.WasCorrected) / (double)results.Count;
        
        return new PostProcessingMethodResult(
            "ハイブリッド (N-gram → Dictionary)",
            averageAccuracy,
            correctionRate,
            totalTime,
            results);
    }
    
    /// <summary>
    /// ハイブリッド (Dictionary → N-gram) のベンチマーク
    /// </summary>
    private async Task<PostProcessingMethodResult> BenchmarkHybridDictionaryFirstAsync()
    {
        _logger.LogInformation("ハイブリッド (Dictionary → N-gram) のベンチマーク開始");
        
        var dictionaryProcessor = new JapaneseOcrPostProcessor(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JapaneseOcrPostProcessor>.Instance);
        var ngramModel = await _trainingService.LoadJapaneseBigramModelAsync().ConfigureAwait(false);
        var ngramProcessor = new NgramOcrPostProcessor(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NgramOcrPostProcessor>.Instance, 
            ngramModel);
        var hybridProcessor = new HybridOcrPostProcessor(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HybridOcrPostProcessor>.Instance, 
            dictionaryProcessor, 
            ngramProcessor, 
            useNgramFirst: false);
        
        var testCases = GetTestCases();
        var results = new List<PostProcessingTestResult>();
        
        var startTime = DateTime.UtcNow;
        
        foreach (var testCase in testCases)
        {
            var processedText = await hybridProcessor.ProcessAsync(testCase.Input, 1.0f).ConfigureAwait(false);
            var accuracy = CalculateAccuracy(testCase.Expected, processedText);
            
            results.Add(new PostProcessingTestResult(
                testCase.Input,
                testCase.Expected,
                processedText,
                accuracy,
                testCase.Input != processedText));
        }
        
        var totalTime = DateTime.UtcNow - startTime;
        var averageAccuracy = results.Average(r => r.Accuracy);
        var correctionRate = results.Count(r => r.WasCorrected) / (double)results.Count;
        
        return new PostProcessingMethodResult(
            "ハイブリッド (Dictionary → N-gram)",
            averageAccuracy,
            correctionRate,
            totalTime,
            results);
    }
    
    /// <summary>
    /// テストケースを取得
    /// </summary>
    private IEnumerable<PostProcessingTestCase> GetTestCases()
    {
        return
        [
            // 実際に報告された誤認識パターン
            new PostProcessingTestCase("車体テスト", "単体テスト"),
            new PostProcessingTestCase("オンボーデイシグ (院法体勝)の恐計", "オンボーディング（魔法体験）の設計"),
            new PostProcessingTestCase("役計書", "設計書"),
            new PostProcessingTestCase("恐計", "設計"),
            new PostProcessingTestCase("院法", "魔法"),
            new PostProcessingTestCase("体勝", "体験"),
            
            // 追加のテストケース
            new PostProcessingTestCase("データベース", "データベース"), // 正しいケース
            new PostProcessingTestCase("システム", "システム"), // 正しいケース
            new PostProcessingTestCase("プログラム", "プログラム"), // 正しいケース
            new PostProcessingTestCase("アルゴリズム", "アルゴリズム"), // 正しいケース
            new PostProcessingTestCase("インターフェース", "インターフェース"), // 正しいケース
            new PostProcessingTestCase("オブジェクト指向", "オブジェクト指向"), // 正しいケース
            new PostProcessingTestCase("フレームワーク", "フレームワーク"), // 正しいケース
            
            // 混在テキスト
            new PostProcessingTestCase("API応答時間", "API応答時間"),
            new PostProcessingTestCase("SQL クエリ", "SQLクエリ"),
            new PostProcessingTestCase("HTTP ステータス", "HTTPステータス"),
            new PostProcessingTestCase("JSON データ", "JSONデータ"),
            new PostProcessingTestCase("XML ファイル", "XMLファイル"),
            
            // 複合誤認識パターン
            new PostProcessingTestCase("車体テストの役計", "単体テストの設計"),
            new PostProcessingTestCase("システムの恐計書", "システムの設計書"),
            new PostProcessingTestCase("データベースの院法", "データベースの魔法"),
            
            // 英数字混在
            new PostProcessingTestCase("バージョン1.O.O", "バージョン1.0.0"),
            new PostProcessingTestCase("ポート番号808O", "ポート番号8080"),
            new PostProcessingTestCase("HTTPコード2OO", "HTTPコード200"),
            new PostProcessingTestCase("エラーコード4O4", "エラーコード404"),
        ];
    }
    
    /// <summary>
    /// 精度を計算
    /// </summary>
    private double CalculateAccuracy(string expected, string actual)
    {
        if (string.IsNullOrEmpty(expected) && string.IsNullOrEmpty(actual))
            return 1.0;
        
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual))
            return 0.0;
        
        // レーベンシュタイン距離を使用
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
    /// 推奨設定を生成
    /// </summary>
    private string GenerateRecommendations(List<PostProcessingMethodResult> results)
    {
        var recommendations = new List<string>();
        
        var bestAccuracy = results.OrderByDescending(r => r.AccuracyScore).First();
        recommendations.Add($"最高精度: {bestAccuracy.MethodName} ({bestAccuracy.AccuracyScore:F2}%)");
        
        var fastestMethod = results.OrderBy(r => r.ProcessingTime).First();
        recommendations.Add($"最速処理: {fastestMethod.MethodName} ({fastestMethod.ProcessingTime.TotalMilliseconds:F1}ms)");
        
        var bestCorrectionRate = results.OrderByDescending(r => r.CorrectionRate).First();
        recommendations.Add($"最高修正率: {bestCorrectionRate.MethodName} ({bestCorrectionRate.CorrectionRate:F2}%)");
        
        // バランスの良い手法を推奨
        var balancedMethod = results
            .OrderByDescending(r => r.AccuracyScore * 0.6 + r.CorrectionRate * 0.4 - r.ProcessingTime.TotalSeconds * 0.01)
            .First();
        recommendations.Add($"バランス推奨: {balancedMethod.MethodName}");
        
        return string.Join("\n", recommendations);
    }
}

/// <summary>
/// 後処理テストケース
/// </summary>
public class PostProcessingTestCase(string input, string expected)
{
    public string Input { get; } = input;
    public string Expected { get; } = expected;
}

/// <summary>
/// 後処理テスト結果
/// </summary>
public class PostProcessingTestResult(string input, string expected, string actual, double accuracy, bool wasCorrected)
{
    public string Input { get; } = input;
    public string Expected { get; } = expected;
    public string Actual { get; } = actual;
    public double Accuracy { get; } = accuracy;
    public bool WasCorrected { get; } = wasCorrected;
}

/// <summary>
/// 後処理手法の結果
/// </summary>
public class PostProcessingMethodResult(
    string methodName,
    double accuracyScore,
    double correctionRate,
    TimeSpan processingTime,
    IEnumerable<PostProcessingTestResult> detailedResults)
{
    public string MethodName { get; } = methodName;
    public double AccuracyScore { get; } = accuracyScore;
    public double CorrectionRate { get; } = correctionRate;
    public TimeSpan ProcessingTime { get; } = processingTime;
    public IReadOnlyList<PostProcessingTestResult> DetailedResults { get; } = [.. detailedResults];
}

/// <summary>
/// N-gramベンチマーク結果
/// </summary>
public class NgramBenchmarkResult(
    IEnumerable<PostProcessingMethodResult> results,
    PostProcessingMethodResult bestMethod,
    string recommendations)
{
    public IReadOnlyList<PostProcessingMethodResult> Results { get; } = [.. results];
    public PostProcessingMethodResult BestMethod { get; } = bestMethod;
    public string Recommendations { get; } = recommendations;
}
