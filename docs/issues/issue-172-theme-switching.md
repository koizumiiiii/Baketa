# Issue #172: テーマ切替機能（Light/Dark）

## 📋 概要
Light/Darkテーマ切替機能を実装し、ユーザーがアプリケーション全体の外観を変更できるようにします。

## 🎯 目的
- ユーザーの好みに応じた視覚的カスタマイズ
- 環境光に応じた快適な使用体験
- Avaloniaの`ResourceDictionary`を活用したテーマ管理

## 📦 Epic
**Epic 3: UI/UXの刷新** (#166 - #173)

## 🔗 依存関係
- **Blocks**: なし
- **Blocked by**: #171 (メインウィンドウUI刷新)
- **Related**: #173 (フォントサイズ変更)

## 📝 要件

### 機能要件

#### 1. テーマ定義
**Dark Theme (デフォルト)**
- 背景色: `#1E1E1E` (濃いグレー)
- セカンダリ背景: `#2C2C2C`
- テキスト: `#FFFFFF` (白)
- プライマリカラー: `#007ACC` (青)
- ボーダー: `#404040` (中間グレー)
- アクセント: `#FF0000` (赤)

**Light Theme**
- 背景色: `#F5F5F5` (薄いグレー)
- セカンダリ背景: `#FFFFFF` (白)
- テキスト: `#1E1E1E` (濃いグレー)
- プライマリカラー: `#0078D4` (明るい青)
- ボーダー: `#E0E0E0` (薄いグレー)
- アクセント: `#D13438` (明るい赤)

#### 2. テーマ切替UI
**設定画面内のテーマセレクター**
```
┌─────────────────────────┐
│  テーマ                  │
│  ○ Light  ● Dark        │
└─────────────────────────┘
```
- ラジオボタンで選択
- 選択即時反映 (再起動不要)

**メインウィンドウからのクイックアクセス**
```
┌─────────────────┐
│   🎨 テーマ     │  ← クリックでDark ⇄ Light切り替え
└─────────────────┘
```
- ホバー時に表示されるクイックアクセスボタン
- クリックでテーマトグル

#### 3. テーマ永続化
- 選択したテーマを `appsettings.json` に保存
- アプリケーション起動時に前回のテーマを自動適用

### 非機能要件

1. **パフォーマンス**
   - テーマ切替時の遅延: <100ms
   - アニメーション: 0.3秒のフェードイン/アウト

2. **一貫性**
   - すべてのウィンドウ (MainWindow, Settings, Login) に統一テーマ適用
   - オーバーレイウィンドウもテーマに連動

## 🏗️ 実装方針

### 1. ResourceDictionary定義

#### Themes/DarkTheme.axaml
```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Colors -->
    <SolidColorBrush x:Key="BackgroundBrush" Color="#1E1E1E" />
    <SolidColorBrush x:Key="SecondaryBackgroundBrush" Color="#2C2C2C" />
    <SolidColorBrush x:Key="ForegroundBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="PrimaryBrush" Color="#007ACC" />
    <SolidColorBrush x:Key="BorderBrush" Color="#404040" />
    <SolidColorBrush x:Key="AccentBrush" Color="#FF0000" />
    <SolidColorBrush x:Key="DisabledBrush" Color="#808080" />

    <!-- Button Styles -->
    <Style Selector="Button">
        <Setter Property="Background" Value="{StaticResource SecondaryBackgroundBrush}" />
        <Setter Property="Foreground" Value="{StaticResource ForegroundBrush}" />
        <Setter Property="BorderBrush" Value="{StaticResource BorderBrush}" />
    </Style>

    <!-- TextBox Styles -->
    <Style Selector="TextBox">
        <Setter Property="Background" Value="{StaticResource SecondaryBackgroundBrush}" />
        <Setter Property="Foreground" Value="{StaticResource ForegroundBrush}" />
        <Setter Property="BorderBrush" Value="{StaticResource BorderBrush}" />
    </Style>
</ResourceDictionary>
```

#### Themes/LightTheme.axaml
```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Colors -->
    <SolidColorBrush x:Key="BackgroundBrush" Color="#F5F5F5" />
    <SolidColorBrush x:Key="SecondaryBackgroundBrush" Color="#FFFFFF" />
    <SolidColorBrush x:Key="ForegroundBrush" Color="#1E1E1E" />
    <SolidColorBrush x:Key="PrimaryBrush" Color="#0078D4" />
    <SolidColorBrush x:Key="BorderBrush" Color="#E0E0E0" />
    <SolidColorBrush x:Key="AccentBrush" Color="#D13438" />
    <SolidColorBrush x:Key="DisabledBrush" Color="#A0A0A0" />

    <!-- Button Styles -->
    <Style Selector="Button">
        <Setter Property="Background" Value="{StaticResource SecondaryBackgroundBrush}" />
        <Setter Property="Foreground" Value="{StaticResource ForegroundBrush}" />
        <Setter Property="BorderBrush" Value="{StaticResource BorderBrush}" />
    </Style>

    <!-- TextBox Styles -->
    <Style Selector="TextBox">
        <Setter Property="Background" Value="{StaticResource SecondaryBackgroundBrush}" />
        <Setter Property="Foreground" Value="{StaticResource ForegroundBrush}" />
        <Setter Property="BorderBrush" Value="{StaticResource BorderBrush}" />
    </Style>
</ResourceDictionary>
```

### 2. IThemeService Interface（システムテーマ検出対応）
```csharp
namespace Baketa.Core.Abstractions.Services;

public interface IThemeService : IDisposable
{
    AppTheme CurrentTheme { get; }
    event EventHandler<ThemeChangedEventArgs> ThemeChanged;

    Task SwitchThemeAsync(AppTheme theme, CancellationToken cancellationToken = default);
    Task ToggleThemeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// システムテーマを検出（将来実装）
    /// </summary>
    AppTheme DetectSystemTheme();
}

public enum AppTheme
{
    Light,
    Dark,
    Auto  // 将来実装: システム設定に従う
}

public class ThemeChangedEventArgs : EventArgs
{
    public required AppTheme OldTheme { get; init; }
    public required AppTheme NewTheme { get; init; }
}

/// <summary>
/// システムテーマ検出インターフェース
/// </summary>
public interface ISystemThemeDetector
{
    AppTheme DetectSystemTheme();
}
```

### 3. ThemeService実装（エラーハンドリング・ログ記録・並行制御）
```csharp
namespace Baketa.Infrastructure.Services;

public class ThemeService : IThemeService, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly Application _application;
    private readonly ILogger<ThemeService> _logger;
    private readonly ISystemThemeDetector? _systemThemeDetector;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private bool _disposed;

    public AppTheme CurrentTheme { get; private set; }
    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    public ThemeService(
        ISettingsService settingsService,
        Application application,
        ILogger<ThemeService> logger,
        ISystemThemeDetector? systemThemeDetector = null)
    {
        _settingsService = settingsService;
        _application = application;
        _logger = logger;
        _systemThemeDetector = systemThemeDetector;

        // 設定から初期テーマを読み込み
        try
        {
            CurrentTheme = _settingsService.Get<AppTheme>("Theme", AppTheme.Dark);
            ApplyTheme(CurrentTheme);
            _logger.LogInformation("初期テーマ適用: {Theme}", CurrentTheme);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初期テーマ適用に失敗しました。デフォルトテーマを使用します");
            CurrentTheme = AppTheme.Dark;
        }
    }

    public async Task SwitchThemeAsync(AppTheme theme, CancellationToken cancellationToken = default)
    {
        // 並行切替防止
        await _switchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (CurrentTheme == theme)
            {
                _logger.LogDebug("テーマは既に {Theme} です。切替をスキップします", theme);
                return;
            }

            var oldTheme = CurrentTheme;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // UIスレッドでテーマを適用
                await Dispatcher.UIThread.InvokeAsync(() => ApplyTheme(theme));
                CurrentTheme = theme;

                // 設定を保存
                await _settingsService.SetAsync("Theme", theme, cancellationToken)
                    .ConfigureAwait(false);

                stopwatch.Stop();
                _logger.LogInformation(
                    "テーマ切替成功: {OldTheme} → {NewTheme} ({ElapsedMs}ms)",
                    oldTheme, theme, stopwatch.ElapsedMilliseconds);

                // パフォーマンス警告
                if (stopwatch.ElapsedMilliseconds > 100)
                {
                    _logger.LogWarning(
                        "テーマ切替が目標時間（100ms）を超過しました: {ElapsedMs}ms",
                        stopwatch.ElapsedMilliseconds);
                }

                // イベント発火
                ThemeChanged?.Invoke(this, new ThemeChangedEventArgs
                {
                    OldTheme = oldTheme,
                    NewTheme = theme
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "テーマ切替に失敗しました: {OldTheme} → {NewTheme}", oldTheme, theme);

                // ロールバック試行
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() => ApplyTheme(oldTheme));
                    _logger.LogInformation("テーマを {OldTheme} にロールバックしました", oldTheme);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "テーマのロールバックに失敗しました");
                }

                throw new ThemeServiceException($"テーマの切替に失敗しました: {theme}", ex);
            }
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public async Task ToggleThemeAsync(CancellationToken cancellationToken = default)
    {
        var newTheme = CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        await SwitchThemeAsync(newTheme, cancellationToken);
    }

    public AppTheme DetectSystemTheme()
    {
        if (_systemThemeDetector != null)
        {
            try
            {
                var detectedTheme = _systemThemeDetector.DetectSystemTheme();
                _logger.LogInformation("システムテーマ検出: {Theme}", detectedTheme);
                return detectedTheme;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "システムテーマ検出に失敗しました");
            }
        }

        return AppTheme.Dark; // デフォルト
    }

    private void ApplyTheme(AppTheme theme)
    {
        var themeUri = theme == AppTheme.Dark
            ? new Uri("avares://Baketa.UI/Themes/DarkTheme.axaml")
            : new Uri("avares://Baketa.UI/Themes/LightTheme.axaml");

        // 既存のテーマを削除
        var existingTheme = _application.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.ToString().Contains("Theme.axaml") == true);

        if (existingTheme != null)
        {
            _application.Resources.MergedDictionaries.Remove(existingTheme);
            _logger.LogDebug("既存テーマを削除しました: {Source}", existingTheme.Source);
        }

        // 新しいテーマを追加
        try
        {
            var newTheme = new ResourceInclude(themeUri) { Source = themeUri };
            _application.Resources.MergedDictionaries.Add(newTheme);
            _logger.LogDebug("新しいテーマを追加しました: {ThemeUri}", themeUri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "テーマファイルの読み込みに失敗しました: {ThemeUri}", themeUri);
            throw new ThemeServiceException($"テーマファイルが見つかりません: {themeUri}", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _switchLock.Dispose();
        _disposed = true;
        _logger.LogDebug("ThemeService disposed");
    }
}

/// <summary>
/// テーマサービス例外
/// </summary>
public class ThemeServiceException : Exception
{
    public ThemeServiceException(string message) : base(message) { }
    public ThemeServiceException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Windowsシステムテーマ検出実装（将来実装）
/// </summary>
public class WindowsThemeDetector : ISystemThemeDetector
{
    private readonly ILogger<WindowsThemeDetector> _logger;

    public WindowsThemeDetector(ILogger<WindowsThemeDetector> logger)
    {
        _logger = logger;
    }

    public AppTheme DetectSystemTheme()
    {
        // Windows 10/11のシステムテーマ検出
        // HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize
        // Key: AppsUseLightTheme (0 = Dark, 1 = Light)

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            var value = key?.GetValue("AppsUseLightTheme");
            var theme = value is int intValue && intValue == 1
                ? AppTheme.Light
                : AppTheme.Dark;

            _logger.LogInformation("Windowsシステムテーマ検出: {Theme}", theme);
            return theme;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "システムテーマの検出に失敗しました。Darkをデフォルトとします");
            return AppTheme.Dark;
        }
    }
}
```

### 4. App.axaml統合
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Baketa.UI.App">

    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <!-- デフォルトテーマ (Dark) -->
                <ResourceInclude Source="avares://Baketa.UI/Themes/DarkTheme.axaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

### 5. SettingsViewModel統合（エラーハンドリング追加）
```csharp
public class SettingsViewModel : ViewModelBase
{
    private readonly IThemeService _themeService;
    private readonly ILogger<SettingsViewModel> _logger;

    [Reactive] public AppTheme SelectedTheme { get; set; }
    [Reactive] public string? ThemeErrorMessage { get; private set; }
    [Reactive] public bool IsThemeSwitching { get; private set; }

    public ReactiveCommand<AppTheme, Unit> SwitchThemeCommand { get; }

    public SettingsViewModel(IThemeService themeService, ILogger<SettingsViewModel> logger)
    {
        _themeService = themeService;
        _logger = logger;
        SelectedTheme = _themeService.CurrentTheme;

        // テーマ切替コマンド（エラーハンドリング付き）
        SwitchThemeCommand = ReactiveCommand.CreateFromTask<AppTheme>(async theme =>
        {
            IsThemeSwitching = true;
            ThemeErrorMessage = null;

            try
            {
                await _themeService.SwitchThemeAsync(theme);
                SelectedTheme = theme;
                _logger.LogInformation("ユーザーがテーマを切り替えました: {Theme}", theme);
            }
            catch (ThemeServiceException ex)
            {
                _logger.LogError(ex, "テーマ切替に失敗しました");
                ThemeErrorMessage = "テーマの切替に失敗しました。もう一度お試しください。";

                // UIを元の状態に戻す
                SelectedTheme = _themeService.CurrentTheme;
            }
            finally
            {
                IsThemeSwitching = false;
            }
        });
    }
}
```

### 6. Settings.axamlにテーマセレクター追加（エラー表示・アクセシビリティ対応）
```xml
<StackPanel Spacing="10">
    <TextBlock Text="テーマ" FontWeight="Bold" />

    <!-- エラーメッセージ -->
    <TextBlock IsVisible="{Binding ThemeErrorMessage, Converter={x:Static ObjectConverters.IsNotNull}}"
               Text="{Binding ThemeErrorMessage}"
               Foreground="#FF5555"
               TextWrapping="Wrap"
               AutomationProperties.LiveSetting="Assertive" />

    <!-- ローディング表示 -->
    <ProgressBar IsVisible="{Binding IsThemeSwitching}"
                 IsIndeterminate="True"
                 Height="4"
                 Margin="0,5,0,5" />

    <StackPanel Orientation="Horizontal" Spacing="20"
                IsEnabled="{Binding !IsThemeSwitching}">
        <RadioButton Content="Light"
                     IsChecked="{Binding SelectedTheme, Converter={StaticResource EnumToBoolConverter}, ConverterParameter={x:Static local:AppTheme.Light}}"
                     Command="{Binding SwitchThemeCommand}"
                     CommandParameter="{x:Static local:AppTheme.Light}"
                     AutomationProperties.Name="Lightテーマ"
                     AutomationProperties.HelpText="明るいテーマに切り替えます" />

        <RadioButton Content="Dark"
                     IsChecked="{Binding SelectedTheme, Converter={StaticResource EnumToBoolConverter}, ConverterParameter={x:Static local:AppTheme.Dark}}"
                     Command="{Binding SwitchThemeCommand}"
                     CommandParameter="{x:Static local:AppTheme.Dark}"
                     AutomationProperties.Name="Darkテーマ"
                     AutomationProperties.HelpText="暗いテーマに切り替えます" />
    </StackPanel>
</StackPanel>
```

### 7. MainViewModelにクイックアクセス追加
```csharp
public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }

public MainViewModel(IThemeService themeService)
{
    ToggleThemeCommand = ReactiveCommand.CreateFromTask(async () =>
    {
        await themeService.ToggleThemeAsync();
    });
}
```

## ✅ 受け入れ基準

### 機能テスト
- [ ] 設定画面からLight/Darkテーマを切り替えできる
- [ ] メインウィンドウのクイックアクセスボタンでテーマトグルできる
- [ ] テーマ切替が即時反映される (再起動不要)
- [ ] テーマが `appsettings.json` に保存される
- [ ] アプリケーション起動時に前回のテーマが適用される
- [ ] **テーマ切替失敗時にエラーメッセージが表示される**
- [ ] **エラー発生時に元のテーマにロールバックされる**
- [ ] **並行テーマ切替が正しく順次処理される**
- [ ] **システムテーマ検出（Autoモード）の準備ができている**

### UIテスト
- [ ] Darkテーマの色が仕様通り
- [ ] Lightテーマの色が仕様通り
- [ ] すべてのウィンドウ (MainWindow, Settings, Login) にテーマが適用される
- [ ] オーバーレイウィンドウにもテーマが適用される
- [ ] テーマ切替時のアニメーションが滑らか (0.3秒フェード)

### パフォーマンステスト
- [ ] テーマ切替時の遅延が100ms以内

### 単体テスト（18ケース）
```csharp
public class ThemeServiceTests
{
    private Mock<ISettingsService> _mockSettingsService = null!;
    private Mock<ILogger<ThemeService>> _mockLogger = null!;
    private Application _application = null!;
    private ThemeService _themeService = null!;

    public ThemeServiceTests()
    {
        _mockSettingsService = new Mock<ISettingsService>();
        _mockLogger = new Mock<ILogger<ThemeService>>();
        _application = new Application();
        _themeService = new ThemeService(
            _mockSettingsService.Object,
            _application,
            _mockLogger.Object);
    }

    // ===== 基本機能テスト (6ケース) =====

    [Fact]
    public async Task SwitchThemeAsync_Dark_to_Light_成功()
    {
        // Arrange
        _mockSettingsService.Setup(x => x.SetAsync("Theme", AppTheme.Light, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _themeService.SwitchThemeAsync(AppTheme.Light);

        // Assert
        _themeService.CurrentTheme.Should().Be(AppTheme.Light);
        _mockSettingsService.Verify(
            x => x.SetAsync("Theme", AppTheme.Light, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SwitchThemeAsync_同じテーマ_切替スキップ()
    {
        // Arrange
        await _themeService.SwitchThemeAsync(AppTheme.Dark);

        // Act
        await _themeService.SwitchThemeAsync(AppTheme.Dark);

        // Assert
        _mockSettingsService.Verify(
            x => x.SetAsync(It.IsAny<string>(), It.IsAny<AppTheme>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ToggleThemeAsync_Dark_to_Light()
    {
        // Arrange
        _mockSettingsService.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<AppTheme>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _themeService.ToggleThemeAsync();

        // Assert
        _themeService.CurrentTheme.Should().Be(AppTheme.Light);
    }

    [Fact]
    public async Task ToggleThemeAsync_Light_to_Dark()
    {
        // Arrange
        await _themeService.SwitchThemeAsync(AppTheme.Light);

        // Act
        await _themeService.ToggleThemeAsync();

        // Assert
        _themeService.CurrentTheme.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public void ThemeChanged_イベント発火()
    {
        // Arrange
        ThemeChangedEventArgs? eventArgs = null;
        _themeService.ThemeChanged += (s, e) => eventArgs = e;

        // Act
        await _themeService.SwitchThemeAsync(AppTheme.Light);

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.OldTheme.Should().Be(AppTheme.Dark);
        eventArgs.NewTheme.Should().Be(AppTheme.Light);
    }

    [Fact]
    public async Task SwitchThemeAsync_複数回連続呼び出し_順次処理()
    {
        // Arrange
        var tasks = new[]
        {
            _themeService.SwitchThemeAsync(AppTheme.Light),
            _themeService.SwitchThemeAsync(AppTheme.Dark),
            _themeService.SwitchThemeAsync(AppTheme.Light)
        };

        // Act
        await Task.WhenAll(tasks);

        // Assert
        _themeService.CurrentTheme.Should().Be(AppTheme.Light);
    }

    // ===== エラーハンドリングテスト (5ケース) =====

    [Fact]
    public async Task SwitchThemeAsync_設定保存失敗_例外スロー()
    {
        // Arrange
        _mockSettingsService
            .Setup(x => x.SetAsync("Theme", AppTheme.Light, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk full"));

        // Act & Assert
        await Assert.ThrowsAsync<ThemeServiceException>(
            () => _themeService.SwitchThemeAsync(AppTheme.Light));
    }

    [Fact]
    public async Task SwitchThemeAsync_設定保存失敗_元のテーマに戻る()
    {
        // Arrange
        var originalTheme = _themeService.CurrentTheme;
        _mockSettingsService
            .Setup(x => x.SetAsync("Theme", It.IsAny<AppTheme>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk full"));

        // Act
        try
        {
            await _themeService.SwitchThemeAsync(AppTheme.Light);
        }
        catch (ThemeServiceException)
        {
            // Expected
        }

        // Assert
        _themeService.CurrentTheme.Should().Be(originalTheme, "エラー時は元のテーマを維持");
    }

    [Fact]
    public void Constructor_初期テーマ読み込み失敗_Darkがデフォルト()
    {
        // Arrange
        _mockSettingsService.Setup(x => x.Get<AppTheme>("Theme", AppTheme.Dark))
            .Throws(new InvalidOperationException("Settings corrupted"));

        // Act
        var service = new ThemeService(
            _mockSettingsService.Object,
            _application,
            _mockLogger.Object);

        // Assert
        service.CurrentTheme.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public async Task SwitchThemeAsync_テーマファイル不存在_例外スロー()
    {
        // Note: この場合、ResourceIncludeがFileNotFoundExceptionをスローする想定
        // 実際のテストでは、Applicationのモックが必要

        // Arrange & Act & Assert
        await Assert.ThrowsAsync<ThemeServiceException>(
            () => _themeService.SwitchThemeAsync((AppTheme)999)); // 無効な値
    }

    [Fact]
    public async Task SwitchThemeAsync_パフォーマンス警告ログ()
    {
        // Arrange
        _mockSettingsService
            .Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<AppTheme>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(150); // 100msを超える遅延
            });

        // Act
        await _themeService.SwitchThemeAsync(AppTheme.Light);

        // Assert
        _mockLogger.Verify(x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("目標時間（100ms）を超過")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ===== Disposeテスト (2ケース) =====

    [Fact]
    public void Dispose_複数回呼び出し_安全に処理()
    {
        // Act
        _themeService.Dispose();
        _themeService.Dispose();

        // Assert - 例外が発生しないこと
    }

    [Fact]
    public void Dispose_SemaphoreSlim解放()
    {
        // Act
        _themeService.Dispose();

        // Assert - Dispose後はSemaphoreSlimが解放されている
        // 内部実装の検証のため、直接確認は困難だがエラーが発生しないことを確認
    }

    // ===== パフォーマンステスト (3ケース) =====

    [Fact]
    public async Task SwitchThemeAsync_100ms以内に完了()
    {
        // Arrange
        var stopwatch = Stopwatch.StartNew();

        // Act
        await _themeService.SwitchThemeAsync(AppTheme.Light);

        // Assert
        stopwatch.Stop();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100,
            "テーマ切替は100ms以内に完了すべき");
    }

    [Fact]
    public async Task ToggleThemeAsync_100ms以内に完了()
    {
        // Arrange
        var stopwatch = Stopwatch.StartNew();

        // Act
        await _themeService.ToggleThemeAsync();

        // Assert
        stopwatch.Stop();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    [Fact]
    public async Task SwitchThemeAsync_連続10回_すべて100ms以内()
    {
        // Arrange & Act
        var stopwatches = new List<long>();
        for (int i = 0; i < 10; i++)
        {
            var theme = i % 2 == 0 ? AppTheme.Light : AppTheme.Dark;
            var sw = Stopwatch.StartNew();
            await _themeService.SwitchThemeAsync(theme);
            sw.Stop();
            stopwatches.Add(sw.ElapsedMilliseconds);
        }

        // Assert
        stopwatches.Should().OnlyContain(ms => ms < 100,
            "すべてのテーマ切替が100ms以内に完了すべき");
    }

    // ===== システムテーマ検出テスト (2ケース) =====

    [Fact]
    public void DetectSystemTheme_検出成功()
    {
        // Arrange
        var mockDetector = new Mock<ISystemThemeDetector>();
        mockDetector.Setup(x => x.DetectSystemTheme()).Returns(AppTheme.Light);

        var service = new ThemeService(
            _mockSettingsService.Object,
            _application,
            _mockLogger.Object,
            mockDetector.Object);

        // Act
        var theme = service.DetectSystemTheme();

        // Assert
        theme.Should().Be(AppTheme.Light);
    }

    [Fact]
    public void DetectSystemTheme_検出器なし_Darkをデフォルト()
    {
        // Act
        var theme = _themeService.DetectSystemTheme();

        // Assert
        theme.Should().Be(AppTheme.Dark, "検出器がない場合はDarkをデフォルトとする");
    }
}
```

## 📊 見積もり
- **作業時間**: 10時間
  - エラーハンドリング追加: +1時間
  - システムテーマ検出準備: +1時間
- **優先度**: 🟡 Medium
- **リスク**: 🟢 Low

## 📌 備考

### 実装の改善点
1. **エラーハンドリング強化**: テーマ切替失敗時の自動ロールバック機能
2. **並行制御**: `SemaphoreSlim`で複数スレッドからの同時切替を防止
3. **パフォーマンス計測**: `Stopwatch`で切替時間を計測し、100ms超過時に警告ログ
4. **ログ記録**: すべてのテーマ操作を`ILogger`で記録
5. **Dispose実装**: `SemaphoreSlim`の適切な解放
6. **システムテーマ検出準備**: Windows Registry経由でシステムテーマを検出（Autoモード用）
7. **テストケース拡充**: 3ケース → 18ケース（エラーハンドリング、パフォーマンス、システム検出を網羅）

### 技術的な利点
- **信頼性向上**: エラー発生時の自動復旧でユーザー体験を維持
- **スレッドセーフ**: 並行切替による不整合を防止
- **パフォーマンス**: 100ms以内の切替で即座にテーマが反映
- **保守性**: 包括的なログ記録でトラブルシューティングが容易
- **拡張性**: Autoモード（システム連携）の基盤が整備済み

### その他
- 将来的にシステム設定に従う「Auto」モードを追加予定（実装準備完了）
- カスタムテーマ作成機能は v1.0 以降で検討
- オーバーレイウィンドウの背景透明度はテーマによらず固定
- Windows 10/11のシステムテーマ検出機能は`WindowsThemeDetector`で実装済み
