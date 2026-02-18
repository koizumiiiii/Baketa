using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Services;

/// <summary>
/// 翻訳エンジン検出サービスの実装
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="engines">DIコンテナから注入された翻訳エンジンコレクション</param>
public class DefaultTranslationEngineDiscovery(
        IEnumerable<ITranslationEngine> engines) : ITranslationEngineDiscovery
{
    private readonly IReadOnlyList<ITranslationEngine> _engines = [.. engines ?? throw new ArgumentNullException(nameof(engines))];

    /// <summary>
    /// 利用可能な翻訳エンジン名の一覧を取得します
    /// </summary>
    /// <returns>翻訳エンジン名のリスト</returns>
    public Task<IReadOnlyList<string>> GetAvailableEngineNamesAsync()
    {
        IReadOnlyList<string> names = [.. _engines.Select(e => e.Name)];
        return Task.FromResult(names);
    }

    /// <summary>
    /// 指定した名前のエンジンを取得します
    /// </summary>
    /// <param name="engineName">エンジン名</param>
    /// <returns>見つかればエンジン、見つからなければnull</returns>
    public Task<ITranslationEngine?> GetEngineByNameAsync(string engineName)
    {
        if (string.IsNullOrWhiteSpace(engineName))
        {
            throw new ArgumentException("エンジン名が無効です。", nameof(engineName));
        }

        var engine = _engines.FirstOrDefault(e => string.Equals(e.Name, engineName, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(engine);
    }

    /// <summary>
    /// 指定した言語ペアに最適なエンジンを取得します
    /// </summary>
    /// <param name="languagePair">言語ペア</param>
    /// <returns>最適なエンジン、見つからなければnull</returns>
    public Task<ITranslationEngine?> GetBestEngineForLanguagePairAsync(LanguagePair languagePair)
    {
        ArgumentNullException.ThrowIfNull(languagePair, nameof(languagePair));

        return GetBestEngineForLanguagePairAsync(languagePair.SourceLanguage, languagePair.TargetLanguage);
    }

    /// <summary>
    /// 指定した言語ペアに最適なエンジンを取得します
    /// </summary>
    /// <param name="sourceLang">元言語</param>
    /// <param name="targetLang">対象言語</param>
    /// <returns>見つかればエンジン、見つからなければnull</returns>
    public Task<ITranslationEngine?> GetBestEngineForLanguagePairAsync(Language sourceLang, Language targetLang)
    {
        ArgumentNullException.ThrowIfNull(sourceLang, nameof(sourceLang));
        ArgumentNullException.ThrowIfNull(targetLang, nameof(targetLang));

        // 現在の実装では最初に利用可能なエンジンを返す
        var engine = _engines.FirstOrDefault();
        return Task.FromResult(engine);
    }

    /// <summary>
    /// 指定した言語ペアをサポートする翻訳エンジン名の一覧を取得します
    /// </summary>
    /// <param name="sourceLang">元言語</param>
    /// <param name="targetLang">対象言語</param>
    /// <returns>サポートするエンジン名のリスト</returns>
    public Task<IReadOnlyList<string>> GetSupportedEnginesForLanguagePairAsync(Language sourceLang, Language targetLang)
    {
        ArgumentNullException.ThrowIfNull(sourceLang, nameof(sourceLang));
        ArgumentNullException.ThrowIfNull(targetLang, nameof(targetLang));

        // 現在の実装では全エンジン名を返す
        IReadOnlyList<string> names = [.. _engines.Select(e => e.Name)];
        return Task.FromResult(names);
    }

    /// <summary>
    /// 言語検出に最適なエンジンを取得します
    /// </summary>
    /// <returns>見つかればエンジン、見つからなければnull</returns>
    public Task<ITranslationEngine?> GetBestLanguageDetectionEngineAsync()
    {
        var engine = _engines.FirstOrDefault();
        return Task.FromResult(engine);
    }
}
