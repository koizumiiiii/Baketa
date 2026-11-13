using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models;
using Language = Baketa.Core.Models.Translation.Language;
using Baketa.Core.Translation.Models;

namespace Baketa.Infrastructure.Translation.Cloud;

/// <summary>Routes the existing OCR text path, preserving batch order and the local fallback.</summary>
public sealed class SelectedTextTranslationEngine(
    ITranslationEngine local, GoogleTranslationEngine google, ITranslationMethodService methods) : IBatchTranslationEngine, IUnloadableTranslationEngine
{
    private ITranslationEngine Selected => methods.Current == TranslationMethod.Google ? google : local;
    // Model lifecycle work must bypass translation-method routing.
    internal ITranslationEngine LocalEngine => local;
    public string Name => Selected.Name;
    public IReadOnlyList<string> Aliases => local.Aliases.Append(local.Name).Append(google.Name).Distinct().ToArray();
    public string Description => Selected.Description;
    public bool RequiresNetwork => Selected.RequiresNetwork;
    public Task<bool> IsReadyAsync() => local.IsReadyAsync();
    public Task<bool> InitializeAsync() => local.InitializeAsync();
    public async Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
    {
        var engine = Selected;
        if (engine == local && IsAuto(languagePair.SourceLanguage))
            return (await local.GetSupportedLanguagePairsAsync().ConfigureAwait(false))
                .Any(pair => pair.TargetLanguage.Equals(languagePair.TargetLanguage));
        return await engine.SupportsLanguagePairAsync(languagePair).ConfigureAwait(false);
    }
    public Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync() => Selected.GetSupportedLanguagePairsAsync();
    public async Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default) =>
        (await TranslateBatchAsync([request], cancellationToken).ConfigureAwait(false))[0];
    public async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(IReadOnlyList<TranslationRequest> requests, CancellationToken cancellationToken = default)
    {
        var engine = Selected;
        if (engine == local)
            return await TranslateLocalAsync(requests, cancellationToken).ConfigureAwait(false);
        var results = await engine.TranslateBatchAsync(requests, cancellationToken).ConfigureAwait(false);
        if (engine == google && results.Any(r => !r.IsSuccess))
            return await TranslateLocalAsync(requests, cancellationToken).ConfigureAwait(false);
        return results;
    }

    private async Task<IReadOnlyList<TranslationResponse>> TranslateLocalAsync(
        IReadOnlyList<TranslationRequest> requests, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = new List<(TranslationRequest request, int index)>();
        var results = new TranslationResponse[requests.Count];
        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            if (IsAuto(request.SourceLanguage))
            {
                var source = ResolveLocalSource(request.SourceText);
                if (source == null && request.TargetLanguage.Equals(Language.English))
                {
                    results[i] = TranslationResponse.CreateError(request,
                        new TranslationError { ErrorCode = "SOURCE_LANGUAGE_UNDETERMINED", Message = "Cannot determine the source language for local translation to English." }, local.Name);
                    continue;
                }
                var copy = request.Clone();
                copy.PreferredEngine = request.PreferredEngine;
                copy.SourceLanguage = source ?? Language.English;
                request = copy;
                // Only a recognized script can justify a same-language success.
                if (source != null && source.Equals(request.TargetLanguage))
                {
                    results[i] = TranslationResponse.CreateSuccess(request, request.SourceText, local.Name, 0);
                    continue;
                }
            }
            pending.Add((request, i));
        }
        if (pending.Count > 0)
        {
            var translated = await local.TranslateBatchAsync(pending.Select(item => item.request).ToArray(), cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < pending.Count; i++) results[pending[i].index] = translated[i];
        }
        for (var i = 0; i < results.Length; i++) results[i].RequestId = requests[i].RequestId;
        return results;
    }

    private static bool IsAuto(Language language) => language.IsAutoDetect || language.Code.Equals("auto", StringComparison.OrdinalIgnoreCase);

    // The existing OCR path forwards "auto" unchanged. NLLB requires a concrete
    // source: preserve the existing script heuristics, leaving unknown text unresolved.
    private static Language? ResolveLocalSource(string text)
    {
        if (text.Any(c => c is >= '\u3040' and <= '\u30ff')) return Language.Japanese;
        if (text.Any(c => c is >= '\uac00' and <= '\ud7af' or >= '\u1100' and <= '\u11ff')) return Language.Korean;
        if (text.Any(c => c is >= '\u4e00' and <= '\u9fff')) return Language.ChineseSimplified;
        if (text.Any(c => c is >= '\u0400' and <= '\u04ff')) return Language.Russian;
        if (text.Any(c => c is >= '\u0600' and <= '\u06ff')) return Language.Arabic;
        return null;
    }
    public Task UnloadModelsAsync() => local is IUnloadableTranslationEngine unloadable ? unloadable.UnloadModelsAsync() : Task.CompletedTask;
    // Engines are owned and disposed by the DI container.
    public void Dispose() { }
}
