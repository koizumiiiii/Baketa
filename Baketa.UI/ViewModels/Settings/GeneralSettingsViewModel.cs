using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Styling;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.License.Events;
using Baketa.Core.License.Models;
using Baketa.Core.Settings;
using Baketa.UI.Framework;
using Baketa.UI.Services;
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
    private readonly TranslationSettings _originalTranslationSettings;
    private readonly ILogger<GeneralSettingsViewModel>? _logger;
    private readonly ILocalizationService? _localizationService;
    private readonly ISettingsChangeTracker? _changeTracker;
    private readonly ILicenseManager? _licenseManager;
    private readonly IBonusTokenService? _bonusTokenService;
    private readonly IUnifiedSettingsService? _unifiedSettingsService;
    private readonly ITokenConsumptionTracker? _tokenTracker;

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

    // [Issue #78 Phase 5] Cloud AI使用量表示用バッキングフィールド
    private long _cloudTokensUsed;
    private long _cloudTokenLimit;
    private double _cloudUsagePercentage;

    // [Issue #280+#281] ボーナストークン残高
    private long _bonusTokensRemaining;

    // テーマ設定用バッキングフィールド
    private UiTheme _selectedTheme = UiTheme.Auto;

    // UI言語設定用バッキングフィールド
    private SupportedLanguage? _selectedUiLanguage;

    /// <summary>
    /// GeneralSettingsViewModelを初期化します
    /// </summary>
    /// <param name="settings">一般設定データ</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="localizationService">ローカライゼーションサービス（オプション）</param>
    /// <param name="changeTracker">設定変更追跡サービス（オプション）</param>
    /// <param name="logger">ロガー（オプション）</param>
    /// <param name="translationSettings">翻訳設定データ（オプション）</param>
    /// <param name="licenseManager">ライセンスマネージャー（オプション）</param>
    /// <param name="bonusTokenService">ボーナストークンサービス（オプション）</param>
    /// <param name="unifiedSettingsService">統一設定サービス（オプション）</param>
    /// <param name="tokenTracker">トークン消費トラッカー（オプション）</param>
    public GeneralSettingsViewModel(
        GeneralSettings settings,
        IEventAggregator eventAggregator,
        ILocalizationService? localizationService = null,
        ISettingsChangeTracker? changeTracker = null,
        ILogger<GeneralSettingsViewModel>? logger = null,
        TranslationSettings? translationSettings = null,
        ILicenseManager? licenseManager = null,
        IBonusTokenService? bonusTokenService = null,
        IUnifiedSettingsService? unifiedSettingsService = null,
        ITokenConsumptionTracker? tokenTracker = null) : base(eventAggregator)
    {
        _originalSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        _originalTranslationSettings = translationSettings ?? new TranslationSettings();
        _logger = logger;
        _localizationService = localizationService;
        _changeTracker = changeTracker;
        _licenseManager = licenseManager;
        _bonusTokenService = bonusTokenService;
        _unifiedSettingsService = unifiedSettingsService;
        _tokenTracker = tokenTracker;

        // [Issue #78 Phase 5] ライセンス状態変更時のUI更新を購読
        if (_licenseManager != null)
        {
            _licenseManager.StateChanged += OnLicenseStateChanged;
            Disposables.Add(Disposable.Create(() =>
            {
                _licenseManager.StateChanged -= OnLicenseStateChanged;
            }));
        }

        // [Issue #280+#281] ボーナストークン変更時のUI更新を購読
        // ボーナストークンのみで消費が完了した場合、StateChangedイベントは発火しないため
        if (_bonusTokenService != null)
        {
            _bonusTokenService.BonusTokensChanged += OnBonusTokensChanged;
            _bonusTokensRemaining = _bonusTokenService.GetTotalRemainingTokens();
            Disposables.Add(Disposable.Create(() =>
            {
                _bonusTokenService.BonusTokensChanged -= OnBonusTokensChanged;
            }));
        }

        // [Issue #243] 統一設定変更時のUI更新を購読
        if (_unifiedSettingsService != null)
        {
            _unifiedSettingsService.SettingsChanged += OnUnifiedSettingsChanged;
            Disposables.Add(Disposable.Create(() =>
            {
                _unifiedSettingsService.SettingsChanged -= OnUnifiedSettingsChanged;
            }));
        }

        // 初期化
        InitializeFromSettings(settings);
        InitializeFromTranslationSettings(_originalTranslationSettings);

        // [Issue #78 Phase 5] Cloud AI使用量の初期化
        InitializeCloudUsageFromLicenseState();

        // [Issue #275] TokenUsageRepositoryからトークン使用量を読み込み、LicenseManagerに同期
        _ = LoadTokenUsageFromRepositoryAsync();

        // 変更追跡の設定
        SetupChangeTracking();

        // 選択肢の初期化
        LogLevelOptions = [.. Enum.GetValues<LogLevel>()];

        // 翻訳先言語リストの初期化
        UpdateAvailableTargetLanguages();

        // UI言語の初期化
        InitializeUiLanguage();

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
    /// AI翻訳が有効かどうか（Pro/Premiaプランまたはボーナストークンで利用可能）
    /// </summary>
    /// <remarks>
    /// [Issue #280+#281] LicenseManager.IsFeatureAvailableでプラン・ボーナストークン両方をチェック
    /// </remarks>
    public bool IsCloudTranslationEnabled =>
        _licenseManager?.IsFeatureAvailable(FeatureType.CloudAiTranslation) ?? false;

    /// <summary>
    /// クラウド翻訳の注記テキスト（プランに応じて表示）
    /// Pro/Premia: 空文字、Free/Standard: "Pro/Premiaプランで利用可能"
    /// </summary>
    public string CloudTranslationNote =>
        IsCloudTranslationEnabled
            ? string.Empty
            : Resources.Strings.Settings_General_CloudTranslationUpgradeNote;

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

    #region [Issue #78 Phase 5] Cloud AI使用量表示プロパティ

    /// <summary>
    /// Cloud AI使用済みトークン数
    /// </summary>
    public long CloudTokensUsed
    {
        get => _cloudTokensUsed;
        private set => this.RaiseAndSetIfChanged(ref _cloudTokensUsed, value);
    }

    /// <summary>
    /// Cloud AIトークン上限
    /// </summary>
    public long CloudTokenLimit
    {
        get => _cloudTokenLimit;
        private set => this.RaiseAndSetIfChanged(ref _cloudTokenLimit, value);
    }

    /// <summary>
    /// Cloud AI使用率（0-100）
    /// </summary>
    public double CloudUsagePercentage
    {
        get => _cloudUsagePercentage;
        private set => this.RaiseAndSetIfChanged(ref _cloudUsagePercentage, value);
    }

    /// <summary>
    /// [Issue #296] 使用率に応じたゲージ色
    /// 0-79%: 青（デフォルト）、80-89%: 黄、90-99%: オレンジ、100%+: 赤
    /// </summary>
    public IBrush CloudUsageGaugeBrush => CloudUsagePercentage switch
    {
        >= 100 => new SolidColorBrush(Color.FromRgb(220, 53, 69)),   // Red (#DC3545)
        >= 90 => new SolidColorBrush(Color.FromRgb(253, 126, 20)),   // Orange (#FD7E14)
        >= 80 => new SolidColorBrush(Color.FromRgb(255, 193, 7)),    // Yellow (#FFC107)
        _ => new SolidColorBrush(Color.FromRgb(13, 110, 253))        // Blue (#0D6EFD)
    };

    /// <summary>
    /// Cloud AI使用量表示文字列
    /// [Issue #307] プラン+ボーナスの合計を表示
    /// 詳細な内訳はライセンスページで確認
    /// </summary>
    public string CloudUsageDisplay
    {
        get
        {
            var totalPool = CloudTokenLimit + _bonusTokensRemaining;
            if (totalPool > 0)
            {
                // 合計表示: "1,234,567 / 54,000,000"
                return $"{CloudTokensUsed:N0} / {totalPool:N0}";
            }
            else
            {
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// [Issue #307] トークンプール合計（プラン上限 + ボーナス残高）
    /// </summary>
    private long TotalTokenPool => CloudTokenLimit + _bonusTokensRemaining;

    /// <summary>
    /// [Issue #296] 有料プラン（Pro/Premium/Ultimate）またはボーナストークン保有者
    /// クォータ超過に関係なく判定
    /// [Issue #298] ボーナストークン保有者もゲージ表示対象に追加
    /// </summary>
    public bool HasPaidPlan
    {
        get
        {
            var plan = _licenseManager?.CurrentState?.CurrentPlan ?? PlanType.Free;
            var hasBonusTokens = (_bonusTokenService?.GetTotalRemainingTokens() ?? 0) > 0;
            return plan is PlanType.Pro or PlanType.Premium or PlanType.Ultimate || hasBonusTokens;
        }
    }

    /// <summary>
    /// Cloud AI使用量情報を表示するかどうか
    /// [Issue #296] クォータ超過時もゲージを表示するためHasPaidPlanを使用
    /// </summary>
    public bool ShowCloudUsageInfo => HasPaidPlan;

    #endregion

    #region テーマ設定プロパティ

    /// <summary>
    /// 選択されたテーマ
    /// テーマの実際の適用は保存時に行われます
    /// </summary>
    public UiTheme SelectedTheme
    {
        get => _selectedTheme;
        set => this.RaiseAndSetIfChanged(ref _selectedTheme, value);
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

    #region UI言語設定プロパティ

    /// <summary>
    /// 選択されたUI言語
    /// 実際の言語変更は保存時にのみ適用されます（ApplyUiLanguageAsync）
    /// </summary>
    public SupportedLanguage? SelectedUiLanguage
    {
        get => _selectedUiLanguage;
        set
        {
            _logger?.LogDebug("SelectedUiLanguage変更: {OldCode} → {NewCode}",
                _selectedUiLanguage?.Code ?? "(null)", value?.Code ?? "(null)");
            this.RaiseAndSetIfChanged(ref _selectedUiLanguage, value);
            // 注意: 即座に言語を変更しない（保存時にApplyUiLanguageAsyncで適用）
        }
    }

    /// <summary>
    /// 利用可能なUI言語リスト（日本語と英語のみ）
    /// 翻訳言語を増やすタイミングでUI言語も増やす予定
    /// </summary>
    public IReadOnlyList<SupportedLanguage> AvailableUiLanguages { get; } = new List<SupportedLanguage>
    {
        new("ja", "日本語", "Japanese"),
        new("en", "English", "English")
    }.AsReadOnly();

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
        _selectedTheme = settings.Theme;

        // UI言語設定の復元（AvailableUiLanguagesから検索）
        if (!string.IsNullOrEmpty(settings.UiLanguage))
        {
            _selectedUiLanguage = AvailableUiLanguages
                .FirstOrDefault(lang => lang.Code == settings.UiLanguage);
        }
    }

    /// <summary>
    /// 翻訳設定データから初期化
    /// </summary>
    private void InitializeFromTranslationSettings(TranslationSettings settings)
    {
        // 翻訳言語の復元（NLLB形式 ja/en から表示名に変換）
        _sourceLanguage = settings.DefaultSourceLanguage == "ja" ? "Japanese" : "English";
        _targetLanguage = settings.DefaultTargetLanguage == "ja" ? "Japanese" : "English";
        _fontSize = settings.OverlayFontSize;

        // [Issue #78 Phase 5] Cloud AI翻訳設定の復元
        // LicenseManager.IsFeatureAvailableでプラン・ボーナストークン両方をチェック
        var isCloudAvailable = _licenseManager?.IsFeatureAvailable(FeatureType.CloudAiTranslation) ?? false;

        // Cloud利用不可なら強制的にローカル翻訳
        _useLocalEngine = !isCloudAvailable || settings.UseLocalEngine;
    }

    /// <summary>
    /// 変更追跡を設定
    /// </summary>
    private void SetupChangeTracking()
    {
        const string categoryId = "General";

        // 主要プロパティの変更追跡
        this.WhenAnyValue(x => x.AutoStartWithWindows)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(AutoStartWithWindows)));

        this.WhenAnyValue(x => x.MinimizeToTray)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(MinimizeToTray)));

        this.WhenAnyValue(x => x.ShowExitConfirmation)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(ShowExitConfirmation)));

        this.WhenAnyValue(x => x.AllowUsageStatistics)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(AllowUsageStatistics)));

        this.WhenAnyValue(x => x.CheckForUpdatesAutomatically)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(CheckForUpdatesAutomatically)));

        this.WhenAnyValue(x => x.PerformanceMode)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(PerformanceMode)));

        this.WhenAnyValue(x => x.MaxMemoryUsageMb)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(MaxMemoryUsageMb)));

        this.WhenAnyValue(x => x.LogLevel)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(LogLevel)));

        this.WhenAnyValue(x => x.LogRetentionDays)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(LogRetentionDays)));

        this.WhenAnyValue(x => x.EnableDebugMode)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(EnableDebugMode)));

        // 翻訳設定の変更追跡
        this.WhenAnyValue(x => x.UseLocalEngine)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(UseLocalEngine)));

        this.WhenAnyValue(x => x.SourceLanguage)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(SourceLanguage)));

        this.WhenAnyValue(x => x.TargetLanguage)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(TargetLanguage)));

        this.WhenAnyValue(x => x.FontSize)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(FontSize)));

        // テーマ設定の変更追跡
        this.WhenAnyValue(x => x.SelectedTheme)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(SelectedTheme)));

        // UI言語設定の変更追跡
        this.WhenAnyValue(x => x.SelectedUiLanguage)
            .Skip(1).DistinctUntilChanged()
            .Subscribe(_ => TrackPropertyChange(categoryId, nameof(SelectedUiLanguage)));
    }

    /// <summary>
    /// プロパティ変更を追跡
    /// </summary>
    private void TrackPropertyChange(string categoryId, string propertyName)
    {
        HasChanges = true;
        _changeTracker?.TrackChange(categoryId, propertyName, null, "changed");
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
    /// 現在選択されているテーマを適用します（保存時に呼び出される）
    /// </summary>
    public void ApplySelectedTheme()
    {
        ApplyTheme(SelectedTheme);
    }

    /// <summary>
    /// テーマを適用します
    /// </summary>
    private void ApplyTheme(UiTheme theme)
    {
        // UIスレッドで実行する必要がある
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyTheme(theme));
            return;
        }

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
    /// UI言語の初期化
    /// </summary>
    private void InitializeUiLanguage()
    {
        if (_localizationService == null)
        {
            // サービスがない場合はデフォルトで日本語
            if (_selectedUiLanguage == null)
            {
                _selectedUiLanguage = AvailableUiLanguages.First();
            }
            return;
        }

        // [Issue #261] 常にランタイムのカルチャを優先
        // 同意ダイアログで言語変更された場合、設定ファイルより現在のカルチャが正しい
        var currentCultureCode = _localizationService.CurrentCulture.TwoLetterISOLanguageName;
        var runtimeLanguage = AvailableUiLanguages
            .FirstOrDefault(l => l.Code == currentCultureCode || l.Code == _localizationService.CurrentCulture.Name)
            ?? AvailableUiLanguages.First();

        // 設定とランタイムが異なる場合はランタイムを優先
        if (_selectedUiLanguage == null || _selectedUiLanguage.Code != runtimeLanguage.Code)
        {
            _logger?.LogDebug("[Issue #261] UI言語をランタイムカルチャに同期: {SavedCode} → {RuntimeCode}",
                _selectedUiLanguage?.Code ?? "(null)", runtimeLanguage.Code);
            _selectedUiLanguage = runtimeLanguage;
        }
    }

    /// <summary>
    /// UI言語を適用します（保存時に呼び出される）
    /// </summary>
    /// <returns>成功した場合はtrue</returns>
    public async Task<bool> ApplyUiLanguageAsync()
    {
        var cultureCode = SelectedUiLanguage?.Code;
        if (string.IsNullOrEmpty(cultureCode))
        {
            _logger?.LogDebug("ApplyUiLanguageAsync: UI言語が選択されていません");
            return false;
        }

        if (_localizationService == null)
        {
            _logger?.LogWarning("LocalizationService is not available, cannot change UI language");
            return false;
        }

        try
        {
            _logger?.LogDebug("UI言語を適用: {CultureCode}", cultureCode);
            var success = await _localizationService.ChangeLanguageAsync(cultureCode);
            if (success)
            {
                _logger?.LogInformation("UI language changed to: {CultureCode}", cultureCode);
            }
            return success;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error applying UI language: {CultureCode}", cultureCode);
            return false;
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
        ActiveGameProfile = _activeGameProfile,
        UiLanguage = SelectedUiLanguage?.Code,
        Theme = SelectedTheme
    };

    /// <summary>
    /// 現在の翻訳設定データを取得
    /// 元の設定をクローンして変更分を適用
    /// </summary>
    public TranslationSettings CurrentTranslationSettings
    {
        get
        {
            var settings = _originalTranslationSettings.Clone();
            // 表示名からNLLB形式に変換
            settings.DefaultSourceLanguage = SourceLanguage == "Japanese" ? "ja" : "en";
            settings.DefaultTargetLanguage = TargetLanguage == "Japanese" ? "ja" : "en";
            settings.OverlayFontSize = FontSize;
            // [Issue #78 Phase 5] + [Issue #280+#281] Cloud AI翻訳設定を反映
            // UseLocalEngine=true(ローカル翻訳) → EnableCloudAiTranslation=false
            // UseLocalEngine=false(AI翻訳) → EnableCloudAiTranslation=true
            settings.EnableCloudAiTranslation = !UseLocalEngine;
            settings.UseLocalEngine = UseLocalEngine;
            return settings;
        }
    }

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

    /// <summary>
    /// [Issue #78 Phase 5] ライセンス状態変更時のハンドラ
    /// プランが変更された場合、Cloud AI翻訳関連のプロパティを更新
    /// </summary>
    private void OnLicenseStateChanged(object? sender, LicenseStateChangedEventArgs e)
    {
        _logger?.LogDebug("ライセンス状態変更を検出: {OldPlan} → {NewPlan}, Tokens: {Used}/{Limit}",
            e.OldState.CurrentPlan, e.NewState.CurrentPlan,
            e.NewState.CloudAiTokensUsed, e.NewState.MonthlyTokenLimit);

        // UIスレッドでプロパティ変更通知を発行
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            // プランが変更された場合
            if (e.OldState.CurrentPlan != e.NewState.CurrentPlan)
            {
                this.RaisePropertyChanged(nameof(IsCloudTranslationEnabled));
                this.RaisePropertyChanged(nameof(CloudTranslationNote));
                this.RaisePropertyChanged(nameof(HasPaidPlan));
                this.RaisePropertyChanged(nameof(ShowCloudUsageInfo));

                // Cloud AI翻訳が利用可能になった/なくなった場合、UseLocalEngineも更新
                var isCloudAvailable = _licenseManager?.IsFeatureAvailable(FeatureType.CloudAiTranslation) ?? false;

                if (!isCloudAvailable && !UseLocalEngine)
                {
                    // Cloud AIが使えなくなった場合はローカル翻訳に切り替え
                    _logger?.LogInformation("[Issue #243] Cloud AIが利用不可になったためローカル翻訳に切り替え");
                    UseLocalEngine = true;
                }
                else if (isCloudAvailable && UseLocalEngine)
                {
                    // [Issue #243] Cloud AIが使えるようになった場合、設定に基づいてCloud AIに切り替え
                    var currentSettings = _unifiedSettingsService?.GetTranslationSettings();
                    if (currentSettings?.EnableCloudAiTranslation == true)
                    {
                        _logger?.LogInformation("[Issue #243] Cloud AIが利用可能になったためCloud AI翻訳に切り替え");
                        UseLocalEngine = false;
                    }
                }
            }

            // 使用量の更新（プラン変更とトークン消費の両方で更新）
            UpdateCloudUsageFromState(e.NewState);
        });
    }

    /// <summary>
    /// [Issue #78 Phase 5] Cloud AI使用量をライセンス状態から初期化
    /// </summary>
    private void InitializeCloudUsageFromLicenseState()
    {
        if (_licenseManager == null)
        {
            return;
        }

        var state = _licenseManager.CurrentState;
        UpdateCloudUsageFromState(state);
    }

    /// <summary>
    /// [Issue #275] TokenUsageRepositoryからトークン使用量を読み込み、LicenseManagerに同期
    /// </summary>
    private async Task LoadTokenUsageFromRepositoryAsync()
    {
        if (_tokenTracker == null || _licenseManager == null)
        {
            return;
        }

        try
        {
            var usage = await _tokenTracker.GetMonthlyUsageAsync().ConfigureAwait(false);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                // リポジトリから取得した値がLicenseStateより大きい場合は更新
                var currentValue = CloudTokensUsed;
                var repositoryValue = usage.TotalTokensUsed;

                if (repositoryValue > currentValue)
                {
                    CloudTokensUsed = repositoryValue;
                    // LicenseManagerの内部状態も同期
                    _licenseManager.SyncTokenUsage(repositoryValue);
                    _logger?.LogDebug(
                        "[Issue #275] GeneralSettings: トークン使用量をリポジトリから更新: {Current} → {Repository}",
                        currentValue, repositoryValue);
                }
                else if (currentValue > repositoryValue && currentValue > 0)
                {
                    // 現在の値を維持し、LicenseManagerにも同期
                    _licenseManager.SyncTokenUsage(currentValue);
                }

                // TokenLimitも大きい方を採用
                var currentLimit = CloudTokenLimit;
                var repositoryLimit = usage.MonthlyLimit;
                if (repositoryLimit > currentLimit)
                {
                    CloudTokenLimit = repositoryLimit;
                }

                // [Issue #307] プラン+ボーナスの合計でパーセンテージを計算
                CloudUsagePercentage = TotalTokenPool > 0
                    ? (double)CloudTokensUsed / TotalTokenPool * 100
                    : 0;

                this.RaisePropertyChanged(nameof(CloudUsageDisplay));
                this.RaisePropertyChanged(nameof(CloudUsageGaugeBrush));
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[Issue #275] GeneralSettings: トークン使用量の読み込みに失敗しました");
        }
    }

    /// <summary>
    /// [Issue #78 Phase 5] ライセンス状態からCloud AI使用量プロパティを更新
    /// </summary>
    private void UpdateCloudUsageFromState(Core.License.Models.LicenseState state)
    {
        CloudTokensUsed = state.CloudAiTokensUsed;
        CloudTokenLimit = state.MonthlyTokenLimit;
        // [Issue #307] プラン+ボーナスの合計でパーセンテージを計算
        CloudUsagePercentage = TotalTokenPool > 0
            ? (double)CloudTokensUsed / TotalTokenPool * 100
            : 0;

        // 派生プロパティの更新通知
        this.RaisePropertyChanged(nameof(CloudUsageDisplay));
        this.RaisePropertyChanged(nameof(CloudUsageGaugeBrush));
    }

    /// <summary>
    /// [Issue #243] 統一設定変更時のハンドラ
    /// 外部から翻訳設定が変更された場合にUIを更新
    /// </summary>
    private void OnUnifiedSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        // 翻訳設定の変更のみ処理
        if (e.SettingsType != SettingsType.Translation)
        {
            return;
        }

        _logger?.LogDebug("[Issue #243] 統一設定変更を検出: Section={Section}", e.SectionName);

        // UIスレッドでプロパティ更新
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_unifiedSettingsService == null)
            {
                return;
            }

            var newSettings = _unifiedSettingsService.GetTranslationSettings();

            // Cloud AI翻訳設定の変更を反映
            // EnableCloudAiTranslation=true → UseLocalEngine=false（AI翻訳）
            // EnableCloudAiTranslation=false → UseLocalEngine=true（ローカル翻訳）
            var newUseLocalEngine = !newSettings.EnableCloudAiTranslation;
            if (UseLocalEngine != newUseLocalEngine)
            {
                _logger?.LogInformation(
                    "[Issue #243] 翻訳エンジン設定を外部変更から更新: UseLocalEngine={Old} → {New}",
                    UseLocalEngine,
                    newUseLocalEngine);

                UseLocalEngine = newUseLocalEngine;
            }
        });
    }

    /// <summary>
    /// [Issue #280+#281] ボーナストークン変更時のハンドラ
    /// ボーナストークンが消費された場合にCloud AI利用可能状態を更新
    /// </summary>
    private void OnBonusTokensChanged(object? sender, BonusTokensChangedEventArgs e)
    {
        _logger?.LogDebug(
            "[Issue #280+#281] ボーナストークン変更を検出: TotalRemaining={TotalRemaining}, Reason={Reason}",
            e.TotalRemaining, e.Reason);

        // UIスレッドでプロパティ変更通知を発行
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            // ボーナストークン残高を更新
            _bonusTokensRemaining = e.TotalRemaining;

            // [Issue #307] プラン+ボーナスの合計でパーセンテージを再計算
            CloudUsagePercentage = TotalTokenPool > 0
                ? (double)CloudTokensUsed / TotalTokenPool * 100
                : 0;

            // Cloud AI利用可否が変わった可能性があるため更新
            this.RaisePropertyChanged(nameof(IsCloudTranslationEnabled));
            this.RaisePropertyChanged(nameof(CloudTranslationNote));
            this.RaisePropertyChanged(nameof(ShowCloudUsageInfo));
            this.RaisePropertyChanged(nameof(CloudUsageDisplay));
            this.RaisePropertyChanged(nameof(CloudUsageGaugeBrush));

            // ボーナストークンが0になった場合、Cloud AIが使えなくなる可能性
            // LicenseManager.IsFeatureAvailableでプラン・ボーナストークン両方をチェック
            var isCloudAvailable = _licenseManager?.IsFeatureAvailable(FeatureType.CloudAiTranslation) ?? false;

            if (!isCloudAvailable && !UseLocalEngine)
            {
                // Cloud AIが使えなくなった場合はローカル翻訳に切り替え
                _logger?.LogInformation("[Issue #280+#281] ボーナストークン枯渇によりローカル翻訳に切り替え");
                UseLocalEngine = true;
            }
        });
    }

    #endregion
}
