using Baketa.Core.Models;

namespace Baketa.Core.Abstractions.Translation;

public interface IGoogleTranslationSettingsService
{
    bool HasApiKey { get; }
    long ApiKeySaveRevision { get; }
    event EventHandler? Changed;
    string? GetApiKey();
    Task SaveApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);
    Task DeleteApiKeyAsync(CancellationToken cancellationToken = default);
}

public interface IGoogleTranslationConnectionService
{
    Task<GoogleTranslationResult> TestConnectionAsync(string apiKey, CancellationToken cancellationToken = default);
}

public interface ITranslationMethodService
{
    TranslationMethod Current { get; }
    bool CanSelect(TranslationMethod method);
    event EventHandler? Changed;
    Task SelectAsync(TranslationMethod method, CancellationToken cancellationToken = default);
    /// <summary>Changes the stored selection only if it still matches expected, atomically with persistence.</summary>
    Task SelectIfCurrentAsync(TranslationMethod expected, TranslationMethod method, CancellationToken cancellationToken = default);
}
