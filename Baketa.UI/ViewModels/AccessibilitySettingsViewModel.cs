using System;
using System.Reactive;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Services;
using Baketa.UI.Framework.ReactiveUI;

// using UIEvents = Baketa.UI.Framework.Events; // 古いEventsを削除

namespace Baketa.UI.ViewModels;

    /// <summary>
    /// アクセシビリティ設定を管理するビューモデル
    /// </summary>
    internal sealed class AccessibilitySettingsViewModel : Framework.ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        
        // ReactiveUI.Fodyを使用したReactiveプロパティ
        [Reactive] public bool DisableAnimations { get; set; }
        [Reactive] public bool HighContrastMode { get; set; }
        [Reactive] public double FontScaleFactor { get; set; } = 1.0;
        [Reactive] public bool AlwaysShowKeyboardFocus { get; set; }
        [Reactive] public double KeyboardNavigationSpeed { get; set; } = 1.0;
        
        // コマンド定義
        public ReactiveCommand<Unit, Unit> SaveSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetToDefaultCommand { get; }
        
        /// <summary>
        /// 新しいAccessibilitySettingsViewModelを初期化します
        /// </summary>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="settingsService">設定サービス</param>
        /// <param name="logger">ロガー</param>
        public AccessibilitySettingsViewModel(
            Baketa.Core.Abstractions.Events.IEventAggregator eventAggregator,
            ISettingsService settingsService,
            ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            
            // コマンドの初期化
            SaveSettingsCommand = ReactiveCommandFactory.Create(SaveSettingsAsync);
            ResetToDefaultCommand = ReactiveCommandFactory.Create(ResetToDefaults);
            
            // 設定の読み込み
            LoadSettings();
        }
        
        /// <summary>
        /// 設定をロードします
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                Logger?.LogInformation("アクセシビリティ設定を読み込み中");
                
                // 設定サービスから値を読み込む
                DisableAnimations = _settingsService.GetValue("Accessibility:DisableAnimations", false);
                HighContrastMode = _settingsService.GetValue("Accessibility:HighContrastMode", false);
                FontScaleFactor = _settingsService.GetValue("Accessibility:FontScaleFactor", 1.0);
                AlwaysShowKeyboardFocus = _settingsService.GetValue("Accessibility:AlwaysShowKeyboardFocus", false);
                KeyboardNavigationSpeed = _settingsService.GetValue("Accessibility:KeyboardNavigationSpeed", 1.0);
                
                Logger?.LogInformation("アクセシビリティ設定を読み込みました");
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogError(ex, "アクセシビリティ設定の読み込み中に操作エラーが発生しました");
                ResetToDefaults();
            }
            catch (ArgumentException ex)
            {
                Logger?.LogError(ex, "アクセシビリティ設定の読み込み中に引数エラーが発生しました");
                ResetToDefaults();
            }
            catch (FormatException ex)
            {
                Logger?.LogError(ex, "アクセシビリティ設定の読み込み中に形式エラーが発生しました");
                ResetToDefaults();
            }
            catch (NullReferenceException ex)
            {
                Logger?.LogError(ex, "アクセシビリティ設定の読み込み中に参照エラーが発生しました");
                ResetToDefaults();
            }
        }
        
        /// <summary>
        /// 設定をデフォルト値にリセットします
        /// </summary>
        private void ResetToDefaults()
        {
            Logger?.LogInformation("アクセシビリティ設定をデフォルト値にリセットします");
            
            DisableAnimations = false;
            HighContrastMode = false;
            FontScaleFactor = 1.0;
            AlwaysShowKeyboardFocus = false;
            KeyboardNavigationSpeed = 1.0;
        }
        
        /// <summary>
        /// 設定を保存し、アプリケーション全体に設定変更を通知します
        /// </summary>
        private async Task SaveSettingsAsync()
        {
            try
            {
                Logger?.LogInformation("アクセシビリティ設定を保存中");
                
                // 設定サービスに値を保存
                _settingsService.SetValue("Accessibility:DisableAnimations", DisableAnimations);
                _settingsService.SetValue("Accessibility:HighContrastMode", HighContrastMode);
                _settingsService.SetValue("Accessibility:FontScaleFactor", FontScaleFactor);
                _settingsService.SetValue("Accessibility:AlwaysShowKeyboardFocus", AlwaysShowKeyboardFocus);
                _settingsService.SetValue("Accessibility:KeyboardNavigationSpeed", KeyboardNavigationSpeed);
                
                // 設定を確定
                await _settingsService.SaveAsync().ConfigureAwait(false);
                
                // 設定変更イベントを発行（AccessibilitySettingsChangedEventは後で定義）
                // await _eventAggregator.PublishAsync(new AccessibilitySettingsChangedEvent
                // {
                //     DisableAnimations = DisableAnimations,
                //     HighContrastMode = HighContrastMode,
                //     FontScaleFactor = FontScaleFactor,
                //     AlwaysShowKeyboardFocus = AlwaysShowKeyboardFocus,
                //     KeyboardNavigationSpeed = KeyboardNavigationSpeed
                // }).ConfigureAwait(false);
                
                Logger?.LogInformation("アクセシビリティ設定を保存しました");
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogError(ex, "アクセシビリティ設定の保存中に操作エラーが発生しました");
                ErrorMessage = "設定の保存中にエラーが発生しました。";
            }
            catch (ArgumentException ex)
            {
                Logger?.LogError(ex, "アクセシビリティ設定の保存中に引数エラーが発生しました");
                ErrorMessage = "設定の保存中にエラーが発生しました。";
            }
            catch (NullReferenceException ex)
            {
                Logger?.LogError(ex, "アクセシビリティ設定の保存中に参照エラーが発生しました");
                ErrorMessage = "設定の保存中にエラーが発生しました。";
            }
            catch (TaskCanceledException ex)
            {
                Logger?.LogError(ex, "アクセシビリティ設定の保存中にタスクがキャンセルされました");
                ErrorMessage = "設定の保存がキャンセルされました。";
            }
        }
    }
