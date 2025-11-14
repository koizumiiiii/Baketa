using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR.TextDetection;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;
using Timer = System.Threading.Timer;

namespace Baketa.Infrastructure.OCR.TextDetection;

/// <summary>
/// 適応的テキスト領域検出器の管理クラス
/// 複数の検出器を統合管理し、最適な検出戦略を動的に選択
/// 1-B2: テキスト領域検出高度化の管理システム
/// </summary>
public sealed class AdaptiveTextRegionManager : IDisposable
{
    private readonly ILogger<AdaptiveTextRegionManager> _logger;
    private readonly IOptionsMonitor<OcrSettings> _ocrSettings;
    private readonly Dictionary<string, ITextRegionDetector> _detectors = [];
    private readonly Dictionary<string, DetectorPerformanceMetrics> _performanceMetrics = [];
    private readonly Timer _performanceEvaluationTimer;

    private string _currentBestDetector = "adaptive";
    private DateTime _lastEvaluation = DateTime.MinValue;
    private bool _disposed;

    private const int EvaluationIntervalMs = 30000; // 30秒間隔で評価
    private const int MaxPerformanceHistorySize = 100;

    public AdaptiveTextRegionManager(
        ILogger<AdaptiveTextRegionManager> logger,
        IOptionsMonitor<OcrSettings> ocrSettings,
        IEnumerable<ITextRegionDetector> detectors)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ocrSettings = ocrSettings ?? throw new ArgumentNullException(nameof(ocrSettings));

        // 利用可能な検出器を登録
        RegisterDetectors(detectors);

        // パフォーマンス評価タイマーを開始
        _performanceEvaluationTimer = new Timer(EvaluatePerformance, null,
            TimeSpan.FromMilliseconds(EvaluationIntervalMs),
            TimeSpan.FromMilliseconds(EvaluationIntervalMs));

        _logger.LogInformation("適応的テキスト領域管理システムを初期化: 検出器数={DetectorCount}", _detectors.Count);
    }

    /// <summary>
    /// 最適な検出器を使用してテキスト領域を検出
    /// </summary>
    public async Task<IReadOnlyList<OCRTextRegion>> DetectOptimalRegionsAsync(
        IAdvancedImage image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var detectorName = SelectBestDetector();

        try
        {
            _logger.LogDebug("最適検出器選択: {DetectorName} (画像サイズ: {Width}x{Height})",
                detectorName, image.Width, image.Height);

            if (!_detectors.TryGetValue(detectorName, out var detector))
            {
                _logger.LogWarning("検出器が見つからないため、デフォルトに切り替え: {DetectorName} → adaptive", detectorName);
                detector = _detectors["adaptive"];
                detectorName = "adaptive";
            }

            var regions = await detector.DetectRegionsAsync(image, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            // パフォーマンス記録
            RecordPerformance(detectorName, stopwatch.Elapsed.TotalMilliseconds, regions.Count, true);

            _logger.LogInformation("テキスト領域検出完了: 検出器={DetectorName}, 領域数={RegionCount}, 処理時間={ElapsedMs}ms",
                detectorName, regions.Count, stopwatch.ElapsedMilliseconds);

            return regions;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            RecordPerformance(detectorName, stopwatch.Elapsed.TotalMilliseconds, 0, false);

            _logger.LogError(ex, "テキスト領域検出エラー: 検出器={DetectorName}", detectorName);

            // フォールバック実行
            return await ExecuteFallbackDetectionAsync(image, detectorName, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 複数の検出器による並列検出とアンサンブル結果
    /// </summary>
    public async Task<IReadOnlyList<OCRTextRegion>> DetectEnsembleRegionsAsync(
        IAdvancedImage image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        var ensembleSettings = _ocrSettings.CurrentValue.TextDetectionEnsemble;
        if (!ensembleSettings.EnableEnsemble)
        {
            return await DetectOptimalRegionsAsync(image, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug("アンサンブル検出開始: 使用検出器数={DetectorCount}", ensembleSettings.DetectorNames.Count);

        var detectionTasks = ensembleSettings.DetectorNames
            .Where(name => _detectors.ContainsKey(name))
            .Select(async name =>
            {
                try
                {
                    var detector = _detectors[name];
                    var regions = await detector.DetectRegionsAsync(image, cancellationToken).ConfigureAwait(false);
                    return new DetectionResult(name, regions, true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "アンサンブル検出器エラー: {DetectorName}", name);
                    return new DetectionResult(name, [], false);
                }
            });

        var results = await Task.WhenAll(detectionTasks).ConfigureAwait(false);
        var successfulResults = results.Where(r => r.Success).ToList();

        if (successfulResults.Count == 0)
        {
            _logger.LogWarning("全ての検出器が失敗、空の結果を返します");
            return [];
        }

        // アンサンブル統合
        var ensembleRegions = CombineEnsembleResults(successfulResults, ensembleSettings);

        _logger.LogInformation("アンサンブル検出完了: 成功検出器数={SuccessCount}/{TotalCount}, 最終領域数={RegionCount}",
            successfulResults.Count, results.Length, ensembleRegions.Count);

        return ensembleRegions;
    }

    /// <summary>
    /// 検出器のパフォーマンス統計を取得
    /// </summary>
    public Dictionary<string, DetectorPerformanceMetrics> GetPerformanceStatistics()
    {
        return _performanceMetrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Copy());
    }

    /// <summary>
    /// 検出器のランキングを取得
    /// </summary>
    public List<DetectorRanking> GetDetectorRankings()
    {
        return [.. _performanceMetrics
            .Select(kvp => new DetectorRanking
            {
                DetectorName = kvp.Key,
                OverallScore = CalculateOverallScore(kvp.Value),
                AverageProcessingTime = kvp.Value.AverageProcessingTimeMs,
                SuccessRate = kvp.Value.SuccessRate,
                AverageRegionCount = kvp.Value.AverageRegionCount
            })
            .OrderByDescending(r => r.OverallScore)];
    }

    /// <summary>
    /// 特定の検出器のパラメータを動的調整
    /// </summary>
    public void TuneDetectorParameters(string detectorName, Dictionary<string, object> parameters)
    {
        if (!_detectors.TryGetValue(detectorName, out var detector))
        {
            _logger.LogWarning("検出器が見つかりません: {DetectorName}", detectorName);
            return;
        }

        foreach (var (key, value) in parameters)
        {
            try
            {
                detector.SetParameter(key, value);
                _logger.LogDebug("検出器パラメータ調整: {DetectorName}.{ParameterName} = {Value}",
                    detectorName, key, value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "パラメータ設定エラー: {DetectorName}.{ParameterName}", detectorName, key);
            }
        }
    }

    #region Private Methods

    private void RegisterDetectors(IEnumerable<ITextRegionDetector> detectors)
    {
        foreach (var detector in detectors)
        {
            var name = detector.Name.ToLowerInvariant();
            _detectors[name] = detector;
            _performanceMetrics[name] = new DetectorPerformanceMetrics(name);

            _logger.LogDebug("検出器登録: {DetectorName} ({Method})", detector.Name, detector.Method);
        }

        // フォールバック用の基本検出器を確保
        if (!_detectors.ContainsKey("adaptive"))
        {
            _logger.LogWarning("適応的検出器が見つからないため、基本検出器を使用");
        }
    }

    private string SelectBestDetector()
    {
        var settings = _ocrSettings.CurrentValue.TextDetectionSettings;

        // 設定による強制指定があれば使用
        if (!string.IsNullOrEmpty(settings.ForcedDetectorName) &&
            _detectors.ContainsKey(settings.ForcedDetectorName))
        {
            return settings.ForcedDetectorName;
        }

        // パフォーマンス履歴による最適選択
        if (_performanceMetrics.Any(p => p.Value.TotalExecutions > 5))
        {
            var bestDetector = _performanceMetrics
                .Where(p => p.Value.TotalExecutions > 5)
                .OrderByDescending(p => CalculateOverallScore(p.Value))
                .FirstOrDefault();

            if (bestDetector.Key != null)
            {
                _currentBestDetector = bestDetector.Key;
            }
        }

        return _currentBestDetector;
    }

    private async Task<IReadOnlyList<OCRTextRegion>> ExecuteFallbackDetectionAsync(
        IAdvancedImage image,
        string failedDetector,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("フォールバック検出実行: 失敗検出器={FailedDetector}", failedDetector);

        // 失敗した検出器以外で最も信頼性の高い検出器を選択
        var fallbackDetector = _performanceMetrics
            .Where(p => p.Key != failedDetector && p.Value.SuccessRate > 0.8)
            .OrderByDescending(p => p.Value.SuccessRate)
            .FirstOrDefault();

        if (fallbackDetector.Key != null && _detectors.TryGetValue(fallbackDetector.Key, out var detector))
        {
            try
            {
                var regions = await detector.DetectRegionsAsync(image, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("フォールバック検出成功: 使用検出器={FallbackDetector}, 領域数={RegionCount}",
                    fallbackDetector.Key, regions.Count);
                return regions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "フォールバック検出も失敗: {FallbackDetector}", fallbackDetector.Key);
            }
        }

        // すべて失敗した場合は空の結果を返す
        _logger.LogWarning("全ての検出器が失敗、空の結果を返します");
        return [];
    }

    private List<OCRTextRegion> CombineEnsembleResults(
        List<DetectionResult> results,
        EnsembleSettings settings)
    {
        if (results.Count == 1)
        {
            return [.. results[0].Regions];
        }

        // 投票ベースの統合アルゴリズム
        var allRegions = results.SelectMany(r => r.Regions).ToList();
        var combinedRegions = new List<OCRTextRegion>();

        // 類似領域をグループ化
        var regionGroups = GroupSimilarRegions(allRegions, settings.OverlapThreshold);

        foreach (var group in regionGroups)
        {
            if (group.Count >= settings.MinVotes)
            {
                // グループの代表領域を作成
                var representativeRegion = CreateRepresentativeRegion(group);
                combinedRegions.Add(representativeRegion);
            }
        }

        return combinedRegions;
    }

    private List<List<OCRTextRegion>> GroupSimilarRegions(List<OCRTextRegion> regions, double overlapThreshold)
    {
        var groups = new List<List<OCRTextRegion>>();
        var processed = new bool[regions.Count];

        for (int i = 0; i < regions.Count; i++)
        {
            if (processed[i]) continue;

            var group = new List<OCRTextRegion> { regions[i] };
            processed[i] = true;

            for (int j = i + 1; j < regions.Count; j++)
            {
                if (processed[j]) continue;

                var overlap = regions[i].CalculateOverlapRatio(regions[j]);
                if (overlap >= overlapThreshold)
                {
                    group.Add(regions[j]);
                    processed[j] = true;
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    private OCRTextRegion CreateRepresentativeRegion(List<OCRTextRegion> group)
    {
        // 最も信頼度の高い領域をベースとして使用
        var bestRegion = group.OrderByDescending(r => r.Confidence).First();

        // 平均位置とサイズを計算
        var avgX = (int)group.Average(r => r.Bounds.X);
        var avgY = (int)group.Average(r => r.Bounds.Y);
        var avgWidth = (int)group.Average(r => r.Bounds.Width);
        var avgHeight = (int)group.Average(r => r.Bounds.Height);
        var avgConfidence = group.Average(r => r.Confidence);

        return new OCRTextRegion(new Rectangle(avgX, avgY, avgWidth, avgHeight), (float)avgConfidence)
        {
            RegionType = bestRegion.RegionType,
            DetectionMethod = $"Ensemble({group.Count}votes)"
        };
    }

    private void RecordPerformance(string detectorName, double processingTimeMs, int regionCount, bool success)
    {
        if (_performanceMetrics.TryGetValue(detectorName, out var metrics))
        {
            metrics.RecordExecution(processingTimeMs, regionCount, success);
        }
    }

    private double CalculateOverallScore(DetectorPerformanceMetrics metrics)
    {
        // 成功率(60%) + 処理速度(25%) + 検出品質(15%)のスコア
        var successScore = metrics.SuccessRate * 0.6;
        var speedScore = Math.Max(0, (2000 - metrics.AverageProcessingTimeMs) / 2000) * 0.25; // 2秒を基準
        var qualityScore = Math.Min(1.0, metrics.AverageRegionCount / 10.0) * 0.15; // 10個を理想とする

        return successScore + speedScore + qualityScore;
    }

    private void EvaluatePerformance(object? state)
    {
        try
        {
            if (DateTime.Now - _lastEvaluation < TimeSpan.FromMilliseconds(EvaluationIntervalMs))
                return;

            _lastEvaluation = DateTime.Now;

            var rankings = GetDetectorRankings();
            if (rankings.Count > 0)
            {
                var newBest = rankings[0].DetectorName;
                if (newBest != _currentBestDetector)
                {
                    _logger.LogInformation("最適検出器変更: {OldDetector} → {NewDetector} (スコア: {Score:F3})",
                        _currentBestDetector, newBest, rankings[0].OverallScore);
                    _currentBestDetector = newBest;
                }
            }

            // パフォーマンス履歴のクリーンアップ
            CleanupPerformanceHistory();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "パフォーマンス評価中にエラー");
        }
    }

    private void CleanupPerformanceHistory()
    {
        foreach (var metrics in _performanceMetrics.Values)
        {
            metrics.CleanupOldData(MaxPerformanceHistorySize);
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;

        _performanceEvaluationTimer?.Dispose();

        foreach (var detector in _detectors.Values)
        {
            if (detector is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _disposed = true;
        _logger.LogInformation("適応的テキスト領域管理システムをクリーンアップ");

        GC.SuppressFinalize(this);
    }
}

#region Supporting Classes

/// <summary>
/// 検出結果
/// </summary>
public record DetectionResult(string DetectorName, IReadOnlyList<OCRTextRegion> Regions, bool Success);

/// <summary>
/// 検出器パフォーマンス指標
/// </summary>
public class DetectorPerformanceMetrics(string detectorName)
{
    public string DetectorName { get; } = detectorName;
    public int TotalExecutions { get; private set; }
    public int SuccessfulExecutions { get; private set; }
    public double TotalProcessingTimeMs { get; private set; }
    public int TotalRegionsDetected { get; private set; }
    public DateTime LastExecutionTime { get; private set; }

    private readonly Queue<PerformanceDataPoint> _recentPerformance = new();

    public double SuccessRate => TotalExecutions > 0 ? (double)SuccessfulExecutions / TotalExecutions : 0.0;
    public double AverageProcessingTimeMs => SuccessfulExecutions > 0 ? TotalProcessingTimeMs / SuccessfulExecutions : 0.0;
    public double AverageRegionCount => SuccessfulExecutions > 0 ? (double)TotalRegionsDetected / SuccessfulExecutions : 0.0;

    public void RecordExecution(double processingTimeMs, int regionCount, bool success)
    {
        TotalExecutions++;
        LastExecutionTime = DateTime.Now;

        var dataPoint = new PerformanceDataPoint
        {
            Timestamp = DateTime.Now,
            ProcessingTimeMs = processingTimeMs,
            RegionCount = regionCount,
            Success = success
        };

        _recentPerformance.Enqueue(dataPoint);

        if (success)
        {
            SuccessfulExecutions++;
            TotalProcessingTimeMs += processingTimeMs;
            TotalRegionsDetected += regionCount;
        }
    }

    public void CleanupOldData(int maxSize)
    {
        while (_recentPerformance.Count > maxSize)
        {
            _recentPerformance.Dequeue();
        }
    }

    public DetectorPerformanceMetrics Copy()
    {
        var copy = new DetectorPerformanceMetrics(DetectorName)
        {
            TotalExecutions = TotalExecutions,
            SuccessfulExecutions = SuccessfulExecutions,
            TotalProcessingTimeMs = TotalProcessingTimeMs,
            TotalRegionsDetected = TotalRegionsDetected,
            LastExecutionTime = LastExecutionTime
        };
        return copy;
    }
}

/// <summary>
/// パフォーマンスデータポイント
/// </summary>
public class PerformanceDataPoint
{
    public DateTime Timestamp { get; set; }
    public double ProcessingTimeMs { get; set; }
    public int RegionCount { get; set; }
    public bool Success { get; set; }
}

/// <summary>
/// 検出器ランキング
/// </summary>
public class DetectorRanking
{
    public string DetectorName { get; set; } = string.Empty;
    public double OverallScore { get; set; }
    public double AverageProcessingTime { get; set; }
    public double SuccessRate { get; set; }
    public double AverageRegionCount { get; set; }
}

#endregion
