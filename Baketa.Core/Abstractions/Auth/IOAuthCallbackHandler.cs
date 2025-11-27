using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Auth;

/// <summary>
/// OAuth callback handler interface for desktop OAuth flows
/// Starts a local HTTP server to receive OAuth callbacks and exchange codes for sessions
/// </summary>
public interface IOAuthCallbackHandler
{
    /// <summary>
    /// Start OAuth flow with the specified provider
    /// Opens browser for authentication and waits for callback
    /// </summary>
    /// <param name="provider">OAuth provider</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication result</returns>
    Task<AuthResult> StartOAuthFlowAsync(AuthProvider provider, CancellationToken cancellationToken = default);
}
