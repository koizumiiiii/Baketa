namespace Baketa.Core.Abstractions.Auth;

/// <summary>
/// Base authentication result record using C# 12 abstract record types
/// </summary>
public abstract record AuthResult;

/// <summary>
/// Successful authentication result containing session information
/// </summary>
/// <param name="Session">The authentication session</param>
public sealed record AuthSuccess(AuthSession Session) : AuthResult;

/// <summary>
/// Failed authentication result containing error information
/// </summary>
/// <param name="ErrorCode">Specific error code for programmatic handling</param>
/// <param name="Message">User-friendly error message</param>
public sealed record AuthFailure(string ErrorCode, string Message) : AuthResult;

/// <summary>
/// Authentication session containing user and token information
/// </summary>
/// <param name="AccessToken">JWT access token</param>
/// <param name="RefreshToken">Refresh token for session renewal</param>
/// <param name="ExpiresAt">Session expiration time in UTC</param>
/// <param name="User">User information</param>
public sealed record AuthSession(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserInfo User)
{
    /// <summary>
    /// Check if the session is still valid
    /// </summary>
    public bool IsValid => DateTime.UtcNow < ExpiresAt;

    /// <summary>
    /// Check if the session will expire soon (within 5 minutes)
    /// </summary>
    public bool IsNearExpiry => DateTime.UtcNow.AddMinutes(5) >= ExpiresAt;
}

/// <summary>
/// User information record
/// </summary>
/// <param name="Id">Unique user identifier</param>
/// <param name="Email">User email address</param>
/// <param name="DisplayName">User display name (optional)</param>
/// <param name="AvatarUrl">User avatar URL (optional)</param>
/// <param name="Provider">Authentication provider used</param>
public sealed record UserInfo(
    string Id,
    string Email,
    string? DisplayName = null,
    string? AvatarUrl = null,
    AuthProvider? Provider = null);

/// <summary>
/// Authentication status change event arguments
/// </summary>
/// <remarks>
/// Initialize authentication status change event arguments
/// </remarks>
/// <param name="isLoggedIn">Current login status</param>
/// <param name="user">Current user information</param>
/// <param name="wasLoggedIn">Previous login status</param>
public sealed class AuthStatusChangedEventArgs(bool isLoggedIn, UserInfo? user, bool wasLoggedIn = false) : EventArgs
{
    /// <summary>
    /// Whether user is currently logged in
    /// </summary>
    public bool IsLoggedIn { get; } = isLoggedIn;

    /// <summary>
    /// Current user information (null if not logged in)
    /// </summary>
    public UserInfo? User { get; } = user;

    /// <summary>
    /// Previous authentication status
    /// </summary>
    public bool WasLoggedIn { get; } = wasLoggedIn;

    /// <summary>
    /// Timestamp of the status change
    /// </summary>
    public DateTime ChangedAt { get; } = DateTime.UtcNow;
}

/// <summary>
/// Authentication error codes for programmatic error handling
/// </summary>
public static class AuthErrorCodes
{
    public const string InvalidCredentials = "InvalidCredentials";
    public const string UserNotFound = "UserNotFound";
    public const string UserAlreadyExists = "UserAlreadyExists";
    public const string EmailNotConfirmed = "EmailNotConfirmed";
    public const string WeakPassword = "WeakPassword";
    public const string NetworkError = "NetworkError";
    public const string ServiceUnavailable = "ServiceUnavailable";
    public const string RateLimitExceeded = "RateLimitExceeded";
    public const string InvalidToken = "InvalidToken";
    public const string TokenExpired = "TokenExpired";
    public const string OAuthError = "OAuthError";
    public const string PasswordResetFailed = "PasswordResetFailed";
    public const string UnexpectedError = "UnexpectedError";

    // OAuth specific error codes
    public const string OAuthAccessDenied = "OAuthAccessDenied";
    public const string OAuthInvalidRequest = "OAuthInvalidRequest";
    public const string OAuthUnauthorizedClient = "OAuthUnauthorizedClient";
    public const string OAuthServerError = "OAuthServerError";
}
