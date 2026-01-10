using Baketa.Core.License.Models;

namespace Baketa.Core.Abstractions.License;

/// <summary>
/// ボーナストークンサービスインターフェース
/// Issue #280+#281: プロモーション等で付与されたボーナストークンを管理
/// </summary>
/// <remarks>
/// <para>
/// ボーナストークンは以下の特性を持つ:
/// - 複数のボーナス（プロモーション、キャンペーン等）を個別に管理
/// - 有効期限が近い順に消費（FIFOライク）
/// - オフライン時はローカルで消費を記録し、オンライン時にCRDT G-Counterで同期
/// </para>
/// <para>
/// 消費優先順位:
/// 1. ボーナストークン（有効期限が近い順）
/// 2. プラン付帯の月間クォータ
/// </para>
/// </remarks>
public interface IBonusTokenService
{
    /// <summary>
    /// 現在のボーナストークン一覧を取得
    /// </summary>
    /// <returns>有効期限順にソートされたボーナストークン一覧</returns>
    IReadOnlyList<BonusToken> GetBonusTokens();

    /// <summary>
    /// 合計残りトークン数を取得
    /// </summary>
    /// <returns>使用可能なボーナストークンの合計残高</returns>
    long GetTotalRemainingTokens();

    /// <summary>
    /// ボーナストークンを消費
    /// </summary>
    /// <param name="amount">消費トークン数</param>
    /// <returns>実際に消費できたトークン数（残高不足の場合は0以上amount以下）</returns>
    /// <remarks>
    /// 有効期限が近いボーナスから順に消費される。
    /// ローカルで消費を記録し、次回同期時にサーバーへ反映。
    /// </remarks>
    long ConsumeTokens(long amount);

    /// <summary>
    /// ボーナストークンで消費可能な量を確認
    /// </summary>
    /// <param name="amount">消費予定トークン数</param>
    /// <returns>ボーナスで賄える量（残高が足りない場合は残高まで）</returns>
    long GetConsumeableAmount(long amount);

    /// <summary>
    /// サーバーからボーナストークン状態を取得
    /// </summary>
    /// <param name="accessToken">Supabase認証トークン（JWT）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>同期結果</returns>
    Task<BonusSyncResult> FetchFromServerAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ローカルの消費状態をサーバーへ同期
    /// </summary>
    /// <param name="accessToken">Supabase認証トークン（JWT）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>同期結果</returns>
    /// <remarks>
    /// CRDT G-Counterパターン: 各ボーナスIDごとに大きい方を採用。
    /// オフライン時のローカル消費もサーバーに反映可能。
    /// </remarks>
    Task<BonusSyncResult> SyncToServerAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ボーナストークン状態変更イベント
    /// </summary>
    /// <remarks>
    /// 以下のタイミングで発火:
    /// - サーバーからの取得完了時
    /// - ローカルでの消費時
    /// - 同期完了時
    /// </remarks>
    event EventHandler<BonusTokensChangedEventArgs>? BonusTokensChanged;

    /// <summary>
    /// 未同期の消費があるかどうか
    /// </summary>
    bool HasPendingSync { get; }
}
