using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Styling;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Settings;
using Baketa.UI.Framework;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels.Settings;

/// <summary>
/// 一般設定画面のViewModel
/// アプリケーションの基本的な動作設定を管理
/// </summary>
public sealed class GeneralSettingsViewModel : Framework.ViewModelBase
{
    private readonly GeneralSettings _originalSettings;
    private readonly ILogger<GeneralSettingsViewModel>? _logger;

    // バッキングフィールド
    private bool _autoStartWithWindows;
    private bool _minimizeToTray;
    private bool _showExitConfirmation;
    private bool _allowUsageStatistics;
    private bool _checkForUpdatesAutomatically;
    private bool _performanceMode;
    private int _maxMemoryUsageMb;
    private LogLevel _logLevel;
    private int _logRetentionDays;
    private bool _enableDebugMode;
    private string? _activeGameProfile;
    private bool _showAdvancedSettings;
    private bool _hasChanges;

    // 翻訳設定用バッキングフィールド
    private bool _useLocalEngine = true;
    private string _sourceLanguage = "Japanese";
    private string _targetLanguage = "English";
    private int _fontSize = 14;

    // テーマ設定用バッキングフィールド
    private UiTheme _selectedTheme = UiTheme.Auto;

    /// <summary>
    /// GeneralSettingsViewModelを初期化します
    /// </summary>
    /// <param name="settings">一般設定データ</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー（オプション）</param>
    public GeneralSettingsViewModel(
        GeneralSettings settings,
        IEventAggregator eventAggregator,
        ILogger<GeneralSettingsViewModel>? logger = null) : base(eventAggregator)
    {
        _originalSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;

        // 初期化
        InitializeFromSettings(settings);

        // 変更追跡の設定
        SetupChangeTracking();

        // 選択肢の初期化
        LogLevelOptions = [.. Enum.GetValues<LogLevel>()];

        // 翻訳先言語リストの初期化
        UpdateAvailableTargetLanguages();

        // コマンドの初期化
        ResetToDefaultsCommand = ReactiveCommand.Create(ResetToDefaults);
        ToggleAdvancedSettingsCommand = ReactiveCommand.Create(ToggleAdvancedSettings);
        OpenLogFolderCommand = ReactiveCommand.Create(OpenLogFolder);
    }

    #region 基本設定プロパティ

    /// <summary>
    /// Windows起動時の自動開始
    /// </summary>
    public bool AutoStartWithWindows
    {
        get => _autoStartWithWindows;
        set => this.RaiseAndSetIfChanged(ref _autoStartWithWindows, value);
    }

    /// <summary>
    /// システムトレイに最小化
    /// </summary>
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => this.RaiseAndSetIfChanged(ref _minimizeToTray, value);
    }

    /// <summary>
    /// 終了時の確認ダイアログ表示
    /// </summary>
    public bool ShowExitConfirmation
    {
        get => _showExitConfirmation;
        set => this.RaiseAndSetIfChanged(ref _showExitConfirmation, value);
    }

    /// <summary>
    /// 使用統計情報の収集許可
    /// </summary>
    public bool AllowUsageStatistics
    {
        get => _allowUsageStatistics;
        set => this.RaiseAndSetIfChanged(ref _allowUsageStatistics, value);
    }

    /// <summary>
    /// 自動アップデート確認
    /// </summary>
    public bool CheckForUpdatesAutomatically
    {
        get => _checkForUpdatesAutomatically;
        set => this.RaiseAndSetIfChanged(ref _checkForUpdatesAutomatically, value);
    }

    #endregion

    #region 詳細設定プロパティ

    /// <summary>
    /// パフォーマンス優先モード
    /// </summary>
    public bool PerformanceMode
    {
        get => _performanceMode;
        set => this.RaiseAndSetIfChanged(ref _performanceMode, value);
    }

    /// <summary>
    /// 最大メモリ使用量（MB）
    /// </summary>
    public int MaxMemoryUsageMb
    {
        get => _maxMemoryUsageMb;
        set => this.RaiseAndSetIfChanged(ref _maxMemoryUsageMb, value);
    }

    /// <summary>
    /// ログレベル
    /// </summary>
    public LogLevel LogLevel
    {
        get => _logLevel;
        set => this.RaiseAndSetIfChanged(ref _logLevel, value);
    }

    /// <summary>
    /// ログファイルの保持日数
    /// </summary>
    public int LogRetentionDays
    {
        get => _logRetentionDays;
        set => this.RaiseAndSetIfChanged(ref _logRetentionDays, value);
    }

    #endregion

    #region デバッグ設定プロパティ

    /// <summary>
    /// デバッグモードの有効化
    /// </summary>
    public bool EnableDebugMode
    {
        get => _enableDebugMode;
        set => this.RaiseAndSetIfChanged(ref _enableDebugMode, value);
    }

    #endregion

    #region 翻訳設定プロパティ

    /// <summary>
    /// ローカル翻訳エンジンを使用するかどうか
    /// </summary>
    public bool UseLocalEngine
    {
        get => _useLocalEngine;
        set => this.RaiseAndSetIfChanged(ref _useLocalEngine, value);
    }

    /// <summary>
    /// AI翻訳が有効かどうか（αテストでは無効）
    /// </summary>
    public bool IsCloudTranslationEnabled => false;

    /// <summary>
    /// 翻訳元言語
    /// </summary>
    public string SourceLanguage
    {
        get => _sourceLanguage;
        set
        {
            this.RaiseAndSetIfChanged(ref _sourceLanguage, value);
            UpdateAvailableTargetLanguages();
        }
    }

    /// <summary>
    /// 翻訳先言語
    /// </summary>
    public string TargetLanguage
    {
        get => _targetLanguage;
        set => this.RaiseAndSetIfChanged(ref _targetLanguage, value);
    }

    /// <summary>
    /// フォントサイズ
    /// </summary>
    public int FontSize
    {
        get => _fontSize;
        set => this.RaiseAndSetIfChanged(ref _fontSize, value);
    }

    /// <summary>
    /// 利用可能な言語リスト
    /// </summary>
    public ObservableCollection<string> AvailableLanguages { get; } = ["Japanese", "English"];

    /// <summary>
    /// 利用可能な翻訳先言語リスト
    /// </summary>
    public ObservableCollection<string> AvailableTargetLanguages { get; } = [];

    /// <summary>
    /// フォントサイズの選択肢
    /// </summary>
    public ObservableCollection<int> FontSizeOptions { get; } = [10, 12, 14, 16, 18, 20, 24, 28, 32];

    /// <summary>
    /// 言語ペアが有効かどうか
    /// </summary>
    public bool IsLanguagePairValid => !string.Equals(SourceLanguage, TargetLanguage, StringComparison.OrdinalIgnoreCase);

    #endregion

    #region テーマ設定プロパティ

    /// <summary>
    /// 選択されたテーマ
    /// </summary>
    public UiTheme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            var oldValue = _selectedTheme;
            this.RaiseAndSetIfChanged(ref _selectedTheme, value);
            if (oldValue != value)
            {
                ApplyTheme(value);
            }
        }
    }

    /// <summary>
    /// 利用可能なテーマリスト
    /// </summary>
    public IReadOnlyList<UiTheme> AvailableThemes { get; } = [UiTheme.Light, UiTheme.Dark, UiTheme.Auto];

    /// <summary>
    /// テーマ表示名を取得
    /// </summary>
    public static string GetThemeDisplayName(UiTheme theme) => theme switch
    {
        UiTheme.Light => "ライト",
        UiTheme.Dark => "ダーク",
        UiTheme.Auto => "システム設定に従う",
        _ => theme.ToString()
    };

    #endregion

    #region UI制御プロパティ

    /// <summary>
    /// 詳細設定を表示するかどうか
    /// </summary>
    public bool ShowAdvancedSettings
    {
        get => _showAdvancedSettings;
        set => this.RaiseAndSetIfChanged(ref _showAdvancedSettings, value);
    }

    /// <summary>
    /// 設定に変更があるかどうか
    /// </summary>
    public bool HasChanges
    {
        get => _hasChanges;
        set => this.RaiseAndSetIfChanged(ref _hasChanges, value);
    }

    /// <summary>
    /// ログレベルの選択肢
    /// </summary>
    public IReadOnlyList<LogLevel> LogLevelOptions { get; }

    #endregion

    #region コマンド

    /// <summary>
    /// デフォルト値にリセットするコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ResetToDefaultsCommand { get; }

    /// <summary>
    /// 詳細設定表示切り替えコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ToggleAdvancedSettingsCommand { get; }

    /// <summary>
    /// ログフォルダを開くコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenLogFolderCommand { get; }

    #endregion

    #region メソッド

    /// <summary>
    /// 設定データから初期化
    /// </summary>
    private void InitializeFromSettings(GeneralSettings settings)
    {
        _autoStartWithWindows = settings.AutoStartWithWindows;
        _minimizeToTray = settings.MinimizeToTray;
        _showExitConfirmation = settings.ShowExitConfirmation;
        _allowUsageStatistics = settings.AllowUsageStatistics;
        _checkForUpdatesAutomatically = settings.CheckForUpdatesAutomatically;
        _performanceMode = settings.PerformanceMode;
        _maxMemoryUsageMb = settings.MaxMemoryUsageMb;
        _logLevel = settings.LogLevel;
        _logRetentionDays = settings.LogRetentionDays;
        _enableDebugMode = settings.EnableDebugMode;
        _activeGameProfile = settings.ActiveGameProfile;
    }

    /// <summary>
    /// 変更追跡を設定
    /// </summary>
    private void SetupChangeTracking()
    {
        // 主要プロパティの変更追跡
        this.WhenAnyValue(x => x.AutoStartWithWindows)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.MinimizeToTray)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.ShowExitConfirmation)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.AllowUsageStatistics)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.CheckForUpdatesAutomatically)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.PerformanceMode)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.MaxMemoryUsageMb)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.LogLevel)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.LogRetentionDays)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.EnableDebugMode)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        // 翻訳設定の変更追跡
        this.WhenAnyValue(x => x.UseLocalEngine)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.SourceLanguage)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.TargetLanguage)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        this.WhenAnyValue(x => x.FontSize)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);

        // テーマ設定の変更追跡
        this.WhenAnyValue(x => x.SelectedTheme)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => HasChanges = true);
    }

    /// <summary>
    /// デフォルト値にリセット
    /// </summary>
    private void ResetToDefaults()
    {
        var defaultSettings = new GeneralSettings();
        InitializeFromSettings(defaultSettings);
        HasChanges = true;
        _logger?.LogInformation("一般設定をデフォルト値にリセットしました");
    }

    /// <summary>
    /// 詳細設定表示を切り替え
    /// </summary>
    private void ToggleAdvancedSettings()
    {
        ShowAdvancedSettings = !ShowAdvancedSettings;
        _logger?.LogDebug("詳細設定表示を切り替えました: {ShowAdvanced}", ShowAdvancedSettings);
    }

    /// <summary>
    /// ログフォルダを開く
    /// </summary>
    private void OpenLogFolder()
    {
        // TODO: 実際のログフォルダパスを取得して開く
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Baketa", "Logs");

        try
        {
            if (Directory.Exists(logPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", logPath);
                _logger?.LogInformation("ログフォルダを開きました: {LogPath}", logPath);
            }
            else
            {
                _logger?.LogWarning("ログフォルダが見つかりません: {LogPath}", logPath);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogError(ex, "ログフォルダへのアクセスが拒否されました: {LogPath}", logPath);
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger?.LogError(ex, "ログフォルダが見つかりません: {LogPath}", logPath);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger?.LogError(ex, "ログフォルダを開くアプリケーションが見つかりません: {LogPath}", logPath);
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "無効なパスが指定されました: {LogPath}", logPath);
        }
    }

    /// <summary>
    /// テーマを適用します
    /// </summary>
    private void ApplyTheme(UiTheme theme)
    {
        try
        {
            var app = Avalonia.Application.Current;
            if (app == null)
            {
                _logger?.LogWarning("Application.Current が null のためテーマを適用できません");
                return;
            }

            app.RequestedThemeVariant = theme switch
            {
                UiTheme.Light => ThemeVariant.Light,
                UiTheme.Dark => ThemeVariant.Dark,
                UiTheme.Auto => ThemeVariant.Default,
                _ => ThemeVariant.Default
            };

            _logger?.LogInformation("テーマを変更しました: {Theme}", theme);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "テーマの適用に失敗しました: {Theme}", theme);
        }
    }

    /// <summary>
    /// 翻訳先言語リストを更新
    /// </summary>
    private void UpdateAvailableTargetLanguages()
    {
        AvailableTargetLanguages.Clear();

        // 翻訳元言語に基づいて翻訳先言語を設定
        if (SourceLanguage == "Japanese")
        {
            AvailableTargetLanguages.Add("English");
        }
        else if (SourceLanguage == "English")
        {
            AvailableTargetLanguages.Add("Japanese");
        }

        // 現在の翻訳先言語が選択肢にない場合は最初の選択肢を選択
        if (!AvailableTargetLanguages.Contains(TargetLanguage) && AvailableTargetLanguages.Count > 0)
        {
            TargetLanguage = AvailableTargetLanguages.First();
        }
    }

    /// <summary>
    /// 現在の設定データを取得
    /// </summary>
    public GeneralSettings CurrentSettings => new()
    {
        AutoStartWithWindows = AutoStartWithWindows,
        MinimizeToTray = MinimizeToTray,
        ShowExitConfirmation = ShowExitConfirmation,
        AllowUsageStatistics = AllowUsageStatistics,
        CheckForUpdatesAutomatically = CheckForUpdatesAutomatically,
        PerformanceMode = PerformanceMode,
        MaxMemoryUsageMb = MaxMemoryUsageMb,
        LogLevel = LogLevel,
        LogRetentionDays = LogRetentionDays,
        EnableDebugMode = EnableDebugMode,
        ActiveGameProfile = _activeGameProfile
    };

    /// <summary>
    /// 設定データを更新
    /// </summary>
    /// <param name="settings">新しい設定データ</param>
    public void UpdateSettings(GeneralSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        InitializeFromSettings(settings);
        HasChanges = false;
        _logger?.LogDebug("一般設定を更新しました");
    }

    #endregion
}
