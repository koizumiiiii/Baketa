namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// [Issue #185] Component downloader interface for first-time setup
/// Downloads required components (translation server, models) from GitHub Releases
/// </summary>
public interface IComponentDownloader
{
    /// <summary>
    /// Download progress changed event
    /// </summary>
    event EventHandler<ComponentDownloadProgressEventArgs>? DownloadProgressChanged;

    /// <summary>
    /// Gets all required components for the application
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of required components</returns>
    Task<IReadOnlyList<ComponentInfo>> GetRequiredComponentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a component is already installed locally
    /// </summary>
    /// <param name="component">Component to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if installed, false otherwise</returns>
    Task<bool> IsComponentInstalledAsync(ComponentInfo component, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads and installs a component
    /// </summary>
    /// <param name="component">Component to download</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DownloadComponentAsync(ComponentInfo component, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads all missing components
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of components downloaded</returns>
    Task<int> DownloadMissingComponentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// [Issue #185] Ensures NLLB tokenizer.json exists, downloading from HuggingFace if missing
    /// This is needed because the CTranslate2 model package may not include the tokenizer file
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if tokenizer was downloaded, false if already exists</returns>
    Task<bool> EnsureNllbTokenizerAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a downloadable component
/// </summary>
/// <param name="Id">Unique identifier for the component</param>
/// <param name="DisplayName">User-friendly display name</param>
/// <param name="DownloadUrl">URL to download from (GitHub Releases)</param>
/// <param name="LocalPath">Local path where component will be installed</param>
/// <param name="ExpectedSizeBytes">Expected file size in bytes (for progress display)</param>
/// <param name="Checksum">Optional SHA256 checksum for verification</param>
/// <param name="IsRequired">Whether this component is required for app to function</param>
/// <param name="SplitParts">[Issue #210] Number of split parts (> 1 for files exceeding GitHub's 2GB limit)</param>
/// <param name="PartChecksums">[Issue #210] SHA256 checksums for each split part (for early failure detection)</param>
/// <param name="SplitPartSuffixFormat">[Issue #210] Format string for split part suffix</param>
public record ComponentInfo(
    string Id,
    string DisplayName,
    string DownloadUrl,
    string LocalPath,
    long ExpectedSizeBytes,
    string? Checksum = null,
    bool IsRequired = true,
    int SplitParts = 1,
    IReadOnlyList<string>? PartChecksums = null,
    string SplitPartSuffixFormat = ".{0:D3}");

/// <summary>
/// Download progress event arguments
/// </summary>
public class ComponentDownloadProgressEventArgs : EventArgs
{
    /// <summary>
    /// Component being downloaded
    /// </summary>
    public required ComponentInfo Component { get; init; }

    /// <summary>
    /// Bytes received so far
    /// </summary>
    public long BytesReceived { get; init; }

    /// <summary>
    /// Total bytes to download
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Download percentage (0-100)
    /// </summary>
    public double PercentComplete => TotalBytes > 0
        ? Math.Round((double)BytesReceived / TotalBytes * 100, 1)
        : 0;

    /// <summary>
    /// Download speed in bytes per second
    /// </summary>
    public double SpeedBytesPerSecond { get; init; }

    /// <summary>
    /// Estimated time remaining
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (SpeedBytesPerSecond <= 0 || BytesReceived >= TotalBytes)
                return null;

            var remainingBytes = TotalBytes - BytesReceived;
            var seconds = remainingBytes / SpeedBytesPerSecond;
            return TimeSpan.FromSeconds(seconds);
        }
    }

    /// <summary>
    /// Whether download is completed
    /// </summary>
    public bool IsCompleted { get; init; }

    /// <summary>
    /// Error message if download failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// [Issue #198] Status message for current operation (e.g., "Extracting files...")
    /// Takes precedence over default progress message when set
    /// </summary>
    public string? StatusMessage { get; init; }
}

/// <summary>
/// Exception thrown when a required component fails to download after all retry attempts
/// </summary>
public class ComponentDownloadException : Exception
{
    /// <summary>
    /// The component ID that failed to download
    /// </summary>
    public string ComponentId { get; }

    /// <summary>
    /// Creates a new ComponentDownloadException
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="componentId">Component ID</param>
    /// <param name="innerException">Inner exception</param>
    public ComponentDownloadException(string message, string componentId, Exception? innerException = null)
        : base(message, innerException)
    {
        ComponentId = componentId;
    }
}
