namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// 翻訳モード
/// </summary>
public enum TranslationMode
{
    /// <summary>
    /// モード未設定（初期状態）
    /// </summary>
    None = 0,

    /// <summary>
    /// Live翻訳モード（常時監視型）
    /// ユーザーが開始ボタンを押すと、画面変化を監視し続けて自動的に翻訳を実行
    /// </summary>
    Live = 1,

    /// <summary>
    /// シングルショットモード（単発実行型）
    /// ユーザーがボタンを押したタイミングで1回だけキャプチャ→翻訳を実行
    /// </summary>
    Singleshot = 2
}
