using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using Baketa.Core.Events;
using Baketa.UI.Framework;
using Baketa.UI.Framework.ReactiveUI;
using Baketa.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;
using DynamicData;

// 名前空間エイリアスを使用して衝突を解決
using CoreEvents = Baketa.Core.Events;
// using UIEvents = Baketa.UI.Framework.Events; // 古いEventsを削除

// IDE0028の警告を抑制
#pragma warning disable IDE0028 // コレクション初期化を簡細化できます

// CA1515の警告を抑制
#pragma warning disable CA1515 // クラスライブラリと異なり、アプリケーションのAPIは通常公開参照されないため、型を内部としてマークできます

namespace Baketa.UI.ViewModels;

/// <summary>
/// 設定画面のビューモデル
/// </summary>
public sealed class SettingsViewModel : Framework.ViewModelBase
{
    // LoggerMessageデリゲート
    private static readonly Action<ILogger, string, Exception?> _logSettingsOperationError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(_logSettingsOperationError)),
            "設定の保存中に操作エラーが発生しました: {Message}");

    private static readonly Action<ILogger, string, Exception?> _logSettingsArgumentError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2, nameof(_logSettingsArgumentError)),
            "設定の保存中に引数エラーが発生しました: {Message}");

    private static readonly Action<ILogger, string, Exception?> _logSettingsFileError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(3, nameof(_logSettingsFileError)),
            "設定ファイルの操作中にエラーが発生しました: {Message}");

    // サービス依存関係
    private readonly ITranslationEngineStatusService? _statusService;
    
    // 状態監視関連のフィールド
    private IDisposable? _statusUpdateSubscription;

    // 設定カテゴリ
    public enum SettingCategory
    {
        General,
        Appearance,
        Language,
        LanguagePairs,
        TranslationEngine,
        Hotkeys,
        Advanced,
        Accessibility
    }

    // 選択中の設定カテゴリ
    private SettingCategory _selectedCategory = SettingCategory.General;
    public SettingCategory SelectedCategory
    {
        get => _selectedCategory;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _selectedCategory, value);
    }

    // テーマ設定
    private bool _isDarkTheme = true;
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set 
        { 
            ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _isDarkTheme, value);
            OnThemeChanged(value);
        }
    }

    // UIスケール
    private double _uiScale = 1.0;
    public double UIScale
    {
        get => _uiScale;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _uiScale, value);
    }

    // UIスケールの選択肢
    public ObservableCollection<double> UIScaleOptions { get; } = new() { 0.8, 0.9, 1.0, 1.1, 1.2, 1.5 };

    // UI言語
    private string _uiLanguage = "日本語";
    public string UILanguage
    {
        get => _uiLanguage;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _uiLanguage, value);
    }

    // UI言語の選択肢
    public ObservableCollection<string> UILanguageOptions { get; } = new() { "日本語", "English", "简体中文" };

    // OCR言語
    private string _ocrLanguage = "日本語";
    public string OCRLanguage
    {
        get => _ocrLanguage;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _ocrLanguage, value);
    }

    // OCR言語の選択肢
    public ObservableCollection<string> OCRLanguageOptions { get; } = new() { "日本語", "English", "简体中文", "繁體中文", "한국어" };

    // 翻訳言語
    private string _translationLanguage = "英語";
    public string TranslationLanguage
    {
        get => _translationLanguage;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _translationLanguage, value);
    }

    // 翻訳言語の選択肢
    public ObservableCollection<string> TranslationLanguageOptions { get; } = new() { "日本語", "英語", "簡体字中国語", "繁体字中国語", "韓国語" };

    // ==== 翻訳エンジン設定 ====
    
    // 選択された翻訳エンジン
    private string _selectedTranslationEngine = "LocalOnly";
    public string SelectedTranslationEngine
    {
        get => _selectedTranslationEngine;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _selectedTranslationEngine, value);
    }

    // 翻訳エンジンの選択肢
    public ObservableCollection<string> TranslationEngineOptions { get; } = new() { "LocalOnly", "CloudOnly" };

    // LocalOnly選択状態
    public bool IsLocalOnlySelected
    {
        get => SelectedTranslationEngine == "LocalOnly";
        set
        {
            if (value) SelectedTranslationEngine = "LocalOnly";
        }
    }

    // CloudOnly選択状態
    public bool IsCloudOnlySelected
    {
        get => SelectedTranslationEngine == "CloudOnly";
        set
        {
            if (value) SelectedTranslationEngine = "CloudOnly";
        }
    }

    // フォールバック設定
    private bool _enableRateLimitFallback = true;
    public bool EnableRateLimitFallback
    {
        get => _enableRateLimitFallback;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _enableRateLimitFallback, value);
    }

    private bool _enableNetworkErrorFallback = true;
    public bool EnableNetworkErrorFallback
    {
        get => _enableNetworkErrorFallback;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _enableNetworkErrorFallback, value);
    }

    private bool _enableApiErrorFallback = true;
    public bool EnableApiErrorFallback
    {
        get => _enableApiErrorFallback;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _enableApiErrorFallback, value);
    }

    private bool _showFallbackNotifications = true;
    public bool ShowFallbackNotifications
    {
        get => _showFallbackNotifications;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _showFallbackNotifications, value);
    }

    private int _fallbackTimeoutSeconds = 10;
    public int FallbackTimeoutSeconds
    {
        get => _fallbackTimeoutSeconds;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _fallbackTimeoutSeconds, value);
    }

    private int _recoveryCheckIntervalMinutes = 5;
    public int RecoveryCheckIntervalMinutes
    {
        get => _recoveryCheckIntervalMinutes;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _recoveryCheckIntervalMinutes, value);
    }

    // エンジンの説明テキスト
    public string SelectedEngineDescription
    {
        get
        {
            return SelectedTranslationEngine switch
            {
                "LocalOnly" =>
                    "OPUS-MT専用エンジン\n" +
                    "✅ 高速処理（50ms以下）\n" +
                    "✅ 完全無料\n" +
                    "✅ オフライン対応\n" +
                    "📝 適用: 短いテキスト、一般的な翻訳\n" +
                    "🎯 品質: 標準品質",
                "CloudOnly" =>
                    "Gemini API専用エンジン\n" +
                    "✅ 高品質翻訳\n" +
                    "✅ 専門用語対応\n" +
                    "✅ 文脈理解\n" +
                    "💰 課金制\n" +
                    "🌐 ネットワーク必須\n" +
                    "📝 適用: 複雑なテキスト、専門分野\n" +
                    "🎯 品質: 高品質",
                _ => "不明なエンジン"
            };
        }
    }

    // コスト情報
    public string EstimatedCostInfo
    {
        get
        {
            return SelectedTranslationEngine switch
            {
                "LocalOnly" => "📊 コスト: 無料（モデルダウンロード時のみ通信）",
                "CloudOnly" => "📊 コスト: 約 $0.01-0.05 / 1000文字（文字数により変動）",
                _ => ""
            };
        }
    }

    // キャプチャホットキー
    private string _captureHotkey = "Ctrl+Alt+C";
    public string CaptureHotkey
    {
        get => _captureHotkey;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _captureHotkey, value);
    }

    // 翻訳ホットキー
    private string _translateHotkey = "Ctrl+Alt+T";
    public string TranslateHotkey
    {
        get => _translateHotkey;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _translateHotkey, value);
    }

    // リセットホットキー
    private string _resetHotkey = "Ctrl+Alt+R";
    public string ResetHotkey
    {
        get => _resetHotkey;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _resetHotkey, value);
    }

    // 自動スタートアップ
    private bool _startWithWindows;
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _startWithWindows, value);
    }

    // 最小化で最小化
    private bool _minimizeToTray = true;
    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _minimizeToTray, value);
    }

    // デバッグログ有効化
    private bool _enableDebugLogs;
    public bool EnableDebugLogs
    {
        get => _enableDebugLogs;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _enableDebugLogs, value);
    }

    // 設定変更されたフラグ
    private bool _hasChanges;
    public bool HasChanges
    {
        get => _hasChanges;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _hasChanges, value);
    }
    
    // アクセシビリティ設定ビューモデル
    public AccessibilitySettingsViewModel AccessibilityViewModel { get; }
    
    // 言語ペア設定ビューモデル
    public LanguagePairsViewModel LanguagePairsViewModel { get; }
    
    // ==== エンジン状態監視関連 ====
    
    /// <summary>
    /// LocalOnlyエンジンの状態
    /// </summary>
    public TranslationEngineStatus? LocalEngineStatus => _statusService?.LocalEngineStatus;
    
    /// <summary>
    /// CloudOnlyエンジンの状態
    /// </summary>
    public TranslationEngineStatus? CloudEngineStatus => _statusService?.CloudEngineStatus;
    
    /// <summary>
    /// ネットワーク接続状態
    /// </summary>
    public NetworkConnectionStatus? NetworkStatus => _statusService?.NetworkStatus;
    
    /// <summary>
    /// 最後のフォールバック情報
    /// </summary>
    public FallbackInfo? LastFallbackInfo => _statusService?.LastFallback;
    
    // 状態監視機能有効化
    private bool _isStatusMonitoringEnabled;
    public bool IsStatusMonitoringEnabled
    {
        get => _isStatusMonitoringEnabled;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _isStatusMonitoringEnabled, value);
    }
    
    // リアルタイム状態表示用プロパティ
    
    /// <summary>
    /// LocalOnlyエンジンの状態テキスト
    /// </summary>
    public string LocalEngineStatusText
    {
        get
        {
            if (LocalEngineStatus == null) return "状態不明";
            
            return LocalEngineStatus.OverallStatus switch
            {
                EngineHealthStatus.Healthy => "✅ 正常動作中",
                EngineHealthStatus.Warning => "⚠️ 警告",
                EngineHealthStatus.Error => "❌ エラー",
                EngineHealthStatus.Offline => "🔴 オフライン",
                _ => "状態不明"
            };
        }
    }
    
    /// <summary>
    /// CloudOnlyエンジンの状態テキスト
    /// </summary>
    public string CloudEngineStatusText
    {
        get
        {
            if (CloudEngineStatus == null) return "状態不明";
            
            var statusText = CloudEngineStatus.OverallStatus switch
            {
                EngineHealthStatus.Healthy => "✅ 正常動作中",
                EngineHealthStatus.Warning => "⚠️ 警告",
                EngineHealthStatus.Error => "❌ エラー",
                EngineHealthStatus.Offline => "🔴 オフライン",
                _ => "状態不明"
            };
            
            // レート制限情報を追加
            if (CloudEngineStatus.IsOnline && CloudEngineStatus.RemainingRequests >= 0)
            {
                statusText += $" (残り: {CloudEngineStatus.RemainingRequests}回)";
            }
            
            return statusText;
        }
    }
    
    /// <summary>
    /// ネットワーク状態テキスト
    /// </summary>
    public string NetworkStatusText
    {
        get
        {
            if (NetworkStatus == null) return "状態不明";
            
            if (!NetworkStatus.IsConnected)
            {
                return "🔴 オフライン";
            }
            
            var latencyText = NetworkStatus.LatencyMs > 0 ? $" ({NetworkStatus.LatencyMs}ms)" : "";
            return $"✅ 接続中{latencyText}";
        }
    }
    
    /// <summary>
    /// 最後のフォールバック情報テキスト
    /// </summary>
    public string LastFallbackText
    {
        get
        {
            if (LastFallbackInfo == null) return "フォールバックなし";
            
            var timeAgo = DateTime.Now - LastFallbackInfo.OccurredAt;
            var timeText = timeAgo.TotalMinutes < 1 ? "つい先ほど" :
                          timeAgo.TotalHours < 1 ? $"{(int)timeAgo.TotalMinutes}分前" :
                          $"{(int)timeAgo.TotalHours}時間前";
            
            return $"{LastFallbackInfo.FromEngine}→{LastFallbackInfo.ToEngine} ({timeText})";
        }
    }

    // コマンド
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetToDefaultsCommand { get; }
    public ReactiveCommand<SettingCategory, Unit> SelectCategoryCommand { get; }
    public ReactiveCommand<Unit, Unit> StartStatusMonitoringCommand { get; }
    public ReactiveCommand<Unit, Unit> StopStatusMonitoringCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshStatusCommand { get; }

    /// <summary>
    /// 新しいSettingsViewModelを初期化します
    /// </summary>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="accessibilityViewModel">アクセシビリティ設定ビューモデル</param>
    /// <param name="languagePairsViewModel">言語ペア設定ビューモデル</param>
    /// <param name="statusService">翻訳エンジン状態監視サービス</param>
    /// <param name="logger">ロガー</param>
    public SettingsViewModel(
        Baketa.Core.Abstractions.Events.IEventAggregator eventAggregator, 
        AccessibilitySettingsViewModel accessibilityViewModel,
        LanguagePairsViewModel languagePairsViewModel,
        ITranslationEngineStatusService? statusService = null,
        ILogger? logger = null)
        : base(eventAggregator, logger)
    {
        AccessibilityViewModel = accessibilityViewModel 
            ?? throw new ArgumentNullException(nameof(accessibilityViewModel));
        
        LanguagePairsViewModel = languagePairsViewModel
            ?? throw new ArgumentNullException(nameof(languagePairsViewModel));
        
        _statusService = statusService;
        
        // コマンドの初期化
        SaveCommand = Baketa.UI.Framework.CommandHelper.CreateCommand(ExecuteSaveAsync);
        CancelCommand = Baketa.UI.Framework.CommandHelper.CreateCommand(ExecuteCancelAsync);
        ResetToDefaultsCommand = Baketa.UI.Framework.CommandHelper.CreateCommand(ExecuteResetToDefaultsAsync);
        SelectCategoryCommand = ReactiveCommand.Create<SettingCategory>(ExecuteSelectCategory);
        StartStatusMonitoringCommand = Baketa.UI.Framework.CommandHelper.CreateCommand(ExecuteStartStatusMonitoringAsync);
        StopStatusMonitoringCommand = Baketa.UI.Framework.CommandHelper.CreateCommand(ExecuteStopStatusMonitoringAsync);
        RefreshStatusCommand = Baketa.UI.Framework.CommandHelper.CreateCommand(ExecuteRefreshStatusAsync);

        // プロパティの変更を監視して設定変更フラグを設定
        // ReactiveUIのWhenAnyValueの制限により、監視を複数に分割
        
        // 基本設定の監視
        this.WhenAnyValue(
            x => x.IsDarkTheme,
            x => x.UIScale,
            x => x.UILanguage,
            x => x.OCRLanguage,
            x => x.TranslationLanguage,
            x => x.SelectedTranslationEngine,
            x => x.EnableRateLimitFallback,
            x => x.EnableNetworkErrorFallback,
            (darkTheme, uiScale, uiLang, ocrLang, transLang, transEngine, rateLimitFallback, networkErrorFallback) => true
        ).Subscribe(_ => HasChanges = true);
        
        // フォールバック設定の監視
        this.WhenAnyValue(
            x => x.EnableApiErrorFallback,
            x => x.ShowFallbackNotifications,
            x => x.FallbackTimeoutSeconds,
            x => x.RecoveryCheckIntervalMinutes,
            x => x.CaptureHotkey,
            x => x.TranslateHotkey,
            x => x.ResetHotkey,
            x => x.StartWithWindows,
            (apiErrorFallback, showFallbackNotifications, fallbackTimeout, recoveryInterval, captureHotkey, translateHotkey, resetHotkey, startWithWindows) => true
        ).Subscribe(_ => HasChanges = true);
        
        // その他の設定の監視
        this.WhenAnyValue(
            x => x.MinimizeToTray,
            x => x.EnableDebugLogs,
            (minimizeToTray, enableDebugLogs) => true
        ).Subscribe(_ => HasChanges = true);
        
        // 翻訳エンジン選択変更時の監視
        this.WhenAnyValue(x => x.SelectedTranslationEngine)
            .Subscribe(_ => 
            {
                this.RaisePropertyChanged(nameof(IsLocalOnlySelected));
                this.RaisePropertyChanged(nameof(IsCloudOnlySelected));
                this.RaisePropertyChanged(nameof(SelectedEngineDescription));
                this.RaisePropertyChanged(nameof(EstimatedCostInfo));
            });
        
        // アクセシビリティ設定開始イベントの購読
        SubscribeToEvent<CoreEvents.AccessibilityEvents.OpenAccessibilitySettingsRequestedEvent>(async _ =>
        {
            SelectedCategory = SettingCategory.Accessibility;
            await Task.CompletedTask.ConfigureAwait(false);
        });
        
        // 状態監視サービスの初期化と購読
        InitializeStatusMonitoring();
    }
    
    /// <summary>
    /// 状態監視の初期化
    /// </summary>
    private void InitializeStatusMonitoring()
    {
        if (_statusService == null)
        {
            return;
        }
        
        // 状態更新イベントの購読
        _statusUpdateSubscription = _statusService.StatusUpdates
            .Subscribe(OnStatusUpdate);
        
        // 状態オブジェクトのプロパティ変更を監視
        _statusService.LocalEngineStatus?.WhenAnyValue(
                x => x.IsOnline,
                x => x.IsHealthy,
                x => x.RemainingRequests,
                x => x.LastError)
                .Subscribe(_ => 
                {
                    this.RaisePropertyChanged(nameof(LocalEngineStatusText));
                    this.RaisePropertyChanged(nameof(LocalEngineStatus));
                });
        
        _statusService.CloudEngineStatus?.WhenAnyValue(
                x => x.IsOnline,
                x => x.IsHealthy,
                x => x.RemainingRequests,
                x => x.LastError)
                .Subscribe(_ => 
                {
                    this.RaisePropertyChanged(nameof(CloudEngineStatusText));
                    this.RaisePropertyChanged(nameof(CloudEngineStatus));
                });
        
        _statusService.NetworkStatus?.WhenAnyValue(
                x => x.IsConnected,
                x => x.LatencyMs)
                .Subscribe(_ => 
                {
                    this.RaisePropertyChanged(nameof(NetworkStatusText));
                    this.RaisePropertyChanged(nameof(NetworkStatus));
                });
        
        // 状態監視を自動開始
        _ = Task.Run(async () =>
        {
            try
            {
                await _statusService.StartMonitoringAsync().ConfigureAwait(false);
                IsStatusMonitoringEnabled = true;
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogError(ex, "状態監視サービスが既に実行中です");
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger?.LogError(ex, "状態監視の開始権限がありません");
            }
            catch (TimeoutException ex)
            {
                Logger?.LogError(ex, "状態監視の開始がタイムアウトしました");
            }
        });
    }
    
    /// <summary>
    /// 状態更新イベントの処理
    /// </summary>
    private void OnStatusUpdate(TranslationEngineStatusUpdate update)
    {
        try
        {
            // フォールバック情報の更新
            this.RaisePropertyChanged(nameof(LastFallbackInfo));
            this.RaisePropertyChanged(nameof(LastFallbackText));
            
            // エンジン別の状態更新
            switch (update.EngineName)
            {
                case "LocalOnly":
                    this.RaisePropertyChanged(nameof(LocalEngineStatus));
                    this.RaisePropertyChanged(nameof(LocalEngineStatusText));
                    break;
                    
                case "CloudOnly":
                    this.RaisePropertyChanged(nameof(CloudEngineStatus));
                    this.RaisePropertyChanged(nameof(CloudEngineStatusText));
                    break;
                    
                case "Network":
                    this.RaisePropertyChanged(nameof(NetworkStatus));
                    this.RaisePropertyChanged(nameof(NetworkStatusText));
                    break;
            }
            
            // フォールバック発生時の特別処理
            if (update.UpdateType == StatusUpdateType.FallbackTriggered)
            {
                Logger?.LogInformation(
                    "フォールバックが発生しました: {EngineName} at {UpdatedAt}",
                    update.EngineName, update.UpdatedAt);
                    
                // フォールバック通知を表示する必要がある場合
                if (ShowFallbackNotifications)
                {
                    // TODO: トースト通知の実装
                }
            }
        }
        catch (ArgumentNullException ex)
        {
            Logger?.LogError(ex, "状態更新イベントのパラメータが無効です");
        }
        catch (InvalidOperationException ex)
        {
            Logger?.LogError(ex, "状態更新イベントの処理中に無効な操作が発生しました");
        }
    }

    /// <summary>
    /// アクティベーション時の処理
    /// </summary>
    protected override void HandleActivation()
    {
        // 設定を読み込む
        LoadSettings();
    }

    /// <summary>
    /// 設定を読み込みます
    /// </summary>
    private void LoadSettings()
    {
        // TODO: 永続化された設定を読み込む処理を実装する
        // 現状はデフォルト値を設定
        HasChanges = false;
    }

    /// <summary>
    /// テーマ変更時の処理
    /// </summary>
    private void OnThemeChanged(bool isDarkTheme)
    {
        // テーマ変更イベントを発行
        _ = PublishEventAsync(new ThemeChangedEvent
        {
            IsDarkTheme = isDarkTheme
        });
    }

    /// <summary>
    /// 設定を保存するコマンド実行
    /// </summary>
    private async Task ExecuteSaveAsync()
    {
        IsLoading = true;
        try
        {
            // 言語設定の変更を通知
            var languageEvent = new LanguageSettingsChangedEvent
            {
                UILanguage = UILanguage,
                OCRLanguage = OCRLanguage,
                TranslationLanguage = TranslationLanguage
            };
            await PublishEventAsync(languageEvent).ConfigureAwait(false);

            // ホットキー設定の変更を通知
            var hotkeyEvent = new HotkeySettingsChangedEvent
            {
                CaptureHotkey = CaptureHotkey,
                TranslateHotkey = TranslateHotkey,
                ResetHotkey = ResetHotkey
            };
            await PublishEventAsync(hotkeyEvent).ConfigureAwait(false);

            // 翻訳エンジン設定の変更を通知
            var translationEngineEvent = new TranslationEngineSettingsChangedEvent
            {
                SelectedEngine = SelectedTranslationEngine,
                EnableRateLimitFallback = EnableRateLimitFallback,
                EnableNetworkErrorFallback = EnableNetworkErrorFallback,
                EnableApiErrorFallback = EnableApiErrorFallback,
                ShowFallbackNotifications = ShowFallbackNotifications,
                FallbackTimeoutSeconds = FallbackTimeoutSeconds,
                RecoveryCheckIntervalMinutes = RecoveryCheckIntervalMinutes
            };
            await PublishEventAsync(translationEngineEvent).ConfigureAwait(false);

            // 一般設定の変更を通知
            var generalEvent = new GeneralSettingsChangedEvent
            {
                StartWithWindows = StartWithWindows,
                MinimizeToTray = MinimizeToTray,
                EnableDebugLogs = EnableDebugLogs
            };
            await PublishEventAsync(generalEvent).ConfigureAwait(false);

            // TODO: 永続化処理を実装

            // 変更フラグをリセット
            HasChanges = false;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = $"設定の保存中に操作エラーが発生しました: {ex.Message}";
            _logSettingsOperationError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = $"設定の保存中に引数エラーが発生しました: {ex.Message}";
            _logSettingsArgumentError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
        catch (IOException ex)
        {
            ErrorMessage = $"設定ファイルの操作中にエラーが発生しました: {ex.Message}";
            _logSettingsFileError(Logger ?? NullLogger.Instance, ex.Message, ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 設定の変更をキャンセルするコマンド実行
    /// </summary>
    private async Task ExecuteCancelAsync()
    {
        // 設定を再読み込み
        LoadSettings();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 設定をデフォルトに戻すコマンド実行
    /// </summary>
    private async Task ExecuteResetToDefaultsAsync()
    {
        // デフォルト設定を適用
        IsDarkTheme = true;
        UIScale = 1.0;
        UILanguage = "日本語";
        OCRLanguage = "日本語";
        TranslationLanguage = "英語";
        SelectedTranslationEngine = "LocalOnly";
        EnableRateLimitFallback = true;
        EnableNetworkErrorFallback = true;
        EnableApiErrorFallback = true;
        ShowFallbackNotifications = true;
        FallbackTimeoutSeconds = 10;
        RecoveryCheckIntervalMinutes = 5;
        CaptureHotkey = "Ctrl+Alt+C";
        TranslateHotkey = "Ctrl+Alt+T";
        ResetHotkey = "Ctrl+Alt+R";
        StartWithWindows = false;
        MinimizeToTray = true;
        EnableDebugLogs = false;

        // 変更フラグを設定
        HasChanges = true;

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 設定カテゴリを選択するコマンド実行
    /// </summary>
    private void ExecuteSelectCategory(SettingCategory category)
    {
        SelectedCategory = category;
    }
    
    /// <summary>
    /// 状態監視開始コマンド実行
    /// </summary>
    private async Task ExecuteStartStatusMonitoringAsync()
    {
        if (_statusService == null)
        {
            ErrorMessage = "状態監視サービスが利用できません";
            return;
        }
        
        IsLoading = true;
        try
        {
            await _statusService.StartMonitoringAsync().ConfigureAwait(false);
            IsStatusMonitoringEnabled = true;
            ErrorMessage = string.Empty;
            
            Logger?.LogInformation("状態監視を手動で開始しました");
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = $"状態監視が既に実行中です: {ex.Message}";
            Logger?.LogError(ex, "状態監視が既に実行中です");
        }
        catch (UnauthorizedAccessException ex)
        {
            ErrorMessage = $"状態監視の開始権限がありません: {ex.Message}";
            Logger?.LogError(ex, "状態監視の開始権限がありません");
        }
        catch (TimeoutException ex)
        {
            ErrorMessage = $"状態監視の開始がタイムアウトしました: {ex.Message}";
            Logger?.LogError(ex, "状態監視の開始がタイムアウトしました");
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// 状態監視停止コマンド実行
    /// </summary>
    private async Task ExecuteStopStatusMonitoringAsync()
    {
        if (_statusService == null)
        {
            return;
        }
        
        IsLoading = true;
        try
        {
            await _statusService.StopMonitoringAsync().ConfigureAwait(false);
            IsStatusMonitoringEnabled = false;
            ErrorMessage = string.Empty;
            
            Logger?.LogInformation("状態監視を手動で停止しました");
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = $"状態監視が実行中ではありません: {ex.Message}";
            Logger?.LogError(ex, "状態監視が実行中ではありません");
        }
        catch (TimeoutException ex)
        {
            ErrorMessage = $"状態監視の停止がタイムアウトしました: {ex.Message}";
            Logger?.LogError(ex, "状態監視の停止がタイムアウトしました");
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// 状態リフレッシュコマンド実行
    /// </summary>
    private async Task ExecuteRefreshStatusAsync()
    {
        if (_statusService == null)
        {
            ErrorMessage = "状態監視サービスが利用できません";
            return;
        }
        
        IsLoading = true;
        try
        {
            await _statusService.RefreshStatusAsync().ConfigureAwait(false);
            ErrorMessage = string.Empty;
            
            Logger?.LogDebug("状態を手動でリフレッシュしました");
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = $"状態リフレッシュが無効な状態です: {ex.Message}";
            Logger?.LogError(ex, "状態リフレッシュが無効な状態です");
        }
        catch (TimeoutException ex)
        {
            ErrorMessage = $"状態リフレッシュがタイムアウトしました: {ex.Message}";
            Logger?.LogError(ex, "状態リフレッシュがタイムアウトしました");
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// リソースを解放します
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 状態更新購読を解除
            _statusUpdateSubscription?.Dispose();
            
            // 状態監視を停止
            if (_statusService != null && IsStatusMonitoringEnabled)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _statusService.StopMonitoringAsync().ConfigureAwait(false);
                    }
                    catch (InvalidOperationException ex)
                    {
                        Logger?.LogWarning(ex, "ViewModel破棄時に状態監視が既に停止していました");
                    }
                    catch (TimeoutException ex)
                    {
                        Logger?.LogWarning(ex, "ViewModel破棄時の状態監視停止がタイムアウトしました");
                    }
                });
            }
        }
        
        base.Dispose(disposing);
    }
}

// 警告抑制を解除
#pragma warning restore IDE0028
#pragma warning restore CA1515

/// <summary>
/// テーマ変更イベント
/// </summary>
internal sealed class ThemeChangedEvent : CoreEvents.EventBase
{
/// <inheritdoc/>
public override string Name => "ThemeChanged";

/// <inheritdoc/>
public override string Category => "UI.Settings";

/// <summary>
/// ダークテーマフラグ
/// </summary>
public bool IsDarkTheme { get; set; }
}

/// <summary>
/// 言語設定変更イベント
/// </summary>
internal sealed class LanguageSettingsChangedEvent : CoreEvents.EventBase
{
/// <inheritdoc/>
public override string Name => "LanguageSettingsChanged";

/// <inheritdoc/>
public override string Category => "UI.Settings";

/// <summary>
/// UI言語
/// </summary>
public string UILanguage { get; set; } = string.Empty;

/// <summary>
/// OCR言語
/// </summary>
public string OCRLanguage { get; set; } = string.Empty;

/// <summary>
/// 翻訳言語
/// </summary>
public string TranslationLanguage { get; set; } = string.Empty;
}

/// <summary>
/// ホットキー設定変更イベント
/// </summary>
internal sealed class HotkeySettingsChangedEvent : CoreEvents.EventBase
{
/// <inheritdoc/>
public override string Name => "HotkeySettingsChanged";

/// <inheritdoc/>
public override string Category => "UI.Settings";

/// <summary>
/// キャプチャホットキー
/// </summary>
public string CaptureHotkey { get; set; } = string.Empty;

/// <summary>
/// 翻訳ホットキー
/// </summary>
public string TranslateHotkey { get; set; } = string.Empty;

/// <summary>
/// リセットホットキー
/// </summary>
public string ResetHotkey { get; set; } = string.Empty;
}

/// <summary>
/// 翻訳エンジン設定変更イベント
/// </summary>
internal sealed class TranslationEngineSettingsChangedEvent : CoreEvents.EventBase
{
/// <inheritdoc/>
public override string Name => "TranslationEngineSettingsChanged";

/// <inheritdoc/>
public override string Category => "UI.Settings";

/// <summary>
/// 選択されたエンジン
/// </summary>
public string SelectedEngine { get; set; } = string.Empty;

/// <summary>
/// レート制限時のフォールバック有効化
/// </summary>
public bool EnableRateLimitFallback { get; set; }

/// <summary>
/// ネットワークエラー時のフォールバック有効化
/// </summary>
public bool EnableNetworkErrorFallback { get; set; }

/// <summary>
/// APIエラー時のフォールバック有効化
/// </summary>
public bool EnableApiErrorFallback { get; set; }

/// <summary>
/// フォールバック通知表示
/// </summary>
public bool ShowFallbackNotifications { get; set; }

/// <summary>
/// フォールバック判定タイムアウト（秒）
/// </summary>
public int FallbackTimeoutSeconds { get; set; }

/// <summary>
/// 復旧チェック間隔（分）
/// </summary>
public int RecoveryCheckIntervalMinutes { get; set; }
}

/// <summary>
/// 一般設定変更イベント
/// </summary>
internal sealed class GeneralSettingsChangedEvent : CoreEvents.EventBase
{
/// <inheritdoc/>
public override string Name => "GeneralSettingsChanged";

/// <inheritdoc/>
public override string Category => "UI.Settings";

/// <summary>
/// Windows起動時に自動起動
/// </summary>
public bool StartWithWindows { get; set; }

/// <summary>
/// トレイに最小化
/// </summary>
public bool MinimizeToTray { get; set; }

/// <summary>
/// デバッグログ有効化
/// </summary>
public bool EnableDebugLogs { get; set; }
}
