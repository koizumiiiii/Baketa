using System;
using System.Collections.Generic;

namespace Baketa.Core.Abstractions.Auth;

/// <summary>
/// パスワード強度レベル
/// </summary>
public enum PasswordStrength
{
    /// <summary>弱い - 基本的な要件を満たさない</summary>
    Weak = 0,
    /// <summary>普通 - 基本要件は満たすが改善の余地あり</summary>
    Medium = 1,
    /// <summary>強い - セキュリティ要件を十分に満たす</summary>
    Strong = 2
}

/// <summary>
/// パスワードバリデーション結果
/// </summary>
/// <param name="IsValid">パスワードが有効かどうか</param>
/// <param name="Strength">パスワード強度</param>
/// <param name="Errors">バリデーションエラーメッセージのリスト</param>
/// <param name="CategoryCount">満たしている文字種カテゴリの数（大文字/小文字/数字/記号）</param>
public sealed record PasswordValidationResult(
    bool IsValid,
    PasswordStrength Strength,
    IReadOnlyList<string> Errors,
    int CategoryCount)
{
    /// <summary>
    /// 成功結果を作成します
    /// </summary>
    public static PasswordValidationResult Success(PasswordStrength strength, int categoryCount) =>
        new(true, strength, Array.Empty<string>(), categoryCount);

    /// <summary>
    /// 失敗結果を作成します
    /// </summary>
    public static PasswordValidationResult Failure(IReadOnlyList<string> errors, int categoryCount) =>
        new(false, PasswordStrength.Weak, errors, categoryCount);
}

/// <summary>
/// パスワード強度バリデーターインターフェース
/// </summary>
public interface IPasswordStrengthValidator
{
    /// <summary>
    /// パスワードを検証し、詳細な結果を返します
    /// </summary>
    /// <param name="password">検証するパスワード</param>
    /// <returns>バリデーション結果</returns>
    PasswordValidationResult ValidatePassword(string password);

    /// <summary>
    /// パスワードの強度を取得します
    /// </summary>
    /// <param name="password">パスワード</param>
    /// <returns>パスワード強度</returns>
    PasswordStrength GetPasswordStrength(string password);

    /// <summary>
    /// パスワードが脆弱なブラックリストに含まれているかチェックします
    /// </summary>
    /// <param name="password">パスワード</param>
    /// <returns>ブラックリストに含まれている場合true</returns>
    bool IsBlacklistedPassword(string password);

    /// <summary>
    /// パスワード強度を日本語メッセージで取得します
    /// </summary>
    /// <param name="strength">パスワード強度</param>
    /// <returns>日本語メッセージ</returns>
    string GetStrengthMessage(PasswordStrength strength);
}
