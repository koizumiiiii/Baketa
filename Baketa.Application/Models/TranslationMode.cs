namespace Baketa.Application.Models;

/// <summary>
/// 翻訳実行モード
/// </summary>
public enum TranslationMode
{
    /// <summary>
    /// 手動モード - 単発翻訳のみ
    /// </summary>
    Manual,

    /// <summary>
    /// 自動モード - 連続翻訳実行
    /// </summary>
    Automatic
}
