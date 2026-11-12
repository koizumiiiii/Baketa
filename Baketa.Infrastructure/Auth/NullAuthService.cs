using Baketa.Core.Abstractions.Auth;

namespace Baketa.Infrastructure.Auth;

/// <summary>No account, persisted session, OAuth listener, or remote authentication in local mode.</summary>
public sealed class NullAuthService : IAuthService, IOAuthCallbackHandler, IUserService, IJwtTokenService
{
    private static Task<AuthResult> DisabledAsync() => Task.FromResult<AuthResult>(new AuthFailure("local_only", "Authentication is disabled."));
    public Task<AuthResult> SignUpWithEmailPasswordAsync(string email, string password, Dictionary<string, object>? userMetadata = null, CancellationToken cancellationToken = default) => DisabledAsync();
    public Task<AuthResult> SignInWithEmailPasswordAsync(string email, string password, CancellationToken cancellationToken = default) => DisabledAsync();
    public Task<AuthResult> SignInWithOAuthAsync(AuthProvider provider, CancellationToken cancellationToken = default) => DisabledAsync();
    public Task<AuthResult> StartOAuthFlowAsync(AuthProvider provider, CancellationToken cancellationToken = default) => DisabledAsync();
    public Task<AuthResult> UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default) => DisabledAsync();
    public Task<AuthSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult<AuthSession?>(null);
    public Task RestoreSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> SendPasswordResetEmailAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public event EventHandler<AuthStatusChangedEventArgs> AuthStatusChanged { add { } remove { } }
    public Task<UserProfile?> GetUserProfileAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<UserProfile?>(null);
    public Task<UserProfile> UpdateUserProfileAsync(string userId, UserProfileUpdate profile, CancellationToken cancellationToken = default) => Task.FromException<UserProfile>(new NotSupportedException("Accounts are disabled."));
    public Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<UserUsageStats> GetUserUsageStatsAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(new UserUsageStats(userId, 0, 0, null, null, [], TimeSpan.Zero));
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    public Task<JwtTokenPair?> ExchangeSessionTokenAsync(string sessionToken, CancellationToken cancellationToken = default) => Task.FromResult<JwtTokenPair?>(null);
    public Task<JwtTokenPair?> RefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult<JwtTokenPair?>(null);
    public Task ClearTokensAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public bool IsNearExpiry() => false;
    public bool HasValidToken => false;
    public event EventHandler<JwtRefreshFailedEventArgs>? RefreshFailed { add { } remove { } }
    public void Dispose() { }
}
