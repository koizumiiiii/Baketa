using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Baketa.Core.Abstractions.Auth;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Auth;

/// <summary>
/// パスワード強度バリデーター
/// - 最小8文字
/// - 大文字・小文字・数字・記号のうち3種類以上
/// - 一般的な脆弱パスワードのブラックリストチェック
/// </summary>
public sealed class PasswordStrengthValidator : IPasswordStrengthValidator
{
    private readonly ILogger<PasswordStrengthValidator>? _logger;

    /// <summary>
    /// 最小パスワード長
    /// </summary>
    public const int MinimumPasswordLength = 8;

    /// <summary>
    /// 必要な文字種カテゴリ数
    /// </summary>
    public const int RequiredCategoryCount = 3;

    /// <summary>
    /// 一般的な脆弱パスワードのブラックリスト
    /// </summary>
    private static readonly HashSet<string> BlacklistedPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        // 連続数字
        "12345678",
        "123456789",
        "1234567890",
        "87654321",

        // 連続文字
        "abcdefgh",
        "qwertyui",
        "qwerty123",
        "asdfghjk",

        // 一般的な脆弱パスワード
        "password",
        "password1",
        "password123",
        "Password1",
        "Password123",
        "passw0rd",
        "p@ssword",
        "p@ssw0rd",

        // アプリケーション関連
        "baketa123",
        "baketapass",

        // 日本語ローマ字
        "nihongo1",
        "japanese",

        // 年度パターン
        "spring2024",
        "summer2024",
        "autumn2024",
        "winter2024",
        "spring2025",
        "summer2025",

        // デフォルト/テスト
        "admin123",
        "test1234",
        "testtest",
        "changeme",
        "letmein1",

        // キーボードパターン
        "1q2w3e4r",
        "zaq12wsx",
        "1qaz2wsx"
    };

    /// <summary>
    /// PasswordStrengthValidatorを初期化します
    /// </summary>
    /// <param name="logger">ロガー（オプション）</param>
    public PasswordStrengthValidator(ILogger<PasswordStrengthValidator>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    [SuppressMessage("CodeQuality", "cs/clear-text-storage-of-sensitive-information",
        Justification = "Only logging category count (1-4) and strength enum, not the actual password")]
    public PasswordValidationResult ValidatePassword(string password)
    {
        var errors = new List<string>();

        // null/空チェック
        if (string.IsNullOrWhiteSpace(password))
        {
            errors.Add("パスワードを入力してください");
            return PasswordValidationResult.Failure(errors, 0);
        }

        // 最小長チェック
        if (password.Length < MinimumPasswordLength)
        {
            errors.Add($"パスワードは{MinimumPasswordLength}文字以上で入力してください");
        }

        // 文字種カテゴリをカウント
        var categoryCount = CountPasswordCategories(password);

        // 3種類以上チェック
        if (categoryCount < RequiredCategoryCount)
        {
            errors.Add($"大文字・小文字・数字・記号のうち{RequiredCategoryCount}種類以上を含めてください（現在: {categoryCount}種類）");
        }

        // ブラックリストチェック
        if (IsBlacklistedPassword(password))
        {
            errors.Add("一般的すぎるパスワードです。より独自性のあるパスワードを設定してください");
        }

        // エラーがあれば失敗
        if (errors.Count > 0)
        {
            _logger?.LogDebug("パスワードバリデーション失敗: {ErrorCount}件のエラー", errors.Count);
            return PasswordValidationResult.Failure(errors, categoryCount);
        }

        // 強度を計算
        var strength = CalculateStrength(password, categoryCount);
        _logger?.LogDebug("パスワードバリデーション成功: 強度={Strength}, カテゴリ={CategoryCount}", strength, categoryCount);

        return PasswordValidationResult.Success(strength, categoryCount);
    }

    /// <inheritdoc/>
    public PasswordStrength GetPasswordStrength(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return PasswordStrength.Weak;

        var categoryCount = CountPasswordCategories(password);
        return CalculateStrength(password, categoryCount);
    }

    /// <inheritdoc/>
    public bool IsBlacklistedPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        // 完全一致チェック
        if (BlacklistedPasswords.Contains(password))
            return true;

        // パスワードの一部がブラックリストに含まれるかチェック（12文字以上の場合のみ）
        if (password.Length >= 12)
        {
            foreach (var blacklisted in BlacklistedPasswords)
            {
                if (password.Contains(blacklisted, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public string GetStrengthMessage(PasswordStrength strength)
    {
        return strength switch
        {
            PasswordStrength.Weak => "弱い",
            PasswordStrength.Medium => "普通",
            PasswordStrength.Strong => "強い",
            _ => "不明"
        };
    }

    /// <summary>
    /// パスワードに含まれる文字種カテゴリの数をカウントします
    /// </summary>
    private static int CountPasswordCategories(string password)
    {
        var categoryCount = 0;

        bool hasUpper = false;
        bool hasLower = false;
        bool hasDigit = false;
        bool hasSymbol = false;

        foreach (char c in password)
        {
            if (!hasUpper && char.IsUpper(c))
                hasUpper = true;
            else if (!hasLower && char.IsLower(c))
                hasLower = true;
            else if (!hasDigit && char.IsDigit(c))
                hasDigit = true;
            else if (!hasSymbol && IsSymbol(c))
                hasSymbol = true;
        }

        if (hasUpper) categoryCount++;
        if (hasLower) categoryCount++;
        if (hasDigit) categoryCount++;
        if (hasSymbol) categoryCount++;

        return categoryCount;
    }

    /// <summary>
    /// 文字が記号かどうかを判定します
    /// </summary>
    private static bool IsSymbol(char c)
    {
        // ASCII記号と一般的に使用される記号
        return !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c);
    }

    /// <summary>
    /// パスワード強度を計算します
    /// </summary>
    private PasswordStrength CalculateStrength(string password, int categoryCount)
    {
        // 基本要件を満たさない場合は弱い
        if (password.Length < MinimumPasswordLength || categoryCount < RequiredCategoryCount)
            return PasswordStrength.Weak;

        // ブラックリストに含まれている場合は弱い
        if (IsBlacklistedPassword(password))
            return PasswordStrength.Weak;

        // 強いパスワードの条件:
        // - 12文字以上
        // - 4種類全てを含む
        if (password.Length >= 12 && categoryCount == 4)
            return PasswordStrength.Strong;

        // 中程度の強いパスワードの条件:
        // - 10文字以上かつ4種類全て
        // または
        // - 12文字以上かつ3種類以上
        if ((password.Length >= 10 && categoryCount == 4) ||
            (password.Length >= 12 && categoryCount >= 3))
            return PasswordStrength.Strong;

        // その他は普通
        return PasswordStrength.Medium;
    }
}
