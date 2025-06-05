using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Baketa.UI.Models;

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
    Models.ChineseVariant DetectChineseVariant(string text);

    /// <summary>
    /// 言語変更イベント
    /// </summary>
    event EventHandler<LanguageChangedEventArgs> LanguageChanged;

    /// <summary>
    /// 現在の言語変更監視用オブザーバブル
    /// </summary>
    System.IObservable<CultureInfo> CurrentLanguageChanged { get; }
}

/// <summary>
/// サポート対象言語
/// </summary>
public record SupportedLanguage(
    string Code,
    string NativeName,
    string EnglishName,
    bool IsRightToLeft = false);

// ChineseVariantはBaketa.UI.Models名前空間で定義されています

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
