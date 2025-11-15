using System;
using System.Collections.Generic;
using System.Linq;

namespace Baketa.Core.Settings;

/// <summary>
/// 設定値検証結果
/// </summary>
/// <remarks>
/// SettingValidationResultを初期化します（テスト用）
/// </remarks>
/// <param name="metadata">メタデータ</param>
/// <param name="value">検証した値</param>
/// <param name="isValid">検証成功フラグ</param>
/// <param name="errorMessage">エラーメッセージ</param>
/// <param name="warningMessage">警告メッセージ</param>
public sealed class SettingValidationResult(SettingMetadata metadata, object? value, bool isValid, string? errorMessage, string? warningMessage)
{
    /// <summary>
    /// 検証対象のメタデータ
    /// </summary>
    public SettingMetadata Metadata { get; } = metadata ?? throw new ArgumentNullException(nameof(metadata));

    /// <summary>
    /// 検証が成功したかどうか
    /// </summary>
    public bool IsValid { get; } = isValid;

    /// <summary>
    /// エラーメッセージ（検証失敗時）
    /// </summary>
    public string? ErrorMessage { get; } = errorMessage;

    /// <summary>
    /// 警告メッセージ（検証成功でも注意が必要な場合）
    /// </summary>
    public string? WarningMessage { get; } = warningMessage;

    /// <summary>
    /// 検証した値
    /// </summary>
    public object? Value { get; } = value;

    /// <summary>
    /// 成功した検証結果を作成します
    /// </summary>
    /// <param name="metadata">メタデータ</param>
    /// <param name="value">検証した値</param>
    /// <param name="warningMessage">警告メッセージ（任意）</param>
    /// <returns>検証結果</returns>
    public static SettingValidationResult Success(SettingMetadata metadata, object? value, string? warningMessage = null)
    {
        return new SettingValidationResult(metadata, value, true, null, warningMessage);
    }

    /// <summary>
    /// 失敗した検証結果を作成します
    /// </summary>
    /// <param name="metadata">メタデータ</param>
    /// <param name="value">検証した値</param>
    /// <param name="errorMessage">エラーメッセージ</param>
    /// <returns>検証結果</returns>
    public static SettingValidationResult Failure(SettingMetadata metadata, object? value, string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        // エラーメッセージの検証を厳密に行う - null/emptyどちらもArgumentExceptionに統一
        if (string.IsNullOrEmpty(errorMessage))
        {
            throw new ArgumentException("エラーメッセージは必須です", nameof(errorMessage));
        }

        return new SettingValidationResult(metadata, value, false, errorMessage, null);
    }

    /// <summary>
    /// 検証結果の文字列表現を取得します
    /// </summary>
    /// <returns>検証結果の文字列表現</returns>
    public override string ToString()
    {
        var status = IsValid ? "Valid" : "Invalid";
        var displayName = Metadata?.DisplayName ?? "Unknown Setting";
        var result = $"{status}: {displayName}";

        if (!IsValid && !string.IsNullOrEmpty(ErrorMessage))
        {
            result += $" - {ErrorMessage}";
        }

        if (IsValid && !string.IsNullOrEmpty(WarningMessage))
        {
            result += $" (Warning: {WarningMessage})";
        }

        return result;
    }
}

/// <summary>
/// 複数の検証結果をまとめるヘルパークラス
/// </summary>
public static class SettingValidationResults
{
    /// <summary>
    /// すべての検証が成功したかどうかを判定します
    /// </summary>
    /// <param name="results">検証結果のコレクション</param>
    /// <returns>すべて成功した場合はtrue</returns>
    public static bool AllValid(IEnumerable<SettingValidationResult> results)
    {
        return results.All(r => r.IsValid);
    }

    /// <summary>
    /// エラーがある検証結果のみを取得します
    /// </summary>
    /// <param name="results">検証結果のコレクション</param>
    /// <returns>エラーのある検証結果</returns>
    public static IEnumerable<SettingValidationResult> GetErrors(IEnumerable<SettingValidationResult> results)
    {
        return results.Where(r => !r.IsValid);
    }

    /// <summary>
    /// 警告がある検証結果のみを取得します
    /// </summary>
    /// <param name="results">検証結果のコレクション</param>
    /// <returns>警告のある検証結果</returns>
    public static IEnumerable<SettingValidationResult> GetWarnings(IEnumerable<SettingValidationResult> results)
    {
        return results.Where(r => r.IsValid && !string.IsNullOrEmpty(r.WarningMessage));
    }

    /// <summary>
    /// エラーメッセージを文字列として結合します
    /// </summary>
    /// <param name="results">検証結果のコレクション</param>
    /// <param name="separator">区切り文字</param>
    /// <returns>結合されたエラーメッセージ</returns>
    public static string CombineErrorMessages(IEnumerable<SettingValidationResult> results, string separator = "\n")
    {
        return string.Join(separator, GetErrors(results).Select(r => r.ErrorMessage).Where(m => !string.IsNullOrEmpty(m)));
    }
}
