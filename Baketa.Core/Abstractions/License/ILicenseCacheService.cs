using Baketa.Core.License.Models;

namespace Baketa.Core.Abstractions.License;

/// <summary>
/// ライセンス状態のローカルキャッシュを管理するサービス
/// </summary>
public interface ILicenseCacheService
{
    /// <summary>
    /// キャッシュされたライセンス状態を取得
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>キャッシュされた状態（存在しない場合はnull）</returns>
    Task<LicenseState?> GetCachedStateAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ライセンス状態をキャッシュに保存
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="state">保存する状態</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task SetCachedStateAsync(
        string userId,
        LicenseState state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// キャッシュをクリア
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task ClearCacheAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// キャッシュが有効かどうか（期限内かどうか）
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>有効な場合true</returns>
    Task<bool> IsCacheValidAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// トークン使用量をローカルで更新（オフライン時の楽観的更新用）
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="tokensConsumed">消費したトークン数</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>更新後の状態</returns>
    Task<LicenseState?> UpdateTokenUsageAsync(
        string userId,
        long tokensConsumed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 未同期のトークン消費記録を取得（オンライン復帰時の同期用）
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>未同期の消費記録リスト</returns>
    Task<IReadOnlyList<PendingTokenConsumption>> GetPendingConsumptionsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 未同期の消費記録を追加
    /// </summary>
    /// <param name="consumption">消費記録</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task AddPendingConsumptionAsync(
        PendingTokenConsumption consumption,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 同期済みの消費記録を削除
    /// </summary>
    /// <param name="idempotencyKeys">同期済みのIdempotency Keyリスト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task RemoveSyncedConsumptionsAsync(
        IEnumerable<string> idempotencyKeys,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 未同期のトークン消費記録
/// </summary>
public sealed record PendingTokenConsumption
{
    /// <summary>
    /// ユーザーID
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Idempotency Key
    /// </summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>
    /// 消費したトークン数
    /// </summary>
    public required int TokenCount { get; init; }

    /// <summary>
    /// 消費日時
    /// </summary>
    public required DateTime ConsumedAt { get; init; }

    /// <summary>
    /// メタデータ
    /// </summary>
    public TokenConsumptionMetadata? Metadata { get; init; }
}
