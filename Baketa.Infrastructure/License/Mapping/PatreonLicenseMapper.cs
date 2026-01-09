using Baketa.Core.License.Models;

namespace Baketa.Infrastructure.License.Mapping;

/// <summary>
/// Patreon同期結果からLicenseStateを構築するマッパー
/// DRY原則に従い、PatreonSyncHostedServiceとAccountSettingsViewModelで共用
/// </summary>
public static class PatreonLicenseMapper
{
    /// <summary>
    /// Patreon同期結果とクレデンシャルからLicenseStateを構築
    /// </summary>
    /// <param name="syncResult">Patreon同期結果</param>
    /// <param name="credentials">Patreonクレデンシャル</param>
    /// <returns>構築されたLicenseState</returns>
    public static LicenseState ToLicenseState(
        PatreonSyncResult syncResult,
        PatreonLocalCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(syncResult);
        ArgumentNullException.ThrowIfNull(credentials);

        return new LicenseState
        {
            CurrentPlan = syncResult.Plan,
            UserId = credentials.PatreonUserId,
            PatreonUserId = credentials.PatreonUserId,
            PatreonSyncStatus = syncResult.FromCache ? PatreonSyncStatus.Offline : PatreonSyncStatus.Synced,
            PatronStatus = credentials.PatronStatus,
            PatreonTierId = credentials.LastKnownTierId,
            ExpirationDate = syncResult.NextChargeDate,
            ContractStartDate = credentials.SessionTokenObtainedAt,
            BillingCycleEnd = syncResult.NextChargeDate,
            LastServerSync = DateTime.UtcNow,
            IsCached = syncResult.FromCache,
            CloudAiTokensUsed = 0 // Patreonではトークン消費管理はクライアント側
        };
    }
}
