using Baketa.Core.License.Models;

namespace Baketa.Core.Abstractions.License;

/// <summary>
/// プロモーション設定の永続化インターフェース
/// </summary>
/// <remarks>
/// Issue #237 Phase 2: 設定永続化ロジックの分離
/// - 単一責任原則（SRP）を維持
/// - テスタビリティの向上
/// - IOptionsMonitorとの依存を分離
/// </remarks>
public interface IPromotionSettingsPersistence
{
    /// <summary>
    /// プロモーション情報を永続化
    /// </summary>
    /// <param name="code">適用されたプロモーションコード</param>
    /// <param name="plan">適用されたプラン</param>
    /// <param name="expiresAt">有効期限</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>保存が成功した場合はtrue</returns>
    Task<bool> SavePromotionAsync(
        string code,
        PlanType plan,
        DateTime expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// プロモーション情報をクリア
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>クリアが成功した場合はtrue</returns>
    Task<bool> ClearPromotionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 最終オンライン検証日時を更新
    /// </summary>
    /// <param name="verificationTime">検証日時</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>更新が成功した場合はtrue</returns>
    Task<bool> UpdateLastVerificationAsync(
        DateTime verificationTime,
        CancellationToken cancellationToken = default);
}
