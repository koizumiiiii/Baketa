// Program.cs - アプリケーションエントリーポイント
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Baketa.UI.Extensions;
using Baketa.UI.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;

namespace Baketa.UI
{
    public partial class App : Application
    {
        private IHost? _host;
        private IServiceProvider? _services;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            
            // DI コンテナとホストの設定
            ConfigureHost();
        }

        private void ConfigureHost()
        {
            var builder = Host.CreateDefaultBuilder();
            
            builder.ConfigureAppConfiguration((context, config) =>
            {
                // 設定ファイルの読み込み
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", 
                                   optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables();
            });

            builder.ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;
                
                // 既存の翻訳サービス
                services.AddSentencePieceTokenizer(configuration);
                services.AddChineseTranslationSupport(configuration);
                services.AddCompleteTranslationServices(configuration);
                
                // UI層のサービス
                services.AddTranslationSettingsUI(configuration);
                
                // 通知サービス
                services.AddSingleton<INotificationService, AvaloniaNotificationService>();
                
                // ログ設定
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.AddDebug();
                    builder.SetMinimumLevel(LogLevel.Information);
                });
            });

            _host = builder.Build();
            _services = _host.Services;
            
            // ViewModelLocatorの初期化
            ViewModelLocator.Initialize(_services);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                try
                {
                    var mainViewModel = _services!.GetRequiredService<MainViewModel>();
                    desktop.MainWindow = new MainWindow
                    {
                        DataContext = mainViewModel
                    };
                }
                catch (Exception ex)
                {
                    // 初期化エラーの場合はフォールバック
                    desktop.MainWindow = new MainWindow
                    {
                        DataContext = new FallbackMainViewModel()
                    };
                    
                    // エラーログ
                    var logger = _services?.GetService<ILogger<App>>();
                    logger?.LogError(ex, "アプリケーション初期化に失敗しました");
                }
            }

            base.OnFrameworkInitializationCompleted();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _host?.Dispose();
            base.OnExit(e);
        }

        // 緊急時のサービス取得
        public static T? GetService<T>() where T : class
        {
            return (Current as App)?._services?.GetService<T>();
        }
    }
}

// MainWindow.axaml - メインウィンドウ統合
using Avalonia.Controls;
using Avalonia.Interactivity;
using Baketa.UI.ViewModels;

namespace Baketa.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // ウィンドウサイズとポジションの復元
            RestoreWindowState();
            
            // 閉じるイベントのハンドリング
            Closing += OnWindowClosing;
        }

        private void RestoreWindowState()
        {
            // 設定から前回のウィンドウ状態を復元
            var settingsService = App.GetService<ISettingsService>();
            if (settingsService != null)
            {
                var windowSettings = settingsService.GetWindowSettings();
                if (windowSettings != null)
                {
                    Width = windowSettings.Width;
                    Height = windowSettings.Height;
                    if (windowSettings.X.HasValue && windowSettings.Y.HasValue)
                    {
                        Position = new PixelPoint(windowSettings.X.Value, windowSettings.Y.Value);
                    }
                    WindowState = windowSettings.IsMaximized ? WindowState.Maximized : WindowState.Normal;
                }
            }
        }

        private async void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            // ウィンドウ状態の保存
            var settingsService = App.GetService<ISettingsService>();
            if (settingsService != null)
            {
                var windowSettings = new WindowSettings
                {
                    Width = Width,
                    Height = Height,
                    X = Position.X,
                    Y = Position.Y,
                    IsMaximized = WindowState == WindowState.Maximized
                };
                
                await settingsService.SaveWindowSettingsAsync(windowSettings);
            }

            // ViewModelのクリーンアップ
            if (DataContext is IDisposable disposableViewModel)
            {
                disposableViewModel.Dispose();
            }
        }
    }
}

// MainViewModel.cs - メインビューモデル統合
using Baketa.UI.ViewModels.Settings;
using ReactiveUI;
using System.Reactive;

namespace Baketa.UI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly INavigationService _navigationService;
        private readonly INotificationService _notificationService;
        private ViewModelBase _currentViewModel;
        private bool _isTranslationSettingsVisible;

        public MainViewModel(
            INavigationService navigationService,
            INotificationService notificationService,
            TranslationSettingsViewModel translationSettingsViewModel)
        {
            _navigationService = navigationService;
            _notificationService = notificationService;
            
            TranslationSettings = translationSettingsViewModel;
            _currentViewModel = new DashboardViewModel(); // デフォルト画面
            
            // コマンドの初期化
            ShowTranslationSettingsCommand = ReactiveCommand.Create(ShowTranslationSettings);
            ShowDashboardCommand = ReactiveCommand.Create(ShowDashboard);
            ShowOcrSettingsCommand = ReactiveCommand.Create(ShowOcrSettings);
            
            // 通知の監視
            SetupNotifications();
        }

        public TranslationSettingsViewModel TranslationSettings { get; }

        public ViewModelBase CurrentViewModel
        {
            get => _currentViewModel;
            private set => this.RaiseAndSetIfChanged(ref _currentViewModel, value);
        }

        public bool IsTranslationSettingsVisible
        {
            get => _isTranslationSettingsVisible;
            set => this.RaiseAndSetIfChanged(ref _isTranslationSettingsVisible, value);
        }

        public ReactiveCommand<Unit, Unit> ShowTranslationSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowDashboardCommand { get; }
        public ReactiveCommand<Unit, Unit> ShowOcrSettingsCommand { get; }

        private void ShowTranslationSettings()
        {
            CurrentViewModel = TranslationSettings;
            IsTranslationSettingsVisible = true;
            _navigationService.NavigateTo("TranslationSettings");
        }

        private void ShowDashboard()
        {
            CurrentViewModel = new DashboardViewModel();
            IsTranslationSettingsVisible = false;
            _navigationService.NavigateTo("Dashboard");
        }

        private void ShowOcrSettings()
        {
            // OCR設定画面への遷移（将来実装）
            _notificationService.ShowInfo("OCR設定", "OCR設定は今後実装予定です。");
        }

        private void SetupNotifications()
        {
            // 翻訳設定の保存成功通知
            TranslationSettings.SaveSettingsCommand.Subscribe(_ =>
            {
                _notificationService.ShowSuccess("設定保存", "翻訳設定を保存しました。");
            });

            // エラー通知の監視
            TranslationSettings.SaveSettingsCommand.ThrownExceptions.Subscribe(ex =>
            {
                _notificationService.ShowError("設定保存エラー", $"設定の保存に失敗しました: {ex.Message}");
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                TranslationSettings?.Dispose();
                _currentViewModel?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

// INavigationService.cs - ナビゲーションサービス
namespace Baketa.UI.Services
{
    public interface INavigationService
    {
        void NavigateTo(string viewName);
        void NavigateBack();
        bool CanNavigateBack { get; }
        event EventHandler<NavigationEventArgs> Navigated;
    }

    public class NavigationEventArgs : EventArgs
    {
        public string ViewName { get; set; } = string.Empty;
        public object? Parameter { get; set; }
    }

    public class NavigationService : INavigationService
    {
        private readonly Stack<string> _navigationStack = new();

        public bool CanNavigateBack => _navigationStack.Count > 1;
        public event EventHandler<NavigationEventArgs>? Navigated;

        public void NavigateTo(string viewName)
        {
            _navigationStack.Push(viewName);
            Navigated?.Invoke(this, new NavigationEventArgs { ViewName = viewName });
        }

        public void NavigateBack()
        {
            if (CanNavigateBack)
            {
                _navigationStack.Pop();
                var previousView = _navigationStack.Peek();
                Navigated?.Invoke(this, new NavigationEventArgs { ViewName = previousView });
            }
        }
    }
}

// INotificationService.cs - 通知サービス統合
using Avalonia.Controls.Notifications;

namespace Baketa.UI.Services
{
    public interface INotificationService
    {
        void ShowSuccess(string title, string message);
        void ShowInfo(string title, string message);
        void ShowWarning(string title, string message);
        void ShowError(string title, string message);
    }

    public class AvaloniaNotificationService : INotificationService
    {
        private readonly WindowNotificationManager _notificationManager;

        public AvaloniaNotificationService()
        {
            // メインウィンドウが利用可能になってから初期化
            _notificationManager = new WindowNotificationManager(GetMainWindow())
            {
                Position = NotificationPosition.BottomRight,
                MaxItems = 3
            };
        }

        public void ShowSuccess(string title, string message)
        {
            _notificationManager.Show(new Notification(title, message, NotificationType.Success));
        }

        public void ShowInfo(string title, string message)
        {
            _notificationManager.Show(new Notification(title, message, NotificationType.Information));
        }

        public void ShowWarning(string title, string message)
        {
            _notificationManager.Show(new Notification(title, message, NotificationType.Warning));
        }

        public void ShowError(string title, string message)
        {
            _notificationManager.Show(new Notification(title, message, NotificationType.Error));
        }

        private static Window GetMainWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow ?? throw new InvalidOperationException("メインウィンドウが見つかりません");
            }
            throw new InvalidOperationException("デスクトップアプリケーションではありません");
        }
    }
}

// WindowSettings.cs - ウィンドウ状態管理
namespace Baketa.UI.Models
{
    public class WindowSettings
    {
        public double Width { get; set; } = 1000;
        public double Height { get; set; } = 700;
        public int? X { get; set; }
        public int? Y { get; set; }
        public bool IsMaximized { get; set; }
    }
}

// UIServiceCollectionExtensions.cs - UI DI設定完全版
using Baketa.UI.Services;
using Baketa.UI.ViewModels;
using Baketa.UI.ViewModels.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.UI.Extensions
{
    public static class UIServiceCollectionExtensions
    {
        public static IServiceCollection AddTranslationSettingsUI(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // ViewModels の登録
            services.AddSingleton<MainViewModel>();
            services.AddTransient<TranslationSettingsViewModel>();
            services.AddTransient<EngineSelectionViewModel>();
            services.AddTransient<LanguagePairSelectionViewModel>();
            services.AddTranslationStrategyViewModel>();
            services.AddTransient<EngineStatusViewModel>();
            
            // UI専用サービスの登録
            services.AddSingleton<IUserPlanService, UserPlanService>();
            services.AddSingleton<ILocalizationService, LocalizationService>();
            services.AddSingleton<INavigationService, NavigationService>();
            
            // 設定サービス（既存のISettingsServiceを拡張）
            services.AddSingleton<ISettingsService>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<SettingsService>>();
                return new SettingsService(logger);
            });

            // アダプターサービス
            services.AddSingleton<TranslationEngineStatusAdapter>();
            
            // 設定オプション
            services.Configure<TranslationUIOptions>(
                configuration.GetSection("TranslationUI"));
            services.Configure<UserPlanOptions>(
                configuration.GetSection("UserPlan"));

            return services;
        }
    }
}

// TranslationUIOptions.cs - UI設定オプション
namespace Baketa.UI.Configuration
{
    public class TranslationUIOptions
    {
        public string DefaultEngine { get; set; } = "LocalOnly";
        public bool ShowEnginePerformanceMetrics { get; set; } = true;
        public int StatusUpdateInterval { get; set; } = 30000; // 30秒
        public int MaxFallbackHistoryItems { get; set; } = 20;
        public bool AutoSaveSettings { get; set; } = true;
        public bool EnableNotifications { get; set; } = true;
        public string NotificationPosition { get; set; } = "BottomRight";
        public int NotificationTimeout { get; set; } = 5000; // 5秒
    }

    public class UserPlanOptions
    {
        public string Type { get; set; } = "Free";
        public string? LicenseKey { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public bool EnableCloudAccess { get; set; } = false;
    }
}

// ErrorRecoveryService.cs - エラー回復サービス
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services
{
    public interface IErrorRecoveryService
    {
        Task<bool> TryRecoverFromError(Exception error, string context);
        void RegisterRecoveryStrategy<TException>(Func<TException, string, Task<bool>> strategy)
            where TException : Exception;
    }

    public class ErrorRecoveryService : IErrorRecoveryService
    {
        private readonly ILogger<ErrorRecoveryService> _logger;
        private readonly INotificationService _notificationService;
        private readonly Dictionary<Type, Func<Exception, string, Task<bool>>> _strategies;

        public ErrorRecoveryService(
            ILogger<ErrorRecoveryService> logger,
            INotificationService notificationService)
        {
            _logger = logger;
            _notificationService = notificationService;
            _strategies = new Dictionary<Type, Func<Exception, string, Task<bool>>>();
            
            RegisterDefaultStrategies();
        }

        public async Task<bool> TryRecoverFromError(Exception error, string context)
        {
            _logger.LogWarning(error, "エラー回復を試行中: {Context}", context);

            if (_strategies.TryGetValue(error.GetType(), out var strategy))
            {
                try
                {
                    var recovered = await strategy(error, context);
                    if (recovered)
                    {
                        _logger.LogInformation("エラーから回復しました: {Context}", context);
                        _notificationService.ShowSuccess("回復", "エラーから回復しました");
                        return true;
                    }
                }
                catch (Exception recoveryError)
                {
                    _logger.LogError(recoveryError, "エラー回復中に追加エラーが発生: {Context}", context);
                }
            }

            _logger.LogError(error, "エラー回復に失敗: {Context}", context);
            return false;
        }

        public void RegisterRecoveryStrategy<TException>(Func<TException, string, Task<bool>> strategy)
            where TException : Exception
        {
            _strategies[typeof(TException)] = (ex, ctx) => strategy((TException)ex, ctx);
        }

        private void RegisterDefaultStrategies()
        {
            // ネットワークエラーの回復戦略
            RegisterRecoveryStrategy<HttpRequestException>(async (ex, context) =>
            {
                // ネットワーク接続の確認と再試行
                await Task.Delay(1000);
                return await CheckNetworkConnectivity();
            });

            // 設定エラーの回復戦略
            RegisterRecoveryStrategy<InvalidOperationException>(async (ex, context) =>
            {
                if (context.Contains("Settings"))
                {
                    // 設定をデフォルトにリセット
                    var settingsService = App.GetService<ISettingsService>();
                    if (settingsService != null)
                    {
                        await settingsService.ResetTranslationSettingsAsync();
                        return true;
                    }
                }
                return false;
            });

            // ファイルアクセスエラーの回復戦略
            RegisterRecoveryStrategy<UnauthorizedAccessException>(async (ex, context) =>
            {
                // 代替パスでの保存を試行
                return await TryAlternativePath(context);
            });
        }

        private async Task<bool> CheckNetworkConnectivity()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = await client.GetAsync("https://www.google.com");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> TryAlternativePath(string context)
        {
            try
            {
                var tempPath = Path.GetTempPath();
                var backupPath = Path.Combine(tempPath, "Baketa", "backup");
                Directory.CreateDirectory(backupPath);
                
                _notificationService.ShowWarning("設定保存", 
                    $"通常の場所に保存できませんでした。一時的に {backupPath} に保存します。");
                
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

// HealthCheckService.cs - ヘルスチェックサービス
namespace Baketa.UI.Services
{
    public interface IHealthCheckService
    {
        Task<HealthStatus> CheckApplicationHealthAsync();
        Task<HealthStatus> CheckTranslationEnginesHealthAsync();
        Task<HealthStatus> CheckConfigurationHealthAsync();
    }

    public class HealthCheckService : IHealthCheckService
    {
        private readonly ITranslationEngineStatusService _statusService;
        private readonly ISettingsService _settingsService;
        private readonly ILogger<HealthCheckService> _logger;

        public HealthCheckService(
            ITranslationEngineStatusService statusService,
            ISettingsService settingsService,
            ILogger<HealthCheckService> logger)
        {
            _statusService = statusService;
            _settingsService = settingsService;
            _logger = logger;
        }

        public async Task<HealthStatus> CheckApplicationHealthAsync()
        {
            var engineHealth = await CheckTranslationEnginesHealthAsync();
            var configHealth = await CheckConfigurationHealthAsync();

            if (engineHealth == HealthStatus.Healthy && configHealth == HealthStatus.Healthy)
                return HealthStatus.Healthy;
            
            if (engineHealth == HealthStatus.Unhealthy || configHealth == HealthStatus.Unhealthy)
                return HealthStatus.Unhealthy;
                
            return HealthStatus.Degraded;
        }

        public async Task<HealthStatus> CheckTranslationEnginesHealthAsync()
        {
            try
            {
                var status = _statusService.GetCurrentStatus();
                
                var localHealthy = status.LocalOnlyStatus.Health == EngineHealth.Healthy;
                var cloudHealthy = status.CloudOnlyStatus.Health == EngineHealth.Healthy;

                if (localHealthy && cloudHealthy)
                    return HealthStatus.Healthy;
                
                if (localHealthy || cloudHealthy)
                    return HealthStatus.Degraded;
                
                return HealthStatus.Unhealthy;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "翻訳エンジンのヘルスチェックに失敗");
                return HealthStatus.Unhealthy;
            }
        }

        public async Task<HealthStatus> CheckConfigurationHealthAsync()
        {
            try
            {
                var settings = await _settingsService.GetTranslationSettingsAsync();
                
                // 設定の妥当性検証
                var validation = ValidationHelper.ValidateTranslationSettings(settings);
                
                return validation.IsValid ? HealthStatus.Healthy : HealthStatus.Degraded;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "設定のヘルスチェックに失敗");
                return HealthStatus.Unhealthy;
            }
        }
    }

    public enum HealthStatus
    {
        Healthy,
        Degraded,
        Unhealthy
    }
}

// FallbackMainViewModel.cs - 緊急時フォールバック
namespace Baketa.UI.ViewModels
{
    public class FallbackMainViewModel : ViewModelBase
    {
        public FallbackMainViewModel()
        {
            ErrorMessage = "アプリケーションの初期化に失敗しました。基本機能のみ利用可能です。";
            RestartCommand = ReactiveCommand.Create(RestartApplication);
        }

        [Reactive] public string ErrorMessage { get; set; }
        public ReactiveCommand<Unit, Unit> RestartCommand { get; }

        private void RestartApplication()
        {
            // アプリケーションの再起動
            var processInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? "Baketa.exe",
                UseShellExecute = true
            };
            
            Process.Start(processInfo);
            Environment.Exit(0);
        }
    }
}