using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Events;
using Baketa.Core.License.Models;
using Baketa.Core.Translation.Abstractions;

namespace Baketa.Infrastructure.License.Services;

/// <summary>Permanent local entitlement, independent of accounts and subscriptions.</summary>
public sealed class LocalLicenseManager : ILicenseManager, ILicenseInfoProvider
{
    public LicenseState CurrentState { get; } = new()
    {
        CurrentPlan = PlanType.Ultimate,
        ServerMonthlyTokenLimit = long.MaxValue
    };

    public string? CurrentSessionId => null;
    public bool IsFeatureAvailable(FeatureType feature) => true;
    public Task<LicenseState> GetCurrentStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentState);
    public Task<LicenseState> RefreshStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentState);
    public Task<LicenseState> ForceRefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentState);
    public Task<TokenConsumptionResult> ConsumeCloudAiTokensAsync(int tokenCount, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(TokenConsumptionResult.CreateSuccess(0, long.MaxValue));
    public Task<bool> SetTestPlanAsync(PlanType plan, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public void SetResolvedLicenseState(LicenseState state, string source, LicenseChangeReason reason) { }
    public void SyncTokenUsage(long tokensUsed) { }
    public void SyncMonthlyUsageFromServer(ServerMonthlyUsage monthlyUsage) { }
    public void NotifyBonusTokensLoaded() { }
    public void SetSessionToken(string? sessionToken) { }
    public void SetUserId(string? userId) { }
    public event EventHandler<LicenseStateChangedEventArgs>? StateChanged { add { } remove { } }
    public event EventHandler<TokenUsageWarningEventArgs>? TokenUsageWarning { add { } remove { } }
    public event EventHandler<SessionInvalidatedEventArgs>? SessionInvalidated { add { } remove { } }
    public event EventHandler<PlanExpirationWarningEventArgs>? PlanExpirationWarning { add { } remove { } }
}
