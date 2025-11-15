namespace Baketa.Core.Translation.Pipeline;

/// <summary>
/// 翻訳結果の表示モード
/// </summary>
public enum TranslationDisplayMode
{
    /// <summary>
    /// デフォルト表示（通常のUIコンポーネント経由）
    /// TranslationCompletedEvent発行による従来フロー
    /// </summary>
    Default,

    /// <summary>
    /// インプレース表示（座標ベース直接表示）
    /// ShowInPlaceOverlayAsync経由による座標ベース表示
    /// </summary>
    InPlace
}
