// TranslationSettingsView.axaml.cs
using Avalonia.Controls;
using Avalonia.Interactivity;
using Baketa.UI.ViewModels.Settings;

namespace Baketa.UI.Views.Settings
{
    public partial class TranslationSettingsView : UserControl
    {
        public TranslationSettingsView()
        {
            InitializeComponent();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            
            // ViewModelがある場合は初期化
            if (DataContext is TranslationSettingsViewModel viewModel)
            {
                // 必要に応じて初期化処理
                viewModel.LoadInitialSettingsAsync();
            }
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            // リソースクリーンアップ
            if (DataContext is IDisposable disposableViewModel)
            {
                disposableViewModel.Dispose();
            }
            
            base.OnUnloaded(e);
        }
    }
}

// EngineSelectionControl.axaml.cs
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Baketa.UI.ViewModels.Settings;

namespace Baketa.UI.Views.Settings
{
    public partial class EngineSelectionControl : UserControl
    {
        public EngineSelectionControl()
        {
            InitializeComponent();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            
            // キーボードアクセシビリティ対応
            this.KeyDown += OnKeyDown;
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            this.KeyDown -= OnKeyDown;
            base.OnUnloaded(e);
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is not EngineSelectionViewModel viewModel) 
                return;

            // キーボードショートカット対応
            switch (e.Key)
            {
                case Key.D1:
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                    {
                        viewModel.IsLocalOnlySelected = true;
                        e.Handled = true;
                    }
                    break;
                    
                case Key.D2:
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && viewModel.IsCloudOnlyAvailable)
                    {
                        viewModel.IsCloudOnlySelected = true;
                        e.Handled = true;
                    }
                    break;
                    
                case Key.U:
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && viewModel.IsFreePlan)
                    {
                        viewModel.UpgradeCommand.Execute().Subscribe();
                        e.Handled = true;
                    }
                    break;
            }
        }
    }
}

// LanguagePairSelectionControl.axaml.cs
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Baketa.UI.ViewModels.Settings;
using System.Linq;

namespace Baketa.UI.Views.Settings
{
    public partial class LanguagePairSelectionControl : UserControl
    {
        public LanguagePairSelectionControl()
        {
            InitializeComponent();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            
            // マウスアクセシビリティ対応
            this.DoubleTapped += OnDoubleTapped;
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            this.DoubleTapped -= OnDoubleTapped;
            base.OnUnloaded(e);
        }

        private void OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is not LanguagePairSelectionViewModel viewModel) 
                return;

            // ダブルクリックで全選択/全解除切り替え
            var allEnabled = viewModel.AvailableLanguagePairs.All(p => p.IsEnabled);
            foreach (var pair in viewModel.AvailableLanguagePairs)
            {
                pair.IsEnabled = !allEnabled;
            }
        }
    }
}

// TranslationStrategyControl.axaml.cs
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Baketa.UI.ViewModels.Settings;

namespace Baketa.UI.Views.Settings
{
    public partial class TranslationStrategyControl : UserControl
    {
        public TranslationStrategyControl()
        {
            InitializeComponent();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            
            // ツールチップ用のマウスイベント
            this.PointerEntered += OnPointerEntered;
            this.PointerExited += OnPointerExited;
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            this.PointerEntered -= OnPointerEntered;
            this.PointerExited -= OnPointerExited;
            base.OnUnloaded(e);
        }

        private void OnPointerEntered(object? sender, PointerEventArgs e)
        {
            // 戦略説明の強調表示
            if (DataContext is TranslationStrategyViewModel viewModel)
            {
                // 必要に応じてホバー効果を追加
            }
        }

        private void OnPointerExited(object? sender, PointerEventArgs e)
        {
            // ホバー効果のリセット
        }
    }
}

// EngineStatusControl.axaml.cs
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Baketa.UI.ViewModels.Settings;
using System;

namespace Baketa.UI.Views.Settings
{
    public partial class EngineStatusControl : UserControl
    {
        private DispatcherTimer? _refreshTimer;

        public EngineStatusControl()
        {
            InitializeComponent();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            
            // 自動更新タイマーの開始
            StartAutoRefresh();
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            // タイマーの停止
            StopAutoRefresh();
            base.OnUnloaded(e);
        }

        private void StartAutoRefresh()
        {
            if (_refreshTimer != null) return;

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30) // 30秒間隔で更新
            };

            _refreshTimer.Tick += OnRefreshTick;
            _refreshTimer.Start();
        }

        private void StopAutoRefresh()
        {
            _refreshTimer?.Stop();
            _refreshTimer = null;
        }

        private void OnRefreshTick(object? sender, EventArgs e)
        {
            if (DataContext is EngineStatusViewModel viewModel)
            {
                // 自動更新実行
                viewModel.RefreshStatusCommand?.Execute().Subscribe();
            }
        }
    }
}

// ViewModelLocator.cs - ViewModelの集中管理
using Baketa.UI.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.UI.ViewModels
{
    public static class ViewModelLocator
    {
        private static IServiceProvider? _serviceProvider;

        public static void Initialize(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public static TranslationSettingsViewModel TranslationSettings =>
            _serviceProvider?.GetRequiredService<TranslationSettingsViewModel>() 
            ?? throw new InvalidOperationException("ServiceProvider not initialized");

        public static EngineSelectionViewModel EngineSelection =>
            _serviceProvider?.GetRequiredService<EngineSelectionViewModel>() 
            ?? throw new InvalidOperationException("ServiceProvider not initialized");

        public static LanguagePairSelectionViewModel LanguagePairSelection =>
            _serviceProvider?.GetRequiredService<LanguagePairSelectionViewModel>() 
            ?? throw new InvalidOperationException("ServiceProvider not initialized");

        public static TranslationStrategyViewModel TranslationStrategy =>
            _serviceProvider?.GetRequiredService<TranslationStrategyViewModel>() 
            ?? throw new InvalidOperationException("ServiceProvider not initialized");

        public static EngineStatusViewModel EngineStatus =>
            _serviceProvider?.GetRequiredService<EngineStatusViewModel>() 
            ?? throw new InvalidOperationException("ServiceProvider not initialized");
    }
}

// ErrorHandlingExtensions.cs - エラーハンドリング支援
using Avalonia.Controls.Notifications;
using System;
using System.Threading.Tasks;

namespace Baketa.UI.Extensions
{
    public static class ErrorHandlingExtensions
    {
        public static async Task HandleWithNotification<T>(
            this Task<T> task,
            INotificationManager? notificationManager,
            string? successMessage = null,
            string? errorTitle = "エラー")
        {
            try
            {
                await task;
                
                if (!string.IsNullOrEmpty(successMessage) && notificationManager != null)
                {
                    notificationManager.Show(new Notification(
                        "成功", 
                        successMessage, 
                        NotificationType.Success));
                }
            }
            catch (OperationCanceledException)
            {
                // キャンセルは無視
            }
            catch (Exception ex)
            {
                if (notificationManager != null)
                {
                    notificationManager.Show(new Notification(
                        errorTitle ?? "エラー", 
                        ex.Message, 
                        NotificationType.Error));
                }
            }
        }

        public static async Task HandleWithNotification(
            this Task task,
            INotificationManager? notificationManager,
            string? successMessage = null,
            string? errorTitle = "エラー")
        {
            try
            {
                await task;
                
                if (!string.IsNullOrEmpty(successMessage) && notificationManager != null)
                {
                    notificationManager.Show(new Notification(
                        "成功", 
                        successMessage, 
                        NotificationType.Success));
                }
            }
            catch (OperationCanceledException)
            {
                // キャンセルは無視
            }
            catch (Exception ex)
            {
                if (notificationManager != null)
                {
                    notificationManager.Show(new Notification(
                        errorTitle ?? "エラー", 
                        ex.Message, 
                        NotificationType.Error));
                }
            }
        }
    }
}

// PerformanceOptimizations.cs - パフォーマンス最適化
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace Baketa.UI.Helpers
{
    public static class PerformanceOptimizations
    {
        // ViewModel変更の監視を効率化
        public static IDisposable WhenAnyValueThrottled<TViewModel, TProperty>(
            this TViewModel viewModel,
            Func<TViewModel, TProperty> propertySelector,
            TimeSpan throttle,
            Action<TProperty> onNext)
            where TViewModel : class, INotifyPropertyChanged
        {
            return viewModel.WhenAnyValue(propertySelector)
                .Throttle(throttle)
                .ObserveOn(AvaloniaScheduler.Instance)
                .Subscribe(onNext);
        }

        // 重い処理の遅延実行
        public static IDisposable DelayedExecution(
            Action action,
            TimeSpan delay,
            CompositeDisposable disposables)
        {
            var subscription = Observable.Timer(delay)
                .ObserveOn(AvaloniaScheduler.Instance)
                .Subscribe(_ => action());
                
            subscription.DisposeWith(disposables);
            return subscription;
        }

        // メモリリーク防止のためのWeakEvent購読
        public static IDisposable SubscribeWeak<T>(
            this IObservable<T> source,
            WeakReference<Action<T>> actionRef)
        {
            return source.Subscribe(value =>
            {
                if (actionRef.TryGetTarget(out var action))
                {
                    action(value);
                }
            });
        }
    }
}

// AccessibilityHelper.cs - アクセシビリティ支援
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;

namespace Baketa.UI.Helpers
{
    public static class AccessibilityHelper
    {
        public static void ConfigureTranslationSettingsAccessibility(Control control)
        {
            // スクリーンリーダー対応
            AutomationProperties.SetName(control, "翻訳設定");
            AutomationProperties.SetHelpText(control, "Baketaの翻訳エンジンと言語設定を管理");
            
            // キーボードナビゲーション
            control.TabIndex = 0;
            KeyboardNavigation.SetIsTabStop(control, true);
        }

        public static void ConfigureEngineSelectionAccessibility(RadioButton radioButton, string engineName)
        {
            AutomationProperties.SetName(radioButton, $"{engineName}翻訳エンジン");
            AutomationProperties.SetHelpText(radioButton, $"{engineName}エンジンを選択");
            
            // ラジオボタンのロール設定
            AutomationProperties.SetAccessibilityView(radioButton, AccessibilityView.Content);
        }

        public static void ConfigureLanguagePairAccessibility(CheckBox checkBox, string languagePair)
        {
            AutomationProperties.SetName(checkBox, $"{languagePair}言語ペア");
            AutomationProperties.SetHelpText(checkBox, $"{languagePair}の翻訳を有効化");
        }

        public static void ConfigureStatusAccessibility(Control statusControl, string statusText)
        {
            AutomationProperties.SetName(statusControl, "翻訳エンジン状態");
            AutomationProperties.SetHelpText(statusControl, $"現在の状態: {statusText}");
            
            // ライブリージョン設定（状態変更を自動読み上げ）
            AutomationProperties.SetLiveSetting(statusControl, AutomationLiveSetting.Polite);
        }
    }
}

// ValidationHelper.cs - 設定検証
using Baketa.Core.Translation.Models;
using System.Collections.Generic;
using System.Linq;

namespace Baketa.UI.Helpers
{
    public static class ValidationHelper
    {
        public static ValidationResult ValidateTranslationSettings(TranslationSettings settings)
        {
            var errors = new List<string>();

            // エンジン選択の検証
            if (!Enum.IsDefined(typeof(TranslationEngine), settings.SelectedEngine))
            {
                errors.Add("無効な翻訳エンジンが選択されています。");
            }

            // 言語ペアの検証
            if (settings.EnabledLanguagePairs?.Count == 0)
            {
                errors.Add("少なくとも1つの言語ペアを有効にしてください。");
            }

            // 中国語変種の検証
            if (!Enum.IsDefined(typeof(ChineseVariant), settings.ChineseVariant))
            {
                errors.Add("無効な中国語変種が選択されています。");
            }

            // 翻訳戦略の検証
            if (!Enum.IsDefined(typeof(TranslationStrategy), settings.TranslationStrategy))
            {
                errors.Add("無効な翻訳戦略が選択されています。");
            }

            // TwoStage戦略での言語ペア検証
            if (settings.TranslationStrategy == TranslationStrategy.TwoStage)
            {
                var supportedTwoStagePairs = new[] { "ja-zh" };
                var enabledTwoStagePairs = settings.EnabledLanguagePairs?
                    .Where(pair => supportedTwoStagePairs.Contains(pair))
                    .ToList();

                if (enabledTwoStagePairs?.Count == 0)
                {
                    errors.Add("TwoStage戦略が選択されていますが、対応する言語ペア（ja-zh）が有効になっていません。");
                }
            }

            return new ValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors
            };
        }

        public static ValidationResult ValidateEngineAvailability(
            TranslationEngine selectedEngine, 
            bool hasCloudAccess)
        {
            var errors = new List<string>();

            if (selectedEngine == TranslationEngine.CloudOnly && !hasCloudAccess)
            {
                errors.Add("CloudOnlyエンジンの使用には有料プランが必要です。");
            }

            return new ValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors
            };
        }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
}

// LanguagePairItemViewModel拡張 - 詳細説明追加
namespace Baketa.UI.ViewModels.Settings
{
    public partial class LanguagePairItemViewModel
    {
        public string DetailedDescription => Type switch
        {
            LanguagePairType.Direct => "単一モデルによる直接翻訳。最高速度。",
            LanguagePairType.Bidirectional => "双方向翻訳対応。高精度・高速。",
            LanguagePairType.TwoStage => "2段階翻訳。英語経由で高品質翻訳。",
            _ => "標準翻訳。"
        };
    }
}

// EngineStatusViewModel拡張 - 詳細状態情報
namespace Baketa.UI.ViewModels.Settings
{
    public partial class EngineStatusViewModel
    {
        [Reactive] public string LastUpdateTime { get; private set; } = "確認中...";
        [Reactive] public string DetailedStatusInfo { get; private set; } = string.Empty;
        [Reactive] public string StatisticsSummary { get; private set; } = string.Empty;
        [Reactive] public string TodayTranslationCount { get; private set; } = "0";
        [Reactive] public bool HasError { get; private set; }
        [Reactive] public string LastErrorMessage { get; private set; } = string.Empty;
        [Reactive] public string FallbackDuration { get; private set; } = string.Empty;

        public ReactiveCommand<Unit, Unit> RefreshStatusCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowFallbackDetailsCommand { get; }
        public ReactiveCommand<Unit, Unit> RetryCommand { get; }

        private void UpdateDetailedInfo(TranslationEngineStatus status)
        {
            LastUpdateTime = $"最終更新: {DateTime.Now:HH:mm:ss}";
            
            DetailedStatusInfo = $"ローカルエンジン: {status.LocalOnlyStatus.Health}\n" +
                               $"クラウドエンジン: {status.CloudOnlyStatus.Health}\n" +
                               $"ネットワーク: {(status.NetworkAvailable ? "接続中" : "未接続")}";

            StatisticsSummary = $"本日 {TodayTranslationCount} 件翻訳 | " +
                              $"平均 {AverageLatency} | " +
                              $"成功率 {SuccessRate}";

            HasError = !string.IsNullOrEmpty(status.LastError);
            LastErrorMessage = status.LastError ?? string.Empty;
        }
    }
}