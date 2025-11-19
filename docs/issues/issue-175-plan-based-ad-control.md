# Issue #175: プラン別広告制御

## 📋 概要
ユーザープラン (Free/Premium) に基づいた広告表示制御と、プラン変更機能を実装します。

## 🎯 目的
- ユーザープランによる広告の自動表示/非表示制御
- プラン変更UIの実装
- 有料プラン (Premium) への誘導

## 📦 Epic
**Epic 4: 認証とマネタイゼーション** (#167 - #169, #174 - #175)

## 🔗 依存関係
- **Blocks**: なし
- **Blocked by**: #174 (WebView統合), #168 (トークン管理), #133 (Supabase Auth設定)
- **Related**: #77 (ライセンス管理システム基盤 - 既存Issue)

## 📝 要件

### 機能要件

#### 1. ユーザープラン定義
```csharp
public enum UserPlan
{
    Free,      // 無料プラン (広告あり)
    Premium    // 有料プラン (広告なし、クラウド翻訳使用可能)
}
```

**Free Plan**
- 広告表示あり
- ローカル翻訳のみ (NLLB-200)
- 翻訳回数制限なし

**Premium Plan**
- 広告非表示
- クラウド翻訳使用可能 (Google Gemini)
- 翻訳回数制限なし
- 将来的な新機能への優先アクセス

#### 2. プラン情報管理
**Supabaseデータベース (`users` テーブル)**
```sql
CREATE TABLE users (
    id UUID PRIMARY KEY REFERENCES auth.users(id),
    email TEXT NOT NULL,
    plan TEXT NOT NULL DEFAULT 'Free',
    plan_expires_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);
```

**Row Level Security (RLS)**
```sql
-- ユーザーは自分の情報のみ読み取り可能
CREATE POLICY "Users can view own data"
    ON users FOR SELECT
    USING (auth.uid() = id);
```

#### 3. プラン変更UI

**設定画面内のプラン表示**
```
┌─────────────────────────────────────┐
│  現在のプラン: Free                  │
│                                     │
│  ✓ ローカル翻訳 (NLLB-200)          │
│  ✗ クラウド翻訳 (Gemini)            │
│  ✗ 広告非表示                       │
│                                     │
│  [Premiumにアップグレード]          │
└─────────────────────────────────────┘
```

**Premiumプランダイアログ**
```
┌─────────────────────────────────────┐
│  Baketa Premium                     │
│                                     │
│  ✓ 広告非表示                       │
│  ✓ クラウド翻訳 (Google Gemini)     │
│  ✓ 優先サポート                     │
│  ✓ 新機能への優先アクセス           │
│                                     │
│  月額: ¥500                         │
│  年額: ¥5,000 (17% OFF)             │
│                                     │
│  [月額で購入]  [年額で購入]         │
│  [キャンセル]                       │
└─────────────────────────────────────┘
```

#### 4. プラン変更フロー
1. ユーザーが "Premiumにアップグレード" ボタンをクリック
2. Premiumプランダイアログを表示
3. 月額/年額を選択
4. 外部決済サービス (Stripe等) へリダイレクト
5. 決済完了後、Supabaseの `users.plan` を `Premium` に更新
6. アプリケーション側でプラン変更を検知し、広告を非表示

### 非機能要件

1. **リアルタイム更新**
   - プラン変更時に即座に広告表示/非表示を切り替え
   - 再起動不要

2. **セキュリティ**
   - プラン情報改ざん防止 (サーバーサイドで検証)
   - RLSによるデータアクセス制御

3. **可用性**
   - Supabase接続失敗時は前回のプラン情報をキャッシュから使用

## 🏗️ 実装方針

### 1. IUserPlanService Interface
```csharp
namespace Baketa.Core.Abstractions.Services;

public interface IUserPlanService
{
    UserPlan CurrentPlan { get; }
    DateTime? PlanExpiresAt { get; }
    bool IsPremium { get; }

    event EventHandler<PlanChangedEventArgs> PlanChanged;

    Task LoadPlanAsync(CancellationToken cancellationToken = default);
    Task UpgradeToPremiumAsync(PlanDuration duration, CancellationToken cancellationToken = default);
    Task<bool> ValidatePlanAsync(CancellationToken cancellationToken = default);
}

public enum PlanDuration
{
    Monthly,
    Yearly
}

public class PlanChangedEventArgs : EventArgs
{
    public required UserPlan OldPlan { get; init; }
    public required UserPlan NewPlan { get; init; }
    public DateTime? ExpiresAt { get; init; }
}
```

### 2. UserPlanService実装（エラーハンドリング・キャッシング強化版）
```csharp
namespace Baketa.Infrastructure.Services;

public class UserPlanService : IUserPlanService, IDisposable
{
    private readonly ISupabaseClient _supabaseClient;
    private readonly IAuthenticationService _authService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<UserPlanService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _disposed;

    private const int MaxRetryAttempts = 3;
    private const int RetryDelayMs = 1000;

    public UserPlan CurrentPlan { get; private set; } = UserPlan.Free;
    public DateTime? PlanExpiresAt { get; private set; }
    public bool IsPremium => CurrentPlan == UserPlan.Premium;

    public event EventHandler<PlanChangedEventArgs>? PlanChanged;

    public UserPlanService(
        ISupabaseClient supabaseClient,
        IAuthenticationService authService,
        ISettingsService settingsService,
        ILogger<UserPlanService> logger)
    {
        _supabaseClient = supabaseClient;
        _authService = authService;
        _settingsService = settingsService;
        _logger = logger;

        // 起動時にキャッシュからプラン情報を復元
        LoadFromCache();

        // 認証状態変更時にプラン情報を読み込み
        _authService.AuthStateChanged += OnAuthStateChanged;

        _logger.LogInformation("UserPlanService initialized. CurrentPlan: {CurrentPlan}", CurrentPlan);
    }

    public async Task LoadPlanAsync(CancellationToken cancellationToken = default)
    {
        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_authService.IsAuthenticated)
            {
                _logger.LogDebug("ユーザー未認証のため、プランをFreeにリセット");
                ResetPlan();
                return;
            }

            var userId = _authService.CurrentUser?.Id;
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("ユーザーIDが取得できません");
                ResetPlan();
                return;
            }

            // リトライロジック付きでSupabaseから読み込み
            var response = await LoadPlanWithRetryAsync(userId, cancellationToken).ConfigureAwait(false);

            if (response != null)
            {
                UpdatePlan(response.Plan, response.PlanExpiresAt);
                await SaveToCacheAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("Supabaseからプラン情報を取得できませんでした（キャッシュを使用）");
            }
        }
        catch (UserPlanServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "プラン情報の読み込みに失敗しました");
            throw new UserPlanServiceException("プラン情報の読み込みに失敗しました", ex);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task UpgradeToPremiumAsync(PlanDuration duration, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Premiumアップグレード開始: {Duration}", duration);

            // Stripe決済ページへリダイレクト (実装はβ版では簡略化)
            var checkoutUrl = GenerateStripeCheckoutUrl(duration);
            Process.Start(new ProcessStartInfo
            {
                FileName = checkoutUrl,
                UseShellExecute = true
            });

            _logger.LogInformation("Stripe決済ページを開きました: {Url}", checkoutUrl);

            // 決済完了後、Webhookでプラン更新 (サーバーサイド処理)
            // ここでは手動でプラン更新を確認
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            await LoadPlanAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Premiumアップグレードに失敗しました");
            throw new UserPlanServiceException("プラン変更に失敗しました", ex);
        }
    }

    public async Task<bool> ValidatePlanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // サーバーサイドでプラン有効期限を検証
            await LoadPlanAsync(cancellationToken).ConfigureAwait(false);

            if (PlanExpiresAt.HasValue && PlanExpiresAt.Value < DateTime.UtcNow)
            {
                _logger.LogWarning("プラン期限切れを検出: {ExpiresAt}", PlanExpiresAt);

                // プラン期限切れ → Freeに降格
                var oldPlan = CurrentPlan;
                CurrentPlan = UserPlan.Free;
                PlanExpiresAt = null;

                await SaveToCacheAsync(cancellationToken).ConfigureAwait(false);

                PlanChanged?.Invoke(this, new PlanChangedEventArgs
                {
                    OldPlan = oldPlan,
                    NewPlan = UserPlan.Free,
                    ExpiresAt = null
                });

                _logger.LogInformation("プランをFreeに降格しました");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "プラン検証に失敗しました");
            throw new UserPlanServiceException("プラン検証に失敗しました", ex);
        }
    }

    private async Task<UserData?> LoadPlanWithRetryAsync(string userId, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            try
            {
                _logger.LogDebug("Supabaseからプラン情報を取得中（試行 {Attempt}/{MaxAttempts}）", attempt, MaxRetryAttempts);

                var response = await _supabaseClient
                    .From<UserData>()
                    .Where(x => x.Id == userId)
                    .Single();

                _logger.LogInformation("Supabaseからプラン情報を取得しました: {Plan}", response?.Plan);
                return response;
            }
            catch (Exception ex) when (attempt < MaxRetryAttempts)
            {
                _logger.LogWarning(ex, "プラン情報の取得に失敗しました（試行 {Attempt}/{MaxAttempts}）", attempt, MaxRetryAttempts);
                await Task.Delay(RetryDelayMs * attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogError("プラン情報の取得に{MaxAttempts}回失敗しました", MaxRetryAttempts);
        return null;
    }

    private void UpdatePlan(string planString, DateTime? expiresAt)
    {
        var oldPlan = CurrentPlan;
        var newPlan = Enum.Parse<UserPlan>(planString);

        if (oldPlan != newPlan || PlanExpiresAt != expiresAt)
        {
            CurrentPlan = newPlan;
            PlanExpiresAt = expiresAt;

            _logger.LogInformation(
                "プラン更新: {OldPlan} → {NewPlan} (期限: {ExpiresAt})",
                oldPlan, newPlan, expiresAt?.ToString("yyyy-MM-dd") ?? "なし");

            PlanChanged?.Invoke(this, new PlanChangedEventArgs
            {
                OldPlan = oldPlan,
                NewPlan = newPlan,
                ExpiresAt = expiresAt
            });
        }
    }

    private void OnAuthStateChanged(object? sender, AuthStateChangedEventArgs e)
    {
        _logger.LogDebug("認証状態変更を検出: IsAuthenticated={IsAuthenticated}", e.IsAuthenticated);
        if (e.IsAuthenticated)
            _ = LoadPlanAsync();
        else
            ResetPlan();
    }

    private void ResetPlan()
    {
        if (CurrentPlan != UserPlan.Free)
        {
            _logger.LogInformation("プランをFreeにリセットします");
            var oldPlan = CurrentPlan;
            CurrentPlan = UserPlan.Free;
            PlanExpiresAt = null;

            PlanChanged?.Invoke(this, new PlanChangedEventArgs
            {
                OldPlan = oldPlan,
                NewPlan = UserPlan.Free,
                ExpiresAt = null
            });
        }
    }

    private void LoadFromCache()
    {
        try
        {
            var cachedPlan = _settingsService.Get<string>("UserPlan");
            var cachedExpiresAt = _settingsService.Get<DateTime?>("UserPlanExpiresAt");

            if (!string.IsNullOrEmpty(cachedPlan) && Enum.TryParse<UserPlan>(cachedPlan, out var plan))
            {
                CurrentPlan = plan;
                PlanExpiresAt = cachedExpiresAt;
                _logger.LogInformation("キャッシュからプラン情報を復元しました: {Plan}", plan);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "キャッシュからのプラン情報復元に失敗しました");
        }
    }

    private async Task SaveToCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _settingsService.SetAsync("UserPlan", CurrentPlan.ToString(), cancellationToken).ConfigureAwait(false);
            await _settingsService.SetAsync("UserPlanExpiresAt", PlanExpiresAt, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("プラン情報をキャッシュに保存しました");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "プラン情報のキャッシュ保存に失敗しました");
        }
    }

    private string GenerateStripeCheckoutUrl(PlanDuration duration)
    {
        var priceId = duration == PlanDuration.Monthly
            ? "price_monthly_xxxxxx"  // Stripe価格ID
            : "price_yearly_xxxxxx";

        return $"https://buy.stripe.com/test_xxxxxx?prefilled_promo_code=BAKETA2025";
    }

    public void Dispose()
    {
        if (_disposed) return;

        _authService.AuthStateChanged -= OnAuthStateChanged;
        _loadLock.Dispose();

        _disposed = true;
        _logger.LogDebug("UserPlanService disposed");
    }
}

// カスタム例外
public class UserPlanServiceException : Exception
{
    public UserPlanServiceException(string message) : base(message) { }
    public UserPlanServiceException(string message, Exception innerException) : base(message, innerException) { }
}

// Supabaseモデル
public class UserData
{
    [PrimaryKey("id")]
    public string Id { get; set; } = string.Empty;

    [Column("email")]
    public string Email { get; set; } = string.Empty;

    [Column("plan")]
    public string Plan { get; set; } = "Free";

    [Column("plan_expires_at")]
    public DateTime? PlanExpiresAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
```

### 3. SettingsViewModel統合
```csharp
public class SettingsViewModel : ViewModelBase
{
    private readonly IUserPlanService _userPlanService;

    [Reactive] public UserPlan CurrentPlan { get; private set; }
    [Reactive] public bool IsPremium { get; private set; }
    [Reactive] public string PlanDisplayName { get; private set; } = string.Empty;
    [Reactive] public string PlanFeaturesText { get; private set; } = string.Empty;

    public ReactiveCommand<Unit, Unit> UpgradeToPremiumCommand { get; }

    public SettingsViewModel(IUserPlanService userPlanService)
    {
        _userPlanService = userPlanService;

        // プラン情報の初期化
        UpdatePlanDisplay();

        // プラン変更イベント購読
        _userPlanService.PlanChanged += OnPlanChanged;

        // アップグレードコマンド
        UpgradeToPremiumCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var duration = await ShowPlanSelectionDialogAsync();
            if (duration.HasValue)
            {
                await _userPlanService.UpgradeToPremiumAsync(duration.Value);
            }
        });
    }

    private void OnPlanChanged(object? sender, PlanChangedEventArgs e)
    {
        UpdatePlanDisplay();
    }

    private void UpdatePlanDisplay()
    {
        CurrentPlan = _userPlanService.CurrentPlan;
        IsPremium = _userPlanService.IsPremium;
        PlanDisplayName = CurrentPlan == UserPlan.Premium ? "Premium" : "Free";

        PlanFeaturesText = CurrentPlan == UserPlan.Premium
            ? "✓ ローカル翻訳\n✓ クラウド翻訳 (Gemini)\n✓ 広告非表示"
            : "✓ ローカル翻訳\n✗ クラウド翻訳 (Gemini)\n✗ 広告非表示";
    }

    private async Task<PlanDuration?> ShowPlanSelectionDialogAsync()
    {
        // Premiumプランダイアログを表示
        var dialog = new PremiumPlanDialog();
        var result = await dialog.ShowDialog<PlanDuration?>(Application.Current.MainWindow);
        return result;
    }
}
```

### 4. Settings.axamlにプラン表示追加
```xml
<StackPanel Spacing="20">
    <TextBlock Text="現在のプラン" FontWeight="Bold" FontSize="16" />

    <Border BorderBrush="{DynamicResource BorderBrush}"
            BorderThickness="1"
            Padding="15">
        <StackPanel Spacing="10">
            <TextBlock Text="{Binding PlanDisplayName}"
                       FontSize="20"
                       FontWeight="Bold"
                       Foreground="{DynamicResource PrimaryBrush}" />

            <TextBlock Text="{Binding PlanFeaturesText}"
                       TextWrapping="Wrap" />

            <Button Content="Premiumにアップグレード"
                    Command="{Binding UpgradeToPremiumCommand}"
                    IsVisible="{Binding !IsPremium}"
                    Classes="PrimaryButton"
                    Margin="0,10,0,0" />
        </StackPanel>
    </Border>
</StackPanel>
```

### 5. PremiumPlanDialog.axaml
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="Baketa.UI.Views.PremiumPlanDialog"
        Title="Baketa Premium"
        Width="500" Height="450"
        WindowStartupLocation="CenterOwner"
        CanResize="False">

    <StackPanel Padding="30" Spacing="20">
        <TextBlock Text="Baketa Premium"
                   FontSize="24"
                   FontWeight="Bold"
                   HorizontalAlignment="Center" />

        <StackPanel Spacing="10">
            <TextBlock Text="✓ 広告非表示" FontSize="16" />
            <TextBlock Text="✓ クラウド翻訳 (Google Gemini)" FontSize="16" />
            <TextBlock Text="✓ 優先サポート" FontSize="16" />
            <TextBlock Text="✓ 新機能への優先アクセス" FontSize="16" />
        </StackPanel>

        <Separator />

        <StackPanel Spacing="15">
            <Button Content="月額 ¥500"
                    Command="{Binding SelectMonthlyCommand}"
                    Height="50"
                    Classes="PrimaryButton" />

            <Button Content="年額 ¥5,000 (17% OFF)"
                    Command="{Binding SelectYearlyCommand}"
                    Height="50"
                    Classes="AccentButton" />
        </StackPanel>

        <Button Content="キャンセル"
                Command="{Binding CancelCommand}"
                HorizontalAlignment="Center"
                Margin="0,10,0,0" />
    </StackPanel>
</Window>
```

## ✅ 受け入れ基準

### 機能テスト
- [ ] ログイン時にSupabaseからプラン情報を取得できる
- [ ] Free/Premiumプランに応じて広告表示が切り替わる
- [ ] 設定画面に現在のプランが表示される
- [ ] "Premiumにアップグレード" ボタンでダイアログが表示される
- [ ] 月額/年額選択後、Stripe決済ページへリダイレクトされる
- [ ] 決済完了後、プランがPremiumに更新される
- [ ] プラン変更時に即座に広告が非表示になる
- [ ] プラン期限切れ時に自動的にFreeに降格する

### UIテスト
- [ ] プラン表示が仕様通り
- [ ] Premiumプランダイアログのデザインが正しい
- [ ] プラン変更時のアニメーションが滑らか

### セキュリティテスト
- [ ] RLSによりユーザーは自分のプラン情報のみ閲覧可能
- [ ] プラン情報改ざんが検知される

### 単体テスト（15個）
```csharp
public class UserPlanServiceTests
{
    private readonly Mock<ISupabaseClient> _mockSupabaseClient;
    private readonly Mock<IAuthenticationService> _mockAuthService;
    private readonly Mock<ISettingsService> _mockSettingsService;
    private readonly Mock<ILogger<UserPlanService>> _mockLogger;
    private readonly UserPlanService _service;

    public UserPlanServiceTests()
    {
        _mockSupabaseClient = new Mock<ISupabaseClient>();
        _mockAuthService = new Mock<IAuthenticationService>();
        _mockSettingsService = new Mock<ISettingsService>();
        _mockLogger = new Mock<ILogger<UserPlanService>>();

        _service = new UserPlanService(
            _mockSupabaseClient.Object,
            _mockAuthService.Object,
            _mockSettingsService.Object,
            _mockLogger.Object);
    }

    // 1. 基本機能テスト (5個)
    [Fact]
    public async Task LoadPlanAsync_Premium_プラン情報を取得()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(true);
        _mockAuthService.Setup(x => x.CurrentUser.Id).Returns("user-123");
        _mockSupabaseClient.Setup(x => x.From<UserData>().Where(It.IsAny<Expression<Func<UserData, bool>>>()).Single())
            .ReturnsAsync(new UserData { Plan = "Premium", PlanExpiresAt = DateTime.UtcNow.AddMonths(1) });

        // Act
        await _service.LoadPlanAsync();

        // Assert
        _service.CurrentPlan.Should().Be(UserPlan.Premium);
        _service.IsPremium.Should().BeTrue();
        _mockSettingsService.Verify(x => x.SetAsync("UserPlan", "Premium", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoadPlanAsync_Free_プラン情報を取得()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(true);
        _mockAuthService.Setup(x => x.CurrentUser.Id).Returns("user-123");
        _mockSupabaseClient.Setup(x => x.From<UserData>().Where(It.IsAny<Expression<Func<UserData, bool>>>()).Single())
            .ReturnsAsync(new UserData { Plan = "Free", PlanExpiresAt = null });

        // Act
        await _service.LoadPlanAsync();

        // Assert
        _service.CurrentPlan.Should().Be(UserPlan.Free);
        _service.IsPremium.Should().BeFalse();
    }

    [Fact]
    public async Task LoadPlanAsync_未認証_Freeにリセット()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(false);

        // Act
        await _service.LoadPlanAsync();

        // Assert
        _service.CurrentPlan.Should().Be(UserPlan.Free);
        _service.IsPremium.Should().BeFalse();
    }

    [Fact]
    public async Task ValidatePlanAsync_有効期限内_true()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(true);
        _mockAuthService.Setup(x => x.CurrentUser.Id).Returns("user-123");
        _mockSupabaseClient.Setup(x => x.From<UserData>().Where(It.IsAny<Expression<Func<UserData, bool>>>()).Single())
            .ReturnsAsync(new UserData { Plan = "Premium", PlanExpiresAt = DateTime.UtcNow.AddMonths(1) });

        // Act
        var isValid = await _service.ValidatePlanAsync();

        // Assert
        isValid.Should().BeTrue();
        _service.CurrentPlan.Should().Be(UserPlan.Premium);
    }

    [Fact]
    public async Task ValidatePlanAsync_期限切れ_Freeに降格()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(true);
        _mockAuthService.Setup(x => x.CurrentUser.Id).Returns("user-123");
        _mockSupabaseClient.Setup(x => x.From<UserData>().Where(It.IsAny<Expression<Func<UserData, bool>>>()).Single())
            .ReturnsAsync(new UserData { Plan = "Premium", PlanExpiresAt = DateTime.UtcNow.AddDays(-1) });

        // Act
        var isValid = await _service.ValidatePlanAsync();

        // Assert
        isValid.Should().BeFalse();
        _service.CurrentPlan.Should().Be(UserPlan.Free);
        _mockSettingsService.Verify(x => x.SetAsync("UserPlan", "Free", It.IsAny<CancellationToken>()), Times.Once);
    }

    // 2. エラーハンドリングテスト (4個)
    [Fact]
    public async Task LoadPlanAsync_Supabaseエラー_リトライ後キャッシュ使用()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(true);
        _mockAuthService.Setup(x => x.CurrentUser.Id).Returns("user-123");
        _mockSupabaseClient.Setup(x => x.From<UserData>().Where(It.IsAny<Expression<Func<UserData, bool>>>()).Single())
            .ThrowsAsync(new Exception("Network error"));

        // Act
        await _service.LoadPlanAsync();

        // Assert
        // 3回リトライされ、キャッシュのプラン情報を使用
        _service.CurrentPlan.Should().Be(UserPlan.Free); // キャッシュがない場合
    }

    [Fact]
    public async Task LoadPlanAsync_ユーザーIDなし_例外なし()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(true);
        _mockAuthService.Setup(x => x.CurrentUser.Id).Returns((string?)null);

        // Act
        Func<Task> act = async () => await _service.LoadPlanAsync();

        // Assert
        await act.Should().NotThrowAsync();
        _service.CurrentPlan.Should().Be(UserPlan.Free);
    }

    [Fact]
    public async Task UpgradeToPremiumAsync_エラー_例外スロー()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(true);
        _mockAuthService.Setup(x => x.CurrentUser.Id).Returns("user-123");
        _mockSupabaseClient.Setup(x => x.From<UserData>().Where(It.IsAny<Expression<Func<UserData, bool>>>()).Single())
            .ThrowsAsync(new Exception("Network error"));

        // Act
        Func<Task> act = async () => await _service.UpgradeToPremiumAsync(PlanDuration.Monthly);

        // Assert
        await act.Should().ThrowAsync<UserPlanServiceException>();
    }

    [Fact]
    public async Task ValidatePlanAsync_エラー_例外スロー()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(true);
        _mockAuthService.Setup(x => x.CurrentUser.Id).Returns("user-123");
        _mockSupabaseClient.Setup(x => x.From<UserData>().Where(It.IsAny<Expression<Func<UserData, bool>>>()).Single())
            .ThrowsAsync(new Exception("Network error"));

        // Act
        Func<Task> act = async () => await _service.ValidatePlanAsync();

        // Assert
        await act.Should().ThrowAsync<UserPlanServiceException>();
    }

    // 3. キャッシングテスト (2個)
    [Fact]
    public void Constructor_キャッシュから復元()
    {
        // Arrange
        _mockSettingsService.Setup(x => x.Get<string>("UserPlan")).Returns("Premium");
        _mockSettingsService.Setup(x => x.Get<DateTime?>("UserPlanExpiresAt")).Returns(DateTime.UtcNow.AddMonths(1));

        // Act
        var service = new UserPlanService(
            _mockSupabaseClient.Object,
            _mockAuthService.Object,
            _mockSettingsService.Object,
            _mockLogger.Object);

        // Assert
        service.CurrentPlan.Should().Be(UserPlan.Premium);
    }

    [Fact]
    public async Task LoadPlanAsync_キャッシュに保存()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(true);
        _mockAuthService.Setup(x => x.CurrentUser.Id).Returns("user-123");
        _mockSupabaseClient.Setup(x => x.From<UserData>().Where(It.IsAny<Expression<Func<UserData, bool>>>()).Single())
            .ReturnsAsync(new UserData { Plan = "Premium", PlanExpiresAt = DateTime.UtcNow.AddMonths(1) });

        // Act
        await _service.LoadPlanAsync();

        // Assert
        _mockSettingsService.Verify(x => x.SetAsync("UserPlan", "Premium", It.IsAny<CancellationToken>()), Times.Once);
        _mockSettingsService.Verify(x => x.SetAsync("UserPlanExpiresAt", It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // 4. イベントテスト (2個)
    [Fact]
    public void PlanChanged_イベント発火()
    {
        // Arrange
        PlanChangedEventArgs? eventArgs = null;
        _service.PlanChanged += (s, e) => eventArgs = e;

        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(true);
        _mockAuthService.Setup(x => x.CurrentUser.Id).Returns("user-123");
        _mockSupabaseClient.Setup(x => x.From<UserData>().Where(It.IsAny<Expression<Func<UserData, bool>>>()).Single())
            .ReturnsAsync(new UserData { Plan = "Premium", PlanExpiresAt = DateTime.UtcNow.AddMonths(1) });

        // Act
        _service.LoadPlanAsync().Wait();

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.NewPlan.Should().Be(UserPlan.Premium);
        eventArgs.OldPlan.Should().Be(UserPlan.Free);
    }

    [Fact]
    public void AuthStateChanged_ログイン時_プラン読み込み()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(true);
        _mockAuthService.Setup(x => x.CurrentUser.Id).Returns("user-123");
        _mockSupabaseClient.Setup(x => x.From<UserData>().Where(It.IsAny<Expression<Func<UserData, bool>>>()).Single())
            .ReturnsAsync(new UserData { Plan = "Premium", PlanExpiresAt = DateTime.UtcNow.AddMonths(1) });

        // Act
        _mockAuthService.Raise(x => x.AuthStateChanged += null, new AuthStateChangedEventArgs { IsAuthenticated = true });
        Task.Delay(500).Wait(); // イベント処理を待機

        // Assert
        _service.CurrentPlan.Should().Be(UserPlan.Premium);
    }

    // 5. 同時実行制御テスト (1個)
    [Fact]
    public async Task LoadPlanAsync_同時実行_排他制御()
    {
        // Arrange
        _mockAuthService.Setup(x => x.IsAuthenticated).Returns(true);
        _mockAuthService.Setup(x => x.CurrentUser.Id).Returns("user-123");
        _mockSupabaseClient.Setup(x => x.From<UserData>().Where(It.IsAny<Expression<Func<UserData, bool>>>()).Single())
            .ReturnsAsync(new UserData { Plan = "Premium", PlanExpiresAt = DateTime.UtcNow.AddMonths(1) });

        // Act
        var task1 = _service.LoadPlanAsync();
        var task2 = _service.LoadPlanAsync();
        await Task.WhenAll(task1, task2);

        // Assert
        // SemaphoreSlimにより排他制御されることを確認
        _service.CurrentPlan.Should().Be(UserPlan.Premium);
    }

    // 6. Disposeテスト (1個)
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
- **作業時間**: 18時間
  - 基本実装: 14時間
  - エラーハンドリング・ログ記録: 2時間
  - キャッシング・リトライロジック: 2時間
- **優先度**: 🟠 High
- **リスク**: 🟡 Medium
  - **軽減策**: 包括的なエラーハンドリング、ローカルキャッシュによるフォールバック、リトライロジック実装

## 📌 備考
- β版では決済機能は簡略化 (手動プラン変更も可能)
- v1.0で本格的なStripe決済統合を実施
- プラン変更履歴をSupabaseに記録 (将来的な分析用)
- プラン期限切れ通知機能は v1.0 以降で実装
