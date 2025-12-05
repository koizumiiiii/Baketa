using Microsoft.Extensions.Options;

namespace Baketa.Core.Settings;

/// <summary>
/// [Issue #185] Component download settings
/// Configured in appsettings.json under "ComponentDownload" section
/// </summary>
public class ComponentDownloadSettings
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "ComponentDownload";

    /// <summary>
    /// GitHub Releases base URL
    /// </summary>
    public string GitHubReleasesBaseUrl { get; set; } = "https://github.com/koizumiiiii/Baketa/releases/download";

    /// <summary>
    /// Release version tag (e.g., "v0.1.0")
    /// </summary>
    public string ReleaseVersion { get; set; } = "v0.1.0";

    /// <summary>
    /// Maximum retry attempts for download failures
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay in milliseconds between retry attempts (exponential backoff)
    /// </summary>
    public int RetryBaseDelayMs { get; set; } = 1000;

    /// <summary>
    /// Download timeout in seconds
    /// </summary>
    public int DownloadTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// List of components to download
    /// </summary>
    public List<ComponentConfig> Components { get; set; } = [];
}

/// <summary>
/// Individual component configuration
/// </summary>
public class ComponentConfig
{
    /// <summary>
    /// Unique identifier for the component
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// User-friendly display name
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Filename in GitHub Releases
    /// </summary>
    public string FileName { get; set; } = "";

    /// <summary>
    /// Local subdirectory path (relative to app base or %APPDATA%\Baketa)
    /// </summary>
    public string LocalSubPath { get; set; } = "";

    /// <summary>
    /// Whether to install to %APPDATA%\Baketa (true) or app base directory (false)
    /// </summary>
    public bool UseAppData { get; set; } = false;

    /// <summary>
    /// Expected file size in bytes (for progress display)
    /// </summary>
    public long ExpectedSizeBytes { get; set; }

    /// <summary>
    /// SHA256 checksum for verification (null to skip verification)
    /// </summary>
    public string? Checksum { get; set; }

    /// <summary>
    /// Whether this component is required for app to function
    /// </summary>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// File to check for determining if component is installed
    /// </summary>
    public string? VerificationFile { get; set; }
}

/// <summary>
/// [Issue #185] Validates ComponentDownloadSettings at startup
/// Implements IValidateOptions pattern for eager validation
/// </summary>
public class ComponentDownloadSettingsValidator : IValidateOptions<ComponentDownloadSettings>
{
    public ValidateOptionsResult Validate(string? name, ComponentDownloadSettings options)
    {
        var failures = new List<string>();

        // Validate GitHubReleasesBaseUrl
        if (string.IsNullOrWhiteSpace(options.GitHubReleasesBaseUrl))
        {
            failures.Add("GitHubReleasesBaseUrl is required");
        }
        else if (!Uri.TryCreate(options.GitHubReleasesBaseUrl, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"GitHubReleasesBaseUrl must be a valid HTTP(S) URL: {options.GitHubReleasesBaseUrl}");
        }

        // Validate ReleaseVersion
        if (string.IsNullOrWhiteSpace(options.ReleaseVersion))
        {
            failures.Add("ReleaseVersion is required");
        }

        // Validate MaxRetryAttempts (1-10)
        if (options.MaxRetryAttempts < 1 || options.MaxRetryAttempts > 10)
        {
            failures.Add($"MaxRetryAttempts must be between 1 and 10, but was {options.MaxRetryAttempts}");
        }

        // Validate RetryBaseDelayMs (100-60000)
        if (options.RetryBaseDelayMs < 100 || options.RetryBaseDelayMs > 60000)
        {
            failures.Add($"RetryBaseDelayMs must be between 100 and 60000, but was {options.RetryBaseDelayMs}");
        }

        // Validate DownloadTimeoutSeconds (30-3600)
        if (options.DownloadTimeoutSeconds < 30 || options.DownloadTimeoutSeconds > 3600)
        {
            failures.Add($"DownloadTimeoutSeconds must be between 30 and 3600, but was {options.DownloadTimeoutSeconds}");
        }

        // Validate Components
        if (options.Components.Count == 0)
        {
            failures.Add("At least one component must be configured");
        }

        var componentIds = new HashSet<string>();
        for (var i = 0; i < options.Components.Count; i++)
        {
            var component = options.Components[i];
            var prefix = $"Components[{i}]";

            if (string.IsNullOrWhiteSpace(component.Id))
            {
                failures.Add($"{prefix}.Id is required");
            }
            else if (!componentIds.Add(component.Id))
            {
                failures.Add($"{prefix}.Id '{component.Id}' is duplicated");
            }

            if (string.IsNullOrWhiteSpace(component.DisplayName))
            {
                failures.Add($"{prefix}.DisplayName is required");
            }

            if (string.IsNullOrWhiteSpace(component.FileName))
            {
                failures.Add($"{prefix}.FileName is required");
            }

            if (string.IsNullOrWhiteSpace(component.LocalSubPath))
            {
                failures.Add($"{prefix}.LocalSubPath is required");
            }

            if (component.ExpectedSizeBytes <= 0)
            {
                failures.Add($"{prefix}.ExpectedSizeBytes must be greater than 0");
            }

            // Validate checksum format if provided (SHA256 = 64 hex characters)
            if (!string.IsNullOrEmpty(component.Checksum) && component.Checksum.Length != 64)
            {
                failures.Add($"{prefix}.Checksum must be a 64-character SHA256 hash, but was {component.Checksum.Length} characters");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
