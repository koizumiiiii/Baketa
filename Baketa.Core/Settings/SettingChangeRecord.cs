using System;

namespace Baketa.Core.Settings;

/// <summary>
/// 設定変更履歴レコード
/// 設定変更の履歴を記録・追跡するためのデータ構造
/// </summary>
/// <remarks>
/// SettingChangeRecordを初期化します
/// </remarks>
/// <param name="settingKey">設定キー</param>
/// <param name="category">カテゴリ</param>
/// <param name="oldValue">変更前の値</param>
/// <param name="newValue">変更後の値</param>
/// <param name="changeType">変更の種類</param>
/// <param name="source">変更元</param>
/// <param name="comment">コメント</param>
/// <param name="userId">ユーザーID</param>
/// <param name="sessionId">セッションID</param>
/// <param name="profileId">プロファイルID</param>
/// <param name="importance">重要度</param>
/// <param name="triggeredOtherChanges">他の変更をトリガーしたか</param>
/// <param name="undoInformation">元に戻す情報</param>
public sealed class SettingChangeRecord(
    string settingKey,
    string category,
    object? oldValue,
    object? newValue,
    SettingChangeType changeType,
    string source,
    string? comment = null,
    string? userId = null,
    string? sessionId = null,
    string? profileId = null,
    ChangeImportance importance = ChangeImportance.Normal,
    bool triggeredOtherChanges = false,
    string? undoInformation = null)
{
    /// <summary>
    /// 変更のユニークID
    /// </summary>
    public string Id { get; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 変更された設定のキー
    /// </summary>
    public string SettingKey { get; } = settingKey ?? throw new ArgumentNullException(nameof(settingKey));

    /// <summary>
    /// 設定カテゴリ
    /// </summary>
    public string Category { get; } = category ?? throw new ArgumentNullException(nameof(category));

    /// <summary>
    /// 変更前の値
    /// </summary>
    public object? OldValue { get; } = oldValue;

    /// <summary>
    /// 変更後の値
    /// </summary>
    public object? NewValue { get; } = newValue;

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

    /// <summary>
    /// 変更元（UI、API、マイグレーション等）
    /// </summary>
    public string Source { get; } = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>
    /// ユーザーID（将来の拡張用）
    /// </summary>
    public string? UserId { get; } = userId;

    /// <summary>
    /// セッションID（関連する変更をグループ化）
    /// </summary>
    public string? SessionId { get; } = sessionId;

    /// <summary>
    /// 変更が適用されたプロファイルID
    /// </summary>
    public string? ProfileId { get; } = profileId;

    /// <summary>
    /// 変更の重要度
    /// </summary>
    public ChangeImportance Importance { get; } = importance;

    /// <summary>
    /// この変更が他の変更をトリガーしたかどうか
    /// </summary>
    public bool TriggeredOtherChanges { get; } = triggeredOtherChanges;

    /// <summary>
    /// 元に戻すための情報
    /// </summary>
    public string? UndoInformation { get; } = undoInformation;

    /// <summary>
    /// 変更の説明を取得します
    /// </summary>
    /// <returns>変更の説明文</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate",
        Justification = "計算処理を含むためメソッドとして保持")]
    public string GetDescription()
    {
        var action = ChangeType switch
        {
            SettingChangeType.Created => "作成",
            SettingChangeType.Updated => "更新",
            SettingChangeType.Deleted => "削除",
            SettingChangeType.Reset => "リセット",
            SettingChangeType.Restored => "復元",
            SettingChangeType.Migrated => "マイグレーション",
            SettingChangeType.ProfileSwitched => "プロファイル切り替え",
            SettingChangeType.BatchImport => "一括インポート",
            SettingChangeType.BatchExport => "一括エクスポート",
            SettingChangeType.AutoAdjusted => "自動調整",
            _ => "変更"
        };

        var valueInfo = ChangeType == SettingChangeType.Deleted
            ? $"値: {OldValue}"
            : $"値: {OldValue} → {NewValue}";

        return $"[{action}] {Category}.{SettingKey}: {valueInfo}";
    }

    /// <summary>
    /// 変更のサマリーを取得します
    /// </summary>
    /// <returns>短縮されたサマリー</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate",
        Justification = "計算処理を含むためメソッドとして保持")]
    public string GetSummary()
    {
        return $"{ChangeType} {Category}.{SettingKey} ({Timestamp:HH:mm:ss})";
    }

    /// <summary>
    /// この変更が重要かどうかを判定します
    /// </summary>
    /// <returns>重要な変更の場合はtrue</returns>
    public bool IsImportant()
    {
        return Importance == ChangeImportance.High || Importance == ChangeImportance.Critical;
    }

    /// <summary>
    /// この変更が元に戻せるかどうかを判定します
    /// </summary>
    /// <returns>元に戻せる場合はtrue</returns>
    public bool CanUndo()
    {
        return !string.IsNullOrEmpty(UndoInformation) &&
               ChangeType != SettingChangeType.Migrated &&
               ChangeType != SettingChangeType.Restored &&
               ChangeType != SettingChangeType.BatchExport &&
               ChangeType != SettingChangeType.AutoAdjusted;
    }
}

/// <summary>
/// 変更の重要度
/// </summary>
public enum ChangeImportance
{
    /// <summary>
    /// 低重要度（ログレベル等）
    /// </summary>
    Low,

    /// <summary>
    /// 通常重要度
    /// </summary>
    Normal,

    /// <summary>
    /// 高重要度（セキュリティ設定等）
    /// </summary>
    High,

    /// <summary>
    /// 緊急重要度（システム設定等）
    /// </summary>
    Critical
}
