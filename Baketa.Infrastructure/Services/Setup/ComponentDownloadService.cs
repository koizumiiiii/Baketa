using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Services.Setup;

/// <summary>
/// [Issue #185] Component download service for first-time setup
/// Downloads translation server and models from GitHub Releases
/// </summary>
public class ComponentDownloadService : IComponentDownloader
{
    private readonly ILogger<ComponentDownloadService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ComponentDownloadSettings _settings;
    private readonly string _appDataPath;
    private readonly string _appBasePath;

    /// <inheritdoc/>
    public event EventHandler<ComponentDownloadProgressEventArgs>? DownloadProgressChanged;

    public ComponentDownloadService(
        ILogger<ComponentDownloadService> logger,
        HttpClient httpClient,
        IOptions<ComponentDownloadSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        // Configure HttpClient timeout from settings
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.DownloadTimeoutSeconds);

        // %APPDATA%\Baketa for user data
        _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Baketa");

        // Application base path for bundled components
        _appBasePath = AppContext.BaseDirectory;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ComponentInfo>> GetRequiredComponentsAsync(CancellationToken cancellationToken = default)
    {
        var components = _settings.Components
            .Select(config => new ComponentInfo(
                Id: config.Id,
                DisplayName: config.DisplayName,
                DownloadUrl: $"{_settings.GitHubReleasesBaseUrl}/{_settings.ReleaseVersion}/{config.FileName}",
                LocalPath: config.UseAppData
                    ? Path.Combine(_appDataPath, config.LocalSubPath)
                    : Path.Combine(_appBasePath, config.LocalSubPath),
                ExpectedSizeBytes: config.ExpectedSizeBytes,
                Checksum: config.Checksum,
                IsRequired: config.IsRequired))
            .ToList();

        return Task.FromResult<IReadOnlyList<ComponentInfo>>(components);
    }

    /// <inheritdoc/>
    public Task<bool> IsComponentInstalledAsync(ComponentInfo component, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Find the component config to get VerificationFile
        var config = _settings.Components.FirstOrDefault(c => c.Id == component.Id);
        var verificationFile = config?.VerificationFile;

        bool isInstalled;
        if (!string.IsNullOrEmpty(verificationFile))
        {
            // Check using verification file from settings
            var verificationPath = Path.Combine(component.LocalPath, verificationFile);
            isInstalled = File.Exists(verificationPath);
        }
        else
        {
            // Fallback: check if directory exists with any files
            isInstalled = Directory.Exists(component.LocalPath) &&
                          Directory.GetFiles(component.LocalPath, "*", SearchOption.TopDirectoryOnly).Length > 0;
        }

        _logger.LogInformation(
            "Component check: {ComponentId} - {Status} (VerificationFile: {VerificationFile})",
            component.Id,
            isInstalled ? "Installed" : "Not installed",
            verificationFile ?? "N/A");

        return Task.FromResult(isInstalled);
    }

    /// <inheritdoc/>
    public async Task DownloadComponentAsync(ComponentInfo component, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting download: {ComponentId} from {Url}", component.Id, component.DownloadUrl);

        // [Issue #185] Disk space check before download
        EnsureSufficientDiskSpace(component);

        var stopwatch = Stopwatch.StartNew();
        var tempZipPath = Path.GetTempFileName();
        Exception? lastException = null;

        for (var attempt = 1; attempt <= _settings.MaxRetryAttempts; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attempt > 1)
                {
                    // Exponential backoff: 1s, 2s, 4s, ...
                    var delay = _settings.RetryBaseDelayMs * (int)Math.Pow(2, attempt - 2);
                    _logger.LogInformation(
                        "Retry attempt {Attempt}/{MaxAttempts} for {ComponentId} after {Delay}ms",
                        attempt, _settings.MaxRetryAttempts, component.Id, delay);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                // Download with progress reporting
                await DownloadFileWithProgressAsync(component, tempZipPath, cancellationToken).ConfigureAwait(false);

                // Verify checksum if provided
                if (!string.IsNullOrEmpty(component.Checksum))
                {
                    var actualChecksum = await ComputeChecksumAsync(tempZipPath, cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(actualChecksum, component.Checksum, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Checksum mismatch for {component.Id}. Expected: {component.Checksum}, Actual: {actualChecksum}");
                    }
                    _logger.LogInformation("Checksum verified for {ComponentId}", component.Id);
                }

                // Extract to destination
                await ExtractZipAsync(tempZipPath, component.LocalPath, cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();
                _logger.LogInformation(
                    "Download complete: {ComponentId} in {ElapsedMs}ms (attempt {Attempt})",
                    component.Id,
                    stopwatch.ElapsedMilliseconds,
                    attempt);

                // Report completion
                ReportProgress(component, component.ExpectedSizeBytes, component.ExpectedSizeBytes, 0, isCompleted: true);
                return; // Success - exit retry loop
            }
            catch (OperationCanceledException)
            {
                // Don't retry on cancellation
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(
                    ex,
                    "Download attempt {Attempt}/{MaxAttempts} failed for {ComponentId}: {Message}",
                    attempt, _settings.MaxRetryAttempts, component.Id, ex.Message);

                // Clean up partial download
                if (File.Exists(tempZipPath))
                {
                    try { File.Delete(tempZipPath); }
                    catch { /* ignore cleanup errors */ }
                }

                // Create new temp file for retry
                tempZipPath = Path.GetTempFileName();
            }
        }

        // All retries exhausted
        _logger.LogError(lastException, "Download failed after {MaxAttempts} attempts: {ComponentId}", _settings.MaxRetryAttempts, component.Id);
        ReportProgress(component, 0, component.ExpectedSizeBytes, 0, isCompleted: false, errorMessage: lastException?.Message);

        // For required components, throw a specific exception
        if (component.IsRequired)
        {
            throw new ComponentDownloadException(
                $"Failed to download required component '{component.DisplayName}' after {_settings.MaxRetryAttempts} attempts.",
                component.Id,
                lastException);
        }

        throw lastException ?? new InvalidOperationException($"Download failed for {component.Id}");
    }

    /// <inheritdoc/>
    public async Task<int> DownloadMissingComponentsAsync(CancellationToken cancellationToken = default)
    {
        var components = await GetRequiredComponentsAsync(cancellationToken).ConfigureAwait(false);
        var downloadCount = 0;

        foreach (var component in components)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isInstalled = await IsComponentInstalledAsync(component, cancellationToken).ConfigureAwait(false);
            if (!isInstalled)
            {
                _logger.LogInformation("Downloading missing component: {ComponentId}", component.Id);
                await DownloadComponentAsync(component, cancellationToken).ConfigureAwait(false);
                downloadCount++;
            }
        }

        _logger.LogInformation("Downloaded {Count} missing components", downloadCount);
        return downloadCount;
    }

    #region Private Methods

    /// <summary>
    /// [Issue #185] Ensures sufficient disk space is available before downloading
    /// Checks both temp directory (for download) and destination directory (for extraction)
    /// Handles same-drive scenario to avoid double-counting space requirements
    /// </summary>
    /// <param name="component">Component to download</param>
    /// <exception cref="IOException">Thrown when insufficient disk space is available</exception>
    private void EnsureSufficientDiskSpace(ComponentInfo component)
    {
        const long safetyMarginBytes = 100 * 1024 * 1024; // 100MB

        // Get drive information
        var tempPath = Path.GetTempPath();
        var tempRoot = Path.GetPathRoot(tempPath) ?? "C:\\";
        var tempDrive = new DriveInfo(tempRoot);

        var destinationRoot = Path.GetPathRoot(component.LocalPath);
        var isSameDrive = string.Equals(tempRoot, destinationRoot, StringComparison.OrdinalIgnoreCase);

        if (isSameDrive)
        {
            // Same drive: need space for both zip download AND extraction
            var requiredBytes = (component.ExpectedSizeBytes * 2) + safetyMarginBytes;
            if (tempDrive.AvailableFreeSpace < requiredBytes)
            {
                var requiredMB = requiredBytes / (1024 * 1024);
                var availableMB = tempDrive.AvailableFreeSpace / (1024 * 1024);
                var message = $"Insufficient disk space on drive ({tempDrive.Name}). " +
                             $"Required: {requiredMB:N0} MB (download + extraction), Available: {availableMB:N0} MB";
                _logger.LogError(message);
                throw new IOException(message);
            }
            _logger.LogDebug(
                "Disk space check passed for {ComponentId} (same drive). Required: {RequiredMB:N0} MB",
                component.Id,
                requiredBytes / (1024 * 1024));
        }
        else
        {
            // Different drives: check each separately
            var tempRequiredBytes = component.ExpectedSizeBytes + safetyMarginBytes;
            if (tempDrive.AvailableFreeSpace < tempRequiredBytes)
            {
                var requiredMB = tempRequiredBytes / (1024 * 1024);
                var availableMB = tempDrive.AvailableFreeSpace / (1024 * 1024);
                var message = $"Insufficient disk space on temp drive ({tempDrive.Name}). " +
                             $"Required: {requiredMB:N0} MB, Available: {availableMB:N0} MB";
                _logger.LogError(message);
                throw new IOException(message);
            }

            if (!string.IsNullOrEmpty(destinationRoot))
            {
                var destDrive = new DriveInfo(destinationRoot);
                var destRequiredBytes = component.ExpectedSizeBytes + safetyMarginBytes;
                if (destDrive.AvailableFreeSpace < destRequiredBytes)
                {
                    var requiredMB = destRequiredBytes / (1024 * 1024);
                    var availableMB = destDrive.AvailableFreeSpace / (1024 * 1024);
                    var message = $"Insufficient disk space on destination drive ({destDrive.Name}). " +
                                 $"Required: {requiredMB:N0} MB, Available: {availableMB:N0} MB";
                    _logger.LogError(message);
                    throw new IOException(message);
                }
            }

            _logger.LogDebug(
                "Disk space check passed for {ComponentId} (different drives). Temp: {TempDrive}, Dest: {DestDrive}",
                component.Id,
                tempRoot,
                destinationRoot ?? "N/A");
        }
    }

    private async Task DownloadFileWithProgressAsync(
        ComponentInfo component,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            component.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? component.ExpectedSizeBytes;
        var bytesReceived = 0L;
        var lastReportTime = Stopwatch.StartNew();
        var lastBytesReceived = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920]; // 80KB buffer
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            bytesReceived += bytesRead;

            // Report progress every 500ms
            if (lastReportTime.ElapsedMilliseconds >= 500)
            {
                var speed = (bytesReceived - lastBytesReceived) / (lastReportTime.ElapsedMilliseconds / 1000.0);
                ReportProgress(component, bytesReceived, totalBytes, speed);
                lastBytesReceived = bytesReceived;
                lastReportTime.Restart();
            }
        }
    }

    private static async Task<string> ComputeChecksumAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hashBytes);
    }

    private async Task ExtractZipAsync(string zipPath, string destinationPath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Extracting to: {DestinationPath}", destinationPath);

        // Ensure destination directory exists
        if (Directory.Exists(destinationPath))
        {
            Directory.Delete(destinationPath, recursive: true);
        }
        Directory.CreateDirectory(destinationPath);

        // Extract in background thread to avoid blocking
        await Task.Run(() =>
        {
            ZipFile.ExtractToDirectory(zipPath, destinationPath, overwriteFiles: true);
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Extraction complete: {DestinationPath}", destinationPath);
    }

    private void ReportProgress(
        ComponentInfo component,
        long bytesReceived,
        long totalBytes,
        double speedBytesPerSecond,
        bool isCompleted = false,
        string? errorMessage = null)
    {
        DownloadProgressChanged?.Invoke(this, new ComponentDownloadProgressEventArgs
        {
            Component = component,
            BytesReceived = bytesReceived,
            TotalBytes = totalBytes,
            SpeedBytesPerSecond = speedBytesPerSecond,
            IsCompleted = isCompleted,
            ErrorMessage = errorMessage
        });
    }

    #endregion
}
