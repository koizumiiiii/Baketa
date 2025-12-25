namespace Baketa.Core.Abstractions.License;

/// <summary>
/// Patreon OAuth コールバックハンドラのインターフェース
/// カスタムURIスキーム (baketa://patreon/callback) を処理する
/// </summary>
public interface IPatreonCallbackHandler
{
    /// <summary>
    /// 指定されたURLがPatreonコールバックとして処理可能かどうかを判定
    /// </summary>
    /// <param name="url">判定するURL</param>
    /// <returns>処理可能な場合はtrue</returns>
    bool CanHandle(string url);

    /// <summary>
    /// コールバックURLを処理し、認証を完了する
    /// </summary>
    /// <param name="callbackUrl">コールバックURL (例: baketa://patreon/callback?code=xxx&state=yyy)</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>認証結果</returns>
    Task<PatreonAuthResult> HandleCallbackUrlAsync(
        string callbackUrl,
        CancellationToken cancellationToken = default);
}
