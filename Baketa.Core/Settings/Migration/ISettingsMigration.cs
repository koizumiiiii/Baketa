using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Baketa.Core.Settings.Migration;

/// <summary>
/// 設定マイグレーションインターフェース
/// 設定スキーマのバージョン間移行を定義
/// </summary>
public interface ISettingsMigration
{
    /// <summary>
    /// 移行元のスキーマバージョン
    /// </summary>
    int FromVersion { get; }
    
    /// <summary>
    /// 移行先のスキーマバージョン
    /// </summary>
    int ToVersion { get; }
    
    /// <summary>
    /// マイグレーションの説明
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// マイグレーションの実行可能性をチェックします
    /// </summary>
    /// <param name="currentSettings">現在の設定データ</param>
    /// <returns>実行可能な場合はtrue</returns>
    bool CanMigrate(Dictionary<string, object?> currentSettings);
    
    /// <summary>
    /// マイグレーションを実行します
    /// </summary>
    /// <param name="currentSettings">現在の設定データ</param>
    /// <returns>マイグレーション結果</returns>
    Task<MigrationResult> MigrateAsync(Dictionary<string, object?> currentSettings);
    
    /// <summary>
    /// マイグレーションをドライラン（実行せずに検証のみ）します
    /// </summary>
    /// <param name="currentSettings">現在の設定データ</param>
    /// <returns>ドライラン結果</returns>
    Task<MigrationResult> DryRunAsync(Dictionary<string, object?> currentSettings);
}

/// <summary>
/// マイグレーション結果
/// </summary>
public sealed class MigrationResult
{
    /// <summary>
    /// マイグレーションが成功したかどうか
    /// </summary>
    public bool IsSuccess { get; }
    
    /// <summary>
    /// マイグレーションが成功したかどうか（IsSuccessのエイリアス）
    /// </summary>
    public bool Success => IsSuccess;
    
    /// <summary>
    /// マイグレーション後の設定データ
    /// </summary>
    public Dictionary<string, object?> MigratedSettings { get; }
    
    /// <summary>
    /// エラーメッセージ（失敗時）
    /// </summary>
    public string? ErrorMessage { get; }
    
    /// <summary>
    /// 警告メッセージ
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }
    
    /// <summary>
    /// 変更された設定項目の一覧
    /// </summary>
    public IReadOnlyList<MigrationSettingChange> Changes { get; }
    
    /// <summary>
    /// 削除された設定項目の一覧
    /// </summary>
    public IReadOnlyList<string> DeletedSettings { get; }
    
    /// <summary>
    /// 追加された設定項目の一覧
    /// </summary>
    public IReadOnlyList<string> AddedSettings { get; }
    
    /// <summary>
    /// マイグレーション実行時間（ミリ秒）
    /// </summary>
    public long ExecutionTimeMs { get; }
    
    /// <summary>
    /// マイグレーション日時
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// 成功したマイグレーション結果を作成します
    /// </summary>
    /// <param name="migratedSettings">マイグレーション後の設定</param>
    /// <param name="changes">変更一覧</param>
    /// <param name="deletedSettings">削除された設定</param>
    /// <param name="addedSettings">追加された設定</param>
    /// <param name="warnings">警告メッセージ</param>
    /// <param name="executionTimeMs">実行時間</param>
    /// <returns>マイグレーション結果</returns>
    public static MigrationResult CreateSuccess(
        Dictionary<string, object?> migratedSettings,
        IReadOnlyList<MigrationSettingChange>? changes = null,
        IReadOnlyList<string>? deletedSettings = null,
        IReadOnlyList<string>? addedSettings = null,
        IReadOnlyList<string>? warnings = null,
        long executionTimeMs = 0)
    {
        return new MigrationResult(
            true, 
            migratedSettings, 
            null, 
            warnings ?? [], 
            changes ?? [],
            deletedSettings ?? [],
            addedSettings ?? [],
            executionTimeMs);
    }
    
    /// <summary>
    /// 失敗したマイグレーション結果を作成します
    /// </summary>
    /// <param name="errorMessage">エラーメッセージ</param>
    /// <param name="executionTimeMs">実行時間</param>
    /// <returns>マイグレーション結果</returns>
    public static MigrationResult CreateFailure(string errorMessage, long executionTimeMs = 0)
    {
        return new MigrationResult(
            false, 
            [], 
            errorMessage, 
            [], 
            [],
            [],
            [],
            executionTimeMs);
    }

    private MigrationResult(
        bool isSuccess, 
        Dictionary<string, object?> migratedSettings, 
        string? errorMessage, 
        IReadOnlyList<string> warnings,
        IReadOnlyList<MigrationSettingChange> changes,
        IReadOnlyList<string> deletedSettings,
        IReadOnlyList<string> addedSettings,
        long executionTimeMs)
    {
        IsSuccess = isSuccess;
        MigratedSettings = migratedSettings ?? throw new ArgumentNullException(nameof(migratedSettings));
        ErrorMessage = errorMessage;
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        Changes = changes ?? throw new ArgumentNullException(nameof(changes));
        DeletedSettings = deletedSettings ?? throw new ArgumentNullException(nameof(deletedSettings));
        AddedSettings = addedSettings ?? throw new ArgumentNullException(nameof(addedSettings));
        ExecutionTimeMs = executionTimeMs;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// マイグレーション設定変更情報
/// </summary>
public sealed class MigrationSettingChange
{
    /// <summary>
    /// 設定キー
    /// </summary>
    public string Key { get; }
    
    /// <summary>
    /// 変更前の値
    /// </summary>
    public object? OldValue { get; }
    
    /// <summary>
    /// 変更後の値
    /// </summary>
    public object? NewValue { get; }
    
    /// <summary>
    /// 変更理由
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// MigrationSettingChangeを初期化します
    /// </summary>
    /// <param name="key">設定キー</param>
    /// <param name="oldValue">変更前の値</param>
    /// <param name="newValue">変更後の値</param>
    /// <param name="reason">変更理由</param>
    public MigrationSettingChange(string key, object? oldValue, object? newValue, string reason)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        OldValue = oldValue;
        NewValue = newValue;
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }
}
