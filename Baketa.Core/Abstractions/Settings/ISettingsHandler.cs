using System.Collections.Generic;
using System.Threading.Tasks;
using Baketa.Core.Settings;

namespace Baketa.Core.Abstractions.Settings;

/// <summary>
/// 設定ハンドラーインターフェース
/// 設定変更を特定のサブシステムに適用する責務を持つ
/// </summary>
public interface ISettingsHandler
{
    /// <summary>
    /// ハンドラーの優先度（数値が小さいほど高優先度）
    /// </summary>
    int Priority { get; }
    
    /// <summary>
    /// 担当するカテゴリのリスト
    /// </summary>
    IReadOnlyList<string> HandledCategories { get; }
    
    /// <summary>
    /// ハンドラーの名前
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// ハンドラーの説明
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// 設定変更を適用します
    /// </summary>
    /// <param name="oldSettings">変更前の設定</param>
    /// <param name="newSettings">変更後の設定</param>
    /// <param name="changedCategory">変更されたカテゴリ（nullの場合は全体変更）</param>
    /// <returns>適用が成功したかどうか</returns>
    Task<SettingsApplicationResult> ApplySettingsAsync(AppSettings oldSettings, AppSettings newSettings, string? changedCategory = null);
    
    /// <summary>
    /// 設定変更をロールバックします
    /// </summary>
    /// <param name="currentSettings">現在の設定</param>
    /// <param name="previousSettings">前の設定</param>
    /// <param name="changedCategory">変更されたカテゴリ（nullの場合は全体変更）</param>
    /// <returns>ロールバックが成功したかどうか</returns>
    Task<SettingsApplicationResult> RollbackSettingsAsync(AppSettings currentSettings, AppSettings previousSettings, string? changedCategory = null);
    
    /// <summary>
    /// 指定されたカテゴリをこのハンドラーが処理可能かどうかを判定します
    /// </summary>
    /// <param name="category">カテゴリ名</param>
    /// <returns>処理可能な場合はtrue</returns>
    bool CanHandle(string category);
    
    /// <summary>
    /// 設定変更前の検証を行います
    /// </summary>
    /// <param name="oldSettings">変更前の設定</param>
    /// <param name="newSettings">変更後の設定</param>
    /// <param name="changedCategory">変更されたカテゴリ</param>
    /// <returns>検証結果</returns>
    Task<SettingsValidationResult> ValidateChangesAsync(AppSettings oldSettings, AppSettings newSettings, string? changedCategory = null);
}

/// <summary>
/// 設定適用結果
/// </summary>
/// <remarks>
/// SettingsApplicationResultを初期化します
/// </remarks>
public sealed class SettingsApplicationResult(
    bool isSuccess,
    string? errorMessage = null,
    string? warningMessage = null,
    string? appliedCategory = null,
    long elapsedMilliseconds = 0,
    bool requiresRestart = false,
    IReadOnlyDictionary<string, object>? additionalInfo = null)
{
    /// <summary>
    /// 適用が成功したかどうか
    /// </summary>
    public bool IsSuccess { get; } = isSuccess;

    /// <summary>
    /// エラーメッセージ（失敗時）
    /// </summary>
    public string? ErrorMessage { get; } = errorMessage;

    /// <summary>
    /// 警告メッセージ（成功時でも注意が必要な場合）
    /// </summary>
    public string? WarningMessage { get; } = warningMessage;

    /// <summary>
    /// 適用されたカテゴリ
    /// </summary>
    public string? AppliedCategory { get; } = appliedCategory;

    /// <summary>
    /// 実行時間（ミリ秒）
    /// </summary>
    public long ElapsedMilliseconds { get; } = elapsedMilliseconds;

    /// <summary>
    /// 再起動が必要かどうか
    /// </summary>
    public bool RequiresRestart { get; } = requiresRestart;

    /// <summary>
    /// 追加情報
    /// </summary>
    public IReadOnlyDictionary<string, object> AdditionalInfo { get; } = additionalInfo ?? new Dictionary<string, object>();

    /// <summary>
    /// 成功結果を作成します
    /// </summary>
    public static SettingsApplicationResult Success(string? appliedCategory = null, string? warningMessage = null, bool requiresRestart = false)
    {
        return new SettingsApplicationResult(true, null, warningMessage, appliedCategory, requiresRestart: requiresRestart);
    }
    
    /// <summary>
    /// 失敗結果を作成します
    /// </summary>
    public static SettingsApplicationResult Failure(string errorMessage, string? appliedCategory = null)
    {
        return new SettingsApplicationResult(false, errorMessage, null, appliedCategory);
    }
}
