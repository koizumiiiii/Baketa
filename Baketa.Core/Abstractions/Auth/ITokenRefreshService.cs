namespace Baketa.Core.Abstractions.Auth;

/// <summary>
/// Interface for background token refresh operations
/// Monitors token expiration and automatically refreshes before expiry
/// </summary>
public interface ITokenRefreshService : IDisposable
{
    /// <summary>
    /// Start background monitoring for token expiration
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the start operation</returns>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop background token monitoring
    /// </summary>
    void StopMonitoring();

    /// <summary>
    /// Force a token refresh operation
    /// Uses parallel control to prevent duplicate refresh requests
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Refreshed session or null if refresh failed</returns>
    Task<AuthSession?> RefreshTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether monitoring is currently active
    /// </summary>
    bool IsMonitoring { get; }

    /// <summary>
    /// Event fired when token refresh fails and re-login is required
    /// </summary>
    event EventHandler<TokenRefreshFailedEventArgs>? RefreshFailed;
}

/// <summary>
/// Event arguments for token refresh failure
/// </summary>
/// <param name="reason">Reason for failure</param>
/// <param name="requiresReLogin">Whether user needs to re-login</param>
public sealed class TokenRefreshFailedEventArgs(string reason, bool requiresReLogin) : EventArgs
{
    /// <summary>
    /// Reason for the refresh failure
    /// </summary>
    public string Reason { get; } = reason;

    /// <summary>
    /// Whether user needs to re-authenticate
    /// </summary>
    public bool RequiresReLogin { get; } = requiresReLogin;

    /// <summary>
    /// Timestamp of the failure
    /// </summary>
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
