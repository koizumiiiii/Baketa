# Issue #167: ログイン/登録UI実装（MVP）

**優先度**: 🔴 Critical+ (P0+)
**所要時間**: 4-5日
**Epic**: ユーザー認証システム
**ラベル**: `priority: critical+`, `epic: authentication`, `type: feature`, `layer: ui`, `security: enhanced`, `oauth: enabled`

---

## 概要

Supabase認証システム（#133で構築）を利用したログイン/登録UIを実装します。ユーザーがメールアドレスとパスワードで新規登録・ログインできる最小限の機能（MVP）を提供し、将来的な有料プラン管理の土台を構築します。

---

## 背景・目的

### 現状の課題
- ユーザーアカウント機能が存在せず、プラン管理ができない
- 広告表示の有無を制御できない（#125で必要）
- ユーザーごとの設定・履歴を保存できない

### 目指す状態
- メールアドレス・パスワードでユーザー登録ができる
- **Twitch、Discord、Googleアカウントでワンクリックログインができる**
- **Steam認証は Issue #173 で別途実装予定**
- 登録済みユーザーがログインできる
- ログイン状態を視覚的に確認できる
- エラー時に適切なメッセージが表示される
- ソーシャルログイン後、既存アカウントと自動で紐付けできる

---

## スコープ

### 実装タスク

#### 1. ログイン/登録画面UI作成
- [ ] **`LoginView.axaml` 作成**（Avalonia XAML）
  - メールアドレス入力フィールド
  - パスワード入力フィールド（マスク表示）
  - ログインボタン
  - 新規登録ボタン
  - エラーメッセージ表示エリア
  - ローディングスピナー（認証処理中）

- [ ] **UI要素の配置**
  - 中央揃えレイアウト
  - Baketaロゴ（上部）
  - フォーム（中央）
  - リンク（下部: 「パスワードを忘れた」※#169で実装）

#### 2. ViewModelロジック実装
- [ ] **`LoginViewModel.cs` 作成**（ReactiveUI）
  - `Email` プロパティ（string, INotifyPropertyChanged）
  - `Password` プロパティ（string, INotifyPropertyChanged）
  - `ErrorMessage` プロパティ（string, エラー表示用）
  - `IsLoading` プロパティ（bool, ローディング状態）
  - `LoginCommand` (ReactiveCommand): ログイン実行
  - `SignUpCommand` (ReactiveCommand): 新規登録実行

- [ ] **バリデーション実装**（ReactiveUI.Validation）
  - Emailフォーマットチェック（正規表現）
  - パスワード長チェック（8文字以上）
  - 必須入力チェック（空白不可）
  - **🔒 パスワード強度チェック強化（P0）**
    - 大文字・小文字・数字・記号のうち3種類以上を含むこと
    - 一般的な脆弱パスワード（"password", "12345678"等）のブラックリストチェック
    - パスワード強度インジケーター表示（弱い/普通/強い）

#### 3. 認証フロー統合
- [ ] **`IAuthenticationService` 注入**
  - DIコンテナから `IAuthenticationService` を取得
  - ViewModelに注入

- [ ] **ログイン処理**
  ```csharp
  LoginCommand = ReactiveCommand.CreateFromTask(async () =>
  {
      IsLoading = true;
      ErrorMessage = string.Empty;

      try
      {
          var result = await _authService.SignInAsync(Email, Password);
          if (result.IsSuccess)
          {
              // メインウィンドウへ遷移
              NavigateToMainWindow();
          }
          else
          {
              ErrorMessage = result.ErrorMessage;
          }
      }
      catch (Exception ex)
      {
          ErrorMessage = "ログインに失敗しました。";
          _logger.LogError(ex, "ログインエラー");
      }
      finally
      {
          IsLoading = false;
      }
  });
  ```

- [ ] **新規登録処理**
  ```csharp
  SignUpCommand = ReactiveCommand.CreateFromTask(async () =>
  {
      IsLoading = true;
      ErrorMessage = string.Empty;

      try
      {
          var result = await _authService.SignUpAsync(Email, Password);
          if (result.IsSuccess)
          {
              // 登録成功メッセージ表示
              // 確認メール送信案内（Supabaseの設定による）
              ErrorMessage = "登録完了しました。ログインしてください。";
          }
          else
          {
              ErrorMessage = result.ErrorMessage;
          }
      }
      catch (Exception ex)
      {
          ErrorMessage = "登録に失敗しました。";
          _logger.LogError(ex, "登録エラー");
      }
      finally
      {
          IsLoading = false;
      }
  });
  ```

#### 4. 画面遷移ロジック
- [ ] **起動時の分岐処理**
  - トークンが保存されている → 自動ログイン試行 → MainWindowへ
  - トークンがない → LoginViewを表示

- [ ] **ログイン成功後の遷移**
  - LoginViewを閉じる
  - MainWindowを表示

#### 5. エラーハンドリング
- [ ] **Supabaseエラーメッセージのマッピング**
  - `Invalid login credentials` → 「メールアドレスまたはパスワードが正しくありません」
  - `User already registered` → 「このメールアドレスは既に登録されています」
  - `Email not confirmed` → 「メールアドレスが確認されていません」

- [ ] **ネットワークエラー対応**
  - Supabase接続失敗時のフォールバック
  - 「ネットワーク接続を確認してください」メッセージ

#### 6. ソーシャルログイン対応（P1 → P0昇格）
- [x] **Supabase OAuth設定** (Issue #133 で完了)
  - Googleプロバイダー設定（Supabaseダッシュボード）✅
  - Discordプロバイダー設定（Discord Developer Portal連携）✅
  - Twitchプロバイダー設定（Twitch Developer Console連携）✅
  - Steam OpenID設定 → Issue #173 へ分離

- [ ] **UI実装**
  - Googleログインボタン（Google標準デザイン）
  - Discordログインボタン（Discord標準デザイン）
  - Twitchログインボタン（Twitch標準デザイン）
  - 区切り線とラベル（「または」）
  - ※ Steamログインボタンは Issue #173 実装後に追加

- [ ] **OAuth フロー実装**
  ```csharp
  // Google/Discord/Twitch: Supabase標準OAuth
  await _authService.SignInWithOAuthAsync(Provider.Google);
  await _authService.SignInWithOAuthAsync(Provider.Discord);
  await _authService.SignInWithOAuthAsync(Provider.Twitch);

  // Steam: カスタムOpenID実装 (Issue #173)
  // await _authService.SignInWithSteamAsync();
  ```

- [ ] **アカウント紐付け処理**
  - 既存メールアドレスと一致する場合、自動紐付け
  - 初回ログイン時、Supabaseアカウント作成
  - プロフィール情報同期（アバター、表示名）

- [ ] **エラーハンドリング**
  - OAuth認証キャンセル時の処理
  - OAuth プロバイダーエラー時のフォールバック
  - アカウント重複時の警告表示

#### 7. UIテスト実装
- [ ] **`LoginViewModelTests.cs` 作成**（xUnit + Moq）
  - バリデーションテスト (5ケース)
  - ログイン成功テスト (3ケース)
  - ログイン失敗テスト (5ケース)
  - 新規登録成功テスト (2ケース)
  - **ソーシャルログインテスト (6ケース)**
    - Google OAuth成功/失敗
    - Discord OAuth成功/失敗
    - Twitch OAuth成功/失敗
  - ※ Steam OpenIDテストは Issue #173 で追加

---

## 技術仕様

### LoginView.axaml（Avalonia XAML）

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:Baketa.UI.ViewModels"
        x:Class="Baketa.UI.Views.LoginView"
        Title="Baketa - ログイン"
        Width="400" Height="500"
        WindowStartupLocation="CenterScreen"
        CanResize="False">

  <Design.DataContext>
    <vm:LoginViewModel />
  </Design.DataContext>

  <StackPanel Margin="40" Spacing="20" VerticalAlignment="Center">

    <!-- ロゴ -->
    <Image Source="/Assets/baketa-logo.png"
           Width="120" Height="120"
           HorizontalAlignment="Center" />

    <TextBlock Text="Baketa"
               FontSize="24"
               FontWeight="Bold"
               HorizontalAlignment="Center" />

    <!-- メールアドレス入力 -->
    <TextBox Text="{Binding Email}"
             Watermark="メールアドレス"
             Width="300" />

    <!-- パスワード入力 -->
    <TextBox Text="{Binding Password}"
             PasswordChar="●"
             Watermark="パスワード（8文字以上）"
             Width="300" />

    <!-- エラーメッセージ -->
    <TextBlock Text="{Binding ErrorMessage}"
               Foreground="Red"
               TextWrapping="Wrap"
               HorizontalAlignment="Center"
               IsVisible="{Binding ErrorMessage, Converter={StaticResource StringNotEmptyConverter}}" />

    <!-- ローディングスピナー -->
    <ProgressBar IsIndeterminate="True"
                 IsVisible="{Binding IsLoading}"
                 Width="300" />

    <!-- ログインボタン -->
    <Button Content="ログイン"
            Command="{Binding LoginCommand}"
            IsEnabled="{Binding !IsLoading}"
            Width="300"
            Height="40"
            Classes="PrimaryButton" />

    <!-- 新規登録ボタン -->
    <Button Content="新規登録"
            Command="{Binding SignUpCommand}"
            IsEnabled="{Binding !IsLoading}"
            Width="300"
            Height="40"
            Classes="SecondaryButton" />

    <!-- パスワードを忘れた（#169で実装） -->
    <TextBlock Text="パスワードを忘れた方はこちら"
               Foreground="Blue"
               TextDecorations="Underline"
               HorizontalAlignment="Center"
               Cursor="Hand"
               IsVisible="False" />

    <!-- 区切り線 -->
    <Separator Margin="0,10,0,10" />
    <TextBlock Text="または"
               HorizontalAlignment="Center"
               Foreground="#808080"
               FontSize="12" />

    <!-- ソーシャルログインボタン -->
    <Button Command="{Binding LoginWithGoogleCommand}"
            IsEnabled="{Binding !IsLoading}"
            Width="300"
            Height="40"
            Background="White"
            BorderBrush="#4285F4"
            BorderThickness="1">
      <StackPanel Orientation="Horizontal" Spacing="10">
        <Image Source="/Assets/Icons/google-icon.png" Width="20" Height="20" />
        <TextBlock Text="Googleでログイン" Foreground="Black" VerticalAlignment="Center" />
      </StackPanel>
    </Button>

    <Button Command="{Binding LoginWithDiscordCommand}"
            IsEnabled="{Binding !IsLoading}"
            Width="300"
            Height="40"
            Background="#5865F2"
            BorderThickness="0">
      <StackPanel Orientation="Horizontal" Spacing="10">
        <Image Source="/Assets/Icons/discord-icon.png" Width="20" Height="20" />
        <TextBlock Text="Discordでログイン" Foreground="White" VerticalAlignment="Center" />
      </StackPanel>
    </Button>

    <Button Command="{Binding LoginWithTwitchCommand}"
            IsEnabled="{Binding !IsLoading}"
            Width="300"
            Height="40"
            Background="#9146FF"
            BorderThickness="0">
      <StackPanel Orientation="Horizontal" Spacing="10">
        <Image Source="/Assets/Icons/twitch-icon.png" Width="20" Height="20" />
        <TextBlock Text="Twitchでログイン" Foreground="White" VerticalAlignment="Center" />
      </StackPanel>
    </Button>

    <!-- Steam認証は Issue #173 実装後に追加 -->
  </StackPanel>

</Window>
```

---

### LoginViewModel.cs（ReactiveUI）

```csharp
namespace Baketa.UI.ViewModels;

public class LoginViewModel : ViewModelBase
{
    private readonly IAuthenticationService _authService;
    private readonly ILogger<LoginViewModel> _logger;
    private readonly Action _navigateToMainWindow;

    [Reactive] public string Email { get; set; } = string.Empty;
    [Reactive] public string Password { get; set; } = string.Empty;
    [Reactive] public string ErrorMessage { get; set; } = string.Empty;
    [Reactive] public bool IsLoading { get; set; }

    public ReactiveCommand<Unit, Unit> LoginCommand { get; }
    public ReactiveCommand<Unit, Unit> SignUpCommand { get; }

    // ソーシャルログインコマンド
    public ReactiveCommand<Unit, Unit> LoginWithGoogleCommand { get; }
    public ReactiveCommand<Unit, Unit> LoginWithDiscordCommand { get; }
    public ReactiveCommand<Unit, Unit> LoginWithTwitchCommand { get; }
    // Steam認証は Issue #173 で実装予定
    // public ReactiveCommand<Unit, Unit> LoginWithSteamCommand { get; }

    public LoginViewModel(
        IAuthenticationService authService,
        ILogger<LoginViewModel> logger,
        Action navigateToMainWindow)
    {
        _authService = authService;
        _logger = logger;
        _navigateToMainWindow = navigateToMainWindow;

        // バリデーション
        var isEmailValid = this.WhenAnyValue(
            x => x.Email,
            email => !string.IsNullOrWhiteSpace(email) && email.Contains('@'));

        var isPasswordValid = this.WhenAnyValue(
            x => x.Password,
            password => !string.IsNullOrWhiteSpace(password) && password.Length >= 8);

        var canExecute = this.WhenAnyValue(
            x => x.IsLoading,
            isLoading => !isLoading);

        var canLogin = Observable.CombineLatest(
            isEmailValid,
            isPasswordValid,
            canExecute,
            (emailValid, passwordValid, canExec) => emailValid && passwordValid && canExec);

        // コマンド定義
        LoginCommand = ReactiveCommand.CreateFromTask(ExecuteLoginAsync, canLogin);
        SignUpCommand = ReactiveCommand.CreateFromTask(ExecuteSignUpAsync, canLogin);

        // ソーシャルログインコマンド（ローディング中以外は常に実行可能）
        LoginWithGoogleCommand = ReactiveCommand.CreateFromTask(
            async () => await ExecuteSocialLoginAsync(OAuthProvider.Google), canExecute);
        LoginWithDiscordCommand = ReactiveCommand.CreateFromTask(
            async () => await ExecuteSocialLoginAsync(OAuthProvider.Discord), canExecute);
        LoginWithTwitchCommand = ReactiveCommand.CreateFromTask(
            async () => await ExecuteSocialLoginAsync(OAuthProvider.Twitch), canExecute);
        // Steam認証は Issue #173 で実装予定
        // LoginWithSteamCommand = ReactiveCommand.CreateFromTask(ExecuteSteamLoginAsync, canExecute);
    }

    private async Task ExecuteLoginAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _authService.SignInAsync(Email, Password);
            if (result.IsSuccess)
            {
                _logger.LogInformation("ログイン成功: {Email}", Email);
                _navigateToMainWindow();
            }
            else
            {
                ErrorMessage = MapErrorMessage(result.ErrorMessage);
                _logger.LogWarning("ログイン失敗: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "ログインに失敗しました。ネットワーク接続を確認してください。";
            _logger.LogError(ex, "ログインエラー");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ExecuteSignUpAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _authService.SignUpAsync(Email, Password);
            if (result.IsSuccess)
            {
                ErrorMessage = "登録完了しました。ログインしてください。";
                _logger.LogInformation("新規登録成功: {Email}", Email);
            }
            else
            {
                ErrorMessage = MapErrorMessage(result.ErrorMessage);
                _logger.LogWarning("新規登録失敗: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "登録に失敗しました。";
            _logger.LogError(ex, "新規登録エラー");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string MapErrorMessage(string supabaseError)
    {
        return supabaseError switch
        {
            "Invalid login credentials" => "メールアドレスまたはパスワードが正しくありません。",
            "User already registered" => "このメールアドレスは既に登録されています。",
            "Email not confirmed" => "メールアドレスが確認されていません。",
            _ => $"エラー: {supabaseError}"
        };
    }

    private async Task ExecuteSocialLoginAsync(OAuthProvider provider)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            _logger.LogInformation("ソーシャルログイン開始: {Provider}", provider);

            var result = await _authService.SignInWithOAuthAsync(provider);

            if (result.IsSuccess)
            {
                _logger.LogInformation("ソーシャルログイン成功: {Provider}, User: {UserId}",
                    provider, result.User?.Id);
                _navigateToMainWindow();
            }
            else
            {
                ErrorMessage = provider switch
                {
                    OAuthProvider.Google => "Googleログインに失敗しました。",
                    OAuthProvider.Discord => "Discordログインに失敗しました。",
                    OAuthProvider.Twitch => "Twitchログインに失敗しました。",
                    _ => $"{provider}ログインに失敗しました。"
                };
                _logger.LogWarning("ソーシャルログイン失敗: {Provider}, Error: {Error}",
                    provider, result.ErrorMessage);
            }
        }
        catch (OAuthCancelledException)
        {
            ErrorMessage = "ログインがキャンセルされました。";
            _logger.LogInformation("ユーザーがOAuth認証をキャンセル: {Provider}", provider);
        }
        catch (Exception ex)
        {
            ErrorMessage = "ログインに失敗しました。ネットワーク接続を確認してください。";
            _logger.LogError(ex, "ソーシャルログインエラー: {Provider}", provider);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ExecuteSteamLoginAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            _logger.LogInformation("Steam OpenIDログイン開始");

            // Steam OpenIDは別のフロー（Webブラウザ起動→コールバック）
            var result = await _authService.SignInWithSteamAsync();

            if (result.IsSuccess)
            {
                _logger.LogInformation("Steamログイン成功: User: {UserId}", result.User?.Id);
                _navigateToMainWindow();
            }
            else
            {
                ErrorMessage = "Steamログインに失敗しました。";
                _logger.LogWarning("Steamログイン失敗: {Error}", result.ErrorMessage);
            }
        }
        catch (OAuthCancelledException)
        {
            ErrorMessage = "ログインがキャンセルされました。";
            _logger.LogInformation("ユーザーがSteam認証をキャンセル");
        }
        catch (Exception ex)
        {
            ErrorMessage = "Steamログインに失敗しました。ネットワーク接続を確認してください。";
            _logger.LogError(ex, "Steamログインエラー");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
```

---

## 動作確認基準

### 必須動作確認項目

#### メール/パスワード認証
- [ ] **ログイン成功**: 正しいEmail/Passwordでログインすると、MainWindowが表示される
- [ ] **ログイン失敗**: 間違ったPasswordでログインすると、エラーメッセージが表示される
- [ ] **新規登録成功**: 未登録のEmailで新規登録すると、成功メッセージが表示される
- [ ] **新規登録失敗（重複）**: 既存のEmailで新規登録すると、「既に登録されています」エラーが表示される
- [ ] **バリデーション（Email）**: Email形式が不正な場合、ボタンが無効化される
- [ ] **バリデーション（Password）**: Password長が8文字未満の場合、ボタンが無効化される
- [ ] **ローディング表示**: 認証処理中にスピナーが表示され、ボタンが無効化される
- [ ] **ネットワークエラー**: Supabase接続失敗時に適切なエラーメッセージが表示される

#### ソーシャルログイン
- [ ] **Googleログイン成功**: Googleアカウントでログインし、MainWindowが表示される
- [ ] **Googleログイン失敗**: Google認証エラー時、適切なエラーメッセージが表示される
- [ ] **Googleログインキャンセル**: ユーザーがGoogle認証をキャンセルすると、「ログインがキャンセルされました」と表示される
- [ ] **Discordログイン成功**: Discordアカウントでログインし、MainWindowが表示される
- [ ] **Discordログイン失敗**: Discord認証エラー時、適切なエラーメッセージが表示される
- [ ] **Discordログインキャンセル**: ユーザーがDiscord認証をキャンセルすると、エラーメッセージが表示される
- [ ] **Twitchログイン成功**: Twitchアカウントでログインし、MainWindowが表示される
- [ ] **Twitchログイン失敗**: Twitch認証エラー時、適切なエラーメッセージが表示される
- [ ] **Twitchログインキャンセル**: ユーザーがTwitch認証をキャンセルすると、エラーメッセージが表示される
- [ ] **アカウント紐付け**: 既存のメールアドレスと一致するソーシャルログインの場合、自動紐付けされる
- [ ] **プロフィール同期**: ソーシャルログイン後、アバターと表示名が同期される

> **Note**: Steam認証テスト (成功/失敗/キャンセル) は Issue #173 で追加

### UIテスト実行基準

- [ ] `LoginViewModelTests`: 全21ケースが成功（元の15件 + ソーシャルログイン6件）

---

## 依存関係

### Blocked by（先行して完了すべきissue）
- #133: Supabase Auth クラウド側設定実施（Supabaseプロジェクト作成、`IAuthenticationService`実装）

### Blocks（このissue完了後に着手可能なissue）
- #168: トークン管理と永続化（ログイン成功後のトークン保存）
- #169: 認証UI拡張（パスワードリセット、ログアウト）

---

## 変更ファイル

### 新規作成
- `Baketa.UI/Views/LoginView.axaml`
- `Baketa.UI/Views/LoginView.axaml.cs`
- `Baketa.UI/ViewModels/LoginViewModel.cs`
- `Baketa.UI/Assets/baketa-logo.png`
- `Baketa.UI/Assets/Icons/google-icon.png` (Googleロゴ: 20x20px)
- `Baketa.UI/Assets/Icons/discord-icon.png` (Discordロゴ: 20x20px)
- `Baketa.UI/Assets/Icons/twitch-icon.png` (Twitchロゴ: 20x20px)
- `Baketa.UI/Assets/Icons/steam-icon.png` (Steamロゴ: 20x20px) ※Issue #173 で使用
- `Baketa.UI/Styles/LoginStyles.axaml`
- `Baketa.Core.Abstractions/Services/IAuthenticationService.cs` (OAuth拡張)
- `Baketa.Infrastructure/Authentication/OAuthProvider.cs` (列挙型: Google, Discord, Twitch)
- `Baketa.Infrastructure/Authentication/Exceptions/OAuthCancelledException.cs`
- `tests/Baketa.UI.Tests/ViewModels/LoginViewModelTests.cs`

**Issue #173 で追加予定:**
- `Baketa.Infrastructure/Authentication/SteamOpenIdAuthenticator.cs` (Steam専用実装)
- `tests/Baketa.Infrastructure.Tests/Authentication/SteamOpenIdAuthenticatorTests.cs`

### 修正
- `Baketa.UI/App.axaml.cs` (起動時の分岐処理: トークンの有無で画面切替)
- `Baketa.UI/DI/Modules/UIModule.cs` (LoginViewModel のDI登録)
- `Baketa.Infrastructure/Authentication/SupabaseAuthenticationService.cs` (OAuth実装追加)

---

## 実装ガイドライン

### ReactiveUIバリデーション
- `WhenAnyValue()` でプロパティ変更を監視
- `CombineLatest()` で複数条件を組み合わせ
- コマンドの `canExecute` に条件を設定

### パスワードマスク表示
- Avalonia標準の `PasswordChar` プロパティを使用
- 「パスワードを表示」トグルは#169で実装（オプション機能）

### セキュリティ考慮
- パスワードは平文保存しない（Supabase側で管理）
- HTTPS通信必須（Supabase接続）
- CSRF対策はSupabase側で実装済み
- OAuth State parameter検証（CSRF対策）
- Steam OpenID署名検証（なりすまし防止）

### ソーシャルログイン実装手順

#### 1. Supabase側設定（Google/Discord）
```bash
# Supabaseダッシュボード → Authentication → Providers

# Google OAuth
1. Google Cloud Consoleでプロジェクト作成
2. OAuth 2.0クライアントID作成
3. リダイレクトURI: https://[project-ref].supabase.co/auth/v1/callback
4. SupabaseにClient IDとClient Secretを設定

# Discord OAuth
1. Discord Developer Portalでアプリケーション作成
2. OAuth2タブでリダイレクトURI追加
3. SupabaseにClient IDとClient Secretを設定
```

#### 2. Steam OpenID実装（カスタム）
```csharp
// Steam OpenIDはSupabase標準OAuthではないため、カスタム実装が必要

public class SteamOpenIdAuthenticator
{
    private const string SteamOpenIdUrl = "https://steamcommunity.com/openid/login";

    public async Task<AuthResult> AuthenticateAsync(string returnUrl)
    {
        // 1. Steam OpenIDにリダイレクト（ブラウザ起動）
        var openIdParams = new Dictionary<string, string>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = returnUrl,
            ["openid.realm"] = returnUrl,
            ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
            ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select"
        };

        var url = $"{SteamOpenIdUrl}?{BuildQueryString(openIdParams)}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        // 2. ローカルHTTPサーバーでコールバック待機
        var steamId = await WaitForCallbackAsync();

        // 3. SteamIDをSupabaseアカウントと紐付け
        return await LinkSteamAccountAsync(steamId);
    }
}
```

#### 3. アカウント紐付けロジック
```csharp
public async Task<AuthResult> LinkSocialAccountAsync(OAuthProvider provider, string email)
{
    // 既存アカウント検索
    var existingUser = await _supabase
        .From<User>()
        .Where(u => u.Email == email)
        .Single();

    if (existingUser != null)
    {
        // 既存アカウントに紐付け
        await _supabase
            .From<UserIdentity>()
            .Insert(new UserIdentity
            {
                UserId = existingUser.Id,
                Provider = provider.ToString(),
                ProviderId = providerId
            });
    }
    else
    {
        // 新規アカウント作成
        var newUser = await _supabase.Auth.SignUp(email, GenerateRandomPassword());
        await LinkProviderIdentity(newUser.Id, provider, providerId);
    }

    return AuthResult.Success(existingUser ?? newUser);
}
```

---

## 備考

### 実装済み機能（β版）
- ✅ **ソーシャルログイン**: Google、Discord、Twitch対応
- ✅ **パスワード強度チェック**: 大文字・小文字・数字・記号の組み合わせ
- ⏳ **Steam認証**: Issue #173 で別途実装予定

### 将来的な拡張（v1.0.0以降）
- GitHub OAuth対応（開発者向け）
- 多要素認証（2FA）
- パスワードリセット機能の拡張（#169で基本実装）

### デザイン指針
- シンプルで直感的なUI
- エラーメッセージは具体的で理解しやすい表現
- ローディング状態を明確に表示

---

**作成日**: 2025-11-18
**最終更新**: 2025-11-18
**作成者**: Claude Code
**関連ドキュメント**: `docs/BETA_DEVELOPMENT_PLAN.md`, `docs/issues/issue-133-supabase-auth.md` (既存)

---

## 更新履歴

### 2025-11-18: ソーシャルログイン対応追加
- **変更理由**: ゲーム翻訳アプリとして、Discord/Googleアカウント連携は必須機能
- **追加内容**:
  - Google OAuth実装（Supabase標準）
  - Discord OAuth実装（Supabase標準）
  - Steam OpenID実装（カスタム実装）
  - アカウント紐付けロジック
  - プロフィール同期機能
  - ソーシャルログインUI（3つのボタン）
  - ソーシャルログインテスト（6ケース追加）
- **優先度変更**: Critical → Critical+ (P0+)
- **所要時間変更**: 3-4日 → 4-5日
- **テストケース変更**: 15件 → 21件

### 2025-11-26: Steam → Twitch変更、Issue分離
- **変更理由**: Steam OpenIDはSupabaseでネイティブサポートされず、カスタム実装が必要なためIssue #173へ分離
- **変更内容**:
  - Steam認証 → Issue #173 へ分離
  - Twitch OAuth追加（Supabase標準サポート）
  - Supabase OAuth設定完了マーク（Issue #133 で完了）
  - テストケース更新（Steam → Twitch）
- **関連Issue**: #133 (Supabase Auth基盤構築), #173 (Steam OpenID認証)
