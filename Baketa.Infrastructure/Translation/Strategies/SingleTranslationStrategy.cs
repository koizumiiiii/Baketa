using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Language = Baketa.Core.Models.Translation.Language;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Strategies;

/// <summary>
/// 単一翻訳戦略
/// 1件ずつ順次処理を行う基本戦略
/// Issue #147 Phase 3.2
/// </summary>
public sealed class SingleTranslationStrategy(
    ITranslationEngine translationEngine,
    ILogger<SingleTranslationStrategy> logger,
    ILanguageConfigurationService languageConfig) : ITranslationStrategy
{
    private readonly ITranslationEngine _translationEngine = translationEngine ?? throw new ArgumentNullException(nameof(translationEngine));
    private readonly ILogger<SingleTranslationStrategy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ILanguageConfigurationService _languageConfig = languageConfig ?? throw new ArgumentNullException(nameof(languageConfig));

    public int Priority => 10; // 最低優先度（フォールバック用）

    public bool CanHandle(TranslationStrategyContext context)
    {
        // 常に処理可能（フォールバック戦略として機能）
        return true;
    }

    public async Task<TranslationResult> ExecuteAsync(
        string text,
        string? sourceLanguage,
        string? targetLanguage,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("🔄 単一翻訳実行 - テキスト長: {Length}文字", text.Length);

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
            _logger.LogError(ex, "単一翻訳でエラーが発生しました");

            return new TranslationResult(
                OriginalText: text,
                TranslatedText: string.Empty,
                Success: false,
                ErrorMessage: $"翻訳エラー: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<TranslationResult>> ExecuteBatchAsync(
        IReadOnlyList<string> texts,
        string? sourceLanguage,
        string? targetLanguage,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔄 単一戦略によるバッチ翻訳 - 件数: {Count}", texts.Count);

        var results = new List<TranslationResult>();

        foreach (var text in texts)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var result = await ExecuteAsync(text, sourceLanguage, targetLanguage, cancellationToken);
            results.Add(result);
        }

        _logger.LogDebug("🔄 単一戦略バッチ翻訳完了 - 成功: {Success}/{Total}",
            results.Count(r => r.Success), results.Count);

        return results;
    }
}
