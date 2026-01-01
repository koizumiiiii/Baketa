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

/// <summary>
/// プラン別の機能制限情報
/// </summary>
public static class PlanFeatures
{
    /// <summary>
    /// Cloud AI翻訳が利用可能なプラン
    /// </summary>
    public static readonly PlanType[] CloudAIEnabledPlans = [PlanType.Pro, PlanType.Premia];

    /// <summary>
    /// プラン別月間トークン上限
    /// </summary>
    // Issue #125: Standardプラン廃止
    public static readonly IReadOnlyDictionary<PlanType, long> MonthlyTokenLimits =
        new Dictionary<PlanType, long>
        {
            [PlanType.Free] = 0,           // Cloud AI利用不可
            [PlanType.Pro] = 4_000_000,    // 400万トークン
            [PlanType.Premia] = 8_000_000  // 800万トークン
        };

    /// <summary>
    /// 指定されたプランでCloud AIが利用可能かチェック
    /// </summary>
    public static bool IsCloudAIEnabled(PlanType plan) =>
        Array.Exists(CloudAIEnabledPlans, p => p == plan);

    /// <summary>
    /// 指定されたプランの月間トークン上限を取得
    /// </summary>
    public static long GetTokenLimit(PlanType plan) =>
        MonthlyTokenLimits.TryGetValue(plan, out var limit) ? limit : 0;
}
