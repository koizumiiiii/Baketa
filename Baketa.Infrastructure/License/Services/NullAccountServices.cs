using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Models;

namespace Baketa.Infrastructure.License.Services;

/// <summary>Retains legacy account contracts without loading credentials or contacting services.</summary>
public sealed class NullAccountServices : IPatreonOAuthService, IPatreonCallbackHandler, IPromotionCodeService, IBonusTokenService
{
    public bool IsAuthenticated => false;
    public PatreonSyncStatus SyncStatus => PatreonSyncStatus.NotConnected;
    public DateTime? LastSyncTime => null;
    public string? PatreonUserId => null;
    public string GenerateSecureState() => string.Empty;
    public bool ValidateState(string state) => false;
    public string GenerateAuthorizationUrl(string state) => throw new NotSupportedException("Accounts are disabled.");
    public Task<PatreonAuthResult> HandleCallbackAsync(string authorizationCode, string state, CancellationToken cancellationToken = default) =>
        Task.FromResult(PatreonAuthResult.CreateFailure("local_only", "Accounts are disabled."));
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ClearLocalCredentialsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<PatreonLocalCredentials?> LoadCredentialsAsync(CancellationToken cancellationToken = default) => Task.FromResult<PatreonLocalCredentials?>(null);
    public Task<string?> GetSessionTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    public Task<PatreonSyncResult> SyncLicenseAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) =>
        Task.FromResult(PatreonSyncResult.CreateError(PatreonSyncStatus.NotConnected, "Accounts are disabled."));
    public event EventHandler<PatreonStatusChangedEventArgs>? StatusChanged { add { } remove { } }
    public bool CanHandle(string url) => false;
    public Task<PatreonAuthResult> HandleCallbackUrlAsync(string callbackUrl, CancellationToken cancellationToken = default) => HandleCallbackAsync(string.Empty, string.Empty, cancellationToken);
    public Task<PromotionCodeResult> ApplyCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(PromotionCodeResult.CreateFailure(PromotionErrorCode.AlreadyProOrHigher, "All local features are available."));
    public PromotionInfo? GetCurrentPromotion() => null;
    public bool ValidateCodeFormat(string code) => false;
    public event EventHandler<PromotionStateChangedEventArgs>? PromotionStateChanged { add { } remove { } }
    public Task<ServerSyncResult> SyncFromServerAsync(string accessToken, CancellationToken cancellationToken = default) => Task.FromResult(ServerSyncResult.NoDataFound);
    public IReadOnlyList<BonusToken> GetBonusTokens() => [];
    public long GetTotalRemainingTokens() => 0;
    public long ConsumeTokens(long amount) => 0;
    public long GetConsumeableAmount(long amount) => 0;
    public Task<BonusSyncResult> FetchFromServerAsync(string accessToken, CancellationToken cancellationToken = default) => Task.FromResult(new BonusSyncResult { Success = false });
    public Task<BonusSyncResult> SyncToServerAsync(string accessToken, CancellationToken cancellationToken = default) => Task.FromResult(new BonusSyncResult { Success = false });
    public event EventHandler<BonusTokensChangedEventArgs>? BonusTokensChanged { add { } remove { } }
    public bool HasPendingSync => false;
    public bool WelcomeBonusJustGranted { get => false; set { } }
    public long WelcomeBonusAmount { get => 0; set { } }
    public void ApplySyncedData(IReadOnlyList<BonusTokenInfo> bonuses, long totalRemaining) { }
}
