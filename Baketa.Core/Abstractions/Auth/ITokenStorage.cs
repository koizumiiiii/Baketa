namespace Baketa.Core.Abstractions.Auth;

/// <summary>
/// Interface for secure token storage operations
/// Enables persistence of authentication tokens across application restarts
/// </summary>
public interface ITokenStorage
{
    /// <summary>
    /// Store authentication tokens securely
    /// </summary>
    /// <param name="accessToken">JWT access token</param>
    /// <param name="refreshToken">Refresh token for session renewal</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if storage was successful</returns>
    Task<bool> StoreTokensAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve stored authentication tokens
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of (accessToken, refreshToken) or null if not found</returns>
    Task<(string AccessToken, string RefreshToken)?> RetrieveTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear all stored tokens (used during logout)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if clearing was successful</returns>
    Task<bool> ClearTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if tokens are currently stored
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if tokens exist in storage</returns>
    Task<bool> HasStoredTokensAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Token storage data record
/// </summary>
/// <param name="AccessToken">JWT access token</param>
/// <param name="RefreshToken">Refresh token</param>
/// <param name="StoredAt">When tokens were stored</param>
public sealed record StoredTokens(
    string AccessToken,
    string RefreshToken,
    DateTime StoredAt);
