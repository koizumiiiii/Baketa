using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Baketa.Core.Abstractions.Translation;
using Baketa.Infrastructure.Translation.Metrics;

namespace Baketa.Infrastructure.Translation.Strategies;

/// <summary>
/// ハイブリッド翻訳戦略
/// リクエストの特性に応じて最適な戦略を自動選択
/// Issue #147 Phase 3.2
/// </summary>
public sealed class HybridTranslationStrategy : IDisposable
{
    private readonly IReadOnlyList<ITranslationStrategy> _strategies;
    private readonly ILogger<HybridTranslationStrategy> _logger;
    private readonly TranslationMetricsCollector _metricsCollector;
    private readonly HybridStrategySettings _settings;
    private bool _disposed;

    public HybridTranslationStrategy(
        IEnumerable<ITranslationStrategy> strategies,
        TranslationMetricsCollector metricsCollector,
        HybridStrategySettings settings,
        ILogger<HybridTranslationStrategy> logger)
    {
        _strategies = [..strategies.OrderByDescending(s => s.Priority)];
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _logger.LogInformation("🚀 HybridTranslationStrategy初期化 - 戦略数: {StrategyCount}", _strategies.Count);
    }

    /// <summary>
    /// 単一テキストの翻訳
    /// </summary>
    public async Task<TranslationResult> TranslateAsync(
        string text,
        string? sourceLanguage,
        string? targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TranslationResult(text, string.Empty, false, "入力テキストが空です");
        }

        var stopwatch = Stopwatch.StartNew();
        var context = CreateContext(text);
        
        try
        {
            var strategy = SelectStrategy(context);
            _logger.LogDebug("選択された戦略: {StrategyType} (テキスト長: {Length}文字)", 
                strategy.GetType().Name, text.Length);

            var result = await strategy.ExecuteAsync(
                text, sourceLanguage, targetLanguage, cancellationToken);

            stopwatch.Stop();
            
            // メトリクス記録
            _metricsCollector.RecordTranslation(new TranslationMetrics
            {
                Strategy = strategy.GetType().Name,
                TextLength = text.Length,
                ProcessingTime = stopwatch.Elapsed,
                Success = result.Success,
                Timestamp = DateTime.UtcNow
            });

            return result with { ProcessingTime = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳処理中にエラーが発生しました");
            stopwatch.Stop();
            
            _metricsCollector.RecordError(ex, context);
            
            return new TranslationResult(
                text, 
                string.Empty, 
                false, 
                $"翻訳エラー: {ex.Message}",
                stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// バッチ翻訳
    /// </summary>
    public async Task<IReadOnlyList<TranslationResult>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string? sourceLanguage,
        string? targetLanguage,
        CancellationToken cancellationToken = default)
    {
        if (texts == null || texts.Count == 0)
        {
            return [];
        }

        var stopwatch = Stopwatch.StartNew();
        var context = CreateBatchContext(texts);
        
        try
        {
            var strategy = SelectStrategy(context);
            _logger.LogInformation("🎯 バッチ翻訳戦略選択: {StrategyType} (件数: {Count}, 合計文字数: {TotalChars})",
                strategy.GetType().Name, texts.Count, context.TotalCharacterCount);

            var results = await strategy.ExecuteBatchAsync(
                texts, sourceLanguage, targetLanguage, cancellationToken);

            stopwatch.Stop();
            
            // バッチメトリクス記録
            _metricsCollector.RecordBatchTranslation(new BatchTranslationMetrics
            {
                Strategy = strategy.GetType().Name,
                TextCount = texts.Count,
                TotalCharacterCount = context.TotalCharacterCount,
                ProcessingTime = stopwatch.Elapsed,
                SuccessCount = results.Count(r => r.Success),
                FailureCount = results.Count(r => !r.Success),
                Timestamp = DateTime.UtcNow
            });

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "バッチ翻訳処理中にエラーが発生しました");
            stopwatch.Stop();
            
            _metricsCollector.RecordError(ex, context);
            
            // 全件エラーとして返す
            return [..texts.Select(t => new TranslationResult(
                t,
                string.Empty,
                false,
                $"バッチ翻訳エラー: {ex.Message}",
                stopwatch.Elapsed))];
        }
    }

    /// <summary>
    /// 最適な戦略を選択
    /// </summary>
    private ITranslationStrategy SelectStrategy(TranslationStrategyContext context)
    {
        // 処理可能な戦略を優先度順に評価
        foreach (var strategy in _strategies)
        {
            if (strategy.CanHandle(context))
            {
                _logger.LogDebug("戦略選択完了: {StrategyType}", strategy.GetType().Name);
                return strategy;
            }
        }

        // フォールバック：最も優先度の低い戦略を使用
        var fallback = _strategies.LastOrDefault() 
            ?? throw new InvalidOperationException("利用可能な翻訳戦略がありません");
            
        _logger.LogWarning("フォールバック戦略を使用: {StrategyType}", fallback.GetType().Name);
        return fallback;
    }

    /// <summary>
    /// 単一テキスト用コンテキスト作成
    /// </summary>
    private TranslationStrategyContext CreateContext(string text)
    {
        return new TranslationStrategyContext(
            TextCount: 1,
            TotalCharacterCount: text.Length,
            IsBatchRequest: false,
            AverageTextLength: text.Length);
    }

    /// <summary>
    /// バッチ用コンテキスト作成
    /// </summary>
    private TranslationStrategyContext CreateBatchContext(IReadOnlyList<string> texts)
    {
        var totalChars = texts.Sum(t => t?.Length ?? 0);
        var avgLength = texts.Count > 0 ? (double)totalChars / texts.Count : 0;
        
        return new TranslationStrategyContext(
            TextCount: texts.Count,
            TotalCharacterCount: totalChars,
            IsBatchRequest: true,
            AverageTextLength: avgLength);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        foreach (var strategy in _strategies.OfType<IDisposable>())
        {
            strategy.Dispose();
        }
        
        _metricsCollector?.Dispose();
        
        _disposed = true;
    }
}

/// <summary>
/// ハイブリッド戦略設定
/// </summary>
public class HybridStrategySettings
{
    /// <summary>
    /// バッチ処理閾値（これ以上の件数でバッチ戦略を使用）
    /// </summary>
    public int BatchThreshold { get; set; } = 5;
    
    /// <summary>
    /// 並列処理閾値（これ以上の件数で並列戦略を使用）
    /// </summary>
    public int ParallelThreshold { get; set; } = 2;
    
    /// <summary>
    /// 最大並列度
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;
    
    /// <summary>
    /// メトリクス有効化
    /// </summary>
    public bool EnableMetrics { get; set; } = true;
}