# パスワードリセット機能実装 Issue

## 📝 概要

Supabase Auth のパスワードリセット機能を利用して、メールによるパスワードリセットフローを完全実装します。現在はUIの骨格のみ実装されており、実際のSupabase Auth連携とバックエンド処理が未実装の状態です。

## 🎯 目的

1. ユーザーがパスワードを忘れた際に、セルフサービスでパスワードリセットできる機能を提供する
2. Supabase Auth の標準機能を活用し、セキュアなパスワードリセットフローを実装する
3. ユーザーフレンドリーなUI/UXでパスワードリセット体験を提供する

## 🛠️ 技術要件

### 1. バックエンド実装
- **IAuthService.ResetPasswordAsync()** メソッドをインターフェースに追加
- **SupabaseAuthService.ResetPasswordAsync()** でSupabase Auth連携実装
- メールテンプレート設定とリンク生成ロジック
- パスワードリセットトークンの検証機能

### 2. UI/UX実装
- 現在のplaceholder実装をSupabase Auth連携に置き換え
- パスワードリセット成功時のメッセージ表示
- エラーハンドリングの改善
- パスワードリセット完了画面の実装

### 3. セキュリティ実装
- レート制限機能（同一メールアドレスへの連続リクエスト制限）
- トークン有効期限の設定
- セキュリティログの記録

## 📋 実装タスクリスト

### 1. インターフェース設計
- [ ] **IAuthService.ResetPasswordAsync()** メソッド定義
  ```csharp
  Task<AuthResult> ResetPasswordAsync(string email, CancellationToken cancellationToken = default);
  Task<AuthResult> ConfirmPasswordResetAsync(string token, string newPassword, CancellationToken cancellationToken = default);
  ```
- [ ] **AuthResult** 型にパスワードリセット専用レスポンス追加
- [ ] **AuthSettings** にパスワードリセット設定項目追加

### 2. SupabaseAuthService実装
- [ ] **ResetPasswordAsync()** でSupabase Auth API呼び出し実装
  ```csharp
  await _client.Auth.ResetPasswordForEmail(email, new ResetPasswordOptions
  {
      RedirectTo = "baketa://password-reset"
  });
  ```
- [ ] **ConfirmPasswordResetAsync()** でパスワード更新実装
- [ ] エラーハンドリングとログ記録
- [ ] レート制限機能の実装

### 3. UI実装改善
- [ ] **LoginViewModel.ExecuteForgotPasswordAsync()** の実装完了
  - placeholder の `Task.Delay(1000)` を実際のAPI呼び出しに置き換え
  - 成功時のメッセージ表示ロジック
  - リセットメール送信済み状態の管理
- [ ] **パスワードリセット完了画面** の新規実装
  - `PasswordResetCompletionViewModel` / `PasswordResetCompletionView`
  - 新しいパスワード入力とConfirmPassword
  - バリデーション（パスワード強度要件）
- [ ] **ナビゲーション統合**
  - `INavigationService.ShowPasswordResetCompletionAsync()`
  - Deep Link対応（`baketa://password-reset`）

### 4. Supabase設定
- [ ] **Email Templates** 設定
  - パスワードリセットメールテンプレートのカスタマイズ
  - 日本語対応とブランディング
- [ ] **Rate Limiting** 設定
  - 同一IPアドレス・メールアドレスからのリクエスト制限
- [ ] **Security Settings** 確認
  - トークン有効期限（推奨: 1時間）
  - Redirect URL許可リスト設定

### 5. セキュリティ実装
- [ ] **SecurityAuditLogger** 統合
  - パスワードリセット試行のログ記録
  - 不審なアクティビティの検知
- [ ] **InputValidator** 統合
  - メールアドレスの検証とサニタイゼーション
- [ ] **LoginAttemptTracker** 連携
  - パスワードリセット後のログイン試行状況追跡

### 6. テスト実装
- [ ] **SupabaseAuthService単体テスト**
  ```csharp
  [Fact]
  public async Task ResetPasswordAsync_WithValidEmail_ReturnsSuccess()
  [Fact] 
  public async Task ResetPasswordAsync_WithInvalidEmail_ReturnsFailure()
  [Fact]
  public async Task ResetPasswordAsync_WhenRateLimited_ReturnsRateLimitError()
  [Fact]
  public async Task ConfirmPasswordResetAsync_WithValidToken_UpdatesPassword()
  [Fact]
  public async Task ConfirmPasswordResetAsync_WithExpiredToken_ReturnsTokenExpiredError()
  ```
- [ ] **LoginViewModel テスト**
  ```csharp
  [Fact]
  public async Task ForgotPasswordCommand_WithValidEmail_CallsResetPasswordAsync()
  [Fact]
  public async Task ForgotPasswordCommand_WithInvalidEmail_ShowsValidationError()
  [Fact]
  public async Task ForgotPasswordCommand_WhenServiceFails_ShowsErrorMessage()
  ```
- [ ] **PasswordResetCompletionViewModel テスト**
  ```csharp
  [Fact]
  public async Task ConfirmResetCommand_WithValidPassword_CallsConfirmPasswordResetAsync()
  [Fact]
  public async Task ConfirmResetCommand_WithWeakPassword_ShowsValidationError()
  [Fact]
  public async Task ConfirmResetCommand_WithExpiredToken_ShowsTokenExpiredError()
  ```
- [ ] **統合テスト**
  ```csharp
  [Fact]
  public async Task PasswordResetFlow_EndToEnd_CompletesSuccessfully()
  [Fact]
  public async Task PasswordResetFlow_WithRateLimit_HandlesGracefully()
  ```

### 7. エラーハンドリング実装
- [ ] **AuthFailure** 型に専用エラーコード追加
  ```csharp
  public static class PasswordResetErrorCodes
  {
      public const string EmailNotFound = "email_not_found";
      public const string RateLimitExceeded = "rate_limit_exceeded";
      public const string TokenExpired = "token_expired";
      public const string TokenInvalid = "token_invalid";
      public const string WeakPassword = "weak_password";
  }
  ```
- [ ] **ユーザーフレンドリーエラーメッセージ** 実装
- [ ] **リトライ機能** とエクスポネンシャルバックオフ

## 🎨 UI/UX設計要件

### 1. パスワードリセット要求画面（既存LoginView拡張）
- メールアドレス入力フィールド（既存）
- 「パスワードをリセット」ボタン
- 送信済み状態の表示（「リセットメールを送信しました」）
- 再送信機能（レート制限付き）

### 2. パスワードリセット完了画面（新規実装）
- 新しいパスワード入力フィールド
- パスワード確認入力フィールド
- パスワード強度インジケーター
- 「パスワードを更新」ボタン
- 成功時のログイン画面への自動遷移

### 3. エラー表示
- ネットワークエラー時の適切なメッセージ
- レート制限時の待機時間表示
- トークン期限切れ時の再送信案内

## 📁 実装予定ファイル

### 新規作成ファイル
- `Baketa.UI/ViewModels/Auth/PasswordResetCompletionViewModel.cs`
- `Baketa.UI/Views/Auth/PasswordResetCompletionView.axaml`
- `tests/Baketa.Infrastructure.Tests/Auth/PasswordResetTests.cs`
- `tests/Baketa.UI.Tests/ViewModels/Auth/PasswordResetCompletionViewModelTests.cs`

### 修正予定ファイル
- `Baketa.Core/Abstractions/Auth/IAuthService.cs` - メソッド追加
- `Baketa.Core/Abstractions/Auth/AuthModels.cs` - エラーコード追加
- `Baketa.Core/Settings/AuthSettings.cs` - 設定項目追加
- `Baketa.Infrastructure/Auth/SupabaseAuthService.cs` - 実装追加
- `Baketa.UI/ViewModels/Auth/LoginViewModel.cs` - placeholder置き換え
- `Baketa.UI/Services/INavigationService.cs` - メソッド追加
- `Baketa.UI/Services/AvaloniaNavigationService.cs` - 実装追加

## 🚀 実装順序

### Phase 1: バックエンド基盤（2-3時間）
1. IAuthService インターフェース拡張
2. SupabaseAuthService 実装
3. 基本的な単体テスト

### Phase 2: UI実装（2-3時間）
1. LoginViewModel の placeholder 置き換え
2. PasswordResetCompletionViewModel/View 実装
3. ナビゲーション統合

### Phase 3: セキュリティとテスト（1-2時間）
1. セキュリティ機能統合
2. 包括的なテスト実装
3. エラーハンドリング改善

### Phase 4: Supabase設定とE2E（1時間）
1. Supabase Email Templates設定
2. Rate Limiting設定
3. E2Eテスト実行

## 🔗 依存関係

### 前提条件
- Issue #114（認証システム基盤）完了済み ✅
- Supabase Auth OAuth設定完了
- メール送信機能の動作確認

### 関連Issue
- Issue #114 - アカウント作成・認証システムの要件定義（前提）
- Issue #115 - アカウント作成・認証システム 詳細設計（参考）

## 💡 実装時の注意点

### セキュリティ考慮事項
- パスワードリセットトークンは一度だけ使用可能
- メールアドレスの存在チェック時の情報漏洩対策
- CSRF攻撃対策（Supabase Auth標準機能）
- ブルートフォース攻撃対策（レート制限）

### UX考慮事項
- パスワードリセット要求時は常に「送信しました」と表示（メールアドレス存在確認の防止）
- 明確なエラーメッセージとガイダンス
- パスワード要件の事前表示
- モバイル環境での使いやすさ

### テスト考慮事項
- メール送信のモック化
- Supabase Auth APIのモック設定
- 非同期処理のタイミング制御
- UIスレッド同期（Avalonia特有）

## 📊 完了条件

- [ ] すべてのテストが成功する
- [ ] パスワードリセットフローがE2Eで動作する
- [ ] セキュリティ要件を満たす
- [ ] コード品質基準（C# 12/.NET 8準拠）を満たす
- [ ] ドキュメント更新完了

## 🎯 期待される成果

1. **ユーザビリティ向上**: パスワード忘れ時のセルフサービス対応
2. **セキュリティ強化**: 適切なパスワードリセットフローの実装
3. **運用負荷軽減**: サポート問い合わせの削減
4. **システム完成度**: 認証システムの機能完全性

この実装により、Baketaの認証システムが完全な機能セットを持つことになり、エンタープライズレベルの要件を満たすことができます。