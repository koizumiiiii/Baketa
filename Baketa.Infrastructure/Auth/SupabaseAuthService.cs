using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Baketa.Core.Abstractions.Auth;
using Microsoft.Extensions.Logging;
using Supabase.Gotrue;
using Supabase.Gotrue.Exceptions;
using SupabaseClient = Supabase.Client;

namespace Baketa.Infrastructure.Auth;

/// <summary>
/// OAuth flow state containing PKCE verifier and CSRF state parameter
/// </summary>
/// <param name="Uri">OAuth authorization URL</param>
/// <param name="PkceVerifier">PKCE code verifier for token exchange</param>
/// <param name="StateParameter">CSRF protection state parameter</param>
public sealed record OAuthFlowState(Uri Uri, string PkceVerifier, string StateParameter);

/// <summary>
/// Supabase authentication service implementation using C# 12 features
/// Provides comprehensive authentication functionality with OAuth and email/password support
/// </summary>
[SuppressMessage("CodeQuality", "cs/exposure-of-private-information",
    Justification = "All email addresses are masked using MaskEmail() before logging. MaskEmail converts 'user@example.com' to 'us***@example.com'")]
public sealed class SupabaseAuthService : IAuthService, IDisposable
{
    private readonly ILogger<SupabaseAuthService> _logger;
    private readonly SemaphoreSlim _authSemaphore = new(1, 1);
    private readonly SupabaseClient _supabaseClient;
    private readonly ITokenStorage _tokenStorage;
    private readonly ITokenAuditLogger _auditLogger;
    private bool _disposed;

    /// <summary>
    /// Event fired when authentication status changes
    /// </summary>
    public event EventHandler<AuthStatusChangedEventArgs>? AuthStatusChanged;

    /// <summary>
    /// Initialize Supabase authentication service with modern C# 12 primary constructor
    /// </summary>
    /// <param name="supabaseClient">Supabase client instance</param>
    /// <param name="tokenStorage">Token storage for session persistence</param>
    /// <param name="auditLogger">Token audit logger for security compliance</param>
    /// <param name="logger">Logger instance</param>
    public SupabaseAuthService(
        SupabaseClient supabaseClient,
        ITokenStorage tokenStorage,
        ITokenAuditLogger auditLogger,
        ILogger<SupabaseAuthService> logger)
    {
        _supabaseClient = supabaseClient ?? throw new ArgumentNullException(nameof(supabaseClient));
        _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to auth state changes
        _supabaseClient.Auth.AddStateChangedListener(OnAuthStateChanged);

        _logger.LogInformation("SupabaseAuthService initialized with Supabase client and token storage");
    }

    /// <summary>
    /// Sign up with email and password using modern async patterns
    /// </summary>
    /// <param name="email">User email address</param>
    /// <param name="password">User password</param>
    /// <param name="userMetadata">Optional user metadata (e.g., language preference)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication result</returns>
    public async Task<AuthResult> SignUpWithEmailPasswordAsync(
        string email,
        string password,
        Dictionary<string, object>? userMetadata = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        await _authSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Starting email signup for user: {MaskedEmail}", MaskEmail(email));

            // [Issue #179] Call Supabase SignUp API with user metadata (e.g., language preference)
            Supabase.Gotrue.Session? session;
            if (userMetadata != null && userMetadata.Count > 0)
            {
                var options = new Supabase.Gotrue.SignUpOptions
                {
                    Data = userMetadata
                };
                _logger.LogDebug("SignUp with user metadata: {MetadataKeys}", string.Join(", ", userMetadata.Keys));
                session = await _supabaseClient.Auth.SignUp(email, password, options).ConfigureAwait(false);
            }
            else
            {
                session = await _supabaseClient.Auth.SignUp(email, password).ConfigureAwait(false);
            }

            if (session?.User != null)
            {
                // Check if email confirmation is required
                if (session.User.ConfirmedAt == null)
                {
                    _logger.LogInformation("Signup confirmation email sent to: {MaskedEmail}", MaskEmail(email));
                    return new AuthFailure(AuthErrorCodes.EmailNotConfirmed,
                        "サインアップ確認メールを送信しました。メールを確認してください。");
                }

                // User is confirmed (auto-confirm enabled in Supabase)
                var authSession = ConvertToAuthSession(session);
                _logger.LogInformation("Signup successful for user: {MaskedEmail}", MaskEmail(email));

                // Record audit log for token issuance
                await _auditLogger.LogTokenIssuedAsync(authSession.User.Id, authSession.ExpiresAt, cancellationToken).ConfigureAwait(false);

                AuthStatusChanged?.Invoke(this, new AuthStatusChangedEventArgs(true, authSession.User, false));
                return new AuthSuccess(authSession);
            }

            _logger.LogWarning("Signup returned null session for user: {MaskedEmail}", MaskEmail(email));
            return new AuthFailure(AuthErrorCodes.UnexpectedError, "サインアップに失敗しました。");
        }
        catch (GotrueException ex)
        {
            _logger.LogWarning(ex, "Supabase auth error during signup for user: {MaskedEmail}", MaskEmail(email));
            return MapGotrueException(ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Signup cancelled for user: {MaskedEmail}", MaskEmail(email));
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during signup for user: {MaskedEmail}", MaskEmail(email));
            return new AuthFailure(AuthErrorCodes.UnexpectedError, $"予期せぬエラーが発生しました: {ex.Message}");
        }
        finally
        {
            _authSemaphore.Release();
        }
    }

    /// <summary>
    /// Sign in with email and password using modern async patterns
    /// </summary>
    /// <param name="email">User email address</param>
    /// <param name="password">User password</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication result</returns>
    public async Task<AuthResult> SignInWithEmailPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        await _authSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Starting email signin for user: {MaskedEmail}", MaskEmail(email));

            // Call Supabase SignIn API
            var session = await _supabaseClient.Auth.SignIn(email, password).ConfigureAwait(false);

            if (session?.User != null)
            {
                var authSession = ConvertToAuthSession(session);
                _logger.LogInformation("Signin successful for user: {MaskedEmail}", MaskEmail(email));

                // Record audit log for token issuance
                await _auditLogger.LogTokenIssuedAsync(authSession.User.Id, authSession.ExpiresAt, cancellationToken).ConfigureAwait(false);

                AuthStatusChanged?.Invoke(this, new AuthStatusChangedEventArgs(true, authSession.User, false));
                return new AuthSuccess(authSession);
            }

            _logger.LogWarning("Signin returned null session for user: {MaskedEmail}", MaskEmail(email));
            return new AuthFailure(AuthErrorCodes.InvalidCredentials, "メールアドレスまたはパスワードが正しくありません。");
        }
        catch (GotrueException ex)
        {
            _logger.LogWarning(ex, "Supabase auth error during signin for user: {MaskedEmail}", MaskEmail(email));
            return MapGotrueException(ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Signin cancelled for user: {MaskedEmail}", MaskEmail(email));
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during signin for user: {MaskedEmail}", MaskEmail(email));
            return new AuthFailure(AuthErrorCodes.UnexpectedError, $"予期せぬエラーが発生しました: {ex.Message}");
        }
        finally
        {
            _authSemaphore.Release();
        }
    }

    /// <summary>
    /// Initiate OAuth flow and return flow state for callback handling
    /// This method does NOT open the browser - caller is responsible for that
    /// </summary>
    /// <param name="provider">OAuth provider</param>
    /// <param name="redirectPort">Local redirect port for callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>OAuth flow state containing URI, PKCE verifier, and CSRF state</returns>
    public async Task<OAuthFlowState?> InitiateOAuthFlowAsync(AuthProvider provider, int redirectPort, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _authSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Initiating OAuth flow for provider: {Provider}", provider);

            // Map Baketa AuthProvider to Supabase Constants.Provider
            var supabaseProvider = MapToSupabaseProvider(provider);

            // Steam uses OpenID 2.0, not OAuth - requires custom implementation (Issue #173)
            if (provider == AuthProvider.Steam)
            {
                _logger.LogWarning("Steam OAuth not supported - requires custom OpenID 2.0 implementation");
                return null;
            }

            // 🔥 [ISSUE#167] リダイレクトURLはクエリパラメータなしのクリーンなURLにする
            // SupabaseのPKCEフローがCSRF保護を提供するため、カスタムstateパラメータは不要
            var redirectUrl = $"http://localhost:{redirectPort}/oauth/callback";

            _logger.LogDebug("OAuth redirect URL: {RedirectUrl}", redirectUrl);

            // Initiate OAuth flow with PKCE
            var providerAuthState = await _supabaseClient.Auth.SignIn(supabaseProvider, new SignInOptions
            {
                FlowType = Constants.OAuthFlowType.PKCE,
                RedirectTo = redirectUrl
            }).ConfigureAwait(false);

            if (providerAuthState?.Uri != null && !string.IsNullOrEmpty(providerAuthState.PKCEVerifier))
            {
                _logger.LogDebug("OAuth flow initiated for provider: {Provider}, PKCE verifier length: {Length}",
                    provider, providerAuthState.PKCEVerifier.Length);

                // PKCEVerifierをstate代わりに使用（PKCEフローではPKCE自体がCSRF保護を提供）
                return new OAuthFlowState(providerAuthState.Uri, providerAuthState.PKCEVerifier, providerAuthState.PKCEVerifier);
            }

            _logger.LogWarning("OAuth initiation returned invalid state for provider: {Provider}", provider);
            return null;
        }
        catch (GotrueException ex)
        {
            _logger.LogWarning(ex, "Supabase auth error during OAuth initiation for provider: {Provider}", provider);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("OAuth initiation cancelled for provider: {Provider}", provider);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during OAuth initiation for provider: {Provider}", provider);
            return null;
        }
        finally
        {
            _authSemaphore.Release();
        }
    }

    /// <summary>
    /// Sign in with OAuth provider using PKCE flow
    /// Note: For full OAuth flow with callback handling, use OAuthCallbackHandler.StartOAuthFlowAsync instead
    /// </summary>
    /// <param name="provider">OAuth provider</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication result</returns>
    public async Task<AuthResult> SignInWithOAuthAsync(AuthProvider provider, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Steam uses OpenID 2.0, not OAuth - requires custom implementation (Issue #173)
        if (provider == AuthProvider.Steam)
        {
            return new AuthFailure(AuthErrorCodes.OAuthError,
                "Steam認証はカスタム実装が必要です。Issue #173で対応予定です。");
        }

        // For OAuth, we recommend using OAuthCallbackHandler.StartOAuthFlowAsync
        // which properly handles PKCE verifier and CSRF protection
        _logger.LogInformation("OAuth signin requested for provider: {Provider}. Use OAuthCallbackHandler for full flow.", provider);

        return new AuthFailure(AuthErrorCodes.OAuthError,
            "OAuth認証を開始するにはOAuthCallbackHandlerを使用してください。");
    }

    /// <summary>
    /// Generate a cryptographically secure random string
    /// </summary>
    private static string GenerateSecureRandomString(int length)
    {
        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=')[..length];
    }

    /// <summary>
    /// Mask email address for logging (PII protection)
    /// Example: "user@example.com" -> "u***@e***.com"
    /// </summary>
    private static string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email))
            return "[empty]";

        var atIndex = email.IndexOf('@');
        if (atIndex < 1)
            return "[invalid]";

        var localPart = email[..atIndex];
        var domainPart = email[(atIndex + 1)..];
        var dotIndex = domainPart.LastIndexOf('.');

        var maskedLocal = localPart.Length > 1 ? $"{localPart[0]}***" : "*";
        var maskedDomain = dotIndex > 1 ? $"{domainPart[0]}***{domainPart[dotIndex..]}" : "***";

        return $"{maskedLocal}@{maskedDomain}";
    }

    /// <summary>
    /// Exchange OAuth code for session (called by OAuthCallbackHandler)
    /// </summary>
    /// <param name="pkceVerifier">PKCE verifier from initial OAuth request</param>
    /// <param name="code">Authorization code from callback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication result</returns>
    public async Task<AuthResult> ExchangeCodeForSessionAsync(string pkceVerifier, string code, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(pkceVerifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        await _authSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Exchanging OAuth code for session");

            var session = await _supabaseClient.Auth.ExchangeCodeForSession(pkceVerifier, code).ConfigureAwait(false);

            if (session?.User != null)
            {
                var authSession = ConvertToAuthSession(session);
                _logger.LogInformation("OAuth code exchange successful for user: {UserId}", session.User.Id);

                // Record audit log for token issuance
                await _auditLogger.LogTokenIssuedAsync(authSession.User.Id, authSession.ExpiresAt, cancellationToken).ConfigureAwait(false);

                AuthStatusChanged?.Invoke(this, new AuthStatusChangedEventArgs(true, authSession.User, false));
                return new AuthSuccess(authSession);
            }

            _logger.LogWarning("OAuth code exchange returned null session");
            return new AuthFailure(AuthErrorCodes.OAuthError, "OAuth認証の完了に失敗しました。");
        }
        catch (GotrueException ex)
        {
            _logger.LogWarning(ex, "Supabase auth error during OAuth code exchange");
            return MapGotrueException(ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("OAuth code exchange cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during OAuth code exchange");
            return new AuthFailure(AuthErrorCodes.UnexpectedError, $"予期せぬエラーが発生しました: {ex.Message}");
        }
        finally
        {
            _authSemaphore.Release();
        }
    }

    /// <summary>
    /// Get current authentication session
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current session or null if not authenticated</returns>
    public async Task<AuthSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            // Get current session from Supabase client
            var session = _supabaseClient.Auth.CurrentSession;

            if (session?.User != null && session.ExpiresAt() > DateTime.UtcNow)
            {
                return ConvertToAuthSession(session);
            }

            // Try to retrieve session from persistence
            var retrievedSession = await _supabaseClient.Auth.RetrieveSessionAsync().ConfigureAwait(false);
            if (retrievedSession?.User != null && retrievedSession.ExpiresAt() > DateTime.UtcNow)
            {
                return ConvertToAuthSession(retrievedSession);
            }

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Get current session cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current session");
            return null;
        }
    }

    /// <summary>
    /// Restore session on application startup from persistent storage
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the restore operation</returns>
    public async Task RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            _logger.LogInformation("Attempting to restore user session from persistent storage...");

            // Check if tokens are stored
            var hasStoredTokens = await _tokenStorage.HasStoredTokensAsync(cancellationToken).ConfigureAwait(false);
            if (!hasStoredTokens)
            {
                _logger.LogInformation("No stored tokens found");
                AuthStatusChanged?.Invoke(this, new AuthStatusChangedEventArgs(false, null, false));
                return;
            }

            // Retrieve stored tokens
            var storedTokens = await _tokenStorage.RetrieveTokensAsync(cancellationToken).ConfigureAwait(false);
            if (storedTokens == null)
            {
                _logger.LogWarning("Failed to retrieve stored tokens");
                AuthStatusChanged?.Invoke(this, new AuthStatusChangedEventArgs(false, null, false));
                return;
            }

            _logger.LogDebug("Retrieved stored tokens, attempting to restore session...");

            // Use Supabase's SetSession to restore from tokens
            var session = await _supabaseClient.Auth.SetSession(storedTokens.Value.AccessToken, storedTokens.Value.RefreshToken).ConfigureAwait(false);

            if (session != null && session.User != null)
            {
                var authSession = ConvertToAuthSession(session);
                _logger.LogInformation("Session restored successfully for user: {UserId}", session.User.Id);
                AuthStatusChanged?.Invoke(this, new AuthStatusChangedEventArgs(true, authSession.User, false));

                // Update stored tokens with potentially refreshed ones
                var tokensStored = await _tokenStorage.StoreTokensAsync(session.AccessToken, session.RefreshToken, cancellationToken).ConfigureAwait(false);
                if (!tokensStored)
                {
                    _logger.LogWarning("Session restored but failed to update stored tokens - next restart may require re-login");
                }
            }
            else
            {
                _logger.LogWarning("[TOKEN_CLEAR][Path-3A] SupabaseAuthService.RestoreSessionAsync: SetSession returned null/no user — clearing stored tokens");
                await _tokenStorage.ClearTokensAsync(cancellationToken).ConfigureAwait(false);
                AuthStatusChanged?.Invoke(this, new AuthStatusChangedEventArgs(false, null, false));
            }
        }
        catch (GotrueException ex)
        {
            _logger.LogWarning(ex, "[TOKEN_CLEAR][Path-3B] SupabaseAuthService.RestoreSessionAsync: GotrueException (invalid/expired tokens) — clearing stored tokens. Message={Message}", ex.Message);
            await _tokenStorage.ClearTokensAsync(cancellationToken).ConfigureAwait(false);
            AuthStatusChanged?.Invoke(this, new AuthStatusChangedEventArgs(false, null, false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Session restore cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during session restore");
            AuthStatusChanged?.Invoke(this, new AuthStatusChangedEventArgs(false, null, false));
        }
    }

    /// <summary>
    /// Sign out current user
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the sign out operation</returns>
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _authSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Starting user signout");

            await _supabaseClient.Auth.SignOut().ConfigureAwait(false);

            // Clear stored tokens from persistent storage
            _logger.LogInformation("[TOKEN_CLEAR][Path-3C] SupabaseAuthService.SignOutAsync: Normal signout flow — clearing stored tokens");
            await _tokenStorage.ClearTokensAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("[TOKEN_CLEAR][Path-3C] Cleared stored tokens");

            _logger.LogInformation("User signout completed");
            AuthStatusChanged?.Invoke(this, new AuthStatusChangedEventArgs(false, null, true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Signout cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during signout");
            // Even if signout fails on server, clear local state and tokens
            _logger.LogWarning("[TOKEN_CLEAR][Path-3D] SupabaseAuthService.SignOutAsync: Error recovery — clearing stored tokens. Error={Error}", ex.Message);
            await _tokenStorage.ClearTokensAsync(CancellationToken.None).ConfigureAwait(false);
            AuthStatusChanged?.Invoke(this, new AuthStatusChangedEventArgs(false, null, true));
        }
        finally
        {
            _authSemaphore.Release();
        }
    }

    /// <summary>
    /// Send password reset email
    /// </summary>
    /// <param name="email">User email address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if email sent successfully</returns>
    public async Task<bool> SendPasswordResetEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        try
        {
            _logger.LogInformation("Sending password reset email to: {MaskedEmail}", MaskEmail(email));

            // Basic email validation
            if (!email.Contains('@') || email.Length < 5)
            {
                _logger.LogWarning("Invalid email address format: {MaskedEmail}", MaskEmail(email));
                return false;
            }

            // Supabase Auth password reset API call
            await _supabaseClient.Auth.ResetPasswordForEmail(email).ConfigureAwait(false);

            _logger.LogInformation("Password reset email sent successfully to: {MaskedEmail}", MaskEmail(email));
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Password reset email cancelled for: {MaskedEmail}", MaskEmail(email));
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending password reset email to: {MaskedEmail}", MaskEmail(email));
            return false;
        }
    }

    /// <summary>
    /// Update user password (after reset flow)
    /// </summary>
    /// <param name="newPassword">New password</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication result</returns>
    public async Task<AuthResult> UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);

        await _authSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Updating user password");

            var user = await _supabaseClient.Auth.Update(new UserAttributes { Password = newPassword }).ConfigureAwait(false);

            if (user != null)
            {
                _logger.LogInformation("Password updated successfully for user: {UserId}", user.Id);

                // Get current session after password update
                var session = await GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
                if (session != null)
                {
                    return new AuthSuccess(session);
                }

                // If no session, return success without session
                return new AuthFailure(AuthErrorCodes.UnexpectedError, "パスワードは更新されましたが、セッションの取得に失敗しました。再度ログインしてください。");
            }

            _logger.LogWarning("Password update returned null user");
            return new AuthFailure(AuthErrorCodes.UnexpectedError, "パスワードの更新に失敗しました。");
        }
        catch (GotrueException ex)
        {
            _logger.LogWarning(ex, "Supabase auth error during password update");
            return MapGotrueException(ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Password update cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating password");
            return new AuthFailure(AuthErrorCodes.UnexpectedError, $"予期せぬエラーが発生しました: {ex.Message}");
        }
        finally
        {
            _authSemaphore.Release();
        }
    }

    /// <summary>
    /// Convert Supabase Session to Baketa AuthSession
    /// </summary>
    /// <param name="session">Supabase session</param>
    /// <returns>Baketa authentication session</returns>
    private static AuthSession ConvertToAuthSession(Session session)
    {
        var user = session.User!;
        var userInfo = new UserInfo(
            Id: user.Id ?? string.Empty,
            Email: user.Email ?? string.Empty,
            DisplayName: user.UserMetadata?.GetValueOrDefault("display_name")?.ToString()
                ?? user.UserMetadata?.GetValueOrDefault("full_name")?.ToString()
                ?? user.Email?.Split('@')[0],
            AvatarUrl: user.UserMetadata?.GetValueOrDefault("avatar_url")?.ToString(),
            Provider: MapFromIdentityProvider(user.AppMetadata?.GetValueOrDefault("provider")?.ToString()),
            Language: user.UserMetadata?.GetValueOrDefault("language")?.ToString()
        );

        return new AuthSession(
            AccessToken: session.AccessToken ?? string.Empty,
            RefreshToken: session.RefreshToken ?? string.Empty,
            ExpiresAt: session.ExpiresAt(),
            User: userInfo
        );
    }

    /// <summary>
    /// Map Baketa AuthProvider to Supabase Constants.Provider
    /// </summary>
    /// <param name="provider">Baketa auth provider</param>
    /// <returns>Supabase provider constant</returns>
    private static Constants.Provider MapToSupabaseProvider(AuthProvider provider) =>
        provider switch
        {
            AuthProvider.Google => Constants.Provider.Google,
            AuthProvider.X => Constants.Provider.Twitter,
            AuthProvider.Discord => Constants.Provider.Discord,
            AuthProvider.Twitch => Constants.Provider.Twitch,
            AuthProvider.Steam => throw new NotSupportedException("Steam uses OpenID 2.0, not OAuth. Use custom implementation."),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), "サポートされていないプロバイダーです。")
        };

    /// <summary>
    /// Map identity provider string to Baketa AuthProvider
    /// </summary>
    /// <param name="provider">Identity provider string</param>
    /// <returns>Baketa auth provider or null</returns>
    private static AuthProvider? MapFromIdentityProvider(string? provider) =>
        provider?.ToLowerInvariant() switch
        {
            "google" => AuthProvider.Google,
            "twitter" => AuthProvider.X,
            "discord" => AuthProvider.Discord,
            "twitch" => AuthProvider.Twitch,
            "steam" => AuthProvider.Steam,
            "email" => null,
            _ => null
        };

    /// <summary>
    /// Map GotrueException to AuthFailure with appropriate error code
    /// </summary>
    /// <param name="ex">Gotrue exception</param>
    /// <returns>Authentication failure result</returns>
    private static AuthFailure MapGotrueException(GotrueException ex)
    {
        var message = ex.Message?.ToLowerInvariant() ?? string.Empty;

        // [Issue #179] Infrastructure層ではエラーコードのみを返す
        // メッセージのローカライズはUI層の責務
        return message switch
        {
            _ when message.Contains("invalid login credentials") ||
                   message.Contains("invalid password") ||
                   message.Contains("email not confirmed")
                => new AuthFailure(AuthErrorCodes.InvalidCredentials, ex.Message ?? "InvalidCredentials"),

            _ when message.Contains("user not found")
                => new AuthFailure(AuthErrorCodes.UserNotFound, ex.Message ?? "UserNotFound"),

            _ when message.Contains("user already registered") ||
                   message.Contains("email already exists")
                => new AuthFailure(AuthErrorCodes.UserAlreadyExists, ex.Message ?? "UserAlreadyExists"),

            _ when message.Contains("email not confirmed")
                => new AuthFailure(AuthErrorCodes.EmailNotConfirmed, ex.Message ?? "EmailNotConfirmed"),

            _ when message.Contains("weak password") ||
                   message.Contains("password should be")
                => new AuthFailure(AuthErrorCodes.WeakPassword, ex.Message ?? "WeakPassword"),

            _ when message.Contains("rate limit") ||
                   message.Contains("too many requests")
                => new AuthFailure(AuthErrorCodes.RateLimitExceeded, ex.Message ?? "RateLimitExceeded"),

            _ when message.Contains("network") ||
                   message.Contains("connection")
                => new AuthFailure(AuthErrorCodes.NetworkError, ex.Message ?? "NetworkError"),

            _ when message.Contains("token") ||
                   message.Contains("jwt")
                => new AuthFailure(AuthErrorCodes.InvalidToken, ex.Message ?? "InvalidToken"),

            _ when message.Contains("expired")
                => new AuthFailure(AuthErrorCodes.TokenExpired, ex.Message ?? "TokenExpired"),

            _ => new AuthFailure(AuthErrorCodes.UnexpectedError, ex.Message ?? "UnexpectedError")
        };
    }

    /// <summary>
    /// Handle Supabase auth state changes
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="changedState">Changed auth state</param>
    private void OnAuthStateChanged(object? sender, Constants.AuthState changedState)
    {
        try
        {
            _logger.LogDebug("Auth state changed: {State}", changedState);

            var session = _supabaseClient.Auth.CurrentSession;
            var user = _supabaseClient.Auth.CurrentUser;

            bool isLoggedIn = session != null && user != null;

            if (isLoggedIn && user != null)
            {
                var userInfo = new UserInfo(
                    user.Id ?? string.Empty,
                    user.Email ?? string.Empty,
                    user.UserMetadata?.GetValueOrDefault("display_name")?.ToString()
                        ?? user.Email?.Split('@')[0] ?? "User"
                );

                _logger.LogDebug("[AUTH_DEBUG] AuthStatusChanged.Invoke呼び出し");
                AuthStatusChanged?.Invoke(this, new AuthStatusChangedEventArgs(true, userInfo, false));
                _logger.LogDebug("[AUTH_DEBUG] AuthStatusChanged.Invoke完了");
                _logger.LogInformation("Auth state changed: User logged in - {MaskedEmail}", MaskEmail(user.Email ?? string.Empty));
            }
            else
            {
                _logger.LogDebug("[AUTH_DEBUG] AuthStatusChanged.Invoke呼び出し前 (logged out)");
                AuthStatusChanged?.Invoke(this, new AuthStatusChangedEventArgs(false, null, true));
                _logger.LogDebug("[AUTH_DEBUG] AuthStatusChanged.Invoke呼び出し後 (logged out)");
                _logger.LogInformation("Auth state changed: User logged out");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling auth state change: {Message}", ex.Message);
            Console.WriteLine($"[AUTH_DEBUG] OnAuthStateChanged全体で例外: {ex}");
        }
    }

    /// <summary>
    /// Helper method to check if object is disposed
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Dispose of resources using modern disposal pattern
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        // Unsubscribe from Supabase events
        _supabaseClient.Auth.RemoveStateChangedListener(OnAuthStateChanged);

        _authSemaphore.Dispose();
        _disposed = true;

        _logger.LogInformation("SupabaseAuthService disposed");
    }
}
