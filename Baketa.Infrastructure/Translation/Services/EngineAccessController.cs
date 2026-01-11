using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Extensions;
using Baketa.Core.License.Models;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// ユーザープランに基づいて翻訳エンジンへのアクセスを制御する実装
/// Issue #280+#281: ボーナストークン残高によるCloud AIアクセス制御を追加
/// </summary>
public sealed class EngineAccessController : IEngineAccessController
{
    private readonly ILicenseManager _licenseManager;
    private readonly IEngineStatusManager _engineStatusManager;
    private readonly IBonusTokenService? _bonusTokenService;
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
    /// <param name="licenseManager">ライセンス管理</param>
    /// <param name="engineStatusManager">エンジン状態管理</param>
    /// <param name="bonusTokenService">ボーナストークンサービス（オプション）</param>
    /// <param name="logger">ロガー</param>
    public EngineAccessController(
        ILicenseManager licenseManager,
        IEngineStatusManager engineStatusManager,
        IBonusTokenService? bonusTokenService,
        ILogger<EngineAccessController> logger)
    {
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _engineStatusManager = engineStatusManager ?? throw new ArgumentNullException(nameof(engineStatusManager));
        _bonusTokenService = bonusTokenService; // オプショナル（Issue #280+#281）
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

        // Issue #280+#281: プランによるアクセス OR ボーナストークン残高あり
        var bonusRemaining = GetBonusTokensRemaining();
        var hasCloudAccess = state.HasCloudAiAccess || bonusRemaining > 0;

        if (!hasCloudAccess)
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
        // ライセンス状態は将来の拡張用に取得（現時点ではCanUseEngineAsyncで判定）
        _ = await _licenseManager.GetCurrentStateAsync(cancellationToken)
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

        // Issue #280+#281: ボーナストークン残高チェック
        var bonusRemaining = GetBonusTokensRemaining();

        // プラン確認 (Issue #257: HasCloudAiAccess拡張メソッドを使用)
        // Issue #280+#281: ボーナストークンがあればプラン不問でアクセス可
        if (!state.CurrentPlan.HasCloudAiAccess() && bonusRemaining <= 0)
        {
            return $"Cloud AI翻訳は{PlanType.Pro}以上のプラン、またはボーナストークンが必要です";
        }

        // サブスクリプション有効性（ボーナストークンがあれば無視）
        if (!state.IsSubscriptionActive && bonusRemaining <= 0)
        {
            return "サブスクリプションが有効ではありません";
        }

        // トークンクォータ（ボーナストークンがあれば無視）
        if (state.IsQuotaExceeded && bonusRemaining <= 0)
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
    /// <remarks>
    /// Issue #280+#281: プランによるアクセス OR ボーナストークン残高ありでCloud AI利用可能
    /// </remarks>
    public async Task<bool> CanUseCloudAIAsync(CancellationToken cancellationToken = default)
    {
        var state = await _licenseManager.GetCurrentStateAsync(cancellationToken)
            .ConfigureAwait(false);

        // Issue #280+#281: ボーナストークン残高チェック
        var bonusRemaining = GetBonusTokensRemaining();

        // プランによるアクセス OR ボーナストークン残高あり
        return state.HasCloudAiAccess || bonusRemaining > 0;
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
        // Issue #257: PlanTypeExtensions に一本化
        return planType.GetMonthlyTokenLimit();
    }

    /// <summary>
    /// [Gemini Review] ボーナストークン残高取得のヘルパーメソッド
    /// </summary>
    /// <returns>ボーナストークン残高（サービスがnullの場合は0）</returns>
    private long GetBonusTokensRemaining()
    {
        return _bonusTokenService?.GetTotalRemainingTokens() ?? 0;
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
