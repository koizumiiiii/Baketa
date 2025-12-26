using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Models;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// ユーザープランに基づいて翻訳エンジンへのアクセスを制御する実装
/// </summary>
public sealed class EngineAccessController : IEngineAccessController
{
    private readonly ILicenseManager _licenseManager;
    private readonly IEngineStatusManager _engineStatusManager;
    private readonly ILogger<EngineAccessController> _logger;

    // エンジン定義（静的設定）
    private static readonly IReadOnlyDictionary<string, EngineDefinition> EngineDefinitions =
        new Dictionary<string, EngineDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["primary"] = new()
            {
                EngineId = "primary",
                DisplayName = "Cloud AI (Primary)",
                Description = "高精度なCloud AI翻訳（メイン）",
                IsCloud = true,
                RequiredPlan = PlanType.Pro,
                Priority = 1,
                IconName = "CloudTranslation"
            },
            ["secondary"] = new()
            {
                EngineId = "secondary",
                DisplayName = "Cloud AI (Secondary)",
                Description = "高精度なCloud AI翻訳（サブ）",
                IsCloud = true,
                RequiredPlan = PlanType.Pro,
                Priority = 2,
                IconName = "CloudTranslation"
            },
            ["local"] = new()
            {
                EngineId = "local",
                DisplayName = "ローカル翻訳 (NLLB-200)",
                Description = "オフラインで動作するローカル翻訳エンジン",
                IsCloud = false,
                RequiredPlan = PlanType.Free,
                Priority = 10,
                IconName = "LocalTranslation"
            }
        };

    /// <summary>
    /// EngineAccessControllerを初期化
    /// </summary>
    public EngineAccessController(
        ILicenseManager licenseManager,
        IEngineStatusManager engineStatusManager,
        ILogger<EngineAccessController> logger)
    {
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _engineStatusManager = engineStatusManager ?? throw new ArgumentNullException(nameof(engineStatusManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<bool> CanUseEngineAsync(string engineId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(engineId);

        if (!EngineDefinitions.TryGetValue(engineId, out var definition))
        {
            _logger.LogWarning("不明なエンジンID: {EngineId}", engineId);
            return false;
        }

        // ローカルエンジンは常に利用可能
        if (!definition.IsCloud)
        {
            return true;
        }

        // クラウドエンジンはプランチェックが必要
        var state = await _licenseManager.GetCurrentStateAsync(cancellationToken)
            .ConfigureAwait(false);

        // プラン確認
        if (!PlanFeatures.IsCloudAIEnabled(state.CurrentPlan))
        {
            return false;
        }

        // サブスクリプション有効性
        if (!state.IsSubscriptionActive)
        {
            return false;
        }

        // トークンクォータ
        if (state.IsQuotaExceeded)
        {
            return false;
        }

        // エンジン状態（フォールバック管理）
        if (!_engineStatusManager.IsEngineAvailable(engineId))
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TranslationEngineInfo>> GetAvailableEnginesAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await _licenseManager.GetCurrentStateAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = new List<TranslationEngineInfo>();

        foreach (var (engineId, definition) in EngineDefinitions.OrderBy(e => e.Value.Priority))
        {
            var isAvailable = await CanUseEngineAsync(engineId, cancellationToken)
                .ConfigureAwait(false);

            string? restrictionReason = null;
            if (!isAvailable)
            {
                restrictionReason = await GetRestrictionReasonAsync(engineId, cancellationToken)
                    .ConfigureAwait(false);
            }

            result.Add(new TranslationEngineInfo
            {
                EngineId = definition.EngineId,
                DisplayName = definition.DisplayName,
                Description = definition.Description,
                IsCloud = definition.IsCloud,
                RequiredPlan = definition.RequiredPlan,
                IsAvailable = isAvailable,
                RestrictionReason = restrictionReason,
                Priority = definition.Priority,
                IconName = definition.IconName
            });
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<string?> GetRestrictionReasonAsync(
        string engineId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(engineId);

        if (!EngineDefinitions.TryGetValue(engineId, out var definition))
        {
            return "不明なエンジンIDです";
        }

        // ローカルエンジンは常に利用可能
        if (!definition.IsCloud)
        {
            return null;
        }

        var state = await _licenseManager.GetCurrentStateAsync(cancellationToken)
            .ConfigureAwait(false);

        // プラン確認
        if (!PlanFeatures.IsCloudAIEnabled(state.CurrentPlan))
        {
            return $"Cloud AI翻訳は{PlanType.Pro}以上のプランが必要です";
        }

        // サブスクリプション有効性
        if (!state.IsSubscriptionActive)
        {
            return "サブスクリプションが有効ではありません";
        }

        // トークンクォータ
        if (state.IsQuotaExceeded)
        {
            return "今月のクラウドAI翻訳上限に達しました";
        }

        // エンジン状態
        if (!_engineStatusManager.IsEngineAvailable(engineId))
        {
            var status = _engineStatusManager.GetStatus(engineId);
            if (status.NextRetryTime.HasValue)
            {
                var remaining = status.NextRetryTime.Value - DateTime.UtcNow;
                if (remaining.TotalSeconds > 0)
                {
                    return $"一時的に利用不可（{remaining.TotalMinutes:F0}分後に再試行）";
                }
            }
            return status.UnavailableReason ?? "一時的に利用できません";
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<bool> CanUseCloudAIAsync(CancellationToken cancellationToken = default)
    {
        var state = await _licenseManager.GetCurrentStateAsync(cancellationToken)
            .ConfigureAwait(false);

        return state.HasCloudAiAccess;
    }

    /// <inheritdoc/>
    public async Task<PlanType> GetCurrentPlanAsync(CancellationToken cancellationToken = default)
    {
        var state = await _licenseManager.GetCurrentStateAsync(cancellationToken)
            .ConfigureAwait(false);

        return state.CurrentPlan;
    }

    /// <inheritdoc/>
    public long GetMonthlyTokenLimit(PlanType planType)
    {
        return PlanFeatures.GetTokenLimit(planType);
    }

    /// <summary>
    /// 内部用エンジン定義
    /// </summary>
    private sealed class EngineDefinition
    {
        public required string EngineId { get; init; }
        public required string DisplayName { get; init; }
        public required string Description { get; init; }
        public bool IsCloud { get; init; }
        public PlanType RequiredPlan { get; init; }
        public int Priority { get; init; }
        public string? IconName { get; init; }
    }
}
