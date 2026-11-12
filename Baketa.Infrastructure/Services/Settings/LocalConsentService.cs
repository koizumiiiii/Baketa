using Baketa.Core.Abstractions.Settings;
using Baketa.Core.License.Models;
using Baketa.Core.Settings;

namespace Baketa.Infrastructure.Services.Settings;

/// <summary>Keeps local consent records while disabling every server sync operation.</summary>
public sealed class LocalConsentService(ConsentService localStore) : IConsentService
{
    public event EventHandler<LegalConsentChangedEventArgs>? ConsentChanged
    {
        add => localStore.ConsentChanged += value;
        remove => localStore.ConsentChanged -= value;
    }

    public Task<ConsentSettings> GetConsentStateAsync(CancellationToken cancellationToken = default) =>
        localStore.GetConsentStateAsync(cancellationToken);
    public Task<bool> NeedsPrivacyPolicyReConsentAsync(CancellationToken cancellationToken = default) =>
        localStore.NeedsPrivacyPolicyReConsentAsync(cancellationToken);
    public Task<bool> NeedsTermsOfServiceReConsentAsync(CancellationToken cancellationToken = default) =>
        localStore.NeedsTermsOfServiceReConsentAsync(cancellationToken);
    public Task<bool> NeedsInitialConsentAsync(CancellationToken cancellationToken = default) =>
        localStore.NeedsInitialConsentAsync(cancellationToken);
    public Task<bool> CanCreateAccountAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public string GetCurrentVersion(ConsentType consentType) => localStore.GetCurrentVersion(consentType);
    public Task AcceptPrivacyPolicyAsync(CancellationToken cancellationToken = default) =>
        localStore.AcceptPrivacyPolicyAsync(cancellationToken);
    public Task AcceptTermsOfServiceAsync(CancellationToken cancellationToken = default) =>
        localStore.AcceptTermsOfServiceAsync(cancellationToken);
    public Task AcceptAllAsync(CancellationToken cancellationToken = default) => localStore.AcceptAllAsync(cancellationToken);
    public Task ClearLocalConsentAsync(CancellationToken cancellationToken = default) => localStore.ClearLocalConsentAsync(cancellationToken);

    public Task RecordConsentToServerAsync(string userId, string accessToken, ConsentType consentType,
        string version, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SyncLocalConsentToServerAsync(string userId, string accessToken,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<ServerSyncResult> SyncFromServerAsync(string accessToken,
        CancellationToken cancellationToken = default) => Task.FromResult(ServerSyncResult.NotAuthenticated);
    public Task<ConsentVerificationState> EnsureConsentValidAsync(string accessToken,
        CancellationToken cancellationToken = default) => Task.FromResult(ConsentVerificationState.LocalOnly);
}
