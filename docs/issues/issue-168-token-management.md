# Issue #168: トークン管理と永続化

**優先度**: 🔴 Critical+ (P0+)
**所要時間**: 2-3日
**Epic**: ユーザー認証システム
**ラベル**: `priority: critical`, `epic: authentication`, `type: feature`, `layer: infrastructure`, `security: high`

---

## 概要

ログイン成功後に取得したSupabase認証トークン（Access Token / Refresh Token）を安全に保存・管理し、アプリ再起動時に自動ログインを実現します。Windows Credential Managerを活用したセキュアなトークン保存機構を実装します。

---

## 背景・目的

### 現状の課題（#167完了後）
- ログイン後、アプリを再起動するとログイン状態が失われる
- トークンをメモリ上にのみ保持しており、永続化されていない
- トークン有効期限切れ時の自動更新ができない

### 目指す状態
- ログイン後、トークンをWindows Credential Managerに安全に保存
- アプリ再起動時に保存済みトークンで自動ログイン
- トークン期限切れ時に自動的にRefresh Tokenで更新
- ログアウト時にトークンを完全削除

---

## スコープ

### 実装タスク

#### 1. 認証情報ストレージの抽象化
- [ ] **`ICredentialStorage` インターフェース定義**（Baketa.Core）
  - `SaveCredentialAsync()`: 認証情報の保存
  - `LoadCredentialAsync()`: 認証情報の読み込み
  - `DeleteCredentialAsync()`: 認証情報の削除
  - `ExistsAsync()`: 認証情報の存在確認

- [ ] **`ITokenRefreshService` インターフェース定義**（Baketa.Core）
  - `RefreshAccessTokenAsync()`: Access Tokenの更新
  - `IsTokenExpiredAsync()`: トークン有効期限の確認

#### 2. Windows Credential Manager統合
- [ ] **`WindowsCredentialStorage` 実装**（Baketa.Infrastructure.Platform）
  - Windows Credential Manager APIの利用
  - P/Invoke宣言（CredRead, CredWrite, CredDelete）
  - 暗号化された認証情報の保存・読み込み

- [ ] **NuGetパッケージ導入**（オプション）
  - `CredentialManagement` (https://github.com/AdysTech/CredentialManager)
  - または標準P/Invokeで実装

#### 3. トークン保存・読み込みロジック
- [ ] **ログイン成功時のトークン保存**
  ```csharp
  // SupabaseAuthenticationService.cs
  public async Task<AuthenticationResult> SignInAsync(string email, string password, ...)
  {
      var result = await _supabaseClient.Auth.SignIn(email, password);
      if (result.User != null)
      {
          // トークンを保存
          await _credentialStorage.SaveCredentialAsync(new AuthCredential
          {
              AccessToken = result.AccessToken,
              RefreshToken = result.RefreshToken,
              ExpiresAt = result.ExpiresAt
          });
      }
      return result;
  }
  ```

- [ ] **アプリ起動時の自動ログイン**
  ```csharp
  // App.axaml.cs OnStartup()
  public override async void OnStartup(StartupEventArgs e)
  {
      var credential = await _credentialStorage.LoadCredentialAsync();
      if (credential != null)
      {
          // 保存済みトークンでログイン試行
          var isValid = await _authService.ValidateTokenAsync(credential.AccessToken);
          if (isValid)
          {
              // MainWindowを表示
              ShowMainWindow();
              return;
          }
      }

      // トークンがないか無効 → LoginViewを表示
      ShowLoginView();
  }
  ```

#### 4. トークン自動更新
- [ ] **`TokenRefreshService` 実装**（Baketa.Application）
  - バックグラウンドでトークン有効期限を監視
  - 期限切れ前（5分前）に自動更新
  - 更新失敗時の再ログイン促進

- [ ] **タイマーベースの監視**
  ```csharp
  private Timer _tokenRefreshTimer;

  public void StartTokenMonitoring()
  {
      _tokenRefreshTimer = new Timer(CheckAndRefreshToken, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
  }

  private async void CheckAndRefreshToken(object? state)
  {
      var credential = await _credentialStorage.LoadCredentialAsync();
      if (credential != null && credential.ExpiresAt < DateTime.UtcNow.AddMinutes(5))
      {
          // トークン更新
          var newToken = await _supabaseClient.Auth.RefreshSession(credential.RefreshToken);
          await _credentialStorage.SaveCredentialAsync(newToken);
      }
  }
  ```

#### 5. ログアウト時のクリーンアップ
- [ ] **トークン削除ロジック**
  ```csharp
  public async Task SignOutAsync()
  {
      // Supabaseセッション終了
      await _supabaseClient.Auth.SignOut();

      // ローカル保存のトークンを削除
      await _credentialStorage.DeleteCredentialAsync();

      // ログイン画面へ遷移
      _navigationService.NavigateToLogin();
  }
  ```

#### 6. セキュリティテスト
- [ ] **トークン暗号化の確認**
  - Windows Credential Managerに保存されたトークンが暗号化されていることを確認
  - 他のユーザーアカウントから読み込めないことを確認

- [ ] **トークン漏洩対策**
  - メモリダンプからトークンが読み取れないことを確認（SecureString使用検討）
  - ログファイルにトークンが出力されないことを確認

#### 7. テスト実装
- [ ] **`WindowsCredentialStorageTests.cs` 作成**（xUnit）
  - 保存・読み込みテスト (5ケース)
  - 削除テスト (2ケース)
  - 存在確認テスト (3ケース)

- [ ] **`TokenRefreshServiceTests.cs` 作成**（xUnit + Moq）
  - トークン更新テスト (5ケース)
  - 期限チェックテスト (3ケース)
  - **並列制御テスト** (5ケース) ← 追加
    - 複数スレッドからの同時更新テスト
    - ダブルチェックロックの検証
    - 進行中タスクの待機テスト

- [ ] **`FileTokenAuditLoggerTests.cs` 作成**（xUnit） ← 追加
  - ログ書き込みテスト (4ケース)
  - ログファイル作成テスト (2ケース)
  - エラー時の継続テスト (2ケース)

- [ ] **`TokenExpirationHandlerTests.cs` 作成**（xUnit + Moq） ← 追加
  - 失効処理フローテスト (5ケース)
  - HTTP 401検出テスト (3ケース)
  - クリーンアップテスト (3ケース)

---

## 技術仕様

### ICredentialStorage インターフェース

```csharp
namespace Baketa.Core.Abstractions.Authentication;

/// <summary>
/// 認証情報（トークン）の安全な保存・読み込みを提供するストレージ
/// </summary>
public interface ICredentialStorage
{
    /// <summary>認証情報を保存</summary>
    Task SaveCredentialAsync(AuthCredential credential, CancellationToken cancellationToken = default);

    /// <summary>認証情報を読み込み</summary>
    Task<AuthCredential?> LoadCredentialAsync(CancellationToken cancellationToken = default);

    /// <summary>認証情報を削除</summary>
    Task DeleteCredentialAsync(CancellationToken cancellationToken = default);

    /// <summary>認証情報が存在するか確認</summary>
    Task<bool> ExistsAsync(CancellationToken cancellationToken = default);
}

/// <summary>認証情報</summary>
public record AuthCredential
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public string? UserId { get; init; }
    public string? Email { get; init; }
}
```

---

### WindowsCredentialStorage 実装

```csharp
namespace Baketa.Infrastructure.Platform.Windows.Authentication;

/// <summary>
/// Windows Credential Managerを使用した認証情報ストレージ
/// </summary>
public class WindowsCredentialStorage : ICredentialStorage
{
    private const string TargetName = "Baketa_SupabaseAuth";

    public Task SaveCredentialAsync(AuthCredential credential, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(credential);

        using var cred = new Credential
        {
            Target = TargetName,
            Username = credential.Email ?? "user",
            Password = json,
            Type = CredentialType.Generic,
            PersistanceType = PersistanceType.LocalComputer
        };

        cred.Save();
        return Task.CompletedTask;
    }

    public Task<AuthCredential?> LoadCredentialAsync(CancellationToken cancellationToken = default)
    {
        var cred = new Credential { Target = TargetName };
        if (!cred.Load())
        {
            return Task.FromResult<AuthCredential?>(null);
        }

        var credential = JsonSerializer.Deserialize<AuthCredential>(cred.Password);
        return Task.FromResult(credential);
    }

    public Task DeleteCredentialAsync(CancellationToken cancellationToken = default)
    {
        var cred = new Credential { Target = TargetName };
        cred.Delete();
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        var cred = new Credential { Target = TargetName };
        var exists = cred.Exists();
        return Task.FromResult(exists);
    }
}
```

---

### TokenRefreshService 実装（並列制御対応）

```csharp
namespace Baketa.Application.Services.Authentication;

/// <summary>
/// トークン自動更新サービス（並列制御対応）
/// </summary>
public class TokenRefreshService : ITokenRefreshService, IDisposable
{
    private readonly ICredentialStorage _credentialStorage;
    private readonly IAuthenticationService _authService;
    private readonly ITokenAuditLogger _auditLogger;
    private readonly ILogger<TokenRefreshService> _logger;
    private Timer? _refreshTimer;

    // 並列制御用
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private Task<AuthCredential?>? _ongoingRefreshTask;

    public TokenRefreshService(
        ICredentialStorage credentialStorage,
        IAuthenticationService authService,
        ITokenAuditLogger auditLogger,
        ILogger<TokenRefreshService> logger)
    {
        _credentialStorage = credentialStorage;
        _authService = authService;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public void StartMonitoring()
    {
        _refreshTimer = new Timer(CheckAndRefreshToken, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        _logger.LogInformation("トークン監視を開始しました");
    }

    private async void CheckAndRefreshToken(object? state)
    {
        try
        {
            var credential = await _credentialStorage.LoadCredentialAsync();
            if (credential == null) return;

            // 期限切れ5分前に更新
            if (credential.ExpiresAt < DateTime.UtcNow.AddMinutes(5))
            {
                await RefreshTokenWithLockAsync(credential);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "トークン更新エラー");
        }
    }

    /// <summary>
    /// 並列制御付きトークン更新
    /// </summary>
    public async Task<AuthCredential?> RefreshTokenWithLockAsync(
        AuthCredential currentCredential,
        CancellationToken cancellationToken = default)
    {
        // 既に更新中の場合は、その結果を待つ
        if (_ongoingRefreshTask != null && !_ongoingRefreshTask.IsCompleted)
        {
            _logger.LogDebug("既に別のスレッドがトークンを更新中です。完了を待機します");
            return await _ongoingRefreshTask;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // ダブルチェック: ロック取得中に他のスレッドが更新した可能性
            var latestCredential = await _credentialStorage.LoadCredentialAsync(cancellationToken)
                .ConfigureAwait(false);

            if (latestCredential != null &&
                latestCredential.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            {
                _logger.LogDebug("既に他のスレッドがトークンを更新済みです");
                return latestCredential;
            }

            // トークン更新タスクを開始
            _ongoingRefreshTask = RefreshTokenInternalAsync(currentCredential, cancellationToken);
            return await _ongoingRefreshTask;
        }
        finally
        {
            _refreshLock.Release();
            _ongoingRefreshTask = null;
        }
    }

    /// <summary>
    /// トークン更新の実装（並列制御の内側）
    /// </summary>
    private async Task<AuthCredential?> RefreshTokenInternalAsync(
        AuthCredential currentCredential,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("トークンを更新します（有効期限: {ExpiresAt}）", currentCredential.ExpiresAt);

        try
        {
            var result = await _authService.RefreshTokenAsync(
                currentCredential.RefreshToken,
                cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess && result.Credential != null)
            {
                await _credentialStorage.SaveCredentialAsync(
                    result.Credential,
                    cancellationToken).ConfigureAwait(false);

                // 監査ログ記録
                await _auditLogger.LogTokenRefreshedAsync(
                    result.Credential.UserId ?? "unknown",
                    currentCredential.ExpiresAt,
                    result.Credential.ExpiresAt).ConfigureAwait(false);

                _logger.LogInformation("トークン更新成功");
                return result.Credential;
            }
            else
            {
                _logger.LogWarning("トークン更新失敗: {Error}", result.ErrorMessage);

                // 監査ログ記録
                await _auditLogger.LogTokenValidationFailedAsync(
                    $"Refresh failed: {result.ErrorMessage}").ConfigureAwait(false);

                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "トークン更新中に例外が発生しました");
            await _auditLogger.LogTokenValidationFailedAsync(
                $"Exception during refresh: {ex.Message}").ConfigureAwait(false);
            throw;
        }
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
        _refreshLock?.Dispose();
    }
}
```

**並列制御のポイント**:
- `SemaphoreSlim` で複数スレッドからの同時アクセスを制御
- ダブルチェックロックパターンで不要な更新を回避
- 進行中のタスクがあれば、その完了を待つ
- `ConfigureAwait(false)` でデッドロック回避

---

### セキュリティ監査ログ実装

```csharp
namespace Baketa.Core.Abstractions.Authentication;

/// <summary>
/// トークン操作の監査ログを記録するインターフェース
/// </summary>
public interface ITokenAuditLogger
{
    /// <summary>トークン発行時</summary>
    Task LogTokenIssuedAsync(
        string userId,
        DateTime expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>トークン更新時</summary>
    Task LogTokenRefreshedAsync(
        string userId,
        DateTime oldExpiry,
        DateTime newExpiry,
        CancellationToken cancellationToken = default);

    /// <summary>トークン失効時</summary>
    Task LogTokenRevokedAsync(
        string userId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>トークン検証失敗時</summary>
    Task LogTokenValidationFailedAsync(
        string reason,
        CancellationToken cancellationToken = default);
}
```

```csharp
namespace Baketa.Infrastructure.Authentication;

/// <summary>
/// ファイルベースのトークン監査ログ実装
/// </summary>
public class FileTokenAuditLogger : ITokenAuditLogger
{
    private readonly ILogger<FileTokenAuditLogger> _logger;
    private readonly string _logFilePath;

    public FileTokenAuditLogger(
        ILogger<FileTokenAuditLogger> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _logFilePath = configuration["Logging:TokenAuditLogPath"]
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "token_audit.log");
    }

    public Task LogTokenIssuedAsync(
        string userId,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        var logEntry = $"[{DateTime.UtcNow:O}] TOKEN_ISSUED | UserId={userId} | ExpiresAt={expiresAt:O}";
        _logger.LogInformation("Token issued for user {UserId}, expires at {ExpiresAt}", userId, expiresAt);
        return AppendToLogFileAsync(logEntry, cancellationToken);
    }

    public Task LogTokenRefreshedAsync(
        string userId,
        DateTime oldExpiry,
        DateTime newExpiry,
        CancellationToken cancellationToken = default)
    {
        var logEntry = $"[{DateTime.UtcNow:O}] TOKEN_REFRESHED | UserId={userId} | OldExpiry={oldExpiry:O} | NewExpiry={newExpiry:O}";
        _logger.LogInformation(
            "Token refreshed for user {UserId}, old expiry: {OldExpiry}, new expiry: {NewExpiry}",
            userId, oldExpiry, newExpiry);
        return AppendToLogFileAsync(logEntry, cancellationToken);
    }

    public Task LogTokenRevokedAsync(
        string userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var logEntry = $"[{DateTime.UtcNow:O}] TOKEN_REVOKED | UserId={userId} | Reason={reason}";
        _logger.LogWarning("Token revoked for user {UserId}, reason: {Reason}", userId, reason);
        return AppendToLogFileAsync(logEntry, cancellationToken);
    }

    public Task LogTokenValidationFailedAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        var logEntry = $"[{DateTime.UtcNow:O}] TOKEN_VALIDATION_FAILED | Reason={reason}";
        _logger.LogWarning("Token validation failed: {Reason}", reason);
        return AppendToLogFileAsync(logEntry, cancellationToken);
    }

    private async Task AppendToLogFileAsync(string logEntry, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.AppendAllTextAsync(_logFilePath, logEntry + Environment.NewLine, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "監査ログの書き込みに失敗しました");
        }
    }
}
```

**監査ログのポイント**:
- すべてのトークン操作をログに記録
- ISO 8601形式のタイムスタンプ
- ユーザーID、有効期限、失効理由を記録
- ファイル書き込み失敗時もアプリケーションは継続

---

### トークン失効時の処理フロー

```csharp
namespace Baketa.Application.Services.Authentication;

/// <summary>
/// トークン失効ハンドラー
/// </summary>
public class TokenExpirationHandler
{
    private readonly ICredentialStorage _credentialStorage;
    private readonly ITokenAuditLogger _auditLogger;
    private readonly INavigationService _navigationService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<TokenExpirationHandler> _logger;

    public async Task HandleTokenExpiredAsync(string reason, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("トークンが失効しました: {Reason}", reason);

        try
        {
            // 1. 現在のトークンを取得（監査ログ用）
            var credential = await _credentialStorage.LoadCredentialAsync(cancellationToken)
                .ConfigureAwait(false);

            // 2. 監査ログ記録
            if (credential?.UserId != null)
            {
                await _auditLogger.LogTokenRevokedAsync(
                    credential.UserId,
                    reason,
                    cancellationToken).ConfigureAwait(false);
            }

            // 3. ローカル保存のトークンを削除
            await _credentialStorage.DeleteCredentialAsync(cancellationToken)
                .ConfigureAwait(false);

            // 4. ユーザーに通知
            await _notificationService.ShowToastAsync(
                "セッションが期限切れです",
                "再度ログインしてください",
                NotificationType.Warning,
                cancellationToken).ConfigureAwait(false);

            // 5. ログイン画面へリダイレクト
            await _navigationService.NavigateToLoginAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("トークン失効処理が完了しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "トークン失効処理中にエラーが発生しました");
            // ユーザーに致命的エラーを通知
            await _notificationService.ShowToastAsync(
                "エラー",
                "セッション処理に失敗しました。アプリを再起動してください。",
                NotificationType.Error,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Refresh Token失効検出（HTTP 401応答）
    /// </summary>
    public async Task<bool> TryHandleUnauthorizedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await HandleTokenExpiredAsync(
                "HTTP 401 Unauthorized received",
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        return false;
    }
}
```

**トークン失効処理のフロー**:
1. **検出**: HTTP 401 / Refresh Token期限切れ
2. **監査ログ記録**: 失効理由を記録
3. **クリーンアップ**: ローカルトークンを削除
4. **ユーザー通知**: トースト通知を表示
5. **リダイレクト**: ログイン画面へ遷移

---

## 動作確認基準

### 必須動作確認項目

#### 基本機能
- [ ] **トークン保存**: ログイン成功後、Windows Credential Managerにトークンが保存される
- [ ] **自動ログイン**: アプリ再起動時、保存済みトークンで自動ログインできる
- [ ] **トークン更新**: 期限切れ前にトークンが自動更新される
- [ ] **トークン削除**: ログアウト時にCredential Managerからトークンが削除される
- [ ] **有効期限チェック**: 期限切れトークンでログイン試行時、エラーが返される
- [ ] **セキュリティ**: 他のWindowsユーザーからトークンが読み込めないことを確認

#### 並列制御（追加）
- [ ] **同時更新**: 複数のAPI呼び出しが同時にトークン更新を試みても、1回のみ更新される
- [ ] **ダブルチェック**: ロック取得中に他のスレッドが更新した場合、不要な更新をスキップする
- [ ] **進行中タスク待機**: 既に更新中の場合、その結果を待つ

#### 監査ログ（追加）
- [ ] **ログ記録**: トークン発行・更新・失効がすべてログファイルに記録される
- [ ] **ログ形式**: ISO 8601形式のタイムスタンプとユーザーIDが記録される
- [ ] **エラー継続**: ログ書き込み失敗時もアプリケーションは正常に継続する

#### トークン失効処理（追加）
- [ ] **HTTP 401検出**: Supabase APIが401を返した場合、自動的にログイン画面へリダイレクト
- [ ] **ユーザー通知**: トークン失効時、トースト通知が表示される
- [ ] **クリーンアップ**: トークン失効時、ローカル保存のトークンが削除される

### テスト実行基準

- [ ] `WindowsCredentialStorageTests`: 全10ケースが成功
- [ ] `TokenRefreshServiceTests`: 全13ケースが成功（並列制御テスト追加）
- [ ] `FileTokenAuditLoggerTests`: 全8ケースが成功 ← 追加
- [ ] `TokenExpirationHandlerTests`: 全11ケースが成功 ← 追加
- [ ] **合計**: 42テストケース（既存1,518 + 42 = 1,560テスト）

---

## 依存関係

### Blocked by（先行して完了すべきissue）
- #167: ログイン/登録UI実装（ログイン成功後のトークン取得が必要）

### Blocks（このissue完了後に着手可能なissue）
- #169: 認証UI拡張（ログアウト機能でトークン削除が必要）

### Related（関連issue）
- #77: ライセンス管理システム基盤の実装（トークン管理の一部として位置づけ）

---

## 変更ファイル

### 新規作成
- `Baketa.Core/Abstractions/Authentication/ICredentialStorage.cs`
- `Baketa.Core/Abstractions/Authentication/ITokenRefreshService.cs`
- `Baketa.Core/Abstractions/Authentication/ITokenAuditLogger.cs` ← 追加
- `Baketa.Core/Abstractions/Authentication/AuthCredential.cs`
- `Baketa.Infrastructure.Platform/Windows/Authentication/WindowsCredentialStorage.cs`
- `Baketa.Infrastructure/Authentication/FileTokenAuditLogger.cs` ← 追加
- `Baketa.Application/Services/Authentication/TokenRefreshService.cs` (並列制御対応)
- `Baketa.Application/Services/Authentication/TokenExpirationHandler.cs` ← 追加
- `tests/Baketa.Infrastructure.Platform.Tests/Windows/Authentication/WindowsCredentialStorageTests.cs`
- `tests/Baketa.Application.Tests/Services/Authentication/TokenRefreshServiceTests.cs` (並列制御テスト追加)
- `tests/Baketa.Infrastructure.Tests/Authentication/FileTokenAuditLoggerTests.cs` ← 追加
- `tests/Baketa.Application.Tests/Services/Authentication/TokenExpirationHandlerTests.cs` ← 追加

### 修正
- `Baketa.Infrastructure/Authentication/SupabaseAuthenticationService.cs` (トークン保存・読み込み統合)
- `Baketa.UI/App.axaml.cs` (起動時の自動ログイン処理)
- `Baketa.Application/DI/Modules/ApplicationModule.cs` (DI登録)
- `Baketa.Infrastructure.Platform/DI/Modules/PlatformModule.cs` (DI登録)

---

## 実装ガイドライン

### Windows Credential Managerの利用
- `CredentialManagement` NuGetパッケージを推奨
- P/Invokeで直接実装する場合は、`advapi32.dll` の `CredRead/CredWrite/CredDelete` を使用

### トークンの暗号化
- Windows Credential Managerは自動的にDPAPI（Data Protection API）で暗号化
- 明示的な暗号化処理は不要

### タイマーのリソース管理
- `TokenRefreshService` は `IDisposable` を実装
- アプリ終了時に確実に `Dispose()` を呼び出す

### エラーハンドリング
- ネットワークエラー時はリトライ（最大3回）
- リトライ失敗時はユーザーに再ログインを促す

---

## セキュリティ考慮事項

### トークン保存のセキュリティ
- Windows Credential Managerは以下のセキュリティ特性を持つ：
  - ユーザーアカウント単位で分離
  - DPAPI（Data Protection API）による暗号化
  - 管理者権限でも他ユーザーのトークンは読めない

### トークン漏洩対策
- ログファイルにトークンを出力しない（マスク処理）
- メモリダンプ対策として、トークンは使用後すぐに破棄
- デバッグモードでもトークンをコンソール出力しない

### トークン有効期限
- Supabaseデフォルト: Access Token（1時間）、Refresh Token（30日）
- 有効期限切れ時は自動更新、更新失敗時は再ログイン

---

## 備考

### Issue #77との関係
- 本issueはトークン管理の基盤を実装
- #77（ライセンス管理）ではこのトークンを利用してプラン情報を取得

### セキュリティ強化機能（追加実装）
- **並列制御**: `SemaphoreSlim`による競合状態の防止
- **監査ログ**: すべてのトークン操作を記録し、セキュリティインシデント調査に活用
- **失効処理**: トークン失効時の自動クリーンアップとユーザー通知

### 監査ログの活用
- セキュリティインシデント調査
- 不正アクセスの検出
- ユーザーアクティビティの追跡
- コンプライアンス要件への対応

### 将来的な拡張
- マルチアカウント対応（複数ユーザーの切り替え）
- トークンのローテーション（セキュリティ強化）
- バイオメトリクス認証（Windows Hello統合）
- 集中ログサーバーへの送信（Elasticsearch, Application Insights等）

---

**作成日**: 2025-11-18
**作成者**: Claude Code
**関連ドキュメント**: `docs/BETA_DEVELOPMENT_PLAN.md`, `docs/issues/issue-167-login-ui.md`
