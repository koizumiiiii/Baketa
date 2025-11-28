using Baketa.Core.Abstractions.Auth;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.Auth;

/// <summary>
/// Background token refresh service with parallel control
/// Monitors token expiration and automatically refreshes before expiry
/// Uses SemaphoreSlim and double-check locking to prevent duplicate refresh requests
/// </summary>
public sealed class TokenRefreshService : ITokenRefreshService
{
    private readonly IAuthService _authService;
    private readonly ITokenStorage _tokenStorage;
    private readonly ITokenAuditLogger _auditLogger;
    private readonly ILogger<TokenRefreshService> _logger;

    // Timer for background monitoring
    private System.Threading.Timer? _refreshTimer;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);
    private readonly TimeSpan _refreshThreshold = TimeSpan.FromMinutes(5);

    // Parallel control
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile Task<AuthSession?>? _ongoingRefreshTask;
    private volatile bool _isMonitoring;
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsMonitoring => _isMonitoring;

    /// <inheritdoc/>
    public event EventHandler<TokenRefreshFailedEventArgs>? RefreshFailed;

    /// <summary>
    /// Initialize token refresh service with required dependencies
    /// </summary>
    public TokenRefreshService(
        IAuthService authService,
        ITokenStorage tokenStorage,
        ITokenAuditLogger auditLogger,
        ILogger<TokenRefreshService> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogDebug("TokenRefreshService initialized");
    }

    /// <inheritdoc/>
    public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_isMonitoring)
        {
            _logger.LogDebug("Token monitoring is already active");
            return Task.CompletedTask;
        }

        _isMonitoring = true;
        _refreshTimer = new System.Threading.Timer(
            callback: CheckAndRefreshTokenAsync,
            state: null,
            dueTime: TimeSpan.Zero,
            period: _checkInterval);

        _logger.LogInformation("Token refresh monitoring started (interval: {Interval})", _checkInterval);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void StopMonitoring()
    {
        ThrowIfDisposed();

        if (!_isMonitoring)
        {
            return;
        }

        _isMonitoring = false;
        _refreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _refreshTimer?.Dispose();
        _refreshTimer = null;

        _logger.LogInformation("Token refresh monitoring stopped");
    }

    /// <inheritdoc/>
    public async Task<AuthSession?> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var currentSession = await _authService.GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
        if (currentSession == null)
        {
            _logger.LogWarning("No active session to refresh");
            return null;
        }

        return await RefreshTokenWithLockAsync(currentSession, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Timer callback to check and refresh token if needed
    /// </summary>
    private async void CheckAndRefreshTokenAsync(object? state)
    {
        if (_disposed || !_isMonitoring)
        {
            return;
        }

        try
        {
            var session = await _authService.GetCurrentSessionAsync().ConfigureAwait(false);
            if (session == null)
            {
                _logger.LogDebug("No active session, skipping token check");
                return;
            }

            // Check if token is near expiry (within threshold)
            if (session.IsNearExpiry)
            {
                _logger.LogInformation(
                    "Token near expiry (expires at: {ExpiresAt}), initiating refresh",
                    session.ExpiresAt);

                await RefreshTokenWithLockAsync(session).ConfigureAwait(false);
            }
            else
            {
                _logger.LogDebug(
                    "Token valid until {ExpiresAt}, no refresh needed",
                    session.ExpiresAt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token expiration check");

            // Don't fail silently - notify listeners of the failure
            RefreshFailed?.Invoke(this, new TokenRefreshFailedEventArgs(
                reason: ex.Message,
                requiresReLogin: false));
        }
    }

    /// <summary>
    /// Refresh token with parallel control using SemaphoreSlim and double-check locking
    /// </summary>
    private async Task<AuthSession?> RefreshTokenWithLockAsync(
        AuthSession currentSession,
        CancellationToken cancellationToken = default)
    {
        // Fast path: If a refresh is already in progress, wait for its result
        var ongoingTask = _ongoingRefreshTask;
        if (ongoingTask is { IsCompleted: false })
        {
            _logger.LogDebug("Another thread is already refreshing token, waiting for completion");
            return await ongoingTask.ConfigureAwait(false);
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check: After acquiring lock, check if another thread already refreshed
            var latestSession = await _authService.GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
            if (latestSession != null && !latestSession.IsNearExpiry)
            {
                _logger.LogDebug("Token already refreshed by another thread");
                return latestSession;
            }

            // Start the refresh task
            _ongoingRefreshTask = RefreshTokenInternalAsync(currentSession, cancellationToken);
            return await _ongoingRefreshTask.ConfigureAwait(false);
        }
        finally
        {
            _ongoingRefreshTask = null;
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Internal token refresh implementation (called within lock)
    /// </summary>
    private async Task<AuthSession?> RefreshTokenInternalAsync(
        AuthSession currentSession,
        CancellationToken cancellationToken)
    {
        var oldExpiry = currentSession.ExpiresAt;
        _logger.LogInformation("Refreshing token (current expiry: {ExpiresAt})", oldExpiry);

        try
        {
            // Use RestoreSession which internally refreshes if needed
            // Note: Supabase SDK handles the actual refresh via RefreshSession
            await _authService.RestoreSessionAsync(cancellationToken).ConfigureAwait(false);

            var newSession = await _authService.GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);

            if (newSession != null && newSession.IsValid)
            {
                // Log successful refresh
                await _auditLogger.LogTokenRefreshedAsync(
                    userId: newSession.User.Id,
                    oldExpiry: oldExpiry,
                    newExpiry: newSession.ExpiresAt,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Token refreshed successfully (new expiry: {ExpiresAt})",
                    newSession.ExpiresAt);

                return newSession;
            }

            // Refresh failed - token might be invalid
            var reason = "Token refresh returned invalid session";
            _logger.LogWarning(reason);

            await _auditLogger.LogTokenValidationFailedAsync(
                reason: reason,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            RefreshFailed?.Invoke(this, new TokenRefreshFailedEventArgs(
                reason: reason,
                requiresReLogin: true));

            return null;
        }
        catch (Exception ex)
        {
            var reason = $"Token refresh failed: {ex.Message}";
            _logger.LogError(ex, reason);

            await _auditLogger.LogTokenValidationFailedAsync(
                reason: reason,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            RefreshFailed?.Invoke(this, new TokenRefreshFailedEventArgs(
                reason: reason,
                requiresReLogin: ex.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                              || ex.Message.Contains("expired", StringComparison.OrdinalIgnoreCase)));

            return null;
        }
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

        // StopMonitoringを先に呼び出す（ThrowIfDisposedチェックがあるため）
        StopMonitoring();
        _disposed = true;
        _refreshLock.Dispose();

        _logger.LogDebug("TokenRefreshService disposed");
    }
}
