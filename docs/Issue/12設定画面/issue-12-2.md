# Issue 12-2: 設定UI画面の設計と実装

## 概要
Baketaアプリケーションの設定を管理するためのユーザーインターフェース(UI)画面を設計・実装します。ユーザーがアプリケーションの様々な設定を直感的に変更できる、カテゴリ分けされた設定画面を提供します。

## 目的・理由
設定UI画面は以下の理由で重要です：

1. ユーザーが視覚的かつ直感的にアプリケーション設定を変更できるようにする
2. 複雑な設定オプションを整理し、カテゴリごとに分類して使いやすくする
3. 設定変更のリアルタイムプレビューやフィードバックを提供する
4. 設定の検証やエラー表示を視覚的に行い、ユーザーの理解を助ける

## 詳細
- カテゴリ分けされた設定画面レイアウトの設計と実装
- 各種設定コントロール（トグル、スライダー、リスト等）の実装
- 設定変更の即時適用とプレビュー機能
- 設定エラー表示と検証フィードバックの実装

## タスク分解
- [ ] 設定ウィンドウの基本構造
  - [ ] `SettingsWindow`クラスの設計と実装
  - [ ] カテゴリナビゲーションの実装
  - [ ] 設定コンテンツエリアの実装
  - [ ] アクションボタン（保存、キャンセル、リセット）の実装
- [ ] 設定ビューモデルの実装
  - [ ] `SettingsViewModel`基底クラスの実装
  - [ ] 各カテゴリのビューモデルの実装
  - [ ] 設定 <-> ビューモデル間のマッピング実装
  - [ ] 設定変更追跡とリセット機能の実装
- [ ] 共通設定コントロールの実装
  - [ ] トグルスイッチコントロール
  - [ ] スライダーコントロール
  - [ ] ドロップダウンリストコントロール
  - [ ] カラーピッカーコントロール
  - [ ] ホットキー入力コントロール
  - [ ] ディレクトリ選択コントロール
- [ ] カテゴリページの実装
  - [ ] 一般設定ページ
  - [ ] テーマ設定ページ
  - [ ] キャプチャ設定ページ
  - [ ] OCR設定ページ
  - [ ] 翻訳設定ページ
  - [ ] オーバーレイ設定ページ
  - [ ] ホットキー設定ページ
  - [ ] 拡張設定ページ
- [ ] インタラクション機能
  - [ ] 設定変更の即時適用機能
  - [ ] プレビュー機能
  - [ ] バリデーションと視覚的フィードバック
  - [ ] ツールチップヘルプと説明テキスト
- [ ] プロファイル管理UI
  - [ ] プロファイル選択コントロール
  - [ ] プロファイル作成・削除機能
  - [ ] プロファイル設定クローン機能
- [ ] 設定サービスとの連携
  - [ ] 設定の読み込み
  - [ ] 設定の保存
  - [ ] 設定変更のリアルタイム通知の購読
- [ ] 単体テストの実装

## XAML設計案
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:vm="using:Baketa.UI.ViewModels.Settings"
        xmlns:controls="using:Baketa.UI.Controls"
        xmlns:loc="using:Baketa.UI.Localization"
        x:Class="Baketa.UI.Views.SettingsWindow"
        x:DataType="vm:SettingsWindowViewModel"
        Title="{loc:Localize SettingsWindow_Title}"
        Width="900" Height="650"
        MinWidth="800" MinHeight="600"
        WindowStartupLocation="CenterOwner"
        Icon="/Assets/settings-icon.png"
        Background="{DynamicResource ApplicationPageBackgroundThemeBrush}">

    <Grid RowDefinitions="Auto,*,Auto">
        <!-- ヘッダー -->
        <Grid Grid.Row="0" ColumnDefinitions="Auto,*,Auto" Background="{DynamicResource HeaderBackground}" Margin="0">
            <TextBlock Grid.Column="0" Text="{loc:Localize SettingsWindow_Title}" 
                       FontSize="16" FontWeight="SemiBold" Margin="20,15" VerticalAlignment="Center"/>
            
            <!-- プロファイル選択 -->
            <StackPanel Grid.Column="2" Orientation="Horizontal" Margin="10,5" VerticalAlignment="Center">
                <TextBlock Text="{loc:Localize SettingsWindow_Profile}" VerticalAlignment="Center" Margin="0,0,10,0"/>
                <ComboBox ItemsSource="{Binding AvailableProfiles}"
                          SelectedItem="{Binding SelectedProfile}"
                          Width="150" Height="30"/>
                <Button Command="{Binding CreateProfileCommand}" ToolTip.Tip="{loc:Localize SettingsWindow_CreateProfile}"
                        Margin="5,0" Width="30" Height="30">
                    <PathIcon Data="{StaticResource AddIcon}"/>
                </Button>
                <Button Command="{Binding DeleteProfileCommand}" ToolTip.Tip="{loc:Localize SettingsWindow_DeleteProfile}"
                        IsEnabled="{Binding CanDeleteProfile}" Margin="0,0,10,0" Width="30" Height="30">
                    <PathIcon Data="{StaticResource DeleteIcon}"/>
                </Button>
            </StackPanel>
        </Grid>
        
        <!-- メインコンテンツ -->
        <Grid Grid.Row="1" ColumnDefinitions="250,*">
            <!-- カテゴリナビゲーション -->
            <Border Grid.Column="0" Background="{DynamicResource NavigationViewBackground}" 
                    BorderBrush="{DynamicResource NavigationViewBorderBrush}" BorderThickness="0,0,1,0">
                <ListBox ItemsSource="{Binding Categories}"
                         SelectedItem="{Binding SelectedCategory}"
                         Margin="0,10">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal" Margin="5">
                                <PathIcon Data="{Binding IconData}" Width="18" Height="18" Margin="5,0,10,0"/>
                                <TextBlock Text="{Binding Name}" VerticalAlignment="Center"/>
                            </StackPanel>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </Border>
            
            <!-- 設定コンテンツ -->
            <ScrollViewer Grid.Column="1" Margin="10">
                <ContentControl Content="{Binding SelectedCategory.Content}"/>
            </ScrollViewer>
        </Grid>
        
        <!-- フッター -->
        <Grid Grid.Row="2" ColumnDefinitions="*,Auto" Background="{DynamicResource FooterBackground}" Padding="20,10">
            <!-- 状態メッセージ -->
            <TextBlock Grid.Column="0" Text="{Binding StatusMessage}" VerticalAlignment="Center"/>
            
            <!-- アクションボタン -->
            <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="10">
                <Button Content="{loc:Localize Common_Reset}" Command="{Binding ResetCommand}" Width="100"/>
                <Button Content="{loc:Localize Common_Cancel}" Command="{Binding CancelCommand}" Width="100"/>
                <Button Content="{loc:Localize Common_Save}" Command="{Binding SaveCommand}" 
                        IsEnabled="{Binding HasChanges}" Classes="accent" Width="100"/>
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

## 設定カテゴリビューの例（一般設定）
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:Baketa.UI.ViewModels.Settings"
             xmlns:controls="using:Baketa.UI.Controls"
             xmlns:loc="using:Baketa.UI.Localization"
             x:Class="Baketa.UI.Views.Settings.GeneralSettingsView"
             x:DataType="vm:GeneralSettingsViewModel">
    
    <StackPanel Margin="20" Spacing="20">
        <TextBlock Text="{loc:Localize Settings_General_Title}" 
                   FontSize="20" FontWeight="SemiBold" Margin="0,0,0,10"/>
        
        <!-- スタートアップセクション -->
        <StackPanel>
            <TextBlock Text="{loc:Localize Settings_General_Startup}" 
                       FontSize="16" FontWeight="SemiBold" Margin="0,0,0,10"/>
            
            <controls:SettingsItem Title="{loc:Localize Settings_General_AutoStartOnLaunch}"
                                    Description="{loc:Localize Settings_General_AutoStartOnLaunch_Description}">
                <ToggleSwitch IsChecked="{Binding AutoStartOnLaunch}"/>
            </controls:SettingsItem>
            
            <controls:SettingsItem Title="{loc:Localize Settings_General_AutoStartWithSystem}"
                                    Description="{loc:Localize Settings_General_AutoStartWithSystem_Description}">
                <ToggleSwitch IsChecked="{Binding AutoStartWithSystem}"/>
            </controls:SettingsItem>
        </StackPanel>
        
        <!-- UIセクション -->
        <StackPanel>
            <TextBlock Text="{loc:Localize Settings_General_UI}" 
                       FontSize="16" FontWeight="SemiBold" Margin="0,0,0,10"/>
            
            <controls:SettingsItem Title="{loc:Localize Settings_General_MinimizeToTray}"
                                    Description="{loc:Localize Settings_General_MinimizeToTray_Description}">
                <ToggleSwitch IsChecked="{Binding MinimizeToTray}"/>
            </controls:SettingsItem>
            
            <controls:SettingsItem Title="{loc:Localize Settings_General_CloseButtonMinimizes}"
                                    Description="{loc:Localize Settings_General_CloseButtonMinimizes_Description}">
                <ToggleSwitch IsChecked="{Binding CloseButtonMinimizes}"/>
            </controls:SettingsItem>
        </StackPanel>
        
        <!-- 更新セクション -->
        <StackPanel>
            <TextBlock Text="{loc:Localize Settings_General_Updates}" 
                       FontSize="16" FontWeight="SemiBold" Margin="0,0,0,10"/>
            
            <controls:SettingsItem Title="{loc:Localize Settings_General_EnableAutoUpdate}"
                                    Description="{loc:Localize Settings_General_EnableAutoUpdate_Description}">
                <ToggleSwitch IsChecked="{Binding EnableAutoUpdate}"/>
            </controls:SettingsItem>
            
            <controls:SettingsItem Title="{loc:Localize Settings_General_AllowTelemetry}"
                                    Description="{loc:Localize Settings_General_AllowTelemetry_Description}">
                <ToggleSwitch IsChecked="{Binding AllowTelemetry}"/>
            </controls:SettingsItem>
        </StackPanel>
        
        <!-- ログセクション -->
        <StackPanel>
            <TextBlock Text="{loc:Localize Settings_General_Logging}" 
                       FontSize="16" FontWeight="SemiBold" Margin="0,0,0,10"/>
            
            <controls:SettingsItem Title="{loc:Localize Settings_General_LogLevel}"
                                    Description="{loc:Localize Settings_General_LogLevel_Description}">
                <ComboBox ItemsSource="{Binding LogLevels}"
                          SelectedItem="{Binding SelectedLogLevel}"
                          Width="150"/>
            </controls:SettingsItem>
            
            <controls:SettingsItem Title="{loc:Localize Settings_General_LogRetentionDays}"
                                    Description="{loc:Localize Settings_General_LogRetentionDays_Description}">
                <NumericUpDown Value="{Binding LogRetentionDays}"
                               Minimum="1" Maximum="90" 
                               Increment="1" Width="150"/>
            </controls:SettingsItem>
            
            <controls:SettingsItem Title="{loc:Localize Settings_General_OpenLogDirectory}"
                                    Description="{loc:Localize Settings_General_OpenLogDirectory_Description}">
                <Button Content="{loc:Localize Common_Open}" 
                        Command="{Binding OpenLogDirectoryCommand}" Width="100"/>
            </controls:SettingsItem>
        </StackPanel>
    </StackPanel>
</UserControl>
```

## 設定アイテムのカスタムコントロール
```csharp
namespace Baketa.UI.Controls
{
    /// <summary>
    /// 設定項目のカスタムコントロール
    /// </summary>
    public class SettingsItem : ContentControl
    {
        /// <summary>
        /// タイトルプロパティ
        /// </summary>
        public static readonly StyledProperty<string> TitleProperty =
            AvaloniaProperty.Register<SettingsItem, string>(nameof(Title), string.Empty);
            
        /// <summary>
        /// 説明プロパティ
        /// </summary>
        public static readonly StyledProperty<string> DescriptionProperty =
            AvaloniaProperty.Register<SettingsItem, string>(nameof(Description), string.Empty);
            
        /// <summary>
        /// アイコンプロパティ
        /// </summary>
        public static readonly StyledProperty<IImage?> IconProperty =
            AvaloniaProperty.Register<SettingsItem, IImage?>(nameof(Icon), null);
            
        /// <summary>
        /// ヘルプテキストプロパティ
        /// </summary>
        public static readonly StyledProperty<string> HelpTextProperty =
            AvaloniaProperty.Register<SettingsItem, string>(nameof(HelpText), string.Empty);
            
        /// <summary>
        /// エラーメッセージプロパティ
        /// </summary>
        public static readonly StyledProperty<string> ErrorMessageProperty =
            AvaloniaProperty.Register<SettingsItem, string>(nameof(ErrorMessage), string.Empty);
            
        /// <summary>
        /// エラー状態プロパティ
        /// </summary>
        public static readonly StyledProperty<bool> HasErrorProperty =
            AvaloniaProperty.Register<SettingsItem, bool>(nameof(HasError), false);
            
        /// <summary>
        /// タイトル
        /// </summary>
        public string Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }
        
        /// <summary>
        /// 説明
        /// </summary>
        public string Description
        {
            get => GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }
        
        /// <summary>
        /// アイコン
        /// </summary>
        public IImage? Icon
        {
            get => GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }
        
        /// <summary>
        /// ヘルプテキスト
        /// </summary>
        public string HelpText
        {
            get => GetValue(HelpTextProperty);
            set => SetValue(HelpTextProperty, value);
        }
        
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public string ErrorMessage
        {
            get => GetValue(ErrorMessageProperty);
            set => SetValue(ErrorMessageProperty, value);
        }
        
        /// <summary>
        /// エラー状態
        /// </summary>
        public bool HasError
        {
            get => GetValue(HasErrorProperty);
            set => SetValue(HasErrorProperty, value);
        }
        
        static SettingsItem()
        {
            // デフォルトのテンプレートを設定
            AffectsRender<SettingsItem>(TitleProperty, DescriptionProperty, IconProperty, 
                HelpTextProperty, ErrorMessageProperty, HasErrorProperty);
                
            ContentProperty.Changed.AddClassHandler<SettingsItem>((x, e) => x.OnContentChanged(e));
        }
        
        /// <summary>
        /// 新しい設定項目を初期化します
        /// </summary>
        public SettingsItem()
        {
        }
        
        /// <summary>
        /// コンテンツ変更時の処理
        /// </summary>
        /// <param name="e">イベント引数</param>
        private void OnContentChanged(AvaloniaPropertyChangedEventArgs e)
        {
            UpdatePseudoClasses();
        }
        
        /// <override />
        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            UpdatePseudoClasses();
        }
        
        /// <summary>
        /// 擬似クラスを更新します
        /// </summary>
        private void UpdatePseudoClasses()
        {
            PseudoClasses.Set(":hasError", HasError);
            PseudoClasses.Set(":hasIcon", Icon != null);
            PseudoClasses.Set(":hasHelp", !string.IsNullOrEmpty(HelpText));
        }
    }
}
```

## 設定ウィンドウビューモデル
```csharp
namespace Baketa.UI.ViewModels.Settings
{
    /// <summary>
    /// 設定ウィンドウビューモデル
    /// </summary>
    public class SettingsWindowViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IWindowManager _windowManager;
        private string _statusMessage = string.Empty;
        private bool _hasChanges;
        private string _selectedProfile = "Default";
        private SettingsCategoryViewModel? _selectedCategory;
        private readonly ObservableCollection<string> _availableProfiles = new();
        private readonly ObservableCollection<SettingsCategoryViewModel> _categories = new();
        
        /// <summary>
        /// 状態メッセージ
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }
        
        /// <summary>
        /// 変更があるかどうか
        /// </summary>
        public bool HasChanges
        {
            get => _hasChanges;
            set => this.RaiseAndSetIfChanged(ref _hasChanges, value);
        }
        
        /// <summary>
        /// 利用可能なプロファイル
        /// </summary>
        public ObservableCollection<string> AvailableProfiles => _availableProfiles;
        
        /// <summary>
        /// 選択されたプロファイル
        /// </summary>
        public string SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _selectedProfile, value))
                {
                    SwitchProfileAsync(value).ConfigureAwait(false);
                }
            }
        }
        
        /// <summary>
        /// プロファイルを削除できるかどうか
        /// </summary>
        public bool CanDeleteProfile => _selectedProfile != "Default";
        
        /// <summary>
        /// 設定カテゴリ
        /// </summary>
        public ObservableCollection<SettingsCategoryViewModel> Categories => _categories;
        
        /// <summary>
        /// 選択されたカテゴリ
        /// </summary>
        public SettingsCategoryViewModel? SelectedCategory
        {
            get => _selectedCategory;
            set => this.RaiseAndSetIfChanged(ref _selectedCategory, value);
        }
        
        /// <summary>
        /// 保存コマンド
        /// </summary>
        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        
        /// <summary>
        /// キャンセルコマンド
        /// </summary>
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        
        /// <summary>
        /// リセットコマンド
        /// </summary>
        public ReactiveCommand<Unit, Unit> ResetCommand { get; }
        
        /// <summary>
        /// プロファイル作成コマンド
        /// </summary>
        public ReactiveCommand<Unit, Unit> CreateProfileCommand { get; }
        
        /// <summary>
        /// プロファイル削除コマンド
        /// </summary>
        public ReactiveCommand<Unit, Unit> DeleteProfileCommand { get; }
        
        /// <summary>
        /// 新しい設定ウィンドウビューモデルを初期化します
        /// </summary>
        /// <param name="settingsService">設定サービス</param>
        /// <param name="windowManager">ウィンドウマネージャー</param>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="logger">ロガー</param>
        public SettingsWindowViewModel(
            ISettingsService settingsService,
            IWindowManager windowManager,
            IEventAggregator eventAggregator,
            ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
            
            // コマンドの初期化
            SaveCommand = ReactiveCommand.CreateFromTask(ExecuteSaveAsync);
            CancelCommand = ReactiveCommand.Create(ExecuteCancel);
            ResetCommand = ReactiveCommand.CreateFromTask(ExecuteResetAsync);
            CreateProfileCommand = ReactiveCommand.CreateFromTask(ExecuteCreateProfileAsync);
            DeleteProfileCommand = ReactiveCommand.CreateFromTask(ExecuteDeleteProfileAsync);
            
            // カテゴリの初期化
            InitializeCategories();
            
            // プロファイルの初期化
            InitializeProfilesAsync().ConfigureAwait(false);
            
            _logger?.LogInformation("設定ウィンドウビューモデルが初期化されました。");
        }
        
        /// <summary>
        /// アクティブ化時の処理
        /// </summary>
        protected override void HandleActivation()
        {
            base.HandleActivation();
            
            // 設定変更通知を購読
            this.SubscribeToEvent<SettingsChangedEvent>(OnSettingsChanged)
                .DisposeWith(Disposables);
                
            // カテゴリの変更検知を設定
            foreach (var category in _categories)
            {
                category.PropertyChanged += OnCategoryPropertyChanged;
            }
        }
        
        /// <summary>
        /// 非アクティブ化時の処理
        /// </summary>
        protected override void HandleDeactivation()
        {
            // カテゴリの変更検知を解除
            foreach (var category in _categories)
            {
                category.PropertyChanged -= OnCategoryPropertyChanged;
            }
            
            base.HandleDeactivation();
        }
        
        /// <summary>
        /// カテゴリを初期化します
        /// </summary>
        private void InitializeCategories()
        {
            _categories.Clear();
            
            // カテゴリの作成
            _categories.Add(new GeneralSettingsViewModel(_settingsService.CurrentSettings.General));
            _categories.Add(new ThemeSettingsViewModel(_settingsService.CurrentSettings.Theme));
            _categories.Add(new CaptureSettingsViewModel(_settingsService.CurrentSettings.Capture));
            _categories.Add(new OcrSettingsViewModel(_settingsService.CurrentSettings.Ocr));
            _categories.Add(new TranslationSettingsViewModel(_settingsService.CurrentSettings.Translation));
            _categories.Add(new OverlaySettingsViewModel(_settingsService.CurrentSettings.Overlay));
            _categories.Add(new HotkeySettingsViewModel(_settingsService.CurrentSettings.Hotkeys));
            _categories.Add(new AdvancedSettingsViewModel(_settingsService.CurrentSettings.Advanced));
            
            // 最初のカテゴリを選択
            _selectedCategory = _categories.FirstOrDefault();
        }
        
        /// <summary>
        /// プロファイルを初期化します
        /// </summary>
        private async Task InitializeProfilesAsync()
        {
            // 利用可能なプロファイルを取得
            var profiles = await _settingsService.GetAvailableProfilesAsync();
            
            _availableProfiles.Clear();
            foreach (var profile in profiles)
            {
                _availableProfiles.Add(profile);
            }
            
            // 現在のプロファイルを選択
            _selectedProfile = _settingsService.GetCurrentProfileName();
            
            this.RaisePropertyChanged(nameof(CanDeleteProfile));
        }
        
        /// <summary>
        /// プロファイルを切り替えます
        /// </summary>
        /// <param name="profileName">プロファイル名</param>
        private async Task SwitchProfileAsync(string profileName)
        {
            if (HasChanges)
            {
                // 変更がある場合は確認ダイアログを表示
                var result = await _windowManager.ShowMessageBoxAsync(
                    "Settings_SaveChangesPrompt".Localize(),
                    "Settings_SaveChangesPrompt_Title".Localize(),
                    MessageBoxButtons.YesNoCancel);
                    
                if (result == MessageBoxResult.Cancel)
                {
                    // キャンセルされた場合は元のプロファイルに戻す
                    _selectedProfile = _settingsService.GetCurrentProfileName();
                    this.RaisePropertyChanged(nameof(SelectedProfile));
                    return;
                }
                
                if (result == MessageBoxResult.Yes)
                {
                    // 保存する場合
                    await SaveSettingsAsync();
                }
            }
            
            // プロファイルを切り替え
            await _settingsService.SwitchProfileAsync(profileName);
            
            // 設定を再読み込み
            await _settingsService.LoadSettingsAsync();
            
            // カテゴリを再初期化
            InitializeCategories();
            
            HasChanges = false;
            StatusMessage = $"Settings_ProfileSwitched".Localize(profileName);
            this.RaisePropertyChanged(nameof(CanDeleteProfile));
        }
        
        /// <summary>
        /// 設定を保存します
        /// </summary>
        private async Task SaveSettingsAsync()
        {
            // 設定を更新
            var settings = new AppSettings
            {
                General = ((GeneralSettingsViewModel)Categories[0]).GetSettings(),
                Theme = ((ThemeSettingsViewModel)Categories[1]).GetSettings(),
                Capture = ((CaptureSettingsViewModel)Categories[2]).GetSettings(),
                Ocr = ((OcrSettingsViewModel)Categories[3]).GetSettings(),
                Translation = ((TranslationSettingsViewModel)Categories[4]).GetSettings(),
                Overlay = ((OverlaySettingsViewModel)Categories[5]).GetSettings(),
                Hotkeys = ((HotkeySettingsViewModel)Categories[6]).GetSettings(),
                Advanced = ((AdvancedSettingsViewModel)Categories[7]).GetSettings(),
                GameProfiles = _settingsService.CurrentSettings.GameProfiles
            };
            
            // 設定を検証
            var validationResult = _settingsService.ValidateSettings(settings);
            if (!validationResult.IsValid)
            {
                // エラーがある場合はメッセージを表示
                await _windowManager.ShowMessageBoxAsync(
                    string.Join("\n", validationResult.Errors),
                    "Settings_ValidationError_Title".Localize(),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                    
                return;
            }
            
            // 設定を保存
            await _settingsService.UpdateSettingsAsync(settings);
            
            HasChanges = false;
            StatusMessage = "Settings_SavedSuccessfully".Localize();
        }
        
        /// <summary>
        /// 保存コマンドの実行
        /// </summary>
        private async Task ExecuteSaveAsync()
        {
            await SaveSettingsAsync();
            _windowManager.CloseWindow(this);
        }
        
        /// <summary>
        /// キャンセルコマンドの実行
        /// </summary>
        private void ExecuteCancel()
        {
            _windowManager.CloseWindow(this);
        }
        
        /// <summary>
        /// リセットコマンドの実行
        /// </summary>
        private async Task ExecuteResetAsync()
        {
            // 確認ダイアログを表示
            var result = await _windowManager.ShowMessageBoxAsync(
                "Settings_ResetConfirm".Localize(),
                "Settings_ResetConfirm_Title".Localize(),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
                
            if (result != MessageBoxResult.Yes)
                return;
                
            // 設定をリセット
            await _settingsService.ResetToDefaultsAsync();
            
            // カテゴリを再初期化
            InitializeCategories();
            
            HasChanges = false;
            StatusMessage = "Settings_ResetSuccessfully".Localize();
        }
        
        /// <summary>
        /// プロファイル作成コマンドの実行
        /// </summary>
        private async Task ExecuteCreateProfileAsync()
        {
            // プロファイル名の入力ダイアログを表示
            var profileName = await _windowManager.ShowInputDialogAsync(
                "Settings_CreateProfile_Prompt".Localize(),
                "Settings_CreateProfile_Title".Localize(),
                string.Empty);
                
            if (string.IsNullOrWhiteSpace(profileName))
                return;
                
            // プロファイル名のバリデーション
            if (profileName == "Default" || _availableProfiles.Contains(profileName))
            {
                await _windowManager.ShowMessageBoxAsync(
                    "Settings_CreateProfile_NameExists".Localize(),
                    "Settings_CreateProfile_Error".Localize(),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                    
                return;
            }
            
            // 禁止文字のチェック
            if (!IsValidProfileName(profileName))
            {
                await _windowManager.ShowMessageBoxAsync(
                    "Settings_CreateProfile_InvalidName".Localize(),
                    "Settings_CreateProfile_Error".Localize(),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                    
                return;
            }
            
            // 保存確認（現在の設定をコピーするか）
            var saveResult = await _windowManager.ShowMessageBoxAsync(
                "Settings_CreateProfile_SaveCurrent".Localize(),
                "Settings_CreateProfile_SaveCurrent_Title".Localize(),
                MessageBoxButtons.YesNo);
                
            if (saveResult == MessageBoxResult.Yes)
            {
                // 現在の設定を保存
                await SaveSettingsAsync();
            }
            
            // プロファイルを切り替え
            SelectedProfile = profileName;
            
            // 利用可能なプロファイルを更新
            _availableProfiles.Add(profileName);
            
            StatusMessage = $"Settings_ProfileCreated".Localize(profileName);
        }
        
        /// <summary>
        /// プロファイル削除コマンドの実行
        /// </summary>
        private async Task ExecuteDeleteProfileAsync()
        {
            if (SelectedProfile == "Default")
                return;
                
            // 確認ダイアログを表示
            var result = await _windowManager.ShowMessageBoxAsync(
                "Settings_DeleteProfile_Confirm".Localize(SelectedProfile),
                "Settings_DeleteProfile_Title".Localize(),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
                
            if (result != MessageBoxResult.Yes)
                return;
                
            var profileToDelete = SelectedProfile;
            
            // Defaultプロファイルに切り替え
            SelectedProfile = "Default";
            
            // プロファイルフォルダを削除
            DeleteProfileFolder(profileToDelete);
            
            // 利用可能なプロファイルを更新
            _availableProfiles.Remove(profileToDelete);
            
            StatusMessage = $"Settings_ProfileDeleted".Localize(profileToDelete);
        }
        
        /// <summary>
        /// プロファイルフォルダを削除します
        /// </summary>
        /// <param name="profileName">プロファイル名</param>
        private void DeleteProfileFolder(string profileName)
        {
            // プロファイルフォルダのパスを取得
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string profilePath = Path.Combine(appDataPath, "Baketa", "Settings", "Profiles", profileName);
            
            if (Directory.Exists(profilePath))
            {
                try
                {
                    Directory.Delete(profilePath, true);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "プロファイルフォルダの削除中にエラーが発生しました: {ProfilePath}", profilePath);
                    
                    StatusMessage = "Settings_ProfileDeleteError".Localize();
                }
            }
        }
        
        /// <summary>
        /// プロファイル名が有効かどうかをチェックします
        /// </summary>
        /// <param name="profileName">プロファイル名</param>
        /// <returns>有効であればtrue</returns>
        private bool IsValidProfileName(string profileName)
        {
            return !string.IsNullOrWhiteSpace(profileName) &&
                   !profileName.Any(c => Path.GetInvalidFileNameChars().Contains(c));
        }
        
        /// <summary>
        /// 設定変更イベントハンドラー
        /// </summary>
        /// <param name="e">イベント引数</param>
        private async Task OnSettingsChanged(SettingsChangedEvent e)
        {
            // カテゴリを再初期化
            InitializeCategories();
            
            HasChanges = false;
            StatusMessage = "Settings_ReloadedSuccessfully".Localize();
        }
        
        /// <summary>
        /// カテゴリプロパティ変更イベントハンドラー
        /// </summary>
        /// <param name="sender">送信元</param>
        /// <param name="e">イベント引数</param>
        private void OnCategoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsCategoryViewModel.HasChanges))
            {
                // いずれかのカテゴリに変更があるか確認
                bool anyChanges = _categories.Any(c => c.HasChanges);
                HasChanges = anyChanges;
                
                if (anyChanges)
                {
                    StatusMessage = "Settings_UnsavedChanges".Localize();
                }
            }
        }
    }
}
```

## 実装上の注意点
- UIのレスポンシブ性とユーザビリティを重視したデザイン
- 設定変更のリアルタイム検証と視覚的フィードバック
- 設定データモデルとビューモデル間のマッピングの一貫性
- 設定変更追跡と保存確認の適切な処理
- 多言語対応と国際化のサポート
- スクリーンリーダーなどのアクセシビリティ対応
- 大量の設定項目を効率的に管理するためのパフォーマンス最適化
- プロファイル管理の安全性と信頼性の確保

## 関連Issue/参考
- 親Issue: #12 設定画面
- 依存Issue: #12-1 設定データモデルと永続化システムの実装
- 関連Issue: #10-5 テーマと国際化対応の基盤実装
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\settings-ui.md
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\mvvm-patterns.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (5.2 イベント通知パターン)

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: medium`
- `component: ui`
