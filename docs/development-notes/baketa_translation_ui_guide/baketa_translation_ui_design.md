# Baketa翻訳設定UI実装設計書 - 初期リリース対応

*作成日: 2025年6月2日*  
*対象: フェーズ4.1 最優先実装項目*

## 1. 実装概要

### 1.1 設計原則

- **無料/有料モデルに対応**: 無料版はLocalOnlyのみ、有料版はLocalOnly + CloudOnly
- **初期リリーススコープ**: 基本機能に絞った実装
- **技術基盤活用**: 既存のTranslationEngineStatusService等を最大限活用
- **アプリ言語連動**: 翻訳先言語はアプリケーション自体の言語設定とリンク

### 1.2 除外項目（v1.1以降に延期）

- ❌ ホットキー機能
- ❌ Auto/Cantonese中国語変種
- ❌ 詳細監視機能（レート制限詳細、パフォーマンス統計）
- ❌ リアルタイム翻訳品質メトリクス

### 1.3 実装優先度

**最優先（今すぐ実装）**:
1. 基本エンジン選択UI（LocalOnly vs CloudOnly + 状態表示）
2. 基本言語ペア選択UI（ja⇔en, zh⇔en, zh→ja + 簡体字/繁体字）
3. 翻訳戦略選択UI（Direct vs TwoStage + 2段階翻訳対応）

## 2. UI構成設計

### 2.1 メイン翻訳設定ビュー

```xml
<!-- TranslationSettingsView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Baketa.UI.ViewModels.Settings"
             x:Class="Baketa.UI.Views.Settings.TranslationSettingsView"
             x:DataType="vm:TranslationSettingsViewModel">

  <ScrollViewer>
    <StackPanel Spacing="20" Margin="20">
      
      <!-- エンジン選択セクション -->
      <Border Classes="setting-section">
        <StackPanel Spacing="15">
          <TextBlock Text="翻訳エンジン選択" Classes="section-title"/>
          <ContentControl Content="{Binding EngineSelection}"/>
        </StackPanel>
      </Border>
      
      <!-- 言語ペア選択セクション -->
      <Border Classes="setting-section">
        <StackPanel Spacing="15">
          <TextBlock Text="言語設定" Classes="section-title"/>
          <ContentControl Content="{Binding LanguagePairSelection}"/>
        </StackPanel>
      </Border>
      
      <!-- 翻訳戦略選択セクション -->
      <Border Classes="setting-section">
        <StackPanel Spacing="15">
          <TextBlock Text="翻訳戦略" Classes="section-title"/>
          <ContentControl Content="{Binding TranslationStrategy}"/>
        </StackPanel>
      </Border>
      
      <!-- エンジン状態表示セクション -->
      <Border Classes="setting-section">
        <StackPanel Spacing="15">
          <TextBlock Text="翻訳エンジン状態" Classes="section-title"/>
          <ContentControl Content="{Binding EngineStatus}"/>
        </StackPanel>
      </Border>
      
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

### 2.2 エンジン選択コントロール

```xml
<!-- EngineSelectionControl.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Baketa.UI.ViewModels.Settings"
             x:Class="Baketa.UI.Views.Settings.EngineSelectionControl"
             x:DataType="vm:EngineSelectionViewModel">

  <StackPanel Spacing="15">
    
    <!-- プラン表示（無料/有料の区別） -->
    <Border Classes="plan-info" IsVisible="{Binding ShowPlanInfo}">
      <StackPanel Orientation="Horizontal" Spacing="10">
        <TextBlock Text="現在のプラン:" FontWeight="SemiBold"/>
        <TextBlock Text="{Binding CurrentPlan}" Classes="plan-badge"/>
        <Button Content="アップグレード" 
                IsVisible="{Binding IsFreePlan}"
                Classes="upgrade-button"
                Command="{Binding UpgradeCommand}"/>
      </StackPanel>
    </Border>
    
    <!-- エンジン選択 -->
    <StackPanel Spacing="10">
      
      <!-- LocalOnly選択 -->
      <RadioButton GroupName="Engine" 
                   IsChecked="{Binding IsLocalOnlySelected}"
                   Classes="engine-option">
        <Grid ColumnDefinitions="Auto,*,Auto">
          <StackPanel Grid.Column="0" Spacing="5">
            <TextBlock Text="LocalOnly" FontWeight="Bold" FontSize="16"/>
            <TextBlock Text="• OPUS-MT専用・高速・無料・オフライン対応" Classes="description"/>
            <TextBlock Text="• 適用：短いテキスト、一般的翻訳" Classes="description"/>
          </StackPanel>
          
          <StackPanel Grid.Column="2" Spacing="5">
            <Ellipse Width="12" Height="12" 
                     Fill="{Binding LocalEngineStatusColor}"/>
            <TextBlock Text="{Binding LocalEnginePerformance}" 
                       Classes="performance-text"/>
          </StackPanel>
        </Grid>
      </RadioButton>
      
      <!-- CloudOnly選択（有料プランのみ） -->
      <RadioButton GroupName="Engine" 
                   IsChecked="{Binding IsCloudOnlySelected}"
                   IsEnabled="{Binding IsCloudOnlyAvailable}"
                   Classes="engine-option">
        <Grid ColumnDefinitions="Auto,*,Auto">
          <StackPanel Grid.Column="0" Spacing="5">
            <StackPanel Orientation="Horizontal" Spacing="10">
              <TextBlock Text="CloudOnly" FontWeight="Bold" FontSize="16"/>
              <Border Classes="premium-badge" IsVisible="{Binding IsFreePlan}">
                <TextBlock Text="有料" FontSize="10"/>
              </Border>
            </StackPanel>
            <TextBlock Text="• Gemini API専用・高品質・文脈理解" Classes="description"/>
            <TextBlock Text="• 適用：複雑なテキスト、専門用語" Classes="description"/>
          </StackPanel>
          
          <StackPanel Grid.Column="2" Spacing="5">
            <Ellipse Width="12" Height="12" 
                     Fill="{Binding CloudEngineStatusColor}"/>
            <TextBlock Text="{Binding CloudEnginePerformance}" 
                       Classes="performance-text"/>
          </StackPanel>
        </Grid>
      </RadioButton>
      
    </StackPanel>
    
    <!-- 選択中エンジンの詳細情報 -->
    <Border Classes="engine-detail">
      <StackPanel Spacing="8">
        <TextBlock Text="選択中のエンジン" FontWeight="SemiBold"/>
        <TextBlock Text="{Binding SelectedEngineDescription}" 
                   TextWrapping="Wrap" Classes="description"/>
        <TextBlock Text="{Binding CostEstimation}" 
                   Classes="cost-info"/>
      </StackPanel>
    </Border>
    
  </StackPanel>
</UserControl>
```

### 2.3 言語ペア選択コントロール

```xml
<!-- LanguagePairSelectionControl.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Baketa.UI.ViewModels.Settings"
             x:Class="Baketa.UI.Views.Settings.LanguagePairSelectionControl"
             x:DataType="vm:LanguagePairSelectionViewModel">

  <StackPanel Spacing="15">
    
    <!-- アプリ言語連動の説明 -->
    <Border Classes="info-panel">
      <StackPanel Spacing="5">
        <TextBlock Text="💡 翻訳先言語について" FontWeight="SemiBold"/>
        <TextBlock Text="翻訳先言語は、アプリケーションの表示言語と連動しています。" 
                   Classes="info-text"/>
        <StackPanel Orientation="Horizontal" Spacing="5">
          <TextBlock Text="現在の翻訳先言語:" Classes="info-text"/>
          <TextBlock Text="{Binding CurrentTargetLanguage}" 
                     FontWeight="SemiBold"/>
        </StackPanel>
      </StackPanel>
    </Border>
    
    <!-- 対応言語ペア一覧 -->
    <StackPanel Spacing="10">
      <TextBlock Text="対応言語ペア" FontWeight="SemiBold"/>
      
      <ItemsControl ItemsSource="{Binding AvailableLanguagePairs}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <Border Classes="language-pair-item" Margin="0,2">
              <Grid ColumnDefinitions="Auto,*,Auto,Auto">
                
                <CheckBox Grid.Column="0" 
                          IsChecked="{Binding IsEnabled}"
                          Margin="0,0,10,0"/>
                
                <StackPanel Grid.Column="1" Spacing="2">
                  <TextBlock Text="{Binding DisplayName}" FontWeight="SemiBold"/>
                  <TextBlock Text="{Binding Description}" 
                             Classes="pair-description"/>
                </StackPanel>
                
                <Ellipse Grid.Column="2" 
                         Width="8" Height="8" 
                         Fill="{Binding StatusColor}"
                         Margin="10,0"/>
                
                <TextBlock Grid.Column="3" 
                           Text="{Binding PerformanceText}"
                           Classes="performance-text"/>
                
              </Grid>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </StackPanel>
    
    <!-- 中国語変種設定 -->
    <Border Classes="chinese-variant-section" 
            IsVisible="{Binding HasChinesePairs}">
      <StackPanel Spacing="10">
        <TextBlock Text="中国語変種設定" FontWeight="SemiBold"/>
        
        <StackPanel Spacing="8">
          <RadioButton GroupName="ChineseVariant" 
                       IsChecked="{Binding IsSimplifiedSelected}"
                       Content="简体字（簡体字）- 中国大陸で使用"/>
          
          <RadioButton GroupName="ChineseVariant" 
                       IsChecked="{Binding IsTraditionalSelected}"
                       Content="繁體字（繁体字）- 台湾・香港で使用"/>
        </StackPanel>
        
        <TextBlock Text="💡 変種選択により適切な翻訳結果を提供します" 
                   Classes="info-text"/>
      </StackPanel>
    </Border>
    
  </StackPanel>
</UserControl>
```

### 2.4 翻訳戦略選択コントロール

```xml
<!-- TranslationStrategyControl.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Baketa.UI.ViewModels.Settings"
             x:Class="Baketa.UI.Views.Settings.TranslationStrategyControl"
             x:DataType="vm:TranslationStrategyViewModel">

  <StackPanel Spacing="15">
    
    <!-- 戦略選択 -->
    <StackPanel Spacing="10">
      
      <!-- Direct戦略 -->
      <RadioButton GroupName="Strategy" 
                   IsChecked="{Binding IsDirectSelected}"
                   Classes="strategy-option">
        <StackPanel Spacing="5">
          <TextBlock Text="Direct（直接翻訳）" FontWeight="Bold"/>
          <TextBlock Text="• 単一モデルでの直接翻訳" Classes="description"/>
          <TextBlock Text="• 高速・低遅延" Classes="description"/>
          <TextBlock Text="• 対応：ja⇔en, zh⇔en, zh→ja" Classes="description"/>
        </StackPanel>
      </RadioButton>
      
      <!-- TwoStage戦略 -->
      <RadioButton GroupName="Strategy" 
                   IsChecked="{Binding IsTwoStageSelected}"
                   Classes="strategy-option">
        <StackPanel Spacing="5">
          <TextBlock Text="TwoStage（2段階翻訳）" FontWeight="Bold"/>
          <TextBlock Text="• 英語を中継言語とした2段階翻訳" Classes="description"/>
          <TextBlock Text="• 高品質・文脈保持" Classes="description"/>
          <TextBlock Text="• 対応：ja→zh（日本語→英語→中国語）" Classes="description"/>
        </StackPanel>
      </RadioButton>
      
    </StackPanel>
    
    <!-- 自動フォールバック設定 -->
    <Border Classes="fallback-section">
      <StackPanel Spacing="10">
        <TextBlock Text="自動フォールバック" FontWeight="SemiBold"/>
        
        <CheckBox IsChecked="{Binding EnableCloudToLocalFallback}"
                  Content="CloudOnly → LocalOnly 自動切り替え"/>
        
        <StackPanel IsVisible="{Binding EnableCloudToLocalFallback}" 
                    Margin="20,5,0,0" Spacing="5">
          <TextBlock Text="以下の場合にLocalOnlyに自動切り替え:" 
                     Classes="info-text"/>
          <TextBlock Text="• ネットワークエラー" Classes="info-text"/>
          <TextBlock Text="• APIレート制限" Classes="info-text"/>
          <TextBlock Text="• クラウドサービス障害" Classes="info-text"/>
        </StackPanel>
        
      </StackPanel>
    </Border>
    
    <!-- 選択中戦略の詳細 -->
    <Border Classes="strategy-detail">
      <StackPanel Spacing="8">
        <TextBlock Text="選択中の戦略" FontWeight="SemiBold"/>
        <TextBlock Text="{Binding SelectedStrategyDescription}" 
                   TextWrapping="Wrap" Classes="description"/>
        <TextBlock Text="{Binding PerformanceExpectation}" 
                   Classes="performance-text"/>
      </StackPanel>
    </Border>
    
  </StackPanel>
</UserControl>
```

### 2.5 エンジン状態表示コントロール

```xml
<!-- EngineStatusControl.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Baketa.UI.ViewModels.Settings"
             x:Class="Baketa.UI.Views.Settings.EngineStatusControl"
             x:DataType="vm:EngineStatusViewModel">

  <StackPanel Spacing="15">
    
    <!-- 現在の状態表示 -->
    <Border Classes="current-status">
      <Grid ColumnDefinitions="Auto,*,Auto">
        
        <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="10">
          <Ellipse Width="14" Height="14" 
                   Fill="{Binding CurrentStatusColor}"/>
          <TextBlock Text="{Binding CurrentStatusText}" 
                     FontWeight="SemiBold"/>
        </StackPanel>
        
        <TextBlock Grid.Column="1" 
                   Text="{Binding CurrentEngineInfo}" 
                   Classes="engine-info"/>
        
        <Button Grid.Column="2" 
                Content="テスト"
                Classes="test-button"
                Command="{Binding TestEngineCommand}"/>
        
      </Grid>
    </Border>
    
    <!-- 基本統計情報 -->
    <Border Classes="basic-stats">
      <Grid ColumnDefinitions="*,*,*">
        
        <StackPanel Grid.Column="0" HorizontalAlignment="Center">
          <TextBlock Text="平均速度" Classes="stat-label"/>
          <TextBlock Text="{Binding AverageLatency}" Classes="stat-value"/>
        </StackPanel>
        
        <StackPanel Grid.Column="1" HorizontalAlignment="Center">
          <TextBlock Text="成功率" Classes="stat-label"/>
          <TextBlock Text="{Binding SuccessRate}" Classes="stat-value"/>
        </StackPanel>
        
        <StackPanel Grid.Column="2" HorizontalAlignment="Center">
          <TextBlock Text="状態" Classes="stat-label"/>
          <TextBlock Text="{Binding EngineHealth}" Classes="stat-value"/>
        </StackPanel>
        
      </Grid>
    </Border>
    
    <!-- フォールバック通知（表示される場合のみ） -->
    <Border Classes="fallback-notification" 
            IsVisible="{Binding HasActiveFallback}">
      <StackPanel Orientation="Horizontal" Spacing="10">
        <TextBlock Text="⚠️" FontSize="16"/>
        <StackPanel>
          <TextBlock Text="フォールバック中" FontWeight="SemiBold"/>
          <TextBlock Text="{Binding FallbackReason}" Classes="fallback-reason"/>
        </StackPanel>
      </StackPanel>
    </Border>
    
  </StackPanel>
</UserControl>
```

## 3. ViewModel実装

### 3.1 メイン翻訳設定ViewModel

```csharp
// TranslationSettingsViewModel.cs
using Baketa.Core.Translation.Models;
using Baketa.UI.Services;
using ReactiveUI;
using System.Reactive;

namespace Baketa.UI.ViewModels.Settings
{
    public class TranslationSettingsViewModel : ViewModelBase
    {
        private readonly ITranslationEngineStatusService _statusService;
        private readonly IUserPlanService _planService;
        private readonly ISettingsService _settingsService;
        
        public TranslationSettingsViewModel(
            ITranslationEngineStatusService statusService,
            IUserPlanService planService,
            ISettingsService settingsService)
        {
            _statusService = statusService;
            _planService = planService;
            _settingsService = settingsService;
            
            // 子ViewModelの初期化
            EngineSelection = new EngineSelectionViewModel(statusService, planService);
            LanguagePairSelection = new LanguagePairSelectionViewModel(settingsService);
            TranslationStrategy = new TranslationStrategyViewModel(settingsService);
            EngineStatus = new EngineStatusViewModel(statusService);
            
            // コマンド初期化
            SaveSettingsCommand = ReactiveCommand.CreateFromTask(SaveSettingsAsync);
            ResetSettingsCommand = ReactiveCommand.CreateFromTask(ResetSettingsAsync);
        }
        
        public EngineSelectionViewModel EngineSelection { get; }
        public LanguagePairSelectionViewModel LanguagePairSelection { get; }
        public TranslationStrategyViewModel TranslationStrategy { get; }
        public EngineStatusViewModel EngineStatus { get; }
        
        public ReactiveCommand<Unit, Unit> SaveSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetSettingsCommand { get; }
        
        private async Task SaveSettingsAsync()
        {
            var settings = new TranslationSettings
            {
                SelectedEngine = EngineSelection.SelectedEngine,
                EnabledLanguagePairs = LanguagePairSelection.GetEnabledPairs(),
                ChineseVariant = LanguagePairSelection.SelectedChineseVariant,
                TranslationStrategy = TranslationStrategy.SelectedStrategy,
                EnableFallback = TranslationStrategy.EnableCloudToLocalFallback
            };
            
            await _settingsService.SaveTranslationSettingsAsync(settings);
        }
        
        private async Task ResetSettingsAsync()
        {
            await _settingsService.ResetTranslationSettingsAsync();
            await LoadSettingsAsync();
        }
        
        private async Task LoadSettingsAsync()
        {
            var settings = await _settingsService.GetTranslationSettingsAsync();
            EngineSelection.ApplySettings(settings);
            LanguagePairSelection.ApplySettings(settings);
            TranslationStrategy.ApplySettings(settings);
        }
    }
}
```

### 3.2 エンジン選択ViewModel

```csharp
// EngineSelectionViewModel.cs
using Baketa.Core.Translation.Models;
using ReactiveUI;

namespace Baketa.UI.ViewModels.Settings
{
    public class EngineSelectionViewModel : ViewModelBase
    {
        private readonly ITranslationEngineStatusService _statusService;
        private readonly IUserPlanService _planService;
        private TranslationEngine _selectedEngine = TranslationEngine.LocalOnly;
        
        public EngineSelectionViewModel(
            ITranslationEngineStatusService statusService,
            IUserPlanService planService)
        {
            _statusService = statusService;
            _planService = planService;
            
            // 初期設定
            LoadCurrentPlan();
            LoadEngineStatus();
            
            // プロパティ変更の監視
            this.WhenAnyValue(x => x.SelectedEngine)
                .Subscribe(UpdateEngineDescription);
                
            // コマンド初期化
            UpgradeCommand = ReactiveCommand.Create(ExecuteUpgrade);
        }
        
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
        
        [Reactive] public string LocalEngineStatusColor { get; private set; } = "#4CAF50";
        [Reactive] public string CloudEngineStatusColor { get; private set; } = "#9E9E9E";
        [Reactive] public string LocalEnginePerformance { get; private set; } = "< 50ms";
        [Reactive] public string CloudEnginePerformance { get; private set; } = "利用不可";
        
        [Reactive] public string SelectedEngineDescription { get; private set; } = string.Empty;
        [Reactive] public string CostEstimation { get; private set; } = string.Empty;
        
        public ReactiveCommand<Unit, Unit> UpgradeCommand { get; }
        
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
        
        private void LoadEngineStatus()
        {
            var localStatus = _statusService.GetEngineStatus(TranslationEngine.LocalOnly);
            LocalEngineStatusColor = GetStatusColor(localStatus);
            LocalEnginePerformance = GetPerformanceText(localStatus);
            
            if (IsCloudOnlyAvailable)
            {
                var cloudStatus = _statusService.GetEngineStatus(TranslationEngine.CloudOnly);
                CloudEngineStatusColor = GetStatusColor(cloudStatus);
                CloudEnginePerformance = GetPerformanceText(cloudStatus);
            }
        }
        
        private void UpdateEngineDescription(TranslationEngine engine)
        {
            SelectedEngineDescription = engine switch
            {
                TranslationEngine.LocalOnly => 
                    "OPUS-MT専用エンジン\n" +
                    "✅ 高速処理（50ms以下）\n" +
                    "✅ 完全無料\n" +
                    "✅ オフライン対応\n" +
                    "🎯 標準品質の翻訳",
                
                TranslationEngine.CloudOnly when IsCloudOnlyAvailable => 
                    "Gemini API専用エンジン\n" +
                    "✅ 高品質翻訳\n" +
                    "✅ 専門用語対応\n" +
                    "✅ 文脈理解\n" +
                    "🎯 高品質翻訳",
                    
                TranslationEngine.CloudOnly when !IsCloudOnlyAvailable =>
                    "有料プランでご利用いただけます\n" +
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
        
        private string GetStatusColor(EngineStatus status) => status switch
        {
            EngineStatus.Online => "#4CAF50", // Green
            EngineStatus.Degraded => "#FF9800", // Orange
            EngineStatus.Offline => "#F44336", // Red
            _ => "#9E9E9E" // Gray
        };
        
        private string GetPerformanceText(EngineStatus status) => status switch
        {
            EngineStatus.Online => "正常動作",
            EngineStatus.Degraded => "制限あり",
            EngineStatus.Offline => "利用不可",
            _ => "不明"
        };
        
        private void ExecuteUpgrade()
        {
            // アップグレード画面への遷移
            // 実装は別途定義
        }
        
        public void ApplySettings(TranslationSettings settings)
        {
            SelectedEngine = settings.SelectedEngine;
        }
    }
}
```

### 3.3 言語ペア選択ViewModel

```csharp
// LanguagePairSelectionViewModel.cs
using Baketa.Core.Translation.Models;
using ReactiveUI;
using System.Collections.ObjectModel;

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
                new LanguagePairItemViewModel("ja-en", "日本語 ⇔ 英語", "双方向翻訳・最高精度", true),
                new LanguagePairItemViewModel("en-ja", "英語 ⇔ 日本語", "双方向翻訳・最高精度", true),
                new LanguagePairItemViewModel("zh-en", "中国語 ⇔ 英語", "簡体字・繁体字対応", true),
                new LanguagePairItemViewModel("en-zh", "英語 ⇔ 中国語", "簡体字・繁体字対応", true),
                new LanguagePairItemViewModel("zh-ja", "中国語 → 日本語", "直接翻訳・高精度", true),
                new LanguagePairItemViewModel("ja-zh", "日本語 → 中国語", "2段階翻訳・高品質", true)
            };
            
            foreach (var pair in pairs)
            {
                AvailableLanguagePairs.Add(pair);
            }
            
            // 中国語ペアの存在を確認
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
        
        public List<string> GetEnabledPairs()
        {
            return AvailableLanguagePairs
                .Where(p => p.IsEnabled)
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
    }
    
    public class LanguagePairItemViewModel : ViewModelBase
    {
        public LanguagePairItemViewModel(string id, string displayName, string description, bool isEnabled)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            IsEnabled = isEnabled;
            StatusColor = "#4CAF50"; // Green for available
            PerformanceText = "利用可能";
        }
        
        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        
        [Reactive] public bool IsEnabled { get; set; }
        [Reactive] public string StatusColor { get; set; }
        [Reactive] public string PerformanceText { get; set; }
    }
}
```

## 4. スタイル定義

### 4.1 翻訳設定用スタイル

```xml
<!-- Styles/TranslationSettingsStyles.axaml -->
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- 設定セクション -->
  <Style Selector="Border.setting-section">
    <Setter Property="Background" Value="#FAFAFA"/>
    <Setter Property="BorderBrush" Value="#E0E0E0"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="8"/>
    <Setter Property="Padding" Value="20"/>
  </Style>

  <!-- セクションタイトル -->
  <Style Selector="TextBlock.section-title">
    <Setter Property="FontSize" Value="18"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
    <Setter Property="Foreground" Value="#1976D2"/>
  </Style>

  <!-- エンジン選択オプション -->
  <Style Selector="RadioButton.engine-option">
    <Setter Property="Padding" Value="15"/>
    <Setter Property="Margin" Value="0,5"/>
    <Setter Property="BorderBrush" Value="#E0E0E0"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="6"/>
    <Setter Property="Background" Value="White"/>
  </Style>

  <Style Selector="RadioButton.engine-option:checked">
    <Setter Property="BorderBrush" Value="#1976D2"/>
    <Setter Property="Background" Value="#E3F2FD"/>
  </Style>

  <Style Selector="RadioButton.engine-option:disabled">
    <Setter Property="Opacity" Value="0.6"/>
    <Setter Property="Background" Value="#F5F5F5"/>
  </Style>

  <!-- プラン情報 -->
  <Style Selector="Border.plan-info">
    <Setter Property="Background" Value="#E8F5E8"/>
    <Setter Property="BorderBrush" Value="#4CAF50"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="4"/>
    <Setter Property="Padding" Value="10"/>
  </Style>

  <!-- プラン バッジ -->
  <Style Selector="TextBlock.plan-badge">
    <Setter Property="Background" Value="#4CAF50"/>
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="Padding" Value="4,2"/>
    <Setter Property="CornerRadius" Value="3"/>
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="FontWeight" Value="Bold"/>
  </Style>

  <!-- プレミアム バッジ -->
  <Style Selector="Border.premium-badge">
    <Setter Property="Background" Value="#FF9800"/>
    <Setter Property="CornerRadius" Value="3"/>
    <Setter Property="Padding" Value="4,2"/>
  </Style>

  <Style Selector="Border.premium-badge TextBlock">
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="FontWeight" Value="Bold"/>
  </Style>

  <!-- エンジン詳細 -->
  <Style Selector="Border.engine-detail">
    <Setter Property="Background" Value="#F0F7FF"/>
    <Setter Property="BorderBrush" Value="#2196F3"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="4"/>
    <Setter Property="Padding" Value="12"/>
  </Style>

  <!-- 言語ペア項目 -->
  <Style Selector="Border.language-pair-item">
    <Setter Property="Background" Value="White"/>
    <Setter Property="BorderBrush" Value="#E0E0E0"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="4"/>
    <Setter Property="Padding" Value="10"/>
  </Style>

  <Style Selector="Border.language-pair-item:pointerover">
    <Setter Property="Background" Value="#F5F5F5"/>
  </Style>

  <!-- 説明テキスト -->
  <Style Selector="TextBlock.description">
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="Foreground" Value="#666666"/>
    <Setter Property="LineHeight" Value="1.4"/>
  </Style>

  <!-- パフォーマンステキスト -->
  <Style Selector="TextBlock.performance-text">
    <Setter Property="FontSize" Value="11"/>
    <Setter Property="Foreground" Value="#4CAF50"/>
    <Setter Property="FontWeight" Value="SemiBold"/>
  </Style>

  <!-- コスト情報 -->
  <Style Selector="TextBlock.cost-info">
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Foreground" Value="#FF9800"/>
    <Setter Property="FontStyle" Value="Italic"/>
  </Style>

  <!-- 情報パネル -->
  <Style Selector="Border.info-panel">
    <Setter Property="Background" Value="#E1F5FE"/>
    <Setter Property="BorderBrush" Value="#0288D1"/>
    <Setter Property="BorderThickness" Value="1,1,1,3"/>
    <Setter Property="CornerRadius" Value="4"/>
    <Setter Property="Padding" Value="12"/>
  </Style>

  <!-- 情報テキスト -->
  <Style Selector="TextBlock.info-text">
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="Foreground" Value="#0277BD"/>
  </Style>

  <!-- 中国語変種セクション -->
  <Style Selector="Border.chinese-variant-section">
    <Setter Property="Background" Value="#FFF3E0"/>
    <Setter Property="BorderBrush" Value="#FF9800"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="4"/>
    <Setter Property="Padding" Value="12"/>
  </Style>

  <!-- 戦略オプション -->
  <Style Selector="RadioButton.strategy-option">
    <Setter Property="Padding" Value="12"/>
    <Setter Property="Margin" Value="0,5"/>
    <Setter Property="BorderBrush" Value="#E0E0E0"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="4"/>
  </Style>

  <Style Selector="RadioButton.strategy-option:checked">
    <Setter Property="BorderBrush" Value="#4CAF50"/>
    <Setter Property="Background" Value="#F1F8E9"/>
  </Style>

  <!-- フォールバックセクション -->
  <Style Selector="Border.fallback-section">
    <Setter Property="Background" Value="#FFF8E1"/>
    <Setter Property="BorderBrush" Value="#FFC107"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="4"/>
    <Setter Property="Padding" Value="12"/>
  </Style>

  <!-- 戦略詳細 -->
  <Style Selector="Border.strategy-detail">
    <Setter Property="Background" Value="#E8F5E8"/>
    <Setter Property="BorderBrush" Value="#4CAF50"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="4"/>
    <Setter Property="Padding" Value="12"/>
  </Style>

  <!-- 現在の状態 -->
  <Style Selector="Border.current-status">
    <Setter Property="Background" Value="White"/>
    <Setter Property="BorderBrush" Value="#E0E0E0"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="6"/>
    <Setter Property="Padding" Value="15"/>
  </Style>

  <!-- 基本統計 -->
  <Style Selector="Border.basic-stats">
    <Setter Property="Background" Value="#FAFAFA"/>
    <Setter Property="BorderBrush" Value="#E0E0E0"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="4"/>
    <Setter Property="Padding" Value="15"/>
  </Style>

  <!-- 統計ラベル -->
  <Style Selector="TextBlock.stat-label">
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Foreground" Value="#757575"/>
    <Setter Property="HorizontalAlignment" Value="Center"/>
  </Style>

  <!-- 統計値 -->
  <Style Selector="TextBlock.stat-value">
    <Setter Property="FontSize" Value="16"/>
    <Setter Property="FontWeight" Value="Bold"/>
    <Setter Property="Foreground" Value="#1976D2"/>
    <Setter Property="HorizontalAlignment" Value="Center"/>
  </Style>

  <!-- フォールバック通知 -->
  <Style Selector="Border.fallback-notification">
    <Setter Property="Background" Value="#FFF3CD"/>
    <Setter Property="BorderBrush" Value="#FF9800"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="4"/>
    <Setter Property="Padding" Value="12"/>
  </Style>

  <Style Selector="TextBlock.fallback-reason">
    <Setter Property="FontSize" Value="13"/>
    <Setter Property="Foreground" Value="#E65100"/>
  </Style>

  <!-- アップグレードボタン -->
  <Style Selector="Button.upgrade-button">
    <Setter Property="Background" Value="#FF9800"/>
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="CornerRadius" Value="4"/>
    <Setter Property="Padding" Value="8,4"/>
    <Setter Property="FontSize" Value="12"/>
  </Style>

  <Style Selector="Button.upgrade-button:pointerover">
    <Setter Property="Background" Value="#F57C00"/>
  </Style>

  <!-- テストボタン -->
  <Style Selector="Button.test-button">
    <Setter Property="Background" Value="#2196F3"/>
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="CornerRadius" Value="4"/>
    <Setter Property="Padding" Value="8,4"/>
    <Setter Property="FontSize" Value="12"/>
  </Style>

</Styles>
```

## 5. 実装手順

### 5.1 Phase 1: 基本UI構造（1-2日）

1. **UIファイル作成**
   - TranslationSettingsView.axaml / .axaml.cs
   - EngineSelectionControl.axaml / .axaml.cs
   - LanguagePairSelectionControl.axaml / .axaml.cs
   - TranslationStrategyControl.axaml / .axaml.cs
   - EngineStatusControl.axaml / .axaml.cs

2. **ViewModelファイル作成**
   - TranslationSettingsViewModel.cs
   - EngineSelectionViewModel.cs
   - LanguagePairSelectionViewModel.cs
   - TranslationStrategyViewModel.cs
   - EngineStatusViewModel.cs

3. **スタイルファイル作成**
   - Styles/TranslationSettingsStyles.axaml

### 5.2 Phase 2: 基本機能実装（2-3日）

1. **エンジン選択機能**
   - 無料/有料プラン判定
   - LocalOnly/CloudOnly切り替え
   - 状態表示連携

2. **言語ペア選択機能**
   - 対応言語ペア表示
   - 中国語変種選択
   - アプリ言語連動表示

3. **翻訳戦略選択機能**
   - Direct/TwoStage選択
   - フォールバック設定
   - 戦略説明表示

### 5.3 Phase 3: 状態監視統合（1-2日）

1. **TranslationEngineStatusService統合**
   - リアルタイム状態表示
   - エンジンヘルス監視
   - フォールバック通知

2. **設定保存・復元**
   - 永続化機能
   - 設定検証
   - デフォルト値管理

### 5.4 Phase 4: テスト・調整（1日）

1. **機能テスト**
   - エンジン切り替えテスト
   - 言語ペア選択テスト
   - 設定保存/復元テスト

2. **UI/UXテスト**
   - レスポンシブデザイン確認
   - エラー状態表示確認
   - アクセシビリティ確認

## 6. 技術的考慮事項

### 6.1 無料/有料プラン対応

```csharp
// IUserPlanService実装例
public interface IUserPlanService
{
    UserPlan GetCurrentPlan();
    bool HasCloudAccess { get; }
    bool CanUpgrade { get; }
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
```

### 6.2 アプリ言語連動

```csharp
// ILocalizationService実装例
public interface ILocalizationService
{
    CultureInfo CurrentCulture { get; }
    string GetTargetLanguageCode();
    string GetTargetLanguageDisplayName();
    event EventHandler<CultureChangedEventArgs> CultureChanged;
}
```

### 6.3 設定管理

```csharp
// TranslationSettings モデル
public class TranslationSettings
{
    public TranslationEngine SelectedEngine { get; set; } = TranslationEngine.LocalOnly;
    public List<string> EnabledLanguagePairs { get; set; } = new();
    public ChineseVariant ChineseVariant { get; set; } = ChineseVariant.Simplified;
    public TranslationStrategy TranslationStrategy { get; set; } = TranslationStrategy.Direct;
    public bool EnableFallback { get; set; } = true;
}
```

## 7. まとめ

この設計により、初期リリース向けの基本的な翻訳設定UIが実現できます：

**主要な特徴**：
- ✅ **無料/有料プラン対応**: 適切なアクセス制御
- ✅ **初期リリーススコープ**: 必要最小限の機能に集中
- ✅ **技術基盤活用**: 既存サービスとの適切な統合
- ✅ **ユーザビリティ**: 分かりやすいUI設計

**次のステップ**：
1. Phase 1の基本UI構造作成から開始
2. 既存のTranslationEngineStatusService等との統合
3. 段階的な機能実装とテスト
4. v1.1での拡張機能追加準備

*実装完了時期: 1週間程度*