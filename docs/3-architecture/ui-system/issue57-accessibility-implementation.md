# Issue 57 アクセシビリティ実装

## AccessibilityHelper.cs の内容

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;

namespace Baketa.UI.Helpers
{
    /// <summary>
    /// アクセシビリティ関連のヘルパーメソッドを提供します。
    /// </summary>
    public static class AccessibilityHelper
    {
        /// <summary>
        /// コントロールにスクリーンリーダー用の名前を設定します。
        /// </summary>
        /// <typeparam name="T">コントロール型</typeparam>
        /// <param name="element">対象コントロール</param>
        /// <param name="name">設定する名前</param>
        /// <returns>設定後のコントロール</returns>
        public static T WithLabel<T>(this T element, string name) where T : Control
        {
            AutomationProperties.SetName(element, name);
            return element;
        }
        
        /// <summary>
        /// コントロールにスクリーンリーダー用のヘルプテキストを設定します。
        /// </summary>
        /// <typeparam name="T">コントロール型</typeparam>
        /// <param name="element">対象コントロール</param>
        /// <param name="helpText">設定するヘルプテキスト</param>
        /// <returns>設定後のコントロール</returns>
        public static T WithHelpText<T>(this T element, string helpText) where T : Control
        {
            AutomationProperties.SetHelpText(element, helpText);
            return element;
        }
        
        /// <summary>
        /// コントロールにアクセシビリティプロパティを一括設定します。
        /// </summary>
        /// <typeparam name="T">コントロール型</typeparam>
        /// <param name="element">対象コントロール</param>
        /// <param name="name">設定する名前</param>
        /// <param name="helpText">設定するヘルプテキスト</param>
        /// <returns>設定後のコントロール</returns>
        public static T WithAccessibility<T>(this T element, string name, string helpText) where T : Control
        {
            AutomationProperties.SetName(element, name);
            AutomationProperties.SetHelpText(element, helpText);
            return element;
        }
        
        /// <summary>
        /// コントロールにアクセシビリティラベルを設定します。
        /// </summary>
        /// <typeparam name="T">コントロール型</typeparam>
        /// <param name="element">対象コントロール</param>
        /// <param name="labelElement">ラベル要素</param>
        /// <returns>設定後のコントロール</returns>
        public static T WithLabelledBy<T>(this T element, Control labelElement) where T : Control
        {
            AutomationProperties.SetLabelledBy(element, labelElement);
            return element;
        }
        
        /// <summary>
        /// コントロールにアクセシビリティフレームワークでのコントロール種類を設定します。
        /// </summary>
        /// <typeparam name="T">コントロール型</typeparam>
        /// <param name="element">対象コントロール</param>
        /// <param name="controlType">コントロール種類</param>
        /// <returns>設定後のコントロール</returns>
        public static T WithControlType<T>(this T element, AutomationControlType controlType) where T : Control
        {
            AutomationProperties.SetControlType(element, controlType);
            return element;
        }
    }
}
```

## ViewとViewModelへの適用例

### 1. MainWindow.axaml.cs でのアクセシビリティ設定

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Baketa.UI.Helpers;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Views
{
    public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
    {
        public MainWindow()
        {
            InitializeComponent();
            
            this.WhenActivated(disposables => 
            {
                // アクセシビリティ設定を適用
                ApplyAccessibility();
            });
        }

        private void ApplyAccessibility()
        {
            // ウィンドウ自体の設定
            this.WithAccessibility("Baketa メインウィンドウ", "Baketaアプリケーションのメインウィンドウです");
            
            // ホームタブ
            FindControl<TabItem>("HomeTab")
                ?.WithAccessibility("ホーム画面", "アプリケーションのホーム画面を表示します");
            
            // キャプチャタブ
            FindControl<TabItem>("CaptureTab")
                ?.WithAccessibility("キャプチャ設定", "キャプチャ領域と設定の管理を行います");
            
            // 翻訳タブ
            FindControl<TabItem>("TranslationTab")
                ?.WithAccessibility("翻訳設定", "翻訳エンジンと言語設定を管理します");
            
            // オーバーレイタブ
            FindControl<TabItem>("OverlayTab")
                ?.WithAccessibility("オーバーレイ設定", "翻訳結果表示のオーバーレイ設定を管理します");
            
            // 履歴タブ
            FindControl<TabItem>("HistoryTab")
                ?.WithAccessibility("翻訳履歴", "過去の翻訳履歴を表示します");
                
            // メニュー項目
            FindControl<MenuItem>("FileMenuItem")
                ?.WithLabel("ファイルメニュー");
            
            FindControl<MenuItem>("CaptureMenuItem")
                ?.WithLabel("キャプチャメニュー");
                
            FindControl<MenuItem>("HelpMenuItem")
                ?.WithLabel("ヘルプメニュー");
                
            // ステータスバー
            FindControl<TextBlock>("StatusText")
                ?.WithLabel("ステータスメッセージ");
                
            FindControl<ProgressBar>("ProgressIndicator")
                ?.WithLabel("処理進捗状況");
        }
    }
}
```

### 2. MainWindow.axaml でのアクセシビリティ定義

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:Baketa.UI.ViewModels"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:local="using:Baketa.UI.Views"
        mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="600"
        x:Class="Baketa.UI.Views.MainWindow"
        x:DataType="vm:MainWindowViewModel"
        Icon="/Assets/avalonia-logo.ico"
        Title="Baketa - ゲーム翻訳オーバーレイ"
        MinWidth="600" MinHeight="400"
        AutomationProperties.Name="Baketa メインウィンドウ"
        AutomationProperties.HelpText="Baketaアプリケーションのメインウィンドウです">

    <Design.DataContext>
        <vm:MainWindowViewModel/>
    </Design.DataContext>

    <Grid RowDefinitions="Auto,*,Auto">
        <!-- ヘッダー -->
        <Grid Grid.Row="0" ColumnDefinitions="Auto,*,Auto" Background="{DynamicResource HeaderBackground}">
            <StackPanel Grid.Column="0" Orientation="Horizontal" Margin="10,5">
                <Image Source="/Assets/baketa-logo.png" Width="24" Height="24" Margin="0,0,10,0"
                       AutomationProperties.Name="Baketaロゴ" />
                <TextBlock Text="Baketa" VerticalAlignment="Center" FontWeight="Bold"
                           AutomationProperties.Name="アプリケーション名" />
            </StackPanel>
            
            <Menu Grid.Column="1" Margin="20,0">
                <MenuItem x:Name="FileMenuItem" Header="ファイル" AutomationProperties.Name="ファイルメニュー">
                    <MenuItem Header="設定" Command="{Binding OpenSettingsCommand}" 
                              AutomationProperties.Name="設定"
                              AutomationProperties.HelpText="アプリケーション設定を開きます" />
                    <Separator />
                    <MenuItem Header="終了" Command="{Binding ExitCommand}" 
                              AutomationProperties.Name="終了"
                              AutomationProperties.HelpText="アプリケーションを終了します" />
                </MenuItem>
                <MenuItem x:Name="CaptureMenuItem" Header="キャプチャ" AutomationProperties.Name="キャプチャメニュー">
                    <MenuItem Header="キャプチャ開始" Command="{Binding StartCaptureCommand}" 
                              AutomationProperties.Name="キャプチャ開始"
                              AutomationProperties.HelpText="画面キャプチャを開始します" />
                    <MenuItem Header="キャプチャ停止" Command="{Binding StopCaptureCommand}" 
                              AutomationProperties.Name="キャプチャ停止"
                              AutomationProperties.HelpText="画面キャプチャを停止します" />
                    <MenuItem Header="領域選択" Command="{Binding SelectRegionCommand}" 
                              AutomationProperties.Name="領域選択"
                              AutomationProperties.HelpText="キャプチャする画面領域を選択します" />
                </MenuItem>
                <MenuItem x:Name="HelpMenuItem" Header="ヘルプ" AutomationProperties.Name="ヘルプメニュー">
                    <MenuItem Header="使い方" Command="{Binding OpenHelpCommand}" 
                              AutomationProperties.Name="使い方"
                              AutomationProperties.HelpText="ヘルプドキュメントを開きます" />
                    <MenuItem Header="バージョン情報" Command="{Binding OpenAboutCommand}" 
                              AutomationProperties.Name="バージョン情報"
                              AutomationProperties.HelpText="アプリケーションのバージョン情報を表示します" />
                </MenuItem>
            </Menu>
        </Grid>

        <!-- メインコンテンツエリア -->
        <TabControl Grid.Row="1" TabStripPlacement="Left" SelectedIndex="{Binding SelectedTabIndex}">
            <TabItem x:Name="HomeTab" ToolTip.Tip="ホーム画面"
                     AutomationProperties.Name="ホーム画面"
                     AutomationProperties.HelpText="アプリケーションのホーム画面を表示します">
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <PathIcon Data="{StaticResource HomeIcon}" Width="16" Height="16"/>
                        <TextBlock Text="ホーム" VerticalAlignment="Center"/>
                    </StackPanel>
                </TabItem.Header>
                <local:HomeView DataContext="{Binding HomeViewModel}" />
            </TabItem>
            
            <TabItem x:Name="CaptureTab" ToolTip.Tip="キャプチャ設定"
                     AutomationProperties.Name="キャプチャ設定"
                     AutomationProperties.HelpText="キャプチャ領域と設定の管理を行います">
                <TabItem.Header>
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <PathIcon Data="{StaticResource CaptureIcon}" Width="16" Height="16"/>
                        <TextBlock Text="キャプチャ" VerticalAlignment="Center"/>
                    </StackPanel>
                </TabItem.Header>
                <local:CaptureView DataContext="{Binding CaptureViewModel}" />
            </TabItem>
            
            <!-- 他のタブも同様に定義 -->
        </TabControl>
        
        <!-- ステータスバー -->
        <Grid Grid.Row="2" ColumnDefinitions="*,Auto" Background="{DynamicResource StatusBarBackground}">
            <StackPanel Grid.Column="0" Orientation="Horizontal" Margin="10,2">
                <TextBlock x:Name="StatusText" Text="{Binding StatusMessage}" VerticalAlignment="Center"
                           AutomationProperties.Name="ステータスメッセージ" />
            </StackPanel>
            
            <StackPanel Grid.Column="1" Orientation="Horizontal" Margin="10,2">
                <TextBlock Text="{Binding TranslationEngine}" Margin="0,0,10,0" VerticalAlignment="Center"
                           AutomationProperties.Name="使用中の翻訳エンジン" />
                <TextBlock Text="{Binding CaptureStatus}" Margin="0,0,10,0" VerticalAlignment="Center"
                           AutomationProperties.Name="キャプチャ状態" />
                <ProgressBar x:Name="ProgressIndicator" Value="{Binding Progress}" Width="100" Height="16" Margin="0,0,10,0"
                             IsVisible="{Binding IsProcessing}"
                             AutomationProperties.Name="処理進捗状況" />
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

### 3. キーボードナビゲーション例（CaptureView.axaml）

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Baketa.UI.ViewModels"
             x:Class="Baketa.UI.Views.CaptureView"
             x:DataType="vm:CaptureViewModel">
    
    <StackPanel Spacing="16" Margin="20">
        <TextBlock Text="キャプチャ設定" FontSize="18" FontWeight="SemiBold"
                   AutomationProperties.Name="キャプチャ設定見出し" />
        
        <!-- キャプチャモード選択 -->
        <StackPanel Spacing="8">
            <TextBlock Text="キャプチャモード:" 
                       AutomationProperties.Name="キャプチャモードラベル" />
            <ComboBox ItemsSource="{Binding CaptureModes}" 
                      SelectedItem="{Binding SelectedCaptureMode}"
                      HorizontalAlignment="Left" Width="200"
                      TabIndex="0"
                      KeyboardNavigation.TabNavigation="Local"
                      AutomationProperties.Name="キャプチャモード選択"
                      AutomationProperties.HelpText="使用するキャプチャモードを選択します" />
        </StackPanel>
        
        <!-- キャプチャ間隔設定 -->
        <StackPanel Spacing="8">
            <TextBlock Text="キャプチャ間隔 (ミリ秒):" 
                       AutomationProperties.Name="キャプチャ間隔ラベル" />
            <NumericUpDown Value="{Binding CaptureInterval}" 
                          Minimum="100" Maximum="5000" Increment="100"
                          HorizontalAlignment="Left" Width="200"
                          TabIndex="1"
                          KeyboardNavigation.TabNavigation="Local"
                          AutomationProperties.Name="キャプチャ間隔設定"
                          AutomationProperties.HelpText="キャプチャ間隔をミリ秒単位で設定します" />
        </StackPanel>
        
        <!-- キャプチャ設定ボタン -->
        <StackPanel Orientation="Horizontal" Spacing="10" Margin="0,20,0,0">
            <Button Content="キャプチャ開始" Command="{Binding StartCaptureCommand}"
                    TabIndex="2"
                    KeyboardNavigation.TabNavigation="Local"
                    AutomationProperties.Name="キャプチャ開始ボタン"
                    AutomationProperties.HelpText="設定された領域のキャプチャを開始します" />
            
            <Button Content="キャプチャ停止" Command="{Binding StopCaptureCommand}"
                    TabIndex="3"
                    KeyboardNavigation.TabNavigation="Local"
                    AutomationProperties.Name="キャプチャ停止ボタン"
                    AutomationProperties.HelpText="実行中のキャプチャを停止します" />
            
            <Button Content="領域選択" Command="{Binding SelectRegionCommand}"
                    TabIndex="4"
                    KeyboardNavigation.TabNavigation="Local"
                    AutomationProperties.Name="領域選択ボタン"
                    AutomationProperties.HelpText="キャプチャする画面領域を選択します" />
        </StackPanel>
    </StackPanel>
</UserControl>
```

## アクセシビリティ設定ビューモデル例

```csharp
using System.Reactive;
using ReactiveUI;
using Baketa.Core.Events;

namespace Baketa.UI.ViewModels
{
    public class AccessibilitySettingsViewModel : ViewModelBase
    {
        // アニメーション無効化フラグ
        private bool _disableAnimations;
        public bool DisableAnimations
        {
            get => _disableAnimations;
            set => this.RaiseAndSetIfChanged(ref _disableAnimations, value);
        }
        
        // ハイコントラストモード
        private bool _highContrastMode;
        public bool HighContrastMode
        {
            get => _highContrastMode;
            set => this.RaiseAndSetIfChanged(ref _highContrastMode, value);
        }
        
        // フォントサイズ倍率
        private double _fontScaleFactor = 1.0;
        public double FontScaleFactor
        {
            get => _fontScaleFactor;
            set => this.RaiseAndSetIfChanged(ref _fontScaleFactor, value);
        }
        
        // 設定保存コマンド
        public ReactiveCommand<Unit, Unit> SaveSettingsCommand { get; }
        
        // コンストラクタ
        public AccessibilitySettingsViewModel(IEventAggregator eventAggregator, ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            // 設定保存コマンドの定義
            SaveSettingsCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                // 設定保存ロジック
                await SaveSettingsAsync();
                
                // アクセシビリティ設定変更イベントを発行
                await _eventAggregator.PublishAsync(new AccessibilitySettingsChangedEvent
                {
                    DisableAnimations = DisableAnimations,
                    HighContrastMode = HighContrastMode,
                    FontScaleFactor = FontScaleFactor
                });
            });
            
            // 設定の読み込み
            LoadSettings();
        }
        
        // 設定読み込みメソッド
        private void LoadSettings()
        {
            // 設定読み込みロジック
            // ...
        }
        
        // 設定保存メソッド
        private async Task SaveSettingsAsync()
        {
            // 設定保存ロジック
            // ...
        }
    }
    
    // アクセシビリティ設定変更イベント
    public class AccessibilitySettingsChangedEvent : EventBase
    {
        public bool DisableAnimations { get; set; }
        public bool HighContrastMode { get; set; }
        public double FontScaleFactor { get; set; }
        
        public override string EventId => "AccessibilitySettingsChanged";
    }
}
```
