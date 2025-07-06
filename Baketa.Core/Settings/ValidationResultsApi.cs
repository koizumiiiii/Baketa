namespace Baketa.Core.Settings;

/// <summary>
/// Unified API for validation results to ensure consistency across different validation result types
/// 検証結果の統一APIによる一貫性の確保
/// </summary>
public static class ValidationResultsApi
{
    /// <summary>
    /// Create successful validation result for single setting
    /// </summary>
    public static SettingValidationResult CreateSettingSuccess(SettingMetadata metadata, object? value, string? warningMessage = null)
    {
        return SettingValidationResult.Success(metadata, value, warningMessage);
    }

    /// <summary>
    /// Create failed validation result for single setting
    /// </summary>
    public static SettingValidationResult CreateSettingFailure(SettingMetadata metadata, object? value, string errorMessage)
    {
        return SettingValidationResult.Failure(metadata, value, errorMessage);
    }

    /// <summary>
    /// Create successful validation result for settings collection
    /// </summary>
    public static SettingsValidationResult CreateSettingsSuccess(IEnumerable<string>? warnings = null)
    {
        return warnings?.Any() == true 
            ? SettingsValidationResult.CreateSuccess(warnings)
            : SettingsValidationResult.CreateSuccess();
    }

    /// <summary>
    /// Create failed validation result for settings collection
    /// </summary>
    public static SettingsValidationResult CreateSettingsFailure(IEnumerable<string> errors, IEnumerable<string>? warnings = null)
    {
        return SettingsValidationResult.CreateFailure(errors, warnings);
    }

    /// <summary>
    /// Create failed validation result for settings collection with single error
    /// </summary>
    public static SettingsValidationResult CreateSettingsFailure(string error, IEnumerable<string>? warnings = null)
    {
        return SettingsValidationResult.CreateFailure([error], warnings);
    }

    /// <summary>
    /// Check if validation result has warnings
    /// 検証結果に警告があるかを統一的にチェック
    /// </summary>
    public static bool HasWarnings(SettingValidationResult result)
    {
        return !string.IsNullOrEmpty(result.WarningMessage);
    }

    /// <summary>
    /// Check if validation result has warnings
    /// 検証結果に警告があるかを統一的にチェック
    /// </summary>
    public static bool HasWarnings(SettingsValidationResult result)
    {
        return result.HasWarnings;
    }

    /// <summary>
    /// Get error messages from validation result
    /// 検証結果からエラーメッセージを統一的に取得
    /// </summary>
    public static string GetErrorMessages(SettingValidationResult result)
    {
        return result.ErrorMessage ?? string.Empty;
    }

    /// <summary>
    /// Get error messages from validation result
    /// 検証結果からエラーメッセージを統一的に取得
    /// </summary>
    public static string GetErrorMessages(SettingsValidationResult result, string separator = "; ")
    {
        return result.GetErrorMessages(separator);
    }

    /// <summary>
    /// Get warning messages from validation result
    /// 検証結果から警告メッセージを統一的に取得
    /// </summary>
    public static string GetWarningMessages(SettingValidationResult result)
    {
        return result.WarningMessage ?? string.Empty;
    }

    /// <summary>
    /// Get warning messages from validation result
    /// 検証結果から警告メッセージを統一的に取得
    /// </summary>
    public static string GetWarningMessages(SettingsValidationResult result, string separator = "; ")
    {
        return result.GetWarningMessages(separator);
    }
}

/// <summary>
/// Extension methods for consistent validation result handling
/// 統一的な検証結果処理のための拡張メソッド
/// </summary>
public static class ValidationResultExtensions
{
    /// <summary>
    /// Check if single setting validation result has warnings
    /// </summary>
    public static bool HasWarnings(this SettingValidationResult result)
    {
        return ValidationResultsApi.HasWarnings(result);
    }

    /// <summary>
    /// Get error messages from single setting validation result
    /// </summary>
    public static string GetErrorMessages(this SettingValidationResult result)
    {
        return ValidationResultsApi.GetErrorMessages(result);
    }

    /// <summary>
    /// Get warning messages from single setting validation result
    /// </summary>
    public static string GetWarningMessages(this SettingValidationResult result)
    {
        return ValidationResultsApi.GetWarningMessages(result);
    }
}