using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Baketa.Core.Settings;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Models.Settings;
using Baketa.UI.Services;
using Baketa.UI.Framework;
using Baketa.UI.Views.Settings;
using Baketa.UI.ViewModels.Settings;
using Microsoft.Extensions.Logging;
using UiFramework = Baketa.UI.Framework;

namespace Baketa.UI.ViewModels;

/// <summary>
/// 設定ウィンドウのViewModel
/// プログレッシブディスクロージャーによる基本/詳細設定の階層表示をサポート
/// </summary>
public sealed class SettingsWindowViewModel : UiFramework.ViewModelBase
{
    private readonly ISettingsChangeTracker _changeTracker;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<SettingsWindowViewModel>? _logger;
    private bool _showAdvancedSettings;
    private SettingCategory? _selectedCategory;
    private string _statusMessage = string.Empty;

    /// <summary>
    /// SettingsWindowViewModelを初期化します
    /// </summary>
    /// <param name="changeTracker">設定変更追跡サービス</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー（オプション）</param>
    public SettingsWindowViewModel(
        ISettingsChangeTracker changeTracker,
        IEventAggregator eventAggregator,
        ILogger<SettingsWindowViewModel>? logger = null) : base(eventAggregator)
    {
        _changeTracker = changeTracker ?? throw new ArgumentNullException(nameof(changeTracker));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger;

        // カテゴリの初期化
        InitializeCategories();

        // 変更追跡の設定
        this._changeTracker.HasChangesChanged += (_, e) =>
        {
            this.RaisePropertyChanged(nameof(HasChanges));
            StatusMessage = e.HasChanges ? "設定に変更があります" : "変更なし";
        };

        // コマンドの初期化
        ToggleAdvancedSettingsCommand = ReactiveCommand.Create(ToggleAdvancedSettings);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, this.WhenAnyValue(x => x.HasChanges));
        CancelCommand = ReactiveCommand.CreateFromTask(CancelAsync);
        ResetCommand = ReactiveCommand.CreateFromTask(ResetAsync);

        // 初期カテゴリの選択
        SelectedCategory = VisibleCategories.Count > 0 ? VisibleCategories[0] : null;
    }

    #region プロパティ

    /// <summary>
    /// すべての設定カテゴリ
    /// </summary>
    public IReadOnlyList<SettingCategory> AllCategories { get; private set; } = [];

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
            this.RaiseAndSetIfChanged(ref _showAdvancedSettings, value);
            this.RaisePropertyChanged(nameof(VisibleCategories));

            // 現在選択されているカテゴリが表示されない場合は最初のカテゴリを選択
            if (SelectedCategory != null && !VisibleCategories.Contains(SelectedCategory))
            {
                SelectedCategory = VisibleCategories.Count > 0 ? VisibleCategories[0] : null;
            }
        }
    }

    /// <summary>
    /// 現在選択されているカテゴリ
    /// </summary>
    public SettingCategory? SelectedCategory
    {
        get => _selectedCategory;
        set => this.RaiseAndSetIfChanged(ref _selectedCategory, value);
    }

    /// <summary>
    /// 設定に変更があるかどうか
    /// </summary>
    public bool HasChanges => this._changeTracker.HasChanges;

    /// <summary>
    /// ステータスメッセージ
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    #endregion

    #region コマンド

    /// <summary>
    /// 詳細設定表示切り替えコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ToggleAdvancedSettingsCommand { get; }

    /// <summary>
    /// 保存コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }

    /// <summary>
    /// キャンセルコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    /// <summary>
    /// リセットコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ResetCommand { get; }

    #endregion

    #region メソッド

    /// <summary>
    /// カテゴリを初期化します
    /// </summary>
    private void InitializeCategories()
    {
        var categories = new List<SettingCategory>
        {
            // 基本設定カテゴリ
            new SettingCategory
            {
                Id = "general",
                Name = "一般設定",
                IconData = "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11.03L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.22,8.95 2.27,9.22 2.46,9.37L4.57,11.03C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.22,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.68 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z", // Settings icon
                Level = SettingLevel.Basic,
                DisplayOrder = 1,
                Description = "基本的なアプリケーション設定",
                Content = CreateGeneralSettingsView()
            },

            new SettingCategory
            {
                Id = "appearance",
                Name = "外観設定",
                IconData = "M12,18.5A6.5,6.5 0 0,1 5.5,12A6.5,6.5 0 0,1 12,5.5A6.5,6.5 0 0,1 18.5,12A6.5,6.5 0 0,1 12,18.5M12,16A4,4 0 0,0 16,12A4,4 0 0,0 12,8A4,4 0 0,0 8,12A4,4 0 0,0 12,16Z", // Theme icon
                Level = SettingLevel.Basic,
                DisplayOrder = 2,
                Description = "テーマとUI外観の設定",
                Content = CreateAppearanceSettingsView()
            },

            new SettingCategory
            {
                Id = "mainui",
                Name = "操作パネル",
                IconData = "M13,9H18.5L13,3.5V9M6,2H14L20,8V20A2,2 0 0,1 18,22H6C4.89,22 4,21.1 4,20V4C4,2.89 4.89,2 6,2M15,18V16H6V18H15M18,14V12H6V14H18Z", // UI Panel icon
                Level = SettingLevel.Basic,
                DisplayOrder = 3,
                Description = "翻訳パネルの操作設定",
                Content = CreateMainUiSettingsView()
            },

            new SettingCategory
            {
                Id = "translation",
                Name = "翻訳設定",
                IconData = "M12.87,15.07L10.33,12.56L10.36,12.53C12.1,10.59 13.34,8.36 14.07,6H17V4H10V2H8V4H1V6H12.17C11.5,7.92 10.44,9.75 9,11.35C8.07,10.32 7.3,9.19 6.69,8H4.69C5.42,9.63 6.42,11.17 7.67,12.56L2.58,17.58L4,19L9,14L12.11,17.11L12.87,15.07M18.5,10H16.5L12,22H14L15.12,19H19.87L21,22H23L18.5,10M15.88,17L17.5,12.67L19.12,17H15.88Z", // Translate icon
                Level = SettingLevel.Basic,
                DisplayOrder = 4,
                Description = "翻訳エンジンとオプション設定",
                Content = CreateTranslationSettingsView()
            },

            new SettingCategory
            {
                Id = "overlay",
                Name = "オーバーレイ",
                IconData = "M3,3V21H21V3H3M19,19H5V5H19V19M17,17H7V7H17V17M15,15H9V9H15V15Z", // Overlay icon
                Level = SettingLevel.Basic,
                DisplayOrder = 5,
                Description = "オーバーレイ表示の設定",
                Content = CreateOverlaySettingsView()
            },

            // 詳細設定カテゴリ
            new SettingCategory
            {
                Id = "capture",
                Name = "キャプチャ設定",
                IconData = "M4,4H7L9,2H15L17,4H20A2,2 0 0,1 22,6V18A2,2 0 0,1 20,20H4A2,2 0 0,1 2,18V6A2,2 0 0,1 4,4M12,7A5,5 0 0,0 7,12A5,5 0 0,0 12,17A5,5 0 0,0 17,12A5,5 0 0,0 12,7M12,9A3,3 0 0,1 15,12A3,3 0 0,1 12,15A3,3 0 0,1 9,12A3,3 0 0,1 12,9Z", // Camera icon
                Level = SettingLevel.Advanced,
                DisplayOrder = 6,
                Description = "画面キャプチャの詳細設定",
                Content = CreateCaptureSettingsView()
            },

            new SettingCategory
            {
                Id = "ocr",
                Name = "OCR設定",
                IconData = "M5,3C3.89,3 3,3.89 3,5V19C3,20.11 3.89,21 5,21H11V19H5V5H12V12H19V5C19,3.89 18.11,3 17,3H5M14,2L20,8H14V2M15.5,22L14,20.5L15.5,19L17,20.5L20.5,17L22,18.5L15.5,22Z", // OCR icon
                Level = SettingLevel.Advanced,
                DisplayOrder = 7,
                Description = "OCR処理の詳細設定",
                Content = CreateOcrSettingsView()
            },

            new SettingCategory
            {
                Id = "advanced",
                Name = "拡張設定",
                IconData = "M10,4A4,4 0 0,1 14,8A4,4 0 0,1 10,12A4,4 0 0,1 6,8A4,4 0 0,1 10,4M17,12C18.1,12 19,12.9 19,14V20C19,21.1 18.1,22 17,22H3C1.9,22 1,21.1 1,20V14C1,12.9 1.9,12 3,12H17Z", // Advanced icon
                Level = SettingLevel.Advanced,
                DisplayOrder = 8,
                Description = "パフォーマンスと拡張機能の設定",
                Content = CreateAdvancedSettingsView()
            }
        };
        
        AllCategories = categories;
    }

    /// <summary>
    /// 一般設定Viewを作成します
    /// </summary>
    private GeneralSettingsView CreateGeneralSettingsView()
    {
        GeneralSettings settings = new(); // TODO: 実際の設定データを注入
        GeneralSettingsViewModel viewModel = new(settings, _eventAggregator, _logger as ILogger<GeneralSettingsViewModel>);
        return new GeneralSettingsView { DataContext = viewModel };
    }

    /// <summary>
    /// 外観設定Viewを作成します
    /// </summary>
    private ThemeSettingsView CreateAppearanceSettingsView()
    {
        ThemeSettings settings = new(); // TODO: 実際の設定データを注入
        ThemeSettingsViewModel viewModel = new(settings, _eventAggregator, _logger as ILogger<ThemeSettingsViewModel>);
        return new ThemeSettingsView { DataContext = viewModel };
    }

    /// <summary>
    /// メインUI設定Viewを作成します
    /// </summary>
    private MainUiSettingsView CreateMainUiSettingsView()
    {
        MainUiSettings settings = new(); // TODO: 実際の設定データを注入
        MainUiSettingsViewModel viewModel = new(settings, _eventAggregator, _logger as ILogger<MainUiSettingsViewModel>);
        return new MainUiSettingsView { DataContext = viewModel };
    }

    /// <summary>
    /// 翻訳設定Viewを作成します
    /// </summary>
    private static TranslationSettingsView CreateTranslationSettingsView()
    {
        // 既存のTranslationSettingsViewを使用
        return new TranslationSettingsView();
    }

    /// <summary>
    /// オーバーレイ設定Viewを作成します
    /// </summary>
    [SuppressMessage("IDisposableAnalyzers.Correctness", "CA2000:Dispose objects before losing scope", 
        Justification = "UserControlは呼び出し元のUIコンポーネントとして返され、適切に管理されます")]
    private UserControl CreateOverlaySettingsView()
    {
        _ = new OverlaySettings(); // TODO: 実際の設定データを注入
        _ = new OverlaySettingsViewModel(new OverlaySettings(), _eventAggregator, _logger as ILogger<OverlaySettingsViewModel>);
        // 簡単なスタブ実装としてUserControlを返す
        var textBlock = new Avalonia.Controls.TextBlock { Text = "オーバーレイ設定（開発中）" };
        return new UserControl
        {
            Content = textBlock
        };
    }

    /// <summary>
    /// キャプチャ設定Viewを作成します
    /// </summary>
    [SuppressMessage("IDisposableAnalyzers.Correctness", "CA2000:Dispose objects before losing scope", 
        Justification = "UserControlは呼び出し元のUIコンポーネントとして返され、適切に管理されます")]
    private UserControl CreateCaptureSettingsView()
    {
        _ = new CaptureSettings(); // TODO: 実際の設定データを注入
        _ = new CaptureSettingsViewModel(new CaptureSettings(), _eventAggregator, _logger as ILogger<CaptureSettingsViewModel>);
        // 簡単なスタブ実装としてUserControlを返す
        var textBlock = new Avalonia.Controls.TextBlock { Text = "キャプチャ設定（開発中）" };
        return new UserControl
        {
            Content = textBlock
        };
    }

    /// <summary>
    /// OCR設定Viewを作成します
    /// </summary>
    private OcrSettingsView CreateOcrSettingsView()
    {
        OcrSettings settings = new(); // TODO: 実際の設定データを注入
        OcrSettingsViewModel viewModel = new(settings, _eventAggregator, _logger as ILogger<OcrSettingsViewModel>);
        return new OcrSettingsView { DataContext = viewModel };
    }

    /// <summary>
    /// 拡張設定Viewを作成します
    /// </summary>
    private UserControl CreateAdvancedSettingsView()
    {
        // 簡単なスタブ実装としてUserControlを返す
        var textBlock = new Avalonia.Controls.TextBlock { Text = "拡張設定（開発中）" };
        return new UserControl
        {
            Content = textBlock
        };
    }

    /// <summary>
    /// 詳細設定表示を切り替えます
    /// </summary>
    private void ToggleAdvancedSettings()
    {
        ShowAdvancedSettings = !ShowAdvancedSettings;
    }

    /// <summary>
    /// 設定を保存します
    /// </summary>
    private async Task SaveAsync()
    {
        try
        {
            StatusMessage = "設定を保存中...";

            // TODO: 実際の設定保存処理を実装
            await Task.Delay(500).ConfigureAwait(false); // シミュレーション

            this._changeTracker.ClearChanges();
            StatusMessage = "設定を保存しました";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = $"保存に失敗しました: {ex.Message}";
            _logger?.LogError(ex, "設定の保存中にエラーが発生しました");
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusMessage = $"保存に失敗しました: アクセスが拒否されました";
            _logger?.LogError(ex, "設定ファイルへのアクセスが拒否されました");
        }
        catch (System.IO.IOException ex)
        {
            StatusMessage = $"保存に失敗しました: ファイルI/Oエラー";
            _logger?.LogError(ex, "設定ファイルのI/Oエラーが発生しました");
        }
    }

    /// <summary>
    /// 変更をキャンセルして閉じます
    /// </summary>
    private async Task CancelAsync()
    {
        if (await this._changeTracker.ConfirmDiscardChangesAsync().ConfigureAwait(false))
        {
            // TODO: ウィンドウを閉じる処理
            StatusMessage = "変更をキャンセルしました";
        }
    }

    /// <summary>
    /// 設定をリセットします
    /// </summary>
    private async Task ResetAsync()
    {
        if (await this._changeTracker.ConfirmDiscardChangesAsync().ConfigureAwait(false))
        {
            // TODO: 設定のリセット処理を実装
            StatusMessage = "設定をリセットしました";
        }
    }

    #endregion
}
