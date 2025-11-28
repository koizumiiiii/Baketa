using System;
using System.Collections.Generic;
using System.ComponentModel;
using Baketa.Core.Settings;
using Baketa.UI.Models.Settings;

namespace Baketa.UI.Tests.Stubs;

/// <summary>
/// SettingsWindowViewModelのテスト用Stub実装
/// ReactiveUIに依存しない軽量版でハングを防ぐ
/// </summary>
public sealed class StubSettingsWindowViewModel : INotifyPropertyChanged
{
    private SettingCategory? _selectedCategory;
    private string _statusMessage = string.Empty;
    private bool _hasChanges;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// すべての設定カテゴリ（3カテゴリに簡素化）
    /// </summary>
    public IReadOnlyList<SettingCategory> AllCategories { get; }

    /// <summary>
    /// 選択されたカテゴリ
    /// </summary>
    public SettingCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory != value)
            {
                _selectedCategory = value;
                OnPropertyChanged(nameof(SelectedCategory));
            }
        }
    }

    /// <summary>
    /// ステータスメッセージ
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }
    }

    /// <summary>
    /// 変更があるかどうか
    /// </summary>
    public bool HasChanges
    {
        get => _hasChanges;
        set
        {
            if (_hasChanges != value)
            {
                _hasChanges = value;
                OnPropertyChanged(nameof(HasChanges));
            }
        }
    }

    public StubSettingsWindowViewModel()
    {
        // テスト用のカテゴリを作成（3カテゴリに簡素化）
        AllCategories = CreateTestCategories();

        // 初期カテゴリの選択
        SelectedCategory = AllCategories.Count > 0 ? AllCategories[0] : null;
    }

    /// <summary>
    /// テスト用カテゴリを作成（一般設定、アカウントの2カテゴリ）
    /// </summary>
    private static IReadOnlyList<SettingCategory> CreateTestCategories()
    {
        return
        [
            new SettingCategory
            {
                Id = "settings_general",
                Name = "一般設定",
                IconData = "M12,2C13.1,2 14,2.9 14,4C14,5.1 13.1,6 12,6C10.9,6 10,5.1 10,4C10,2.9 10.9,2 12,2Z",
                Description = "アプリケーションの基本設定",
                Level = SettingLevel.Basic,
                DisplayOrder = 1
            },
            new SettingCategory
            {
                Id = "settings_account",
                Name = "アカウント",
                IconData = "M12,4A4,4 0 0,1 16,8A4,4 0 0,1 12,12A4,4 0 0,1 8,8A4,4 0 0,1 12,4M12,14C16.42,14 20,15.79 20,18V20H4V18C4,15.79 7.58,14 12,14Z",
                Description = "ユーザー認証とアカウント管理",
                Level = SettingLevel.Basic,
                DisplayOrder = 2
            }
        ];
    }

    /// <summary>
    /// PropertyChangedイベントを発火
    /// </summary>
    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        // Stub実装では特別な処理は不要
    }
}
