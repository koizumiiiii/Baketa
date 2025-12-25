# Baketa 認証システム アーキテクチャ

*作成日: 2025年11月28日*

## 1. 概要

Baketaの認証システムは、2つの認証基盤を実装しています：

1. **Supabase Auth**: ユーザー認証（Email/Password、OAuth）
2. **Patreon OAuth**: ライセンス連携認証（サブスクリプションプラン取得）

デスクトップアプリケーションとしての特性を考慮し、PKCEフロー、ローカルHTTPコールバックサーバー、カスタムURIスキーム（`baketa://`）、Windows Credential Managerによるセキュアなトークン保存を組み合わせています。

### 1.1 設計原則

- **セキュリティファースト**: PKCEフロー、CSRF保護、セキュアストレージ、トークン自動更新
- **ユーザー体験**: シームレスなOAuth認証、パスワード強度のリアルタイムフィードバック
- **ライセンス連携**: Patreon自動同期（30分間隔）、リアルタイムプラン反映
- **Clean Architecture準拠**: 認証ロジックの適切なレイヤー分離

## 2. アーキテクチャ図

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              UI Layer (Baketa.UI)                           │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────────────┐  │
│  │   LoginView     │  │   SignupView    │  │   AuthNavigationService     │  │
│  │   (.axaml)      │  │   (.axaml)      │  │                             │  │
│  └────────┬────────┘  └────────┬────────┘  └──────────────┬──────────────┘  │
│           │                    │                          │                 │
│  ┌────────▼────────┐  ┌────────▼────────┐                 │                 │
│  │ LoginViewModel  │  │ SignupViewModel │                 │                 │
│  │ (ReactiveUI)    │  │ (ReactiveUI)    │                 │                 │
│  └────────┬────────┘  └────────┬────────┘                 │                 │
└───────────┼───────────────────┼───────────────────────────┼─────────────────┘
            │                    │                          │
            ▼                    ▼                          ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Application Layer (Baketa.Application)              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                        IAuthenticationService                        │    │
│  │  - LoginAsync(email, password)                                       │    │
│  │  - SignupAsync(email, password)                                      │    │
│  │  - LoginWithProviderAsync(provider)                                  │    │
│  │  - LogoutAsync()                                                     │    │
│  │  - RefreshTokenAsync()                                               │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                     IPasswordStrengthValidator                       │    │
│  │  - Validate(password) -> PasswordStrengthResult                      │    │
│  │  - GetStrengthLevel(password) -> StrengthLevel                       │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
            │                    │
            ▼                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                      Infrastructure Layer (Baketa.Infrastructure)           │
│  ┌──────────────────────┐  ┌──────────────────────┐  ┌──────────────────┐   │
│  │ SupabaseAuthClient   │  │ OAuthCallbackServer  │  │ TokenExpiration  │   │
│  │                      │  │ (localhost HTTP)     │  │ Handler          │   │
│  │ - Supabase REST API  │  │ - PKCE Flow          │  │ - Auto Refresh   │   │
│  │ - GoTrue認証         │  │ - Code Exchange      │  │ - Expiry Monitor │   │
│  └──────────┬───────────┘  └──────────┬───────────┘  └────────┬─────────┘   │
│             │                         │                       │             │
│             ▼                         ▼                       ▼             │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                    ISecureTokenStorage                              │    │
│  │  - SaveTokenAsync(key, token)                                       │    │
│  │  - GetTokenAsync(key) -> token                                      │    │
│  │  - DeleteTokenAsync(key)                                            │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                  Platform Layer (Baketa.Infrastructure.Platform)            │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                   WindowsCredentialStorage                          │    │
│  │  - Windows Credential Manager API (CredRead/CredWrite/CredDelete)   │    │
│  │  - P/Invoke: advapi32.dll                                           │    │
│  │  - CRED_TYPE_GENERIC credentials                                    │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           External Services                                 │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                         Supabase Auth                               │    │
│  │  - GoTrue認証サーバー                                               │    │
│  │  - OAuth Provider統合 (Google, Discord, Twitch)                     │    │
│  │  - JWT Token発行                                                    │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                        GitHub Pages                                 │    │
│  │  - メール確認完了ページ                                             │    │
│  │  - パスワードリセットページ                                         │    │
│  │  - 利用規約・プライバシーポリシー                                   │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
```

## 3. コンポーネント詳細

### 3.1 UI Layer

#### LoginView / LoginViewModel
- **ファイル**: `Baketa.UI/Views/Auth/LoginView.axaml`, `Baketa.UI/ViewModels/Auth/LoginViewModel.cs`
- **責務**: ユーザーログインUI、バリデーション、エラー表示
- **機能**:
  - Email/Password入力フォーム
  - OAuthプロバイダーボタン（Google, Discord, Twitch）
  - リアルタイムバリデーション（ReactiveUI.Validation）
  - ローディング状態表示

#### SignupView / SignupViewModel
- **ファイル**: `Baketa.UI/Views/Auth/SignupView.axaml`, `Baketa.UI/ViewModels/Auth/SignupViewModel.cs`
- **責務**: ユーザー登録UI、パスワード強度表示
- **機能**:
  - Email/Password入力フォーム
  - パスワード強度インジケーター（弱い/普通/強い）
  - 利用規約・プライバシーポリシーへのリンク
  - パスワード確認フィールド

### 3.2 Application Layer

#### IAuthenticationService
- **ファイル**: `Baketa.Core/Abstractions/Auth/IAuthenticationService.cs`
- **責務**: 認証操作の抽象化
- **メソッド**:
  ```csharp
  Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct);
  Task<AuthResult> SignupAsync(string email, string password, CancellationToken ct);
  Task<AuthResult> LoginWithProviderAsync(OAuthProvider provider, CancellationToken ct);
  Task LogoutAsync(CancellationToken ct);
  Task<AuthResult> RefreshTokenAsync(CancellationToken ct);
  ```

#### IPasswordStrengthValidator
- **ファイル**: `Baketa.Core/Abstractions/Auth/IPasswordStrengthValidator.cs`
- **責務**: パスワード強度検証
- **検証ルール**:
  - 最小8文字
  - 大文字・小文字・数字・記号のうち3種類以上
  - 一般的な脆弱パスワードのブラックリストチェック

### 3.3 Infrastructure Layer

#### SupabaseAuthClient
- **ファイル**: `Baketa.Infrastructure/Auth/SupabaseAuthClient.cs`
- **責務**: Supabase Auth APIとの通信
- **機能**:
  - REST API呼び出し（GoTrue）
  - JWTトークン処理
  - エラーハンドリング

#### OAuthCallbackServer
- **ファイル**: `Baketa.Infrastructure/Auth/OAuthCallbackServer.cs`
- **責務**: OAuthコールバック受信
- **機能**:
  - ローカルHTTPサーバー起動（localhost:xxxxx）
  - PKCEコードの受信
  - Authorization Code → Token交換

#### TokenExpirationHandler
- **ファイル**: `Baketa.Infrastructure/Auth/TokenExpirationHandler.cs`
- **責務**: トークン有効期限管理
- **機能**:
  - トークン有効期限監視
  - 期限切れ前の自動リフレッシュ
  - リフレッシュ失敗時の再認証要求

### 3.4 Platform Layer

#### WindowsCredentialStorage
- **ファイル**: `Baketa.Infrastructure.Platform/Windows/Credentials/WindowsCredentialStorage.cs`
- **責務**: Windows Credential Managerによるセキュアストレージ
- **P/Invoke**:
  - `CredRead`: 資格情報読み取り
  - `CredWrite`: 資格情報書き込み
  - `CredDelete`: 資格情報削除
  - `CredFree`: メモリ解放

## 4. 認証フロー

### 4.1 Email/Password認証フロー

```
┌─────────┐      ┌─────────────┐      ┌───────────────┐      ┌──────────────┐
│  User   │      │  LoginView  │      │ AuthService   │      │ Supabase     │
└────┬────┘      └──────┬──────┘      └───────┬───────┘      └──────┬───────┘
     │                  │                     │                     │
     │ Enter email/pass │                     │                     │
     │─────────────────>│                     │                     │
     │                  │                     │                     │
     │                  │ LoginAsync()        │                     │
     │                  │────────────────────>│                     │
     │                  │                     │                     │
     │                  │                     │ POST /auth/token    │
     │                  │                     │────────────────────>│
     │                  │                     │                     │
     │                  │                     │ JWT Tokens          │
     │                  │                     │<────────────────────│
     │                  │                     │                     │
     │                  │                     │ Store in Credential │
     │                  │                     │ Manager             │
     │                  │                     │                     │
     │                  │ AuthResult(success) │                     │
     │                  │<────────────────────│                     │
     │                  │                     │                     │
     │ Navigate to Main │                     │                     │
     │<─────────────────│                     │                     │
```

### 4.2 OAuth認証フロー (PKCE)

```
┌─────────┐   ┌─────────────┐   ┌───────────────┐   ┌───────────────┐   ┌──────────┐
│  User   │   │  LoginView  │   │ OAuthCallback │   │ AuthService   │   │ Supabase │
└────┬────┘   └──────┬──────┘   └───────┬───────┘   └───────┬───────┘   └────┬─────┘
     │               │                  │                   │                │
     │ Click Google  │                  │                   │                │
     │──────────────>│                  │                   │                │
     │               │                  │                   │                │
     │               │ Generate PKCE    │                   │                │
     │               │ (code_verifier,  │                   │                │
     │               │  code_challenge) │                   │                │
     │               │                  │                   │                │
     │               │ Start callback   │                   │                │
     │               │ server           │                   │                │
     │               │─────────────────>│                   │                │
     │               │                  │                   │                │
     │               │ Open browser     │                   │                │
     │               │ (OAuth URL)      │                   │                │
     │<──────────────│                  │                   │                │
     │               │                  │                   │                │
     │ Authenticate  │                  │                   │                │
     │ with Google   │                  │                   │                │
     │───────────────────────────────────────────────────────────────────────>│
     │               │                  │                   │                │
     │               │                  │ Redirect with     │                │
     │               │                  │ auth code         │                │
     │               │                  │<───────────────────────────────────│
     │               │                  │                   │                │
     │               │                  │ Exchange code     │                │
     │               │                  │ for tokens        │                │
     │               │                  │──────────────────>│                │
     │               │                  │                   │                │
     │               │                  │                   │ POST /token    │
     │               │                  │                   │ (code_verifier)│
     │               │                  │                   │───────────────>│
     │               │                  │                   │                │
     │               │                  │                   │ JWT Tokens     │
     │               │                  │                   │<───────────────│
     │               │                  │                   │                │
     │               │ AuthResult       │                   │                │
     │               │<─────────────────────────────────────│                │
     │               │                  │                   │                │
     │ Navigate Main │                  │                   │                │
     │<──────────────│                  │                   │                │
```

## 5. セキュリティ考慮事項

### 5.1 トークン保存

- **Windows Credential Manager使用**: DPAPIによる暗号化
- **メモリ内保持の最小化**: 必要時のみ復号
- **ログアウト時の完全削除**: 資格情報の即座削除

### 5.2 OAuth PKCE

- **code_verifier**: 高エントロピーランダム文字列（43-128文字）
- **code_challenge**: SHA256(code_verifier) のBase64URL
- **state**: CSRF防止トークン

### 5.3 パスワードポリシー

- 最小8文字
- 3種類以上の文字種（大文字/小文字/数字/記号）
- 一般的な脆弱パスワードのブラックリスト
  - "password", "12345678", "qwerty" など

## 6. 外部依存

### 6.1 Supabase

- **Project URL**: 環境設定で指定
- **Anon Key**: 公開APIキー（クライアント認証用）
- **サービス**: GoTrue認証、OAuth Provider統合

### 6.2 GitHub Pages

- **URL**: `https://koizumiiiii.github.io/Baketa/pages/`
- **ホスト**: メール確認、パスワードリセット、法的ページ

## 7. テスト

### 7.1 単体テスト

- `WindowsCredentialStorageTests.cs`: 21テストケース
  - 保存・読み込みテスト
  - 削除テスト
  - 存在確認テスト
  - 並行性テスト
  - サイズ制限テスト

### 7.2 統合テスト

- OAuthコールバックサーバーテスト
- トークンリフレッシュフローテスト

## 8. Patreon OAuth認証システム

### 8.1 概要

Patreon OAuth統合により、PatreonサブスクリプションとBaketaライセンスプランを連携します。

### 8.2 アーキテクチャ

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              UI Layer (Baketa.UI)                           │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                   AccountSettingsViewModel                          │    │
│  │  - ConnectPatreonCommand: Patreon連携開始                           │    │
│  │  - DisconnectPatreonCommand: Patreon連携解除                        │    │
│  │  - PatreonUserName: 連携中のPatreonユーザー名                       │    │
│  │  - IsPatreonConnected: 連携状態                                     │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                      Infrastructure Layer (Baketa.Infrastructure)           │
│  ┌──────────────────────┐  ┌──────────────────────┐  ┌──────────────────┐   │
│  │ PatreonOAuthService  │  │ PatreonCallbackHandler│  │ PatreonSync      │   │
│  │                      │  │                      │  │ HostedService    │   │
│  │ - StartAuthFlowAsync │  │ - HandleCallbackAsync│  │ - 30分間隔同期   │   │
│  │ - ExchangeCodeAsync  │  │ - CSRF検証           │  │ - ライセンス更新 │   │
│  │ - SyncLicenseAsync   │  │ - URIパース          │  │ - バックグラウンド│   │
│  │ - SaveCredentialsAsync│  │                      │  │                  │   │
│  └──────────┬───────────┘  └──────────┬───────────┘  └────────┬─────────┘   │
│             │                         │                       │             │
│             ▼                         ▼                       ▼             │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                     PatreonCredentials                              │    │
│  │  - AccessToken: アクセストークン                                    │    │
│  │  - RefreshToken: リフレッシュトークン                               │    │
│  │  - PatreonUserId: PatreonユーザーID                                 │    │
│  │  - PatreonUserName: Patreonユーザー名                               │    │
│  │  - CurrentTier: 現在のサブスクリプションティア                      │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           External Services                                 │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                         Patreon API                                 │    │
│  │  - OAuth 2.0認証エンドポイント                                      │    │
│  │  - Identity API（ユーザー情報取得）                                 │    │
│  │  - Memberships API（サブスクリプション状態）                        │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 8.3 コンポーネント詳細

#### PatreonOAuthService
- **ファイル**: `Baketa.Infrastructure/License/Services/PatreonOAuthService.cs`
- **責務**: Patreon OAuth認証フロー管理
- **機能**:
  - OAuth認証URL生成（CSRF state付き）
  - Authorization Code → Token交換
  - トークンリフレッシュ
  - ユーザー情報・メンバーシップ取得
  - ライセンス同期

#### PatreonCallbackHandler
- **ファイル**: `Baketa.Infrastructure/License/Services/PatreonCallbackHandler.cs`
- **責務**: URIスキームコールバック処理
- **機能**:
  - `baketa://patreon/callback?code=xxx&state=yyy` パース
  - CSRF state検証
  - PatreonOAuthServiceへの委譲

#### PatreonSyncHostedService
- **ファイル**: `Baketa.Infrastructure/License/Services/PatreonSyncHostedService.cs`
- **責務**: バックグラウンド自動同期
- **機能**:
  - 30分間隔のライセンス状態同期
  - アプリ起動時の初回同期（5秒遅延）
  - エラーハンドリング・リトライ

### 8.4 Patreon OAuth認証フロー

```
┌─────────┐   ┌─────────────┐   ┌───────────────┐   ┌───────────────┐   ┌──────────┐
│  User   │   │  Settings   │   │ PatreonOAuth  │   │ Patreon       │   │ Callback │
│         │   │  View       │   │ Service       │   │ API           │   │ Handler  │
└────┬────┘   └──────┬──────┘   └───────┬───────┘   └───────┬───────┘   └────┬─────┘
     │               │                  │                   │                │
     │ Click         │                  │                   │                │
     │ "Patreon連携" │                  │                   │                │
     │──────────────>│                  │                   │                │
     │               │                  │                   │                │
     │               │ StartAuthFlow    │                   │                │
     │               │ Async()          │                   │                │
     │               │─────────────────>│                   │                │
     │               │                  │                   │                │
     │               │                  │ Generate state    │                │
     │               │                  │ (CSRF token)      │                │
     │               │                  │                   │                │
     │               │                  │ Build OAuth URL   │                │
     │               │                  │ with state        │                │
     │               │                  │                   │                │
     │               │ Open browser     │                   │                │
     │<──────────────│ (OAuth URL)      │                   │                │
     │               │                  │                   │                │
     │ Authenticate  │                  │                   │                │
     │ with Patreon  │                  │                   │                │
     │───────────────────────────────────────────────────────>│                │
     │               │                  │                   │                │
     │               │                  │                   │ Redirect to    │
     │               │                  │                   │ baketa://      │
     │<────────────────────────────────────────────────────────────────────────│
     │               │                  │                   │                │
     │               │                  │                   │ App receives   │
     │               │                  │                   │ URI scheme     │
     │               │                  │                   │────────────────>│
     │               │                  │                   │                │
     │               │                  │ HandleCallback    │                │
     │               │                  │ (code, state)     │                │
     │               │                  │<───────────────────────────────────│
     │               │                  │                   │                │
     │               │                  │ Verify state      │                │
     │               │                  │ (CSRF check)      │                │
     │               │                  │                   │                │
     │               │                  │ Exchange code     │                │
     │               │                  │ for tokens        │                │
     │               │                  │──────────────────>│                │
     │               │                  │                   │                │
     │               │                  │ Access + Refresh  │                │
     │               │                  │ Tokens            │                │
     │               │                  │<──────────────────│                │
     │               │                  │                   │                │
     │               │                  │ Get user info     │                │
     │               │                  │ + memberships     │                │
     │               │                  │──────────────────>│                │
     │               │                  │                   │                │
     │               │                  │ User data +       │                │
     │               │                  │ subscription tier │                │
     │               │                  │<──────────────────│                │
     │               │                  │                   │                │
     │               │                  │ Save credentials  │                │
     │               │                  │ + update license  │                │
     │               │                  │                   │                │
     │               │ Success toast    │                   │                │
     │<──────────────│ "Patreon連携完了"│                   │                │
```

### 8.5 URIスキーム登録

Patreon OAuth コールバックを受信するには、`baketa://` URIスキームをWindowsに登録する必要があります。

#### 登録スクリプト

```powershell
# scripts/register-uri-scheme.ps1
.\register-uri-scheme.ps1 -ExePath "C:\Path\To\Baketa.exe"

# または自動検出
.\register-uri-scheme.ps1

# 解除
.\register-uri-scheme.ps1 -Unregister
```

#### レジストリ構造

```
HKCU\Software\Classes\baketa
├── (Default) = "URL:Baketa Translation Overlay Protocol"
├── URL Protocol = ""
├── DefaultIcon
│   └── (Default) = "C:\Path\To\Baketa.exe,0"
└── shell
    └── open
        └── command
            └── (Default) = "\"C:\Path\To\Baketa.exe\" \"%1\""
```

### 8.6 設定

```json
// appsettings.Local.json (機密情報)
{
  "Patreon": {
    "ClientId": "YOUR_PATREON_CLIENT_ID",
    "ClientSecret": "YOUR_PATREON_CLIENT_SECRET",
    "RedirectUri": "baketa://patreon/callback",
    "CampaignId": "YOUR_CAMPAIGN_ID"
  }
}
```

### 8.7 セキュリティ考慮事項

#### CSRF保護
- **state パラメータ**: 暗号学的ランダム文字列
- **検証**: コールバック時にセッション内stateと照合
- **有効期限**: 認証フロー開始から5分

#### トークン保存
- **アクセストークン**: Windows Credential Manager（DPAPI暗号化）
- **リフレッシュトークン**: Windows Credential Manager（DPAPI暗号化）

## 9. Patreon リレーサーバー

*更新日: 2025年12月25日*

Patreon OAuth認証のセキュリティを強化するため、Cloudflare Workers上でリレーサーバーを運用しています。

### 9.1 概要

リレーサーバーは以下の目的で導入されました：

1. **クライアントシークレット保護**: Patreonクライアントシークレットをサーバー側で管理
2. **トークン隠蔽**: アクセストークンをクライアントに渡さず、セッショントークンで管理
3. **セキュリティ強化**: タイミング攻撃対策、redirect_uri検証等

### 9.2 アーキテクチャ

```
┌─────────────────┐     ┌────────────────────────┐     ┌─────────────────┐
│   Baketa.UI     │     │   Cloudflare Workers   │     │   Patreon API   │
│   (C# Client)   │     │   (Relay Server)       │     │                 │
└────────┬────────┘     └───────────┬────────────┘     └────────┬────────┘
         │                          │                           │
         │ POST /api/patreon/exchange                          │
         │ (code, redirect_uri)    │                           │
         │─────────────────────────>│                           │
         │                          │                           │
         │                          │ POST /api/oauth2/token    │
         │                          │ (code, client_secret)     │
         │                          │──────────────────────────>│
         │                          │                           │
         │                          │ access_token              │
         │                          │<──────────────────────────│
         │                          │                           │
         │                          │ GET /api/oauth2/v2/identity
         │                          │──────────────────────────>│
         │                          │                           │
         │                          │ user info, memberships    │
         │                          │<──────────────────────────│
         │                          │                           │
         │                          │ Store in KV:              │
         │                          │ session_token → {         │
         │                          │   access_token,           │
         │                          │   refresh_token,          │
         │                          │   user_id                 │
         │                          │ }                         │
         │                          │                           │
         │ session_token, plan,     │                           │
         │ user_info               │                           │
         │<─────────────────────────│                           │
         │                          │                           │
```

### 9.3 エンドポイント

| エンドポイント | メソッド | 説明 |
|---------------|----------|------|
| `/health` | GET | ヘルスチェック（認証不要） |
| `/api/patreon/exchange` | POST | OAuth認証コード交換＋セッション発行 |
| `/api/patreon/license-status` | GET | ライセンス状態取得（キャッシュ対応） |
| `/api/patreon/revoke` | POST | セッション無効化 |
| `/api/session/validate` | POST | セッション検証 |
| `/oauth/token` | POST | トークン交換（低レベルAPI） |
| `/oauth/refresh` | POST | トークンリフレッシュ |

### 9.4 セキュリティ機能

#### タイミング攻撃対策
```typescript
function timingSafeCompare(a: string, b: string): boolean {
  // 文字列長に関わらず一定時間で比較
  let result = 0;
  for (let i = 0; i < Math.max(a.length, b.length); i++) {
    result |= a.charCodeAt(i % a.length) ^ b.charCodeAt(i % b.length);
  }
  return result === 0 && a.length === b.length;
}
```

#### redirect_uri ホワイトリスト
```typescript
// 本番環境では ALLOWED_REDIRECT_URIS で許可するURIを制限
function validateRedirectUri(redirectUri: string, env: Env): boolean {
  if (env.ENVIRONMENT === 'production' && !env.ALLOWED_REDIRECT_URIS) {
    return false;  // 本番では必須
  }
  const allowedUris = env.ALLOWED_REDIRECT_URIS.split(',');
  return allowedUris.includes(redirectUri);
}
```

#### 本番環境チェック
- `ENVIRONMENT=production` 時は `API_KEY` と `ALLOWED_REDIRECT_URIS` が必須
- 開発環境では警告を出力しつつ許可

#### メンバーシップキャッシュ
- Patreon Identity APIへのリクエストを5分間キャッシュ
- レートリミット対策とレスポンス高速化

### 9.5 環境変数

| 変数名 | 必須 | 説明 |
|--------|------|------|
| `PATREON_CLIENT_ID` | ✅ | Patreon OAuthクライアントID |
| `PATREON_CLIENT_SECRET` | ✅ | Patreon OAuthクライアントシークレット |
| `API_KEY` | 本番のみ | クライアント認証用APIキー |
| `ALLOWED_REDIRECT_URIS` | 本番のみ | 許可するredirect_uri（カンマ区切り） |
| `ENVIRONMENT` | - | `production` で本番モード |
| `ALLOWED_ORIGINS` | - | CORS許可オリジン（デフォルト: `*`） |
| `SESSIONS` | ✅ | KV Namespace（セッション保存用） |

### 9.6 デプロイ

#### 初回セットアップ

```bash
cd relay-server

# KV Namespaceを作成
npx wrangler kv:namespace create "SESSIONS"
# 出力されたIDをwrangler.tomlに設定

# シークレット設定
npx wrangler secret put PATREON_CLIENT_ID
npx wrangler secret put PATREON_CLIENT_SECRET
npx wrangler secret put API_KEY

# 本番環境用
npx wrangler secret put ENVIRONMENT  # → "production"
npx wrangler secret put ALLOWED_REDIRECT_URIS  # → "http://127.0.0.1:18234/oauth/callback"
```

#### デプロイ

```bash
cd relay-server
npx wrangler deploy
```

### 9.7 クライアント設定

C#クライアント（`PatreonOAuthService`）は以下の設定を使用：

```json
// appsettings.json
{
  "Patreon": {
    "RelayServerUrl": "https://baketa-relay.suke009.workers.dev",
    "ApiKey": "YOUR_API_KEY"
  }
}
```

### 9.8 ソースコード

- **リレーサーバー**: `relay-server/src/index.ts`
- **設定**: `relay-server/wrangler.toml`
- **C#クライアント**: `Baketa.Infrastructure/License/Services/PatreonOAuthService.cs`

## 10. 関連ドキュメント

- [Issue #167: ログインUI実装](../../issues/issue-167-login-ui.md)
- [Issue #168: トークン管理](../../issues/issue-168-token-management.md)
- [Issue #233: Patreon OAuth統合](../../issues/issue-233-patreon-integration.md)
- [Issue #234: Patreonリレーサーバー実装](https://github.com/koizumiiiii/Baketa/issues/234)
- [ReactiveUI Guide](../ui-system/reactiveui-guide.md)
