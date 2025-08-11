using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.Abstractions.OCR.TextDetection;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.PaddleOCR.TextDetection;

namespace Baketa.Infrastructure.OCR.TextDetection;

/// <summary>
/// テキスト領域検出ベンチマーク実行システム
/// 実際のゲーム画像を使用した効果測定を自動実行
/// </summary>
public sealed class TextDetectionBenchmarkRunner : IDisposable
{
    private readonly ILogger<TextDetectionBenchmarkRunner> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TextDetectionEffectivenessAnalyzer _effectivenessAnalyzer;
    private readonly TestCaseGenerator _testCaseGenerator;
    
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly System.Text.Json.JsonSerializerOptions ReportJsonOptions = new() 
    { 
        WriteIndented = true, 
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
    };
    private bool _disposed;

    public TextDetectionBenchmarkRunner(
        ILogger<TextDetectionBenchmarkRunner> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        
        // テキスト領域検出器を取得
        var detectors = GetAvailableDetectors();
        _effectivenessAnalyzer = new TextDetectionEffectivenessAnalyzer(
            serviceProvider.GetRequiredService<ILogger<TextDetectionEffectivenessAnalyzer>>(),
            detectors);
        
        _testCaseGenerator = new TestCaseGenerator(
            serviceProvider.GetRequiredService<ILogger<TestCaseGenerator>>());
            
        _logger.LogInformation("テキスト領域検出ベンチマークシステム初期化完了");
    }

    /// <summary>
    /// 包括的ベンチマーク実行
    /// </summary>
    public async Task<BenchmarkExecutionReport> RunComprehensiveBenchmarkAsync(
        BenchmarkConfiguration config,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("包括的ベンチマーク実行開始: 設定={ConfigName}", config.Name);
        
        var executionReport = new BenchmarkExecutionReport
        {
            ExecutionTime = DateTime.Now,
            Configuration = config
        };

        try
        {
            // 1. テストケース生成
            _logger.LogInformation("テストケース生成開始");
            var testCases = await _testCaseGenerator.GenerateTestCasesAsync(config, cancellationToken).ConfigureAwait(false);
            executionReport.GeneratedTestCases = testCases.Count;
            
            if (testCases.Count == 0)
            {
                _logger.LogWarning("テストケースが生成されませんでした");
                return executionReport;
            }
            
            // 2. OCRエンジン取得（品質測定用）
            IOcrEngine? ocrEngine = null;
            if (config.MeasureOcrQuality)
            {
                try
                {
                    ocrEngine = _serviceProvider.GetService<IOcrEngine>();
                    _logger.LogInformation("OCR品質測定用エンジン取得: {OcrEngine}", ocrEngine?.GetType().Name ?? "None");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "OCRエンジン取得失敗、品質測定をスキップ");
                }
            }
            
            // 3. 効果測定実行
            _logger.LogInformation("効果測定開始: テストケース数={TestCaseCount}", testCases.Count);
            var effectivenessReport = await _effectivenessAnalyzer.MeasureComprehensiveEffectivenessAsync(
                testCases, ocrEngine, cancellationToken).ConfigureAwait(false);
            
            executionReport.EffectivenessReport = effectivenessReport;
            
            // 4. 結果分析と推奨事項生成
            executionReport.ExecutionSummary = GenerateExecutionSummary(effectivenessReport);
            executionReport.PerformanceComparison = GeneratePerformanceComparison(effectivenessReport);
            executionReport.RecommendedConfiguration = GenerateRecommendedConfiguration(effectivenessReport);
            
            // 5. レポート保存
            await SaveBenchmarkReportAsync(executionReport).ConfigureAwait(false);
            
            _logger.LogInformation("包括的ベンチマーク完了: 総合スコア={OverallScore:F3}, 最適検出器={BestDetector}",
                effectivenessReport.ComprehensiveAnalysis.OverallEffectivenessScore,
                effectivenessReport.ComprehensiveAnalysis.MostAccurateDetector);
                
            return executionReport;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ベンチマーク実行中にエラー");
            executionReport.ErrorMessage = ex.Message;
            return executionReport;
        }
    }

    /// <summary>
    /// 簡易ベンチマーク実行（デフォルト設定）
    /// </summary>
    public async Task<BenchmarkExecutionReport> RunQuickBenchmarkAsync(CancellationToken cancellationToken = default)
    {
        var config = new BenchmarkConfiguration
        {
            Name = "Quick Benchmark",
            TestImageCount = 10,
            IncludeSyntheticImages = true,
            MeasureOcrQuality = false,
            MeasureAdaptationEffect = true,
            OutputDetailedResults = false
        };
        
        return await RunComprehensiveBenchmarkAsync(config, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 特定画像でのA/Bテスト
    /// </summary>
    public async Task<AbTestResult> RunAbTestAsync(
        IAdvancedImage testImage,
        string detectorA,
        string detectorB,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("A/Bテスト実行: {DetectorA} vs {DetectorB}", detectorA, detectorB);
        
        var result = new AbTestResult
        {
            DetectorA = detectorA,
            DetectorB = detectorB,
            TestImageSize = new Size(testImage.Width, testImage.Height)
        };

        try
        {
            var detectors = GetAvailableDetectors().ToDictionary(d => d.Name.ToLowerInvariant(), d => d);
            
            if (!detectors.TryGetValue(detectorA.ToLowerInvariant(), out var detA) ||
                !detectors.TryGetValue(detectorB.ToLowerInvariant(), out var detB))
            {
                throw new ArgumentException("指定された検出器が見つかりません");
            }
            
            // 検出器A実行
            var stopwatchA = System.Diagnostics.Stopwatch.StartNew();
            var regionsA = await detA.DetectRegionsAsync(testImage, cancellationToken).ConfigureAwait(false);
            stopwatchA.Stop();
            
            result.DetectorAResults = new AbTestDetectorResult
            {
                ProcessingTimeMs = stopwatchA.Elapsed.TotalMilliseconds,
                RegionCount = regionsA.Count,
                AverageConfidence = regionsA.Count > 0 ? regionsA.Average(r => r.Confidence) : 0,
                RegionSizes = [.. regionsA.Select(r => r.Bounds.Width * r.Bounds.Height)]
            };
            
            // 検出器B実行
            var stopwatchB = System.Diagnostics.Stopwatch.StartNew();
            var regionsB = await detB.DetectRegionsAsync(testImage, cancellationToken).ConfigureAwait(false);
            stopwatchB.Stop();
            
            result.DetectorBResults = new AbTestDetectorResult
            {
                ProcessingTimeMs = stopwatchB.Elapsed.TotalMilliseconds,
                RegionCount = regionsB.Count,
                AverageConfidence = regionsB.Count > 0 ? regionsB.Average(r => r.Confidence) : 0,
                RegionSizes = [.. regionsB.Select(r => r.Bounds.Width * r.Bounds.Height)]
            };
            
            // 比較分析
            result.SpeedAdvantage = result.DetectorAResults.ProcessingTimeMs < result.DetectorBResults.ProcessingTimeMs ? detectorA : detectorB;
            result.RegionCountDifference = Math.Abs(result.DetectorAResults.RegionCount - result.DetectorBResults.RegionCount);
            result.ConfidenceDifference = Math.Abs(result.DetectorAResults.AverageConfidence - result.DetectorBResults.AverageConfidence);
            
            // 推奨判定
            var scoreA = CalculateAbTestScore(result.DetectorAResults);
            var scoreB = CalculateAbTestScore(result.DetectorBResults);
            result.RecommendedDetector = scoreA > scoreB ? detectorA : detectorB;
            result.ScoreDifference = Math.Abs(scoreA - scoreB);
            
            _logger.LogInformation("A/Bテスト完了: 推奨={Recommended}, スコア差={ScoreDiff:F3}",
                result.RecommendedDetector, result.ScoreDifference);
                
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A/Bテスト実行エラー");
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// 継続的パフォーマンス監視
    /// </summary>
    public async Task StartContinuousMonitoringAsync(
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("継続的パフォーマンス監視開始: 間隔={Interval}", interval);
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var quickReport = await RunQuickBenchmarkAsync(cancellationToken).ConfigureAwait(false);
                
                // パフォーマンス劣化の検出
                if (quickReport.EffectivenessReport != null)
                {
                    var overallScore = quickReport.EffectivenessReport.ComprehensiveAnalysis.OverallEffectivenessScore;
                    if (overallScore < 0.6) // 閾値以下の場合
                    {
                        _logger.LogWarning("パフォーマンス劣化検出: スコア={Score:F3} < 0.6。チューニングが必要", overallScore);
                        
                        // アラート処理（将来的にはイベント発行）
                        await TriggerPerformanceAlertAsync(quickReport).ConfigureAwait(false);
                    }
                }
                
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "継続監視中にエラー");
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
            }
        }
        
        _logger.LogInformation("継続的パフォーマンス監視終了");
    }

    #region Private Methods

    private List<ITextRegionDetector> GetAvailableDetectors()
    {
        var detectors = new List<ITextRegionDetector>();
        
        try
        {
            // 個別検出器を取得
            var adaptiveDetector = _serviceProvider.GetService<AdaptiveTextRegionDetector>();
            if (adaptiveDetector != null)
                detectors.Add(adaptiveDetector);
            
            // FastTextRegionDetectorは古いインターフェースのため一時的に無効化
            
            // ファクトリ経由での検出器取得
            var detectorFactory = _serviceProvider.GetService<Func<string, ITextRegionDetector>>();
            if (detectorFactory != null)
            {
                var detectorTypes = new[] { "mser", "swt" };
                foreach (var type in detectorTypes)
                {
                    try
                    {
                        var detector = detectorFactory(type);
                        detectors.Add(detector);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "検出器取得失敗: {DetectorType}", type);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "検出器取得中にエラー");
        }
        
        _logger.LogInformation("利用可能検出器: {DetectorNames}", 
            string.Join(", ", detectors.Select(d => d.Name)));
            
        return detectors;
    }

    private ExecutionSummary GenerateExecutionSummary(EffectivenessReport report)
    {
        return new ExecutionSummary
        {
            TotalDetectors = report.DetectorCount,
            FastestDetector = report.ComprehensiveAnalysis.FastestDetector,
            FastestTime = report.ComprehensiveAnalysis.FastestDetectorTime,
            MostAccurateDetector = report.ComprehensiveAnalysis.MostAccurateDetector,
            HighestAccuracy = report.ComprehensiveAnalysis.HighestF1Score,
            OverallEffectivenessScore = report.ComprehensiveAnalysis.OverallEffectivenessScore,
            EffectivenessLevel = report.ComprehensiveAnalysis.EffectivenessLevel,
            KeyImprovements = [.. report.ImprovementSuggestions.Take(3)]
        };
    }

    private List<DetectorComparison> GeneratePerformanceComparison(EffectivenessReport report)
    {
        var comparisons = new List<DetectorComparison>();
        
        foreach (var (detectorName, perfMetrics) in report.PerformanceResults)
        {
            var accuracyMetrics = report.DetectionAccuracyResults.GetValueOrDefault(detectorName);
            var adaptationMetrics = report.AdaptationEffectResults.GetValueOrDefault(detectorName);
            
            comparisons.Add(new DetectorComparison
            {
                DetectorName = detectorName,
                ProcessingTime = perfMetrics.AverageProcessingTimeMs,
                Accuracy = accuracyMetrics?.AverageF1Score ?? 0,
                Stability = 1.0 - perfMetrics.StabilityIndex,
                AdaptationEffect = adaptationMetrics?.TimeImprovementPercent ?? 0,
                OverallRank = CalculateOverallRank(perfMetrics, accuracyMetrics, adaptationMetrics)
            });
        }
        
        return [.. comparisons.OrderBy(c => c.OverallRank)];
    }

    private RecommendedConfiguration GenerateRecommendedConfiguration(EffectivenessReport report)
    {
        var bestDetector = report.ComprehensiveAnalysis.MostAccurateDetector;
        var fastestDetector = report.ComprehensiveAnalysis.FastestDetector;
        
        return new RecommendedConfiguration
        {
            PrimaryDetector = bestDetector,
            FallbackDetector = fastestDetector != bestDetector ? fastestDetector : "fast",
            EnableEnsemble = report.DetectorCount > 2,
            OptimalSettings = GenerateOptimalSettings(report),
            ExpectedImprovement = $"精度向上: ~{report.ComprehensiveAnalysis.HighestF1Score:P1}, " +
                                $"速度: ~{report.ComprehensiveAnalysis.FastestDetectorTime:F0}ms"
        };
    }

    private Dictionary<string, object> GenerateOptimalSettings(EffectivenessReport report)
    {
        var settings = new Dictionary<string, object>();
        
        // パフォーマンス結果から最適化設定を推定
        var avgProcessingTime = report.PerformanceResults.Values.Average(p => p.AverageProcessingTimeMs);
        
        if (avgProcessingTime > 500)
        {
            settings["AdaptiveSensitivity"] = 0.6; // 感度を下げて高速化
            settings["MaxRegionsPerImage"] = 30;   // 処理領域数を制限
        }
        else
        {
            settings["AdaptiveSensitivity"] = 0.8; // 高精度設定
            settings["MaxRegionsPerImage"] = 50;
        }
        
        // 適応効果が高い場合は適応機能を強化
        var hasGoodAdaptation = report.AdaptationEffectResults.Values
            .Any(a => a.TimeImprovementPercent > 15);
        
        if (hasGoodAdaptation)
        {
            settings["TemplateUpdateThreshold"] = 0.7;
            settings["HistoryConfidenceThreshold"] = 0.4;
        }
        
        return settings;
    }

    private int CalculateOverallRank(
        PerformanceMetrics perfMetrics, 
        DetectionAccuracyMetrics? accuracyMetrics, 
        AdaptationEffectMetrics? adaptationMetrics)
    {
        var speedScore = Math.Max(0, (2000 - perfMetrics.AverageProcessingTimeMs) / 2000 * 100);
        var accuracyScore = (accuracyMetrics?.AverageF1Score ?? 0) * 100;
        var stabilityScore = Math.Max(0, (1 - perfMetrics.StabilityIndex) * 100);
        var adaptationScore = Math.Min(100, (adaptationMetrics?.TimeImprovementPercent ?? 0) * 2);
        
        var totalScore = speedScore * 0.3 + accuracyScore * 0.4 + stabilityScore * 0.2 + adaptationScore * 0.1;
        return (int)(100 - totalScore); // 小さいほど良いランク
    }

    private double CalculateAbTestScore(AbTestDetectorResult result)
    {
        var speedScore = Math.Max(0, (1000 - result.ProcessingTimeMs) / 1000 * 40);
        var confidenceScore = result.AverageConfidence * 30;
        var regionScore = Math.Min(30, result.RegionCount * 2); // 15個程度が理想
        
        return speedScore + confidenceScore + regionScore;
    }

    private async Task TriggerPerformanceAlertAsync(BenchmarkExecutionReport report)
    {
        try
        {
            var alertData = new
            {
                Timestamp = DateTime.Now,
                OverallScore = report.EffectivenessReport?.ComprehensiveAnalysis.OverallEffectivenessScore,
                Issues = report.EffectivenessReport?.ImprovementSuggestions ?? [],
                RecommendedAction = "Parameter tuning or detector switching recommended"
            };
            
            // 将来的には外部システムへの通知を実装
            var alertJson = System.Text.Json.JsonSerializer.Serialize(alertData, JsonOptions);
            _logger.LogCritical("パフォーマンスアラート: {AlertData}", alertJson);
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アラート処理エラー");
        }
    }

    private async Task SaveBenchmarkReportAsync(BenchmarkExecutionReport report)
    {
        try
        {
            var reportDir = "benchmark_reports";
            Directory.CreateDirectory(reportDir);
            
            var fileName = $"text_detection_benchmark_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var filePath = Path.Combine(reportDir, fileName);
            
            var json = System.Text.Json.JsonSerializer.Serialize(report, ReportJsonOptions);
            await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
            
            _logger.LogInformation("ベンチマークレポート保存完了: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ベンチマークレポート保存エラー");
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        
        _effectivenessAnalyzer?.Dispose();
        _testCaseGenerator?.Dispose();
        _disposed = true;
        
        _logger.LogInformation("テキスト領域検出ベンチマークシステムをクリーンアップ");
        GC.SuppressFinalize(this);
    }
}

#region Data Models

/// <summary>
/// ベンチマーク設定
/// </summary>
public class BenchmarkConfiguration
{
    public string Name { get; set; } = string.Empty;
    public int TestImageCount { get; set; } = 20;
    public bool IncludeSyntheticImages { get; set; } = true;
    public bool IncludeRealGameImages { get; set; } = true;
    public bool MeasureOcrQuality { get; set; } = true;
    public bool MeasureAdaptationEffect { get; set; } = true;
    public bool OutputDetailedResults { get; set; } = true;
    public List<string> TargetDetectors { get; set; } = [];
    public Dictionary<string, object> CustomSettings { get; set; } = [];
}

/// <summary>
/// ベンチマーク実行レポート
/// </summary>
public class BenchmarkExecutionReport
{
    public DateTime ExecutionTime { get; set; }
    public BenchmarkConfiguration Configuration { get; set; } = new();
    public int GeneratedTestCases { get; set; }
    public EffectivenessReport? EffectivenessReport { get; set; }
    public ExecutionSummary ExecutionSummary { get; set; } = new();
    public List<DetectorComparison> PerformanceComparison { get; set; } = [];
    public RecommendedConfiguration RecommendedConfiguration { get; set; } = new();
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 実行サマリー
/// </summary>
public class ExecutionSummary
{
    public int TotalDetectors { get; set; }
    public string FastestDetector { get; set; } = string.Empty;
    public double FastestTime { get; set; }
    public string MostAccurateDetector { get; set; } = string.Empty;
    public double HighestAccuracy { get; set; }
    public double OverallEffectivenessScore { get; set; }
    public string EffectivenessLevel { get; set; } = string.Empty;
    public List<string> KeyImprovements { get; set; } = [];
}

/// <summary>
/// 検出器比較
/// </summary>
public class DetectorComparison
{
    public string DetectorName { get; set; } = string.Empty;
    public double ProcessingTime { get; set; }
    public double Accuracy { get; set; }
    public double Stability { get; set; }
    public double AdaptationEffect { get; set; }
    public int OverallRank { get; set; }
}

/// <summary>
/// 推奨設定
/// </summary>
public class RecommendedConfiguration
{
    public string PrimaryDetector { get; set; } = string.Empty;
    public string FallbackDetector { get; set; } = string.Empty;
    public bool EnableEnsemble { get; set; }
    public Dictionary<string, object> OptimalSettings { get; set; } = [];
    public string ExpectedImprovement { get; set; } = string.Empty;
}

/// <summary>
/// A/Bテスト結果
/// </summary>
public class AbTestResult
{
    public string DetectorA { get; set; } = string.Empty;
    public string DetectorB { get; set; } = string.Empty;
    public Size TestImageSize { get; set; }
    public AbTestDetectorResult DetectorAResults { get; set; } = new();
    public AbTestDetectorResult DetectorBResults { get; set; } = new();
    public string SpeedAdvantage { get; set; } = string.Empty;
    public int RegionCountDifference { get; set; }
    public double ConfidenceDifference { get; set; }
    public string RecommendedDetector { get; set; } = string.Empty;
    public double ScoreDifference { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// A/Bテスト検出器結果
/// </summary>
public class AbTestDetectorResult
{
    public double ProcessingTimeMs { get; set; }
    public int RegionCount { get; set; }
    public double AverageConfidence { get; set; }
    public List<int> RegionSizes { get; set; } = [];
}

#endregion