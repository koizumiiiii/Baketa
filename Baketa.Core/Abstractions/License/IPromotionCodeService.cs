using Baketa.Core.License.Models;

namespace Baketa.Core.Abstractions.License;

/// <summary>
/// プロモーションコードサービスインターフェース
/// Issue #237 Phase 2: プロモーションコード機能
/// </summary>
public interface IPromotionCodeService
{
    /// <summary>
    /// プロモーションコードを適用
    /// </summary>
    /// <param name="code">プロモーションコード（例: BAKETA-XXXX-XXXX）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>適用結果</returns>
    Task<PromotionCodeResult> ApplyCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在適用中のプロモーションコード情報を取得
    /// </summary>
    /// <returns>プロモーション情報（未適用の場合null）</returns>
    PromotionInfo? GetCurrentPromotion();

    /// <summary>
    /// プロモーションコードの形式を検証（ローカル検証）
    /// </summary>
    /// <param name="code">プロモーションコード</param>
    /// <returns>形式が正しい場合true</returns>
    bool ValidateCodeFormat(string code);

    /// <summary>
    /// プロモーションコードの適用状態変更イベント
    /// </summary>
    event EventHandler<PromotionStateChangedEventArgs>? PromotionStateChanged;
}

/// <summary>
/// プロモーション情報
/// </summary>
public sealed record PromotionInfo
{
    /// <summary>
    /// 適用されたコード
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 適用されたプラン
    /// </summary>
    public required PlanType Plan { get; init; }

    /// <summary>
    /// 有効期限
    /// </summary>
    public required DateTime ExpiresAt { get; init; }

    /// <summary>
    /// 適用日時
    /// </summary>
    public required DateTime AppliedAt { get; init; }

    /// <summary>
    /// 有効期限内かどうか
    /// </summary>
    public bool IsValid => DateTime.UtcNow < ExpiresAt;
}

/// <summary>
/// プロモーション状態変更イベント引数
/// </summary>
public sealed class PromotionStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 新しいプロモーション情報（解除時はnull）
    /// </summary>
    public PromotionInfo? NewPromotion { get; init; }

    /// <summary>
    /// 変更理由
    /// </summary>
    public required string Reason { get; init; }
}
