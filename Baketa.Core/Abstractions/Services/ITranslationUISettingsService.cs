namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// UI設定の翻訳言語ペアにアクセスするためのサービス
/// </summary>
public interface ITranslationUISettingsService
{
    /// <summary>
    /// 現在のソース言語コードを取得
    /// </summary>
    /// <returns>ソース言語コード（例: "ja"）</returns>
    string GetCurrentSourceLanguage();
    
    /// <summary>
    /// 現在のターゲット言語コードを取得
    /// </summary>
    /// <returns>ターゲット言語コード（例: "en"）</returns>
    string GetCurrentTargetLanguage();
    
    /// <summary>
    /// 自動検出が有効かどうかを取得
    /// </summary>
    /// <returns>自動検出が有効な場合はtrue</returns>
    bool IsAutoDetectEnabled();
}