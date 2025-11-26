# Issue #177: 言語切替機能実装

## 📋 概要
ユーザーが日本語と英語を切り替えられる機能を実装し、アプリケーション全体の言語を動的に変更できるようにします。

## 🎯 目的
- 日本語/英語のリアルタイム切り替え
- ユーザーの言語設定の永続化
- 国際的なユーザーベースへの対応

## 📦 Epic
**Epic 5: 多言語対応** (#176 - #178)

## 🔗 依存関係
- **Blocks**: #178 (英語翻訳品質チェック)
- **Blocked by**: #176 (リソースファイル作成)
- **Related**: #171 (メインウィンドウUI刷新)

## 📝 要件

### 機能要件

#### 1. 言語選択UI

**設定画面内の言語セレクター**
```
┌─────────────────────────┐
│  言語 / Language         │
│  ○ 日本語  ● English    │
└─────────────────────────┘
```
- ラジオボタンで選択
- 選択即時反映 (再起動不要)

**メインウィンドウからのクイックアクセス**
```
┌─────────────────┐
│   🌐 言語      │  ← クリックで日本語 ⇄ English切り替え
└─────────────────┘
```
- ホバー時に表示されるクイックアクセスボタン
- クリックで言語トグル

#### 2. 言語切り替え動作
- すべてのウィンドウのテキストが即座に変更
- 動的に生成されるテキスト (エラーメッセージ等) も対応
- オーバーレイウィンドウのテキストは翻訳結果 (変更なし)

#### 3. 言語永続化
- 選択した言語を `appsettings.json` に保存
- アプリケーション起動時に前回の言語を自動適用
- システム言語も考慮 (初回起動時)

#### 4. サポート言語
- **日本語 (ja-JP)**: デフォルト
- **英語 (en-US)**: セカンダリ
- 将来的に追加予定: 中国語 (zh-CN), 韓国語 (ko-KR), スペイン語 (es-ES)

### 非機能要件

1. **パフォーマンス**
   - 言語切替時の遅延: <200ms
   - UIの一時的なちらつきなし

2. **一貫性**
   - すべてのウィンドウで統一言語表示
   - 日付・数値フォーマットも言語に応じて変更

## 🏗️ 実装方針

### 1. ILocalizationService Interface
```csharp
namespace Baketa.Core.Abstractions.Services;

public interface ILocalizationService
{
    CultureInfo CurrentCulture { get; }
    string CurrentLanguage { get; }

    event EventHandler<LanguageChangedEventArgs> LanguageChanged;

    Task SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default);
    Task ToggleLanguageAsync(CancellationToken cancellationToken = default);
    string GetString(string key);
    string GetString(string key, params object[] args);
}

public class LanguageChangedEventArgs : EventArgs
{
    public required string OldLanguage { get; init; }
    public required string NewLanguage { get; init; }
    public required CultureInfo Culture { get; init; }
}
```

### 2. LocalizationService実装（エラーハンドリング・パフォーマンス計測強化版）
```csharp
namespace Baketa.Infrastructure.Services;

public class LocalizationService : ILocalizationService, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<LocalizationService> _logger;
    private readonly SemaphoreSlim _languageLock = new(1, 1);
    private bool _disposed;

    private const int LanguageSwitchTimeoutMs = 200;

    public CultureInfo CurrentCulture { get; private set; }
    public string CurrentLanguage { get; private set; }

    public event EventHandler<LanguageChangedEventArgs>? LanguageChanged;

    private static readonly Dictionary<string, CultureInfo> SupportedLanguages = new()
    {
        { "ja", new CultureInfo("ja-JP") },
        { "en", new CultureInfo("en-US") }
    };

    public LocalizationService(ISettingsService settingsService, ILogger<LocalizationService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;

        try
        {
            // 設定から言語を読み込み、なければシステム言語を使用
            var savedLanguage = _settingsService.Get<string>("Language", null);
            var initialLanguage = savedLanguage ?? GetSystemLanguage();

            CurrentLanguage = initialLanguage;
            CurrentCulture = SupportedLanguages.GetValueOrDefault(initialLanguage, new CultureInfo("ja-JP"));

            ApplyLanguage(CurrentCulture);

            _logger.LogInformation("LocalizationService initialized. Language: {Language}", CurrentLanguage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LocalizationService初期化中にエラーが発生しました");
            // フォールバック: 日本語をデフォルトとして設定
            CurrentLanguage = "ja";
            CurrentCulture = new CultureInfo("ja-JP");
            ApplyLanguage(CurrentCulture);
        }
    }

    public async Task SetLanguageAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            _logger.LogWarning("言語コードが空です");
            throw new LocalizationServiceException("言語コードが空です");
        }

        if (CurrentLanguage == languageCode)
        {
            _logger.LogDebug("言語は既に {Language} です。切替をスキップします", languageCode);
            return;
        }

        if (!SupportedLanguages.ContainsKey(languageCode))
        {
            _logger.LogWarning("サポートされていない言語: {Language}", languageCode);
            throw new LocalizationServiceException($"サポートされていない言語: {languageCode}");
        }

        await _languageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var oldLanguage = CurrentLanguage;

            try
            {
                CurrentLanguage = languageCode;
                CurrentCulture = SupportedLanguages[languageCode];

                // 言語を適用
                await Dispatcher.UIThread.InvokeAsync(() => ApplyLanguage(CurrentCulture));

                // 設定を保存
                await _settingsService.SetAsync("Language", languageCode, cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();

                _logger.LogInformation(
                    "言語切替成功: {OldLanguage} → {NewLanguage} ({ElapsedMs}ms)",
                    oldLanguage, languageCode, stopwatch.ElapsedMilliseconds);

                // パフォーマンス警告
                if (stopwatch.ElapsedMilliseconds > LanguageSwitchTimeoutMs)
                {
                    _logger.LogWarning(
                        "言語切替が目標時間（{TargetMs}ms）を超過しました: {ElapsedMs}ms",
                        LanguageSwitchTimeoutMs, stopwatch.ElapsedMilliseconds);
                }

                // イベント発火
                LanguageChanged?.Invoke(this, new LanguageChangedEventArgs
                {
                    OldLanguage = oldLanguage,
                    NewLanguage = languageCode,
                    Culture = CurrentCulture
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "言語切替に失敗しました: {Language}", languageCode);
                // ロールバック
                CurrentLanguage = oldLanguage;
                CurrentCulture = SupportedLanguages[oldLanguage];
                ApplyLanguage(CurrentCulture);
                throw new LocalizationServiceException($"言語切替に失敗しました: {languageCode}", ex);
            }
        }
        finally
        {
            _languageLock.Release();
        }
    }

    public async Task ToggleLanguageAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var newLanguage = CurrentLanguage == "ja" ? "en" : "ja";
            _logger.LogDebug("言語をトグルします: {OldLanguage} → {NewLanguage}", CurrentLanguage, newLanguage);
            await SetLanguageAsync(newLanguage, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "言語トグルに失敗しました");
            throw new LocalizationServiceException("言語トグルに失敗しました", ex);
        }
    }

    public string GetString(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("リソースキーが空です");
            return string.Empty;
        }

        try
        {
            var value = Strings.ResourceManager.GetString(key, CurrentCulture);
            if (value == null)
            {
                _logger.LogWarning("リソースキー '{Key}' が見つかりません（Culture: {Culture}）", key, CurrentCulture.Name);
                return key;
            }
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "リソース取得中にエラーが発生しました: {Key}", key);
            return key;
        }
    }

    public string GetString(string key, params object[] args)
    {
        try
        {
            var format = GetString(key);
            return string.Format(CurrentCulture, format, args);
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "フォーマット文字列エラー: {Key}, Args: {Args}", key, string.Join(", ", args));
            return GetString(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "リソース取得中にエラーが発生しました: {Key}", key);
            return key;
        }
    }

    private void ApplyLanguage(CultureInfo culture)
    {
        try
        {
            // スレッドのカルチャを設定
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            // Strings.Designer.cs のカルチャを設定
            Strings.Culture = culture;

            _logger.LogDebug("言語を適用しました: {Culture}", culture.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "言語適用中にエラーが発生しました: {Culture}", culture.Name);
            throw;
        }
    }

    private string GetSystemLanguage()
    {
        try
        {
            var systemLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var language = SupportedLanguages.ContainsKey(systemLang) ? systemLang : "ja";
            _logger.LogInformation("システム言語を検出しました: {SystemLang} → {Language}", systemLang, language);
            return language;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "システム言語の検出に失敗しました。デフォルト（日本語）を使用します");
            return "ja";
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _languageLock.Dispose();

        _disposed = true;
        _logger.LogDebug("LocalizationService disposed");
    }
}

// カスタム例外
public class LocalizationServiceException : Exception
{
    public LocalizationServiceException(string message) : base(message) { }
    public LocalizationServiceException(string message, Exception innerException) : base(message, innerException) { }
}
```

### 3. ReactiveProperty統合 (動的バインディング)
```csharp
namespace Baketa.UI.ViewModels;

public class ViewModelBase : ReactiveObject
{
    protected readonly ILocalizationService Localization;

    public ViewModelBase(ILocalizationService localization)
    {
        Localization = localization;

        // 言語変更時にすべてのプロパティを更新
        localization.LanguageChanged += (s, e) =>
        {
            this.RaisePropertyChanged(string.Empty); // すべてのプロパティを更新
        };
    }

    protected string L(string key) => Localization.GetString(key);
    protected string L(string key, params object[] args) => Localization.GetString(key, args);
}
```

### 4. MainViewModel統合
```csharp
public class MainViewModel : ViewModelBase
{
    // 動的ローカライズプロパティ
    public string LiveButtonText => L("MainWindow_LiveButton");
    public string SingleshotButtonText => L("MainWindow_SingleshotButton");
    public string SettingsButtonText => L("MainWindow_SettingsButton");
    public string ExitButtonText => L("MainWindow_ExitButton");

    public string GetSelectedWindowText(string windowName) =>
        L("MainWindow_SelectedWindow", windowName);

    public string GetTranslationCountText(int count) =>
        L("MainWindow_TranslationCount", count);

    public ReactiveCommand<Unit, Unit> ToggleLanguageCommand { get; }

    public MainViewModel(ILocalizationService localization) : base(localization)
    {
        ToggleLanguageCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await Localization.ToggleLanguageAsync();
        });
    }
}
```

### 5. SettingsViewModel統合
```csharp
public class SettingsViewModel : ViewModelBase
{
    [Reactive] public string SelectedLanguage { get; set; }

    public ReactiveCommand<string, Unit> SetLanguageCommand { get; }

    public SettingsViewModel(ILocalizationService localization) : base(localization)
    {
        SelectedLanguage = localization.CurrentLanguage;

        // 言語切替コマンド
        SetLanguageCommand = ReactiveCommand.CreateFromTask<string>(async languageCode =>
        {
            await Localization.SetLanguageAsync(languageCode);
            SelectedLanguage = languageCode;
        });
    }
}
```

### 6. Settings.axamlに言語セレクター追加
```xml
<StackPanel Spacing="10">
    <TextBlock Text="{Binding L('Settings_Language')}" FontWeight="Bold" />

    <StackPanel Orientation="Horizontal" Spacing="20">
        <RadioButton Content="日本語"
                     IsChecked="{Binding SelectedLanguage, Converter={StaticResource StringEqualConverter}, ConverterParameter=ja}"
                     Command="{Binding SetLanguageCommand}"
                     CommandParameter="ja" />

        <RadioButton Content="English"
                     IsChecked="{Binding SelectedLanguage, Converter={StaticResource StringEqualConverter}, ConverterParameter=en}"
                     Command="{Binding SetLanguageCommand}"
                     CommandParameter="en" />
    </StackPanel>
</StackPanel>
```

### 7. XAML動的バインディング (改良版LocalizeExtension)
```csharp
namespace Baketa.UI.Extensions;

public class LocalizeExtension : MarkupExtension
{
    private static ILocalizationService? _localizationService;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // LocalizationServiceをDIから取得 (初回のみ)
        _localizationService ??= (Application.Current as App)?.Services?.GetService<ILocalizationService>();

        if (_localizationService == null)
            return Key;

        // 動的バインディング (言語変更時に自動更新)
        var binding = new Binding
        {
            Source = _localizationService,
            Path = $"Item[{Key}]",
            Mode = BindingMode.OneWay
        };

        // 言語変更時にバインディングを再評価
        _localizationService.LanguageChanged += (s, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                // バインディングを強制更新
                if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target)
                {
                    if (target.TargetObject is AvaloniaObject obj && target.TargetProperty is AvaloniaProperty property)
                    {
                        obj.SetValue(property, _localizationService.GetString(Key));
                    }
                }
            });
        };

        return _localizationService.GetString(Key);
    }
}
```

### 8. XAML使用例 (動的更新対応)
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:ext="clr-namespace:Baketa.UI.Extensions"
        Title="{ext:Localize Key=App_Title}">

    <StackPanel>
        <Button Content="{Binding LiveButtonText}"
                Command="{Binding ToggleLiveTranslationCommand}" />

        <Button Content="{Binding SingleshotButtonText}"
                Command="{Binding ExecuteSingleshotCommand}" />

        <!-- 動的テキスト -->
        <TextBlock Text="{Binding TranslationCountText}" />
    </StackPanel>
</Window>
```

## ✅ 受け入れ基準

### 機能テスト
- [ ] 設定画面から日本語/英語を切り替えできる
- [ ] メインウィンドウのクイックアクセスボタンで言語トグルできる
- [ ] 言語切替が即時反映される (再起動不要)
- [ ] すべてのウィンドウのテキストが変更される
- [ ] 動的に生成されるテキスト (エラーメッセージ等) も正しい言語で表示される
- [ ] 言語が `appsettings.json` に保存される
- [ ] アプリケーション起動時に前回の言語が適用される
- [ ] 初回起動時にシステム言語が自動選択される

### UIテスト
- [ ] 日本語表示が正しい
- [ ] 英語表示が正しい
- [ ] 言語切替時にちらつきがない
- [ ] 日付・数値フォーマットが言語に応じて変更される

### パフォーマンステスト
- [ ] 言語切替時の遅延が200ms以内

### 単体テスト（15個）
```csharp
public class LocalizationServiceTests
{
    private readonly Mock<ISettingsService> _mockSettingsService;
    private readonly Mock<ILogger<LocalizationService>> _mockLogger;
    private readonly LocalizationService _service;

    public LocalizationServiceTests()
    {
        _mockSettingsService = new Mock<ISettingsService>();
        _mockLogger = new Mock<ILogger<LocalizationService>>();

        _service = new LocalizationService(
            _mockSettingsService.Object,
            _mockLogger.Object);
    }

    // 1. 基本機能テスト (5個)
    [Fact]
    public async Task SetLanguageAsync_日本語に切り替え()
    {
        // Act
        await _service.SetLanguageAsync("ja");

        // Assert
        _service.CurrentLanguage.Should().Be("ja");
        _service.CurrentCulture.Name.Should().Be("ja-JP");
        _mockSettingsService.Verify(x => x.SetAsync("Language", "ja", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetLanguageAsync_英語に切り替え()
    {
        // Act
        await _service.SetLanguageAsync("en");

        // Assert
        _service.CurrentLanguage.Should().Be("en");
        _service.CurrentCulture.Name.Should().Be("en-US");
        _mockSettingsService.Verify(x => x.SetAsync("Language", "en", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetLanguageAsync_同じ言語_スキップ()
    {
        // Arrange
        await _service.SetLanguageAsync("ja");

        // Act
        await _service.SetLanguageAsync("ja");

        // Assert
        // 2回目の呼び出しは設定保存されない
        _mockSettingsService.Verify(x => x.SetAsync("Language", "ja", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ToggleLanguageAsync_日本語から英語()
    {
        // Arrange
        await _service.SetLanguageAsync("ja");

        // Act
        await _service.ToggleLanguageAsync();

        // Assert
        _service.CurrentLanguage.Should().Be("en");
    }

    [Fact]
    public async Task ToggleLanguageAsync_英語から日本語()
    {
        // Arrange
        await _service.SetLanguageAsync("en");

        // Act
        await _service.ToggleLanguageAsync();

        // Assert
        _service.CurrentLanguage.Should().Be("ja");
    }

    // 2. リソース取得テスト (3個)
    [Fact]
    public void GetString_正しいリソースを取得()
    {
        // Arrange
        _service.SetLanguageAsync("ja").Wait();

        // Act
        var text = _service.GetString("MainWindow_LiveButton");

        // Assert
        text.Should().Be("Live翻訳");
    }

    [Fact]
    public void GetString_フォーマット付き()
    {
        // Arrange
        _service.SetLanguageAsync("ja").Wait();

        // Act
        var text = _service.GetString("MainWindow_TranslationCount", 10);

        // Assert
        text.Should().Be("翻訳済み: 10");
    }

    [Fact]
    public void GetString_存在しないキー_キー名を返す()
    {
        // Arrange
        var nonExistentKey = "NonExistent_Key_12345";

        // Act
        var text = _service.GetString(nonExistentKey);

        // Assert
        text.Should().Be(nonExistentKey);
    }

    // 3. エラーハンドリングテスト (4個)
    [Fact]
    public async Task SetLanguageAsync_空文字列_例外スロー()
    {
        // Act
        Func<Task> act = async () => await _service.SetLanguageAsync(string.Empty);

        // Assert
        await act.Should().ThrowAsync<LocalizationServiceException>()
            .WithMessage("言語コードが空です");
    }

    [Fact]
    public async Task SetLanguageAsync_サポート外言語_例外スロー()
    {
        // Act
        Func<Task> act = async () => await _service.SetLanguageAsync("fr");

        // Assert
        await act.Should().ThrowAsync<LocalizationServiceException>()
            .WithMessage("サポートされていない言語: fr");
    }

    [Fact]
    public void GetString_空キー_空文字列を返す()
    {
        // Act
        var text = _service.GetString(string.Empty);

        // Assert
        text.Should().BeEmpty();
    }

    [Fact]
    public void GetString_フォーマットエラー_キー値を返す()
    {
        // Arrange
        _service.SetLanguageAsync("ja").Wait();

        // Act - 引数の数が不一致
        var text = _service.GetString("MainWindow_TranslationCount"); // {0}が必要だが引数なし

        // Assert
        text.Should().Contain("翻訳済み"); // フォーマットエラーでも元の文字列を返す
    }

    // 4. イベントテスト (2個)
    [Fact]
    public async Task LanguageChanged_イベント発火()
    {
        // Arrange
        LanguageChangedEventArgs? eventArgs = null;
        _service.LanguageChanged += (s, e) => eventArgs = e;

        // Act
        await _service.SetLanguageAsync("en");

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.OldLanguage.Should().Be("ja");
        eventArgs!.NewLanguage.Should().Be("en");
        eventArgs!.Culture.Name.Should().Be("en-US");
    }

    [Fact]
    public async Task LanguageChanged_複数回切替_イベント複数回発火()
    {
        // Arrange
        var eventCount = 0;
        _service.LanguageChanged += (s, e) => eventCount++;

        // Act
        await _service.SetLanguageAsync("en");
        await _service.SetLanguageAsync("ja");
        await _service.SetLanguageAsync("en");

        // Assert
        eventCount.Should().Be(3);
    }

    // 5. パフォーマンステスト (2個)
    [Fact]
    public async Task SetLanguageAsync_パフォーマンス_200ms以内()
    {
        // Arrange
        var stopwatch = Stopwatch.StartNew();

        // Act
        await _service.SetLanguageAsync("en");
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(200,
            $"言語切替が200msを超過しました: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ToggleLanguageAsync_パフォーマンス_200ms以内()
    {
        // Arrange
        var stopwatch = Stopwatch.StartNew();

        // Act
        await _service.ToggleLanguageAsync();
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(200,
            $"言語トグルが200msを超過しました: {stopwatch.ElapsedMilliseconds}ms");
    }

    // 6. 同時実行制御テスト (1個)
    [Fact]
    public async Task SetLanguageAsync_同時実行_排他制御()
    {
        // Arrange & Act
        var task1 = _service.SetLanguageAsync("en");
        var task2 = _service.SetLanguageAsync("ja");
        await Task.WhenAll(task1, task2);

        // Assert
        // SemaphoreSlimにより排他制御されることを確認
        _service.CurrentLanguage.Should().BeOneOf("ja", "en");
    }

    // 7. Disposeテスト (1個)
    [Fact]
    public void Dispose_リソース解放()
    {
        // Act
        _service.Dispose();

        // Assert
        // SemaphoreSlimが解放されることを確認
        // (実際のテストではリフレクションまたはモックで検証)
    }
}
```

## 📊 見積もり
- **作業時間**: 16時間
  - 基本実装: 10時間
  - エラーハンドリング・ログ記録: 2時間
  - パフォーマンス計測・最適化: 2時間
  - テスト拡充: 2時間
- **優先度**: 🟠 High
- **リスク**: 🟡 Medium
  - **主要リスク**: 動的バインディングの複雑さ、XAMLバインディング更新の信頼性
  - **軽減策**: 包括的なエラーハンドリング、同時実行制御、パフォーマンス監視、詳細ログ記録

## 📌 備考
- 日付フォーマット例: 日本語 "2025年11月18日", 英語 "November 18, 2025"
- 数値フォーマット例: 日本語 "1,234", 英語 "1,234"
- 将来的に言語ファイル (.json) による外部化も検討
- 翻訳品質は #178 でネイティブスピーカーによるチェックを実施

---

## 🔐 Supabase Auth メールテンプレート多言語対応

### 背景
Issue #133 (Supabase Auth基盤構築) で設定したメールテンプレートを、アプリの言語設定と連動させる必要がある。

### 実装方針: Goテンプレート条件分岐

Supabaseのメールテンプレートは Go のテンプレート言語を使用しており、ユーザーメタデータに基づいて言語を切り替えられる。

#### 1. サインアップ時に言語メタデータを渡す (C#側)
```csharp
// Baketa.Infrastructure/Authentication/SupabaseAuthService.cs
public async Task<AuthResult> SignUpAsync(string email, string password, string language = "ja")
{
    var response = await _supabaseClient.Auth.SignUp(email, password, new SignUpOptions
    {
        Data = new Dictionary<string, object>
        {
            { "language", language },  // "ja" or "en"
            { "display_name", email.Split('@')[0] }
        }
    });
    // ...
}
```

#### 2. Supabaseメールテンプレート (Goテンプレート)

**確認メール (Confirm signup)**
```html
{{ if eq .Data.language "ja" }}
<h2>メールアドレスの確認</h2>
<p>Baketaへのご登録ありがとうございます。</p>
<p>以下のリンクをクリックしてメールアドレスを確認してください：</p>
<a href="{{ .ConfirmationURL }}" style="background-color: #4CAF50; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;">確認する</a>
<p>このリンクは24時間有効です。</p>
{{ else }}
<h2>Confirm your email</h2>
<p>Thank you for signing up for Baketa.</p>
<p>Click the link below to confirm your email address:</p>
<a href="{{ .ConfirmationURL }}" style="background-color: #4CAF50; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;">Confirm</a>
<p>This link is valid for 24 hours.</p>
{{ end }}
```

**パスワードリセット (Reset password)**
```html
{{ if eq .Data.language "ja" }}
<h2>パスワードリセット</h2>
<p>パスワードリセットのリクエストを受け付けました。</p>
<p>以下のリンクをクリックしてパスワードを再設定してください：</p>
<a href="{{ .ConfirmationURL }}" style="background-color: #2196F3; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;">パスワードを再設定</a>
<p>このリンクは1時間有効です。心当たりがない場合は、このメールを無視してください。</p>
{{ else }}
<h2>Reset your password</h2>
<p>We received a request to reset your password.</p>
<p>Click the link below to set a new password:</p>
<a href="{{ .ConfirmationURL }}" style="background-color: #2196F3; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;">Reset Password</a>
<p>This link is valid for 1 hour. If you didn't request this, please ignore this email.</p>
{{ end }}
```

**マジックリンク (Magic Link)**
```html
{{ if eq .Data.language "ja" }}
<h2>ログインリンク</h2>
<p>Baketaへのログインリンクをお送りします。</p>
<p>以下のリンクをクリックしてログインしてください：</p>
<a href="{{ .ConfirmationURL }}" style="background-color: #9C27B0; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;">ログイン</a>
<p>このリンクは10分間有効です。</p>
{{ else }}
<h2>Login Link</h2>
<p>Here's your login link for Baketa.</p>
<p>Click the link below to log in:</p>
<a href="{{ .ConfirmationURL }}" style="background-color: #9C27B0; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;">Log In</a>
<p>This link is valid for 10 minutes.</p>
{{ end }}
```

### タスク
- [ ] C#側でサインアップ時に言語メタデータを送信
- [ ] Supabaseダッシュボードでメールテンプレートを更新
- [ ] テスト: 日本語設定でサインアップ → 日本語メール受信
- [ ] テスト: 英語設定でサインアップ → 英語メール受信
- [ ] テスト: パスワードリセット (日本語/英語)
- [ ] テスト: マジックリンク (日本語/英語)

### 注意事項
- OAuth認証 (Google/Discord/Twitch) ではメールテンプレートは使用されない
- Email/Password認証時のみ有効
- 言語設定はユーザーの `raw_user_meta_data` に保存される
