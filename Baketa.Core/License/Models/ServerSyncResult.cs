namespace Baketa.Core.License.Models;

/// <summary>
/// サーバー同期の結果を表すenum
/// Issue #276/#277: プロモーション・同意設定のDB同期で共通使用
/// </summary>
public enum ServerSyncResult
{
    /// <summary>同期成功</summary>
    Success,

    /// <summary>認証されていない（ログインが必要）</summary>
    NotAuthenticated,

    /// <summary>ネットワークエラー（オフライン等）</summary>
    NetworkError,

    /// <summary>サーバーエラー（5xx）</summary>
    ServerError,

    /// <summary>レスポンス解析失敗</summary>
    InvalidResponse,

    /// <summary>レート制限（429）</summary>
    RateLimited,

    /// <summary>タイムアウト</summary>
    Timeout,

    /// <summary>データなし（正常系）</summary>
    NoDataFound,

    /// <summary>期限切れ（正常系だが通知必要）</summary>
    Expired
}
