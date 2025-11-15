using System.Collections.Concurrent;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Strategies;

/// <summary>
/// 並列翻訳戦略
/// 中規模リクエスト（2-10件）を並列処理
/// Issue #147 Phase 3.2
/// </summary>
public sealed class ParallelTranslationStrategy(
    ITranslationEngine translationEngine,
    HybridStrategySettings settings,
    ILogger<ParallelTranslationStrategy> logger,
    ILanguageConfigurationService languageConfig) : ITranslationStrategy
{
    private readonly ITranslationEngine _translationEngine = translationEngine ?? throw new ArgumentNullException(nameof(translationEngine));
    private readonly HybridStrategySettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ILogger<ParallelTranslationStrategy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ILanguageConfigurationService _languageConfig = languageConfig ?? throw new ArgumentNullException(nameof(languageConfig));

    public int Priority => 50; // 中優先度

    public bool CanHandle(TranslationStrategyContext context)
    {
        // 中規模バッチ処理に適用
        // バッチ処理閾値未満でかつ並列処理閾値以上
        return context.IsBatchRequest
               && context.TextCount >= _settings.ParallelThreshold
               && context.TextCount < _settings.BatchThreshold;
    }

    public async Task<TranslationResult> ExecuteAsync(
        string text,
        string? sourceLanguage,
        string? targetLanguage,
        CancellationToken cancellationToken = default)
    {
        // 単一要求の場合は並列処理の意味がないので、直接実行
        _logger.LogDebug("⚡ 並列戦略で単一翻訳実行 - テキスト長: {Length}文字", text.Length);

        try
        {
            // TranslationRequestを作成（言語設定サービスから取得）
            var languagePair = _languageConfig.GetCurrentLanguagePair();
            var defaultSourceLanguage = languagePair.SourceCode;
            var defaultTargetLanguage = languagePair.TargetCode;
            var sourceLanguageModel = Language.FromCode(sourceLanguage ?? defaultSourceLanguage);
            var targetLanguageModel = Language.FromCode(targetLanguage ?? defaultTargetLanguage);

            var request = new TranslationRequest
            {
                SourceText = text,
                SourceLanguage = sourceLanguageModel,
                TargetLanguage = targetLanguageModel
            };

            var result = await _translationEngine.TranslateAsync(request, cancellationToken);

            return new TranslationResult(
                OriginalText: text,
                TranslatedText: result.TranslatedText ?? string.Empty,
                Success: result.IsSuccess,
                ErrorMessage: result.IsSuccess ? null : result.Error?.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "並列翻訳でエラーが発生しました");

            return new TranslationResult(
                OriginalText: text,
                TranslatedText: string.Empty,
                Success: false,
                ErrorMessage: $"並列翻訳エラー: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<TranslationResult>> ExecuteBatchAsync(
        IReadOnlyList<string> texts,
        string? sourceLanguage,
        string? targetLanguage,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("⚡ 並列翻訳戦略実行 - 件数: {Count}, 並列度: {Parallel}",
            texts.Count, _settings.MaxDegreeOfParallelism);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _settings.MaxDegreeOfParallelism,
            CancellationToken = cancellationToken
        };

        var results = new ConcurrentBag<(int Index, TranslationResult Result)>();

        try
        {
            // 言語モデルを作成（言語設定サービスから取得）
            var languagePair = _languageConfig.GetCurrentLanguagePair();
            var defaultSourceLanguage = languagePair.SourceCode;
            var defaultTargetLanguage = languagePair.TargetCode;
            var sourceLanguageModel = Language.FromCode(sourceLanguage ?? defaultSourceLanguage);
            var targetLanguageModel = Language.FromCode(targetLanguage ?? defaultTargetLanguage);

            await Parallel.ForEachAsync(
                texts.Select((text, index) => new { Text = text, Index = index }),
                options,
                async (item, ct) =>
                {
                    try
                    {
                        var request = new TranslationRequest
                        {
                            SourceText = item.Text,
                            SourceLanguage = sourceLanguageModel,
                            TargetLanguage = targetLanguageModel
                        };

                        var result = await _translationEngine.TranslateAsync(request, ct);

                        var translationResult = new TranslationResult(
                            OriginalText: item.Text,
                            TranslatedText: result.TranslatedText ?? string.Empty,
                            Success: result.IsSuccess,
                            ErrorMessage: result.IsSuccess ? null : result.Error?.Message);

                        results.Add((item.Index, translationResult));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "並列翻訳の個別処理でエラー - インデックス: {Index}", item.Index);

                        var errorResult = new TranslationResult(
                            OriginalText: item.Text,
                            TranslatedText: string.Empty,
                            Success: false,
                            ErrorMessage: $"並列処理エラー: {ex.Message}");

                        results.Add((item.Index, errorResult));
                    }
                });

            // インデックス順にソートして返す
            var sortedResults = results
                .OrderBy(r => r.Index)
                .Select(r => r.Result)
                .ToList();

            _logger.LogInformation("⚡ 並列翻訳完了 - 成功: {Success}/{Total}, 並列度: {Parallel}",
                sortedResults.Count(r => r.Success),
                sortedResults.Count,
                _settings.MaxDegreeOfParallelism);

            return sortedResults;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("並列翻訳がキャンセルされました");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "並列翻訳でエラーが発生しました");

            // 全件エラーとして返す
            return [..texts.Select(t => new TranslationResult(
                OriginalText: t,
                TranslatedText: string.Empty,
                Success: false,
                ErrorMessage: $"並列処理エラー: {ex.Message}"
            ))];
        }
    }
}
