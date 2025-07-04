using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Baketa.Core.Settings;
using Baketa.UI.Models.Settings;

namespace Baketa.UI.Tests.Stubs;

/// <summary>
/// SettingsWindowViewModelのテスト用Stub実装
/// ReactiveUIに依存しない軽量版でハングを防ぐ
/// </summary>
public sealed class StubSettingsWindowViewModel : INotifyPropertyChanged
{
    private bool _showAdvancedSettings;
    private SettingCategory? _selectedCategory;
    private string _statusMessage = string.Empty;
    private bool _hasChanges;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// すべての設定カテゴリ
    /// </summary>
    public IReadOnlyList<SettingCategory> AllCategories { get; }

    /// <summary>
    /// 現在表示されているカテゴリ（フィルタリング済み）
    /// </summary>
    public IReadOnlyList<SettingCategory> VisibleCategories => ShowAdvancedSettings
        ? [.. AllCategories.Where(c => c.Level <= SettingLevel.Advanced).OrderBy(c => c.DisplayOrder)]
        : [.. AllCategories.Where(c => c.Level == SettingLevel.Basic).OrderBy(c => c.DisplayOrder)];

    /// <summary>
    /// 詳細設定を表示するかどうか
    /// </summary>
    public bool ShowAdvancedSettings
    {
        get => _showAdvancedSettings;
        set
        {
            if (_showAdvancedSettings != value)
            {
                _showAdvancedSettings = value;
                OnPropertyChanged(nameof(ShowAdvancedSettings));
                OnPropertyChanged(nameof(VisibleCategories));

                // 現在選択されているカテゴリが表示されなくなる場合、最初のカテゴリを選択
                if (_selectedCategory != null && !VisibleCategories.Contains(_selectedCategory))
                {
                    SelectedCategory = VisibleCategories.Count > 0 ? VisibleCategories[0] : null;
                }
            }
        }
    }

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
        // テスト用のカテゴリを作成
        AllCategories = CreateTestCategories();
        
        // 初期カテゴリの選択
        var initialCategory = VisibleCategories.Count > 0 ? VisibleCategories[0] : null;
        SelectedCategory = initialCategory;
    }

    /// <summary>
    /// テスト用カテゴリを作成
    /// </summary>
    private static IReadOnlyList<SettingCategory> CreateTestCategories()
    {
        return
        [
            new SettingCategory 
            { 
                Id = "general", 
                Name = "一般設定", 
                IconData = "M12,2C13.1,2 14,2.9 14,4C14,5.1 13.1,6 12,6C10.9,6 10,5.1 10,4C10,2.9 10.9,2 12,2Z", 
                Description = "アプリケーションの基本設定", 
                Level = SettingLevel.Basic, 
                DisplayOrder = 1 
            },
            new SettingCategory 
            { 
                Id = "appearance", 
                Name = "外観設定", 
                IconData = "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z", 
                Description = "UI表示の設定", 
                Level = SettingLevel.Basic, 
                DisplayOrder = 2 
            },
            new SettingCategory 
            { 
                Id = "mainui", 
                Name = "操作パネル", 
                IconData = "M3,3V21H21V3H3M5,5H19V19H5V5Z", 
                Description = "メインUIの設定", 
                Level = SettingLevel.Basic, 
                DisplayOrder = 3 
            },
            new SettingCategory 
            { 
                Id = "translation", 
                Name = "翻訳設定", 
                IconData = "M12.87,15.07L10.33,12.56L10.36,12.53C12.1,10.59 13.34,8.36 14.07,6H17V4H10V2H8V4H1V6H12.17C11.5,7.92 10.44,9.75 9,11.35C8.07,10.32 7.3,9.19 6.69,8H4.69C5.42,9.63 6.42,11.17 7.67,12.56L2.58,17.58L4,19L9,14L12.11,17.11L12.87,15.07M18.5,10H16.5L12,22H14L15.12,19H19.87L21,22H23L18.5,10M15.88,17L17.5,12.67L19.12,17H15.88Z", 
                Description = "翻訳機能の設定", 
                Level = SettingLevel.Basic, 
                DisplayOrder = 4 
            },
            new SettingCategory 
            { 
                Id = "overlay", 
                Name = "オーバーレイ", 
                IconData = "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z", 
                Description = "オーバーレイ表示の設定", 
                Level = SettingLevel.Basic, 
                DisplayOrder = 5 
            },
            new SettingCategory 
            { 
                Id = "capture", 
                Name = "キャプチャ設定",
                IconData = "M4,4H7L9,2H15L17,4H20A2,2 0 0,1 22,6V18A2,2 0 0,1 20,20H4A2,2 0 0,1 2,18V6A2,2 0 0,1 4,4M12,7A5,5 0 0,0 7,12A5,5 0 0,0 12,17A5,5 0 0,0 17,12A5,5 0 0,0 12,7Z", 
                Description = "画面キャプチャの設定", 
                Level = SettingLevel.Advanced, 
                DisplayOrder = 6 
            },
            new SettingCategory 
            { 
                Id = "ocr", 
                Name = "OCR設定", 
                IconData = "M9,3V4H4V6H5V7H6V6H7V7H8V6H9V7H10V6H11V7H12V6H13V7H14V6H15V7H16V6H17V7H18V6H19V7H20V6H21V4H16V3H9M4,8V9H3V10H4V11H3V12H4V13H3V14H4V15H3V16H4V17H3V18H4V19H3V20H4V21H5V20H6V21H7V20H8V21H9V20H10V21H11V20H12V21H13V20H14V21H15V20H16V21H17V20H18V21H19V20H20V21H21V20H20V19H21V18H20V17H21V16H20V15H21V14H20V13H21V12H20V11H21V10H20V9H21V8H4M5,9H6V10H5V9M7,9H8V10H7V9M9,9H10V10H9V9M11,9H12V10H11V9M13,9H14V10H13V9M15,9H16V10H15V9M17,9H18V10H17V9M19,9H20V10H19V9Z", 
                Description = "OCR機能の設定", 
                Level = SettingLevel.Advanced, 
                DisplayOrder = 7 
            },
            new SettingCategory 
            { 
                Id = "advanced", 
                Name = "拡張設定", 
                IconData = "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.22,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.22,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.68 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z", 
                Description = "高度な設定", 
                Level = SettingLevel.Advanced, 
                DisplayOrder = 8 
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