using Baketa.Core.Abstractions.Auth;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// Handles token expiration events and coordinates user notification and navigation
/// Listens to TokenRefreshService failure events and cleans up authentication state
/// </summary>
public sealed class TokenExpirationHandler : IDisposable
{
    private readonly ITokenStorage _tokenStorage;
    private readonly ITokenAuditLogger _auditLogger;
    private readonly INavigationService _navigationService;
    private readonly INotificationService _notificationService;
    private readonly ITokenRefreshService _tokenRefreshService;
    private readonly IAuthService _authService;
    private readonly ILogger<TokenExpirationHandler> _logger;
    private bool _disposed;

    /// <summary>
    /// Initialize token expiration handler with required dependencies
    /// </summary>
    public TokenExpirationHandler(
        ITokenStorage tokenStorage,
        ITokenAuditLogger auditLogger,
        INavigationService navigationService,
        INotificationService notificationService,
        ITokenRefreshService tokenRefreshService,
        IAuthService authService,
        ILogger<TokenExpirationHandler> logger)
    {
        _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _tokenRefreshService = tokenRefreshService ?? throw new ArgumentNullException(nameof(tokenRefreshService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to refresh failure events
        _tokenRefreshService.RefreshFailed += OnTokenRefreshFailed;

        _logger.LogDebug("TokenExpirationHandler initialized and subscribed to RefreshFailed event");
    }

    /// <summary>
    /// Handle token refresh failure event
    /// </summary>
    private async void OnTokenRefreshFailed(object? sender, TokenRefreshFailedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogWarning(
            "Token refresh failed: {Reason}, RequiresReLogin: {RequiresReLogin}",
            e.Reason,
            e.RequiresReLogin);

        if (e.RequiresReLogin)
        {
            await HandleTokenExpiredAsync(e.Reason).ConfigureAwait(false);
        }
        else
        {
            // Transient failure - just notify user
            await _notificationService.ShowWarningAsync(
                "認証警告",
                "セッションの更新に一時的に失敗しました。後で再試行されます。").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Handle token expiration - cleanup and redirect to login
    /// </summary>
    /// <param name="reason">Reason for expiration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task HandleTokenExpiredAsync(string reason, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _logger.LogWarning("Handling token expiration: {Reason}", reason);

        try
        {
            // 1. Get current session info for audit log
            var session = await _authService.GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
            var userId = session?.User?.Id ?? "unknown";

            // 2. Record audit log
            await _auditLogger.LogTokenRevokedAsync(
                userId: userId,
                reason: reason,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // 3. Clear local tokens
            await _tokenStorage.ClearTokensAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Local tokens cleared");

            // 4. Sign out from Supabase
            try
            {
                await _authService.SignOutAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Signed out from authentication service");
            }
            catch (Exception ex)
            {
                // Sign out might fail if token is already invalid - this is expected
                _logger.LogWarning(ex, "SignOut failed during token expiration handling (may be expected)");
            }

            // 5. Stop token monitoring
            _tokenRefreshService.StopMonitoring();

            // 6. Notify user
            await _notificationService.ShowWarningAsync(
                "セッション期限切れ",
                "セッションの有効期限が切れました。再度ログインしてください。").ConfigureAwait(false);

            // 7. Navigate to login
            await _navigationService.LogoutAndShowLoginAsync().ConfigureAwait(false);

            _logger.LogInformation("Token expiration handling completed - user redirected to login");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token expiration handling");

            // Show error to user
            await _notificationService.ShowErrorAsync(
                "エラー",
                "セッション処理に失敗しました。アプリを再起動してください。").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Handle HTTP 401 Unauthorized response
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if handled, false otherwise</returns>
    public async Task<bool> TryHandleUnauthorizedAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _logger.LogWarning("HTTP 401 Unauthorized received - handling token expiration");

        await HandleTokenExpiredAsync(
            reason: "HTTP 401 Unauthorized received",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Unsubscribe from events
        _tokenRefreshService.RefreshFailed -= OnTokenRefreshFailed;

        _logger.LogDebug("TokenExpirationHandler disposed");
    }
}
