# Baketa 認証システム アーキテクチャ

*作成日: 2025年11月28日*

## 1. 概要

Baketaの認証システムは、Supabase Authを基盤としたクライアントサイド認証を実装しています。デスクトップアプリケーションとしての特性を考慮し、PKCEフロー、ローカルHTTPコールバックサーバー、Windows Credential Managerによるセキュアなトークン保存を組み合わせています。

### 1.1 設計原則

- **セキュリティファースト**: PKCEフロー、セキュアストレージ、トークン自動更新
- **ユーザー体験**: シームレスなOAuth認証、パスワード強度のリアルタイムフィードバック
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

## 8. 関連ドキュメント

- [Issue #167: ログインUI実装](../../issues/issue-167-login-ui.md)
- [Issue #168: トークン管理](../../issues/issue-168-token-management.md)
- [ReactiveUI Guide](../ui-system/reactiveui-guide.md)
