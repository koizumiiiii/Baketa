namespace Baketa.Core.Settings;

/// <summary>
/// 同意検証状態
/// Issue #277: サーバー同期後の同意状態を表す
/// </summary>
public enum ConsentVerificationState
{
    /// <summary>サーバーと同期済み、有効</summary>
    Verified,

    /// <summary>ローカルのみ（ネットワーク不可）</summary>
    LocalOnly,

    /// <summary>未確認</summary>
    Unknown,

    /// <summary>再同意が必要（バージョン更新または撤回）</summary>
    RequiresReconsent
}
