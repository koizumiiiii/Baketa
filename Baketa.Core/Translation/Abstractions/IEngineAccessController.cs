using Baketa.Core.License.Models;

namespace Baketa.Core.Translation.Abstractions;

/// <summary>
/// ユーザープランに基づいて翻訳エンジンへのアクセスを制御するインターフェース
/// </summary>
public interface IEngineAccessController
{
    /// <summary>
    /// 指定されたエンジンを現在のプランで利用可能かチェックする
    /// </summary>
    /// <param name="engineId">エンジンID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>利用可能な場合true</returns>
    Task<bool> CanUseEngineAsync(string engineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のプランで利用可能なエンジン一覧を取得する
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>利用可能なエンジン情報のリスト</returns>
    Task<IReadOnlyList<TranslationEngineInfo>> GetAvailableEnginesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたエンジンが利用できない理由を取得する
    /// </summary>
    /// <param name="engineId">エンジンID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>利用不可の理由（利用可能な場合はnull）</returns>
    Task<string?> GetRestrictionReasonAsync(string engineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cloud AI翻訳が現在のプランで利用可能かチェックする
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>利用可能な場合true</returns>
    Task<bool> CanUseCloudAIAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のプランタイプを取得する
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>現在のプランタイプ</returns>
    Task<PlanType> GetCurrentPlanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// プラン別のトークン上限を取得する
    /// </summary>
    /// <param name="planType">プランタイプ</param>
    /// <returns>月間トークン上限（-1は無制限）</returns>
    long GetMonthlyTokenLimit(PlanType planType);
}

/// <summary>
/// 翻訳エンジン情報
/// </summary>
public sealed class TranslationEngineInfo
{
    /// <summary>
    /// エンジンID
    /// </summary>
    public required string EngineId { get; init; }

    /// <summary>
    /// 表示名
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// 説明
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// クラウドAIエンジンかどうか
    /// </summary>
    public bool IsCloud { get; init; }

    /// <summary>
    /// 必要な最低プラン
    /// </summary>
    public PlanType RequiredPlan { get; init; }

    /// <summary>
    /// 現在のプランで利用可能かどうか
    /// </summary>
    public bool IsAvailable { get; init; }

    /// <summary>
    /// 利用不可の理由（利用可能な場合はnull）
    /// </summary>
    public string? RestrictionReason { get; init; }

    /// <summary>
    /// エンジンの優先度（低いほど優先）
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// アイコン名（UIで使用）
    /// </summary>
    public string? IconName { get; init; }
}

// Issue #257: PlanFeaturesクラスは削除されました
// - MonthlyTokenLimits, GetTokenLimit() → PlanTypeExtensions.GetMonthlyTokenLimit()
// - CloudAIEnabledPlans, IsCloudAIEnabled() → PlanTypeExtensions.HasCloudAiAccess()
