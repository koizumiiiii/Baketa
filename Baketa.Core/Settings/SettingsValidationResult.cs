using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Baketa.Core.Settings;

/// <summary>
/// 設定全体の検証結果
/// 複数の設定項目の検証結果をまとめて管理
/// </summary>
public sealed class SettingsValidationResult
{
    /// <summary>
    /// 全体の検証が成功したかどうか
    /// </summary>
    public bool IsValid { get; }
    
    /// <summary>
    /// 個別の検証結果
    /// </summary>
    public IReadOnlyList<SettingValidationResult> ValidationResults { get; }
    
    /// <summary>
    /// 個別の検証結果（ValidationResultsのエイリアス）
    /// </summary>
    public IReadOnlyList<SettingValidationResult> Results => ValidationResults;
    
    /// <summary>
    /// エラーのある検証結果
    /// </summary>
    public IReadOnlyList<SettingValidationResult> Errors => ValidationResults.Where(r => !r.IsValid).ToList().AsReadOnly();
    
    /// <summary>
    /// 警告のある検証結果
    /// </summary>
    public IReadOnlyList<SettingValidationResult> Warnings => ValidationResults
        .Where(r => r.IsValid && !string.IsNullOrEmpty(r.WarningMessage)).ToList().AsReadOnly();
    
    /// <summary>
    /// エラー数
    /// </summary>
    public int ErrorCount => Errors.Count;
    
    /// <summary>
    /// 警告数
    /// </summary>
    public int WarningCount => Warnings.Count;
    
    /// <summary>
    /// 警告があるかどうか
    /// </summary>
    public bool HasWarnings => WarningCount > 0;
    
    /// <summary>
    /// 検証実行日時
    /// </summary>
    public DateTime Timestamp { get; }
    
    /// <summary>
    /// 検証対象の設定カテゴリ
    /// </summary>
    public string? Category { get; }
    
    /// <summary>
    /// 検証にかかった時間（ミリ秒）
    /// </summary>
    public long ValidationTimeMs { get; }

    /// <summary>
    /// SettingsValidationResultを初期化します
    /// </summary>
    /// <param name="validationResults">個別の検証結果</param>
    /// <param name="category">検証対象カテゴリ</param>
    /// <param name="validationTimeMs">検証時間</param>
    public SettingsValidationResult(
        IEnumerable<SettingValidationResult> validationResults, 
        string? category = null,
        long validationTimeMs = 0)
    {
        var results = validationResults?.ToList() ?? throw new ArgumentNullException(nameof(validationResults));
        ValidationResults = results;
        IsValid = results.All(r => r.IsValid);
        Category = category;
        ValidationTimeMs = validationTimeMs;
        Timestamp = DateTime.Now;
    }
    
    /// <summary>
    /// 成功した検証結果を作成します（検証対象なし）
    /// </summary>
    /// <returns>成功した検証結果</returns>
    public static SettingsValidationResult CreateSuccess()
    {
        return new SettingsValidationResult([]);
    }

    /// <summary>
    /// 成功した検証結果を作成します（警告付き）
    /// </summary>
    /// <param name="warnings">警告メッセージ</param>
    /// <returns>成功した検証結果</returns>
    public static SettingsValidationResult CreateSuccess(IEnumerable<string> warnings)
    {
        if (warnings == null || !warnings.Any())
        {
            return CreateSuccess();
        }

        var warningResults = warnings.Select(warning =>
        {
            var dummyMetadata = CreateDummyMetadata();
            return new SettingValidationResult(dummyMetadata, null, true, null, warning);
        });

        return new SettingsValidationResult(warningResults);
    }
    
    /// <summary>
    /// 失敗した検証結果を作成します
    /// </summary>
    /// <param name="errorMessage">エラーメッセージ</param>
    /// <param name="metadata">メタデータ（任意）</param>
    /// <returns>失敗した検証結果</returns>
    public static SettingsValidationResult CreateFailure(string errorMessage, SettingMetadata? metadata = null)
    {
        // エラーメッセージの検証を厳密に行う - ArgumentExceptionに統一
        if (string.IsNullOrEmpty(errorMessage))
        {
            throw new ArgumentException("エラーメッセージは必須です", nameof(errorMessage));
        }
        
        SettingValidationResult failureResult;
        if (metadata != null)
        {
            failureResult = SettingValidationResult.Failure(metadata, null, errorMessage);
        }
        else
        {
            // ダミーメタデータを作成
            var dummyMetadata = CreateDummyMetadata();
            failureResult = new SettingValidationResult(dummyMetadata, null, false, errorMessage, null);
        }
        
        return new SettingsValidationResult([failureResult]);
    }

    /// <summary>
    /// 失敗した検証結果を作成します（複数エラー）
    /// </summary>
    /// <param name="errors">エラーメッセージ</param>
    /// <param name="warnings">警告メッセージ（任意）</param>
    /// <returns>失敗した検証結果</returns>
    public static SettingsValidationResult CreateFailure(IEnumerable<string> errors, IEnumerable<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(errors);
        
        var errorList = errors.ToList();
        if (errorList.Count == 0)
        {
            throw new ArgumentException("At least one error is required for failure result", nameof(errors));
        }

        var results = new List<SettingValidationResult>();
        var dummyMetadata = CreateDummyMetadata();

        // Add error results
        foreach (var error in errorList)
        {
            results.Add(new SettingValidationResult(dummyMetadata, null, false, error, null));
        }

        // Add warning results
        if (warnings != null)
        {
            foreach (var warning in warnings)
            {
                results.Add(new SettingValidationResult(dummyMetadata, null, true, null, warning));
            }
        }

        return new SettingsValidationResult(results);
    }
    
    /// <summary>
    /// ダミーメタデータを作成します
    /// </summary>
    /// <returns>ダミーメタデータ</returns>
    private static SettingMetadata CreateDummyMetadata()
    {
        var dummyProperty = typeof(DummySettings).GetProperty(nameof(DummySettings.ErrorProperty)) ?? throw new InvalidOperationException("ダミープロパティが見つかりません");
        var dummyAttribute = new SettingMetadataAttribute(SettingLevel.Basic, "Error", "Error");
        return new SettingMetadata(dummyProperty, dummyAttribute);
    }
    
    /// <summary>
    /// エラーメッセージを取得します
    /// </summary>
    /// <param name="separator">区切り文字</param>
    /// <returns>結合されたエラーメッセージ</returns>
    public string GetErrorMessages(string separator = "\n")
    {
        return string.Join(separator, Errors.Select(e => e.ErrorMessage).Where(m => !string.IsNullOrEmpty(m)));
    }
    
    /// <summary>
    /// 警告メッセージを取得します
    /// </summary>
    /// <param name="separator">区切り文字</param>
    /// <returns>結合された警告メッセージ</returns>
    public string GetWarningMessages(string separator = "\n")
    {
        return string.Join(separator, Warnings.Select(w => w.WarningMessage).Where(m => !string.IsNullOrEmpty(m)));
    }
    
    /// <summary>
    /// 結果のサマリー
    /// </summary>
    public string Summary
    {
        get
        {
            if (IsValid && WarningCount == 0)
            {
                return $"設定検証完了: {ValidationResults.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}項目すべて正常";
            }
            
            var summary = $"設定検証完了: {ValidationResults.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}項目中";
            
            if (ErrorCount > 0)
            {
                summary += $" {ErrorCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}エラー";
            }
            
            if (WarningCount > 0)
            {
                summary += $" {WarningCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}警告";
            }
            
            return summary;
        }
    }
}

/// <summary>
/// ダミー設定クラス（エラー用）
/// </summary>
internal sealed class DummySettings
{
    public string ErrorProperty { get; set; } = string.Empty;
}
