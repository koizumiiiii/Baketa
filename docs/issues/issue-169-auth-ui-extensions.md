# Issue #169: 認証UI拡張（パスワードリセット、ログアウト）

**優先度**: 🟠 High
**所要時間**: 2日
**Epic**: ユーザー認証システム
**ラベル**: `priority: high`, `epic: authentication`, `type: enhancement`, `layer: ui`, `security: enhanced`, `social-auth: integrated`

---

## 概要

ログイン/登録の基本機能（#167）に加えて、パスワードリセット機能とログアウト機能を実装します。また、メインウィンドウにログイン状態を表示し、ユーザー体験を向上させます。

---

## 背景・目的

### 現状の課題（#167, #168完了後）
- パスワードを忘れた場合の対処方法がない
- ログアウト機能がなく、アカウント切り替えができない
- ログイン状態が視覚的に確認できない

### 目指す状態
- パスワードを忘れた場合、リセットメールを送信できる
- いつでもログアウトして別アカウントでログインできる
- メインウィンドウでログイン中のメールアドレス/アバターを確認できる
- **ソーシャルログインのプロバイダー情報が表示される**（例: "Steamアカウントでログイン中"）
- **プロフィール画像が同期される**（Google/Discord/Steamアバター）
- **パスワードリセットが適切に制限される**（レート制限、監査ログ）

---

## スコープ

### 実装タスク

#### 1. パスワードリセット機能
- [ ] **LoginView.axamlに「パスワードを忘れた」リンク追加**
  - クリックでパスワードリセットダイアログ表示

- [ ] **`PasswordResetDialog.axaml` 作成**
  - メールアドレス入力フィールド
  - 送信ボタン
  - 成功/エラーメッセージ表示

- [ ] **ViewModelロジック実装**
  ```csharp
  public ReactiveCommand<Unit, Unit> SendPasswordResetCommand { get; }

  private async Task ExecuteSendPasswordResetAsync()
  {
      IsLoading = true;
      ErrorMessage = string.Empty;

      try
      {
          var result = await _authService.ResetPasswordAsync(Email);
          if (result)
          {
              SuccessMessage = "パスワードリセットメールを送信しました。";
          }
          else
          {
              ErrorMessage = "送信に失敗しました。";
          }
      }
      finally
      {
          IsLoading = false;
      }
  }
  ```

#### 2. ログアウト機能
- [ ] **メインウィンドウにログアウトボタン追加**
  - 設定画面またはメインウィンドウ下部に配置

- [ ] **ログアウト確認ダイアログ**
  - 「本当にログアウトしますか？」確認メッセージ

- [ ] **ログアウト処理実装**
  ```csharp
  public ReactiveCommand<Unit, Unit> LogoutCommand { get; }

  private async Task ExecuteLogoutAsync()
  {
      var confirmed = await ShowConfirmationDialogAsync("ログアウトしますか？");
      if (!confirmed) return;

      await _authService.SignOutAsync();
      // LoginViewへ遷移
      NavigateToLoginView();
  }
  ```

#### 3. ログイン状態の表示
- [ ] **メインウィンドウに認証状態表示エリア追加**
  - ログイン中のメールアドレス表示
  - プラン情報表示（例: "無料プラン"、"スタンダードプラン"）
  - ログアウトボタン

- [ ] **ViewModelプロパティ追加**
  ```csharp
  [Reactive] public string? CurrentUserEmail { get; private set; }
  [Reactive] public string CurrentPlan { get; private set; } = "無料プラン";
  [Reactive] public bool IsAuthenticated { get; private set; }

  private void OnAuthenticationStateChanged(object? sender, AuthenticationStateChangedEventArgs e)
  {
      IsAuthenticated = e.IsAuthenticated;
      CurrentUserEmail = e.User?.Email;
      CurrentPlan = e.User?.Plan ?? "無料プラン";
  }
  ```

#### 4. エラーハンドリング
- [ ] **パスワードリセットエラー**
  - メールアドレスが存在しない場合のエラーメッセージ
  - ネットワークエラー時のリトライ

- [ ] **ログアウトエラー**
  - Supabase接続失敗時もローカルトークンは削除

#### 5. パスワードリセットのセキュリティ強化（P0）
- [ ] **レート制限実装**
  - 同一メールアドレスへのリセットメール送信を15分間に3回まで制限
  - IPアドレスベースのレート制限（1時間に10回まで）

- [ ] **監査ログ統合**
  - パスワードリセット試行の記録（成功/失敗）
  - ログアウト操作の記録
  - 異常なアクセスパターンの検出

- [ ] **リセットトークン管理**
  - トークン有効期限: 1時間
  - ワンタイム使用（再利用不可）
  - トークン無効化API

#### 6. ソーシャルログイン統合（P1）
- [ ] **プロバイダー情報表示**
  - ログイン方法の表示（"Steamアカウント"、"メール/パスワード"等）
  - プロバイダーアイコン表示

- [ ] **プロフィール画像同期**
  - Google/Discord/Steamアバターの取得と表示
  - デフォルトアバター（ソーシャルログインでない場合）
  - アバター画像のキャッシュ

- [ ] **プロバイダー別UI調整**
  - ソーシャルログインの場合、「パスワードを忘れた」リンクを非表示
  - 複数プロバイダー連携の表示（"Google + Steam"等）

---

## 技術仕様

### PasswordResetDialog.axaml

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="Baketa.UI.Views.PasswordResetDialog"
        Title="パスワードリセット"
        Width="400" Height="250"
        WindowStartupLocation="CenterOwner">

  <StackPanel Margin="20" Spacing="15">
    <TextBlock Text="パスワードリセット"
               FontSize="18"
               FontWeight="Bold" />

    <TextBlock Text="登録済みのメールアドレスを入力してください。"
               TextWrapping="Wrap" />

    <TextBox Text="{Binding Email}"
             Watermark="メールアドレス" />

    <TextBlock Text="{Binding ErrorMessage}"
               Foreground="Red"
               TextWrapping="Wrap"
               IsVisible="{Binding ErrorMessage, Converter={StaticResource StringNotEmptyConverter}}" />

    <TextBlock Text="{Binding SuccessMessage}"
               Foreground="Green"
               TextWrapping="Wrap"
               IsVisible="{Binding SuccessMessage, Converter={StaticResource StringNotEmptyConverter}}" />

    <StackPanel Orientation="Horizontal" Spacing="10" HorizontalAlignment="Right">
      <Button Content="キャンセル"
              Command="{Binding CancelCommand}"
              Width="100" />
      <Button Content="送信"
              Command="{Binding SendPasswordResetCommand}"
              IsEnabled="{Binding !IsLoading}"
              Width="100"
              Classes="PrimaryButton" />
    </StackPanel>
  </StackPanel>

</Window>
```

---

### MainWindowViewModel ログイン状態表示

```csharp
public class MainWindowViewModel : ViewModelBase
{
    private readonly IAuthenticationService _authService;

    [Reactive] public string? CurrentUserEmail { get; private set; }
    [Reactive] public string CurrentPlan { get; private set; } = "無料プラン";
    [Reactive] public bool IsAuthenticated { get; private set; }

    public ReactiveCommand<Unit, Unit> LogoutCommand { get; }

    public MainWindowViewModel(IAuthenticationService authService, ...)
    {
        _authService = authService;

        // 認証状態変更イベントをサブスクライブ
        _authService.StateChanged += OnAuthenticationStateChanged;

        // 初期状態設定
        UpdateAuthenticationState();

        // ログアウトコマンド
        LogoutCommand = ReactiveCommand.CreateFromTask(ExecuteLogoutAsync);
    }

    private void OnAuthenticationStateChanged(object? sender, AuthenticationStateChangedEventArgs e)
    {
        UpdateAuthenticationState();
    }

    private void UpdateAuthenticationState()
    {
        IsAuthenticated = _authService.IsAuthenticated;
        CurrentUserEmail = _authService.CurrentUser?.Email;
        CurrentPlan = _authService.CurrentUser?.Plan ?? "無料プラン";
    }

    private async Task ExecuteLogoutAsync()
    {
        var confirmed = await ShowConfirmationDialogAsync("ログアウトしますか？");
        if (!confirmed) return;

        await _authService.SignOutAsync();
        _logger.LogInformation("ログアウトしました");

        // LoginViewへ遷移
        _navigationService.NavigateToLogin();
    }
}
```

---

### パスワードリセットのレート制限実装

```csharp
// PasswordResetViewModel.cs
public class PasswordResetViewModel : ViewModelBase
{
    private readonly IAuthenticationService _authService;
    private readonly IAuditLogger _auditLogger;
    private readonly Dictionary<string, PasswordResetAttemptTracker> _attemptTrackers = new();

    private class PasswordResetAttemptTracker
    {
        public Queue<DateTime> RecentAttempts { get; } = new();
        public DateTime? LockoutUntil { get; set; }

        public bool IsLockedOut => LockoutUntil.HasValue && DateTime.UtcNow < LockoutUntil.Value;

        public bool CanAttemptReset()
        {
            // 15分以内の試行を削除
            while (RecentAttempts.Count > 0 &&
                   (DateTime.UtcNow - RecentAttempts.Peek()).TotalMinutes > 15)
            {
                RecentAttempts.Dequeue();
            }

            // 15分間に3回以上の場合、ロックアウト
            if (RecentAttempts.Count >= 3)
            {
                LockoutUntil = DateTime.UtcNow.AddMinutes(15);
                return false;
            }

            return true;
        }

        public void RecordAttempt()
        {
            RecentAttempts.Enqueue(DateTime.UtcNow);
        }
    }

    private async Task ExecuteSendPasswordResetAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            if (!_attemptTrackers.TryGetValue(Email, out var tracker))
            {
                tracker = new PasswordResetAttemptTracker();
                _attemptTrackers[Email] = tracker;
            }

            if (tracker.IsLockedOut)
            {
                var remainingMinutes = (int)(tracker.LockoutUntil!.Value - DateTime.UtcNow).TotalMinutes;
                ErrorMessage = $"リセット試行回数が上限に達しました。{remainingMinutes}分後に再試行してください。";

                await _auditLogger.LogPasswordResetAttemptAsync(
                    email: Email,
                    success: false,
                    reason: "rate_limit_exceeded");

                return;
            }

            if (!tracker.CanAttemptReset())
            {
                ErrorMessage = "リセット試行回数が上限に達しました。しばらくしてから再試行してください。";
                return;
            }

            tracker.RecordAttempt();

            var result = await _authService.ResetPasswordAsync(Email);

            if (result.IsSuccess)
            {
                SuccessMessage = "パスワードリセットメールを送信しました。メールをご確認ください。";

                await _auditLogger.LogPasswordResetAttemptAsync(
                    email: Email,
                    success: true);
            }
            else
            {
                ErrorMessage = "送信に失敗しました。メールアドレスをご確認ください。";

                await _auditLogger.LogPasswordResetAttemptAsync(
                    email: Email,
                    success: false,
                    reason: result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "送信に失敗しました。ネットワーク接続をご確認ください。";
            _logger.LogError(ex, "パスワードリセットエラー: {Email}", Email);

            await _auditLogger.LogPasswordResetAttemptAsync(
                email: Email,
                success: false,
                reason: "network_error");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
```

---

### ソーシャルログイン統合 - プロフィール表示

```csharp
// MainWindowViewModel.cs (拡張版)
public class MainWindowViewModel : ViewModelBase
{
    [Reactive] public string? CurrentUserEmail { get; private set; }
    [Reactive] public string? CurrentUserDisplayName { get; private set; }
    [Reactive] public string? ProfileImageUrl { get; private set; }
    [Reactive] public string CurrentPlan { get; private set; } = "無料プラン";
    [Reactive] public bool IsAuthenticated { get; private set; }
    [Reactive] public string? LoginProvider { get; private set; } // "Steam", "Google", "Email"等
    [Reactive] public bool IsPasswordResetAvailable { get; private set; } // メール/パスワードログインの場合のみtrue

    private void UpdateAuthenticationState()
    {
        var user = _authService.CurrentUser;

        IsAuthenticated = user != null;
        CurrentUserEmail = user?.Email;
        CurrentUserDisplayName = user?.UserMetadata?.GetValue<string>("full_name") ?? user?.Email?.Split('@')[0];
        CurrentPlan = user?.AppMetadata?.GetValue<string>("plan") ?? "無料プラン";

        // プロバイダー情報取得
        var identities = user?.Identities;
        if (identities?.Any() == true)
        {
            var primaryIdentity = identities.First();
            LoginProvider = primaryIdentity.Provider switch
            {
                "google" => "Google",
                "discord" => "Discord",
                "steam" => "Steam",
                "email" => "メール/パスワード",
                _ => primaryIdentity.Provider
            };

            // プロフィール画像取得
            ProfileImageUrl = primaryIdentity.Provider switch
            {
                "google" => user.UserMetadata?.GetValue<string>("avatar_url"),
                "discord" => user.UserMetadata?.GetValue<string>("avatar_url"),
                "steam" => user.UserMetadata?.GetValue<string>("avatar"),
                _ => null
            };

            // パスワードリセット可否判定
            IsPasswordResetAvailable = primaryIdentity.Provider == "email";
        }
        else
        {
            LoginProvider = "メール/パスワード";
            IsPasswordResetAvailable = true;
        }
    }
}
```

---

### プロフィール表示UI (MainWindow.axaml拡張)

```xml
<!-- メインウィンドウ上部にプロフィール表示エリア -->
<Border Background="#2C2C2C"
        Padding="10"
        CornerRadius="5"
        Margin="10">
  <StackPanel Orientation="Horizontal" Spacing="10">

    <!-- プロフィール画像 -->
    <Border Width="40" Height="40"
            CornerRadius="20"
            ClipToBounds="True">
      <Image Source="{Binding ProfileImageUrl}"
             Stretch="UniformToFill">
        <!-- デフォルトアバター（画像がない場合） -->
        <Image.Fallback>
          <Image Source="/Assets/Icons/default-avatar.png" />
        </Image.Fallback>
      </Image>
    </Border>

    <!-- ユーザー情報 -->
    <StackPanel VerticalAlignment="Center" Spacing="2">
      <TextBlock Text="{Binding CurrentUserDisplayName}"
                 FontWeight="Bold"
                 Foreground="White" />

      <StackPanel Orientation="Horizontal" Spacing="5">
        <!-- プロバイダーアイコン -->
        <Image Width="16" Height="16"
               IsVisible="{Binding LoginProvider, Converter={StaticResource StringEqualsConverter}, ConverterParameter=Steam}">
          <Image.Source>/Assets/Icons/steam-icon.png</Image.Source>
        </Image>

        <Image Width="16" Height="16"
               IsVisible="{Binding LoginProvider, Converter={StaticResource StringEqualsConverter}, ConverterParameter=Google}">
          <Image.Source>/Assets/Icons/google-icon.png</Image.Source>
        </Image>

        <Image Width="16" Height="16"
               IsVisible="{Binding LoginProvider, Converter={StaticResource StringEqualsConverter}, ConverterParameter=Discord}">
          <Image.Source>/Assets/Icons/discord-icon.png</Image.Source>
        </Image>

        <TextBlock Text="{Binding LoginProvider}"
                   FontSize="12"
                   Foreground="#B0B0B0" />
      </StackPanel>

      <TextBlock Text="{Binding CurrentPlan}"
                 FontSize="11"
                 Foreground="#808080" />
    </StackPanel>

    <!-- ログアウトボタン -->
    <Button Command="{Binding LogoutCommand}"
            HorizontalAlignment="Right"
            Content="ログアウト"
            Classes="SecondaryButton"
            Width="80" />
  </StackPanel>
</Border>
```

---

### 監査ログ拡張 (IAuditLogger)

```csharp
// IAuditLogger.cs拡張
public interface IAuditLogger
{
    // 既存メソッド（Issue #168から）
    Task LogTokenIssuedAsync(string userId, DateTime expiresAt, CancellationToken cancellationToken = default);
    Task LogTokenRefreshedAsync(string userId, DateTime oldExpiry, DateTime newExpiry, CancellationToken cancellationToken = default);
    Task LogTokenRevokedAsync(string userId, string reason, CancellationToken cancellationToken = default);
    Task LogTokenValidationFailedAsync(string reason, CancellationToken cancellationToken = default);

    // 新規メソッド（Issue #169）
    Task LogPasswordResetAttemptAsync(string email, bool success, string? reason = null, CancellationToken cancellationToken = default);
    Task LogLogoutAsync(string userId, string? reason = null, CancellationToken cancellationToken = default);
    Task LogProfileViewedAsync(string userId, CancellationToken cancellationToken = default);
}

// FileTokenAuditLogger.cs拡張
public async Task LogPasswordResetAttemptAsync(string email, bool success, string? reason = null, CancellationToken cancellationToken = default)
{
    var entry = new AuditLogEntry
    {
        Timestamp = DateTime.UtcNow,
        EventType = "password_reset_attempt",
        Email = email,
        Success = success,
        Reason = reason,
        IpAddress = await GetClientIpAsync()
    };

    await WriteAuditLogAsync(entry, cancellationToken);
}

public async Task LogLogoutAsync(string userId, string? reason = null, CancellationToken cancellationToken = default)
{
    var entry = new AuditLogEntry
    {
        Timestamp = DateTime.UtcNow,
        EventType = "logout",
        UserId = userId,
        Reason = reason ?? "user_initiated"
    };

    await WriteAuditLogAsync(entry, cancellationToken);
}
```

---

## 動作確認基準

### 必須動作確認項目

#### パスワードリセット
- [ ] **パスワードリセット**: 「パスワードを忘れた」リンクをクリックすると、リセットダイアログが表示される
- [ ] **リセットメール送信**: 正しいメールアドレスを入力すると、リセットメールが送信される
- [ ] **リセットエラー**: 存在しないメールアドレスを入力すると、エラーメッセージが表示される
- [ ] **レート制限**: 15分間に3回リセット試行すると、ロックアウトメッセージが表示される
- [ ] **監査ログ記録**: パスワードリセット試行が監査ログに記録される

#### ログアウト
- [ ] **ログアウト実行**: ログアウトボタンを押すと、確認ダイアログが表示される
- [ ] **ログアウト完了**: 確認後、LoginViewに遷移し、トークンが削除される
- [ ] **ログアウト監査**: ログアウト操作が監査ログに記録される

#### ログイン状態表示
- [ ] **メール表示**: メインウィンドウにログイン中のメールアドレスが表示される
- [ ] **プラン表示**: 現在のプラン（"無料プラン"等）が表示される
- [ ] **表示名表示**: ユーザーの表示名が正しく表示される

#### ソーシャルログイン統合
- [ ] **プロバイダー表示**: ログイン方法（Steam/Google/Discord）が正しく表示される
- [ ] **プロバイダーアイコン**: 各プロバイダーのアイコンが表示される
- [ ] **プロフィール画像**: Google/Discord/Steamアバターが表示される
- [ ] **デフォルトアバター**: ソーシャルログインでない場合、デフォルトアバターが表示される
- [ ] **パスワードリセット非表示**: ソーシャルログインの場合、「パスワードを忘れた」リンクが非表示になる
- [ ] **複数プロバイダー**: 複数プロバイダー連携時、すべてのアイコンが表示される

---

## 依存関係

### Blocked by
- #168: トークン管理と永続化（ログアウト時のトークン削除が必要）

### Blocks
なし（このissueは拡張機能のため、他issueをブロックしない）

---

## 変更ファイル

### 新規作成
- `Baketa.UI/Views/PasswordResetDialog.axaml`
- `Baketa.UI/Views/PasswordResetDialog.axaml.cs`
- `Baketa.UI/ViewModels/PasswordResetViewModel.cs`
- `Baketa.UI/Assets/Icons/default-avatar.png` (デフォルトアバター: 40x40px)
- `Baketa.UI/Services/AvatarCacheService.cs` (プロフィール画像キャッシュ)
- `tests/Baketa.UI.Tests/ViewModels/PasswordResetViewModelTests.cs`

### 修正
- `Baketa.UI/Views/LoginView.axaml` (「パスワードを忘れた」リンク追加、ソーシャルログインでの非表示)
- `Baketa.UI/Views/MainWindow.axaml` (プロフィール表示エリア、ログアウトボタン、プロバイダーアイコン)
- `Baketa.UI/ViewModels/MainWindowViewModel.cs` (+7プロパティ, +1コマンド)
- `Baketa.Core.Abstractions/Services/IAuthenticationService.cs` (+ResetPasswordAsync)
- `Baketa.Core.Abstractions/Services/IAuditLogger.cs` (+3メソッド)
- `Baketa.Infrastructure/Authentication/SupabaseAuthenticationService.cs` (ResetPasswordAsync実装)
- `Baketa.Infrastructure/Authentication/FileTokenAuditLogger.cs` (監査ログメソッド追加)

---

## 実装ガイドライン

### Supabaseパスワードリセット
- `supabase.auth.resetPasswordForEmail(email)` を使用
- Supabaseダッシュボードでリセットメールテンプレートを設定

### ログアウト時の注意
- Supabaseセッション終了が失敗してもローカルトークンは削除（ベストエフォート）
- ネットワークエラー時もログアウトは成功扱い

---

**作成日**: 2025-11-18
**最終更新**: 2025-11-18
**作成者**: Claude Code
**関連ドキュメント**: `docs/BETA_DEVELOPMENT_PLAN.md`, `docs/issues/issue-167-login-ui.md`, `docs/issues/issue-168-token-management.md`

---

## 更新履歴

### 2025-11-18: セキュリティ強化とソーシャルログイン統合
- **変更理由**: Issue #167, #168の改善パターンを適用し、セキュリティと統合性を向上
- **追加内容**:
  - パスワードリセットのレート制限（15分間に3回まで）
  - 監査ログ統合（パスワードリセット、ログアウト、プロフィール閲覧）
  - ソーシャルログインプロバイダー情報表示（Steam/Google/Discord）
  - プロフィール画像同期（各プロバイダーのアバター）
  - プロバイダー別UI調整（ソーシャルログインでのパスワードリセット非表示）
  - デフォルトアバター実装
  - アバター画像キャッシュサービス
- **優先度変更**: Medium → High
- **所要時間変更**: 1日 → 2日
- **Issue #167, #168との統合**: 監査ログ、セキュリティパターン、ソーシャルログイン連携
