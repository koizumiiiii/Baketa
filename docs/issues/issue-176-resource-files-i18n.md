# Issue #176: リソースファイル作成（多言語対応）

## 📋 概要
.NETリソースファイル (.resx) を作成し、日本語と英語の多言語対応を実装します。

## 🎯 目的
- 日本語/英語の切り替え可能なUI
- 国際化 (i18n) 対応の基盤構築
- 将来的な他言語追加の準備

## 📦 Epic
**Epic 5: 多言語対応** (#176 - #178)

## 🔗 依存関係
- **Blocks**: #177 (言語切替機能), #178 (英語翻訳品質チェック)
- **Blocked by**: #171 (メインウィンドウUI刷新)
- **Related**: なし

## 📝 要件

### 機能要件

#### 1. リソースファイル構成
```
Baketa.UI/
└── Resources/
    ├── Strings.resx              (デフォルト: 日本語)
    ├── Strings.en.resx           (英語)
    └── Strings.Designer.cs       (自動生成)
```

**Strings.resx (日本語 - デフォルト)**
```xml
<data name="App_Title" xml:space="preserve">
  <value>Baketa</value>
</data>
<data name="MainWindow_TargetButton" xml:space="preserve">
  <value>対象ウィンドウ選択</value>
</data>
<data name="MainWindow_LiveButton" xml:space="preserve">
  <value>Live翻訳</value>
</data>
<data name="MainWindow_SingleshotButton" xml:space="preserve">
  <value>Singleshot</value>
</data>
<data name="MainWindow_SettingsButton" xml:space="preserve">
  <value>設定</value>
</data>
<data name="MainWindow_ExitButton" xml:space="preserve">
  <value>終了</value>
</data>
```

**Strings.en.resx (英語)**
```xml
<data name="App_Title" xml:space="preserve">
  <value>Baketa</value>
</data>
<data name="MainWindow_TargetButton" xml:space="preserve">
  <value>Select Target Window</value>
</data>
<data name="MainWindow_LiveButton" xml:space="preserve">
  <value>Live Translation</value>
</data>
<data name="MainWindow_SingleshotButton" xml:space="preserve">
  <value>Singleshot</value>
</data>
<data name="MainWindow_SettingsButton" xml:space="preserve">
  <value>Settings</value>
</data>
<data name="MainWindow_ExitButton" xml:space="preserve">
  <value>Exit</value>
</data>
```

#### 2. リソース項目一覧

**カテゴリ別リソースキー**

**App全般**
- `App_Title`: "Baketa"
- `App_LoadingMessage`: "起動中..." / "Loading..."
- `App_ErrorTitle`: "エラー" / "Error"

**MainWindow**
- `MainWindow_TargetButton`: "対象ウィンドウ選択" / "Select Target Window"
- `MainWindow_LiveButton`: "Live翻訳" / "Live Translation"
- `MainWindow_SingleshotButton`: "Singleshot" / "Singleshot"
- `MainWindow_SettingsButton`: "設定" / "Settings"
- `MainWindow_ExitButton`: "終了" / "Exit"
- `MainWindow_SelectedWindow`: "[選択中: {0}]" / "[Selected: {0}]"
- `MainWindow_TranslationCount`: "翻訳済み: {0}" / "Translated: {0}"

**SettingsWindow**
- `Settings_Title`: "設定" / "Settings"
- `Settings_Theme`: "テーマ" / "Theme"
- `Settings_ThemeLight`: "Light" / "Light"
- `Settings_ThemeDark`: "Dark" / "Dark"
- `Settings_FontSize`: "フォントサイズ" / "Font Size"
- `Settings_FontSizeExtraSmall`: "極小" / "Extra Small"
- `Settings_FontSizeSmall`: "小" / "Small"
- `Settings_FontSizeMedium`: "標準" / "Medium"
- `Settings_FontSizeLarge`: "大" / "Large"
- `Settings_FontSizeExtraLarge`: "極大" / "Extra Large"
- `Settings_Language`: "言語" / "Language"
- `Settings_LanguageJapanese`: "日本語" / "Japanese"
- `Settings_LanguageEnglish`: "English" / "English"
- `Settings_CurrentPlan`: "現在のプラン" / "Current Plan"
- `Settings_UpgradeToPremium`: "Premiumにアップグレード" / "Upgrade to Premium"

**LoginWindow**
- `Login_Title`: "ログイン" / "Login"
- `Login_Email`: "メールアドレス" / "Email"
- `Login_Password`: "パスワード" / "Password"
- `Login_LoginButton`: "ログイン" / "Login"
- `Login_SignUpButton`: "新規登録" / "Sign Up"
- `Login_ForgotPassword`: "パスワードを忘れた" / "Forgot Password"
- `Login_ErrorInvalidEmail`: "無効なメールアドレスです" / "Invalid email address"
- `Login_ErrorPasswordTooShort`: "パスワードは8文字以上必要です" / "Password must be at least 8 characters"
- `Login_ErrorLoginFailed`: "ログインに失敗しました" / "Login failed"

**PremiumPlanDialog**
- `Premium_Title`: "Baketa Premium" / "Baketa Premium"
- `Premium_FeatureAdFree`: "広告非表示" / "Ad-free"
- `Premium_FeatureCloudTranslation`: "クラウド翻訳 (Google Gemini)" / "Cloud Translation (Google Gemini)"
- `Premium_FeaturePrioritySupport`: "優先サポート" / "Priority Support"
- `Premium_FeatureEarlyAccess`: "新機能への優先アクセス" / "Early Access to New Features"
- `Premium_Monthly`: "月額 ¥500" / "¥500/month"
- `Premium_Yearly`: "年額 ¥5,000 (17% OFF)" / "¥5,000/year (17% OFF)"
- `Premium_Cancel`: "キャンセル" / "Cancel"

**エラーメッセージ**
- `Error_NetworkError`: "ネットワークエラーが発生しました" / "Network error occurred"
- `Error_AuthenticationFailed`: "認証に失敗しました" / "Authentication failed"
- `Error_TranslationFailed`: "翻訳に失敗しました" / "Translation failed"
- `Error_OcrFailed`: "OCRに失敗しました" / "OCR failed"
- `Error_WindowNotFound`: "対象ウィンドウが見つかりません" / "Target window not found"

### 非機能要件

1. **リソース管理**
   - `Strings.Designer.cs` は自動生成 (手動編集不可)
   - リソースファイルはUTF-8エンコーディング

2. **命名規則**
   - キー形式: `{カテゴリ}_{項目名}` (例: `MainWindow_LiveButton`)
   - PascalCase使用

3. **フォールバック**
   - 翻訳が見つからない場合はデフォルト (日本語) を使用

## 🏗️ 実装方針

### 1. Baketa.UI.csproj設定
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <!-- リソースファイル -->
    <EmbeddedResource Update="Resources\Strings.resx">
      <Generator>ResXFileCodeGenerator</Generator>
      <LastGenOutput>Strings.Designer.cs</LastGenOutput>
    </EmbeddedResource>
    <EmbeddedResource Update="Resources\Strings.en.resx">
      <DependentUpon>Strings.resx</DependentUpon>
    </EmbeddedResource>
  </ItemGroup>

  <ItemGroup>
    <Compile Update="Resources\Strings.Designer.cs">
      <DesignTime>True</DesignTime>
      <AutoGen>True</AutoGen>
      <DependentUpon>Strings.resx</DependentUpon>
    </Compile>
  </ItemGroup>
</Project>
```

### 2. Strings.Designer.cs (自動生成例)
```csharp
namespace Baketa.UI.Resources;

[global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
[global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
[global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
internal class Strings
{
    private static global::System.Resources.ResourceManager resourceMan;
    private static global::System.Globalization.CultureInfo resourceCulture;

    [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
    internal Strings() { }

    [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
    internal static global::System.Resources.ResourceManager ResourceManager
    {
        get
        {
            if (resourceMan == null)
            {
                global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Baketa.UI.Resources.Strings", typeof(Strings).Assembly);
                resourceMan = temp;
            }
            return resourceMan;
        }
    }

    [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
    internal static global::System.Globalization.CultureInfo Culture
    {
        get { return resourceCulture; }
        set { resourceCulture = value; }
    }

    internal static string App_Title => ResourceManager.GetString("App_Title", resourceCulture);
    internal static string MainWindow_LiveButton => ResourceManager.GetString("MainWindow_LiveButton", resourceCulture);
    // ... 他のプロパティ
}
```

### 3. LocalizationExtension（エラーハンドリング強化版）
```csharp
namespace Baketa.UI.Extensions;

public class LocalizeExtension : MarkupExtension
{
    private static readonly ILogger<LocalizeExtension> _logger =
        App.ServiceProvider?.GetService<ILogger<LocalizeExtension>>() ??
        NullLogger<LocalizeExtension>.Instance;

    public string Key { get; set; } = string.Empty;
    public string? FallbackValue { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
        {
            _logger.LogWarning("LocalizeExtension: キーが空です");
            return FallbackValue ?? "[EMPTY_KEY]";
        }

        try
        {
            // リソース取得試行
            var resourceValue = Strings.ResourceManager.GetString(Key, Strings.Culture);

            if (resourceValue != null)
            {
                return resourceValue;
            }

            // フォールバック戦略: 英語 → 日本語 → デフォルト値 → キー名
            _logger.LogWarning("リソースキー '{Key}' が見つかりません（Culture: {Culture}）", Key, Strings.Culture?.Name ?? "default");

            // 英語で再試行
            var enResource = Strings.ResourceManager.GetString(Key, new CultureInfo("en-US"));
            if (enResource != null)
            {
                _logger.LogDebug("リソースキー '{Key}' を英語フォールバックで取得しました", Key);
                return enResource;
            }

            // 日本語で再試行
            var jaResource = Strings.ResourceManager.GetString(Key, new CultureInfo("ja-JP"));
            if (jaResource != null)
            {
                _logger.LogDebug("リソースキー '{Key}' を日本語フォールバックで取得しました", Key);
                return jaResource;
            }

            // すべて失敗した場合
            return FallbackValue ?? $"[{Key}]";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "リソース取得中にエラーが発生しました: {Key}", Key);
            return FallbackValue ?? $"[ERROR:{Key}]";
        }
    }
}
```

### 4. XAML使用例
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ext="clr-namespace:Baketa.UI.Extensions"
        x:Class="Baketa.UI.Views.MainWindow"
        Title="{ext:Localize Key=App_Title}">

    <StackPanel>
        <Button Content="{ext:Localize Key=MainWindow_TargetButton}"
                Command="{Binding SelectTargetWindowCommand}" />

        <Button Content="{ext:Localize Key=MainWindow_LiveButton}"
                Command="{Binding ToggleLiveTranslationCommand}" />

        <Button Content="{ext:Localize Key=MainWindow_SingleshotButton}"
                Command="{Binding ExecuteSingleshotCommand}" />
    </StackPanel>
</Window>
```

### 5. コード内での使用例
```csharp
namespace Baketa.UI.ViewModels;

public class MainViewModel : ViewModelBase
{
    public string LiveButtonText => Strings.MainWindow_LiveButton;
    public string SettingsButtonText => Strings.MainWindow_SettingsButton;

    public string GetTranslationCountText(int count)
    {
        return string.Format(Strings.MainWindow_TranslationCount, count);
    }
}
```

### 6. リソース検証ツール
```csharp
namespace Baketa.UI.Utilities;

public static class ResourceValidator
{
    private static readonly ILogger _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("ResourceValidator");

    /// <summary>
    /// すべてのリソースキーが日本語・英語両方で定義されているかチェック
    /// </summary>
    public static List<string> ValidateAllKeysExist()
    {
        var missingKeys = new List<string>();
        var resourceSet = Strings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, true);

        if (resourceSet == null)
        {
            _logger.LogError("リソースセットの取得に失敗しました");
            return missingKeys;
        }

        foreach (DictionaryEntry entry in resourceSet)
        {
            var key = entry.Key.ToString();
            if (key == null) continue;

            // 日本語チェック
            var jaValue = Strings.ResourceManager.GetString(key, new CultureInfo("ja-JP"));
            if (string.IsNullOrEmpty(jaValue))
            {
                missingKeys.Add($"{key} (ja-JP)");
                _logger.LogWarning("リソースキー '{Key}' の日本語が定義されていません", key);
            }

            // 英語チェック
            var enValue = Strings.ResourceManager.GetString(key, new CultureInfo("en-US"));
            if (string.IsNullOrEmpty(enValue))
            {
                missingKeys.Add($"{key} (en-US)");
                _logger.LogWarning("リソースキー '{Key}' の英語が定義されていません", key);
            }
        }

        return missingKeys;
    }

    /// <summary>
    /// 文字数制限チェック（UIレイアウトに収まるか）
    /// </summary>
    public static List<string> ValidateLengthLimits()
    {
        var violations = new List<string>();
        var lengthLimits = new Dictionary<string, int>
        {
            { "MainWindow_TargetButton", 30 },
            { "MainWindow_LiveButton", 20 },
            { "MainWindow_SingleshotButton", 20 },
            { "Settings_UpgradeToPremium", 40 },
            { "Login_ErrorPasswordTooShort", 60 }
        };

        foreach (var (key, maxLength) in lengthLimits)
        {
            // 日本語チェック
            var jaValue = Strings.ResourceManager.GetString(key, new CultureInfo("ja-JP"));
            if (jaValue != null && jaValue.Length > maxLength)
            {
                violations.Add($"{key} (ja-JP): {jaValue.Length} 文字 > {maxLength} 文字");
                _logger.LogWarning("リソースキー '{Key}' (日本語) が文字数制限を超過: {Length} > {MaxLength}", key, jaValue.Length, maxLength);
            }

            // 英語チェック
            var enValue = Strings.ResourceManager.GetString(key, new CultureInfo("en-US"));
            if (enValue != null && enValue.Length > maxLength)
            {
                violations.Add($"{key} (en-US): {enValue.Length} chars > {maxLength} chars");
                _logger.LogWarning("リソースキー '{Key}' (英語) が文字数制限を超過: {Length} > {MaxLength}", key, enValue.Length, maxLength);
            }
        }

        return violations;
    }

    /// <summary>
    /// フォーマット文字列検証（{0}, {1}プレースホルダーの数が一致するか）
    /// </summary>
    public static List<string> ValidateFormatStrings()
    {
        var violations = new List<string>();
        var formatKeys = new[]
        {
            "MainWindow_SelectedWindow",
            "MainWindow_TranslationCount"
        };

        foreach (var key in formatKeys)
        {
            var jaValue = Strings.ResourceManager.GetString(key, new CultureInfo("ja-JP"));
            var enValue = Strings.ResourceManager.GetString(key, new CultureInfo("en-US"));

            if (jaValue == null || enValue == null) continue;

            var jaPlaceholderCount = CountPlaceholders(jaValue);
            var enPlaceholderCount = CountPlaceholders(enValue);

            if (jaPlaceholderCount != enPlaceholderCount)
            {
                violations.Add($"{key}: 日本語={jaPlaceholderCount}個, 英語={enPlaceholderCount}個");
                _logger.LogWarning(
                    "リソースキー '{Key}' のプレースホルダー数が不一致: 日本語={JaCount}, 英語={EnCount}",
                    key, jaPlaceholderCount, enPlaceholderCount);
            }
        }

        return violations;
    }

    private static int CountPlaceholders(string text)
    {
        var matches = Regex.Matches(text, @"\{\d+\}");
        return matches.Count;
    }
}
```

### 7. リソースファイル作成手順
1. Visual Studio で `Baketa.UI` プロジェクトを右クリック
2. "追加" → "新しい項目" → "リソースファイル (.resx)"
3. ファイル名: `Strings.resx` (デフォルト)
4. プロパティ → "カスタムツール" を `ResXFileCodeGenerator` に設定
5. リソース項目を追加 (Name: `App_Title`, Value: `Baketa`)
6. `Strings.en.resx` を作成 (手順3-5を繰り返し、英語の値を入力)
7. ビルドして `Strings.Designer.cs` が自動生成されることを確認

## ✅ 受け入れ基準

### 機能テスト
- [ ] `Strings.resx` に日本語リソースが定義されている
- [ ] `Strings.en.resx` に英語リソースが定義されている
- [ ] `Strings.Designer.cs` が自動生成される
- [ ] XAMLで `LocalizeExtension` を使用できる
- [ ] コード内で `Strings.XXX` プロパティを使用できる
- [ ] リソースキーが見つからない場合、デフォルト値が返される

### リソース網羅性テスト
- [ ] MainWindow のすべてのテキストがリソース化されている
- [ ] SettingsWindow のすべてのテキストがリソース化されている
- [ ] LoginWindow のすべてのテキストがリソース化されている
- [ ] PremiumPlanDialog のすべてのテキストがリソース化されている
- [ ] エラーメッセージがリソース化されている

### 単体テスト（12個）
```csharp
public class StringResourcesTests
{
    // 1. 基本テスト (4個)
    [Fact]
    public void Strings_日本語_正しい値を取得()
    {
        // Arrange
        Strings.Culture = new CultureInfo("ja-JP");

        // Act
        var title = Strings.App_Title;
        var liveButton = Strings.MainWindow_LiveButton;

        // Assert
        title.Should().Be("Baketa");
        liveButton.Should().Be("Live翻訳");
    }

    [Fact]
    public void Strings_英語_正しい値を取得()
    {
        // Arrange
        Strings.Culture = new CultureInfo("en-US");

        // Act
        var title = Strings.App_Title;
        var liveButton = Strings.MainWindow_LiveButton;

        // Assert
        title.Should().Be("Baketa");
        liveButton.Should().Be("Live Translation");
    }

    [Fact]
    public void Strings_Culture変更_値が切り替わる()
    {
        // Arrange & Act
        Strings.Culture = new CultureInfo("ja-JP");
        var jaValue = Strings.MainWindow_LiveButton;

        Strings.Culture = new CultureInfo("en-US");
        var enValue = Strings.MainWindow_LiveButton;

        // Assert
        jaValue.Should().Be("Live翻訳");
        enValue.Should().Be("Live Translation");
    }

    [Fact]
    public void Strings_フォーマット文字列_正しく動作()
    {
        // Arrange
        Strings.Culture = new CultureInfo("ja-JP");

        // Act
        var formatted = string.Format(Strings.MainWindow_TranslationCount, 42);

        // Assert
        formatted.Should().Be("翻訳済み: 42");
    }

    // 2. エラーハンドリングテスト (3個)
    [Fact]
    public void Strings_存在しないキー_nullを返す()
    {
        // Arrange
        var nonExistentKey = "NonExistent_Key_12345";

        // Act
        var value = Strings.ResourceManager.GetString(nonExistentKey);

        // Assert
        value.Should().BeNull();
    }

    [Fact]
    public void LocalizeExtension_空キー_フォールバック値を返す()
    {
        // Arrange
        var extension = new LocalizeExtension { Key = string.Empty, FallbackValue = "Fallback" };
        var serviceProvider = new Mock<IServiceProvider>().Object;

        // Act
        var result = extension.ProvideValue(serviceProvider);

        // Assert
        result.Should().Be("Fallback");
    }

    [Fact]
    public void LocalizeExtension_存在しないキー_キー名を表示()
    {
        // Arrange
        var extension = new LocalizeExtension { Key = "NonExistent_Key" };
        var serviceProvider = new Mock<IServiceProvider>().Object;

        // Act
        var result = extension.ProvideValue(serviceProvider);

        // Assert
        result.Should().Be("[NonExistent_Key]");
    }

    // 3. 検証テスト (3個)
    [Theory]
    [InlineData("MainWindow_TargetButton")]
    [InlineData("MainWindow_LiveButton")]
    [InlineData("MainWindow_SingleshotButton")]
    [InlineData("Settings_Theme")]
    [InlineData("Login_Email")]
    [InlineData("Premium_Title")]
    [InlineData("Error_NetworkError")]
    public void Strings_全キー_日本語と英語が定義されている(string key)
    {
        // Arrange
        var jaResource = Strings.ResourceManager.GetString(key, new CultureInfo("ja-JP"));
        var enResource = Strings.ResourceManager.GetString(key, new CultureInfo("en-US"));

        // Assert
        jaResource.Should().NotBeNullOrEmpty($"キー '{key}' の日本語が定義されていません");
        enResource.Should().NotBeNullOrEmpty($"キー '{key}' の英語が定義されていません");
    }

    [Theory]
    [InlineData("MainWindow_TargetButton", 30)]
    [InlineData("MainWindow_LiveButton", 20)]
    [InlineData("Settings_UpgradeToPremium", 40)]
    public void Strings_文字数制限_違反なし(string key, int maxLength)
    {
        // Arrange
        Strings.Culture = new CultureInfo("ja-JP");
        var jaValue = Strings.ResourceManager.GetString(key);

        Strings.Culture = new CultureInfo("en-US");
        var enValue = Strings.ResourceManager.GetString(key);

        // Assert
        jaValue.Should().NotBeNull();
        jaValue!.Length.Should().BeLessOrEqualTo(maxLength,
            $"キー '{key}' の日本語が長すぎます: {jaValue.Length} > {maxLength}");

        enValue.Should().NotBeNull();
        enValue!.Length.Should().BeLessOrEqualTo(maxLength,
            $"キー '{key}' の英語が長すぎます: {enValue.Length} > {maxLength}");
    }

    [Fact]
    public void ResourceValidator_全キー網羅性_違反なし()
    {
        // Act
        var missingKeys = ResourceValidator.ValidateAllKeysExist();

        // Assert
        missingKeys.Should().BeEmpty("すべてのキーが日本語と英語で定義されている必要があります");
    }

    // 4. XAMLバインディングテスト (2個)
    [Fact]
    public void LocalizeExtension_正しいキー_値を返す()
    {
        // Arrange
        Strings.Culture = new CultureInfo("ja-JP");
        var extension = new LocalizeExtension { Key = "MainWindow_LiveButton" };
        var serviceProvider = new Mock<IServiceProvider>().Object;

        // Act
        var result = extension.ProvideValue(serviceProvider);

        // Assert
        result.Should().Be("Live翻訳");
    }

    [Fact]
    public void LocalizeExtension_フォールバック戦略_正しく動作()
    {
        // Arrange
        Strings.Culture = new CultureInfo("fr-FR"); // 存在しない言語
        var extension = new LocalizeExtension { Key = "MainWindow_LiveButton" };
        var serviceProvider = new Mock<IServiceProvider>().Object;

        // Act
        var result = extension.ProvideValue(serviceProvider);

        // Assert
        // フランス語は存在しないため、英語または日本語にフォールバックされる
        result.Should().BeOneOf("Live Translation", "Live翻訳");
    }
}
```

## 📊 見積もり
- **作業時間**: 12時間
  - 基本実装: 8時間
  - リソース検証ツール: 2時間
  - テスト拡充: 2時間
- **優先度**: 🟠 High
- **リスク**: 🟢 Low
  - **軽減策**: リソースキー検証ツール、文字数制限チェック、包括的なテストカバレッジ

## 📌 備考
- リソースファイルはUTF-8 BOM付きで保存すること
- 翻訳品質は #178 で専門的にチェック
- 将来的に中国語 (簡体字/繁体字)、韓国語、スペイン語などを追加予定
- リソースキーの命名規則を厳守し、統一性を保つ
