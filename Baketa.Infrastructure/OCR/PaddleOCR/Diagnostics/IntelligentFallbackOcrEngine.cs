using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Diagnostics;

/// <summary>
/// インテリジェントフォールバック機能付きOCRエンジン
/// 複数のOCRエンジンと戦略を使用してタイムアウトや失敗に対応
/// </summary>
public sealed class IntelligentFallbackOcrEngine : IOcrEngine, IDisposable
{
    private readonly ILogger<IntelligentFallbackOcrEngine> _logger;
    private readonly PPOCRv5DiagnosticService _diagnosticService;
    private readonly List<FallbackStrategy> _strategies;
    private readonly FallbackStatistics _statistics = new();
    private readonly object _statisticsLock = new();
    
    private bool _disposed;

    public IntelligentFallbackOcrEngine(
        ILogger<IntelligentFallbackOcrEngine> logger,
        PPOCRv5DiagnosticService diagnosticService,
        IEnumerable<IOcrEngine> engines)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _diagnosticService = diagnosticService ?? throw new ArgumentNullException(nameof(diagnosticService));
        
        ArgumentNullException.ThrowIfNull(engines);
        
        _strategies = [.. CreateStrategies(engines)];
        
        if (_strategies.Count == 0)
            throw new ArgumentException("少なくとも1つのOCRエンジンが必要です", nameof(engines));

        _logger.LogInformation("IntelligentFallbackOcrEngine初期化完了: {StrategyCount}個の戦略", _strategies.Count);
    }

    #region IOcrEngine Implementation

    public string EngineName => "IntelligentFallback";
    public string EngineVersion => "1.0.0";
    public bool IsInitialized => _strategies.Any(s => s.Engine.IsInitialized);
    public string? CurrentLanguage => _strategies.FirstOrDefault(s => s.Engine.IsInitialized)?.Engine.CurrentLanguage;

    public async Task<bool> InitializeAsync(OcrEngineSettings? settings = null, CancellationToken cancellationToken = default)
    {
        var tasks = _strategies.Select(async strategy =>
        {
            try
            {
                await strategy.Engine.InitializeAsync(settings, cancellationToken).ConfigureAwait(false);
                strategy.IsAvailable = true;
                _logger.LogDebug("フォールバック戦略初期化成功: {StrategyName}", strategy.Name);
            }
            catch (Exception ex)
            {
                strategy.IsAvailable = false;
                _logger.LogWarning(ex, "フォールバック戦略初期化失敗: {StrategyName}", strategy.Name);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var availableCount = _strategies.Count(s => s.IsAvailable);
        if (availableCount == 0)
        {
            _logger.LogError("すべてのフォールバック戦略の初期化に失敗しました");
            return false;
        }

        _logger.LogInformation("フォールバック初期化完了: {Available}/{Total}個の戦略が利用可能", 
            availableCount, _strategies.Count);
        return true;
    }
    
    public async Task<bool> WarmupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("IntelligentFallbackOcrEngineウォームアップ開始");
        
        // 初期化済みのエンジンのみウォームアップ
        foreach (var strategy in _strategies.Where(s => s.IsAvailable))
        {
            try
            {
                var result = await strategy.Engine.WarmupAsync(cancellationToken).ConfigureAwait(false);
                if (result)
                {
                    _logger.LogInformation("エンジン {EngineName} のウォームアップ成功", strategy.Name);
                    return true; // 最初に成功したエンジンで完了
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "エンジン {EngineName} のウォームアップ失敗", strategy.Name);
            }
        }
        
        return false;
    }

    public async Task<OcrResults> RecognizeAsync(IImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var operationId = Guid.NewGuid().ToString("N")[..8];
        var overallStopwatch = Stopwatch.StartNew();
        
        _logger.LogDebug("インテリジェントフォールバック開始: OperationId={OperationId}", operationId);

        var attemptResults = new List<AttemptResult>();
        
        foreach (var strategy in GetOrderedStrategies())
        {
            if (!strategy.IsAvailable)
                continue;

            var attemptStopwatch = Stopwatch.StartNew();
            var attempt = new AttemptResult
            {
                StrategyName = strategy.Name,
                StartTime = DateTime.UtcNow
            };

            try
            {
                _logger.LogDebug("フォールバック戦略実行: {Strategy}, OperationId={OperationId}", 
                    strategy.Name, operationId);

                // 戦略に応じたタイムアウト設定
                using var strategyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                strategyCts.CancelAfter(strategy.Timeout);

                OcrResults result;
                if (strategy.UseDiagnostics)
                {
                    result = await _diagnosticService.ExecuteWithDiagnosticsAsync(
                        strategy.Engine, image, strategy.Settings, strategyCts.Token)
                        .ConfigureAwait(false);
                }
                else
                {
                    result = await strategy.Engine.RecognizeAsync(image, cancellationToken: strategyCts.Token)
                        .ConfigureAwait(false);
                }

                attemptStopwatch.Stop();
                attempt.Duration = attemptStopwatch.Elapsed;
                attempt.Success = true;
                attempt.TextRegionsCount = result.TextRegions.Count;
                attempt.Confidence = result.TextRegions.Count > 0 ? 
                    result.TextRegions.Average(r => r.Confidence) : 0.0;

                attemptResults.Add(attempt);
                
                // 成功判定
                if (IsResultAcceptable(result, strategy))
                {
                    overallStopwatch.Stop();
                    
                    UpdateStatistics(attemptResults, result, overallStopwatch.Elapsed);
                    
                    _logger.LogInformation("フォールバック成功: {Strategy}, OperationId={OperationId}, Duration={Duration}ms, TextRegions={Count}",
                        strategy.Name, operationId, overallStopwatch.ElapsedMilliseconds, result.TextRegions.Count);

                    return result;
                }
                else
                {
                    _logger.LogDebug("フォールバック結果が不十分: {Strategy}, TextRegions={Count}, Confidence={Confidence:F3}",
                        strategy.Name, result.TextRegions.Count, attempt.Confidence);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 外部キャンセル - 即座に終了
                attemptStopwatch.Stop();
                attempt.Duration = attemptStopwatch.Elapsed;
                attempt.Success = false;
                attempt.ErrorType = "ExternalCancellation";
                attemptResults.Add(attempt);
                
                _logger.LogDebug("フォールバック外部キャンセル: OperationId={OperationId}", operationId);
                throw;
            }
            catch (OperationCanceledException)
            {
                // 戦略タイムアウト - 次の戦略を試行
                attemptStopwatch.Stop();
                attempt.Duration = attemptStopwatch.Elapsed;
                attempt.Success = false;
                attempt.ErrorType = "Timeout";
                attemptResults.Add(attempt);
                
                _logger.LogWarning("フォールバック戦略タイムアウト: {Strategy}, Duration={Duration}ms, OperationId={OperationId}",
                    strategy.Name, attemptStopwatch.ElapsedMilliseconds, operationId);
                
                // 戦略の優先度を下げる
                strategy.ReducePriority();
                continue;
            }
            catch (Exception ex)
            {
                attemptStopwatch.Stop();
                attempt.Duration = attemptStopwatch.Elapsed;
                attempt.Success = false;
                attempt.ErrorType = ex.GetType().Name;
                attempt.ErrorMessage = ex.Message;
                attemptResults.Add(attempt);
                
                _logger.LogWarning(ex, "フォールバック戦略エラー: {Strategy}, OperationId={OperationId}",
                    strategy.Name, operationId);
                
                // 戦略を一時的に無効化
                strategy.MarkTemporaryFailure();
                continue;
            }
        }

        // すべての戦略が失敗
        overallStopwatch.Stop();
        UpdateStatistics(attemptResults, null, overallStopwatch.Elapsed);

        var errorMessage = $"すべてのフォールバック戦略が失敗: 試行={attemptResults.Count}, 処理時間={overallStopwatch.ElapsedMilliseconds}ms";
        _logger.LogError("{ErrorMessage}, OperationId={OperationId}", errorMessage, operationId);
        
        throw new InvalidOperationException(errorMessage);
    }

    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        return await RecognizeAsync(image, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        Rectangle? regionOfInterest,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        // ROI指定の場合は、まず最初の戦略でROIサポートをチェック
        // 現在の実装では画像全体を処理
        return await RecognizeAsync(image, cancellationToken).ConfigureAwait(false);
    }

    public OcrEngineSettings GetSettings()
    {
        var firstAvailableEngine = _strategies.FirstOrDefault(s => s.IsAvailable)?.Engine;
        return firstAvailableEngine?.GetSettings() ?? new OcrEngineSettings();
    }

    public IReadOnlyList<string> GetAvailableLanguages()
    {
        var allLanguages = new HashSet<string>();
        foreach (var strategy in _strategies.Where(s => s.IsAvailable))
        {
            var languages = strategy.Engine.GetAvailableLanguages();
            foreach (var lang in languages)
            {
                allLanguages.Add(lang);
            }
        }
        return allLanguages.ToList().AsReadOnly();
    }

    public IReadOnlyList<string> GetAvailableModels()
    {
        var allModels = new HashSet<string>();
        foreach (var strategy in _strategies.Where(s => s.IsAvailable))
        {
            var models = strategy.Engine.GetAvailableModels();
            foreach (var model in models)
            {
                allModels.Add(model);
            }
        }
        return allModels.ToList().AsReadOnly();
    }

    public async Task<bool> IsLanguageAvailableAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        foreach (var strategy in _strategies.Where(s => s.IsAvailable))
        {
            try
            {
                var isAvailable = await strategy.Engine.IsLanguageAvailableAsync(languageCode, cancellationToken).ConfigureAwait(false);
                if (isAvailable)
                    return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "言語チェック失敗: {Strategy}, Language={Language}", strategy.Name, languageCode);
            }
        }
        return false;
    }

    public OcrPerformanceStats GetPerformanceStats()
    {
        var stats = GetStatistics();
        
        return new OcrPerformanceStats
        {
            TotalProcessedImages = stats.TotalOperations,
            AverageProcessingTimeMs = stats.AverageDuration.TotalMilliseconds,
            MinProcessingTimeMs = stats.TotalOperations > 0 ? 
                stats.StrategyStats.Values.Where(s => s.TotalAttempts > 0).DefaultIfEmpty().Min(s => s?.AverageDuration.TotalMilliseconds ?? 0) : 0,
            MaxProcessingTimeMs = stats.TotalOperations > 0 ? 
                stats.StrategyStats.Values.Where(s => s.TotalAttempts > 0).DefaultIfEmpty().Max(s => s?.AverageDuration.TotalMilliseconds ?? 0) : 0,
            ErrorCount = stats.FailedOperations,
            SuccessRate = stats.SuccessRate / 100.0,
            StartTime = DateTime.UtcNow.AddTicks(-stats.TotalDuration.Ticks),
            LastUpdateTime = stats.LastUpdated
        };
    }

    public async Task ApplySettingsAsync(OcrEngineSettings settings, CancellationToken cancellationToken = default)
    {
        var tasks = _strategies.Where(s => s.IsAvailable).Select(async strategy =>
        {
            try
            {
                await strategy.Engine.ApplySettingsAsync(settings, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "フォールバック戦略設定適用失敗: {StrategyName}", strategy.Name);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public void CancelCurrentOcrTimeout()
    {
        foreach (var strategy in _strategies.Where(s => s.IsAvailable))
        {
            try
            {
                strategy.Engine.CancelCurrentOcrTimeout();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "フォールバック戦略タイムアウトキャンセル失敗: {StrategyName}", strategy.Name);
            }
        }
    }

    /// <summary>
    /// 連続失敗回数を取得（診断・フォールバック判定用）
    /// </summary>
    /// <returns>連続失敗回数</returns>
    public int GetConsecutiveFailureCount()
    {
        // 最初の利用可能な戦略の失敗回数を返す
        var firstAvailableStrategy = _strategies.FirstOrDefault(s => s.IsAvailable);
        return firstAvailableStrategy?.Engine.GetConsecutiveFailureCount() ?? 0;
    }

    /// <summary>
    /// 失敗カウンタをリセット（緊急時復旧用）
    /// </summary>
    public void ResetFailureCounter()
    {
        // すべての戦略のエンジンのカウンタをリセット
        foreach (var strategy in _strategies.Where(s => s.IsAvailable))
        {
            try
            {
                strategy.Engine.ResetFailureCounter();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "フォールバック戦略失敗カウンタリセット失敗: {StrategyName}", strategy.Name);
            }
        }
    }

    /// <summary>
    /// テキスト検出のみを実行（認識処理をスキップ）
    /// </summary>
    public async Task<OcrResults> DetectTextRegionsAsync(IImage image, CancellationToken cancellationToken = default)
    {
        var availableStrategy = _strategies.FirstOrDefault(s => s.IsAvailable);
        if (availableStrategy != null)
        {
            try
            {
                return await availableStrategy.Engine.DetectTextRegionsAsync(image, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "フォールバック戦略での検出専用処理失敗: {StrategyName}", availableStrategy.Name);
            }
        }
        
        // すべて失敗した場合は空の結果を返す
        return new OcrResults([], image, TimeSpan.Zero, "jpn", null, "");
    }

    #endregion

    /// <summary>
    /// 現在のフォールバック統計を取得
    /// </summary>
    public FallbackStatistics GetStatistics()
    {
        lock (_statisticsLock)
        {
            return _statistics.Clone();
        }
    }

    /// <summary>
    /// 戦略の優先度をリセット
    /// </summary>
    public void ResetStrategyPriorities()
    {
        foreach (var strategy in _strategies)
        {
            strategy.ResetPriority();
        }
        _logger.LogInformation("フォールバック戦略優先度をリセット");
    }

    private static List<FallbackStrategy> CreateStrategies(IEnumerable<IOcrEngine> engines)
    {
        var strategies = new List<FallbackStrategy>();

        foreach (var (engine, index) in engines.Select((e, i) => (e, i)))
        {
            // メインエンジン（通常はPaddleOcrEngine）
            if (index == 0)
            {
                strategies.Add(new FallbackStrategy
                {
                    Name = $"Primary_{engine.EngineName}",
                    Engine = engine,
                    Priority = 1,
                    Timeout = TimeSpan.FromSeconds(45),
                    MinConfidence = 0.3,
                    MinTextRegions = 1,
                    UseDiagnostics = true,
                    Settings = new OcrEngineSettings
                    {
                        Language = "jpn",
                        DetectionThreshold = 0.1f,
                        RecognitionThreshold = 0.1f
                    }
                });

                // 高速モード
                strategies.Add(new FallbackStrategy
                {
                    Name = $"Fast_{engine.EngineName}",
                    Engine = engine,
                    Priority = 2,
                    Timeout = TimeSpan.FromSeconds(15),
                    MinConfidence = 0.2,
                    MinTextRegions = 1,
                    UseDiagnostics = false,
                    Settings = new OcrEngineSettings
                    {
                        Language = "jpn",
                        DetectionThreshold = 0.2f,
                        RecognitionThreshold = 0.2f
                    }
                });
            }
            else
            {
                // サブエンジン
                strategies.Add(new FallbackStrategy
                {
                    Name = $"Fallback_{engine.EngineName}",
                    Engine = engine,
                    Priority = 10 + index,
                    Timeout = TimeSpan.FromSeconds(30),
                    MinConfidence = 0.1,
                    MinTextRegions = 0,
                    UseDiagnostics = false
                });
            }
        }

        return strategies;
    }

    private IEnumerable<FallbackStrategy> GetOrderedStrategies()
    {
        return _strategies
            .Where(s => s.IsAvailable && !s.IsTemporarilyDisabled)
            .OrderBy(s => s.CurrentPriority)
            .ThenBy(s => s.AverageResponseTime);
    }

    private static bool IsResultAcceptable(OcrResults result, FallbackStrategy strategy)
    {
        if (!result.HasText)
            return false;

        if (result.TextRegions.Count < strategy.MinTextRegions)
            return false;

        var averageConfidence = result.TextRegions.Count > 0 ? 
            result.TextRegions.Average(r => r.Confidence) : 0.0;

        return averageConfidence >= strategy.MinConfidence;
    }

    private void UpdateStatistics(List<AttemptResult> attempts, OcrResults? result, TimeSpan totalDuration)
    {
        lock (_statisticsLock)
        {
            _statistics.TotalOperations++;
            _statistics.TotalDuration += totalDuration;
            
            if (result != null)
            {
                _statistics.SuccessfulOperations++;
                _statistics.TotalTextRegions += result.TextRegions.Count;
                _statistics.TotalAttempts += attempts.Count;
                
                var successfulAttempt = attempts.FirstOrDefault(a => a.Success);
                if (successfulAttempt != null)
                {
                    _statistics.UpdateStrategyStats(successfulAttempt.StrategyName, true, successfulAttempt.Duration);
                }
            }
            else
            {
                _statistics.FailedOperations++;
                _statistics.TotalAttempts += attempts.Count;
                
                foreach (var attempt in attempts)
                {
                    _statistics.UpdateStrategyStats(attempt.StrategyName, false, attempt.Duration);
                }
            }

            _statistics.LastUpdated = DateTime.UtcNow;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var strategy in _strategies)
        {
            try
            {
                if (strategy.Engine is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "フォールバック戦略破棄中にエラー: {StrategyName}", strategy.Name);
            }
        }

        _disposed = true;
    }
}

/// <summary>
/// フォールバック戦略
/// </summary>
public class FallbackStrategy
{
    public required string Name { get; init; }
    public required IOcrEngine Engine { get; init; }
    public int Priority { get; init; }
    public int CurrentPriority { get; private set; }
    public TimeSpan Timeout { get; init; }
    public double MinConfidence { get; init; }
    public int MinTextRegions { get; init; }
    public bool UseDiagnostics { get; init; }
    public OcrEngineSettings? Settings { get; init; }
    
    public bool IsAvailable { get; set; }
    public bool IsTemporarilyDisabled { get; private set; }
    public DateTime? LastFailureTime { get; private set; }
    public TimeSpan AverageResponseTime { get; private set; }
    
    private readonly List<TimeSpan> _recentResponseTimes = new(10);
    private int _consecutiveFailures;

    public FallbackStrategy()
    {
        CurrentPriority = Priority;
    }

    public void ReducePriority()
    {
        CurrentPriority = Math.Min(CurrentPriority + 5, 100);
    }

    public void ResetPriority()
    {
        CurrentPriority = Priority;
        IsTemporarilyDisabled = false;
        _consecutiveFailures = 0;
    }

    public void MarkTemporaryFailure()
    {
        _consecutiveFailures++;
        LastFailureTime = DateTime.UtcNow;
        
        if (_consecutiveFailures >= 3)
        {
            IsTemporarilyDisabled = true;
            // 5分後に再有効化
            _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
            {
                IsTemporarilyDisabled = false;
                _consecutiveFailures = 0;
            });
        }
    }

    public void RecordResponseTime(TimeSpan responseTime)
    {
        lock (_recentResponseTimes)
        {
            _recentResponseTimes.Add(responseTime);
            if (_recentResponseTimes.Count > 10)
                _recentResponseTimes.RemoveAt(0);
            
            AverageResponseTime = TimeSpan.FromTicks((long)_recentResponseTimes.Average(t => t.Ticks));
        }
    }
}

/// <summary>
/// 試行結果
/// </summary>
public class AttemptResult
{
    public required string StrategyName { get; init; }
    public DateTime StartTime { get; init; }
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public string? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public int TextRegionsCount { get; set; }
    public double Confidence { get; set; }
}

/// <summary>
/// フォールバック統計
/// </summary>
public class FallbackStatistics
{
    public int TotalOperations { get; set; }
    public int SuccessfulOperations { get; set; }
    public int FailedOperations { get; set; }
    public int TotalAttempts { get; set; }
    public int TotalTextRegions { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public DateTime LastUpdated { get; set; }

    private readonly Dictionary<string, StrategyStats> _strategyStats = [];

    public double SuccessRate => TotalOperations > 0 ? (double)SuccessfulOperations / TotalOperations * 100 : 0;
    public double AverageAttemptsPerOperation => TotalOperations > 0 ? (double)TotalAttempts / TotalOperations : 0;
    public TimeSpan AverageDuration => TotalOperations > 0 ? 
        TimeSpan.FromTicks(TotalDuration.Ticks / TotalOperations) : TimeSpan.Zero;

    public IReadOnlyDictionary<string, StrategyStats> StrategyStats => _strategyStats.AsReadOnly();

    public void UpdateStrategyStats(string strategyName, bool success, TimeSpan duration)
    {
        if (!_strategyStats.TryGetValue(strategyName, out var stats))
        {
            stats = new StrategyStats { StrategyName = strategyName };
            _strategyStats[strategyName] = stats;
        }

        stats.TotalAttempts++;
        stats.TotalDuration += duration;
        
        if (success)
            stats.SuccessfulAttempts++;
        else
            stats.FailedAttempts++;
    }

    public FallbackStatistics Clone()
    {
        var clone = new FallbackStatistics
        {
            TotalOperations = TotalOperations,
            SuccessfulOperations = SuccessfulOperations,
            FailedOperations = FailedOperations,
            TotalAttempts = TotalAttempts,
            TotalTextRegions = TotalTextRegions,
            TotalDuration = TotalDuration,
            LastUpdated = LastUpdated
        };

        foreach (var kvp in _strategyStats)
        {
            clone._strategyStats[kvp.Key] = kvp.Value.Clone();
        }

        return clone;
    }
}

/// <summary>
/// 戦略別統計
/// </summary>
public class StrategyStats
{
    public required string StrategyName { get; init; }
    public int TotalAttempts { get; set; }
    public int SuccessfulAttempts { get; set; }
    public int FailedAttempts { get; set; }
    public TimeSpan TotalDuration { get; set; }

    public double SuccessRate => TotalAttempts > 0 ? (double)SuccessfulAttempts / TotalAttempts * 100 : 0;
    public TimeSpan AverageDuration => TotalAttempts > 0 ? 
        TimeSpan.FromTicks(TotalDuration.Ticks / TotalAttempts) : TimeSpan.Zero;

    public StrategyStats Clone()
    {
        return new StrategyStats
        {
            StrategyName = StrategyName,
            TotalAttempts = TotalAttempts,
            SuccessfulAttempts = SuccessfulAttempts,
            FailedAttempts = FailedAttempts,
            TotalDuration = TotalDuration
        };
    }
}