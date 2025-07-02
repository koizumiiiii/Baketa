using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Baketa.UI.Services;

/// <summary>
/// 設定変更追跡サービスの実装
/// 設定の変更状態を監視し、保存確認機能を提供
/// </summary>
public sealed class SettingsChangeTracker : ISettingsChangeTracker
{
    private readonly HashSet<string> _changedCategories = [];
    private readonly Dictionary<string, Dictionary<string, (object? OldValue, object? NewValue)>> _changes = [];

    /// <summary>
    /// 設定に変更があるかどうか
    /// </summary>
    public bool HasChanges => _changedCategories.Count > 0;

    /// <summary>
    /// 変更状態が変わった時に発生するイベント
    /// </summary>
    public event EventHandler<HasChangesChangedEventArgs>? HasChangesChanged;

    /// <summary>
    /// 変更を記録します
    /// </summary>
    /// <param name="categoryId">変更されたカテゴリID</param>
    /// <param name="propertyName">変更されたプロパティ名</param>
    /// <param name="oldValue">変更前の値</param>
    /// <param name="newValue">変更後の値</param>
    public void TrackChange(string categoryId, string propertyName, object? oldValue, object? newValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        var hadChanges = HasChanges;

        // カテゴリの変更辞書を取得または作成
        if (!_changes.TryGetValue(categoryId, out var categoryChanges))
        {
            categoryChanges = [];
            _changes[categoryId] = categoryChanges;
        }

        // 変更を記録
        categoryChanges[propertyName] = (oldValue, newValue);
        _changedCategories.Add(categoryId);

        // 変更状態が変わった場合はイベントを発行
        if (!hadChanges && HasChanges)
        {
            HasChangesChanged?.Invoke(this, new HasChangesChangedEventArgs(true));
        }
    }

    /// <summary>
    /// すべての変更をクリアします
    /// </summary>
    public void ClearChanges()
    {
        var hadChanges = HasChanges;

        _changedCategories.Clear();
        _changes.Clear();

        // 変更状態が変わった場合はイベントを発行
        if (hadChanges && !HasChanges)
        {
            HasChangesChanged?.Invoke(this, new HasChangesChangedEventArgs(false));
        }
    }

    /// <summary>
    /// 特定カテゴリの変更をクリアします
    /// </summary>
    /// <param name="categoryId">カテゴリID</param>
    public void ClearChanges(string categoryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);

        var hadChanges = HasChanges;

        _changedCategories.Remove(categoryId);
        _changes.Remove(categoryId);

        // 変更状態が変わった場合はイベントを発行
        if (hadChanges && !HasChanges)
        {
            HasChangesChanged?.Invoke(this, new HasChangesChangedEventArgs(false));
        }
    }

    /// <summary>
    /// 変更があるカテゴリの一覧を取得します
    /// </summary>
    /// <returns>変更があるカテゴリIDの配列</returns>
    public string[] GetChangedCategories()
    {
        return [.. _changedCategories];
    }

    /// <summary>
    /// 未保存の変更がある場合に保存確認ダイアログを表示します
    /// </summary>
    /// <returns>続行する場合はtrue</returns>
    public async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!HasChanges)
        {
            return true; // 変更がない場合は続行
        }

        // 確認ダイアログの表示
        var messageBox = new Window
        {
            Title = "未保存の変更",
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var result = false;

        var content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 20
        };

        content.Children.Add(new TextBlock
        {
            Text = "設定に未保存の変更があります。\n変更を破棄して続行しますか？",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 14
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };

        var cancelButton = new Button
        {
            Content = "キャンセル",
            Width = 100,
            IsDefault = false
        };
        cancelButton.Click += (_, _) =>
        {
            result = false;
            messageBox.Close();
        };

        var discardButton = new Button
        {
            Content = "破棄",
            Width = 100,
            IsDefault = true
        };
        discardButton.Click += (_, _) =>
        {
            result = true;
            messageBox.Close();
        };

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(discardButton);
        content.Children.Add(buttonPanel);

        messageBox.Content = content;

        var owner = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
            ? desktop.MainWindow 
            : null;
        
        if (owner != null)
        {
            await messageBox.ShowDialog(owner).ConfigureAwait(false);
        }
        else
        {
            await Task.Run(() => messageBox.Show()).ConfigureAwait(false);
        }

        return result;
    }
}
