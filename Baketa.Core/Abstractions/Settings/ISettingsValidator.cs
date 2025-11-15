using System.Collections.Generic;
using Baketa.Core.Settings;

namespace Baketa.Core.Abstractions.Settings;

/// <summary>
/// 設定バリデーターインターフェース
/// 設定値の検証ルールを管理し、検証を実行する
/// </summary>
public interface ISettingsValidator
{
    /// <summary>
    /// 設定全体を検証します
    /// </summary>
    /// <param name="settings">検証する設定</param>
    /// <returns>検証結果</returns>
    SettingsValidationResult Validate(AppSettings settings);

    /// <summary>
    /// 特定のカテゴリの設定を検証します
    /// </summary>
    /// <typeparam name="T">設定の型</typeparam>
    /// <param name="category">カテゴリ名</param>
    /// <param name="settings">検証する設定</param>
    /// <returns>検証結果</returns>
    SettingsValidationResult Validate<T>(string category, T settings) where T : class;

    /// <summary>
    /// 検証ルールを追加します
    /// </summary>
    /// <param name="rule">検証ルール</param>
    void AddRule(IValidationRule rule);

    /// <summary>
    /// 特定のカテゴリに対する検証ルールを追加します
    /// </summary>
    /// <param name="category">カテゴリ名</param>
    /// <param name="rule">検証ルール</param>
    void AddRule(string category, IValidationRule rule);

    /// <summary>
    /// 登録済みの検証ルールを取得します
    /// </summary>
    /// <returns>検証ルールのコレクション</returns>
    IReadOnlyList<IValidationRule> GetRules();

    /// <summary>
    /// 特定カテゴリの検証ルールを取得します
    /// </summary>
    /// <param name="category">カテゴリ名</param>
    /// <returns>検証ルールのコレクション</returns>
    IReadOnlyList<IValidationRule> GetRules(string category);
}
