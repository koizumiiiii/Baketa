using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Language = Baketa.Core.Models.Translation.Language;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Strategies;

/// <summary>
/// バッチ翻訳戦略
/// 大規模リクエスト（10件以上）をバッチ処理で高効率化
/// Issue #147 Phase 3.2: Phase 2のバッチエンジンを活用
/// </summary>
public sealed class BatchTranslationStrategy(
    ITranslationEngine translationEngine,
    HybridStrategySettings settings,
    ILogger<BatchTranslationStrategy> logger,
    ILanguageConfigurationService languageConfig) : ITranslationStrategy
{
    private readonly ITranslationEngine _translationEngine = translationEngine ?? throw new ArgumentNullException(nameof(translationEngine));
    private readonly HybridStrategySettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ILogger<BatchTranslationStrategy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ILanguageConfigurationService _languageConfig = languageConfig ?? throw new ArgumentNullException(nameof(languageConfig));

    public int Priority => 100; // 最高優先度

    public bool CanHandle(TranslationStrategyContext context)
    {
        // 大規模バッチ処理に適用
        return context.IsBatchRequest
               && context.TextCount >= _settings.BatchThreshold;
    }

    public async Task<TranslationResult> ExecuteAsync(
        string text,
        string? sourceLanguage,
        string? targetLanguage,
        CancellationToken cancellationToken = default)
    {
        // 単一要求でもバッチ処理を使用（パフォーマンス特性の一貫性のため）
        _logger.LogDebug("🚀 バッチ戦略で単一翻訳実行 - テキスト長: {Length}文字", text.Length);

        var results = await ExecuteBatchAsync(
            [text], sourceLanguage, targetLanguage, cancellationToken);

        return results.FirstOrDefault() ?? new TranslationResult(
            OriginalText: text,
            TranslatedText: string.Empty,
            Success: false,
            ErrorMessage: "バッチ処理から結果が返されませんでした");
    }

    public async Task<IReadOnlyList<TranslationResult>> ExecuteBatchAsync(
        IReadOnlyList<string> texts,
        string? sourceLanguage,
        string? targetLanguage,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🚀 バッチ翻訳戦略実行 - 件数: {Count}, 閾値: {Threshold}",
            texts.Count, _settings.BatchThreshold);

        try
        {
            // 言語モデルを作成（言語設定サービスから取得）
            var languagePair = _languageConfig.GetCurrentLanguagePair();
            var defaultSourceLanguage = languagePair.SourceCode;
            var defaultTargetLanguage = languagePair.TargetCode;
            var sourceLanguageModel = Language.FromCode(sourceLanguage ?? defaultSourceLanguage);
            var targetLanguageModel = Language.FromCode(targetLanguage ?? defaultTargetLanguage);

            // IBatchTranslationEngineインターフェースを実装している翻訳エンジンのバッチ機能を使用
            if (_translationEngine is IBatchTranslationEngine batchEngine)
            {
                _logger.LogDebug("🚀 IBatchTranslationEngineインターフェースを使用してバッチ処理実行");

                // TranslationRequestリストを作成
                var requests = texts.Select(text => new TranslationRequest
                {
                    SourceText = text,
                    SourceLanguage = sourceLanguageModel,
                    TargetLanguage = targetLanguageModel
                }).ToList();

                var batchResults = await batchEngine.TranslateBatchAsync(
                    requests, cancellationToken);

                // バッチ結果をTranslationResultに変換
                var results = new List<TranslationResult>();
                for (int i = 0; i < texts.Count; i++)
                {
                    var originalText = texts[i];
                    TranslationResult result;

                    if (i < batchResults.Count)
                    {
                        var batchResult = batchResults[i];
                        result = new TranslationResult(
                            OriginalText: originalText,
                            TranslatedText: batchResult.TranslatedText ?? string.Empty,
                            Success: batchResult.IsSuccess,
                            ErrorMessage: batchResult.IsSuccess ? null : batchResult.Error?.Message);
                    }
                    else
                    {
                        // 結果不足の場合
                        result = new TranslationResult(
                            OriginalText: originalText,
                            TranslatedText: string.Empty,
                            Success: false,
                            ErrorMessage: "バッチ処理結果が不足しています");
                    }

                    results.Add(result);
                }

                _logger.LogInformation("🚀 バッチ翻訳完了 - 成功: {Success}/{Total}",
                    results.Count(r => r.Success), results.Count);

                return results;
            }
            else
            {
                _logger.LogWarning("翻訳エンジンがIBatchTranslationEngineを実装していません。単一処理にフォールバック");

                // バッチ機能がない場合は単一処理にフォールバック
                var results = new List<TranslationResult>();
                foreach (var text in texts)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        var request = new TranslationRequest
                        {
                            SourceText = text,
                            SourceLanguage = sourceLanguageModel,
                            TargetLanguage = targetLanguageModel
                        };

                        var result = await _translationEngine.TranslateAsync(request, cancellationToken);

                        results.Add(new TranslationResult(
                            OriginalText: text,
                            TranslatedText: result.TranslatedText ?? string.Empty,
                            Success: result.IsSuccess,
                            ErrorMessage: result.IsSuccess ? null : result.Error?.Message));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "バッチ戦略のフォールバック処理でエラー");

                        results.Add(new TranslationResult(
                            OriginalText: text,
                            TranslatedText: string.Empty,
                            Success: false,
                            ErrorMessage: $"フォールバック処理エラー: {ex.Message}"));
                    }
                }

                return results;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "バッチ翻訳戦略でエラーが発生しました");

            // 全件エラーとして返す
            return [..texts.Select(t => new TranslationResult(
                OriginalText: t,
                TranslatedText: string.Empty,
                Success: false,
                ErrorMessage: $"バッチ処理エラー: {ex.Message}"
            ))];
        }
    }
}

