using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace Baketa.UI.Security;

/// <summary>
/// 入力値の検証とサニタイゼーションを行うユーティリティクラス
/// </summary>
public static partial class InputValidator
{
    // コンパイル済み正規表現（パフォーマンス最適化）
    [GeneratedRegex(@"^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    // 危険なパターン検出
    [GeneratedRegex(@"<script|javascript:|vbscript:|onload=|onerror=|eval\(|expression\(", RegexOptions.IgnoreCase)]
    private static partial Regex DangerousPatternRegex();

    // よく使われる弱いパスワードパターン
    private static readonly string[] WeakPasswordPatterns = [
        "password", "123456", "qwerty", "admin", "letmein", "welcome",
        "monkey", "dragon", "master", "shadow", "12345678", "abc123"
    ];

    /// <summary>
    /// メールアドレスの厳密検証
    /// </summary>
    /// <param name="email">検証するメールアドレス</param>
    /// <returns>有効な場合true</returns>
    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        // 長さ制限（RFC 5321準拠）
        if (email.Length > 320)
            return false;

        // 危険なパターンチェック
        if (ContainsDangerousPatterns(email))
            return false;

        // 正規表現による形式チェック
        if (!EmailRegex().IsMatch(email))
            return false;

        // ローカル部とドメイン部の長さチェック
        var parts = email.Split('@');
        if (parts[0].Length > 64 || parts[1].Length > 253)
            return false;

        return true;
    }

    /// <summary>
    /// 強固なパスワードの検証
    /// </summary>
    /// <param name="password">検証するパスワード</param>
    /// <returns>強固な場合true</returns>
    public static bool IsStrongPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        // 最小長度：12文字
        if (password.Length < 12)
            return false;

        // 最大長度：128文字（DoS攻撃対策）
        if (password.Length > 128)
            return false;

        // 文字種要件
        bool hasUpper = password.Any(char.IsUpper);
        bool hasLower = password.Any(char.IsLower);
        bool hasDigit = password.Any(char.IsDigit);
        bool hasSpecial = password.Any(c => "!@#$%^&*()_+-=[]{}|;:,.<>?".Contains(c));

        if (!(hasUpper && hasLower && hasDigit && hasSpecial))
            return false;

        // 危険なパターンチェック
        if (ContainsDangerousPatterns(password))
            return false;

        // 弱いパスワードパターンチェック
        if (ContainsWeakPatterns(password))
            return false;

        // 連続文字チェック（3文字以上の連続は拒否）
        if (HasConsecutiveCharacters(password, 3))
            return false;

        return true;
    }

    /// <summary>
    /// 表示名の検証
    /// </summary>
    /// <param name="displayName">検証する表示名</param>
    /// <returns>有効な場合true</returns>
    public static bool IsValidDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return false;

        var trimmed = displayName.Trim();

        // 長さチェック
        if (trimmed.Length < 2 || trimmed.Length > 50)
            return false;

        // 危険なパターンチェック
        if (ContainsDangerousPatterns(trimmed))
            return false;

        // 制御文字チェック
        if (trimmed.Any(char.IsControl))
            return false;

        return true;
    }

    /// <summary>
    /// 入力値のサニタイゼーション
    /// </summary>
    /// <param name="input">サニタイズする文字列</param>
    /// <returns>サニタイズされた文字列</returns>
    public static string SanitizeInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // HTMLエンコード
        var sanitized = HttpUtility.HtmlEncode(input.Trim());

        // 制御文字の除去（改行・タブ以外）
        sanitized = new string([..sanitized.Where(c => !char.IsControl(c) || c == '\r' || c == '\n' || c == '\t')]);

        return sanitized;
    }

    /// <summary>
    /// パスワード強度スコアの計算
    /// </summary>
    /// <param name="password">評価するパスワード</param>
    /// <returns>0-100のスコア</returns>
    public static int CalculatePasswordStrength(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return 0;

        int score = 0;

        // 長さボーナス
        score += Math.Min(password.Length * 4, 25);

        // 文字種ボーナス
        if (password.Any(char.IsUpper)) score += 15;
        if (password.Any(char.IsLower)) score += 15;
        if (password.Any(char.IsDigit)) score += 15;
        if (password.Any(c => "!@#$%^&*()_+-=[]{}|;:,.<>?".Contains(c))) score += 15;

        // 複雑性ボーナス
        var uniqueChars = password.Distinct().Count();
        score += Math.Min(uniqueChars * 2, 15);

        // ペナルティ
        if (ContainsWeakPatterns(password)) score -= 20;
        if (HasConsecutiveCharacters(password, 3)) score -= 15;
        if (password.Length < 8) score -= 20;

        return Math.Max(0, Math.Min(100, score));
    }

    /// <summary>
    /// 危険なパターンが含まれているかチェック
    /// </summary>
    private static bool ContainsDangerousPatterns(string input)
    {
        return DangerousPatternRegex().IsMatch(input);
    }

    /// <summary>
    /// 弱いパスワードパターンが含まれているかチェック
    /// </summary>
    private static bool ContainsWeakPatterns(string password)
    {
        var lowerPassword = password.ToLowerInvariant();
        return WeakPasswordPatterns.Any(pattern => lowerPassword.Contains(pattern));
    }

    /// <summary>
    /// 連続文字があるかチェック
    /// </summary>
    private static bool HasConsecutiveCharacters(string password, int maxConsecutive)
    {
        for (int i = 0; i < password.Length - maxConsecutive + 1; i++)
        {
            bool isConsecutive = true;
            for (int j = 1; j < maxConsecutive; j++)
            {
                if (password[i] != password[i + j])
                {
                    isConsecutive = false;
                    break;
                }
            }
            if (isConsecutive) return true;
        }
        return false;
    }
}