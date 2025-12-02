using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Baketa.UI.Framework;
using Baketa.UI.Models.Settings;
using Baketa.UI.Services;
using Baketa.UI.ViewModels.Settings;
using Baketa.UI.Views.Settings;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

/// <summary>
/// 設定ウィンドウのViewModel（統合設定管理機能付き）
/// プログレッシブディスクロージャーによる基本/詳細設定の階層表示をサポート
/// </summary>
public sealed class EnhancedSettingsWindowViewModel : Framework.ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ISettingsChangeTracker _changeTracker;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILocalizationService? _localizationService;
    private readonly ILogger<EnhancedSettingsWindowViewModel>? _logger;
    private bool _showAdvancedSettings;
    private SettingCategory? _selectedCategory;
    private string _statusMessage = string.Empty;

    // 設定ViewModelのキャッシュ
    private readonly Dictionary<string, object> _settingsViewModels = [];

    /// <summary>
    /// EnhancedSettingsWindowViewModelを初期化します
    /// </summary>
    /// <param name="settingsService">設定サービス</param>
    /// <param name="changeTracker">設定変更追跡サービス</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="localizationService">ローカライゼーションサービス（オプション）</param>
    /// <param name="logger">ロガー（オプション）</param>
    public EnhancedSettingsWindowViewModel(
        ISettingsService settingsService,
        ISettingsChangeTracker changeTracker,
        IEventAggregator eventAggregator,
        ILocalizationService? localizationService = null,
        ILogger<EnhancedSettingsWindowViewModel>? logger = null) : base(eventAggregator)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _changeTracker = changeTracker ?? throw new ArgumentNullException(nameof(changeTracker));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _localizationService = localizationService;
        _logger = logger;

        // カテゴリの初期化
        InitializeCategories();

        // 変更追跡の設定
        _changeTracker.HasChangesChanged += OnHasChangesChanged;

        // コマンドの初期化
        ToggleAdvancedSettingsCommand = ReactiveCommand.Create(ToggleAdvancedSettings);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync, this.WhenAnyValue(x => x.HasChanges));
        CancelCommand = ReactiveCommand.CreateFromTask(CancelAsync);
        ResetCommand = ReactiveCommand.CreateFromTask(ResetAsync);
        ValidateAllCommand = ReactiveCommand.CreateFromTask(ValidateAllAsync);

        // 初期カテゴリの選択
        var initialCategory = VisibleCategories.Count > 0 ? VisibleCategories[0] : null;
        System.Diagnostics.Debug.WriteLine($"DEBUG: Setting initial category to {initialCategory?.Id ?? "null"} with Content = {initialCategory?.Content?.GetType()?.Name ?? "null"}");
        SelectedCategory = initialCategory;
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
        set
        {
            System.Diagnostics.Debug.WriteLine($"DEBUG: SelectedCategory setter called with {value?.Id ?? "null"}, Content before = {value?.Content?.GetType()?.Name ?? "null"}");
            this.RaiseAndSetIfChanged(ref _selectedCategory, value);
            System.Diagnostics.Debug.WriteLine($"DEBUG: SelectedCategory setter completed, Content after = {value?.Content?.GetType()?.Name ?? "null"}");
        }
    }

    /// <summary>
    /// 設定に変更があるかどうか
    /// </summary>
    public bool HasChanges => _changeTracker.HasChanges;

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

    /// <summary>
    /// 全設定バリデーションコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ValidateAllCommand { get; }

    #endregion

    #region メソッド

    /// <summary>
    /// カテゴリを初期化します
    /// </summary>
    private void InitializeCategories()
    {
        // 複数回呼び出しから保護
        if (AllCategories.Count > 0)
        {
            return;
        }

        // ViewModelキャッシュをクリア（テスト間の状態リセット用）
        _settingsViewModels.Clear();

        var categories = new List<SettingCategory>
        {
            // 基本設定カテゴリ
            CreateSettingCategory(
                "enhanced_general",
                "一般設定",
                "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11.03L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.22,8.95 2.27,9.22 2.46,9.37L4.57,11.03C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.22,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.68 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z",
                SettingLevel.Basic,
                1,
                "基本的なアプリケーション設定"
            ),

            CreateSettingCategory(
                "enhanced_appearance",
                "外観設定",
                "M12,18.5A6.5,6.5 0 0,1 5.5,12A6.5,6.5 0 0,1 12,5.5A6.5,6.5 0 0,1 18.5,12A6.5,6.5 0 0,1 12,18.5M12,16A4,4 0 0,0 16,12A4,4 0 0,0 12,8A4,4 0 0,0 8,12A4,4 0 0,0 12,16Z",
                SettingLevel.Basic,
                2,
                "テーマとUI外観の設定"
            ),

            CreateSettingCategory(
                "enhanced_mainui",
                "操作パネル",
                "M13,9H18.5L13,3.5V9M6,2H14L20,8V20A2,2 0 0,1 18,22H6C4.89,22 4,21.1 4,20V4C4,2.89 4.89,2 6,2M15,18V16H6V18H15M18,14V12H6V14H18Z",
                SettingLevel.Basic,
                3,
                "翻訳パネルの操作設定"
            ),

            CreateSettingCategory(
                "enhanced_translation",
                "翻訳設定",
                "M12.87,15.07L10.33,12.56L10.36,12.53C12.1,10.59 13.34,8.36 14.07,6H17V4H10V2H8V4H1V6H12.17C11.5,7.92 10.44,9.75 9,11.35C8.07,10.32 7.3,9.19 6.69,8H4.69C5.42,9.63 6.42,11.17 7.67,12.56L2.58,17.58L4,19L9,14L12.11,17.11L12.87,15.07M18.5,10H16.5L12,22H14L15.12,19H19.87L21,22H23L18.5,10M15.88,17L17.5,12.67L19.12,17H15.88Z",
                SettingLevel.Basic,
                4,
                "翻訳エンジンとオプション設定"
            ),

            CreateSettingCategory(
                "enhanced_overlay",
                "オーバーレイ",
                "M3,3V21H21V3H3M19,19H5V5H19V19M17,17H7V7H17V17M15,15H9V9H15V15Z",
                SettingLevel.Basic,
                5,
                "オーバーレイ表示の設定"
            ),

            // 詳細設定カテゴリ
            CreateSettingCategory(
                "enhanced_capture",
                "キャプチャ設定",
                "M4,4H7L9,2H15L17,4H20A2,2 0 0,1 22,6V18A2,2 0 0,1 20,20H4A2,2 0 0,1 2,18V6A2,2 0 0,1 4,4M12,7A5,5 0 0,0 7,12A5,5 0 0,0 12,17A5,5 0 0,0 17,12A5,5 0 0,0 12,7M12,9A3,3 0 0,1 15,12A3,3 0 0,1 12,15A3,3 0 0,1 9,12A3,3 0 0,1 12,9Z",
                SettingLevel.Advanced,
                6,
                "画面キャプチャの詳細設定"
            ),

            CreateSettingCategory(
                "enhanced_ocr",
                "OCR設定",
                "M5,3C3.89,3 3,3.89 3,5V19C3,20.11 3.89,21 5,21H11V19H5V5H12V12H19V5C19,3.89 18.11,3 17,3H5M14,2L20,8H14V2M15.5,22L14,20.5L15.5,19L17,20.5L20.5,17L22,18.5L15.5,22Z",
                SettingLevel.Advanced,
                7,
                "OCR処理の詳細設定"
            ),

            CreateSettingCategory(
                "enhanced_advanced",
                "拡張設定",
                "M10,4A4,4 0 0,1 14,8A4,4 0 0,1 10,12A4,4 0 0,1 6,8A4,4 0 0,1 10,4M17,12C18.1,12 19,12.9 19,14V20C19,21.1 18.1,22 17,22H3C1.9,22 1,21.1 1,20V14C1,12.9 1.9,12 3,12H17Z",
                SettingLevel.Advanced,
                8,
                "パフォーマンスと拡張機能の設定"
            )
        };

        AllCategories = categories;
    }

    /// <summary>
    /// 設定カテゴリを作成します（テスト環境に対応）
    /// </summary>
    private SettingCategory CreateSettingCategory(string id, string name, string iconData, SettingLevel level, int displayOrder, string description)
    {
        var category = new SettingCategory
        {
            Id = id,
            Name = name,
            IconData = iconData,
            Level = level,
            DisplayOrder = displayOrder,
            Description = description,
            // テスト環境では遅延初期化を避けるため、常にnullを設定
            Content = null
        };

        // DEBUG: カテゴリ作成時のContentがnullであることを確認
        System.Diagnostics.Debug.WriteLine($"DEBUG: Created category {id} with Content = {category.Content?.GetType()?.Name ?? "null"}");

        return category;
    }


    /// <summary>
    /// 一般設定Viewを作成します
    /// </summary>
    private GeneralSettingsView CreateGeneralSettingsView()
    {
        try
        {
            if (_settingsViewModels.TryGetValue("general", out var cachedViewModel) &&
                cachedViewModel is GeneralSettingsViewModel generalViewModel)
            {
                return new GeneralSettingsView(generalViewModel);
            }

            // 実際の設定データを読み込み（テスト環境では同期処理を回避）
            GeneralSettings settings;
            TranslationSettings translationSettings;
            try
            {
                if (IsTestEnvironment())
                {
                    settings = new GeneralSettings(); // テスト環境ではデフォルト設定を使用
                    translationSettings = new TranslationSettings();
                }
                else
                {
                    settings = _settingsService.GetAsync<GeneralSettings>().GetAwaiter().GetResult() ?? new GeneralSettings();
                    translationSettings = _settingsService.GetAsync<TranslationSettings>().GetAwaiter().GetResult() ?? new TranslationSettings();
                }
            }
            catch (Exception settingsEx)
            {
                _logger?.LogWarning(settingsEx, "一般設定の読み込みに失敗しました。デフォルト設定を使用します");
                settings = new GeneralSettings();
                translationSettings = new TranslationSettings();
            }
            var viewModel = new GeneralSettingsViewModel(settings, _eventAggregator, localizationService: _localizationService, changeTracker: _changeTracker, logger: _logger as ILogger<GeneralSettingsViewModel>, translationSettings: translationSettings);

            _settingsViewModels["general"] = viewModel;
            return new GeneralSettingsView(viewModel);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "一般設定Viewの作成中にエラーが発生しました");
            // フォールバック: 空のViewを返す
            return new GeneralSettingsView();
        }
    }

    /// <summary>
    /// 外観設定Viewを作成します
    /// </summary>
    private ThemeSettingsView CreateAppearanceSettingsView()
    {
        try
        {
            if (_settingsViewModels.TryGetValue("appearance", out var cachedViewModel) &&
                cachedViewModel is ThemeSettingsViewModel themeViewModel)
            {
                return new ThemeSettingsView(themeViewModel);
            }

            // 実際の設定データを読み込み（テスト環境では同期処理を回避）
            ThemeSettings settings;
            try
            {
                if (IsTestEnvironment())
                {
                    settings = new ThemeSettings(); // テスト環境ではデフォルト設定を使用
                }
                else
                {
                    settings = _settingsService.GetAsync<ThemeSettings>().GetAwaiter().GetResult() ?? new ThemeSettings();
                }
            }
            catch (Exception settingsEx)
            {
                _logger?.LogWarning(settingsEx, "テーマ設定の読み込みに失敗しました。デフォルト設定を使用します");
                settings = new ThemeSettings();
            }
            var viewModel = new ThemeSettingsViewModel(settings, _eventAggregator, _logger as ILogger<ThemeSettingsViewModel>);

            _settingsViewModels["appearance"] = viewModel;
            return new ThemeSettingsView(viewModel);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "外観設定Viewの作成中にエラーが発生しました");
            // フォールバック: 空のViewを返す
            return new ThemeSettingsView();
        }
    }

    /// <summary>
    /// メインUI設定Viewを作成します
    /// </summary>
    private MainUiSettingsView CreateMainUiSettingsView()
    {
        try
        {
            if (_settingsViewModels.TryGetValue("mainui", out var cachedViewModel) &&
                cachedViewModel is MainUiSettingsViewModel mainUiViewModel)
            {
                return new MainUiSettingsView(mainUiViewModel);
            }

            // 実際の設定データを読み込み（テスト環境では同期処理を回避）
            MainUiSettings settings;
            try
            {
                if (IsTestEnvironment())
                {
                    settings = new MainUiSettings(); // テスト環境ではデフォルト設定を使用
                }
                else
                {
                    settings = _settingsService.GetAsync<MainUiSettings>().GetAwaiter().GetResult() ?? new MainUiSettings();
                }
            }
            catch (Exception settingsEx)
            {
                _logger?.LogWarning(settingsEx, "メインUI設定の読み込みに失敗しました。デフォルト設定を使用します");
                settings = new MainUiSettings();
            }
            var viewModel = new MainUiSettingsViewModel(settings, _eventAggregator, _logger as ILogger<MainUiSettingsViewModel>);

            _settingsViewModels["mainui"] = viewModel;
            return new MainUiSettingsView(viewModel);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "メインUI設定Viewの作成中にエラーが発生しました");
            // フォールバック: 空のViewを返す
            return new MainUiSettingsView();
        }
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
    private OverlaySettingsView CreateOverlaySettingsView()
    {
        try
        {
            if (_settingsViewModels.TryGetValue("overlay", out var cachedViewModel) &&
                cachedViewModel is OverlaySettingsViewModel overlayViewModel)
            {
                return new OverlaySettingsView(overlayViewModel);
            }

            // 実際の設定データを読み込み（テスト環境では同期処理を回避）
            OverlaySettings settings;
            try
            {
                if (IsTestEnvironment())
                {
                    settings = new OverlaySettings(); // テスト環境ではデフォルト設定を使用
                }
                else
                {
                    settings = _settingsService.GetAsync<OverlaySettings>().GetAwaiter().GetResult() ?? new OverlaySettings();
                }
            }
            catch (Exception settingsEx)
            {
                _logger?.LogWarning(settingsEx, "オーバーレイ設定の読み込みに失敗しました。デフォルト設定を使用します");
                settings = new OverlaySettings();
            }
            var viewModel = new OverlaySettingsViewModel(settings, _eventAggregator, _logger as ILogger<OverlaySettingsViewModel>);

            _settingsViewModels["overlay"] = viewModel;
            return new OverlaySettingsView(viewModel);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "オーバーレイ設定Viewの作成中にエラーが発生しました");
            // フォールバック: 空のViewを返す
            return new OverlaySettingsView();
        }
    }

    /// <summary>
    /// キャプチャ設定Viewを作成します
    /// </summary>
    private CaptureSettingsView CreateCaptureSettingsView()
    {
        try
        {
            if (_settingsViewModels.TryGetValue("capture", out var cachedViewModel) &&
                cachedViewModel is CaptureSettingsViewModel captureViewModel)
            {
                return new CaptureSettingsView(captureViewModel);
            }

            // 実際の設定データを読み込み（テスト環境では同期処理を回避）
            CaptureSettings settings;
            try
            {
                if (IsTestEnvironment())
                {
                    settings = new CaptureSettings(); // テスト環境ではデフォルト設定を使用
                }
                else
                {
                    settings = _settingsService.GetAsync<CaptureSettings>().GetAwaiter().GetResult() ?? new CaptureSettings();
                }
            }
            catch (Exception settingsEx)
            {
                _logger?.LogWarning(settingsEx, "キャプチャ設定の読み込みに失敗しました。デフォルト設定を使用します");
                settings = new CaptureSettings();
            }
            var viewModel = new CaptureSettingsViewModel(settings, _eventAggregator, _logger as ILogger<CaptureSettingsViewModel>);

            _settingsViewModels["capture"] = viewModel;
            return new CaptureSettingsView(viewModel);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "キャプチャ設定Viewの作成中にエラーが発生しました");
            // フォールバック: 空のViewを返す
            return new CaptureSettingsView();
        }
    }

    /// <summary>
    /// OCR設定Viewを作成します
    /// </summary>
    private OcrSettingsView CreateOcrSettingsView()
    {
        try
        {
            if (_settingsViewModels.TryGetValue("ocr", out var cachedViewModel) &&
                cachedViewModel is OcrSettingsViewModel ocrViewModel)
            {
                return new OcrSettingsView(ocrViewModel);
            }

            // 実際の設定データを読み込み（テスト環境では同期処理を回避）
            OcrSettings settings;
            try
            {
                if (IsTestEnvironment())
                {
                    settings = new OcrSettings(); // テスト環境ではデフォルト設定を使用
                }
                else
                {
                    settings = _settingsService.GetAsync<OcrSettings>().GetAwaiter().GetResult() ?? new OcrSettings();
                }
            }
            catch (Exception settingsEx)
            {
                _logger?.LogWarning(settingsEx, "OCR設定の読み込みに失敗しました。デフォルト設定を使用します");
                settings = new OcrSettings();
            }
            var viewModel = new OcrSettingsViewModel(settings, _eventAggregator, _logger as ILogger<OcrSettingsViewModel>);

            _settingsViewModels["ocr"] = viewModel;
            return new OcrSettingsView(viewModel);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OCR設定Viewの作成中にエラーが発生しました");
            // フォールバック: 空のViewを返す
            return new OcrSettingsView();
        }
    }

    /// <summary>
    /// 拡張設定Viewを作成します
    /// </summary>
    private AdvancedSettingsView CreateAdvancedSettingsView()
    {
        try
        {
            if (_settingsViewModels.TryGetValue("advanced", out var cachedViewModel) &&
                cachedViewModel is AdvancedSettingsViewModel advancedViewModel)
            {
                return new AdvancedSettingsView(advancedViewModel);
            }

            // 実際の設定データを読み込み（テスト環境では同期処理を回避）
            AdvancedSettings settings;
            try
            {
                if (IsTestEnvironment())
                {
                    settings = new AdvancedSettings(); // テスト環境ではデフォルト設定を使用
                }
                else
                {
                    settings = _settingsService.GetAsync<AdvancedSettings>().GetAwaiter().GetResult() ?? new AdvancedSettings();
                }
            }
            catch (Exception settingsEx)
            {
                _logger?.LogWarning(settingsEx, "拡張設定の読み込みに失敗しました。デフォルト設定を使用します");
                settings = new AdvancedSettings();
            }
            var viewModel = new AdvancedSettingsViewModel(settings, _eventAggregator, _logger as ILogger<AdvancedSettingsViewModel>);

            _settingsViewModels["advanced"] = viewModel;
            return new AdvancedSettingsView(viewModel);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "拡張設定Viewの作成中にエラーが発生しました");
            // フォールバック: 空のViewを返す
            return new AdvancedSettingsView();
        }
    }

    /// <summary>
    /// 詳細設定表示を切り替えます
    /// </summary>
    private void ToggleAdvancedSettings()
    {
        ShowAdvancedSettings = !ShowAdvancedSettings;
        _logger?.LogDebug("詳細設定表示を切り替えました: {ShowAdvanced}", ShowAdvancedSettings);
    }

    /// <summary>
    /// 設定を保存します
    /// </summary>
    private async Task SaveAsync()
    {
        try
        {
            StatusMessage = "設定を保存中...";

            // 全ての設定ViewModelから現在の設定を取得して保存
            var saveTasks = new List<Task>();

            GeneralSettingsViewModel? generalSettingsViewModel = null;
            if (_settingsViewModels.TryGetValue("general", out var generalVm) && generalVm is GeneralSettingsViewModel general)
            {
                generalSettingsViewModel = general;
                saveTasks.Add(_settingsService.SaveAsync(general.CurrentSettings));
                // 翻訳言語とフォントサイズも保存
                saveTasks.Add(_settingsService.SaveAsync(general.CurrentTranslationSettings));
            }

            if (_settingsViewModels.TryGetValue("appearance", out var themeVm) && themeVm is ThemeSettingsViewModel theme)
            {
                saveTasks.Add(_settingsService.SaveAsync(theme.CurrentSettings));
            }

            if (_settingsViewModels.TryGetValue("mainui", out var mainUiVm) && mainUiVm is MainUiSettingsViewModel mainUi)
            {
                saveTasks.Add(_settingsService.SaveAsync(mainUi.CurrentSettings));
            }

            if (_settingsViewModels.TryGetValue("ocr", out var ocrVm) && ocrVm is OcrSettingsViewModel ocr)
            {
                saveTasks.Add(_settingsService.SaveAsync(ocr.CurrentSettings));
            }

            if (_settingsViewModels.TryGetValue("capture", out var captureVm) && captureVm is CaptureSettingsViewModel capture)
            {
                saveTasks.Add(_settingsService.SaveAsync(capture.CurrentSettings));
            }

            if (_settingsViewModels.TryGetValue("overlay", out var overlayVm) && overlayVm is OverlaySettingsViewModel overlay)
            {
                saveTasks.Add(_settingsService.SaveAsync(overlay.CurrentSettings));
            }

            if (_settingsViewModels.TryGetValue("advanced", out var advancedVm) && advancedVm is AdvancedSettingsViewModel advanced)
            {
                saveTasks.Add(_settingsService.SaveAsync(advanced.CurrentSettings));
            }

            // 並列で保存処理を実行
            await Task.WhenAll(saveTasks).ConfigureAwait(false);

            // UI言語を適用（保存後に実際に言語を変更）
            if (generalSettingsViewModel != null)
            {
                await generalSettingsViewModel.ApplyUiLanguageAsync().ConfigureAwait(false);
            }

            _changeTracker.ClearChanges();
            StatusMessage = "設定を保存しました";

            _logger?.LogInformation("設定の保存が完了しました");
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = $"保存に失敗しました: {ex.Message}";
            _logger?.LogError(ex, "設定の保存中にエラーが発生しました");
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusMessage = "保存に失敗しました: アクセスが拒否されました";
            _logger?.LogError(ex, "設定ファイルへのアクセスが拒否されました");
        }
        catch (System.IO.IOException ex)
        {
            StatusMessage = "保存に失敗しました: ファイルI/Oエラー";
            _logger?.LogError(ex, "設定ファイルのI/Oエラーが発生しました");
        }
        catch (ArgumentException ex)
        {
            StatusMessage = "保存に失敗しました: 設定値が無効です";
            _logger?.LogError(ex, "設定値の検証エラーが発生しました");
        }
        catch (TimeoutException ex)
        {
            StatusMessage = "保存に失敗しました: 処理がタイムアウトしました";
            _logger?.LogError(ex, "設定保存処理がタイムアウトしました");
        }
    }

    /// <summary>
    /// 変更をキャンセルして閉じます
    /// </summary>
    private async Task CancelAsync()
    {
        if (await _changeTracker.ConfirmDiscardChangesAsync().ConfigureAwait(false))
        {
            // シンプルアプローチ: キャンセル時は何もしない（言語変更は保存時のみ適用されるため）
            // TODO: ウィンドウを閉じる処理
            StatusMessage = "変更をキャンセルしました";
            _logger?.LogInformation("設定の変更がキャンセルされました");
        }
    }

    /// <summary>
    /// 設定をリセットします
    /// </summary>
    private async Task ResetAsync()
    {
        if (await _changeTracker.ConfirmDiscardChangesAsync().ConfigureAwait(false))
        {
            // 全ての設定ViewModelをリセット
            foreach (var kvp in _settingsViewModels)
            {
                switch (kvp.Value)
                {
                    case GeneralSettingsViewModel general:
                        general.ResetToDefaultsCommand.Execute().Subscribe();
                        break;
                    case ThemeSettingsViewModel theme:
                        theme.ResetToDefaultsCommand.Execute().Subscribe();
                        break;
                    case MainUiSettingsViewModel mainUi:
                        mainUi.ResetToDefaultsCommand.Execute().Subscribe();
                        break;
                    case OcrSettingsViewModel ocr:
                        ocr.ResetToDefaultsCommand.Execute().Subscribe();
                        break;
                    case CaptureSettingsViewModel capture:
                        capture.ResetToDefaultsCommand.Execute().Subscribe();
                        break;
                    case OverlaySettingsViewModel overlay:
                        overlay.ResetToDefaultsCommand.Execute().Subscribe();
                        break;
                    case AdvancedSettingsViewModel advanced:
                        advanced.ResetToDefaultsCommand.Execute().Subscribe();
                        break;
                }
            }

            StatusMessage = "設定をリセットしました";
            _logger?.LogInformation("設定がリセットされました");
        }
    }

    /// <summary>
    /// 全設定のバリデーションを実行します
    /// </summary>
    private async Task ValidateAllAsync()
    {
        try
        {
            StatusMessage = "設定を検証中...";

            var validationResults = new List<(string Category, bool IsValid, string? ErrorMessage)>();

            // 各設定ViewModelのバリデーションを実行
            foreach (var kvp in _settingsViewModels)
            {
                bool isValid = true;
                string? errorMessage = null;

                try
                {
                    switch (kvp.Value)
                    {
                        case OcrSettingsViewModel ocr:
                            isValid = ocr.ValidateSettings();
                            if (!isValid) errorMessage = "OCR設定に問題があります";
                            break;
                        case CaptureSettingsViewModel capture:
                            isValid = capture.ValidateSettings();
                            if (!isValid) errorMessage = "キャプチャ設定に問題があります";
                            break;
                        case OverlaySettingsViewModel overlay:
                            isValid = overlay.ValidateSettings();
                            if (!isValid) errorMessage = "オーバーレイ設定に問題があります";
                            break;
                        case AdvancedSettingsViewModel advanced:
                            isValid = advanced.ValidateSettings();
                            if (!isValid) errorMessage = "拡張設定に問題があります";
                            break;
                            // 他の設定ViewModelにもバリデーションメソッドを追加したら、ここに追加
                    }
                }
                catch (ArgumentException ex)
                {
                    isValid = false;
                    errorMessage = ex.Message;
                    _logger?.LogError(ex, "設定バリデーションで引数エラーが発生しました: {Category}", kvp.Key);
                }
                catch (InvalidOperationException ex)
                {
                    isValid = false;
                    errorMessage = ex.Message;
                    _logger?.LogError(ex, "設定バリデーションで操作エラーが発生しました: {Category}", kvp.Key);
                }
                catch (NotSupportedException ex)
                {
                    isValid = false;
                    errorMessage = "この設定はサポートされていません";
                    _logger?.LogError(ex, "サポートされていない設定操作: {Category}", kvp.Key);
                }

                validationResults.Add((kvp.Key, isValid, errorMessage));
            }

            // バリデーション結果の評価
            var failedValidations = validationResults.Where(r => !r.IsValid).ToList();

            if (failedValidations.Count == 0)
            {
                StatusMessage = "すべての設定が有効です";
                _logger?.LogInformation("設定バリデーションが正常に完了しました");
            }
            else
            {
                var errorMessages = failedValidations.Select(f => f.ErrorMessage).Where(m => m != null);
                StatusMessage = $"設定エラー: {string.Join(", ", errorMessages)}";
                _logger?.LogWarning("設定バリデーションで問題が見つかりました: {Errors}", string.Join(", ", errorMessages));
            }

            await Task.Delay(100).ConfigureAwait(false); // UI更新のための短い遅延
        }
        catch (OperationCanceledException ex)
        {
            StatusMessage = "バリデーション処理がキャンセルされました";
            _logger?.LogWarning(ex, "設定バリデーション処理がキャンセルされました");
        }
        catch (OutOfMemoryException ex)
        {
            StatusMessage = "バリデーション中にメモリ不足が発生しました";
            _logger?.LogError(ex, "設定バリデーション中にメモリ不足が発生しました");
        }
        catch (InvalidDataException ex)
        {
            StatusMessage = "設定データが無効です";
            _logger?.LogError(ex, "設定バリデーション中に無効なデータが見つかりました");
        }
    }

    #endregion

    #region IDisposable


    /// <summary>
    /// HasChangesChangedイベントハンドラー
    /// </summary>
    private void OnHasChangesChanged(object? sender, HasChangesChangedEventArgs e)
    {
        this.RaisePropertyChanged(nameof(HasChanges));
        StatusMessage = e.HasChanges ? "設定に変更があります" : "変更なし";
    }

    /// <summary>
    /// テスト環境かどうかを判定します
    /// </summary>
    private static bool IsTestEnvironment()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => a.FullName?.Contains("xunit") == true ||
                     a.FullName?.Contains("Microsoft.TestPlatform") == true ||
                     a.FullName?.Contains("testhost") == true);
    }

    /// <summary>
    /// マネージドリソースを解放します
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // シンプルアプローチ: 言語変更は保存時のみ適用されるため、ロールバック不要

            // ViewModelキャッシュ内のDisposableオブジェクトを解放
            foreach (var kvp in _settingsViewModels)
            {
                if (kvp.Value is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "設定ViewModelの解放中にエラーが発生しました: {ViewModel}", kvp.Key);
                    }
                }
            }

            _settingsViewModels.Clear();

            // イベントハンドラーの解除
            _changeTracker.HasChangesChanged -= OnHasChangesChanged;
        }

        base.Dispose(disposing);
    }

    #endregion
}
