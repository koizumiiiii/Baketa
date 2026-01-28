namespace Baketa.Core.License.Models;

/// <summary>
/// ボーナストークン情報
/// Issue #280+#281+#347: プロモーション等で付与されたボーナストークン（永続）
/// </summary>
public sealed record BonusToken
{
    /// <summary>
    /// ボーナスID（サーバーで生成されたUUID）
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// ボーナスの出所（promotion, campaign, referral, welcome等）
    /// </summary>
    public required string SourceType { get; init; }

    /// <summary>
    /// 付与されたトークン数
    /// </summary>
    public required long GrantedTokens { get; init; }

    /// <summary>
    /// 使用済みトークン数
    /// </summary>
    public required long UsedTokens { get; init; }

    /// <summary>
    /// 残りトークン数
    /// </summary>
    public long RemainingTokens => GrantedTokens - UsedTokens;

    /// <summary>
    /// 付与日時
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 使用可能かどうか（残高あり）
    /// [Issue #347] 有効期限廃止により、残高のみで判定
    /// </summary>
    public bool IsUsable => RemainingTokens > 0;
}

/// <summary>
/// ボーナストークン同期結果
/// </summary>
public sealed record BonusSyncResult
{
    /// <summary>
    /// 同期成功したかどうか
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 同期後のボーナストークン一覧
    /// </summary>
    public IReadOnlyList<BonusToken> Bonuses { get; init; } = [];

    /// <summary>
    /// 合計残りトークン数
    /// </summary>
    public long TotalRemaining { get; init; }

    /// <summary>
    /// エラーメッセージ（失敗時のみ）
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// ボーナストークン状態変更イベント引数
/// </summary>
public sealed class BonusTokensChangedEventArgs : EventArgs
{
    /// <summary>
    /// 現在のボーナストークン一覧
    /// </summary>
    public IReadOnlyList<BonusToken> Bonuses { get; init; } = [];

    /// <summary>
    /// 合計残りトークン数
    /// </summary>
    public long TotalRemaining { get; init; }

    /// <summary>
    /// 変更理由
    /// </summary>
    public required string Reason { get; init; }
}

/// <summary>
/// [Issue #299+#347] ボーナストークン情報（統合エンドポイント用DTO）
/// </summary>
/// <remarks>
/// 統合エンドポイント `/api/sync/init` から取得したデータを受け渡すための軽量DTO。
/// 完全な <see cref="BonusToken"/> に変換せずに、必要最小限の情報のみを持つ。
/// [Issue #347] 有効期限関連フィールド削除
/// </remarks>
public sealed record BonusTokenInfo
{
    /// <summary>ボーナスID</summary>
    public required string BonusId { get; init; }

    /// <summary>残りトークン数</summary>
    public long RemainingTokens { get; init; }

    /// <summary>付与されたトークン数</summary>
    public long GrantedTokens { get; init; }

    /// <summary>使用済みトークン数</summary>
    public long UsedTokens { get; init; }
}
