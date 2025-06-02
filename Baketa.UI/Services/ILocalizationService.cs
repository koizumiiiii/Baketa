using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Baketa.UI.Services;

/// <summary>
/// ローカライゼーションサービス
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// 現在のアプリケーション言語
    /// </summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>
    /// 利用可能な言語一覧
    /// </summary>
    IReadOnlyList<SupportedLanguage> SupportedLanguages { get; }

    /// <summary>
    /// アプリケーション言語を変更
    /// </summary>
    /// <param name="cultureCode">言語コード (ja, en, zh-CN, zh-TW等)</param>
    /// <returns>変更成功の場合true</returns>
    Task<bool> ChangeLanguageAsync(string cultureCode);

    /// <summary>
    /// システム言語の自動検出
    /// </summary>
    /// <returns>検出された言語コード</returns>
    string DetectSystemLanguage();

    /// <summary>
    /// 中国語変種の判定（簡体字/繁体字）
    /// </summary>
    /// <param name="text">判定対象テキスト</param>
    /// <returns>中国語変種</returns>
    ChineseVariant DetectChineseVariant(string text);

    /// <summary>
    /// 言語変更イベント
    /// </summary>
    event EventHandler<LanguageChangedEventArgs> LanguageChanged;
}

/// <summary>
/// サポート対象言語
/// </summary>
public record SupportedLanguage(
    string Code,
    string NativeName,
    string EnglishName,
    bool IsRightToLeft = false);

/// <summary>
/// 中国語変種
/// </summary>
public enum ChineseVariant
{
    /// <summary>
    /// 自動判定
    /// </summary>
    Auto,

    /// <summary>
    /// 簡体字
    /// </summary>
    Simplified,

    /// <summary>
    /// 繁体字
    /// </summary>
    Traditional,

    /// <summary>
    /// 広東語（将来対応）
    /// </summary>
    Cantonese
}

/// <summary>
/// 言語変更イベント引数
/// </summary>
public class LanguageChangedEventArgs : EventArgs
{
    public CultureInfo OldCulture { get; }
    public CultureInfo NewCulture { get; }
    public DateTime ChangeDate { get; }

    public LanguageChangedEventArgs(CultureInfo oldCulture, CultureInfo newCulture)
    {
        OldCulture = oldCulture;
        NewCulture = newCulture;
        ChangeDate = DateTime.UtcNow;
    }
}
