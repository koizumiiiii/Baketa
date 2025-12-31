using System.Globalization;
using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Extensions;
using Baketa.Core.License.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.License;

/// <summary>
/// プロモーション設定の永続化実装
/// </summary>
/// <remarks>
/// Issue #237 C案: IUnifiedSettingsService経由で設定を永続化
/// promotion-settings.json ファイルに保存される
/// </remarks>
public sealed class PromotionSettingsPersistence : IPromotionSettingsPersistence
{
    private readonly IUnifiedSettingsService _unifiedSettingsService;
    private readonly ILogger<PromotionSettingsPersistence> _logger;

    public PromotionSettingsPersistence(
        IUnifiedSettingsService unifiedSettingsService,
        ILogger<PromotionSettingsPersistence> logger)
    {
        _unifiedSettingsService = unifiedSettingsService ?? throw new ArgumentNullException(nameof(unifiedSettingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<bool> SavePromotionAsync(
        string code,
        PlanType plan,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        try
        {
            var promotionSettings = new WritablePromotionSettings
            {
                AppliedPromotionCode = code,
                PromotionPlanType = (int)plan,
                PromotionExpiresAt = expiresAt.ToString("O", CultureInfo.InvariantCulture),
                PromotionAppliedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                LastOnlineVerification = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };

            await _unifiedSettingsService.UpdatePromotionSettingsAsync(promotionSettings, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "[Issue #237] プロモーション設定を保存しました: Plan={Plan}, ExpiresAt={ExpiresAt}",
                plan, expiresAt);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #237] プロモーション設定の保存に失敗しました");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ClearPromotionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 空の設定を保存してクリア
            var emptySettings = new WritablePromotionSettings();
            await _unifiedSettingsService.UpdatePromotionSettingsAsync(emptySettings, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("[Issue #237] プロモーション設定をクリアしました");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #237] プロモーション設定のクリアに失敗しました");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateLastVerificationAsync(
        DateTime verificationTime,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 現在の設定を取得して更新
            var currentSettings = _unifiedSettingsService.GetPromotionSettings();
            var updatedSettings = new WritablePromotionSettings
            {
                AppliedPromotionCode = currentSettings.AppliedPromotionCode,
                PromotionPlanType = currentSettings.PromotionPlanType,
                PromotionExpiresAt = currentSettings.PromotionExpiresAt,
                PromotionAppliedAt = currentSettings.PromotionAppliedAt,
                LastOnlineVerification = verificationTime.ToString("O", CultureInfo.InvariantCulture)
            };

            await _unifiedSettingsService.UpdatePromotionSettingsAsync(updatedSettings, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug("[Issue #237] 最終検証日時を更新しました: {Time}", verificationTime);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #237] 最終検証日時の更新に失敗しました");
            return false;
        }
    }

    /// <summary>
    /// [Issue #237] 書き込み可能なプロモーション設定
    /// </summary>
    private sealed class WritablePromotionSettings : IPromotionSettings
    {
        public string? AppliedPromotionCode { get; init; }
        public int? PromotionPlanType { get; init; }
        public string? PromotionExpiresAt { get; init; }
        public string? PromotionAppliedAt { get; init; }
        public string? LastOnlineVerification { get; init; }

        /// <summary>
        /// プロモーションが有効かどうかを判定（拡張メソッドに委譲）
        /// </summary>
        public bool IsPromotionActive => this.IsCurrentlyActive();
    }
}
