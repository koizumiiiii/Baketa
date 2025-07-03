using System;

namespace Baketa.Core.Settings;

/// <summary>
/// 設定変更イベント引数
/// </summary>
/// <remarks>
/// SettingChangedEventArgsを初期化します
/// </remarks>
/// <param name="settingKey">設定キー</param>
/// <param name="oldValue">変更前の値</param>
/// <param name="newValue">変更後の値</param>
/// <param name="category">設定カテゴリ</param>
/// <param name="changeType">変更の種類</param>
/// <param name="comment">変更理由・コメント</param>
public sealed class SettingChangedEventArgs(
    string settingKey,
    object? oldValue,
    object? newValue,
    string category,
    SettingChangeType changeType,
    string? comment = null) : EventArgs
{
    /// <summary>
    /// 変更された設定のキー
    /// </summary>
    public string SettingKey { get; } = settingKey ?? throw new ArgumentNullException(nameof(settingKey));

    /// <summary>
    /// 変更前の値
    /// </summary>
    public object? OldValue { get; } = oldValue;

    /// <summary>
    /// 変更後の値
    /// </summary>
    public object? NewValue { get; } = newValue;

    /// <summary>
    /// 設定カテゴリ
    /// </summary>
    public string Category { get; } = category ?? throw new ArgumentNullException(nameof(category));

    /// <summary>
    /// 変更の種類
    /// </summary>
    public SettingChangeType ChangeType { get; } = changeType;

    /// <summary>
    /// 変更日時
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.Now;

    /// <summary>
    /// 変更理由・コメント
    /// </summary>
    public string? Comment { get; } = comment;
}

/// <summary>
/// ゲームプロファイル変更イベント引数
/// </summary>
/// <remarks>
/// GameProfileChangedEventArgsを初期化します
/// </remarks>
/// <param name="profileId">プロファイルID</param>
/// <param name="profile">変更されたプロファイル</param>
/// <param name="changeType">変更の種類</param>
/// <param name="comment">変更理由・コメント</param>
public sealed class GameProfileChangedEventArgs(
    string profileId,
    GameProfileSettings? profile,
    ProfileChangeType changeType,
    string? comment = null) : EventArgs
{
    /// <summary>
    /// プロファイルID
    /// </summary>
    public string ProfileId { get; } = profileId ?? throw new ArgumentNullException(nameof(profileId));

    /// <summary>
    /// 変更されたプロファイル
    /// </summary>
    public GameProfileSettings? Profile { get; } = profile;

    /// <summary>
    /// 変更の種類
    /// </summary>
    public ProfileChangeType ChangeType { get; } = changeType;

    /// <summary>
    /// 変更日時
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.Now;

    /// <summary>
    /// 変更理由・コメント
    /// </summary>
    public string? Comment { get; } = comment;
}

/// <summary>
/// 設定保存完了イベント引数
/// </summary>
public sealed class SettingsSavedEventArgs : EventArgs
{
    /// <summary>
    /// 保存ファイルパス
    /// </summary>
    public string FilePath { get; }
    
    /// <summary>
    /// 保存された設定の数
    /// </summary>
    public int SettingCount { get; }
    
    /// <summary>
    /// 保存時間（ミリ秒）
    /// </summary>
    public long SaveTimeMs { get; }
    
    /// <summary>
    /// 保存日時
    /// </summary>
    public DateTime Timestamp { get; }
    
    /// <summary>
    /// 保存が成功したかどうか
    /// </summary>
    public bool Success { get; }
    
    /// <summary>
    /// エラーメッセージ（保存失敗時）
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// SettingsSavedEventArgsを初期化します（成功時）
    /// </summary>
    /// <param name="filePath">保存ファイルパス</param>
    /// <param name="settingCount">保存された設定の数</param>
    /// <param name="saveTimeMs">保存時間</param>
    public SettingsSavedEventArgs(string filePath, int settingCount, long saveTimeMs)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        SettingCount = settingCount;
        SaveTimeMs = saveTimeMs;
        Success = true;
        ErrorMessage = null;
        Timestamp = DateTime.Now;
    }
    
    /// <summary>
    /// SettingsSavedEventArgsを初期化します（失敗時）
    /// </summary>
    /// <param name="filePath">保存ファイルパス</param>
    /// <param name="errorMessage">エラーメッセージ</param>
    public SettingsSavedEventArgs(string filePath, string errorMessage)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        SettingCount = 0;
        SaveTimeMs = 0;
        Success = false;
        ErrorMessage = errorMessage ?? throw new ArgumentNullException(nameof(errorMessage));
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// 設定変更の種類
/// UX改善対応版 - より詳細な変更タイプをサポート
/// </summary>
public enum SettingChangeType
{
    /// <summary>
    /// 新規作成
    /// </summary>
    Created,
    
    /// <summary>
    /// 値の更新
    /// </summary>
    Updated,
    
    /// <summary>
    /// 削除
    /// </summary>
    Deleted,
    
    /// <summary>
    /// リセット（デフォルト値に戻す）
    /// </summary>
    Reset,
    
    /// <summary>
    /// 復元（バックアップから復元）
    /// </summary>
    Restored,
    
    /// <summary>
    /// マイグレーション（スキーマ更新）
    /// </summary>
    Migrated,
    
    /// <summary>
    /// プロファイル切り替え
    /// </summary>
    ProfileSwitched,
    
    /// <summary>
    /// 一括インポート
    /// </summary>
    BatchImport,
    
    /// <summary>
    /// 一括エクスポート
    /// </summary>
    BatchExport,
    
    /// <summary>
    /// 自動調整（システムによる最適化）
    /// </summary>
    AutoAdjusted
}

/// <summary>
/// プロファイル変更の種類
/// </summary>
public enum ProfileChangeType
{
    /// <summary>
    /// プロファイルの作成
    /// </summary>
    Created,
    
    /// <summary>
    /// プロファイルの更新
    /// </summary>
    Updated,
    
    /// <summary>
    /// プロファイルの削除
    /// </summary>
    Deleted,
    
    /// <summary>
    /// アクティブプロファイルの変更
    /// </summary>
    ActivationChanged,
    
    /// <summary>
    /// プロファイルの複製
    /// </summary>
    Duplicated,
    
    /// <summary>
    /// プロファイルのインポート
    /// </summary>
    Imported,
    
    /// <summary>
    /// プロファイルのエクスポート
    /// </summary>
    Exported
}
