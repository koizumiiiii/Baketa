using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;


namespace Baketa.Core.Abstractions.Translation;
/// <summary>
/// 翻訳エンジン検出サービスのインターフェース
/// </summary>
public interface ITranslationEngineDiscovery
{
    /// <summary>
    /// 利用可能な翻訳エンジン名の一覧を取得します
    /// </summary>
    /// <returns>エンジン名のリスト</returns>
    Task<IReadOnlyList<string>> GetAvailableEngineNamesAsync();

    /// <summary>
    /// 名前を指定して翻訳エンジンを取得します
    /// </summary>
    /// <param name="engineName">エンジン名</param>
    /// <returns>翻訳エンジン、見つからない場合はnull</returns>
    Task<ITranslationEngine?> GetEngineByNameAsync(string engineName);

    /// <summary>
    /// 指定した言語ペアをサポートするエンジン名の一覧を取得します
    /// </summary>
    /// <param name="sourceLang">ソース言語</param>
    /// <param name="targetLang">ターゲット言語</param>
    /// <returns>エンジン名のリスト</returns>
    Task<IReadOnlyList<string>> GetSupportedEnginesForLanguagePairAsync(
        Language sourceLang,
        Language targetLang);

    /// <summary>
    /// 指定した言語ペアに最適な翻訳エンジンを取得します
    /// </summary>
    /// <param name="languagePair">言語ペア</param>
    /// <returns>最適な翻訳エンジン、見つからない場合はnull</returns>
    Task<ITranslationEngine?> GetBestEngineForLanguagePairAsync(LanguagePair languagePair);

    /// <summary>
    /// 言語検出に最適なエンジンを取得します
    /// </summary>
    /// <returns>言語検出エンジン、見つからない場合はnull</returns>
    Task<ITranslationEngine?> GetBestLanguageDetectionEngineAsync();
}
