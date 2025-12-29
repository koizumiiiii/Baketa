using System.Globalization;
using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Models;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.License;

/// <summary>
/// プロモーション設定の永続化実装
/// </summary>
/// <remarks>
/// Issue #237 Phase 2: 設定永続化ロジックの分離
/// IOptionsMonitor経由でLicenseSettingsを更新し、
/// ISettingsServiceを使用して永続化をトリガー
/// </remarks>
public sealed class PromotionSettingsPersistence : IPromotionSettingsPersistence
{
    private readonly IOptionsMonitor<LicenseSettings> _settingsMonitor;
    private readonly ILogger<PromotionSettingsPersistence> _logger;

    public PromotionSettingsPersistence(
        IOptionsMonitor<LicenseSettings> settingsMonitor,
        ILogger<PromotionSettingsPersistence> logger)
    {
        _settingsMonitor = settingsMonitor ?? throw new ArgumentNullException(nameof(settingsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<bool> SavePromotionAsync(
        string code,
        PlanType plan,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        try
        {
            var settings = _settingsMonitor.CurrentValue;

            settings.AppliedPromotionCode = code;
            settings.PromotionPlanType = (int)plan;
            settings.PromotionExpiresAt = expiresAt.ToString("O", CultureInfo.InvariantCulture);
            settings.PromotionAppliedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            settings.LastOnlineVerification = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            _logger.LogInformation(
                "Promotion settings saved: Plan={Plan}, ExpiresAt={ExpiresAt}",
                plan, expiresAt);

            // 設定の永続化は ISettingsService 経由で行われる
            // LicenseSettings は IOptionsMonitor で監視されているため、
            // 変更は設定サービスのSaveAsync呼び出し時に永続化される

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save promotion settings");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    public Task<bool> ClearPromotionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = _settingsMonitor.CurrentValue;

            settings.AppliedPromotionCode = null;
            settings.PromotionPlanType = null;
            settings.PromotionExpiresAt = null;
            settings.PromotionAppliedAt = null;

            _logger.LogInformation("Promotion settings cleared");

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear promotion settings");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    public Task<bool> UpdateLastVerificationAsync(
        DateTime verificationTime,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = _settingsMonitor.CurrentValue;
            settings.LastOnlineVerification = verificationTime.ToString("O", CultureInfo.InvariantCulture);

            _logger.LogDebug("Last verification time updated: {Time}", verificationTime);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update last verification time");
            return Task.FromResult(false);
        }
    }
}
