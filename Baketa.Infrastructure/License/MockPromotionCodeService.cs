using System.Text.RegularExpressions;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.License;
using Baketa.Core.Constants;
using Baketa.Core.License.Events;
using Baketa.Core.License.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.License;

/// <summary>
/// プロモーションコードサービスのモック実装
/// </summary>
/// <remarks>
/// Issue #237 Phase 2: モックモード分離
/// 開発・テスト用のモック実装。本番コードから分離することで:
/// - 単一責任原則（SRP）を維持
/// - テスタビリティ向上
/// - 本番コードの複雑性を低減
/// </remarks>
public sealed class MockPromotionCodeService : IPromotionCodeService, IDisposable
{
    private readonly IPromotionSettingsPersistence _settingsPersistence;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<MockPromotionCodeService> _logger;
    private bool _disposed;

    /// <inheritdoc/>
    public event EventHandler<PromotionStateChangedEventArgs>? PromotionStateChanged;

    public MockPromotionCodeService(
        IPromotionSettingsPersistence settingsPersistence,
        IEventAggregator eventAggregator,
        ILogger<MockPromotionCodeService> logger)
    {
        _settingsPersistence = settingsPersistence ?? throw new ArgumentNullException(nameof(settingsPersistence));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogDebug("MockPromotionCodeService initialized");
    }

    /// <inheritdoc/>
    public async Task<PromotionCodeResult> ApplyCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        // 形式検証
        var normalizedCode = code.Trim().ToUpperInvariant();
        if (!ValidateCodeFormat(normalizedCode))
        {
            _logger.LogWarning("[MockMode] Invalid promotion code format");
            return PromotionCodeResult.CreateFailure(
                PromotionErrorCode.InvalidFormat,
                "[モックモード] プロモーションコードの形式が正しくありません。");
        }

        // 処理シミュレーション
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);

        // テスト用: BAKETA-TEST で始まるコードはProプランを適用
        if (normalizedCode.StartsWith("BAKETA-TEST", StringComparison.OrdinalIgnoreCase))
        {
            var expiresAt = DateTime.UtcNow.AddMonths(1);

            await _settingsPersistence.SavePromotionAsync(
                normalizedCode,
                PlanType.Pro,
                expiresAt,
                cancellationToken).ConfigureAwait(false);

            var promotionInfo = new PromotionInfo
            {
                Code = normalizedCode,
                Plan = PlanType.Pro,
                ExpiresAt = expiresAt,
                AppliedAt = DateTime.UtcNow
            };

            // 従来のイベント（ViewModel向け）
            PromotionStateChanged?.Invoke(this, new PromotionStateChangedEventArgs
            {
                NewPromotion = promotionInfo,
                Reason = "Mock promotion code applied"
            });

            // Issue #243: EventAggregator経由でLicenseManagerに通知
            await _eventAggregator.PublishAsync(new PromotionAppliedEvent(promotionInfo))
                .ConfigureAwait(false);

            _logger.LogInformation("[MockMode] Promotion code applied: BAKETA-****-****");
            return PromotionCodeResult.CreateSuccess(
                PlanType.Pro,
                expiresAt,
                "[モックモード] Proプランが適用されました。");
        }

        // BAKETA-EXPIRE で始まるコードは期限切れ
        if (normalizedCode.StartsWith("BAKETA-EXPI", StringComparison.OrdinalIgnoreCase))
        {
            return PromotionCodeResult.CreateFailure(
                PromotionErrorCode.CodeExpired,
                "[モックモード] このコードは有効期限が切れています。");
        }

        // BAKETA-USED で始まるコードは使用済み
        if (normalizedCode.StartsWith("BAKETA-USED", StringComparison.OrdinalIgnoreCase))
        {
            return PromotionCodeResult.CreateFailure(
                PromotionErrorCode.AlreadyRedeemed,
                "[モックモード] このコードは既に使用されています。");
        }

        // その他のコードは無効
        return PromotionCodeResult.CreateFailure(
            PromotionErrorCode.CodeNotFound,
            "[モックモード] 無効なプロモーションコードです。");
    }

    /// <inheritdoc/>
    public PromotionInfo? GetCurrentPromotion()
    {
        // モックモードでは常にnullを返す（実際の設定は読まない）
        // 必要に応じて設定から読み取るように変更可能
        return null;
    }

    /// <inheritdoc/>
    public bool ValidateCodeFormat(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        // BAKETA-XXXX-XXXX 形式をチェック
        var normalized = code.Trim().ToUpperInvariant();
        return Regex.IsMatch(
            normalized,
            ValidationPatterns.PromotionCode,
            RegexOptions.IgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _logger.LogDebug("MockPromotionCodeService disposed");
    }
}
