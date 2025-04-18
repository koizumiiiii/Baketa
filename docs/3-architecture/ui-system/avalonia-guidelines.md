# Avalonia UIガイドライン - Baketaプロジェクト

## 1. MVVMパターンの実装

BaketaプロジェクトではModel-View-ViewModelパターンを採用し、以下の規則に従います：

### 1.1 基本的なプロジェクト構造

```
Baketa.UI.Avalonia/
├── ViewModels/        # すべてのビューモデル
│   ├── MainViewModel.cs
│   ├── SettingsViewModel.cs
│   └── Base/          # 基底クラス
│       └── ViewModelBase.cs
│
├── Views/             # すべてのビュー（XAML）
│   ├── MainView.axaml
│   ├── SettingsView.axaml
│   └── Controls/      # 再利用可能なコントロール
│       └── OcrPreview.axaml
│
├── Models/            # UIに関連するモデル
│   └── Settings/
│       └── UiSettings.cs
│
└── Services/          # UIサービス実装
    ├── Interfaces/    # サービスインターフェース
    │   └── IDialogService.cs
    └── DialogService.cs
```

### 1.2 ViewModelBase クラス

すべてのビューモデルが継承する基底クラスを定義します：

```csharp
// ReactiveUIベースのViewModelBase
public class ViewModelBase : ReactiveObject, IActivatableViewModel
{
    public ViewModelActivator Activator { get; } = new();

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        return this.RaiseAndSetIfChanged(ref storage, value, propertyName);
    }
    
    // アクティベーション/非アクティベーションの処理
    protected virtual void OnActivated(CompositeDisposable disposables)
    {
        // 派生クラスで実装
    }
    
    // デフォルトの実装
    public ViewModelBase()
    {
        this.WhenActivated((disposables) => 
        {
            OnActivated(disposables);
        });
    }
}
```

### 1.3 ビューとビューモデルの命名規則

- **ビュー**: `<名前>View.axaml` (例: `MainView.axaml`)
- **ビューモデル**: `<名前>ViewModel.cs` (例: `MainViewModel.cs`)
- **ウィンドウ**: `<名前>Window.axaml` (例: `SettingsWindow.axaml`)

### 1.4 ビューとビューモデルの接続

データコンテキストを明示的に設定します：

```csharp
// View.axaml.cs
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        
        // メソッド内でビューモデルを作成・設定（テスト困難）
        DataContext = new MainViewModel();
        
        // または
        
        // 依存性注入による設定（推奨）
        DataContext = App.Current.Services.GetRequiredService<MainViewModel>();
    }
}

// XAML内での設定（設計時サポート用）
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Baketa.UI.Avalonia.ViewModels"
             x:Class="Baketa.UI.Avalonia.Views.MainView"
             x:DataType="vm:MainViewModel">
    
    <Design.DataContext>
        <vm:MainViewModel />
    </Design.DataContext>
    
    <!-- コンテンツ -->
</UserControl>
```

## 2. データバインディングのベストプラクティス

### 2.1 コンパイル済みバインディング

型の安全性と性能を高めるため、コンパイル済みバインディングを使用します：

```xml
<!-- コンパイル済みバインディング -->
<UserControl xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Baketa.UI.Avalonia.ViewModels"
             x:DataType="vm:MainViewModel">

    <StackPanel>
        <TextBlock Text="{Binding Title}" />
        <TextBox Text="{Binding SearchText}" />
        <Button Command="{Binding SearchCommand}" Content="検索" />
    </StackPanel>
</UserControl>
```

コンパイル済みバインディングを使用するには、`x:DataType`属性が必要です。

### 2.2 コレクションバインディング

コレクションに対するバインディングでは、`ItemsSource`を使用します：

```xml
<!-- 避けるべき書き方 -->
<ComboBox Items="{Binding AvailableLanguages}">

<!-- 推奨される書き方 -->
<ComboBox ItemsSource="{Binding AvailableLanguages}">
```

### 2.3 コマンドバインディング

特にリストボックスなどのItemsControl内のコマンドバインディングには、相対ソースを使用します：

```xml
<!-- オプション1: RelativeSourceを使用したバインディング -->
<Button Command="{Binding DataContext.DeleteCommand, RelativeSource={RelativeSource FindAncestor, AncestorType=UserControl}}"
        CommandParameter="{Binding Id}" />

<!-- オプション2: 名前付き要素を使用したバインディング -->
<UserControl x:Name="myControl" ... >
  ...
  <Button Command="{Binding DataContext.DeleteCommand, ElementName=myControl}"
          CommandParameter="{Binding Id}" />
</UserControl>
```

### 2.4 リスト項目へのバインディング

リスト項目の詳細表示には、DataTemplateを使用します：

```xml
<ListBox ItemsSource="{Binding Items}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="{Binding Name}" Margin="0,0,10,0" />
                <TextBlock Text="{Binding Description}" />
                
                <!-- 親コンテキストへのアクセス -->
                <Button Command="{Binding DataContext.RemoveCommand, 
                         RelativeSource={RelativeSource FindAncestor, AncestorType=ListBox}}"
                        CommandParameter="{Binding}" />
            </StackPanel>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

### 2.5 設計時データの提供

XAMLデザイナーでのプレビューのため、設計時データを提供します：

```xml
<UserControl ...>
    <Design.DataContext>
        <vm:MainViewModel />
    </Design.DataContext>
    
    <!-- コンテンツ -->
</UserControl>
```

## 3. コマンド実装パターン

ReactiveUIと連携したAvaloniaのコマンド実装は、以下のパターンに従います：

### 3.1 パラメータなしコマンドの実装

```csharp
// ViewModelでの定義
public class MainViewModel : ViewModelBase
{
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    
    public MainViewModel()
    {
        // コマンドの初期化
        SaveCommand = ReactiveCommand.Create(ExecuteSave);
    }
    
    // 実行メソッド
    private void ExecuteSave()
    {
        // 保存処理ロジック
    }
}
```

### 3.2 パラメータありコマンドの実装

```csharp
// ViewModelでの定義
public class ItemsViewModel : ViewModelBase
{
    public ReactiveCommand<string, Unit> DeleteCommand { get; }
    
    public ItemsViewModel()
    {
        // パラメータを受け取るコマンドの初期化
        DeleteCommand = ReactiveCommand.Create<string>(ExecuteDelete);
    }
    
    // 実行メソッド
    private void ExecuteDelete(string id)
    {
        // 削除処理ロジック
    }
}
```

### 3.3 条件付きコマンド（実行可否の制御）

```csharp
// 条件に基づくコマンドの実行可否制御
public class EditViewModel : ViewModelBase
{
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }
    
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    
    public EditViewModel()
    {
        // 名前が空でない場合のみコマンドを有効化
        var canExecute = this.WhenAnyValue(x => x.Name)
            .Select(name => !string.IsNullOrWhiteSpace(name));
            
        SaveCommand = ReactiveCommand.Create(ExecuteSave, canExecute);
    }
    
    private void ExecuteSave()
    {
        // 保存処理
    }
}
```

### 3.4 非同期コマンドの実装

```csharp
// 非同期コマンドの実装
public class DataViewModel : ViewModelBase
{
    public ReactiveCommand<Unit, Unit> LoadDataCommand { get; }
    
    public DataViewModel(IDataService dataService)
    {
        // 非同期コマンドの初期化
        LoadDataCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            // 進行中フラグの設定
            IsLoading = true;
            
            try
            {
                // 非同期データ読み込み
                var data = await dataService.LoadDataAsync();
                Items = new ObservableCollection<Item>(data);
            }
            catch (Exception ex)
            {
                // エラー処理
                ErrorMessage = ex.Message;
            }
            finally
            {
                // 進行中フラグのリセット
                IsLoading = false;
            }
        });
    }
    
    // プロパティ実装...
}
```

## 4. リソースとスタイル

### 4.1 リソースの構成

アプリケーション全体で一貫したスタイルを適用するため、リソースを適切に構成します：

```
Baketa.UI.Avalonia/
├── Styles/
│   ├── Colors.axaml      # カラーテーマ
│   ├── Typography.axaml  # フォント定義
│   ├── Buttons.axaml     # ボタンスタイル
│   └── Controls.axaml    # その他コントロールスタイル
│
└── App.axaml             # リソースのマージ
```

### 4.2 テーマとリソースの指定

App.axamlでリソースをマージします：

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Baketa.UI.Avalonia.App">
    <Application.Styles>
        <!-- 基本スタイル -->
        <FluentTheme />
        
        <!-- カスタムスタイル -->
        <StyleInclude Source="/Styles/Colors.axaml" />
        <StyleInclude Source="/Styles/Typography.axaml" />
        <StyleInclude Source="/Styles/Buttons.axaml" />
        <StyleInclude Source="/Styles/Controls.axaml" />
    </Application.Styles>
    
    <Application.Resources>
        <!-- グローバルリソース -->
        <SolidColorBrush x:Key="PrimaryBrush" Color="#3B5998" />
        <SolidColorBrush x:Key="SecondaryBrush" Color="#8B9DC3" />
        <!-- その他のリソース -->
    </Application.Resources>
</Application>
```

### 4.3 スタイルの定義

再利用可能なスタイルを定義します：

```xml
<!-- Buttons.axaml -->
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- 基本ボタンスタイル -->
    <Style Selector="Button.primary">
        <Setter Property="Background" Value="{DynamicResource PrimaryBrush}" />
        <Setter Property="Foreground" Value="White" />
        <Setter Property="Padding" Value="12,8" />
        <Setter Property="CornerRadius" Value="4" />
    </Style>
    
    <!-- ホバー状態 -->
    <Style Selector="Button.primary:pointerover">
        <Setter Property="Background" Value="{DynamicResource PrimaryHoverBrush}" />
    </Style>
    
    <!-- 押下状態 -->
    <Style Selector="Button.primary:pressed">
        <Setter Property="Background" Value="{DynamicResource PrimaryPressedBrush}" />
    </Style>
    
    <!-- 無効状態 -->
    <Style Selector="Button.primary:disabled">
        <Setter Property="Opacity" Value="0.5" />
    </Style>
</Styles>
```

### 4.4 スタイルの適用

スタイルをコントロールに適用します：

```xml
<!-- スタイルの適用 -->
<Button Content="保存" Classes="primary" Command="{Binding SaveCommand}" />

<!-- 複数のクラスの適用 -->
<Button Content="キャンセル" Classes="secondary outline" Command="{Binding CancelCommand}" />
```

## 5. カスタムコントロール

### 5.1 カスタムコントロールの作成

カスタムコントロールを作成する際は、適切な基底クラスから派生させます：

```csharp
// UserControlからの派生
public partial class OcrPreviewControl : UserControl
{
    public OcrPreviewControl()
    {
        InitializeComponent();
    }
    
    // 添付プロパティの定義
    public static readonly StyledProperty<IImage> ImageProperty = 
        AvaloniaProperty.Register<OcrPreviewControl, IImage>(nameof(Image));
        
    // CLRプロパティラッパー
    public IImage Image
    {
        get => GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }
    
    // プロパティ変更ハンドラー
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        
        if (change.Property == ImageProperty)
        {
            UpdatePreviewImage();
        }
    }
    
    private void UpdatePreviewImage()
    {
        // 画像更新ロジック
    }
}
```

### 5.2 テンプレートコントロールの作成

カスタムテンプレートコントロールを作成する場合：

```csharp
// TemplatedControlからの派生
public class TranslationResultBox : TemplatedControl
{
    // プロパティ定義
    public static readonly StyledProperty<string> SourceTextProperty = 
        AvaloniaProperty.Register<TranslationResultBox, string>(nameof(SourceText));
        
    public static readonly StyledProperty<string> TranslatedTextProperty = 
        AvaloniaProperty.Register<TranslationResultBox, string>(nameof(TranslatedText));
    
    // CLRプロパティラッパー
    public string SourceText
    {
        get => GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }
    
    public string TranslatedText
    {
        get => GetValue(TranslatedTextProperty);
        set => SetValue(TranslatedTextProperty, value);
    }
    
    // テンプレートの定義はスタイルで行う
}
```

対応するスタイル定義：

```xml
<Style Selector="TranslationResultBox">
    <Setter Property="Template">
        <ControlTemplate>
            <Border Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}"
                    CornerRadius="4">
                <Grid RowDefinitions="Auto,Auto">
                    <TextBlock Grid.Row="0" 
                               Text="{TemplateBinding SourceText}"
                               FontWeight="Bold" />
                    <TextBlock Grid.Row="1" 
                               Text="{TemplateBinding TranslatedText}"
                               Foreground="DarkGreen" />
                </Grid>
            </Border>
        </ControlTemplate>
    </Setter>
</Style>
```

### 5.3 添付プロパティの定義

汎用的な機能を提供する添付プロパティを定義します：

```csharp
public static class TextHelper
{
    // 添付プロパティの定義
    public static readonly AttachedProperty<bool> IsReadOnlyWhenEmptyProperty =
        AvaloniaProperty.RegisterAttached<TextHelper, Interactive, bool>("IsReadOnlyWhenEmpty");
    
    // Getter
    public static bool GetIsReadOnlyWhenEmpty(Interactive element)
    {
        return element.GetValue(IsReadOnlyWhenEmptyProperty);
    }
    
    // Setter
    public static void SetIsReadOnlyWhenEmpty(Interactive element, bool value)
    {
        element.SetValue(IsReadOnlyWhenEmptyProperty, value);
    }
    
    // プロパティ変更時のロジック
    static TextHelper()
    {
        IsReadOnlyWhenEmptyProperty.Changed.AddClassHandler<TextBox>((textBox, e) =>
        {
            if ((bool)e.NewValue)
            {
                textBox.PropertyChanged += TextBoxPropertyChanged;
            }
            else
            {
                textBox.PropertyChanged -= TextBoxPropertyChanged;
            }
        });
    }
    
    private static void TextBoxPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is TextBox textBox && e.Property == TextBox.TextProperty)
        {
            textBox.IsReadOnly = string.IsNullOrEmpty(textBox.Text);
        }
    }
}
```

使用例：

```xml
<TextBox Text="{Binding Name}" local:TextHelper.IsReadOnlyWhenEmpty="True" />
```

## 6. ReactiveUI との統合

### 6.1 プロパティの変更通知

ReactiveUI の `RaiseAndSetIfChanged` を使用して、プロパティの変更通知を簡潔に実装します：

```csharp
public class MainViewModel : ReactiveObject
{
    private string _searchText = string.Empty;
    
    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }
}
```

### 6.2 WhenAnyValueを使用したプロパティ連鎖

プロパティの値に基づいて他のプロパティを計算するには、`WhenAnyValue`と`ToProperty`を使用します：

```csharp
public class SearchViewModel : ReactiveObject
{
    private readonly ObservableAsPropertyHelper<bool> _isSearchEnabled;
    private string _searchText = string.Empty;
    
    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }
    
    public bool IsSearchEnabled => _isSearchEnabled.Value;
    
    public SearchViewModel()
    {
        // SearchTextが空でなければ検索を有効化
        _isSearchEnabled = this.WhenAnyValue(x => x.SearchText)
            .Select(text => !string.IsNullOrEmpty(text))
            .ToProperty(this, x => x.IsSearchEnabled);
    }
}
```

### 6.3 コマンドでの使用

ReactiveCommandの初期化にWhenAnyValueを使用します：

```csharp
public class SettingsViewModel : ReactiveObject
{
    // プロパティ定義...
    
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    
    public SettingsViewModel()
    {
        var canSave = this.WhenAnyValue(
            x => x.Username,
            x => x.Password,
            x => x.ServerUrl,
            (username, password, url) => 
                !string.IsNullOrEmpty(username) && 
                !string.IsNullOrEmpty(password) && 
                Uri.TryCreate(url, UriKind.Absolute, out _)
        );
        
        SaveCommand = ReactiveCommand.CreateFromTask(SaveSettingsAsync, canSave);
    }
    
    private async Task SaveSettingsAsync()
    {
        // 保存処理
    }
}
```

### 6.4 OnActivatedの活用

ウィンドウやビューがアクティブになったときに実行する処理を定義します：

```csharp
public class MainViewModel : ReactiveObject, IActivatableViewModel
{
    public ViewModelActivator Activator { get; } = new();
    
    // プロパティとコマンド...
    
    public MainViewModel()
    {
        this.WhenActivated(disposables =>
        {
            // ウィンドウがアクティブになったときの処理
            LoadInitialData().Subscribe().DisposeWith(disposables);
            
            // イベント購読
            MessageBus.Current.Listen<RefreshMessage>()
                .Subscribe(_ => RefreshData())
                .DisposeWith(disposables);
                
            // 反応型のプロパティ結合
            this.WhenAnyValue(x => x.SearchText)
                .Throttle(TimeSpan.FromMilliseconds(500))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(text => ExecuteSearch(text))
                .DisposeWith(disposables);
        });
    }
    
    private IObservable<Unit> LoadInitialData()
    {
        // 初期データロード
        return Observable.FromAsync(async () =>
        {
            // 非同期データロード
        });
    }
    
    private void RefreshData()
    {
        // データ再取得
    }
    
    private void ExecuteSearch(string searchText)
    {
        // 検索実行
    }
}
```

## 7. 名前空間と型の参照

### 7.1 グローバル名前空間の明示的な参照

同名または類似の名前空間が存在する場合、`global::`プレフィックスを使用して明示的に参照します：

```csharp
// 避けるべき書き方（名前空間の曖昧さがある）
_trayIcon.Icon = new Avalonia.Controls.WindowIcon(bitmap);

// 推奨される書き方（明示的にグローバル名前空間を指定）
_trayIcon.Icon = new global::Avalonia.Controls.WindowIcon(bitmap);
```

### 7.2 XAML名前空間のエイリアス

XAML内で名前空間のエイリアスを適切に定義します：

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Baketa.UI.Avalonia.ViewModels"
             xmlns:local="using:Baketa.UI.Avalonia.Controls"
             xmlns:conv="using:Baketa.UI.Avalonia.Converters">
    
    <!-- ViewModelsからのバインディング -->
    <StackPanel DataContext="{Binding Source={x:Static vm:ViewModelLocator.Main}}">
        <!-- コンテンツ -->
    </StackPanel>
    
    <!-- ローカルコントロールの使用 -->
    <local:OcrPreviewControl Image="{Binding CurrentImage}" />
    
    <!-- コンバーターの使用 -->
    <TextBlock Text="{Binding LastUpdate, Converter={x:Static conv:DateTimeConverter.Instance}}" />
</UserControl>
```

### 7.3 型解決の明示的な指定

Avaloniaの型解決で問題が発生する場合は、明示的に型を指定します：

```csharp
// UIインスタンスの動的生成
public Control CreateSettingsView(string viewName)
{
    // 明示的な型とアセンブリの指定
    var viewType = Type.GetType($"Baketa.UI.Avalonia.Views.{viewName}View, Baketa.UI.Avalonia");
    
    if (viewType != null)
    {
        return (Control)Activator.CreateInstance(viewType);
    }
    
    // フォールバック
    return new TextBlock { Text = $"{viewName} ビューが見つかりません" };
}
```

## 8. XAML での問題回避

### 8.1 複雑な型のバインディング

Generic型（Dictionary<string, int>など）やシステム型のバインディングはXAMLでエラーが発生しやすいです。以下のいずれかの方法で対応します：

```csharp
// オプション1: 専用のビューモデルに変換してからバインド
public class KeyValueViewModel
{
    public string Key { get; set; }
    public int Value { get; set; }
    
    public KeyValueViewModel(string key, int value)
    {
        Key = key;
        Value = value;
    }
}

// ViewModelのプロパティ
private ObservableCollection<KeyValueViewModel> _items;

// Dictionary<string, int>から変換
_items = new ObservableCollection<KeyValueViewModel>(
    dictionary.Select(kvp => new KeyValueViewModel(kvp.Key, kvp.Value))
);
```

```xml
<!-- XAMLでのバインディング -->
<ItemsControl ItemsSource="{Binding Items}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="{Binding Key}" />
                <TextBlock Text="{Binding Value}" />
            </StackPanel>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

### 8.2 コンパイル済みバインディングのトラブル解決

`x:DataType`を使用したコンパイル済みバインディングでエラーが発生する場合の解決策:

1. **動的バインディングに切り替え**: `x:DataType`属性を削除し、動的バインディングを使用
2. **名前付きコントロール**: `x:Name`を使用して参照元を明確にする
3. **DataTemplateの簡略化**: 複雑なデータテンプレートを避け、シンプルなテンプレートに分割

```xml
<!-- 問題が発生しやすいバインディング -->
<ItemsControl ItemsSource="{Binding Items}">
    <ItemsControl.DataTemplates>
        <DataTemplate x:DataType="local:ComplexType">
            <!-- 複雑なテンプレート内容 -->
        </DataTemplate>
    </ItemsControl.DataTemplates>
</ItemsControl>

<!-- より安定するバインディング -->
<ItemsControl ItemsSource="{Binding Items}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <!-- シンプルなテンプレート内容 -->
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

### 8.3 バインディングエラーの解決

よくあるバインディングエラーとその解決方法：

#### ItemsとItemsSourceの混同

```xml
<!-- 避けるべき書き方 -->
<ComboBox Items="{Binding AvailableLanguages}">

<!-- 正しい書き方 -->
<ComboBox ItemsSource="{Binding AvailableLanguages}">
```

#### リスト項目内のパス参照

```xml
<!-- 避けるべき書き方 - 親コンテキストへの参照ができない -->
<ListBox ItemsSource="{Binding Items}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <Button Command="{Binding Path=DataContext.DeleteCommand}" />
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>

<!-- 正しい書き方 - RelativeSourceを使用して親要素を参照 -->
<ListBox ItemsSource="{Binding Items}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <Button Command="{Binding DataContext.DeleteCommand, 
                     RelativeSource={RelativeSource FindAncestor, AncestorType=ListBox}}" />
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

#### Null許容演算子の問題

XAMLでは`?.`演算子は使用できないため、代わりにConverter または プロパティを使用します：

```xml
<!-- 避けるべき書き方 - エラーが発生する -->
<TextBlock Text="{Binding UserProfile?.Name}" />

<!-- 正しい書き方1 - NullCheckConverterを使用 -->
<TextBlock Text="{Binding UserProfile, Converter={StaticResource NullCheckConverter}, ConverterParameter=Name}" />

<!-- 正しい書き方2 - ViewModelでプロパティを用意 -->
<TextBlock Text="{Binding UserName}" />
```

```csharp
// ViewModelで安全なプロパティを実装
public string UserName => UserProfile?.Name ?? string.Empty;
```

## 9. 依存性注入との統合

### 9.1 サービスの登録

Avalonia UIと依存性注入を統合します：

```csharp
// サービス登録とアクセス
public partial class App : Application
{
    public IServiceProvider Services { get; }
    
    public new static App Current => (App)Application.Current;
    
    public App()
    {
        Services = ConfigureServices();
    }
    
    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        
        // サービスの登録
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        services.AddSingleton<INotificationService, AvaloniaNotificationService>();
        
        // ViewModelsの登録
        services.AddSingleton<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        
        return services.BuildServiceProvider();
    }
    
    // アプリケーション初期化と実行...
}
```

### 9.2 サービスの取得

ビューやビューモデルでサービスを取得します：

```csharp
// オプション1: コンストラクタインジェクション（推奨）
public class MainViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    
    public MainViewModel(IDialogService dialogService, ISettingsService settingsService)
    {
        _dialogService = dialogService;
        _settingsService = settingsService;
        
        // 初期化...
    }
    
    // メソッドとプロパティ...
}

// オプション2: サービスロケーター（テストが難しくなるため、必要な場合のみ使用）
public void ShowError(string message)
{
    var dialogService = App.Current.Services.GetRequiredService<IDialogService>();
    dialogService.ShowError("エラー", message);
}
```

### 9.3 ViewModelLocatorパターン

ViewModelLocatorパターンを使用して、XAMLからビューモデルにアクセスします：

```csharp
// ViewModelLocatorの実装
public static class ViewModelLocator
{
    public static MainViewModel Main => 
        App.Current.Services.GetRequiredService<MainViewModel>();
        
    public static SettingsViewModel Settings => 
        App.Current.Services.GetRequiredService<SettingsViewModel>();
}
```

```xml
<!-- XAMLでの使用 -->
<Window xmlns:vm="using:Baketa.UI.Avalonia.ViewModels">
    <Window.DataContext>
        <Binding Source="{x:Static vm:ViewModelLocator.Main}" />
    </Window.DataContext>
    
    <!-- コンテンツ -->
</Window>
```

## 10. パフォーマンス最適化

### 10.1 仮想化の活用

大量のアイテムを表示する場合は、仮想化を活用します：

```xml
<!-- 仮想化の使用 -->
<ListBox ItemsSource="{Binding LargeCollection}" 
         VirtualizationMode="Simple">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <!-- アイテムテンプレート -->
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

### 10.2 バインディングの最適化

バインディングのパフォーマンスを向上させるヒント：

1. **単方向バインディングの使用**: 双方向バインディングが不要な場合は、明示的に単方向バインディングを指定

```xml
<!-- 単方向バインディングの明示 -->
<TextBlock Text="{Binding Status, Mode=OneWay}" />
```

2. **適切なプロパティ更新**: 更新が必要なプロパティのみを変更し、不要な再描画を避ける

```csharp
// 避けるべき書き方 - オブジェクト全体を置き換え
Items = new ObservableCollection<Item>(existingItems);

// 推奨される書き方 - 既存コレクションを更新
Items.Clear();
foreach (var item in newItems)
{
    Items.Add(item);
}
```

3. **一時的なバインディング無効化**: 複数プロパティの一括更新時にはバインディングを一時的に無効化

```csharp
// 推奨される書き方 - 複数のプロパティ更新をバッチ処理
using (DelayChangeNotifications())
{
    Title = "新しいタイトル";
    Description = "新しい説明";
    IsActive = true;
    // 複数のプロパティを更新...
}
```

### 10.3 遅延ロードと部分的更新

必要な時にだけUIを更新または構築します：

```csharp
// 推奨される書き方 - 必要に応じた遅延ロード
public class MainViewModel : ViewModelBase
{
    private SettingsViewModel _settingsViewModel;
    
    public SettingsViewModel SettingsViewModel => 
        _settingsViewModel ??= App.Current.Services.GetRequiredService<SettingsViewModel>();
        
    // プロパティとコマンド...
}
```

### 10.4 リソース管理の最適化

大きなリソースを適切に管理します：

```csharp
// 推奨される書き方 - リソースの適切な解放
public class ImageViewModel : ViewModelBase, IDisposable
{
    private IImage _largeImage;
    
    public IImage LargeImage
    {
        get => _largeImage;
        set
        {
            var oldImage = _largeImage;
            if (this.RaiseAndSetIfChanged(ref _largeImage, value))
            {
                // 古いイメージを解放
                oldImage?.Dispose();
            }
        }
    }
    
    public void Dispose()
    {
        _largeImage?.Dispose();
        _largeImage = null;
    }
}
```

以上がBaketaプロジェクトにおけるAvalonia UIガイドラインです。これらのガイドラインに従うことで、一貫性のある高品質なユーザーインターフェースを構築できます。