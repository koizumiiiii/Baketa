namespace Baketa.Core.Abstractions.Auth;

/// <summary>
/// Interface for token operation audit logging
/// Records all token-related security events for compliance and incident investigation
/// </summary>
public interface ITokenAuditLogger
{
    /// <summary>
    /// Log when a new token is issued (initial login)
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="expiresAt">Token expiration time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task LogTokenIssuedAsync(
        string userId,
        DateTime expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Log when a token is refreshed
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="oldExpiry">Previous expiration time</param>
    /// <param name="newExpiry">New expiration time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task LogTokenRefreshedAsync(
        string userId,
        DateTime oldExpiry,
        DateTime newExpiry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Log when a token is revoked (logout or forced invalidation)
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="reason">Reason for revocation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task LogTokenRevokedAsync(
        string userId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Log when token validation fails
    /// </summary>
    /// <param name="reason">Reason for validation failure</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task LogTokenValidationFailedAsync(
        string reason,
        CancellationToken cancellationToken = default);
}
