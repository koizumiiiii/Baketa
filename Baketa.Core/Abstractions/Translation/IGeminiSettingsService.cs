using Baketa.Core.Models;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>BYOK credentials are stored separately from exportable application settings.</summary>
public interface IGeminiSettingsService
{
    bool HasApiKey { get; }
    bool PreferGemini { get; }
    string Model { get; }
    long Revision { get; }
    /// <summary>Advances after every successful explicit key save, including an unchanged key.</summary>
    long ApiKeySaveRevision { get; }
    event EventHandler? Changed;
    string? GetApiKey();
    Task SaveApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);
    Task DeleteApiKeyAsync(CancellationToken cancellationToken = default);
    Task SetOptionsAsync(string model, bool preferGemini, CancellationToken cancellationToken = default);
}

public interface IGeminiConnectionService
{
    Task<GeminiConnectionResult> TestConnectionAsync(string? apiKey = null, CancellationToken cancellationToken = default);
}

/// <summary>Only a resource identifier crosses the infrastructure/UI boundary.</summary>
public interface IGeminiNotificationService
{
    Task NotifyAsync(string resourceKey);
}
