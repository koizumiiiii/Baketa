using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;

namespace Baketa.Infrastructure.OCR.Ensemble;

/// <summary>
/// 複数OCRエンジンによるアンサンブル処理を実装するメインクラス
/// </summary>
public class EnsembleOcrEngine : IEnsembleOcrEngine
{
    private readonly ILogger<EnsembleOcrEngine> _logger;
    private readonly List<EnsembleEngineInfo> _engines = [];
    private readonly ConcurrentDictionary<string, EnsembleEngineStats> _engineStats = new();
    private IResultFusionStrategy _fusionStrategy;
    private OcrEngineSettings? _currentSettings;
    private bool _isInitialized = false;

    // IOcrEngineプロパティの実装
    public string EngineName => "EnsembleOCR";
    public string EngineVersion => "1.0.0";
    public bool IsInitialized => _isInitialized;
    public string? CurrentLanguage => _currentSettings?.Language;

    public EnsembleOcrEngine(
        IResultFusionStrategy defaultFusionStrategy,
        ILogger<EnsembleOcrEngine> logger)
    {
        _fusionStrategy = defaultFusionStrategy;
        _logger = logger;
    }

    #region IEnsembleOcrEngine Implementation

    public void AddEngine(IOcrEngine engine, double weight = 1.0, EnsembleEngineRole role = EnsembleEngineRole.Primary)
    {
        if (engine == null) throw new ArgumentNullException(nameof(engine));
        if (weight <= 0) throw new ArgumentException("Weight must be positive", nameof(weight));

        var engineInfo = new EnsembleEngineInfo(
            engine,
            engine.EngineName,
            weight,
            role,
            true,
            new EnsembleEngineStats(0, 0, 0, 0, DateTime.MinValue));

        _engines.Add(engineInfo);
        _engineStats.TryAdd(engine.EngineName, engineInfo.Stats);

        _logger.LogInformation("アンサンブルエンジン追加: {EngineName}, 重み={Weight}, 役割={Role}",
            engine.EngineName, weight, role);
    }

    public bool RemoveEngine(IOcrEngine engine)
    {
        if (engine == null) return false;

        var index = _engines.FindIndex(e => e.Engine == engine);
        if (index >= 0)
        {
            _engines.RemoveAt(index);
            _engineStats.TryRemove(engine.EngineName, out _);

            _logger.LogInformation("アンサンブルエンジン削除: {EngineName}", engine.EngineName);
            return true;
        }

        return false;
    }

    public IReadOnlyList<EnsembleEngineInfo> GetEnsembleConfiguration()
    {
        return _engines.AsReadOnly();
    }

    public void SetFusionStrategy(IResultFusionStrategy strategy)
    {
        _fusionStrategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _logger.LogInformation("融合戦略変更: {StrategyName}", strategy.StrategyName);
    }

    public async Task<EnsembleOcrResults> RecognizeWithDetailsAsync(
        IImage image, 
        IProgress<OcrProgress>? progress = null, 
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("アンサンブルOCR認識開始: {Width}x{Height}, エンジン数={EngineCount}",
            image.Width, image.Height, _engines.Count(e => e.IsEnabled));

        try
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("アンサンブルエンジンが初期化されていません");
            }

            var activeEngines = _engines.Where(e => e.IsEnabled).ToList();
            if (activeEngines.Count == 0)
            {
                throw new InvalidOperationException("有効なエンジンがありません");
            }

            // 各エンジンで並列処理実行
            var individualResults = await ExecuteEnginesInParallelAsync(activeEngines, image, progress, cancellationToken);

            // 結果融合
            var fusionParameters = _fusionStrategy.GetRecommendedParameters(activeEngines);
            var ensembleResults = await _fusionStrategy.FuseResultsAsync(individualResults, fusionParameters, cancellationToken);

            // 統計更新
            UpdateEngineStatistics(individualResults);

            _logger.LogInformation(
                "アンサンブルOCR認識完了: {FinalRegions}領域, 総時間={TotalMs}ms, 融合戦略={Strategy}",
                ensembleResults.TextRegions.Count, sw.ElapsedMilliseconds, _fusionStrategy.StrategyName);

            return ensembleResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アンサンブルOCR認識中にエラーが発生しました");
            
            // フォールバック: 最も重みの大きいエンジンで処理
            return await ExecuteFallbackRecognitionAsync(image, progress, cancellationToken, sw.Elapsed);
        }
    }

    public EnsemblePerformanceStats GetEnsembleStats()
    {
        var totalExecutions = _engineStats.Values.Sum(s => s.TotalExecutions);
        var averageTime = _engineStats.Values.Count > 0 
            ? _engineStats.Values.Average(s => s.AverageProcessingTime) 
            : 0;

        var engineStatsDict = _engineStats.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value);

        var fusionStrategyUsage = new Dictionary<string, int>
        {
            [_fusionStrategy.StrategyName] = totalExecutions
        };

        return new EnsemblePerformanceStats(
            totalExecutions,
            averageTime,
            0.0, // 改善率は別途計算が必要
            0.0, // 合意率は別途計算が必要
            engineStatsDict,
            fusionStrategyUsage);
    }

    #endregion

    #region IOcrEngine Implementation

    public async Task<bool> InitializeAsync(OcrEngineSettings? settings = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("アンサンブルエンジン初期化開始");
        _currentSettings = settings;

        try
        {
            var initializationTasks = _engines.Select(async engineInfo =>
            {
                try
                {
                    var result = await engineInfo.Engine.InitializeAsync(settings, cancellationToken);
                    _logger.LogDebug("エンジン初期化結果: {EngineName}={Result}", 
                        engineInfo.EngineName, result);
                    return (engineInfo.EngineName, result);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "エンジン初期化エラー: {EngineName}", engineInfo.EngineName);
                    return (engineInfo.EngineName, false);
                }
            });

            var results = await Task.WhenAll(initializationTasks);
            var successCount = results.Count(r => r.Item2);

            _isInitialized = successCount > 0;

            if (_isInitialized)
            {
                _logger.LogInformation("アンサンブルエンジン初期化完了: {Success}/{Total}エンジン成功",
                    successCount, _engines.Count);
            }
            else
            {
                _logger.LogError("アンサンブルエンジン初期化失敗: すべてのエンジンの初期化に失敗");
            }

            return _isInitialized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アンサンブルエンジン初期化中にエラーが発生しました");
            return false;
        }
    }

    public async Task<OcrResults> RecognizeAsync(IImage image, IProgress<OcrProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var ensembleResults = await RecognizeWithDetailsAsync(image, progress, cancellationToken);
        
        // EnsembleOcrResultsからOcrResultsに変換
        return new OcrResults(
            ensembleResults.TextRegions,
            ensembleResults.SourceImage,
            ensembleResults.ProcessingTime,
            ensembleResults.LanguageCode,
            ensembleResults.RegionOfInterest,
            ensembleResults.Text);
    }

    public async Task<OcrResults> RecognizeAsync(IImage image, Rectangle? region = null, IProgress<OcrProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // 簡易実装：領域指定は無視してフル画像で処理
        return await RecognizeAsync(image, progress, cancellationToken);
    }

    public OcrEngineSettings GetSettings()
    {
        return _currentSettings ?? new OcrEngineSettings();
    }

    public async Task ApplySettingsAsync(OcrEngineSettings settings, CancellationToken cancellationToken = default)
    {
        _currentSettings = settings;

        var tasks = _engines.Select(async engineInfo =>
        {
            try
            {
                await engineInfo.Engine.ApplySettingsAsync(settings, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "エンジン設定適用エラー: {EngineName}", engineInfo.EngineName);
            }
        });

        await Task.WhenAll(tasks);
    }

    public IReadOnlyList<string> GetAvailableLanguages()
    {
        var allLanguages = new HashSet<string>();
        
        foreach (var engineInfo in _engines)
        {
            try
            {
                var languages = engineInfo.Engine.GetAvailableLanguages();
                foreach (var lang in languages)
                {
                    allLanguages.Add(lang);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "言語取得エラー: {EngineName}", engineInfo.EngineName);
            }
        }

        return allLanguages.ToList();
    }

    public IReadOnlyList<string> GetAvailableModels()
    {
        var allModels = new HashSet<string>();
        
        foreach (var engineInfo in _engines)
        {
            try
            {
                var models = engineInfo.Engine.GetAvailableModels();
                foreach (var model in models)
                {
                    allModels.Add(model);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "モデル取得エラー: {EngineName}", engineInfo.EngineName);
            }
        }

        return allModels.ToList();
    }

    public async Task<bool> IsLanguageAvailableAsync(string language, CancellationToken cancellationToken = default)
    {
        var tasks = _engines.Select(async engineInfo =>
        {
            try
            {
                return await engineInfo.Engine.IsLanguageAvailableAsync(language, cancellationToken);
            }
            catch
            {
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.Any(r => r);
    }

    public OcrPerformanceStats GetPerformanceStats()
    {
        if (_engines.Count == 0)
        {
            return new OcrPerformanceStats();
        }

        var totalExecutions = _engineStats.Values.Sum(s => s.TotalExecutions);
        var averageTime = _engineStats.Values.Count > 0 
            ? _engineStats.Values.Average(s => s.AverageProcessingTime) 
            : 0;

        return new OcrPerformanceStats
        {
            // 基本的な統計のみ実装
        };
    }

    public void Dispose()
    {
        foreach (var engineInfo in _engines)
        {
            try
            {
                engineInfo.Engine?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "エンジン破棄エラー: {EngineName}", engineInfo.EngineName);
            }
        }
        
        _engines.Clear();
        _engineStats.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var engineInfo in _engines)
        {
            try
            {
                if (engineInfo.Engine is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else if (engineInfo.Engine is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "エンジン非同期破棄エラー: {EngineName}", engineInfo.EngineName);
            }
        }

        _engines.Clear();
        _engineStats.Clear();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// エンジンを並列で実行
    /// </summary>
    private async Task<List<IndividualEngineResult>> ExecuteEnginesInParallelAsync(
        List<EnsembleEngineInfo> activeEngines,
        IImage image,
        IProgress<OcrProgress>? progress,
        CancellationToken cancellationToken)
    {
        var results = new List<IndividualEngineResult>();
        var tasks = activeEngines.Select(async engineInfo =>
        {
            var engineSw = Stopwatch.StartNew();
            
            try
            {
                var result = await engineInfo.Engine.RecognizeAsync(image, progress, cancellationToken);
                engineSw.Stop();

                return new IndividualEngineResult(
                    engineInfo.EngineName,
                    engineInfo.Role,
                    result,
                    engineSw.Elapsed,
                    engineInfo.Weight,
                    true);
            }
            catch (Exception ex)
            {
                engineSw.Stop();
                _logger.LogWarning(ex, "エンジン実行エラー: {EngineName}", engineInfo.EngineName);

                return new IndividualEngineResult(
                    engineInfo.EngineName,
                    engineInfo.Role,
                    new OcrResults(new List<OcrTextRegion>(), image, engineSw.Elapsed, null),
                    engineSw.Elapsed,
                    engineInfo.Weight,
                    false,
                    ex.Message);
            }
        });

        var engineResults = await Task.WhenAll(tasks);
        results.AddRange(engineResults);

        return results;
    }

    /// <summary>
    /// フォールバック認識を実行
    /// </summary>
    private async Task<EnsembleOcrResults> ExecuteFallbackRecognitionAsync(
        IImage image,
        IProgress<OcrProgress>? progress,
        CancellationToken cancellationToken,
        TimeSpan processingTime)
    {
        try
        {
            var fallbackEngine = _engines
                .Where(e => e.IsEnabled)
                .OrderByDescending(e => e.Weight)
                .FirstOrDefault();

            if (fallbackEngine != null)
            {
                _logger.LogInformation("フォールバック実行: {EngineName}", fallbackEngine.EngineName);
                
                var result = await fallbackEngine.Engine.RecognizeAsync(image, progress, cancellationToken);
                
                return new EnsembleOcrResults(
                    result.TextRegions,
                    result.SourceImage,
                    processingTime,
                    result.LanguageCode,
                    result.RegionOfInterest,
                    result.Text)
                {
                    IndividualResults = [],
                    FusionDetails = new ResultFusionDetails(0, 0, 0, 0, 0, []),
                    FusionStrategy = "Fallback",
                    EnsembleConfidence = result.TextRegions.Count > 0 ? result.TextRegions.Average(r => r.Confidence) : 0,
                    EnsembleProcessingTime = processingTime
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "フォールバック実行中にもエラーが発生しました");
        }

        // 完全な失敗の場合は空の結果を返す
        return new EnsembleOcrResults(
            [],
            image,
            processingTime,
            "unknown")
        {
            IndividualResults = [],
            FusionDetails = new ResultFusionDetails(0, 0, 0, 0, 0, []),
            FusionStrategy = "Failed",
            EnsembleConfidence = 0,
            EnsembleProcessingTime = processingTime
        };
    }

    /// <summary>
    /// エンジン統計を更新
    /// </summary>
    private void UpdateEngineStatistics(List<IndividualEngineResult> results)
    {
        foreach (var result in results)
        {
            if (_engineStats.TryGetValue(result.EngineName, out var currentStats))
            {
                var newStats = new EnsembleEngineStats(
                    currentStats.TotalExecutions + 1,
                    (currentStats.AverageProcessingTime * currentStats.TotalExecutions + result.ProcessingTime.TotalMilliseconds) / (currentStats.TotalExecutions + 1),
                    result.IsSuccessful && result.Results.TextRegions.Count > 0 
                        ? (currentStats.AverageConfidence * currentStats.TotalExecutions + result.Results.TextRegions.Average(r => r.Confidence)) / (currentStats.TotalExecutions + 1)
                        : currentStats.AverageConfidence,
                    result.IsSuccessful 
                        ? (currentStats.SuccessRate * currentStats.TotalExecutions + 1.0) / (currentStats.TotalExecutions + 1)
                        : (currentStats.SuccessRate * currentStats.TotalExecutions) / (currentStats.TotalExecutions + 1),
                    DateTime.UtcNow);

                _engineStats.TryUpdate(result.EngineName, newStats, currentStats);
            }
        }
    }

    #endregion
}