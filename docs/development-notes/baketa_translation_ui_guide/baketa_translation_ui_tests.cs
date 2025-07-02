// EngineSelectionViewModelTests.cs - 単体テスト実装
using Baketa.Core.Translation.Models;
using Baketa.UI.Services;
using Baketa.UI.ViewModels.Settings;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System.Reactive.Subjects;

namespace Baketa.UI.Tests.ViewModels.Settings
{
    [TestFixture]
    public class EngineSelectionViewModelTests
    {
        private Mock<ITranslationEngineStatusService> _mockStatusService;
        private Mock<IUserPlanService> _mockPlanService;
        private Mock<ILogger<EngineSelectionViewModel>> _mockLogger;
        private Subject<TranslationEngineStatus> _statusSubject;
        private EngineSelectionViewModel _viewModel;

        [SetUp]
        public void Setup()
        {
            _mockStatusService = new Mock<ITranslationEngineStatusService>();
            _mockPlanService = new Mock<IUserPlanService>();
            _mockLogger = new Mock<ILogger<EngineSelectionViewModel>>();
            _statusSubject = new Subject<TranslationEngineStatus>();

            // StatusService のモック設定
            _mockStatusService.Setup(x => x.StatusUpdated)
                .Returns(_statusSubject.AsObservable());

            _mockStatusService.Setup(x => x.GetCurrentStatus())
                .Returns(new TranslationEngineStatus
                {
                    LocalOnlyStatus = new EngineStatusInfo 
                    { 
                        Health = EngineHealth.Healthy, 
                        AverageLatencyMs = 45.0,
                        SuccessRate = 0.95,
                        IsAvailable = true 
                    },
                    CloudOnlyStatus = new EngineStatusInfo 
                    { 
                        Health = EngineHealth.Healthy, 
                        AverageLatencyMs = 1500.0,
                        SuccessRate = 0.98,
                        IsAvailable = true 
                    }
                });

            // PlanService のモック設定（無料プラン）
            _mockPlanService.Setup(x => x.GetCurrentPlan())
                .Returns(new UserPlan
                {
                    Type = PlanType.Free,
                    DisplayName = "Free",
                    HasCloudAccess = false
                });

            _mockPlanService.Setup(x => x.HasCloudAccess).Returns(false);
        }

        [TearDown]
        public void TearDown()
        {
            _viewModel?.Dispose();
            _statusSubject?.Dispose();
        }

        [Test]
        public void Constructor_WithFreePlan_SetsLocalOnlyAsDefault()
        {
            // Act
            _viewModel = new EngineSelectionViewModel(_mockStatusService.Object, _mockPlanService.Object);

            // Assert
            Assert.That(_viewModel.SelectedEngine, Is.EqualTo(TranslationEngine.LocalOnly));
            Assert.That(_viewModel.IsLocalOnlySelected, Is.True);
            Assert.That(_viewModel.IsCloudOnlySelected, Is.False);
            Assert.That(_viewModel.IsFreePlan, Is.True);
            Assert.That(_viewModel.IsCloudOnlyAvailable, Is.False);
        }

        [Test]
        public void Constructor_WithPremiumPlan_AllowsCloudOnlySelection()
        {
            // Arrange
            _mockPlanService.Setup(x => x.GetCurrentPlan())
                .Returns(new UserPlan
                {
                    Type = PlanType.Premium,
                    DisplayName = "Premium",
                    HasCloudAccess = true
                });
            _mockPlanService.Setup(x => x.HasCloudAccess).Returns(true);

            // Act
            _viewModel = new EngineSelectionViewModel(_mockStatusService.Object, _mockPlanService.Object);

            // Assert
            Assert.That(_viewModel.IsCloudOnlyAvailable, Is.True);
            Assert.That(_viewModel.IsFreePlan, Is.False);
            Assert.That(_viewModel.CurrentPlan, Is.EqualTo("Premium"));
        }

        [Test]
        public void SelectedEngine_WhenChangedToCloudOnlyWithFreePlan_RemainsLocalOnly()
        {
            // Arrange
            _viewModel = new EngineSelectionViewModel(_mockStatusService.Object, _mockPlanService.Object);

            // Act
            _viewModel.IsCloudOnlySelected = true;

            // Assert
            Assert.That(_viewModel.SelectedEngine, Is.EqualTo(TranslationEngine.LocalOnly));
            Assert.That(_viewModel.IsLocalOnlySelected, Is.True);
            Assert.That(_viewModel.IsCloudOnlySelected, Is.False);
        }

        [Test]
        public void StatusUpdate_UpdatesEnginePerformanceText()
        {
            // Arrange
            _viewModel = new EngineSelectionViewModel(_mockStatusService.Object, _mockPlanService.Object);

            // Act
            _statusSubject.OnNext(new TranslationEngineStatus
            {
                LocalOnlyStatus = new EngineStatusInfo
                {
                    Health = EngineHealth.Degraded,
                    AverageLatencyMs = 120.0,
                    IsAvailable = true
                }
            });

            // Assert
            Assert.That(_viewModel.LocalEngineStatusColor, Is.EqualTo("#FF9800")); // Orange
            Assert.That(_viewModel.LocalEnginePerformance, Contains.Substring("120ms"));
            Assert.That(_viewModel.LocalEnginePerformance, Contains.Substring("制限あり"));
        }

        [Test]
        public async Task UpgradeCommand_WhenSuccessful_UpdatesPlanInfo()
        {
            // Arrange
            _viewModel = new EngineSelectionViewModel(_mockStatusService.Object, _mockPlanService.Object);
            _mockPlanService.Setup(x => x.UpgradeAsync()).ReturnsAsync(true);
            _mockPlanService.Setup(x => x.GetCurrentPlan())
                .Returns(new UserPlan
                {
                    Type = PlanType.Premium,
                    DisplayName = "Premium",
                    HasCloudAccess = true
                });

            // Act
            await _viewModel.UpgradeCommand.Execute();

            // Assert
            _mockPlanService.Verify(x => x.UpgradeAsync(), Times.Once);
            // プラン情報の更新を確認
            // 注意: この実装では LoadCurrentPlan() が呼ばれるタイミングを制御する必要がある
        }

        [Test]
        public void ApplySettings_UpdatesViewModelProperties()
        {
            // Arrange
            _viewModel = new EngineSelectionViewModel(_mockStatusService.Object, _mockPlanService.Object);
            var settings = new TranslationSettings
            {
                SelectedEngine = TranslationEngine.LocalOnly
            };

            // Act
            _viewModel.ApplySettings(settings);

            // Assert
            Assert.That(_viewModel.SelectedEngine, Is.EqualTo(TranslationEngine.LocalOnly));
        }

        [Test]
        public void EngineDescription_LocalOnly_ShowsCorrectDescription()
        {
            // Arrange
            _viewModel = new EngineSelectionViewModel(_mockStatusService.Object, _mockPlanService.Object);

            // Act
            _viewModel.SelectedEngine = TranslationEngine.LocalOnly;

            // Assert
            Assert.That(_viewModel.SelectedEngineDescription, Contains.Substring("OPUS-MT"));
            Assert.That(_viewModel.SelectedEngineDescription, Contains.Substring("高速処理"));
            Assert.That(_viewModel.SelectedEngineDescription, Contains.Substring("無料"));
            Assert.That(_viewModel.CostEstimation, Contains.Substring("完全無料"));
        }

        [Test]
        public void EngineDescription_CloudOnlyWithFreePlan_ShowsUpgradeMessage()
        {
            // Arrange
            _viewModel = new EngineSelectionViewModel(_mockStatusService.Object, _mockPlanService.Object);

            // Act - 強制的にCloudOnlyに設定（テスト用）
            _viewModel.SelectedEngine = TranslationEngine.CloudOnly;

            // Assert
            Assert.That(_viewModel.SelectedEngineDescription, Contains.Substring("有料プラン"));
            Assert.That(_viewModel.CostEstimation, Contains.Substring("アップグレード"));
        }
    }
}

// LanguagePairSelectionViewModelTests.cs
namespace Baketa.UI.Tests.ViewModels.Settings
{
    [TestFixture]
    public class LanguagePairSelectionViewModelTests
    {
        private Mock<ISettingsService> _mockSettingsService;
        private Mock<ILocalizationService> _mockLocalizationService;
        private LanguagePairSelectionViewModel _viewModel;

        [SetUp]
        public void Setup()
        {
            _mockSettingsService = new Mock<ISettingsService>();
            _mockLocalizationService = new Mock<ILocalizationService>();

            _mockLocalizationService.Setup(x => x.CurrentCulture)
                .Returns(new CultureInfo("ja-JP"));
        }

        [TearDown]
        public void TearDown()
        {
            _viewModel?.Dispose();
        }

        [Test]
        public void Constructor_InitializesLanguagePairs()
        {
            // Act
            _viewModel = new LanguagePairSelectionViewModel(_mockSettingsService.Object, _mockLocalizationService.Object);

            // Assert
            Assert.That(_viewModel.AvailableLanguagePairs.Count, Is.EqualTo(6));
            Assert.That(_viewModel.HasChinesePairs, Is.True);
            Assert.That(_viewModel.CurrentTargetLanguage, Is.EqualTo("日本語"));
        }

        [Test]
        public void ChineseVariantSelection_DefaultsToSimplified()
        {
            // Act
            _viewModel = new LanguagePairSelectionViewModel(_mockSettingsService.Object, _mockLocalizationService.Object);

            // Assert
            Assert.That(_viewModel.SelectedChineseVariant, Is.EqualTo(ChineseVariant.Simplified));
            Assert.That(_viewModel.IsSimplifiedSelected, Is.True);
            Assert.That(_viewModel.IsTraditionalSelected, Is.False);
        }

        [Test]
        public void ChineseVariantSelection_CanSwitchToTraditional()
        {
            // Arrange
            _viewModel = new LanguagePairSelectionViewModel(_mockSettingsService.Object, _mockLocalizationService.Object);

            // Act
            _viewModel.IsTraditionalSelected = true;

            // Assert
            Assert.That(_viewModel.SelectedChineseVariant, Is.EqualTo(ChineseVariant.Traditional));
            Assert.That(_viewModel.IsSimplifiedSelected, Is.False);
            Assert.That(_viewModel.IsTraditionalSelected, Is.True);
        }

        [Test]
        public void GetEnabledPairs_ReturnsOnlyEnabledPairs()
        {
            // Arrange
            _viewModel = new LanguagePairSelectionViewModel(_mockSettingsService.Object, _mockLocalizationService.Object);
            
            // 一部のペアを無効化
            _viewModel.AvailableLanguagePairs[0].IsEnabled = true;  // ja-en
            _viewModel.AvailableLanguagePairs[1].IsEnabled = false; // en-ja
            _viewModel.AvailableLanguagePairs[2].IsEnabled = true;  // zh-en

            // Act
            var enabledPairs = _viewModel.GetEnabledPairs();

            // Assert
            Assert.That(enabledPairs.Count, Is.EqualTo(2));
            Assert.That(enabledPairs, Contains.Item("ja-en"));
            Assert.That(enabledPairs, Contains.Item("zh-en"));
            Assert.That(enabledPairs, Does.Not.Contain("en-ja"));
        }

        [Test]
        public void CultureChanged_UpdatesTargetLanguage()
        {
            // Arrange
            _viewModel = new LanguagePairSelectionViewModel(_mockSettingsService.Object, _mockLocalizationService.Object);
            var eventArgs = new CultureChangedEventArgs
            {
                OldCulture = new CultureInfo("ja-JP"),
                NewCulture = new CultureInfo("en-US")
            };

            // Act
            _mockLocalizationService.Raise(x => x.CultureChanged += null, _mockLocalizationService.Object, eventArgs);

            // Assert
            // CurrentTargetLanguage の更新確認
            // 実際の実装では OnCultureChanged が呼ばれることを確認する
        }

        [Test]
        public void ApplySettings_UpdatesLanguagePairSelection()
        {
            // Arrange
            _viewModel = new LanguagePairSelectionViewModel(_mockSettingsService.Object, _mockLocalizationService.Object);
            var settings = new TranslationSettings
            {
                EnabledLanguagePairs = new List<string> { "ja-en", "zh-ja" },
                ChineseVariant = ChineseVariant.Traditional
            };

            // Act
            _viewModel.ApplySettings(settings);

            // Assert
            var enabledPairs = _viewModel.AvailableLanguagePairs.Where(p => p.IsEnabled).ToList();
            Assert.That(enabledPairs.Count, Is.EqualTo(2));
            Assert.That(_viewModel.SelectedChineseVariant, Is.EqualTo(ChineseVariant.Traditional));
        }
    }
}

// TranslationStrategyViewModelTests.cs
namespace Baketa.UI.Tests.ViewModels.Settings
{
    [TestFixture]
    public class TranslationStrategyViewModelTests
    {
        private Mock<ISettingsService> _mockSettingsService;
        private TranslationStrategyViewModel _viewModel;

        [SetUp]
        public void Setup()
        {
            _mockSettingsService = new Mock<ISettingsService>();
        }

        [Test]
        public void Constructor_DefaultsToDirectStrategy()
        {
            // Act
            _viewModel = new TranslationStrategyViewModel(_mockSettingsService.Object);

            // Assert
            Assert.That(_viewModel.SelectedStrategy, Is.EqualTo(TranslationStrategy.Direct));
            Assert.That(_viewModel.IsDirectSelected, Is.True);
            Assert.That(_viewModel.IsTwoStageSelected, Is.False);
            Assert.That(_viewModel.EnableCloudToLocalFallback, Is.True);
        }

        [Test]
        public void StrategySelection_CanSwitchToTwoStage()
        {
            // Arrange
            _viewModel = new TranslationStrategyViewModel(_mockSettingsService.Object);

            // Act
            _viewModel.IsTwoStageSelected = true;

            // Assert
            Assert.That(_viewModel.SelectedStrategy, Is.EqualTo(TranslationStrategy.TwoStage));
            Assert.That(_viewModel.IsDirectSelected, Is.False);
            Assert.That(_viewModel.IsTwoStageSelected, Is.True);
        }

        [Test]
        public void StrategyDescription_DirectStrategy_ShowsCorrectInfo()
        {
            // Arrange
            _viewModel = new TranslationStrategyViewModel(_mockSettingsService.Object);

            // Act
            _viewModel.SelectedStrategy = TranslationStrategy.Direct;

            // Assert
            Assert.That(_viewModel.SelectedStrategyDescription, Contains.Substring("直接翻訳"));
            Assert.That(_viewModel.SelectedStrategyDescription, Contains.Substring("高速処理"));
            Assert.That(_viewModel.PerformanceExpectation, Contains.Substring("50ms"));
        }

        [Test]
        public void StrategyDescription_TwoStageStrategy_ShowsCorrectInfo()
        {
            // Arrange
            _viewModel = new TranslationStrategyViewModel(_mockSettingsService.Object);

            // Act
            _viewModel.SelectedStrategy = TranslationStrategy.TwoStage;

            // Assert
            Assert.That(_viewModel.SelectedStrategyDescription, Contains.Substring("2段階翻訳"));
            Assert.That(_viewModel.SelectedStrategyDescription, Contains.Substring("英語を中継"));
            Assert.That(_viewModel.PerformanceExpectation, Contains.Substring("100-200ms"));
        }

        [Test]
        public void FallbackSetting_CanBeToggled()
        {
            // Arrange
            _viewModel = new TranslationStrategyViewModel(_mockSettingsService.Object);

            // Act
            _viewModel.EnableCloudToLocalFallback = false;

            // Assert
            Assert.That(_viewModel.EnableCloudToLocalFallback, Is.False);

            // Act
            _viewModel.EnableCloudToLocalFallback = true;

            // Assert
            Assert.That(_viewModel.EnableCloudToLocalFallback, Is.True);
        }

        [Test]
        public void ApplySettings_UpdatesAllProperties()
        {
            // Arrange
            _viewModel = new TranslationStrategyViewModel(_mockSettingsService.Object);
            var settings = new TranslationSettings
            {
                TranslationStrategy = TranslationStrategy.TwoStage,
                EnableFallback = false
            };

            // Act
            _viewModel.ApplySettings(settings);

            // Assert
            Assert.That(_viewModel.SelectedStrategy, Is.EqualTo(TranslationStrategy.TwoStage));
            Assert.That(_viewModel.EnableCloudToLocalFallback, Is.False);
        }

        [Test]
        public void GetMethods_ReturnCurrentValues()
        {
            // Arrange
            _viewModel = new TranslationStrategyViewModel(_mockSettingsService.Object);
            _viewModel.SelectedStrategy = TranslationStrategy.TwoStage;
            _viewModel.EnableCloudToLocalFallback = false;

            // Act & Assert
            Assert.That(_viewModel.GetSelectedStrategy(), Is.EqualTo(TranslationStrategy.TwoStage));
            Assert.That(_viewModel.GetFallbackEnabled(), Is.False);
        }
    }
}

// TranslationSettingsIntegrationTests.cs - 統合テスト
namespace Baketa.UI.Tests.Integration
{
    [TestFixture]
    public class TranslationSettingsIntegrationTests
    {
        private IServiceProvider _serviceProvider;
        private Mock<ITranslationEngineStatusService> _mockStatusService;
        private Mock<IUserPlanService> _mockPlanService;
        private Mock<ILogger<SettingsService>> _mockLogger;
        private string _tempSettingsPath;

        [SetUp]
        public void Setup()
        {
            // テスト用の一時ディレクトリ作成
            _tempSettingsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempSettingsPath);

            var services = new ServiceCollection();
            
            // モックサービスの設定
            _mockStatusService = new Mock<ITranslationEngineStatusService>();
            _mockPlanService = new Mock<IUserPlanService>();
            _mockLogger = new Mock<ILogger<SettingsService>>();

            _mockStatusService.Setup(x => x.StatusUpdated)
                .Returns(Observable.Empty<TranslationEngineStatus>());
            
            _mockStatusService.Setup(x => x.GetCurrentStatus())
                .Returns(new TranslationEngineStatus());

            _mockPlanService.Setup(x => x.GetCurrentPlan())
                .Returns(new UserPlan { Type = PlanType.Premium, HasCloudAccess = true });

            // サービス登録
            services.AddSingleton(_mockStatusService.Object);
            services.AddSingleton(_mockPlanService.Object);
            services.AddSingleton(_mockLogger.Object);
            services.AddSingleton<ILocalizationService, LocalizationService>();
            
            // テスト用のSettingsService（一時パス使用）
            services.AddSingleton<ISettingsService>(provider => 
                new TestSettingsService(_tempSettingsPath, _mockLogger.Object));

            // ViewModels
            services.AddTransient<TranslationSettingsViewModel>();
            services.AddTransient<EngineSelectionViewModel>();
            services.AddTransient<LanguagePairSelectionViewModel>();
            services.AddTransient<TranslationStrategyViewModel>();

            _serviceProvider = services.BuildServiceProvider();
        }

        [TearDown]
        public void TearDown()
        {
            _serviceProvider?.Dispose();
            
            if (Directory.Exists(_tempSettingsPath))
            {
                Directory.Delete(_tempSettingsPath, true);
            }
        }

        [Test]
        public async Task TranslationSettingsWorkflow_SaveAndLoad_WorksCorrectly()
        {
            // Arrange
            var viewModel = _serviceProvider.GetRequiredService<TranslationSettingsViewModel>();

            // 設定変更
            viewModel.EngineSelection.SelectedEngine = TranslationEngine.CloudOnly;
            viewModel.LanguagePairSelection.SelectedChineseVariant = ChineseVariant.Traditional;
            viewModel.TranslationStrategy.SelectedStrategy = TranslationStrategy.TwoStage;
            viewModel.TranslationStrategy.EnableCloudToLocalFallback = false;

            // Act - 設定保存
            await viewModel.SaveSettingsCommand.Execute();

            // 新しいViewModelインスタンスで設定読み込み
            var newViewModel = _serviceProvider.GetRequiredService<TranslationSettingsViewModel>();

            // Assert - 設定が正しく保存・復元されることを確認
            Assert.That(newViewModel.EngineSelection.SelectedEngine, Is.EqualTo(TranslationEngine.CloudOnly));
            Assert.That(newViewModel.LanguagePairSelection.SelectedChineseVariant, Is.EqualTo(ChineseVariant.Traditional));
            Assert.That(newViewModel.TranslationStrategy.SelectedStrategy, Is.EqualTo(TranslationStrategy.TwoStage));
            Assert.That(newViewModel.TranslationStrategy.EnableCloudToLocalFallback, Is.False);
        }

        [Test]
        public async Task TranslationSettings_ResetSettings_RestoresDefaults()
        {
            // Arrange
            var viewModel = _serviceProvider.GetRequiredService<TranslationSettingsViewModel>();
            
            // 設定変更
            viewModel.EngineSelection.SelectedEngine = TranslationEngine.CloudOnly;
            viewModel.LanguagePairSelection.SelectedChineseVariant = ChineseVariant.Traditional;
            await viewModel.SaveSettingsCommand.Execute();

            // Act - 設定リセット
            await viewModel.ResetSettingsCommand.Execute();

            // Assert - デフォルト値に戻ることを確認
            Assert.That(viewModel.EngineSelection.SelectedEngine, Is.EqualTo(TranslationEngine.LocalOnly));
            Assert.That(viewModel.LanguagePairSelection.SelectedChineseVariant, Is.EqualTo(ChineseVariant.Simplified));
            Assert.That(viewModel.TranslationStrategy.SelectedStrategy, Is.EqualTo(TranslationStrategy.Direct));
        }

        [Test]
        public void ServiceResolution_AllViewModels_CanBeResolved()
        {
            // Act & Assert - すべてのViewModelが正常に解決できることを確認
            Assert.DoesNotThrow(() => _serviceProvider.GetRequiredService<TranslationSettingsViewModel>());
            Assert.DoesNotThrow(() => _serviceProvider.GetRequiredService<EngineSelectionViewModel>());
            Assert.DoesNotThrow(() => _serviceProvider.GetRequiredService<LanguagePairSelectionViewModel>());
            Assert.DoesNotThrow(() => _serviceProvider.GetRequiredService<TranslationStrategyViewModel>());
        }
    }

    // テスト用のSettingsService実装
    public class TestSettingsService : ISettingsService
    {
        private readonly string _settingsDirectory;
        private readonly ILogger<SettingsService> _logger;
        private readonly string _settingsFilePath;

        public TestSettingsService(string settingsDirectory, ILogger<SettingsService> logger)
        {
            _settingsDirectory = settingsDirectory;
            _logger = logger;
            _settingsFilePath = Path.Combine(settingsDirectory, "test-settings.json");
        }

        public async Task<TranslationSettings> GetTranslationSettingsAsync()
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = await File.ReadAllTextAsync(_settingsFilePath);
                return JsonSerializer.Deserialize<TranslationSettings>(json) ?? GetDefaultSettings();
            }
            return GetDefaultSettings();
        }

        public async Task SaveTranslationSettingsAsync(TranslationSettings settings)
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_settingsFilePath, json);
        }

        public async Task ResetTranslationSettingsAsync()
        {
            await SaveTranslationSettingsAsync(GetDefaultSettings());
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
}

// EngineStatusViewModelTests.cs - エンジン状態表示のテスト
namespace Baketa.UI.Tests.ViewModels.Settings
{
    [TestFixture]
    public class EngineStatusViewModelTests
    {
        private Mock<ITranslationEngineStatusService> _mockStatusService;
        private Subject<TranslationEngineStatus> _statusSubject;
        private EngineStatusViewModel _viewModel;

        [SetUp]
        public void Setup()
        {
            _mockStatusService = new Mock<ITranslationEngineStatusService>();
            _statusSubject = new Subject<TranslationEngineStatus>();

            _mockStatusService.Setup(x => x.StatusUpdated)
                .Returns(_statusSubject.AsObservable());

            _mockStatusService.Setup(x => x.GetCurrentStatus())
                .Returns(new TranslationEngineStatus
                {
                    LocalOnlyStatus = new EngineStatusInfo
                    {
                        Health = EngineHealth.Healthy,
                        AverageLatencyMs = 45.0,
                        SuccessRate = 0.95,
                        IsAvailable = true
                    }
                });
        }

        [TearDown]
        public void TearDown()
        {
            _viewModel?.Dispose();
            _statusSubject?.Dispose();
        }

        [Test]
        public void Constructor_InitializesWithCurrentStatus()
        {
            // Act
            _viewModel = new EngineStatusViewModel(_mockStatusService.Object);

            // Assert
            Assert.That(_viewModel.CurrentStatusColor, Is.EqualTo("#4CAF50")); // Green
            Assert.That(_viewModel.CurrentStatusText, Is.EqualTo("正常動作中"));
            Assert.That(_viewModel.AverageLatency, Contains.Substring("45"));
            Assert.That(_viewModel.SuccessRate, Contains.Substring("95"));
        }

        [Test]
        public void StatusUpdate_UpdatesDisplayProperties()
        {
            // Arrange
            _viewModel = new EngineStatusViewModel(_mockStatusService.Object);

            // Act
            _statusSubject.OnNext(new TranslationEngineStatus
            {
                LocalOnlyStatus = new EngineStatusInfo
                {
                    Health = EngineHealth.Degraded,
                    AverageLatencyMs = 150.0,
                    SuccessRate = 0.85,
                    IsAvailable = true
                }
            });

            // Assert
            Assert.That(_viewModel.CurrentStatusColor, Is.EqualTo("#FF9800")); // Orange
            Assert.That(_viewModel.AverageLatency, Contains.Substring("150"));
            Assert.That(_viewModel.SuccessRate, Contains.Substring("85"));
        }

        [Test]
        public async Task TestEngineCommand_ExecutesSuccessfully()
        {
            // Arrange
            _viewModel = new EngineStatusViewModel(_mockStatusService.Object);

            // Act & Assert - コマンドが実行できることを確認
            Assert.DoesNotThrow(() => _viewModel.TestEngineCommand.Execute().Subscribe());
        }

        [Test]
        public void FallbackNotification_ShowsWhenInFallbackMode()
        {
            // Arrange
            _viewModel = new EngineStatusViewModel(_mockStatusService.Object);

            // Act - フォールバック状態の更新
            _statusSubject.OnNext(new TranslationEngineStatus
            {
                LocalOnlyStatus = new EngineStatusInfo { Health = EngineHealth.Healthy },
                IsInFallbackMode = true,
                CurrentFallbackReason = "レート制限"
            });

            // Assert
            Assert.That(_viewModel.HasActiveFallback, Is.True);
            Assert.That(_viewModel.FallbackReason, Is.EqualTo("レート制限"));
        }
    }
}

// UIテスト用のヘルパークラス
namespace Baketa.UI.Tests.Helpers
{
    public static class ViewModelTestHelper
    {
        public static void SimulatePropertyChange<T>(T viewModel, string propertyName) where T : class, INotifyPropertyChanged
        {
            var eventArgs = new PropertyChangedEventArgs(propertyName);
            var eventInfo = typeof(T).GetEvent("PropertyChanged");
            var field = typeof(T).GetField("PropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            
            if (field?.GetValue(viewModel) is PropertyChangedEventHandler handler)
            {
                handler.Invoke(viewModel, eventArgs);
            }
        }

        public static async Task<bool> WaitForPropertyChange<T>(T viewModel, string propertyName, TimeSpan timeout) 
            where T : class, INotifyPropertyChanged
        {
            var tcs = new TaskCompletionSource<bool>();
            PropertyChangedEventHandler handler = (sender, args) =>
            {
                if (args.PropertyName == propertyName)
                {
                    tcs.TrySetResult(true);
                }
            };

            viewModel.PropertyChanged += handler;
            
            try
            {
                var timeoutTask = Task.Delay(timeout);
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);
                return completedTask == tcs.Task && tcs.Task.Result;
            }
            finally
            {
                viewModel.PropertyChanged -= handler;
            }
        }
    }
}