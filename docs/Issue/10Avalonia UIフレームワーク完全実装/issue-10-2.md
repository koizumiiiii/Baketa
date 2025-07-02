# Issue 10-2: メインウィンドウUIデザインの実装

## 概要
Baketaアプリケーションのメインウィンドウのデザインとレイアウトを実装します。これには、機能へのアクセスを提供する主要なインターフェース要素と、アプリケーションの設定、ステータス表示、操作制御のためのUIコンポーネントが含まれます。

## 目的・理由
メインウィンドウは、ユーザーとアプリケーションの主要な接点です。以下の理由から、機能的で使いやすいメインウィンドウUIが必要です：

1. ユーザーに分かりやすく直感的な操作環境を提供する
2. アプリケーションの主要機能に簡単にアクセスできるようにする
3. システムの現在の状態と動作についての明確なフィードバックを提供する
4. 今後追加される機能にも対応できる拡張性のあるレイアウトを確立する

## 詳細
- メインウィンドウのレイアウト設計とスタイリング
- 主要なコントロールとコンポーネントの配置
- 反応型UIのインタラクションパターンの実装
- 状態表示とフィードバックメカニズムの実装

## タスク分解
- [ ] レイアウト設計
  - [ ] メインウィンドウのグリッドレイアウト設計
  - [ ] コントロール配置の最適化
  - [ ] レスポンシブ設計の適用
- [ ] ヘッダー部分の実装
  - [ ] アプリケーションタイトルとロゴの表示
  - [ ] メインメニューの実装
  - [ ] ツールバーの実装
- [ ] メインコンテンツエリアの実装
  - [ ] コンテンツスイッチャーの実装
  - [ ] ページナビゲーションの実装
  - [ ] コンテンツプレゼンターの実装
- [ ] ステータスバーの実装
  - [ ] 状態表示コンポーネントの実装
  - [ ] プログレス表示の実装
  - [ ] 通知領域の実装
- [ ] 設定パネルの実装
  - [ ] 設定カテゴリナビゲーションの実装
  - [ ] 設定コントロールの実装
  - [ ] 設定適用と取消メカニズムの実装
- [ ] キャプチャコントロールパネルの実装
  - [ ] キャプチャ領域選択コントロールの実装
  - [ ] キャプチャ設定コントロールの実装
  - [ ] キャプチャプレビューの実装
- [ ] 翻訳コントロールパネルの実装
  - [ ] 言語選択コントロールの実装
  - [ ] 翻訳エンジン選択コントロールの実装
  - [ ] 翻訳オプションコントロールの実装
- [ ] スタイルとテーマの適用
  - [ ] スタイルリソースディクショナリの作成
  - [ ] コントロールテンプレートの実装
  - [ ] 色と視覚効果の調整
- [ ] アニメーションとトランジションの実装
  - [ ] ページ遷移アニメーションの実装
  - [ ] コントロール状態変化のアニメーション
  - [ ] フィードバックアニメーションの実装
- [ ] アクセシビリティ対応
  - [ ] スクリーンリーダー対応のラベル設定
  - [ ] キーボードナビゲーションの実装
  - [ ] タブオーダーの最適化

## デザイン構想
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
        MinWidth="600" MinHeight="400">

    <Design.DataContext>
        <vm:MainWindowViewModel/>
    </Design.DataContext>

    <Grid RowDefinitions="Auto,*,Auto">
        <!-- ヘッダー -->
        <Grid Grid.Row="0" ColumnDefinitions="Auto,*,Auto" Background="{DynamicResource HeaderBackground}">
            <StackPanel Grid.Column="0" Orientation="Horizontal" Margin="10,5">
                <Image Source="/Assets/baketa-logo.png" Width="24" Height="24" Margin="0,0,10,0"/>
                <TextBlock Text="Baketa" VerticalAlignment="Center" FontWeight="Bold"/>
            </StackPanel>
            
            <Menu Grid.Column="1" Margin="20,0">
                <MenuItem Header="ファイル">
                    <MenuItem Header="設定" Command="{Binding OpenSettingsCommand}"/>
                    <Separator/>
                    <MenuItem Header="終了" Command="{Binding ExitCommand}"/>
                </MenuItem>
                <MenuItem Header="キャプチャ">
                    <MenuItem Header="キャプチャ開始" Command="{Binding StartCaptureCommand}"/>
                    <MenuItem Header="キャプチャ停止" Command="{Binding StopCaptureCommand}"/>
                    <MenuItem Header="領域選択" Command="{Binding SelectRegionCommand}"/>
                </MenuItem>
                <MenuItem Header="ツール">
                    <MenuItem Header="ログビューア" Command="{Binding OpenLogViewerCommand}"/>
                    <MenuItem Header="翻訳履歴" Command="{Binding OpenTranslationHistoryCommand}"/>
                </MenuItem>
                <MenuItem Header="ヘルプ">
                    <MenuItem Header="使い方" Command="{Binding OpenHelpCommand}"/>
                    <MenuItem Header="バージョン情報" Command="{Binding OpenAboutCommand}"/>
                </MenuItem>
            </Menu>
            
            <StackPanel Grid.Column="2" Orientation="Horizontal" Margin="10,5">
                <Button Command="{Binding MinimizeToTrayCommand}" ToolTip.Tip="トレイに最小化">
                    <PathIcon Data="{StaticResource MinimizeIcon}"/>
                </Button>
            </StackPanel>
        </Grid>

        <!-- メインコンテンツエリア -->
        <TabControl Grid.Row="1" TabStripPlacement="Left" SelectedIndex="{Binding SelectedTabIndex}">
            <TabItem Header="ホーム" ToolTip.Tip="ホーム画面">
                <local:HomeView DataContext="{Binding HomeViewModel}"/>
            </TabItem>
            <TabItem Header="キャプチャ" ToolTip.Tip="キャプチャ設定">
                <local:CaptureView DataContext="{Binding CaptureViewModel}"/>
            </TabItem>
            <TabItem Header="翻訳" ToolTip.Tip="翻訳設定">
                <local:TranslationView DataContext="{Binding TranslationViewModel}"/>
            </TabItem>
            <TabItem Header="オーバーレイ" ToolTip.Tip="オーバーレイ設定">
                <local:OverlayView DataContext="{Binding OverlayViewModel}"/>
            </TabItem>
            <TabItem Header="履歴" ToolTip.Tip="翻訳履歴">
                <local:HistoryView DataContext="{Binding HistoryViewModel}"/>
            </TabItem>
        </TabControl>
        
        <!-- ステータスバー -->
        <Grid Grid.Row="2" ColumnDefinitions="*,Auto" Background="{DynamicResource StatusBarBackground}">
            <StackPanel Grid.Column="0" Orientation="Horizontal" Margin="10,2">
                <TextBlock Text="{Binding StatusMessage}" VerticalAlignment="Center"/>
            </StackPanel>
            
            <StackPanel Grid.Column="1" Orientation="Horizontal" Margin="10,2">
                <TextBlock Text="{Binding TranslationEngine}" Margin="0,0,10,0" VerticalAlignment="Center"/>
                <TextBlock Text="{Binding CaptureStatus}" Margin="0,0,10,0" VerticalAlignment="Center"/>
                <ProgressBar Value="{Binding Progress}" Width="100" Height="16" Margin="0,0,10,0"
                             IsVisible="{Binding IsProcessing}"/>
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

## メインウィンドウビューモデル設計案
```csharp
namespace Baketa.UI.ViewModels
{
    /// <summary>
    /// メインウィンドウのビューモデル
    /// </summary>
    public class MainWindowViewModel : ViewModelBase
    {
        // 選択中のタブインデックス
        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedTabIndex, value);
        }
        
        // ステータスメッセージ
        private string _statusMessage = "準備完了";
        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }
        
        // 翻訳エンジン
        private string _translationEngine = "Google";
        public string TranslationEngine
        {
            get => _translationEngine;
            set => this.RaiseAndSetIfChanged(ref _translationEngine, value);
        }
        
        // キャプチャ状態
        private string _captureStatus = "停止中";
        public string CaptureStatus
        {
            get => _captureStatus;
            set => this.RaiseAndSetIfChanged(ref _captureStatus, value);
        }
        
        // 処理進捗
        private double _progress;
        public double Progress
        {
            get => _progress;
            set => this.RaiseAndSetIfChanged(ref _progress, value);
        }
        
        // 処理中フラグ
        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set => this.RaiseAndSetIfChanged(ref _isProcessing, value);
        }
        
        // 各タブのビューモデル
        public HomeViewModel HomeViewModel { get; }
        public CaptureViewModel CaptureViewModel { get; }
        public TranslationViewModel TranslationViewModel { get; }
        public OverlayViewModel OverlayViewModel { get; }
        public HistoryViewModel HistoryViewModel { get; }
        
        // コマンド
        public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ExitCommand { get; }
        public ReactiveCommand<Unit, Unit> StartCaptureCommand { get; }
        public ReactiveCommand<Unit, Unit> StopCaptureCommand { get; }
        public ReactiveCommand<Unit, Unit> SelectRegionCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenLogViewerCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenTranslationHistoryCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenHelpCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenAboutCommand { get; }
        public ReactiveCommand<Unit, Unit> MinimizeToTrayCommand { get; }
        
        /// <summary>
        /// 新しいメインウィンドウビューモデルを初期化します
        /// </summary>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="homeViewModel">ホームビューモデル</param>
        /// <param name="captureViewModel">キャプチャビューモデル</param>
        /// <param name="translationViewModel">翻訳ビューモデル</param>
        /// <param name="overlayViewModel">オーバーレイビューモデル</param>
        /// <param name="historyViewModel">履歴ビューモデル</param>
        /// <param name="logger">ロガー</param>
        public MainWindowViewModel(
            IEventAggregator eventAggregator,
            HomeViewModel homeViewModel,
            CaptureViewModel captureViewModel,
            TranslationViewModel translationViewModel,
            OverlayViewModel overlayViewModel,
            HistoryViewModel historyViewModel,
            ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            // 各タブのビューモデルを初期化
            HomeViewModel = homeViewModel;
            CaptureViewModel = captureViewModel;
            TranslationViewModel = translationViewModel;
            OverlayViewModel = overlayViewModel;
            HistoryViewModel = historyViewModel;
            
            // コマンドの初期化
            OpenSettingsCommand = ReactiveCommandFactory.Create(ExecuteOpenSettingsAsync);
            ExitCommand = ReactiveCommandFactory.Create(ExecuteExitAsync);
            StartCaptureCommand = ReactiveCommandFactory.Create(ExecuteStartCaptureAsync);
            StopCaptureCommand = ReactiveCommandFactory.Create(ExecuteStopCaptureAsync);
            SelectRegionCommand = ReactiveCommandFactory.Create(ExecuteSelectRegionAsync);
            OpenLogViewerCommand = ReactiveCommandFactory.Create(ExecuteOpenLogViewerAsync);
            OpenTranslationHistoryCommand = ReactiveCommandFactory.Create(ExecuteOpenTranslationHistoryAsync);
            OpenHelpCommand = ReactiveCommandFactory.Create(ExecuteOpenHelpAsync);
            OpenAboutCommand = ReactiveCommandFactory.Create(ExecuteOpenAboutAsync);
            MinimizeToTrayCommand = ReactiveCommandFactory.Create(ExecuteMinimizeToTrayAsync);
            
            // イベント購読
            SubscribeToEvents();
        }
        
        /// <summary>
        /// イベントをサブスクライブします
        /// </summary>
        private void SubscribeToEvents()
        {
            // 翻訳完了イベントを購読
            this.SubscribeToEvent<TranslationCompletedEvent>(OnTranslationCompleted)
                .DisposeWith(Disposables);
                
            // キャプチャ状態変更イベントを購読
            this.SubscribeToEvent<CaptureStatusChangedEvent>(OnCaptureStatusChanged)
                .DisposeWith(Disposables);
        }
        
        /// <summary>
        /// 翻訳完了イベントハンドラ
        /// </summary>
        private async Task OnTranslationCompleted(TranslationCompletedEvent @event)
        {
            // ステータスメッセージを更新
            StatusMessage = $"翻訳完了: {@event.SourceText}";
            IsProcessing = false;
            Progress = 0;
        }
        
        /// <summary>
        /// キャプチャ状態変更イベントハンドラ
        /// </summary>
        private async Task OnCaptureStatusChanged(CaptureStatusChangedEvent @event)
        {
            // キャプチャ状態を更新
            CaptureStatus = @event.IsActive ? "キャプチャ中" : "停止中";
        }
        
        // コマンド実行メソッド
        private async Task ExecuteOpenSettingsAsync() { /* 実装 */ }
        private async Task ExecuteExitAsync() { /* 実装 */ }
        private async Task ExecuteStartCaptureAsync() { /* 実装 */ }
        private async Task ExecuteStopCaptureAsync() { /* 実装 */ }
        private async Task ExecuteSelectRegionAsync() { /* 実装 */ }
        private async Task ExecuteOpenLogViewerAsync() { /* 実装 */ }
        private async Task ExecuteOpenTranslationHistoryAsync() { /* 実装 */ }
        private async Task ExecuteOpenHelpAsync() { /* 実装 */ }
        private async Task ExecuteOpenAboutAsync() { /* 実装 */ }
        private async Task ExecuteMinimizeToTrayAsync() { /* 実装 */ }
    }
}
```

## 実装上の注意点
- UIスレッドとバックグラウンドスレッドの適切な使い分けを実装する
- 長時間の処理中もUIが応答性を保つよう、非同期処理を適切に実装する
- メモリリークを防ぐため、イベント購読と解除を適切に管理する
- ユーザビリティテストを行い、操作性を向上させる
- 画面サイズの変更に適切に対応するレスポンシブなデザインを実装する
- 国際化と地域化に対応できるよう、ハードコードされた文字列の使用を避ける
- アクセシビリティガイドラインに準拠したUI実装を心がける

## 関連Issue/参考
- 親Issue: #10 Avalonia UIフレームワーク完全実装
- 依存Issue: #10-1 ReactiveUIベースのMVVMフレームワーク実装
- 関連Issue: #9-4 翻訳イベントシステムの実装
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\ui-design-guidelines.md
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\avalonia-guidelines.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (3. 非同期プログラミング)

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: high`
- `component: ui`
