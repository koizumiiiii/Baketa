// EngineSelectionViewModel.cs - 既存サービス統合例
using Baketa.Core.Translation.Models;
using Baketa.UI.Services;
using ReactiveUI;
using System.Reactive;
using System.Reactive.Linq;

namespace Baketa.UI.ViewModels.Settings
{
    public class EngineSelectionViewModel : ViewModelBase
    {
        private readonly ITranslationEngineStatusService _statusService;
        private readonly IUserPlanService _planService;
        private readonly IDisposable _statusSubscription;
        private TranslationEngine _selectedEngine = TranslationEngine.LocalOnly;

        public EngineSelectionViewModel(
            ITranslationEngineStatusService statusService,
            IUserPlanService planService)
        {
            _statusService = statusService;
            _planService = planService;

            // 初期設定
            LoadCurrentPlan();
            
            // プロパティ変更の監視
            this.WhenAnyValue(x => x.SelectedEngine)
                .Subscribe(UpdateEngineDescription);

            // 既存のTranslationEngineStatusServiceからの状態監視
            _statusSubscription = _statusService.StatusUpdated
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(UpdateEngineStatus);

            // コマンド初期化
            UpgradeCommand = ReactiveCommand.CreateFromTask(ExecuteUpgradeAsync);
            TestEngineCommand = ReactiveCommand.CreateFromTask(ExecuteTestEngineAsync);

            // 初期状態読み込み
            UpdateEngineStatus(_statusService.GetCurrentStatus());
        }

        #region Properties

        public TranslationEngine SelectedEngine
        {
            get => _selectedEngine;
            set => this.RaiseAndSetIfChanged(ref _selectedEngine, value);
        }

        public bool IsLocalOnlySelected
        {
            get => SelectedEngine == TranslationEngine.LocalOnly;
            set { if (value) SelectedEngine = TranslationEngine.LocalOnly; }
        }

        public bool IsCloudOnlySelected
        {
            get => SelectedEngine == TranslationEngine.CloudOnly;
            set { if (value && IsCloudOnlyAvailable) SelectedEngine = TranslationEngine.CloudOnly; }
        }

        [Reactive] public bool IsFreePlan { get; private set; }
        [Reactive] public bool IsCloudOnlyAvailable { get; private set; }
        [Reactive] public string CurrentPlan { get; private set; } = "Free";
        [Reactive] public bool ShowPlanInfo { get; private set; } = true;

        // エンジン状態表示
        [Reactive] public string LocalEngineStatusColor { get; private set; } = "#9E9E9E";
        [Reactive] public string CloudEngineStatusColor { get; private set; } = "#9E9E9E";
        [Reactive] public string LocalEnginePerformance { get; private set; } = "確認中...";
        [Reactive] public string CloudEnginePerformance { get; private set; } = "利用不可";

        // エンジン説明
        [Reactive] public string SelectedEngineDescription { get; private set; } = string.Empty;
        [Reactive] public string CostEstimation { get; private set; } = string.Empty;

        public ReactiveCommand<Unit, Unit> UpgradeCommand { get; }
        public ReactiveCommand<Unit, Unit> TestEngineCommand { get; }

        #endregion

        #region Private Methods

        private void LoadCurrentPlan()
        {
            var plan = _planService.GetCurrentPlan();
            IsFreePlan = plan.Type == PlanType.Free;
            IsCloudOnlyAvailable = plan.HasCloudAccess;
            CurrentPlan = plan.DisplayName;

            // 無料プランでCloudOnlyが選択されている場合はLocalOnlyに変更
            if (IsFreePlan && SelectedEngine == TranslationEngine.CloudOnly)
            {
                SelectedEngine = TranslationEngine.LocalOnly;
            }
        }

        private void UpdateEngineStatus(TranslationEngineStatus status)
        {
            // LocalOnlyエンジンの状態更新
            var localStatus = status.LocalOnlyStatus;
            LocalEngineStatusColor = GetStatusColor(localStatus.Health);
            LocalEnginePerformance = GetPerformanceText(localStatus);

            // CloudOnlyエンジンの状態更新（有料プランのみ）
            if (IsCloudOnlyAvailable)
            {
                var cloudStatus = status.CloudOnlyStatus;
                CloudEngineStatusColor = GetStatusColor(cloudStatus.Health);
                CloudEnginePerformance = GetPerformanceText(cloudStatus);
            }
            else
            {
                CloudEngineStatusColor = "#9E9E9E";
                CloudEnginePerformance = "有料プランでご利用可能";
            }
        }

        private void UpdateEngineDescription(TranslationEngine engine)
        {
            SelectedEngineDescription = engine switch
            {
                TranslationEngine.LocalOnly => 
                    "OPUS-MT専用エンジン\n" +
                    "✅ 高速処理（50ms以下）\n" +
                    "✅ 完全無料・オフライン対応\n" +
                    "✅ 基本的な翻訳品質\n" +
                    "📝 適用: 短いテキスト、一般的翻訳",

                TranslationEngine.CloudOnly when IsCloudOnlyAvailable => 
                    "Gemini API専用エンジン\n" +
                    "✅ 高品質翻訳・文脈理解\n" +
                    "✅ 専門用語対応\n" +
                    "✅ 長文翻訳に最適\n" +
                    "📝 適用: 複雑なテキスト、専門分野",

                TranslationEngine.CloudOnly when !IsCloudOnlyAvailable =>
                    "🔒 有料プランでご利用いただけます\n" +
                    "• Gemini APIによる高品質翻訳\n" +
                    "• 専門用語・文脈理解\n" +
                    "• アップグレードで利用可能",

                _ => "不明なエンジン"
            };

            CostEstimation = engine switch
            {
                TranslationEngine.LocalOnly => "💰 完全無料（通信費なし）",
                TranslationEngine.CloudOnly when IsCloudOnlyAvailable => "💰 従量課金（約$0.01-0.05/1000文字）",
                TranslationEngine.CloudOnly => "💰 有料プランへのアップグレードが必要",
                _ => ""
            };
        }

        private string GetStatusColor(EngineHealth health) => health switch
        {
            EngineHealth.Healthy => "#4CAF50",    // Green
            EngineHealth.Degraded => "#FF9800",   // Orange  
            EngineHealth.Unhealthy => "#F44336",  // Red
            EngineHealth.Unknown => "#9E9E9E",    // Gray
            _ => "#9E9E9E"
        };

        private string GetPerformanceText(EngineStatusInfo status)
        {
            if (status.Health == EngineHealth.Healthy)
            {
                return $"{status.AverageLatencyMs:F0}ms";
            }
            else if (status.Health == EngineHealth.Degraded)
            {
                return $"{status.AverageLatencyMs:F0}ms (制限あり)";
            }
            else
            {
                return "利用不可";
            }
        }

        #endregion

        #region Commands

        private async Task ExecuteUpgradeAsync()
        {
            try
            {
                var result = await _planService.UpgradeAsync();
                if (result)
                {
                    LoadCurrentPlan();
                    // 成功通知
                    // await _notificationService.ShowSuccessAsync("プランをアップグレードしました");
                }
            }
            catch (Exception ex)
            {
                // エラー通知
                // await _notificationService.ShowErrorAsync("アップグレードに失敗しました", ex.Message);
            }
        }

        private async Task ExecuteTestEngineAsync()
        {
            var testRequest = new TranslationRequest
            {
                SourceText = "Hello, world!",
                SourceLanguage = LanguageInfo.English,
                TargetLanguage = LanguageInfo.Japanese,
                Strategy = SelectedEngine == TranslationEngine.LocalOnly 
                    ? TranslationStrategy.LocalOnly 
                    : TranslationStrategy.CloudOnly
            };

            try
            {
                // 既存のHybridTranslationEngineまたは適切なサービスを使用
                // var response = await _translationService.TranslateAsync(testRequest);
                // テスト結果を表示
                // await _notificationService.ShowSuccessAsync($"テスト成功: {response.TranslatedText}");
            }
            catch (Exception ex)
            {
                // await _notificationService.ShowErrorAsync("翻訳テストに失敗しました", ex.Message);
            }
        }

        #endregion

        #region Public Methods

        public void ApplySettings(TranslationSettings settings)
        {
            SelectedEngine = settings.SelectedEngine;
        }

        public TranslationEngine GetSelectedEngine()
        {
            return SelectedEngine;
        }

        #endregion

        #region IDisposable

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _statusSubscription?.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}

// LanguagePairSelectionViewModel.cs - 言語ペア選択の実装
namespace Baketa.UI.ViewModels.Settings
{
    public class LanguagePairSelectionViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly ILocalizationService _localizationService;
        private ChineseVariant _selectedChineseVariant = ChineseVariant.Simplified;

        public LanguagePairSelectionViewModel(
            ISettingsService settingsService,
            ILocalizationService localizationService)
        {
            _settingsService = settingsService;
            _localizationService = localizationService;

            InitializeLanguagePairs();
            LoadCurrentTargetLanguage();

            // アプリ言語変更の監視
            _localizationService.CultureChanged += OnCultureChanged;
        }

        public ObservableCollection<LanguagePairItemViewModel> AvailableLanguagePairs { get; } = new();

        [Reactive] public string CurrentTargetLanguage { get; private set; } = "日本語";
        [Reactive] public bool HasChinesePairs { get; private set; }

        public ChineseVariant SelectedChineseVariant
        {
            get => _selectedChineseVariant;
            set => this.RaiseAndSetIfChanged(ref _selectedChineseVariant, value);
        }

        public bool IsSimplifiedSelected
        {
            get => SelectedChineseVariant == ChineseVariant.Simplified;
            set { if (value) SelectedChineseVariant = ChineseVariant.Simplified; }
        }

        public bool IsTraditionalSelected
        {
            get => SelectedChineseVariant == ChineseVariant.Traditional;
            set { if (value) SelectedChineseVariant = ChineseVariant.Traditional; }
        }

        private void InitializeLanguagePairs()
        {
            // 初期リリーススコープの言語ペアのみ
            var pairs = new[]
            {
                new LanguagePairItemViewModel(
                    "ja-en", 
                    "日本語 ⇔ 英語", 
                    "双方向翻訳・最高精度・Direct翻訳", 
                    true,
                    LanguagePairType.Bidirectional),
                    
                new LanguagePairItemViewModel(
                    "zh-en", 
                    "中国語 ⇔ 英語", 
                    "簡体字・繁体字対応・Direct翻訳", 
                    true,
                    LanguagePairType.Bidirectional),
                    
                new LanguagePairItemViewModel(
                    "zh-ja", 
                    "中国語 → 日本語", 
                    "直接翻訳・opus-mt-tc-big-zh-ja使用", 
                    true,
                    LanguagePairType.Direct),
                    
                new LanguagePairItemViewModel(
                    "ja-zh", 
                    "日本語 → 中国語", 
                    "2段階翻訳・高品質（日→英→中）", 
                    true,
                    LanguagePairType.TwoStage)
            };

            foreach (var pair in pairs)
            {
                AvailableLanguagePairs.Add(pair);
            }

            HasChinesePairs = pairs.Any(p => p.Id.Contains("zh"));
        }

        private void LoadCurrentTargetLanguage()
        {
            var currentCulture = _localizationService.CurrentCulture;
            CurrentTargetLanguage = currentCulture.Name switch
            {
                "ja-JP" or "ja" => "日本語",
                "en-US" or "en" => "English",
                "zh-CN" => "中文（简体）",
                "zh-TW" => "中文（繁體）",
                _ => currentCulture.DisplayName
            };
        }

        private void OnCultureChanged(object? sender, CultureChangedEventArgs e)
        {
            LoadCurrentTargetLanguage();
            
            // 言語ペアの有効性を再評価
            foreach (var pair in AvailableLanguagePairs)
            {
                pair.UpdateAvailability(e.NewCulture);
            }
        }

        public List<string> GetEnabledPairs()
        {
            return AvailableLanguagePairs
                .Where(p => p.IsEnabled && p.IsAvailable)
                .Select(p => p.Id)
                .ToList();
        }

        public void ApplySettings(TranslationSettings settings)
        {
            foreach (var pair in AvailableLanguagePairs)
            {
                pair.IsEnabled = settings.EnabledLanguagePairs.Contains(pair.Id);
            }

            SelectedChineseVariant = settings.ChineseVariant;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _localizationService.CultureChanged -= OnCultureChanged;
            }
            base.Dispose(disposing);
        }
    }

    public class LanguagePairItemViewModel : ViewModelBase
    {
        public LanguagePairItemViewModel(
            string id, 
            string displayName, 
            string description, 
            bool isEnabled,
            LanguagePairType type)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            Type = type;
            IsEnabled = isEnabled;
            IsAvailable = true; // 初期は利用可能
            StatusColor = "#4CAF50"; // Green
            PerformanceText = "利用可能";
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public LanguagePairType Type { get; }

        [Reactive] public bool IsEnabled { get; set; }
        [Reactive] public bool IsAvailable { get; set; }
        [Reactive] public string StatusColor { get; set; }
        [Reactive] public string PerformanceText { get; set; }

        public void UpdateAvailability(CultureInfo culture)
        {
            // アプリ言語に基づいて言語ペアの利用可能性を更新
            var cultureName = culture.Name;
            
            IsAvailable = Id switch
            {
                "ja-en" or "en-ja" => cultureName.StartsWith("ja") || cultureName.StartsWith("en"),
                "zh-en" or "en-zh" => cultureName.StartsWith("zh") || cultureName.StartsWith("en"),
                "zh-ja" => cultureName.StartsWith("ja"),
                "ja-zh" => cultureName.StartsWith("ja"),
                _ => true
            };

            StatusColor = IsAvailable ? "#4CAF50" : "#9E9E9E";
            PerformanceText = IsAvailable ? "利用可能" : "対象言語外";
        }
    }

    public enum LanguagePairType
    {
        Direct,
        Bidirectional,
        TwoStage
    }
}

// TranslationStrategyViewModel.cs - 翻訳戦略選択の実装
namespace Baketa.UI.ViewModels.Settings
{
    public class TranslationStrategyViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private TranslationStrategy _selectedStrategy = TranslationStrategy.Direct;
        private bool _enableCloudToLocalFallback = true;

        public TranslationStrategyViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;

            // プロパティ変更の監視
            this.WhenAnyValue(x => x.SelectedStrategy)
                .Subscribe(UpdateStrategyDescription);

            // 初期説明更新
            UpdateStrategyDescription(SelectedStrategy);
        }

        public TranslationStrategy SelectedStrategy
        {
            get => _selectedStrategy;
            set => this.RaiseAndSetIfChanged(ref _selectedStrategy, value);
        }

        public bool IsDirectSelected
        {
            get => SelectedStrategy == TranslationStrategy.Direct;
            set { if (value) SelectedStrategy = TranslationStrategy.Direct; }
        }

        public bool IsTwoStageSelected
        {
            get => SelectedStrategy == TranslationStrategy.TwoStage;
            set { if (value) SelectedStrategy = TranslationStrategy.TwoStage; }
        }

        public bool EnableCloudToLocalFallback
        {
            get => _enableCloudToLocalFallback;
            set => this.RaiseAndSetIfChanged(ref _enableCloudToLocalFallback, value);
        }

        [Reactive] public string SelectedStrategyDescription { get; private set; } = string.Empty;
        [Reactive] public string PerformanceExpectation { get; private set; } = string.Empty;

        private void UpdateStrategyDescription(TranslationStrategy strategy)
        {
            SelectedStrategyDescription = strategy switch
            {
                TranslationStrategy.Direct =>
                    "単一モデルによる直接翻訳\n" +
                    "✅ 高速処理（50ms以下）\n" +
                    "✅ 低遅延・リアルタイム対応\n" +
                    "✅ 対応言語ペア: ja⇔en, zh⇔en, zh→ja\n" +
                    "📝 最も一般的な翻訳方式",

                TranslationStrategy.TwoStage =>
                    "英語を中継言語とした2段階翻訳\n" +
                    "✅ 高品質・文脈保持\n" +
                    "✅ 対応言語ペア: ja→zh（日本語→中国語）\n" +
                    "⏱️ やや時間がかかる（100-200ms）\n" +
                    "📝 直接翻訳モデルがない言語ペア用",

                _ => "不明な翻訳戦略"
            };

            PerformanceExpectation = strategy switch
            {
                TranslationStrategy.Direct => "⚡ 高速: 平均50ms以下",
                TranslationStrategy.TwoStage => "🎯 高品質: 平均100-200ms",
                _ => "パフォーマンス不明"
            };
        }

        public void ApplySettings(TranslationSettings settings)
        {
            SelectedStrategy = settings.TranslationStrategy;
            EnableCloudToLocalFallback = settings.EnableFallback;
        }

        public TranslationStrategy GetSelectedStrategy()
        {
            return SelectedStrategy;
        }

        public bool GetFallbackEnabled()
        {
            return EnableCloudToLocalFallback;
        }
    }
}

// 必要なインターフェースとモデルの定義
namespace Baketa.UI.Services
{
    public interface IUserPlanService
    {
        UserPlan GetCurrentPlan();
        bool HasCloudAccess { get; }
        Task<bool> UpgradeAsync();
    }

    public class UserPlan
    {
        public PlanType Type { get; set; }
        public string DisplayName { get; set; }
        public bool HasCloudAccess { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }

    public enum PlanType
    {
        Free,
        Premium,
        Enterprise
    }

    public interface ILocalizationService
    {
        CultureInfo CurrentCulture { get; }
        event EventHandler<CultureChangedEventArgs> CultureChanged;
    }

    public class CultureChangedEventArgs : EventArgs
    {
        public CultureInfo NewCulture { get; set; }
        public CultureInfo OldCulture { get; set; }
    }
}

namespace Baketa.Core.Translation.Models
{
    public class TranslationSettings
    {
        public TranslationEngine SelectedEngine { get; set; } = TranslationEngine.LocalOnly;
        public List<string> EnabledLanguagePairs { get; set; } = new();
        public ChineseVariant ChineseVariant { get; set; } = ChineseVariant.Simplified;
        public TranslationStrategy TranslationStrategy { get; set; } = TranslationStrategy.Direct;
        public bool EnableFallback { get; set; } = true;
    }

    public enum TranslationEngine
    {
        LocalOnly,
        CloudOnly
    }

    public enum TranslationStrategy
    {
        Direct,
        TwoStage
    }

    public enum EngineHealth
    {
        Unknown,
        Healthy,
        Degraded,
        Unhealthy
    }

    public class TranslationEngineStatus
    {
        public EngineStatusInfo LocalOnlyStatus { get; set; } = new();
        public EngineStatusInfo CloudOnlyStatus { get; set; } = new();
        public DateTime LastUpdated { get; set; }
    }

    public class EngineStatusInfo
    {
        public EngineHealth Health { get; set; } = EngineHealth.Unknown;
        public double AverageLatencyMs { get; set; }
        public double SuccessRate { get; set; }
        public string LastError { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
    }
}