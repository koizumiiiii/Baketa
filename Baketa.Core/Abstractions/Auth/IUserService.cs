namespace Baketa.Core.Abstractions.Auth;

/// <summary>
/// Service interface for user profile management
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Get user profile by user ID
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>User profile or null if not found</returns>
    Task<UserProfile?> GetUserProfileAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update user profile information
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="profile">Updated profile information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated user profile</returns>
    Task<UserProfile> UpdateUserProfileAsync(string userId, UserProfileUpdate profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if user exists in the system
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if user exists</returns>
    Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user usage statistics
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>User usage statistics</returns>
    Task<UserUsageStats> GetUserUsageStatsAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Extended user profile information stored in public.users table
/// </summary>
/// <param name="AuthUserId">Supabase auth user ID</param>
/// <param name="Email">User email address</param>
/// <param name="DisplayName">User display name</param>
/// <param name="AvatarUrl">User avatar URL</param>
/// <param name="FirstSeen">First login timestamp</param>
/// <param name="LastActive">Last activity timestamp</param>
/// <param name="Preferences">User preferences (JSON)</param>
/// <param name="IsActive">Whether user account is active</param>
public sealed record UserProfile(
    string AuthUserId,
    string Email,
    string? DisplayName,
    string? AvatarUrl,
    DateTime FirstSeen,
    DateTime LastActive,
    Dictionary<string, object>? Preferences = null,
    bool IsActive = true);

/// <summary>
/// User profile update model for partial updates
/// </summary>
/// <param name="DisplayName">New display name (optional)</param>
/// <param name="AvatarUrl">New avatar URL (optional)</param>
/// <param name="Preferences">Updated preferences (optional)</param>
public sealed record UserProfileUpdate(
    string? DisplayName = null,
    string? AvatarUrl = null,
    Dictionary<string, object>? Preferences = null);

/// <summary>
/// User usage statistics for analytics and licensing
/// </summary>
/// <param name="UserId">User ID</param>
/// <param name="TotalTranslations">Total number of translations</param>
/// <param name="TotalOcrOperations">Total OCR operations</param>
/// <param name="LastTranslationDate">Last translation date</param>
/// <param name="LastOcrDate">Last OCR operation date</param>
/// <param name="FavoriteLanguagePairs">Most used language pairs</param>
/// <param name="TotalSessionTime">Total time spent in application</param>
public sealed record UserUsageStats(
    string UserId,
    int TotalTranslations,
    int TotalOcrOperations,
    DateTime? LastTranslationDate,
    DateTime? LastOcrDate,
    Dictionary<string, int> FavoriteLanguagePairs,
    TimeSpan TotalSessionTime);