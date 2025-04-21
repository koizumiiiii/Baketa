# Issue 10-5: テーマと国際化対応の基盤実装

## 概要
Baketaアプリケーションのテーマ切り替え機能と国際化（多言語対応）の基盤を実装します。これにより、ユーザーが好みの外観に調整でき、さまざまな言語でアプリケーションを使用できるようになります。

## 目的・理由
テーマと国際化対応は以下の理由で重要です：

1. ユーザー体験の向上：ユーザーは自分の好みに合わせてUIの外観をカスタマイズできる
2. アクセシビリティの向上：コントラストを高めた設定やダークモードによって、視覚的な快適さを提供
3. 幅広いユーザー層へのリーチ：異なる言語を話すユーザーがアプリケーションを使用可能に
4. 拡張性の確保：将来的な言語やテーマの追加が容易な基盤を整備

## 詳細
- テーマ切り替え機能の基盤実装
- 多言語対応のためのリソース管理システムの実装
- ユーザー設定との連携機能の実装
- RTL（右から左）言語対応の基盤整備

## タスク分解
- [ ] テーマ基盤の実装
  - [ ] `IThemeManager`インターフェースの設計
  - [ ] `ThemeManager`クラスの実装
  - [ ] テーマリソースディクショナリの作成
  - [ ] 動的テーマ切り替え機能の実装
- [ ] 標準テーマの実装
  - [ ] ライトテーマの実装
  - [ ] ダークテーマの実装
  - [ ] ハイコントラストテーマの実装
  - [ ] カスタムテーマの基盤実装
- [ ] 国際化基盤の実装
  - [ ] `ILocalizationManager`インターフェースの設計
  - [ ] `LocalizationManager`クラスの実装
  - [ ] リソースファイル管理システムの実装
  - [ ] マークアップ拡張による簡易アクセス機能の実装
- [ ] 言語リソースの実装
  - [ ] 日本語リソースの作成
  - [ ] 英語リソースの作成
  - [ ] 翻訳サポートプロセスの整備
- [ ] UIとの統合
  - [ ] テーマ選択UIコンポーネントの実装
  - [ ] 言語選択UIコンポーネントの実装
  - [ ] フォント設定の統合
- [ ] 設定との連携
  - [ ] テーマ設定の保存と読み込み機能
  - [ ] 言語設定の保存と読み込み機能
  - [ ] 設定UIとの連携
- [ ] 特殊言語対応
  - [ ] RTL言語対応の基盤実装
  - [ ] アジア言語フォント対応の実装
- [ ] 単体テストの実装

## インターフェース設計案
```csharp
namespace Baketa.UI.Theming
{
    /// <summary>
    /// テーママネージャーインターフェース
    /// </summary>
    public interface IThemeManager
    {
        /// <summary>
        /// 現在のテーマ
        /// </summary>
        ThemeType CurrentTheme { get; }
        
        /// <summary>
        /// 利用可能なテーマのコレクション
        /// </summary>
        IReadOnlyList<ThemeInfo> AvailableThemes { get; }
        
        /// <summary>
        /// テーマが変更された時に発生するイベント
        /// </summary>
        event EventHandler<ThemeChangedEventArgs> ThemeChanged;
        
        /// <summary>
        /// テーマを変更します
        /// </summary>
        /// <param name="themeType">テーマタイプ</param>
        /// <returns>変更が成功したかどうか</returns>
        bool ChangeTheme(ThemeType themeType);
        
        /// <summary>
        /// 指定したテーマリソースを取得します
        /// </summary>
        /// <param name="resourceKey">リソースキー</param>
        /// <returns>テーマリソース</returns>
        object? GetThemeResource(string resourceKey);
        
        /// <summary>
        /// システムテーマの変更を監視するかどうかを設定します
        /// </summary>
        /// <param name="enabled">有効かどうか</param>
        void SetSystemThemeTracking(bool enabled);
    }
    
    /// <summary>
    /// テーマタイプ
    /// </summary>
    public enum ThemeType
    {
        /// <summary>
        /// ライトテーマ
        /// </summary>
        Light,
        
        /// <summary>
        /// ダークテーマ
        /// </summary>
        Dark,
        
        /// <summary>
        /// ハイコントラストテーマ
        /// </summary>
        HighContrast,
        
        /// <summary>
        /// システムテーマに従う
        /// </summary>
        System,
        
        /// <summary>
        /// カスタムテーマ
        /// </summary>
        Custom
    }
    
    /// <summary>
    /// テーマ情報
    /// </summary>
    public class ThemeInfo
    {
        /// <summary>
        /// テーマタイプ
        /// </summary>
        public ThemeType Type { get; }
        
        /// <summary>
        /// テーマ名
        /// </summary>
        public string Name { get; }
        
        /// <summary>
        /// テーマの説明
        /// </summary>
        public string Description { get; }
        
        /// <summary>
        /// プレビュー画像のパス
        /// </summary>
        public string? PreviewImagePath { get; }
        
        /// <summary>
        /// リソースのキー
        /// </summary>
        public string ResourceKey { get; }
        
        /// <summary>
        /// 新しいテーマ情報を初期化します
        /// </summary>
        /// <param name="type">テーマタイプ</param>
        /// <param name="name">テーマ名</param>
        /// <param name="description">テーマの説明</param>
        /// <param name="resourceKey">リソースのキー</param>
        /// <param name="previewImagePath">プレビュー画像のパス</param>
        public ThemeInfo(
            ThemeType type,
            string name,
            string description,
            string resourceKey,
            string? previewImagePath = null)
        {
            Type = type;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            ResourceKey = resourceKey ?? throw new ArgumentNullException(nameof(resourceKey));
            PreviewImagePath = previewImagePath;
        }
    }
    
    /// <summary>
    /// テーマ変更イベント引数
    /// </summary>
    public class ThemeChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 以前のテーマ
        /// </summary>
        public ThemeType OldTheme { get; }
        
        /// <summary>
        /// 新しいテーマ
        /// </summary>
        public ThemeType NewTheme { get; }
        
        /// <summary>
        /// 新しいテーマ変更イベント引数を初期化します
        /// </summary>
        /// <param name="oldTheme">以前のテーマ</param>
        /// <param name="newTheme">新しいテーマ</param>
        public ThemeChangedEventArgs(ThemeType oldTheme, ThemeType newTheme)
        {
            OldTheme = oldTheme;
            NewTheme = newTheme;
        }
    }
}

namespace Baketa.UI.Localization
{
    /// <summary>
    /// ローカライゼーションマネージャーインターフェース
    /// </summary>
    public interface ILocalizationManager
    {
        /// <summary>
        /// 現在の言語
        /// </summary>
        CultureInfo CurrentCulture { get; }
        
        /// <summary>
        /// 利用可能な言語のコレクション
        /// </summary>
        IReadOnlyList<CultureInfo> AvailableCultures { get; }
        
        /// <summary>
        /// 言語が変更された時に発生するイベント
        /// </summary>
        event EventHandler<CultureChangedEventArgs> CultureChanged;
        
        /// <summary>
        /// 言語を変更します
        /// </summary>
        /// <param name="culture">カルチャ情報</param>
        /// <returns>変更が成功したかどうか</returns>
        bool ChangeCulture(CultureInfo culture);
        
        /// <summary>
        /// 指定したキーのローカライズされた文字列を取得します
        /// </summary>
        /// <param name="key">リソースキー</param>
        /// <param name="args">書式設定引数</param>
        /// <returns>ローカライズされた文字列</returns>
        string GetString(string key, params object[] args);
        
        /// <summary>
        /// 複数形に対応したローカライズされた文字列を取得します
        /// </summary>
        /// <param name="key">リソースキー</param>
        /// <param name="count">数量</param>
        /// <param name="args">書式設定引数</param>
        /// <returns>ローカライズされた文字列</returns>
        string GetPluralString(string key, int count, params object[] args);
        
        /// <summary>
        /// 指定したキーのローカライズされたリソースを取得します
        /// </summary>
        /// <param name="key">リソースキー</param>
        /// <returns>ローカライズされたリソース</returns>
        object? GetResource(string key);
    }
    
    /// <summary>
    /// 言語変更イベント引数
    /// </summary>
    public class CultureChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 以前の言語
        /// </summary>
        public CultureInfo OldCulture { get; }
        
        /// <summary>
        /// 新しい言語
        /// </summary>
        public CultureInfo NewCulture { get; }
        
        /// <summary>
        /// 新しい言語変更イベント引数を初期化します
        /// </summary>
        /// <param name="oldCulture">以前の言語</param>
        /// <param name="newCulture">新しい言語</param>
        public CultureChangedEventArgs(CultureInfo oldCulture, CultureInfo newCulture)
        {
            OldCulture = oldCulture;
            NewCulture = newCulture;
        }
    }
    
    /// <summary>
    /// ローカライズされたテキストを提供するマークアップ拡張
    /// </summary>
    public class LocalizeExtension : MarkupExtension
    {
        private static ILocalizationManager? _localizationManager;
        
        /// <summary>
        /// ローカライゼーションマネージャー
        /// </summary>
        public static ILocalizationManager LocalizationManager
        {
            get => _localizationManager ?? throw new InvalidOperationException("LocalizationManager is not initialized.");
            set => _localizationManager = value;
        }
        
        /// <summary>
        /// リソースキー
        /// </summary>
        public string Key { get; set; } = string.Empty;
        
        /// <summary>
        /// 新しいローカライズ拡張を初期化します
        /// </summary>
        public LocalizeExtension()
        {
        }
        
        /// <summary>
        /// 新しいローカライズ拡張を初期化します
        /// </summary>
        /// <param name="key">リソースキー</param>
        public LocalizeExtension(string key)
        {
            Key = key;
        }
        
        /// <override />
        public override object? ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key))
                return string.Empty;
                
            return LocalizationManager.GetString(Key);
        }
    }
}
```

## テーママネージャー実装例
```csharp
namespace Baketa.UI.Theming
{
    /// <summary>
    /// テーママネージャー実装クラス
    /// </summary>
    public class ThemeManager : IThemeManager
    {
        private readonly ILogger? _logger;
        private readonly Dictionary<string, ResourceDictionary> _themeResourceDictionaries = new();
        private ThemeType _currentTheme = ThemeType.Light;
        private bool _trackSystemTheme;
        private readonly List<ThemeInfo> _availableThemes = new();
        
        /// <summary>
        /// 新しいテーママネージャーを初期化します
        /// </summary>
        /// <param name="settingsService">設定サービス</param>
        /// <param name="logger">ロガー</param>
        public ThemeManager(ISettingsService settingsService, ILogger? logger = null)
        {
            _logger = logger;
            
            // 利用可能なテーマを登録
            RegisterBuiltInThemes();
            
            // 設定からテーマを読み込み
            LoadThemeSettings(settingsService);
            
            _logger?.LogInformation("テーママネージャーが初期化されました。現在のテーマ: {Theme}", _currentTheme);
        }
        
        /// <inheritdoc />
        public ThemeType CurrentTheme => _currentTheme;
        
        /// <inheritdoc />
        public IReadOnlyList<ThemeInfo> AvailableThemes => _availableThemes;
        
        /// <inheritdoc />
        public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;
        
        /// <inheritdoc />
        public bool ChangeTheme(ThemeType themeType)
        {
            if (_currentTheme == themeType)
                return true;
                
            if (!_availableThemes.Any(t => t.Type == themeType))
            {
                _logger?.LogWarning("指定されたテーマタイプ '{ThemeType}' は利用できません。", themeType);
                return false;
            }
            
            var oldTheme = _currentTheme;
            _currentTheme = themeType;
            
            ApplyCurrentTheme();
            
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(oldTheme, themeType));
            
            _logger?.LogInformation("テーマが '{OldTheme}' から '{NewTheme}' に変更されました。", oldTheme, themeType);
            
            return true;
        }
        
        /// <inheritdoc />
        public object? GetThemeResource(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey))
                return null;
                
            var themeInfo = _availableThemes.FirstOrDefault(t => t.Type == _currentTheme);
            if (themeInfo == null)
                return null;
                
            if (_themeResourceDictionaries.TryGetValue(themeInfo.ResourceKey, out var resourceDictionary))
            {
                if (resourceDictionary.Contains(resourceKey))
                    return resourceDictionary[resourceKey];
            }
            
            return null;
        }
        
        /// <inheritdoc />
        public void SetSystemThemeTracking(bool enabled)
        {
            if (_trackSystemTheme == enabled)
                return;
                
            _trackSystemTheme = enabled;
            
            if (enabled)
            {
                // システムテーマ変更の監視を開始
                StartSystemThemeTracking();
                
                // 現在のシステムテーマを取得して適用
                if (_currentTheme == ThemeType.System)
                {
                    ApplySystemTheme();
                }
            }
            else
            {
                // システムテーマ変更の監視を停止
                StopSystemThemeTracking();
            }
            
            _logger?.LogInformation("システムテーマの監視が {State} に設定されました。", enabled ? "有効" : "無効");
        }
        
        // プライベートメソッド実装
        // 省略
    }
}
```

## ローカライゼーションマネージャー実装例
```csharp
namespace Baketa.UI.Localization
{
    /// <summary>
    /// ローカライゼーションマネージャー実装クラス
    /// </summary>
    public class LocalizationManager : ILocalizationManager
    {
        private readonly ILogger? _logger;
        private readonly Dictionary<string, ResourceManager> _resourceManagers = new();
        private readonly List<CultureInfo> _availableCultures = new();
        private CultureInfo _currentCulture;
        
        /// <summary>
        /// 新しいローカライゼーションマネージャーを初期化します
        /// </summary>
        /// <param name="settingsService">設定サービス</param>
        /// <param name="logger">ロガー</param>
        public LocalizationManager(ISettingsService settingsService, ILogger? logger = null)
        {
            _logger = logger;
            
            // デフォルトカルチャを設定
            _currentCulture = CultureInfo.CurrentUICulture;
            
            // 利用可能な言語を登録
            RegisterBuiltInCultures();
            
            // リソースマネージャーを登録
            RegisterResourceManagers();
            
            // 設定から言語を読み込み
            LoadCultureSettings(settingsService);
            
            // マークアップ拡張に自身を設定
            LocalizeExtension.LocalizationManager = this;
            
            _logger?.LogInformation("ローカライゼーションマネージャーが初期化されました。現在の言語: {Culture}", _currentCulture.Name);
        }
        
        /// <inheritdoc />
        public CultureInfo CurrentCulture => _currentCulture;
        
        /// <inheritdoc />
        public IReadOnlyList<CultureInfo> AvailableCultures => _availableCultures;
        
        /// <inheritdoc />
        public event EventHandler<CultureChangedEventArgs>? CultureChanged;
        
        /// <inheritdoc />
        public bool ChangeCulture(CultureInfo culture)
        {
            if (culture == null)
                throw new ArgumentNullException(nameof(culture));
                
            if (_currentCulture.Equals(culture))
                return true;
                
            if (!_availableCultures.Any(c => c.Equals(culture)))
            {
                _logger?.LogWarning("指定された言語 '{Culture}' は利用できません。", culture.Name);
                return false;
            }
            
            var oldCulture = _currentCulture;
            _currentCulture = culture;
            
            // アプリケーション全体のカルチャを設定
            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            
            CultureChanged?.Invoke(this, new CultureChangedEventArgs(oldCulture, culture));
            
            _logger?.LogInformation("言語が '{OldCulture}' から '{NewCulture}' に変更されました。", oldCulture.Name, culture.Name);
            
            return true;
        }
        
        /// <inheritdoc />
        public string GetString(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;
                
            foreach (var resourceManager in _resourceManagers.Values)
            {
                try
                {
                    var value = resourceManager.GetString(key, _currentCulture);
                    if (!string.IsNullOrEmpty(value))
                    {
                        if (args.Length > 0)
                            return string.Format(value, args);
                            
                        return value;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "リソースキー '{Key}' の取得中にエラーが発生しました。", key);
                }
            }
            
            // リソースが見つからない場合はキーを返す（デバッグ用）
            _logger?.LogWarning("リソースキー '{Key}' が見つかりませんでした。", key);
            return $"[{key}]";
        }
        
        /// <inheritdoc />
        public string GetPluralString(string key, int count, params object[] args)
        {
            // 単数形と複数形のキーを生成
            var singularKey = key;
            var pluralKey = key + "_Plural";
            
            // 数量に応じたキーを選択
            var selectedKey = count == 1 ? singularKey : pluralKey;
            
            // パラメータリストの先頭に数量を追加
            var newArgs = new object[args.Length + 1];
            newArgs[0] = count;
            Array.Copy(args, 0, newArgs, 1, args.Length);
            
            return GetString(selectedKey, newArgs);
        }
        
        /// <inheritdoc />
        public object? GetResource(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
                
            // リソースオブジェクトの取得実装
            // 省略
            
            return null;
        }
        
        // プライベートメソッド実装
        // 省略
    }
}
```

## Avalonia XAMLでの使用例
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:loc="using:Baketa.UI.Localization"
        x:Class="Baketa.UI.Views.SettingsWindow"
        Title="{loc:Localize SettingsWindow_Title}"
        Width="800" Height="600">
    
    <Grid>
        <TabControl>
            <TabItem Header="{loc:Localize SettingsWindow_GeneralTab}">
                <!-- 一般設定 -->
                <StackPanel Margin="20">
                    <!-- テーマ設定 -->
                    <TextBlock Text="{loc:Localize SettingsWindow_ThemeSettings}"
                               FontWeight="Bold" Margin="0,0,0,10"/>
                    
                    <Grid ColumnDefinitions="Auto,*" RowDefinitions="Auto,Auto,Auto" Margin="0,0,0,20">
                        <TextBlock Grid.Row="0" Grid.Column="0" 
                                   Text="{loc:Localize SettingsWindow_Theme}"
                                   VerticalAlignment="Center" Margin="0,0,10,0"/>
                        
                        <ComboBox Grid.Row="0" Grid.Column="1" 
                                  ItemsSource="{Binding AvailableThemes}"
                                  SelectedItem="{Binding SelectedTheme}"
                                  DisplayMemberPath="Name"
                                  HorizontalAlignment="Left"
                                  Width="200"/>
                        
                        <CheckBox Grid.Row="1" Grid.Column="1" 
                                  Content="{loc:Localize SettingsWindow_UseSystemTheme}"
                                  IsChecked="{Binding UseSystemTheme}"
                                  Margin="0,10,0,0"/>
                    </Grid>
                    
                    <!-- 言語設定 -->
                    <TextBlock Text="{loc:Localize SettingsWindow_LanguageSettings}"
                               FontWeight="Bold" Margin="0,0,0,10"/>
                    
                    <Grid ColumnDefinitions="Auto,*" RowDefinitions="Auto" Margin="0,0,0,20">
                        <TextBlock Grid.Row="0" Grid.Column="0" 
                                   Text="{loc:Localize SettingsWindow_Language}"
                                   VerticalAlignment="Center" Margin="0,0,10,0"/>
                        
                        <ComboBox Grid.Row="0" Grid.Column="1" 
                                  ItemsSource="{Binding AvailableCultures}"
                                  SelectedItem="{Binding SelectedCulture}"
                                  HorizontalAlignment="Left"
                                  Width="200">
                            <ComboBox.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding NativeName}"/>
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>
                    </Grid>
                </StackPanel>
            </TabItem>
            
            <!-- その他のタブ -->
        </TabControl>
    </Grid>
</Window>
```

## 実装上の注意点
- リソースファイルの適切な構成と命名規則の確立
- 動的なテーマ・言語切り替え時のパフォーマンスに配慮
- RTL言語対応のためのレイアウト調整メカニズムの実装
- リソース未定義時のフォールバック動作の実装
- Avalonia UIフレームワークの制約を考慮したテーマ適用方法の選択
- コンポーネント間の疎結合を維持しつつ、テーマ・言語変更を伝播するメカニズムの実装
- メモリリークを防ぐためのイベント管理

## 関連Issue/参考
- 親Issue: #10 Avalonia UIフレームワーク完全実装
- 依存Issue: #10-1 ReactiveUIベースのMVVMフレームワーク実装
- 関連Issue: #12 設定画面
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\theming-guidelines.md
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\localization-strategy.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (7.2 コメントとドキュメンテーション)

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: medium`
- `component: ui`
