namespace Baketa.Core.Abstractions.Auth;

/// <summary>
/// Authentication provider types supported by the system
/// </summary>
public enum AuthProvider
{
    Google,
    X,
    Discord,
    Steam
}

/// <summary>
/// Main authentication service interface providing comprehensive auth functionality
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Sign up with email and password
    /// </summary>
    /// <param name="email">User email address</param>
    /// <param name="password">User password</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication result</returns>
    Task<AuthResult> SignUpWithEmailPasswordAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sign in with email and password
    /// </summary>
    /// <param name="email">User email address</param>
    /// <param name="password">User password</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication result</returns>
    Task<AuthResult> SignInWithEmailPasswordAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sign in with OAuth provider
    /// </summary>
    /// <param name="provider">OAuth provider</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication result</returns>
    Task<AuthResult> SignInWithOAuthAsync(AuthProvider provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current authentication session
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current session or null if not authenticated</returns>
    Task<AuthSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restore session on application startup
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the restore operation</returns>
    Task RestoreSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sign out current user
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the sign out operation</returns>
    Task SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send password reset email
    /// </summary>
    /// <param name="email">User email address</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if email sent successfully</returns>
    Task<bool> SendPasswordResetEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update user password (after reset flow)
    /// </summary>
    /// <param name="newPassword">New password</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication result</returns>
    Task<AuthResult> UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when authentication status changes
    /// </summary>
    event EventHandler<AuthStatusChangedEventArgs> AuthStatusChanged;
}