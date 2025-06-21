using System;

namespace Baketa.UI.Services;

/// <summary>
/// 設定変更状態のイベントデータ
/// </summary>
public sealed class HasChangesChangedEventArgs : EventArgs
{
    /// <summary>
    /// 変更状態
    /// </summary>
    public bool HasChanges { get; }

    /// <summary>
    /// HasChangesChangedEventArgsを初期化します
    /// </summary>
    /// <param name="hasChanges">変更状態</param>
    public HasChangesChangedEventArgs(bool hasChanges)
    {
        HasChanges = hasChanges;
    }
}

/// <summary>
/// 設定変更追跡サービスのインターフェース
/// 設定の変更状態を監視し、保存確認機能を提供
/// </summary>
public interface ISettingsChangeTracker
{
    /// <summary>
    /// 設定に変更があるかどうか
    /// </summary>
    bool HasChanges { get; }

    /// <summary>
    /// 変更状態が変わった時に発生するイベント
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances", Justification = "EventHandler<T> is the correct generic event handler pattern")]
    event EventHandler<HasChangesChangedEventArgs>? HasChangesChanged;

    /// <summary>
    /// 変更を記録します
    /// </summary>
    /// <param name="categoryId">変更されたカテゴリID</param>
    /// <param name="propertyName">変更されたプロパティ名</param>
    /// <param name="oldValue">変更前の値</param>
    /// <param name="newValue">変更後の値</param>
    void TrackChange(string categoryId, string propertyName, object? oldValue, object? newValue);

    /// <summary>
    /// すべての変更をクリアします
    /// </summary>
    void ClearChanges();

    /// <summary>
    /// 特定カテゴリの変更をクリアします
    /// </summary>
    /// <param name="categoryId">カテゴリID</param>
    void ClearChanges(string categoryId);

    /// <summary>
    /// 変更があるカテゴリの一覧を取得します
    /// </summary>
    /// <returns>変更があるカテゴリIDの配列</returns>
    string[] GetChangedCategories();

    /// <summary>
    /// 未保存の変更がある場合に保存確認ダイアログを表示します
    /// </summary>
    /// <returns>続行する場合はtrue</returns>
    Task<bool> ConfirmDiscardChangesAsync();
}
