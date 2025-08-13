using Baketa.Core.Abstractions.Auth;

namespace Baketa.Core.Settings;

/// <summary>
/// Authentication and user management settings
/// </summary>
public sealed class AuthSettings
{
    /// <summary>
    /// Supabase project configuration
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Authentication", "Supabase Project URL", 
        Description = "Supabase project URL for authentication services")]
    public string SupabaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Supabase anonymous key (safe for client-side use)
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Authentication", "Supabase Anonymous Key", 
        Description = "Supabase anonymous key for client authentication")]
    public string SupabaseAnonKey { get; set; } = string.Empty;

    /// <summary>
    /// Whether to remember user login across app restarts
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Authentication", "Remember Login", 
        Description = "Keep user logged in after closing the application")]
    public bool RememberLogin { get; set; } = true;

    /// <summary>
    /// Session timeout in minutes (0 = no timeout)
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Authentication", "Session Timeout", 
        Description = "Automatic logout after inactivity (minutes, 0 = disabled)")]
    public int SessionTimeoutMinutes { get; set; } = 0;

    /// <summary>
    /// Enable automatic session refresh
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Authentication", "Auto Refresh Session", 
        Description = "Automatically refresh authentication tokens before expiry")]
    public bool AutoRefreshSession { get; set; } = true;

    /// <summary>
    /// Preferred OAuth providers order
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Authentication", "Preferred OAuth Providers", 
        Description = "Order of OAuth providers in the login UI")]
    public IList<AuthProvider> PreferredOAuthProviders { get; set; } = [AuthProvider.Google, AuthProvider.Discord, AuthProvider.Steam, AuthProvider.X];

    /// <summary>
    /// Enable email/password authentication
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Authentication", "Enable Email Auth", 
        Description = "Allow users to register and login with email/password")]
    public bool EnableEmailAuth { get; set; } = true;

    /// <summary>
    /// Enable OAuth authentication
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Authentication", "Enable OAuth", 
        Description = "Allow users to login with third-party OAuth providers")]
    public bool EnableOAuth { get; set; } = true;

    /// <summary>
    /// OAuth callback port for desktop app
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Authentication", "OAuth Callback Port", 
        Description = "Local port for OAuth callback handling in desktop app")]
    public int OAuthCallbackPort { get; set; } = 3000;

    /// <summary>
    /// OAuth callback timeout in seconds
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Authentication", "OAuth Callback Timeout", 
        Description = "Timeout for OAuth callback response (seconds)")]
    public int OAuthCallbackTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Enable authentication logging
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Authentication", "Enable Auth Logging", 
        Description = "Log authentication events for debugging")]
    public bool EnableAuthLogging { get; set; }

    /// <summary>
    /// User profile synchronization interval in hours
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Authentication", "Profile Sync Interval", 
        Description = "How often to sync user profile data (hours)")]
    public int ProfileSyncIntervalHours { get; set; } = 24;

    /// <summary>
    /// Enable offline mode (for future implementation)
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Authentication", "Enable Offline Mode", 
        Description = "Allow limited functionality when authentication service is unavailable")]
    public bool EnableOfflineMode { get; set; }

    /// <summary>
    /// Maximum login attempts before lockout
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Authentication", "Max Login Attempts", 
        Description = "Maximum failed login attempts before temporary lockout")]
    public int MaxLoginAttempts { get; set; } = 5;

    /// <summary>
    /// Login lockout duration in minutes
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Authentication", "Login Lockout Duration", 
        Description = "Duration of login lockout after max attempts (minutes)")]
    public int LoginLockoutMinutes { get; set; } = 15;

    /// <summary>
    /// Validate authentication settings
    /// </summary>
    /// <returns>Validation result</returns>
    public SettingsValidationResult ValidateSettings()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Required settings validation
        if (string.IsNullOrWhiteSpace(SupabaseUrl))
        {
            errors.Add("Supabase URL is required for authentication");
        }

        if (string.IsNullOrWhiteSpace(SupabaseAnonKey))
        {
            errors.Add("Supabase anonymous key is required for authentication");
        }

        // URL format validation
        if (!string.IsNullOrWhiteSpace(SupabaseUrl) && !Uri.TryCreate(SupabaseUrl, UriKind.Absolute, out _))
        {
            errors.Add("Supabase URL must be a valid URL");
        }

        // Port validation
        if (OAuthCallbackPort < 1024 || OAuthCallbackPort > 65535)
        {
            warnings.Add("OAuth callback port should be between 1024 and 65535");
        }

        // Timeout validation
        if (SessionTimeoutMinutes < 0 || SessionTimeoutMinutes > 10080) // 1 week max
        {
            warnings.Add("Session timeout should be between 0 and 10080 minutes (1 week)");
        }

        if (OAuthCallbackTimeoutSeconds < 30 || OAuthCallbackTimeoutSeconds > 600) // 10 minutes max
        {
            warnings.Add("OAuth callback timeout should be between 30 and 600 seconds");
        }

        // Provider validation
        if (!EnableEmailAuth && !EnableOAuth)
        {
            errors.Add("At least one authentication method (Email or OAuth) must be enabled");
        }

        if (EnableOAuth && (PreferredOAuthProviders?.Count ?? 0) == 0)
        {
            warnings.Add("OAuth is enabled but no OAuth providers are configured");
        }

        // Security settings validation
        if (MaxLoginAttempts < 1 || MaxLoginAttempts > 50)
        {
            warnings.Add("Max login attempts should be between 1 and 50");
        }

        if (LoginLockoutMinutes < 1 || LoginLockoutMinutes > 1440) // 24 hours max
        {
            warnings.Add("Login lockout duration should be between 1 and 1440 minutes (24 hours)");
        }

        return errors.Count > 0 
            ? SettingsValidationResult.CreateFailure(errors, warnings)
            : SettingsValidationResult.CreateSuccess(warnings);
    }
}