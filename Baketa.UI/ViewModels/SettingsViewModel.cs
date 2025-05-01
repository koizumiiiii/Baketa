using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using Baketa.Core.Events;
using Baketa.UI.Framework;
using Baketa.UI.Framework.ReactiveUI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReactiveUI;

// 名前空間エイリアスを使用して衝突を解決
using CoreEvents = Baketa.Core.Events;
using UIEvents = Baketa.UI.Framework.Events;

// IDE0028の警告を抑制
#pragma warning disable IDE0028 // コレクション初期化を簡細化できます

// CA1515の警告を抑制
#pragma warning disable CA1515 // クラスライブラリと異なり、アプリケーションのAPIは通常公開参照されないため、型を内部としてマークできます

namespace Baketa.UI.ViewModels
{
    /// <summary>
    /// 設定画面のビューモデル
    /// </summary>
    internal sealed class SettingsViewModel : Framework.ViewModelBase
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

        // 設定カテゴリ
        public enum SettingCategory
        {
            General,
            Appearance,
            Language,
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

        // コマンド
        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetToDefaultsCommand { get; }
        public ReactiveCommand<SettingCategory, Unit> SelectCategoryCommand { get; }

        /// <summary>
        /// 新しいSettingsViewModelを初期化します
        /// </summary>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="accessibilityViewModel">アクセシビリティ設定ビューモデル</param>
        /// <param name="logger">ロガー</param>
        public SettingsViewModel(
            UIEvents.IEventAggregator eventAggregator, 
            AccessibilitySettingsViewModel accessibilityViewModel,
            ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            AccessibilityViewModel = accessibilityViewModel 
                ?? throw new ArgumentNullException(nameof(accessibilityViewModel));
            
            // コマンドの初期化
            SaveCommand = Baketa.UI.Framework.CommandHelper.CreateCommand(ExecuteSaveAsync);
            CancelCommand = Baketa.UI.Framework.CommandHelper.CreateCommand(ExecuteCancelAsync);
            ResetToDefaultsCommand = Baketa.UI.Framework.CommandHelper.CreateCommand(ExecuteResetToDefaultsAsync);
            SelectCategoryCommand = ReactiveCommand.Create<SettingCategory>(ExecuteSelectCategory);

            // プロパティの変更を監視して設定変更フラグを設定
            this.WhenAnyValue(
                x => x.IsDarkTheme,
                x => x.UIScale,
                x => x.UILanguage,
                x => x.OCRLanguage,
                x => x.TranslationLanguage,
                x => x.CaptureHotkey,
                x => x.TranslateHotkey,
                x => x.ResetHotkey,
                x => x.StartWithWindows,
                x => x.MinimizeToTray,
                x => x.EnableDebugLogs,
                (darkTheme, uiScale, uiLang, ocrLang, transLang, captureHotkey, translateHotkey, resetHotkey, startWithWindows, minimizeToTray, enableDebugLogs) => true
            ).Subscribe(_ => HasChanges = true);
            
            // アクセシビリティ設定開始イベントの購読
            SubscribeToEvent<CoreEvents.AccessibilityEvents.OpenAccessibilitySettingsRequestedEvent>(async _ =>
            {
                SelectedCategory = SettingCategory.Accessibility;
                await Task.CompletedTask.ConfigureAwait(false);
            });
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
                _logSettingsOperationError(_logger ?? NullLogger.Instance, ex.Message, ex);
            }
            catch (ArgumentException ex)
            {
                ErrorMessage = $"設定の保存中に引数エラーが発生しました: {ex.Message}";
                _logSettingsArgumentError(_logger ?? NullLogger.Instance, ex.Message, ex);
            }
            catch (IOException ex)
            {
                ErrorMessage = $"設定ファイルの操作中にエラーが発生しました: {ex.Message}";
                _logSettingsFileError(_logger ?? NullLogger.Instance, ex.Message, ex);
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