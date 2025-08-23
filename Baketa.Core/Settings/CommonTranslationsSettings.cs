using System.Collections.Generic;

namespace Baketa.Core.Settings;

/// <summary>
/// 共通翻訳辞書の設定
/// ハードコードされた翻訳から設定ファイルベースへの移行を支援
/// </summary>
public sealed class CommonTranslationsSettings
{
    /// <summary>
    /// 日本語から英語への翻訳辞書
    /// </summary>
    public TranslationDictionary JapaneseToEnglish { get; set; } = new();
    
    /// <summary>
    /// 英語から日本語への翻訳辞書
    /// </summary>
    public TranslationDictionary EnglishToJapanese { get; set; } = new();
    
    /// <summary>
    /// フォールバック設定
    /// </summary>
    public FallbackSettings Fallback { get; set; } = new();
}

/// <summary>
/// 翻訳辞書（カテゴリ別分類）
/// </summary>
public sealed class TranslationDictionary
{
    /// <summary>
    /// UI要素の翻訳
    /// </summary>
    public Dictionary<string, string> UI { get; set; } = new();
    
    /// <summary>
    /// ゲーム用語の翻訳
    /// </summary>
    public Dictionary<string, string> Game { get; set; } = new();
    
    /// <summary>
    /// 動作・アクション関連の翻訳
    /// </summary>
    public Dictionary<string, string> Actions { get; set; } = new();
    
    /// <summary>
    /// 一般的な挨拶・表現の翻訳
    /// </summary>
    public Dictionary<string, string> Common { get; set; } = new();
    
    /// <summary>
    /// その他のカスタム翻訳
    /// </summary>
    public Dictionary<string, string> Custom { get; set; } = new();
}

/// <summary>
/// フォールバック設定
/// </summary>
public sealed class FallbackSettings
{
    /// <summary>
    /// ハードコード辞書をフォールバックとして使用するか
    /// </summary>
    public bool UseHardcodedDictionaryFallback { get; set; } = true;
    
    /// <summary>
    /// 機械翻訳をフォールバックとして使用するか
    /// </summary>
    public bool UseMachineTranslationFallback { get; set; } = true;
    
    /// <summary>
    /// 翻訳が見つからない場合の動作
    /// </summary>
    public FallbackBehavior NotFoundBehavior { get; set; } = FallbackBehavior.ReturnOriginal;
}

/// <summary>
/// 翻訳が見つからない場合の動作
/// </summary>
public enum FallbackBehavior
{
    /// <summary>
    /// 元のテキストをそのまま返す
    /// </summary>
    ReturnOriginal,
    
    /// <summary>
    /// 空文字列を返す
    /// </summary>
    ReturnEmpty,
    
    /// <summary>
    /// プレースホルダー文字列を返す
    /// </summary>
    ReturnPlaceholder
}