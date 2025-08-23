using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// 翻訳辞書サービスのインターフェース
/// ハードコード翻訳から設定ファイルベース翻訳への移行を支援
/// </summary>
public interface ITranslationDictionaryService
{
    /// <summary>
    /// 設定ファイルベースの翻訳を実行します
    /// </summary>
    /// <param name="text">翻訳対象テキスト</param>
    /// <param name="sourceLanguage">源言語コード（例: "ja", "en"）</param>
    /// <param name="targetLanguage">目標言語コード（例: "ja", "en"）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>翻訳されたテキスト。辞書に存在しない場合は元のテキストを返す</returns>
    Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 指定されたテキストが翻訳辞書に存在するかを確認します
    /// </summary>
    /// <param name="text">確認対象テキスト</param>
    /// <param name="sourceLanguage">源言語コード</param>
    /// <param name="targetLanguage">目標言語コード</param>
    /// <returns>辞書に存在する場合はtrue</returns>
    bool HasTranslation(string text, string sourceLanguage, string targetLanguage);
    
    /// <summary>
    /// 翻訳辞書設定を再読み込みします
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task ReloadConfigurationAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 指定された言語ペアの利用可能な翻訳数を取得します
    /// </summary>
    /// <param name="sourceLanguage">源言語コード</param>
    /// <param name="targetLanguage">目標言語コード</param>
    /// <returns>利用可能な翻訳の数</returns>
    int GetTranslationCount(string sourceLanguage, string targetLanguage);
    
    /// <summary>
    /// サポートされている言語ペアを取得します
    /// </summary>
    /// <returns>サポートされている言語ペアのリスト</returns>
    IReadOnlyList<(string sourceLanguage, string targetLanguage)> GetSupportedLanguagePairs();
}