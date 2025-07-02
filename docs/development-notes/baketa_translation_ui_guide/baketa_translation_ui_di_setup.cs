// UIServiceCollectionExtensions.cs - UI層のDI設定
using Baketa.UI.Services;
using Baketa.UI.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.UI.Extensions
{
    public static class UIServiceCollectionExtensions
    {
        public static IServiceCollection AddTranslationSettingsUI(
            this IServiceCollection services)
        {
            // ViewModels の登録
            services.AddTransient<TranslationSettingsViewModel>();
            services.AddTransient<EngineSelectionViewModel>();
            services.AddTransient<LanguagePairSelectionViewModel>();
            services.AddTransient<TranslationStrategyViewModel>();
            services.AddTransient<EngineStatusViewModel>();

            // UI専用サービスの登録
            services.AddSingleton<IUserPlanService, UserPlanService>();
            services.AddSingleton<ILocalizationService, LocalizationService>();
            services.AddSingleton<ISettingsService, SettingsService>();

            return services;
        }
    }
}

// UserPlanService.cs - ユーザープラン管理サービス実装
using Baketa.UI.Services;

namespace Baketa.UI.Services
{
    public class UserPlanService : IUserPlanService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<UserPlanService> _logger;
        private UserPlan _currentPlan;

        public UserPlanService(
            IConfiguration configuration,
            ILogger<UserPlanService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _currentPlan = LoadCurrentPlan();
        }

        public bool HasCloudAccess => _currentPlan.HasCloudAccess;

        public UserPlan GetCurrentPlan()
        {
            return _currentPlan;
        }

        public async Task<bool> UpgradeAsync()
        {
            try
            {
                // 実際の実装では、ライセンス認証やAPI呼び出しを行う
                // 開発時は設定ファイルからプラン情報を読み込む
                var newPlanType = _configuration.GetValue<string>("UserPlan:Type", "Free");
                
                if (Enum.TryParse<PlanType>(newPlanType, out var planType))
                {
                    _currentPlan = new UserPlan
                    {
                        Type = planType,
                        DisplayName = GetPlanDisplayName(planType),
                        HasCloudAccess = planType != PlanType.Free,
                        ExpiryDate = planType == PlanType.Free ? null : DateTime.Now.AddYears(1)
                    };

                    _logger.LogInformation("プランを {PlanType} にアップグレードしました", planType);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "プランアップグレードに失敗しました");
                return false;
            }
        }

        private UserPlan LoadCurrentPlan()
        {
            // 開発時は設定ファイルから、本番では実際のライセンス情報から読み込み
            var planTypeString = _configuration.GetValue<string>("UserPlan:Type", "Free");
            
            if (Enum.TryParse<PlanType>(planTypeString, out var planType))
            {
                return new UserPlan
                {
                    Type = planType,
                    DisplayName = GetPlanDisplayName(planType),
                    HasCloudAccess = planType != PlanType.Free,
                    ExpiryDate = planType == PlanType.Free ? null : DateTime.Now.AddMonths(1)
                };
            }

            // デフォルトは無料プラン
            return new UserPlan
            {
                Type = PlanType.Free,
                DisplayName = "Free",
                HasCloudAccess = false,
                ExpiryDate = null
            };
        }

        private string GetPlanDisplayName(PlanType type) => type switch
        {
            PlanType.Free => "Free",
            PlanType.Premium => "Premium",
            PlanType.Enterprise => "Enterprise",
            _ => "Unknown"
        };
    }
}

// LocalizationService.cs - 言語設定サービス実装
using System.Globalization;
using Baketa.UI.Services;

namespace Baketa.UI.Services
{
    public class LocalizationService : ILocalizationService
    {
        private CultureInfo _currentCulture;

        public LocalizationService()
        {
            _currentCulture = CultureInfo.CurrentUICulture;
            
            // システムの言語変更を監視（簡易実装）
            // 実際の実装では、より詳細な監視機能が必要
        }

        public CultureInfo CurrentCulture => _currentCulture;

        public event EventHandler<CultureChangedEventArgs>? CultureChanged;

        public void ChangeCulture(CultureInfo newCulture)
        {
            var oldCulture = _currentCulture;
            _currentCulture = newCulture;

            CultureChanged?.Invoke(this, new CultureChangedEventArgs
            {
                OldCulture = oldCulture,
                NewCulture = newCulture
            });
        }

        public string GetTargetLanguageCode()
        {
            return _currentCulture.TwoLetterISOLanguageName;
        }

        public string GetTargetLanguageDisplayName()
        {
            return _currentCulture.DisplayName;
        }
    }
}

// SettingsService.cs - 設定管理サービス実装
using Baketa.Core.Translation.Models;
using System.Text.Json;

namespace Baketa.UI.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly ILogger<SettingsService> _logger;
        private readonly string _settingsFilePath;
        private TranslationSettings _currentSettings;

        public SettingsService(ILogger<SettingsService> logger)
        {
            _logger = logger;
            _settingsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Baketa",
                "translation-settings.json");

            _currentSettings = LoadSettingsFromFile();
        }

        public async Task<TranslationSettings> GetTranslationSettingsAsync()
        {
            return _currentSettings;
        }

        public async Task SaveTranslationSettingsAsync(TranslationSettings settings)
        {
            try
            {
                _currentSettings = settings;
                await SaveSettingsToFileAsync(settings);
                _logger.LogInformation("翻訳設定を保存しました");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "翻訳設定の保存に失敗しました");
                throw;
            }
        }

        public async Task ResetTranslationSettingsAsync()
        {
            _currentSettings = GetDefaultSettings();
            await SaveSettingsToFileAsync(_currentSettings);
            _logger.LogInformation("翻訳設定をデフォルトにリセットしました");
        }

        private TranslationSettings LoadSettingsFromFile()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<TranslationSettings>(json);
                    return settings ?? GetDefaultSettings();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "設定ファイルの読み込みに失敗しました。デフォルト設定を使用します");
            }

            return GetDefaultSettings();
        }

        private async Task SaveSettingsToFileAsync(TranslationSettings settings)
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(_settingsFilePath, json);
        }

        private TranslationSettings GetDefaultSettings()
        {
            return new TranslationSettings
            {
                SelectedEngine = TranslationEngine.LocalOnly,
                EnabledLanguagePairs = new List<string> { "ja-en", "en-ja" },
                ChineseVariant = ChineseVariant.Simplified,
                TranslationStrategy = TranslationStrategy.Direct,
                EnableFallback = true
            };
        }
    }

    public interface ISettingsService
    {
        Task<TranslationSettings> GetTranslationSettingsAsync();
        Task SaveTranslationSettingsAsync(TranslationSettings settings);
        Task ResetTranslationSettingsAsync();
    }
}

// TranslationEngineStatusService統合例
// 既存のTranslationEngineStatusServiceを活用する場合の統合例
using Baketa.UI.Services;
using Baketa.Core.Translation.Models;
using System.Reactive.Subjects;

namespace Baketa.UI.Services
{
    public class TranslationEngineStatusAdapter
    {
        private readonly ITranslationEngineStatusService _statusService;
        private readonly Subject<TranslationEngineStatus> _statusSubject;
        private readonly Timer _updateTimer;

        public TranslationEngineStatusAdapter(ITranslationEngineStatusService statusService)
        {
            _statusService = statusService;
            _statusSubject = new Subject<TranslationEngineStatus>();
            
            // 定期的な状態更新（30秒間隔）
            _updateTimer = new Timer(UpdateStatus, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        }

        public IObservable<TranslationEngineStatus> StatusUpdated => _statusSubject.AsObservable();

        public TranslationEngineStatus GetCurrentStatus()
        {
            // 既存のTranslationEngineStatusServiceから状態を取得し、
            // UI用のモデルに変換
            return new TranslationEngineStatus
            {
                LocalOnlyStatus = GetEngineStatusInfo(TranslationEngine.LocalOnly),
                CloudOnlyStatus = GetEngineStatusInfo(TranslationEngine.CloudOnly),
                LastUpdated = DateTime.Now
            };
        }

        private void UpdateStatus(object? state)
        {
            try
            {
                var status = GetCurrentStatus();
                _statusSubject.OnNext(status);
            }
            catch (Exception ex)
            {
                // ログ出力等のエラーハンドリング
            }
        }

        private EngineStatusInfo GetEngineStatusInfo(TranslationEngine engine)
        {
            // 既存のサービスから実際の状態を取得
            // この実装は既存のTranslationEngineStatusServiceの
            // インターフェースに依存
            
            return new EngineStatusInfo
            {
                Health = EngineHealth.Healthy, // 実際の状態から変換
                AverageLatencyMs = 45.0, // 実際の測定値
                SuccessRate = 0.95, // 実際の成功率
                IsAvailable = true, // 実際の利用可能性
                LastError = string.Empty
            };
        }
    }
}

// App.axaml.cs での DI設定統合例
using Baketa.UI.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Baketa.UI
{
    public partial class App : Application
    {
        private IHost? _host;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            
            // DI コンテナの設定
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // 既存のサービス登録
                    services.AddSentencePieceTokenizer(context.Configuration);
                    services.AddChineseTranslationSupport(context.Configuration);
                    services.AddCompleteTranslationServices(context.Configuration);
                    
                    // UI層の新規サービス登録
                    services.AddTranslationSettingsUI();
                    
                    // アダプターサービスの登録
                    services.AddSingleton<TranslationEngineStatusAdapter>();
                })
                .Build();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // メインウィンドウの作成
                var mainViewModel = _host!.Services.GetRequiredService<MainViewModel>();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainViewModel
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}

// appsettings.json 設定例
/*
{
  "UserPlan": {
    "Type": "Free",  // "Free", "Premium", "Enterprise"
    "LicenseKey": "",
    "ExpiryDate": null
  },
  "TranslationUI": {
    "DefaultEngine": "LocalOnly",
    "ShowEnginePerformanceMetrics": true,
    "StatusUpdateInterval": 30000,
    "MaxFallbackHistoryItems": 20,
    "AutoSaveSettings": true
  },
  "Translation": {
    "DefaultStrategy": "Direct",
    "EnabledEngines": ["OPUS-MT", "Gemini", "Hybrid"],
    "LanguagePairs": {
      "ja-en": { "Engine": "OPUS-MT", "ModelName": "opus-mt-ja-en", "Priority": 1 },
      "en-ja": { "Engine": "OPUS-MT", "ModelName": "opus-mt-en-ja", "Priority": 1 },
      "zh-en": { "Engine": "OPUS-MT", "ModelName": "opus-mt-zh-en", "Priority": 2 },
      "en-zh": { "Engine": "OPUS-MT", "ModelName": "opus-mt-en-zh", "Priority": 2 },
      "zh-ja": { "Engine": "OPUS-MT", "ModelName": "opus-mt-tc-big-zh-ja", "Priority": 2 },
      "ja-zh": { "Engine": "TwoStage", "FirstStage": "opus-mt-ja-en", "SecondStage": "opus-mt-en-zh", "Priority": 3 }
    }
  },
  "HybridTranslation": {
    "ShortTextThreshold": 50,
    "LongTextThreshold": 500,
    "DefaultStrategy": "LocalOnly",
    "EnableRateLimitFallback": true,
    "EnableNetworkErrorFallback": true,
    "EnableApiErrorFallback": true,
    "FallbackTimeoutSeconds": 10,
    "RecoveryCheckIntervalMinutes": 5,
    "ShowFallbackNotifications": true
  }
}
*/

// MainWindow.axaml での設定画面統合例
/*
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:Baketa.UI.ViewModels"
        xmlns:views="using:Baketa.UI.Views.Settings"
        x:Class="Baketa.UI.MainWindow"
        x:DataType="vm:MainViewModel"
        Title="Baketa Translation Tool"
        Width="800" Height="600">

  <Grid RowDefinitions="Auto,*">
    
    <!-- メニューバー -->
    <Menu Grid.Row="0">
      <MenuItem Header="設定">
        <MenuItem Header="翻訳設定" Command="{Binding ShowTranslationSettingsCommand}"/>
        <MenuItem Header="OCR設定" Command="{Binding ShowOcrSettingsCommand}"/>
      </MenuItem>
    </Menu>
    
    <!-- メインコンテンツ -->
    <TabControl Grid.Row="1">
      
      <TabItem Header="翻訳">
        <!-- 既存の翻訳UI -->
      </TabItem>
      
      <TabItem Header="設定">
        <views:TranslationSettingsView DataContext="{Binding TranslationSettings}"/>
      </TabItem>
      
    </TabControl>
    
  </Grid>
</Window>
*/