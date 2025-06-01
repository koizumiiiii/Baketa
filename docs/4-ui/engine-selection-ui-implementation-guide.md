# エンジン選択UI実装ガイド - 戦略簡素化対応版

*作成日: 2025年6月1日*  
*ステータス: 実装準備完了*

## 1. 実装概要

翻訳戦略の簡素化（5戦略→2戦略）に伴い、エンジン選択UIを**LocalOnly vs CloudOnly + 自動フォールバック**の構成に変更します。

### 1.1 変更内容サマリー

**従来（廃止）**：
- ❌ OPUS-MT vs Gemini API vs Hybrid（3択選択）
- ❌ 複雑な翻訳戦略設定

**新設計（実装対象）**：
- ✅ LocalOnly vs CloudOnly（2択選択）
- ✅ フォールバック設定（チェックボックス）
- ✅ リアルタイム状態表示（ステータスインジケーター）

## 2. UI構成設計

### 2.1 メイン設定画面レイアウト

```xml
<!-- TranslationSettingsView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Baketa.UI.Views.Settings.TranslationSettingsView">
  
  <StackPanel Spacing="20">
    
    <!-- エンジン選択セクション -->
    <Border Classes="section-border">
      <StackPanel>
        <TextBlock Text="翻訳エンジン選択" Classes="section-header"/>
        <ContentControl Content="{Binding EngineSelection}"/>
      </StackPanel>
    </Border>
    
    <!-- 中国語変種選択セクション -->
    <Border Classes="section-border">
      <StackPanel>
        <TextBlock Text="中国語変種設定" Classes="section-header"/>
        <ContentControl Content="{Binding ChineseVariantSelection}"/>
      </StackPanel>
    </Border>
    
    <!-- フォールバック設定セクション -->
    <Border Classes="section-border">
      <StackPanel>
        <TextBlock Text="フォールバック設定" Classes="section-header"/>
        <ContentControl Content="{Binding FallbackSettings}"/>
      </StackPanel>
    </Border>
    
    <!-- 状態表示セクション -->
    <Border Classes="section-border">
      <StackPanel>
        <TextBlock Text="翻訳エンジン状態" Classes="section-header"/>
        <ContentControl Content="{Binding EngineStatus}"/>
      </StackPanel>
    </Border>
    
  </StackPanel>
</UserControl>
```

### 2.2 エンジン選択コントロール

```xml
<!-- EngineSelectionControl.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Baketa.UI.Views.Settings.EngineSelectionControl">
  
  <StackPanel Spacing="15">
    
    <!-- エンジン選択ラジオボタン -->
    <StackPanel Spacing="10">
      
      <RadioButton GroupName="TranslationEngine" 
                   IsChecked="{Binding IsLocalOnlySelected}"
                   Classes="engine-radio">
        <StackPanel>
          <TextBlock Text="LocalOnly（ローカル翻訳）" FontWeight="Bold"/>
          <TextBlock Text="• OPUS-MT専用" Classes="description"/>
          <TextBlock Text="• 高速・無料・オフライン対応" Classes="description"/>
          <TextBlock Text="• 適用: 短いテキスト、一般的翻訳" Classes="description"/>
          <TextBlock Text="{Binding LocalEnginePerformance}" Classes="performance"/>
        </StackPanel>
      </RadioButton>
      
      <RadioButton GroupName="TranslationEngine" 
                   IsChecked="{Binding IsCloudOnlySelected}"
                   Classes="engine-radio">
        <StackPanel>
          <TextBlock Text="CloudOnly（クラウド翻訳）" FontWeight="Bold"/>
          <TextBlock Text="• Gemini API専用" Classes="description"/>
          <TextBlock Text="• 高品質・有料・ネットワーク必須" Classes="description"/>
          <TextBlock Text="• 適用: 複雑なテキスト、専門用語" Classes="description"/>
          <TextBlock Text="{Binding CloudEnginePerformance}" Classes="performance"/>
        </StackPanel>
      </RadioButton>
      
    </StackPanel>
    
    <!-- 選択されたエンジンの詳細情報 -->
    <Border Classes="info-border">
      <StackPanel>
        <TextBlock Text="選択中のエンジン詳細" FontWeight="Bold"/>
        <TextBlock Text="{Binding SelectedEngineDescription}" TextWrapping="Wrap"/>
        <TextBlock Text="{Binding EstimatedCostInfo}" Classes="cost-info"/>
      </StackPanel>
    </Border>
    
  </StackPanel>
</UserControl>
```

### 2.3 フォールバック設定コントロール

```xml
<!-- FallbackSettingsControl.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Baketa.UI.Views.Settings.FallbackSettingsControl">
  
  <StackPanel Spacing="10">
    
    <TextBlock Text="自動フォールバック設定" FontWeight="Bold"/>
    <TextBlock Text="CloudOnly選択時に以下の状況でLocalOnlyに自動切り替え" 
               Classes="description"/>
    
    <CheckBox IsChecked="{Binding EnableRateLimitFallback}"
              Content="レート制限時の自動フォールバック"/>
    
    <CheckBox IsChecked="{Binding EnableNetworkErrorFallback}"
              Content="ネットワークエラー時の自動フォールバック"/>
    
    <CheckBox IsChecked="{Binding EnableApiErrorFallback}"
              Content="APIエラー時の自動フォールバック"/>
    
    <CheckBox IsChecked="{Binding ShowFallbackNotifications}"
              Content="フォールバック発生時の通知表示"/>
    
    <!-- フォールバック詳細設定 -->
    <Expander Header="詳細設定" IsExpanded="False">
      <StackPanel Spacing="5">
        
        <StackPanel Orientation="Horizontal" Spacing="10">
          <TextBlock Text="フォールバック判定タイムアウト:"/>
          <NumericUpDown Value="{Binding FallbackTimeoutSeconds}" 
                         Minimum="5" Maximum="60" Increment="5"/>
          <TextBlock Text="秒"/>
        </StackPanel>
        
        <StackPanel Orientation="Horizontal" Spacing="10">
          <TextBlock Text="自動復旧チェック間隔:"/>
          <NumericUpDown Value="{Binding RecoveryCheckIntervalMinutes}" 
                         Minimum="1" Maximum="30" Increment="1"/>
          <TextBlock Text="分"/>
        </StackPanel>
        
      </StackPanel>
    </Expander>
    
  </StackPanel>
</UserControl>
```

### 2.4 エンジン状態表示コントロール

```xml
<!-- EngineStatusControl.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Baketa.UI.Views.Settings.EngineStatusControl">
  
  <StackPanel Spacing="15">
    
    <!-- 現在の状態表示 -->
    <Border Classes="status-border">
      <StackPanel Orientation="Horizontal" Spacing="10">
        
        <Ellipse Width="12" Height="12" 
                 Fill="{Binding CurrentStatusColor}"/>
        
        <TextBlock Text="{Binding CurrentStatusText}" 
                   FontWeight="Bold"/>
        
        <TextBlock Text="{Binding CurrentEngineInfo}" 
                   Classes="engine-info"/>
        
      </StackPanel>
    </Border>
    
    <!-- フォールバック履歴 -->
    <Border Classes="history-border" 
            IsVisible="{Binding HasFallbackHistory}">
      <StackPanel>
        
        <TextBlock Text="フォールバック履歴" FontWeight="Bold"/>
        
        <ItemsControl ItemsSource="{Binding FallbackHistory}">
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <StackPanel Orientation="Horizontal" Spacing="10" Margin="0,2">
                <TextBlock Text="{Binding Timestamp, StringFormat='{}{0:HH:mm:ss}'}" 
                           Classes="timestamp"/>
                <TextBlock Text="{Binding Reason}"/>
                <TextBlock Text="{Binding Duration}" Classes="duration"/>
              </StackPanel>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
        
      </StackPanel>
    </Border>
    
    <!-- パフォーマンス統計 -->
    <Border Classes="stats-border">
      <Grid ColumnDefinitions="*,*,*">
        
        <StackPanel Grid.Column="0">
          <TextBlock Text="平均レイテンシ" Classes="stat-label"/>
          <TextBlock Text="{Binding AverageLatency}" Classes="stat-value"/>
        </StackPanel>
        
        <StackPanel Grid.Column="1">
          <TextBlock Text="成功率" Classes="stat-label"/>
          <TextBlock Text="{Binding SuccessRate}" Classes="stat-value"/>
        </StackPanel>
        
        <StackPanel Grid.Column="2">
          <TextBlock Text="フォールバック率" Classes="stat-label"/>
          <TextBlock Text="{Binding FallbackRate}" Classes="stat-value"/>
        </StackPanel>
        
      </Grid>
    </Border>
    
  </StackPanel>
</UserControl>
```

## 3. ViewModel実装

### 3.1 メイン設定ViewModel

```csharp
// TranslationSettingsViewModel.cs
using Baketa.Core.Translation.Models;
using ReactiveUI;
using System.Reactive;

namespace Baketa.UI.ViewModels.Settings
{
    public class TranslationSettingsViewModel : ViewModelBase
    {
        private readonly ITranslationConfigurationService _configService;
        private readonly IHybridTranslationEngine _hybridEngine;
        
        public TranslationSettingsViewModel(
            ITranslationConfigurationService configService,
            IHybridTranslationEngine hybridEngine)
        {
            _configService = configService;
            _hybridEngine = hybridEngine;
            
            // 子ViewModelの初期化
            EngineSelection = new EngineSelectionViewModel(configService);
            FallbackSettings = new FallbackSettingsViewModel(configService);
            EngineStatus = new EngineStatusViewModel(hybridEngine);
            ChineseVariantSelection = new ChineseVariantSelectionViewModel(configService);
            
            // コマンドの初期化
            SaveSettingsCommand = ReactiveCommand.CreateFromTask(SaveSettingsAsync);
            ResetSettingsCommand = ReactiveCommand.CreateFromTask(ResetSettingsAsync);
            TestEngineCommand = ReactiveCommand.CreateFromTask(TestEngineAsync);
        }
        
        public EngineSelectionViewModel EngineSelection { get; }
        public FallbackSettingsViewModel FallbackSettings { get; }
        public EngineStatusViewModel EngineStatus { get; }
        public ChineseVariantSelectionViewModel ChineseVariantSelection { get; }
        
        public ReactiveCommand<Unit, Unit> SaveSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> TestEngineCommand { get; }
        
        private async Task SaveSettingsAsync()
        {
            var config = new TranslationConfiguration
            {
                DefaultStrategy = EngineSelection.SelectedStrategy,
                EnableRateLimitFallback = FallbackSettings.EnableRateLimitFallback,
                EnableNetworkErrorFallback = FallbackSettings.EnableNetworkErrorFallback,
                EnableApiErrorFallback = FallbackSettings.EnableApiErrorFallback,
                DefaultChineseVariant = ChineseVariantSelection.SelectedVariant,
                // その他の設定...
            };
            
            await _configService.SaveConfigurationAsync(config);
        }
        
        private async Task ResetSettingsAsync()
        {
            await _configService.ResetToDefaultAsync();
            // ViewModelを再初期化
            await LoadSettingsAsync();
        }
        
        private async Task TestEngineAsync()
        {
            var testRequest = new TranslationRequest
            {
                SourceText = "Hello, world!",
                SourceLanguage = LanguageInfo.English,
                TargetLanguage = LanguageInfo.Japanese
            };
            
            var response = await _hybridEngine.TranslateAsync(testRequest);
            // テスト結果をユーザーに表示
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
        private readonly ITranslationConfigurationService _configService;
        private TranslationStrategy _selectedStrategy = TranslationStrategy.LocalOnly;
        
        public EngineSelectionViewModel(ITranslationConfigurationService configService)
        {
            _configService = configService;
            
            // 初期設定読み込み
            LoadCurrentConfiguration();
            
            // プロパティ変更の監視
            this.WhenAnyValue(x => x.SelectedStrategy)
                .Subscribe(UpdateEngineDescription);
        }
        
        public TranslationStrategy SelectedStrategy
        {
            get => _selectedStrategy;
            set => this.RaiseAndSetIfChanged(ref _selectedStrategy, value);
        }
        
        public bool IsLocalOnlySelected
        {
            get => SelectedStrategy == TranslationStrategy.LocalOnly;
            set { if (value) SelectedStrategy = TranslationStrategy.LocalOnly; }
        }
        
        public bool IsCloudOnlySelected
        {
            get => SelectedStrategy == TranslationStrategy.CloudOnly;
            set { if (value) SelectedStrategy = TranslationStrategy.CloudOnly; }
        }
        
        [Reactive]
        public string SelectedEngineDescription { get; private set; } = string.Empty;
        
        [Reactive]
        public string LocalEnginePerformance { get; private set; } = "平均レイテンシ: < 50ms";
        
        [Reactive]
        public string CloudEnginePerformance { get; private set; } = "平均レイテンシ: < 2000ms";
        
        [Reactive]
        public string EstimatedCostInfo { get; private set; } = string.Empty;
        
        private void UpdateEngineDescription(TranslationStrategy strategy)
        {
            SelectedEngineDescription = strategy switch
            {
                TranslationStrategy.LocalOnly => 
                    "OPUS-MT専用エンジン\n" +
                    "✅ 高速処理（50ms以下）\n" +
                    "✅ 完全無料\n" +
                    "✅ オフライン対応\n" +
                    "📝 適用: 短いテキスト、一般的な翻訳\n" +
                    "🎯 品質: 標準品質",
                
                TranslationStrategy.CloudOnly => 
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
            
            EstimatedCostInfo = strategy switch
            {
                TranslationStrategy.LocalOnly => "📊 コスト: 無料（モデルダウンロード時のみ通信）",
                TranslationStrategy.CloudOnly => "📊 コスト: 約 $0.01-0.05 / 1000文字（文字数により変動）",
                _ => ""
            };
        }
        
        private async void LoadCurrentConfiguration()
        {
            var config = await _configService.GetConfigurationAsync();
            SelectedStrategy = config.DefaultStrategy;
        }
    }
}
```

### 3.3 フォールバック設定ViewModel

```csharp
// FallbackSettingsViewModel.cs
using ReactiveUI;

namespace Baketa.UI.ViewModels.Settings
{
    public class FallbackSettingsViewModel : ViewModelBase
    {
        private readonly ITranslationConfigurationService _configService;
        
        public FallbackSettingsViewModel(ITranslationConfigurationService configService)
        {
            _configService = configService;
            LoadCurrentConfiguration();
        }
        
        [Reactive]
        public bool EnableRateLimitFallback { get; set; } = true;
        
        [Reactive]
        public bool EnableNetworkErrorFallback { get; set; } = true;
        
        [Reactive]
        public bool EnableApiErrorFallback { get; set; } = true;
        
        [Reactive]
        public bool ShowFallbackNotifications { get; set; } = true;
        
        [Reactive]
        public int FallbackTimeoutSeconds { get; set; } = 10;
        
        [Reactive]
        public int RecoveryCheckIntervalMinutes { get; set; } = 5;
        
        private async void LoadCurrentConfiguration()
        {
            var config = await _configService.GetConfigurationAsync();
            EnableRateLimitFallback = config.EnableRateLimitFallback;
            EnableNetworkErrorFallback = config.EnableNetworkErrorFallback;
            EnableApiErrorFallback = config.EnableApiErrorFallback;
            ShowFallbackNotifications = config.ShowFallbackNotifications;
            FallbackTimeoutSeconds = config.FallbackTimeoutSeconds;
            RecoveryCheckIntervalMinutes = config.RecoveryCheckIntervalMinutes;
        }
    }
}
```

### 3.4 エンジン状態表示ViewModel

```csharp
// EngineStatusViewModel.cs
using ReactiveUI;
using System.Collections.ObjectModel;

namespace Baketa.UI.ViewModels.Settings
{
    public class EngineStatusViewModel : ViewModelBase
    {
        private readonly IHybridTranslationEngine _hybridEngine;
        private readonly Timer _statusUpdateTimer;
        
        public EngineStatusViewModel(IHybridTranslationEngine hybridEngine)
        {
            _hybridEngine = hybridEngine;
            FallbackHistory = new ObservableCollection<FallbackHistoryItem>();
            
            // 定期的な状態更新
            _statusUpdateTimer = new Timer(UpdateStatus, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
            
            // エンジンのイベント監視
            _hybridEngine.FallbackOccurred += OnFallbackOccurred;
            _hybridEngine.EngineRecovered += OnEngineRecovered;
        }
        
        [Reactive]
        public string CurrentStatusColor { get; private set; } = "#4CAF50"; // Green
        
        [Reactive]
        public string CurrentStatusText { get; private set; } = "正常動作中";
        
        [Reactive]
        public string CurrentEngineInfo { get; private set; } = "LocalOnly";
        
        [Reactive]
        public string AverageLatency { get; private set; } = "N/A";
        
        [Reactive]
        public string SuccessRate { get; private set; } = "N/A";
        
        [Reactive]
        public string FallbackRate { get; private set; } = "N/A";
        
        [Reactive]
        public bool HasFallbackHistory { get; private set; }
        
        public ObservableCollection<FallbackHistoryItem> FallbackHistory { get; }
        
        private void UpdateStatus(object? state)
        {
            var statistics = _hybridEngine.GetStatistics();
            
            AverageLatency = $"{statistics.AverageLatencyMs:F1}ms";
            SuccessRate = $"{statistics.SuccessRate:P1}";
            FallbackRate = $"{statistics.FallbackRate:P1}";
            
            // 現在の状態を更新
            if (statistics.IsInFallbackMode)
            {
                CurrentStatusColor = "#FF9800"; // Orange
                CurrentStatusText = "フォールバック中";
                CurrentEngineInfo = $"{statistics.FallbackReason} により LocalOnly で動作中";
            }
            else
            {
                CurrentStatusColor = "#4CAF50"; // Green
                CurrentStatusText = "正常動作中";
                CurrentEngineInfo = $"{statistics.CurrentEngine} で動作中";
            }
        }
        
        private void OnFallbackOccurred(object? sender, FallbackEventArgs e)
        {
            var historyItem = new FallbackHistoryItem
            {
                Timestamp = DateTime.Now,
                Reason = e.Reason,
                FromEngine = e.FromEngine,
                ToEngine = e.ToEngine
            };
            
            // UIスレッドで履歴を更新
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                FallbackHistory.Insert(0, historyItem);
                
                // 履歴を最新20件に制限
                while (FallbackHistory.Count > 20)
                {
                    FallbackHistory.RemoveAt(FallbackHistory.Count - 1);
                }
                
                HasFallbackHistory = FallbackHistory.Count > 0;
            });
        }
        
        private void OnEngineRecovered(object? sender, RecoveryEventArgs e)
        {
            // 復旧時の処理
            if (FallbackHistory.Count > 0)
            {
                var lastItem = FallbackHistory[0];
                lastItem.Duration = $"{(DateTime.Now - lastItem.Timestamp).TotalSeconds:F1}秒";
            }
        }
        
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _statusUpdateTimer?.Dispose();
                _hybridEngine.FallbackOccurred -= OnFallbackOccurred;
                _hybridEngine.EngineRecovered -= OnEngineRecovered;
            }
            base.Dispose(disposing);
        }
    }
    
    public class FallbackHistoryItem
    {
        public DateTime Timestamp { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string FromEngine { get; set; } = string.Empty;
        public string ToEngine { get; set; } = string.Empty;
        public string Duration { get; set; } = "進行中";
    }
}
```

## 4. スタイル定義

### 4.1 CSS/スタイル

```xml
<!-- Styles/TranslationSettingsStyles.axaml -->
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- セクション境界 -->
  <Style Selector="Border.section-border">
    <Setter Property="BorderBrush" Value="#E0E0E0"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="8"/>
    <Setter Property="Padding" Value="15"/>
    <Setter Property="Margin" Value="0,0,0,10"/>
  </Style>

  <!-- セクションヘッダー -->
  <Style Selector="TextBlock.section-header">
    <Setter Property="FontSize" Value="16"/>
    <Setter Property="FontWeight" Value="Bold"/>
    <Setter Property="Margin" Value="0,0,0,10"/>
    <Setter Property="Foreground" Value="#2196F3"/>
  </Style>

  <!-- エンジン選択ラジオボタン -->
  <Style Selector="RadioButton.engine-radio">
    <Setter Property="Margin" Value="0,0,0,15"/>
    <Setter Property="Padding" Value="10"/>
    <Setter Property="BorderBrush" Value="#E0E0E0"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="5"/>
  </Style>

  <Style Selector="RadioButton.engine-radio:checked">
    <Setter Property="BorderBrush" Value="#2196F3"/>
    <Setter Property="Background" Value="#F3F9FF"/>
  </Style>

  <!-- 説明テキスト -->
  <Style Selector="TextBlock.description">
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Foreground" Value="#666666"/>
    <Setter Property="Margin" Value="10,0,0,2"/>
  </Style>

  <!-- パフォーマンス情報 -->
  <Style Selector="TextBlock.performance">
    <Setter Property="FontSize" Value="11"/>
    <Setter Property="Foreground" Value="#4CAF50"/>
    <Setter Property="FontWeight" Value="Bold"/>
    <Setter Property="Margin" Value="10,5,0,0"/>
  </Style>

  <!-- 情報境界 -->
  <Style Selector="Border.info-border">
    <Setter Property="Background" Value="#F5F5F5"/>
    <Setter Property="BorderBrush" Value="#E0E0E0"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="5"/>
    <Setter Property="Padding" Value="10"/>
  </Style>

  <!-- 状態表示境界 -->
  <Style Selector="Border.status-border">
    <Setter Property="Background" Value="#FAFAFA"/>
    <Setter Property="BorderBrush" Value="#E0E0E0"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CornerRadius" Value="5"/>
    <Setter Property="Padding" Value="10"/>
  </Style>

  <!-- コスト情報 -->
  <Style Selector="TextBlock.cost-info">
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="Foreground" Value="#FF9800"/>
    <Setter Property="FontStyle" Value="Italic"/>
  </Style>

  <!-- 統計値 -->
  <Style Selector="TextBlock.stat-label">
    <Setter Property="FontSize" Value="11"/>
    <Setter Property="Foreground" Value="#666666"/>
    <Setter Property="HorizontalAlignment" Value="Center"/>
  </Style>

  <Style Selector="TextBlock.stat-value">
    <Setter Property="FontSize" Value="14"/>
    <Setter Property="FontWeight" Value="Bold"/>
    <Setter Property="HorizontalAlignment" Value="Center"/>
    <Setter Property="Foreground" Value="#2196F3"/>
  </Style>

</Styles>
```

## 5. 実装手順

### 5.1 フェーズ1: 基本UI構造（1-2日）

1. **UIファイル作成**
   - TranslationSettingsView.axaml
   - EngineSelectionControl.axaml
   - FallbackSettingsControl.axaml
   - EngineStatusControl.axaml

2. **ViewModelファイル作成**
   - TranslationSettingsViewModel.cs
   - EngineSelectionViewModel.cs
   - FallbackSettingsViewModel.cs
   - EngineStatusViewModel.cs

3. **スタイルファイル作成**
   - TranslationSettingsStyles.axaml

### 5.2 フェーズ2: 機能実装（2-3日）

1. **設定データバインディング**
   - エンジン選択状態の管理
   - フォールバック設定の保存/読み込み
   - 設定変更の即座反映

2. **状態監視機能**
   - リアルタイム状態表示
   - フォールバック履歴管理
   - パフォーマンス統計表示

### 5.3 フェーズ3: 統合テスト（1日）

1. **動作テスト**
   - エンジン切り替えテスト
   - フォールバック動作テスト
   - 設定保存/復元テスト

2. **UI/UXテスト**
   - レスポンシブデザイン
   - アクセシビリティ
   - エラー表示

## 6. 設定ファイル連携

### 6.1 appsettings.json拡張

```json
{
  "TranslationUI": {
    "DefaultEngine": "LocalOnly",
    "ShowEnginePerformanceMetrics": true,
    "FallbackNotificationDuration": 5000,
    "StatusUpdateInterval": 5000,
    "MaxFallbackHistoryItems": 20
  },
  "HybridTranslation": {
    "DefaultStrategy": "LocalOnly",
    "EnableRateLimitFallback": true,
    "EnableNetworkErrorFallback": true,
    "EnableApiErrorFallback": true,
    "FallbackTimeoutSeconds": 10,
    "RecoveryCheckIntervalMinutes": 5,
    "ShowFallbackNotifications": true
  }
}
```

## 7. テスト戦略

### 7.1 単体テスト

```csharp
// EngineSelectionViewModelTests.cs
[Test]
public void SelectedStrategy_WhenChanged_UpdatesDescription()
{
    // Arrange
    var configService = Mock.Of<ITranslationConfigurationService>();
    var viewModel = new EngineSelectionViewModel(configService);
    
    // Act
    viewModel.SelectedStrategy = TranslationStrategy.CloudOnly;
    
    // Assert
    Assert.That(viewModel.SelectedEngineDescription, Contains.Substring("Gemini API"));
    Assert.That(viewModel.IsCloudOnlySelected, Is.True);
    Assert.That(viewModel.IsLocalOnlySelected, Is.False);
}
```

### 7.2 統合テスト

```csharp
// TranslationSettingsIntegrationTests.cs
[Test]
public async Task SaveSettings_WithValidConfiguration_PersistsCorrectly()
{
    // Arrange
    var settingsViewModel = CreateTranslationSettingsViewModel();
    settingsViewModel.EngineSelection.SelectedStrategy = TranslationStrategy.CloudOnly;
    settingsViewModel.FallbackSettings.EnableRateLimitFallback = false;
    
    // Act
    await settingsViewModel.SaveSettingsCommand.Execute();
    
    // Assert
    var savedConfig = await _configService.GetConfigurationAsync();
    Assert.That(savedConfig.DefaultStrategy, Is.EqualTo(TranslationStrategy.CloudOnly));
    Assert.That(savedConfig.EnableRateLimitFallback, Is.False);
}
```

---

## 8. まとめ

このUI実装により、翻訳戦略の簡素化（LocalOnly vs CloudOnly）を反映した**直感的で分かりやすいエンジン選択UI**が実現されます。

**主要な改善点**：
- ✅ **選択肢の簡素化**: 3択→2択でユーザーの混乱を解消
- ✅ **フォールバック機能の可視化**: 自動切り替えの透明性向上
- ✅ **リアルタイム状態表示**: 現在の動作状況を明確に表示
- ✅ **パフォーマンス統計**: 実際の使用状況に基づいた情報提供

**実装完了後の効果**：
- ユーザーは翻訳エンジンの特性を理解しやすくなる
- フォールバック動作が透明化され、信頼性向上
- 設定変更の影響を即座に確認可能
- トラブルシューティングが容易になる

---

*最終更新: 2025年6月1日*  
*ステータス: 実装準備完了・開発開始可能* ✅🚀
