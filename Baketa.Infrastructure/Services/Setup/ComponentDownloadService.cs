using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Baketa.Core.Abstractions.GPU;
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
    private readonly IGpuEnvironmentDetector? _gpuDetector;
    private readonly string _appDataPath;
    private readonly string _appBasePath;

    // [Issue #292] Unified AI Server component ID for GPU-aware download
    // (Replaces surya_ocr_server from models-v1)
    private const string UnifiedServerComponentId = "unified_server";

    // [Issue #185] HuggingFace NLLB tokenizer constants
    private const string HuggingFaceTokenizerUrl = "https://huggingface.co/facebook/nllb-200-distilled-600M/resolve/main/tokenizer.json";
    private const long TokenizerExpectedSizeBytes = 17_331_176; // ~17.3MB
    private const string NllbModelComponentId = "nllb_model";

    // [Issue #210] Cached GPU detection result
    private bool? _cachedSupportsCuda;

    // [Issue #360] Component metadata file name for version tracking
    private const string MetadataFileName = ".baketa-component-metadata.json";

    /// <inheritdoc/>
    public event EventHandler<ComponentDownloadProgressEventArgs>? DownloadProgressChanged;

    public ComponentDownloadService(
        ILogger<ComponentDownloadService> logger,
        HttpClient httpClient,
        IOptions<ComponentDownloadSettings> settings,
        IGpuEnvironmentDetector? gpuDetector = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _gpuDetector = gpuDetector;

        // Configure HttpClient timeout from settings
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.DownloadTimeoutSeconds);

        // %APPDATA%\Baketa for user data
        _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Baketa");

        // Application base path for bundled components
        _appBasePath = AppContext.BaseDirectory;

        _logger.LogInformation("[Issue #210] ComponentDownloadService initialized (GPU detector: {HasGpuDetector})",
            _gpuDetector != null ? "Available" : "Not available");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ComponentInfo>> GetRequiredComponentsAsync(CancellationToken cancellationToken = default)
    {
        // [Issue #292] Detect GPU capabilities for Unified AI Server selection
        var supportsCuda = await DetectCudaSupportAsync(cancellationToken).ConfigureAwait(false);

        var components = _settings.Components
            .Select(config => CreateComponentInfo(config, supportsCuda))
            .ToList();

        return components;
    }

    /// <summary>
    /// [Issue #210] Creates ComponentInfo with GPU-aware filename selection
    /// </summary>
    private ComponentInfo CreateComponentInfo(ComponentConfig config, bool supportsCuda)
    {
        var fileName = config.FileName;
        var expectedSize = config.ExpectedSizeBytes;
        var checksum = config.Checksum;
        var splitParts = 1;
        IReadOnlyList<string>? partChecksums = null;
        var splitPartSuffixFormat = config.SplitPartSuffixFormat;

        // [Issue #292] For Unified AI Server, select CUDA or CPU version based on GPU detection
        if (config.Id == UnifiedServerComponentId && supportsCuda && !string.IsNullOrEmpty(config.CudaFileName))
        {
            fileName = config.CudaFileName;
            expectedSize = config.CudaExpectedSizeBytes ?? config.ExpectedSizeBytes;
            checksum = config.CudaChecksum ?? config.Checksum;
            splitParts = config.CudaSplitParts ?? 1;
            partChecksums = config.CudaPartChecksums;

            _logger.LogInformation(
                "[Issue #210] GPU detected with CUDA support - selecting CUDA version: {FileName} ({SizeMB:N0} MB, {SplitParts} parts)",
                fileName, expectedSize / (1024 * 1024), splitParts);
        }
        else if (config.Id == UnifiedServerComponentId)
        {
            _logger.LogInformation(
                "[Issue #210] No CUDA support detected - selecting CPU version: {FileName} ({SizeMB:N0} MB)",
                fileName, expectedSize / (1024 * 1024));
        }

        return new ComponentInfo(
            Id: config.Id,
            DisplayName: config.DisplayName,
            DownloadUrl: $"{_settings.GitHubReleasesBaseUrl}/{_settings.ReleaseVersion}/{fileName}",
            LocalPath: config.UseAppData
                ? Path.Combine(_appDataPath, config.LocalSubPath)
                : Path.Combine(_appBasePath, config.LocalSubPath),
            ExpectedSizeBytes: expectedSize,
            Checksum: checksum,
            IsRequired: config.IsRequired,
            SplitParts: splitParts,
            PartChecksums: partChecksums,
            SplitPartSuffixFormat: splitPartSuffixFormat);
    }

    /// <summary>
    /// [Issue #210] Detects CUDA support using IGpuEnvironmentDetector
    /// Falls back to false if detector is not available or detection fails
    /// </summary>
    private async Task<bool> DetectCudaSupportAsync(CancellationToken cancellationToken)
    {
        // Return cached result if available
        if (_cachedSupportsCuda.HasValue)
        {
            return _cachedSupportsCuda.Value;
        }

        if (_gpuDetector == null)
        {
            _logger.LogWarning("[Issue #210] GPU detector not available - defaulting to CPU version");
            _cachedSupportsCuda = false;
            return false;
        }

        try
        {
            var gpuInfo = await _gpuDetector.DetectEnvironmentAsync(cancellationToken).ConfigureAwait(false);
            _cachedSupportsCuda = gpuInfo.SupportsCuda;

            _logger.LogInformation(
                "[Issue #210] GPU detection complete - GPU: {GpuName}, CUDA: {SupportsCuda}, VRAM: {VramMB} MB",
                gpuInfo.GpuName,
                gpuInfo.SupportsCuda,
                gpuInfo.AvailableMemoryMB);

            return gpuInfo.SupportsCuda;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #210] GPU detection failed - defaulting to CPU version");
            _cachedSupportsCuda = false;
            return false;
        }
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

                // [Issue #210] Handle split files for large CUDA downloads
                // [Issue #360] Track Last-Modified for version checking
                DateTimeOffset? lastModified = null;

                if (component.SplitParts > 1)
                {
                    lastModified = await DownloadAndConcatenateSplitFilesAsync(component, tempZipPath, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // Download with progress reporting
                    lastModified = await DownloadFileWithProgressAsync(component, tempZipPath, cancellationToken).ConfigureAwait(false);
                }

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

                // [Issue #198] Report extraction start - notify UI that extraction is in progress
                ReportProgress(
                    component,
                    component.ExpectedSizeBytes,
                    component.ExpectedSizeBytes,
                    0,
                    isCompleted: false,
                    statusMessage: $"{component.DisplayName} を展開しています... (数分かかる場合があります)");

                // Extract to destination with error handling
                try
                {
                    await ExtractZipAsync(tempZipPath, component.LocalPath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // [Issue #198] Report extraction failure to UI
                    ReportProgress(
                        component,
                        component.ExpectedSizeBytes,
                        component.ExpectedSizeBytes,
                        0,
                        isCompleted: false,
                        errorMessage: $"展開に失敗しました: {ex.Message}",
                        statusMessage: "エラー: 展開に失敗しました");
                    throw;
                }

                // [Issue #360] Save metadata for version tracking
                if (lastModified.HasValue)
                {
                    await SaveComponentMetadataAsync(component, lastModified.Value, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // If no Last-Modified was available, use current time
                    await SaveComponentMetadataAsync(component, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
                }

                stopwatch.Stop();
                _logger.LogInformation(
                    "Download and extraction complete: {ComponentId} in {ElapsedMs}ms (attempt {Attempt})",
                    component.Id,
                    stopwatch.ElapsedMilliseconds,
                    attempt);

                // [Issue #198] Report completion - installation is now complete
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

        // [Issue #213] 並列ダウンロードの最適化
        // まず各コンポーネントのインストール状態を並列にチェック
        var installCheckTasks = components.Select(async component =>
        {
            var isInstalled = await IsComponentInstalledAsync(component, cancellationToken).ConfigureAwait(false);
            return (component, isInstalled);
        });
        var checkResults = await Task.WhenAll(installCheckTasks).ConfigureAwait(false);

        // 未インストールのコンポーネントを抽出
        var missingComponents = checkResults
            .Where(r => !r.isInstalled)
            .Select(r => r.component)
            .ToList();

        if (missingComponents.Count == 0)
        {
            _logger.LogInformation("All components are already installed");
            return 0;
        }

        _logger.LogInformation("Found {Count} missing components to download: {Components}",
            missingComponents.Count,
            string.Join(", ", missingComponents.Select(c => c.Id)));

        // [Issue #213] 並列ダウンロード（設定ファイルから読み込み、デフォルト2）
        // [Gemini Review] maxConcurrentDownloadsを設定可能に
        var maxConcurrentDownloads = _settings.MaxConcurrentDownloads > 0 ? _settings.MaxConcurrentDownloads : 2;
        using var semaphore = new SemaphoreSlim(maxConcurrentDownloads);
        var downloadCount = 0;
        var failedComponents = new List<(ComponentInfo Component, Exception Exception)>();
        var failedComponentsLock = new object();

        // [Gemini Review] 経過時間ログ出力
        var totalStopwatch = Stopwatch.StartNew();

        var downloadTasks = missingComponents.Select(async component =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            var componentStopwatch = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogInformation("Downloading component: {ComponentId}", component.Id);
                await DownloadComponentAsync(component, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref downloadCount);
                componentStopwatch.Stop();
                _logger.LogInformation("Completed download: {ComponentId} in {ElapsedMs}ms", component.Id, componentStopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                throw; // キャンセルは再スロー
            }
            catch (Exception ex)
            {
                // [Gemini Review] 部分的成功許容: 失敗したコンポーネントを記録して続行
                componentStopwatch.Stop();
                _logger.LogWarning(ex, "Failed to download component: {ComponentId} after {ElapsedMs}ms", component.Id, componentStopwatch.ElapsedMilliseconds);
                lock (failedComponentsLock)
                {
                    failedComponents.Add((component, ex));
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(downloadTasks).ConfigureAwait(false);

        totalStopwatch.Stop();
        _logger.LogInformation("[Gemini Review] Downloaded {SuccessCount}/{TotalCount} components in {ElapsedMs}ms (並列数: {Concurrency})",
            downloadCount, missingComponents.Count, totalStopwatch.ElapsedMilliseconds, maxConcurrentDownloads);

        // [Gemini Review] 部分的失敗の場合、エラーをログに出力
        if (failedComponents.Count > 0)
        {
            var requiredFailures = failedComponents.Where(f => f.Component.IsRequired).ToList();
            if (requiredFailures.Count > 0)
            {
                // 必須コンポーネントが失敗した場合は例外をスロー
                var failedIds = string.Join(", ", requiredFailures.Select(f => f.Component.Id));
                _logger.LogError("Required components failed to download: {FailedIds}", failedIds);
                throw new AggregateException(
                    $"Failed to download required components: {failedIds}",
                    requiredFailures.Select(f => f.Exception));
            }
            else
            {
                // オプションのコンポーネントのみ失敗した場合は警告のみ
                var failedIds = string.Join(", ", failedComponents.Select(f => f.Component.Id));
                _logger.LogWarning("Optional components failed to download: {FailedIds}. Continuing with partial success.", failedIds);
            }
        }

        return downloadCount;
    }

    /// <inheritdoc/>
    public async Task<bool> EnsureNllbTokenizerAsync(CancellationToken cancellationToken = default)
    {
        // Find nllb_model component to get its local path
        var nllbConfig = _settings.Components.FirstOrDefault(c => c.Id == NllbModelComponentId);
        if (nllbConfig == null)
        {
            _logger.LogWarning("[Issue #185] nllb_model component not configured, skipping tokenizer check");
            return false;
        }

        // Resolve the model path
        var modelPath = nllbConfig.UseAppData
            ? Path.Combine(_appDataPath, nllbConfig.LocalSubPath)
            : Path.Combine(_appBasePath, nllbConfig.LocalSubPath);

        var tokenizerPath = Path.Combine(modelPath, "tokenizer.json");

        // Check if tokenizer already exists
        if (File.Exists(tokenizerPath))
        {
            _logger.LogInformation("[Issue #185] tokenizer.json already exists: {Path}", tokenizerPath);
            return false;
        }

        // Ensure model directory exists
        if (!Directory.Exists(modelPath))
        {
            _logger.LogWarning("[Issue #185] NLLB model directory does not exist: {Path}", modelPath);
            return false;
        }

        _logger.LogInformation("[Issue #185] Downloading tokenizer.json from HuggingFace...");

        // Create a pseudo-component for progress reporting
        var tokenizerComponent = new ComponentInfo(
            Id: "nllb_tokenizer",
            DisplayName: "NLLB Tokenizer",
            DownloadUrl: HuggingFaceTokenizerUrl,
            LocalPath: modelPath,
            ExpectedSizeBytes: TokenizerExpectedSizeBytes,
            IsRequired: true);

        var tempPath = Path.GetTempFileName();
        try
        {
            // Download with progress reporting (using shared download method)
            await DownloadFileWithProgressAsync(tokenizerComponent, tempPath, cancellationToken).ConfigureAwait(false);

            // Move to final destination
            File.Move(tempPath, tokenizerPath, overwrite: true);

            _logger.LogInformation("[Issue #185] tokenizer.json downloaded successfully: {Path}", tokenizerPath);
            ReportProgress(tokenizerComponent, TokenizerExpectedSizeBytes, TokenizerExpectedSizeBytes, 0, isCompleted: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #185] Failed to download tokenizer.json from HuggingFace");
            ReportProgress(tokenizerComponent, 0, TokenizerExpectedSizeBytes, 0, isCompleted: false, errorMessage: ex.Message);

            // Clean up temp file
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch { /* ignore */ }
            }

            throw new ComponentDownloadException(
                $"Failed to download NLLB tokenizer from HuggingFace: {ex.Message}",
                "nllb_tokenizer",
                ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsComponentUpToDateAsync(ComponentInfo component, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // First check if component is installed at all
        var isInstalled = await IsComponentInstalledAsync(component, cancellationToken).ConfigureAwait(false);
        if (!isInstalled)
        {
            _logger.LogDebug("[Issue #360] Component not installed: {ComponentId}", component.Id);
            return false;
        }

        // Read local metadata
        var metadataPath = Path.Combine(component.LocalPath, MetadataFileName);
        var localMetadata = await ReadComponentMetadataAsync(metadataPath, cancellationToken).ConfigureAwait(false);

        if (localMetadata == null)
        {
            // No metadata found - assume outdated for safety
            _logger.LogInformation(
                "[Issue #360] No metadata found for {ComponentId}, assuming outdated",
                component.Id);
            return false;
        }

        // Fetch remote Last-Modified using HEAD request
        try
        {
            var remoteLastModified = await GetRemoteLastModifiedAsync(component.DownloadUrl, cancellationToken).ConfigureAwait(false);

            if (remoteLastModified == null)
            {
                // Cannot determine remote version - assume up to date to avoid unnecessary downloads
                _logger.LogWarning(
                    "[Issue #360] Cannot get remote Last-Modified for {ComponentId}, assuming up-to-date",
                    component.Id);
                return true;
            }

            var isUpToDate = localMetadata.LastModified >= remoteLastModified.Value;

            _logger.LogInformation(
                "[Issue #360] Version check for {ComponentId}: Local={LocalDate:s}, Remote={RemoteDate:s}, UpToDate={IsUpToDate}",
                component.Id,
                localMetadata.LastModified,
                remoteLastModified.Value,
                isUpToDate);

            return isUpToDate;
        }
        catch (HttpRequestException ex)
        {
            // Network error - assume up to date to avoid blocking app startup
            _logger.LogWarning(
                ex,
                "[Issue #360] Network error checking version for {ComponentId}, assuming up-to-date",
                component.Id);
            return true;
        }
    }

    /// <inheritdoc/>
    public async Task<int> DownloadMissingOrOutdatedComponentsAsync(CancellationToken cancellationToken = default)
    {
        var components = await GetRequiredComponentsAsync(cancellationToken).ConfigureAwait(false);

        // Check each component's status (installed AND up-to-date)
        var checkTasks = components.Select(async component =>
        {
            var isInstalled = await IsComponentInstalledAsync(component, cancellationToken).ConfigureAwait(false);
            if (!isInstalled)
            {
                return (component, needsDownload: true, reason: "not installed");
            }

            var isUpToDate = await IsComponentUpToDateAsync(component, cancellationToken).ConfigureAwait(false);
            return (component, needsDownload: !isUpToDate, reason: isUpToDate ? "up-to-date" : "outdated");
        });

        var checkResults = await Task.WhenAll(checkTasks).ConfigureAwait(false);

        // Filter components that need download
        var componentsToDownload = checkResults
            .Where(r => r.needsDownload)
            .Select(r => (r.component, r.reason))
            .ToList();

        if (componentsToDownload.Count == 0)
        {
            _logger.LogInformation("[Issue #360] All components are installed and up-to-date");
            return 0;
        }

        _logger.LogInformation(
            "[Issue #360] Found {Count} components to download/update: {Components}",
            componentsToDownload.Count,
            string.Join(", ", componentsToDownload.Select(c => $"{c.component.Id} ({c.reason})")));

        // Download using existing parallel download logic
        var maxConcurrentDownloads = _settings.MaxConcurrentDownloads > 0 ? _settings.MaxConcurrentDownloads : 2;
        using var semaphore = new SemaphoreSlim(maxConcurrentDownloads);
        var downloadCount = 0;
        var failedComponents = new List<(ComponentInfo Component, Exception Exception)>();
        var failedComponentsLock = new object();
        var totalStopwatch = Stopwatch.StartNew();

        var downloadTasks = componentsToDownload.Select(async item =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            var componentStopwatch = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogInformation(
                    "[Issue #360] Downloading component: {ComponentId} (reason: {Reason})",
                    item.component.Id, item.reason);
                await DownloadComponentAsync(item.component, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref downloadCount);
                componentStopwatch.Stop();
                _logger.LogInformation(
                    "[Issue #360] Completed download: {ComponentId} in {ElapsedMs}ms",
                    item.component.Id, componentStopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                componentStopwatch.Stop();
                _logger.LogWarning(
                    ex,
                    "[Issue #360] Failed to download component: {ComponentId} after {ElapsedMs}ms",
                    item.component.Id, componentStopwatch.ElapsedMilliseconds);
                lock (failedComponentsLock)
                {
                    failedComponents.Add((item.component, ex));
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(downloadTasks).ConfigureAwait(false);

        totalStopwatch.Stop();
        _logger.LogInformation(
            "[Issue #360] Downloaded {SuccessCount}/{TotalCount} components in {ElapsedMs}ms",
            downloadCount, componentsToDownload.Count, totalStopwatch.ElapsedMilliseconds);

        // Handle failures
        if (failedComponents.Count > 0)
        {
            var requiredFailures = failedComponents.Where(f => f.Component.IsRequired).ToList();
            if (requiredFailures.Count > 0)
            {
                var failedIds = string.Join(", ", requiredFailures.Select(f => f.Component.Id));
                _logger.LogError("[Issue #360] Required components failed to download: {FailedIds}", failedIds);
                throw new AggregateException(
                    $"Failed to download required components: {failedIds}",
                    requiredFailures.Select(f => f.Exception));
            }
            else
            {
                var failedIds = string.Join(", ", failedComponents.Select(f => f.Component.Id));
                _logger.LogWarning(
                    "[Issue #360] Optional components failed to download: {FailedIds}. Continuing with partial success.",
                    failedIds);
            }
        }

        return downloadCount;
    }

    #region Private Methods

    /// <summary>
    /// [Issue #210] Downloads and concatenates split files for large CUDA downloads
    /// Files are named using SplitPartSuffixFormat (default: {DownloadUrl}.001, .002, etc.)
    /// Includes part-level checksum verification for early failure detection
    /// [Issue #360] Returns Last-Modified for version tracking
    /// </summary>
    /// <returns>Last-Modified timestamp from the first part's response headers</returns>
    private async Task<DateTimeOffset?> DownloadAndConcatenateSplitFilesAsync(
        ComponentInfo component,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[Issue #210] Downloading split files: {ComponentId} ({Parts} parts, suffix format: {Format})",
            component.Id, component.SplitParts, component.SplitPartSuffixFormat);

        var tempParts = new List<string>();
        var totalDownloaded = 0L;
        var downloadStartTime = Stopwatch.StartNew();
        DateTimeOffset? lastModified = null; // [Issue #360] Track from first part

        try
        {
            // Download each part
            for (var i = 1; i <= component.SplitParts; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // [Issue #210] Use configurable suffix format
                var partSuffix = string.Format(component.SplitPartSuffixFormat, i);
                var partUrl = $"{component.DownloadUrl}{partSuffix}";
                var partPath = Path.GetTempFileName();
                tempParts.Add(partPath);

                _logger.LogInformation(
                    "[Issue #210] Downloading part {Part}/{Total}: {Url}",
                    i, component.SplitParts, partUrl);

                ReportProgress(
                    component,
                    totalDownloaded,
                    component.ExpectedSizeBytes,
                    0,
                    isCompleted: false,
                    statusMessage: $"{component.DisplayName} をダウンロード中... (パート {i}/{component.SplitParts})");

                // Download this part
                using var response = await _httpClient.GetAsync(
                    partUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                // [Issue #360] Capture Last-Modified from first part
                if (i == 1)
                {
                    lastModified = response.Content.Headers.LastModified ?? response.Headers.Date;
                }

                var partSize = response.Content.Headers.ContentLength ?? 0;
                var partBytesReceived = 0L;
                var lastReportTime = Stopwatch.StartNew();
                var lastBytesForSpeed = 0L;

                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    partBytesReceived += bytesRead;

                    // [Issue #210] Real-time progress reporting every 500ms
                    if (lastReportTime.ElapsedMilliseconds >= 500)
                    {
                        var elapsedSeconds = lastReportTime.ElapsedMilliseconds / 1000.0;
                        var speed = (partBytesReceived - lastBytesForSpeed) / elapsedSeconds;
                        var progressPercent = component.ExpectedSizeBytes > 0
                            ? (totalDownloaded + partBytesReceived) * 100.0 / component.ExpectedSizeBytes
                            : 0;

                        ReportProgress(
                            component,
                            totalDownloaded + partBytesReceived,
                            component.ExpectedSizeBytes,
                            speed,
                            isCompleted: false,
                            statusMessage: $"{component.DisplayName} をダウンロード中... (パート {i}/{component.SplitParts}, {progressPercent:F1}%)");

                        lastBytesForSpeed = partBytesReceived;
                        lastReportTime.Restart();
                    }
                }

                totalDownloaded += partBytesReceived;
                _logger.LogInformation(
                    "[Issue #210] Part {Part}/{Total} downloaded: {SizeMB:N0} MB",
                    i, component.SplitParts, partBytesReceived / (1024 * 1024));

                // [Issue #210] Verify part checksum for early failure detection
                if (component.PartChecksums != null && component.PartChecksums.Count >= i)
                {
                    var expectedChecksum = component.PartChecksums[i - 1];
                    if (!string.IsNullOrEmpty(expectedChecksum))
                    {
                        ReportProgress(
                            component,
                            totalDownloaded,
                            component.ExpectedSizeBytes,
                            0,
                            isCompleted: false,
                            statusMessage: $"{component.DisplayName} パート {i} を検証中...");

                        // Need to close fileStream before computing checksum
                        await fileStream.DisposeAsync().ConfigureAwait(false);

                        var actualChecksum = await ComputeChecksumAsync(partPath, cancellationToken).ConfigureAwait(false);
                        if (!string.Equals(actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogError(
                                "[Issue #210] Part {Part} checksum mismatch. Expected: {Expected}, Actual: {Actual}",
                                i, expectedChecksum, actualChecksum);
                            throw new InvalidOperationException(
                                $"Checksum mismatch for part {i} of {component.Id}. Expected: {expectedChecksum}, Actual: {actualChecksum}");
                        }
                        _logger.LogInformation("[Issue #210] Part {Part} checksum verified", i);
                    }
                }
            }

            // Concatenate all parts with progress reporting
            _logger.LogInformation("[Issue #210] Concatenating {Parts} parts...", component.SplitParts);

            await using var outputStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var concatenatedBytes = 0L;
            var concatStartTime = Stopwatch.StartNew();

            for (var partIndex = 0; partIndex < tempParts.Count; partIndex++)
            {
                var partPath = tempParts[partIndex];
                await using var partStream = File.OpenRead(partPath);

                var buffer = new byte[81920];
                int bytesRead;
                var lastConcatReportTime = Stopwatch.StartNew();

                while ((bytesRead = await partStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await outputStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    concatenatedBytes += bytesRead;

                    // [Issue #210] Progress reporting during concatenation
                    if (lastConcatReportTime.ElapsedMilliseconds >= 500)
                    {
                        var concatPercent = totalDownloaded > 0 ? concatenatedBytes * 100.0 / totalDownloaded : 0;
                        ReportProgress(
                            component,
                            component.ExpectedSizeBytes,
                            component.ExpectedSizeBytes,
                            0,
                            isCompleted: false,
                            statusMessage: $"{component.DisplayName} を結合しています... ({concatPercent:F1}%)");
                        lastConcatReportTime.Restart();
                    }
                }
            }

            var totalElapsedSeconds = downloadStartTime.Elapsed.TotalSeconds;
            var averageSpeed = totalDownloaded / totalElapsedSeconds;

            _logger.LogInformation(
                "[Issue #210] Split file concatenation complete: {TotalMB:N0} MB in {Seconds:F1}s ({SpeedMBps:F1} MB/s)",
                totalDownloaded / (1024 * 1024),
                totalElapsedSeconds,
                averageSpeed / (1024 * 1024));

            return lastModified;
        }
        finally
        {
            // Clean up temp parts
            foreach (var partPath in tempParts)
            {
                try { File.Delete(partPath); }
                catch { /* ignore cleanup errors */ }
            }
        }
    }

    /// <summary>
    /// [Issue #185] Ensures sufficient disk space is available before downloading
    /// Checks both temp directory (for download) and destination directory (for extraction)
    /// Handles same-drive scenario to avoid double-counting space requirements
    /// [Issue #210] For split files, accounts for 2x temp space (parts + concatenated file)
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

        // [Issue #210] For split files, temp needs 2x space (all parts + concatenated file exist simultaneously during concatenation)
        var tempMultiplier = component.SplitParts > 1 ? 2 : 1;

        if (isSameDrive)
        {
            // Same drive: need space for both zip download AND extraction
            // For split files: (parts + concatenated) + extraction = 3x
            // For single files: download + extraction = 2x
            var requiredBytes = (component.ExpectedSizeBytes * (tempMultiplier + 1)) + safetyMarginBytes;
            if (tempDrive.AvailableFreeSpace < requiredBytes)
            {
                var requiredMB = requiredBytes / (1024 * 1024);
                var availableMB = tempDrive.AvailableFreeSpace / (1024 * 1024);
                var splitInfo = component.SplitParts > 1 ? $" ({component.SplitParts}パート分割)" : "";
                var message = $"Insufficient disk space on drive ({tempDrive.Name}){splitInfo}. " +
                             $"Required: {requiredMB:N0} MB (download + extraction), Available: {availableMB:N0} MB";
                _logger.LogError(message);
                throw new IOException(message);
            }
            _logger.LogDebug(
                "Disk space check passed for {ComponentId} (same drive, split={SplitParts}). Required: {RequiredMB:N0} MB",
                component.Id,
                component.SplitParts,
                requiredBytes / (1024 * 1024));
        }
        else
        {
            // Different drives: check each separately
            // For split files: temp needs 2x space (parts + concatenated file)
            var tempRequiredBytes = (component.ExpectedSizeBytes * tempMultiplier) + safetyMarginBytes;
            if (tempDrive.AvailableFreeSpace < tempRequiredBytes)
            {
                var requiredMB = tempRequiredBytes / (1024 * 1024);
                var availableMB = tempDrive.AvailableFreeSpace / (1024 * 1024);
                var splitInfo = component.SplitParts > 1 ? $" ({component.SplitParts}パート分割)" : "";
                var message = $"Insufficient disk space on temp drive ({tempDrive.Name}){splitInfo}. " +
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
                "Disk space check passed for {ComponentId} (different drives, split={SplitParts}). Temp: {TempDrive}, Dest: {DestDrive}",
                component.Id,
                component.SplitParts,
                tempRoot,
                destinationRoot ?? "N/A");
        }
    }

    /// <summary>
    /// [Issue #185] Downloads a file with progress reporting
    /// [Issue #360] Returns Last-Modified for version tracking
    /// </summary>
    /// <param name="component">Component info containing download URL and expected size</param>
    /// <param name="destinationPath">Local path to save the downloaded file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Last-Modified timestamp from response headers</returns>
    private async Task<DateTimeOffset?> DownloadFileWithProgressAsync(
        ComponentInfo component,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            component.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        // [Issue #360] Capture Last-Modified for version tracking
        var lastModified = response.Content.Headers.LastModified ?? response.Headers.Date;

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

        return lastModified;
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
        string? errorMessage = null,
        string? statusMessage = null)
    {
        DownloadProgressChanged?.Invoke(this, new ComponentDownloadProgressEventArgs
        {
            Component = component,
            BytesReceived = bytesReceived,
            TotalBytes = totalBytes,
            SpeedBytesPerSecond = speedBytesPerSecond,
            IsCompleted = isCompleted,
            ErrorMessage = errorMessage,
            StatusMessage = statusMessage
        });
    }

    /// <summary>
    /// [Issue #360] Saves component metadata after successful download
    /// </summary>
    private async Task SaveComponentMetadataAsync(
        ComponentInfo component,
        DateTimeOffset lastModified,
        CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(component.LocalPath, MetadataFileName);
        var metadata = new ComponentMetadata
        {
            ComponentId = component.Id,
            LastModified = lastModified,
            DownloadedAt = DateTimeOffset.UtcNow,
            DownloadUrl = component.DownloadUrl
        };

        try
        {
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metadataPath, json, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("[Issue #360] Saved metadata for {ComponentId}: LastModified={LastModified:s}", component.Id, lastModified);
        }
        catch (Exception ex)
        {
            // Non-critical - log and continue
            _logger.LogWarning(ex, "[Issue #360] Failed to save metadata for {ComponentId}", component.Id);
        }
    }

    /// <summary>
    /// [Issue #360] Reads component metadata from local file
    /// </summary>
    private async Task<ComponentMetadata?> ReadComponentMetadataAsync(
        string metadataPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ComponentMetadata>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #360] Failed to read metadata from {Path}", metadataPath);
            return null;
        }
    }

    /// <summary>
    /// [Issue #360] Gets Last-Modified header from remote URL using HEAD request
    /// </summary>
    private async Task<DateTimeOffset?> GetRemoteLastModifiedAsync(
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            // Bypass cache to get actual remote version
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[Issue #360] HEAD request failed for {Url}: {StatusCode}",
                    url, response.StatusCode);
                return null;
            }

            // Try Last-Modified header first
            if (response.Content.Headers.LastModified.HasValue)
            {
                return response.Content.Headers.LastModified.Value;
            }

            // Fallback to Date header
            if (response.Headers.Date.HasValue)
            {
                return response.Headers.Date.Value;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #360] Failed to get Last-Modified for {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// [Issue #360] Fetches Last-Modified during download and saves metadata
    /// </summary>
    private async Task<DateTimeOffset?> GetAndSaveLastModifiedAsync(
        ComponentInfo component,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? lastModified = response.Content.Headers.LastModified
            ?? response.Headers.Date
            ?? DateTimeOffset.UtcNow;

        if (lastModified.HasValue)
        {
            await SaveComponentMetadataAsync(component, lastModified.Value, cancellationToken).ConfigureAwait(false);
        }

        return lastModified;
    }

    #endregion
}

/// <summary>
/// [Issue #360] Component metadata for version tracking
/// </summary>
internal class ComponentMetadata
{
    public string ComponentId { get; set; } = string.Empty;
    public DateTimeOffset LastModified { get; set; }
    public DateTimeOffset DownloadedAt { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
}
