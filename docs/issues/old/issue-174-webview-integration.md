# Issue #174: WebView統合（広告表示）

## 📋 概要
Avalonia WebViewを統合し、メインウィンドウ下部に広告を表示する機能を実装します。

## 🎯 目的
- 無料プランユーザーへの広告表示
- Google AdSense連携の基盤構築
- 収益化機能の第一歩

## 📦 Epic
**Epic 4: 認証とマネタイゼーション** (#167 - #169, #174 - #175)

## 🔗 依存関係
- **Blocks**: #175 (プラン別広告制御)
- **Blocked by**: #169 (認証UI拡張)
- **Related**: #125 (広告表示システムの実装 - 既存Issue)

## 📝 要件

### 機能要件

#### 1. WebView統合
**使用ライブラリ**
- **第1候補**: `Avalonia.WebView` (クロスプラットフォーム対応)
- **第2候補**: `CefGlue.Avalonia` (Chromium Embedded Framework)

**表示位置**
- メインウィンドウ下部に固定
- 高さ: 100px (固定)
- 幅: ウィンドウ幅に追従

#### 2. 広告表示仕様
**Google AdSense統合**
```html
<!-- AdSense広告ユニット -->
<script async src="https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=ca-pub-XXXXXXXXXXXXXXXX"
     crossorigin="anonymous"></script>
<ins class="adsbygoogle"
     style="display:block"
     data-ad-client="ca-pub-XXXXXXXXXXXXXXXX"
     data-ad-slot="1234567890"
     data-ad-format="horizontal"
     data-full-width-responsive="true"></ins>
<script>
     (adsbygoogle = window.adsbygoogle || []).push({});
</script>
```

**バナー広告フォーマット**
- サイズ: 728x90 (Leaderboard) または 468x60 (Banner)
- レスポンシブ対応
- 自動リロード: 30秒ごと

#### 3. 広告表示条件
- **無料プラン**: 広告表示
- **有料プラン (Premium)**: 広告非表示
- **未ログイン**: 広告表示

### 非機能要件

1. **パフォーマンス**
   - WebView初期化時間: <1秒
   - メインウィンドウの動作に影響を与えない

2. **セキュリティ**
   - HTTPS通信のみ許可
   - スクリプト実行を AdSense ドメインのみに制限

3. **プライバシー**
   - ユーザーIDを広告SDKに送信しない
   - トラッキング設定を遵守

## 🏗️ 実装方針

### 1. NuGetパッケージ追加
```xml
<!-- Baketa.UI.csproj -->
<PackageReference Include="Avalonia.WebView" Version="11.2.0" />
```

### 2. IAdvertisementService Interface
```csharp
namespace Baketa.Core.Abstractions.Services;

public interface IAdvertisementService
{
    bool ShouldShowAd { get; }
    string AdHtmlContent { get; }

    event EventHandler<AdDisplayChangedEventArgs> AdDisplayChanged;

    Task LoadAdAsync(CancellationToken cancellationToken = default);
    Task HideAdAsync(CancellationToken cancellationToken = default);
}

public class AdDisplayChangedEventArgs : EventArgs
{
    public bool ShouldShowAd { get; init; }
    public required string Reason { get; init; }
}
```

### 3. AdvertisementService実装（エラーハンドリング・ログ記録）
```csharp
namespace Baketa.Application.Services;

public class AdvertisementService : IAdvertisementService, IDisposable
{
    private readonly IAuthenticationService _authService;
    private readonly IUserPlanService _userPlanService;
    private readonly ILogger<AdvertisementService> _logger;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _disposed;

    public bool ShouldShowAd { get; private set; }
    public string AdHtmlContent { get; private set; } = string.Empty;

    public event EventHandler<AdDisplayChangedEventArgs>? AdDisplayChanged;

    public AdvertisementService(
        IAuthenticationService authService,
        IUserPlanService userPlanService,
        ILogger<AdvertisementService> logger,
        IConfiguration configuration)
    {
        _authService = authService;
        _userPlanService = userPlanService;
        _logger = logger;
        _configuration = configuration;

        // 認証状態変更時に広告表示判定
        _authService.AuthStateChanged += OnAuthStateChanged;
        _userPlanService.PlanChanged += OnPlanChanged;

        UpdateAdDisplayState();
        _logger.LogInformation("AdvertisementService initialized. ShouldShowAd: {ShouldShowAd}", ShouldShowAd);
    }

    public async Task LoadAdAsync(CancellationToken cancellationToken = default)
    {
        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ShouldShowAd)
            {
                AdHtmlContent = string.Empty;
                _logger.LogDebug("広告表示不要のため、HTMLコンテンツをクリア");
                return;
            }

            try
            {
                // 設定から広告情報を取得
                var adSenseClientId = _configuration["Advertisement:AdSenseClientId"];
                var adSenseSlotId = _configuration["Advertisement:AdSenseSlotId"];

                if (string.IsNullOrEmpty(adSenseClientId))
                {
                    _logger.LogWarning("AdSense Client IDが設定されていません");
                    AdHtmlContent = string.Empty;
                    return;
                }

                AdHtmlContent = GenerateAdSenseHtml(adSenseClientId, adSenseSlotId);
                _logger.LogInformation("AdSense広告HTMLを生成しました");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "広告HTML生成中にエラーが発生しました");
                AdHtmlContent = string.Empty; // エラー時は空白
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task HideAdAsync(CancellationToken cancellationToken = default)
    {
        ShouldShowAd = false;
        AdHtmlContent = string.Empty;

        _logger.LogInformation("広告を非表示にしました");

        AdDisplayChanged?.Invoke(this, new AdDisplayChangedEventArgs
        {
            ShouldShowAd = false,
            Reason = "User request"
        });

        await Task.CompletedTask;
    }

    private void OnAuthStateChanged(object? sender, AuthStateChangedEventArgs e)
    {
        _logger.LogDebug("認証状態変更を検出: IsAuthenticated={IsAuthenticated}", e.IsAuthenticated);
        UpdateAdDisplayState();
    }

    private void OnPlanChanged(object? sender, PlanChangedEventArgs e)
    {
        _logger.LogInformation("プラン変更を検出: {OldPlan} → {NewPlan}", e.OldPlan, e.NewPlan);
        UpdateAdDisplayState();
    }

    private void UpdateAdDisplayState()
    {
        var isAuthenticated = _authService.IsAuthenticated;
        var isPremium = isAuthenticated && _userPlanService.CurrentPlan == UserPlan.Premium;

        var shouldShow = !isPremium; // 無料プランまたは未ログイン時に表示
        var reason = isPremium ? "Premium plan" : "Free plan or not logged in";

        if (ShouldShowAd != shouldShow)
        {
            var oldState = ShouldShowAd;
            ShouldShowAd = shouldShow;

            _logger.LogInformation(
                "広告表示状態変更: {OldState} → {NewState} (理由: {Reason})",
                oldState, shouldShow, reason);

            AdDisplayChanged?.Invoke(this, new AdDisplayChangedEventArgs
            {
                ShouldShowAd = shouldShow,
                Reason = reason
            });
        }
    }

    private string GenerateAdSenseHtml(string clientId, string? slotId)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta http-equiv=""Content-Security-Policy"" content=""default-src 'self'; script-src 'unsafe-inline' https://pagead2.googlesyndication.com; frame-src https://googleads.g.doubleclick.net;"">
    <style>
        body {{
            margin: 0;
            padding: 0;
            background-color: #2C2C2C;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100px;
            overflow: hidden;
        }}
    </style>
</head>
<body>
    <!-- AdSense広告ユニット -->
    <script async src=""https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client={clientId}""
         crossorigin=""anonymous""></script>
    <ins class=""adsbygoogle""
         style=""display:block""
         data-ad-client=""{clientId}""
         data-ad-slot=""{slotId ?? "1234567890"}""
         data-ad-format=""horizontal""
         data-full-width-responsive=""true""></ins>
    <script>
         (adsbygoogle = window.adsbygoogle || []).push({{}});
    </script>
</body>
</html>
";
    }

    public void Dispose()
    {
        if (_disposed) return;

        _authService.AuthStateChanged -= OnAuthStateChanged;
        _userPlanService.PlanChanged -= OnPlanChanged;
        _loadLock.Dispose();

        _disposed = true;
        _logger.LogDebug("AdvertisementService disposed");
    }
}
```

### 4. MainWindow.axaml統合
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:webview="clr-namespace:Avalonia.WebView;assembly=Avalonia.WebView"
        x:Class="Baketa.UI.Views.MainWindow"
        Title="Baketa"
        Width="300" Height="600">

    <Grid RowDefinitions="*,Auto">
        <!-- メインコンテンツ -->
        <StackPanel Grid.Row="0">
            <!-- 既存のUI要素 -->
        </StackPanel>

        <!-- 広告エリア (条件付き表示) -->
        <Border Grid.Row="1"
                IsVisible="{Binding ShouldShowAd}"
                Height="100"
                Background="#2C2C2C"
                BorderBrush="#404040"
                BorderThickness="0,1,0,0">

            <webview:WebView x:Name="AdWebView"
                             HtmlContent="{Binding AdHtmlContent}" />
        </Border>
    </Grid>
</Window>
```

### 5. MainViewModel統合
```csharp
public class MainViewModel : ViewModelBase
{
    private readonly IAdvertisementService _advertisementService;

    [Reactive] public bool ShouldShowAd { get; private set; }
    [Reactive] public string AdHtmlContent { get; private set; } = string.Empty;

    public MainViewModel(IAdvertisementService advertisementService)
    {
        _advertisementService = advertisementService;

        // 広告表示状態の初期化
        ShouldShowAd = _advertisementService.ShouldShowAd;
        AdHtmlContent = _advertisementService.AdHtmlContent;

        // イベント購読
        _advertisementService.AdDisplayChanged += OnAdDisplayChanged;

        // 広告読み込み
        _ = _advertisementService.LoadAdAsync();
    }

    private void OnAdDisplayChanged(object? sender, AdDisplayChangedEventArgs e)
    {
        ShouldShowAd = e.ShouldShowAd;

        if (ShouldShowAd)
        {
            _ = _advertisementService.LoadAdAsync();
            AdHtmlContent = _advertisementService.AdHtmlContent;
        }
        else
        {
            AdHtmlContent = string.Empty;
        }
    }
}
```

### 6. appsettings.json設定
```json
{
  "Advertisement": {
    "AdSenseClientId": "ca-pub-XXXXXXXXXXXXXXXX",
    "AdSenseSlotId": "1234567890",
    "AutoReloadInterval": 30
  }
}
```

## ✅ 受け入れ基準

### 機能テスト
- [ ] 無料プランユーザーに広告が表示される
- [ ] 有料プラン (Premium) ユーザーに広告が表示されない
- [ ] 未ログインユーザーに広告が表示される
- [ ] AdSense広告が正常に読み込まれる
- [ ] AdSense広告が表示されない場合、広告エリアが空白になる

### UIテスト
- [ ] 広告エリアの高さが100px固定
- [ ] 広告エリアの幅がウィンドウ幅に追従
- [ ] 広告エリアの上部にボーダーが表示される
- [ ] 広告が30秒ごとに自動リロードされる

### パフォーマンステスト
- [ ] WebView初期化時間が1秒以内
- [ ] 広告読み込みがメインウィンドウの動作に影響しない

### セキュリティテスト
- [ ] HTTPS通信のみ許可される
- [ ] AdSenseドメイン以外のスクリプトが実行されない

### 単体テスト
```csharp
public class AdvertisementServiceTests
{
    // 1. 広告表示判定テスト
    [Fact]
    public void ShouldShowAd_無料プラン_true()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(true);
        _mockUserPlanService.Setup(x => x.CurrentPlan).Returns(UserPlan.Free);

        // Act
        var service = new AdvertisementService(_mockAuthService.Object, _mockUserPlanService.Object, _mockLogger.Object, _mockConfiguration.Object);

        // Assert
        service.ShouldShowAd.Should().BeTrue();
    }

    [Fact]
    public void ShouldShowAd_Premiumプラン_false()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(true);
        _mockUserPlanService.Setup(x => x.CurrentPlan).Returns(UserPlan.Premium);

        // Act
        var service = new AdvertisementService(_mockAuthService.Object, _mockUserPlanService.Object, _mockLogger.Object, _mockConfiguration.Object);

        // Assert
        service.ShouldShowAd.Should().BeFalse();
    }

    [Fact]
    public void ShouldShowAd_未ログイン_true()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(false);

        // Act
        var service = new AdvertisementService(_mockAuthService.Object, _mockUserPlanService.Object, _mockLogger.Object, _mockConfiguration.Object);

        // Assert
        service.ShouldShowAd.Should().BeTrue();
    }

    // 2. 広告読み込みテスト
    [Fact]
    public async Task LoadAdAsync_AdSenseHTMLを生成()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(false);
        _mockConfiguration.Setup(x => x["Advertisement:AdSenseClientId"]).Returns("ca-pub-123456");
        _mockConfiguration.Setup(x => x["Advertisement:AdSenseSlotId"]).Returns("9876543210");

        // Act
        await _service.LoadAdAsync();

        // Assert
        _service.AdHtmlContent.Should().Contain("adsbygoogle");
        _service.AdHtmlContent.Should().Contain("ca-pub-123456");
        _service.AdHtmlContent.Should().Contain("9876543210");
    }

    [Fact]
    public async Task LoadAdAsync_ClientID未設定_空白()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(false);
        _mockConfiguration.Setup(x => x["Advertisement:AdSenseClientId"]).Returns((string?)null);

        // Act
        await _service.LoadAdAsync();

        // Assert
        _service.AdHtmlContent.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAdAsync_Premium_空白()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(true);
        _mockUserPlanService.Setup(x => x.CurrentPlan).Returns(UserPlan.Premium);

        // Act
        await _service.LoadAdAsync();

        // Assert
        _service.AdHtmlContent.Should().BeEmpty();
    }

    // 3. 広告非表示テスト
    [Fact]
    public async Task HideAdAsync_広告非表示()
    {
        // Arrange
        var eventFired = false;
        _service.AdDisplayChanged += (s, e) => eventFired = true;

        // Act
        await _service.HideAdAsync();

        // Assert
        _service.ShouldShowAd.Should().BeFalse();
        _service.AdHtmlContent.Should().BeEmpty();
        eventFired.Should().BeTrue();
    }

    // 4. イベントテスト
    [Fact]
    public void AdDisplayChanged_認証状態変更()
    {
        // Arrange
        var eventFired = false;
        _service.AdDisplayChanged += (s, e) => eventFired = true;

        // Act
        _mockAuthService.Raise(x => x.AuthStateChanged += null, new AuthStateChangedEventArgs { IsAuthenticated = true });

        // Assert
        eventFired.Should().BeTrue();
    }

    [Fact]
    public void AdDisplayChanged_プラン変更()
    {
        // Arrange
        var eventFired = false;
        _service.AdDisplayChanged += (s, e) => eventFired = true;

        // Act
        _mockUserPlanService.Raise(x => x.PlanChanged += null, new PlanChangedEventArgs { OldPlan = UserPlan.Free, NewPlan = UserPlan.Premium });

        // Assert
        eventFired.Should().BeTrue();
    }

    // 5. 同時実行制御テスト
    [Fact]
    public async Task LoadAdAsync_同時実行_排他制御()
    {
        // Arrange
        var task1 = _service.LoadAdAsync();
        var task2 = _service.LoadAdAsync();

        // Act
        await Task.WhenAll(task1, task2);

        // Assert
        // 排他制御により、2回のLoadAdAsyncが順次実行されることを確認
        _service.AdHtmlContent.Should().NotBeEmpty();
    }

    // 6. Disposeテスト
    [Fact]
    public void Dispose_リソース解放()
    {
        // Act
        _service.Dispose();

        // Assert
        // イベントハンドラが解除され、SemaphoreSlimが解放されることを確認
        // (実際のテストではリフレクションまたはモックで検証)
    }
}
```

## 📊 見積もり
- **作業時間**: 12時間
  - エラーハンドリング追加: 基本実装に含む
  - セキュリティ強化（CSP）: 基本実装に含む
- **優先度**: 🟠 High
- **リスク**: 🟡 Medium (エラーハンドリングでリスク軽減)

## 📌 備考
- AdSense審査通過までは広告エリアは空白表示
- WebViewのセキュリティ設定を厳密に管理（CSP実装済み）
- 将来的に他の広告ネットワーク (Microsoft Advertising等) も検討
- 広告表示に関するプライバシーポリシーを別途作成
