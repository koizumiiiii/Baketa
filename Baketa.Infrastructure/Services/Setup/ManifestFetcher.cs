using System.Net.Http;
using System.Text.Json;
using Baketa.Infrastructure.Services.Setup.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services.Setup;

/// <summary>
/// Issue #256: コンポーネントマニフェスト取得サービス
/// models-v1リリースからmanifest.jsonを取得し、キャッシュ管理
/// </summary>
public sealed class ManifestFetcher : IManifestFetcher
{
    private readonly ILogger<ManifestFetcher> _logger;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);

    /// <summary>
    /// マニフェストURL（raw.githubusercontent.com経由でAPI制限回避）
    /// </summary>
    private const string ManifestUrl = "https://raw.githubusercontent.com/koizumiiiii/Baketa/models-v1/manifest.json";

    /// <summary>
    /// キャッシュ有効期間（1時間）
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    // キャッシュ
    private ComponentManifest? _cachedManifest;
    private DateTimeOffset _cacheExpiry = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ManifestFetcher(
        ILogger<ManifestFetcher> logger,
        HttpClient httpClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        // タイムアウト設定
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// マニフェストを取得（キャッシュ対応）
    /// </summary>
    public async Task<ComponentManifest?> GetManifestAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        // キャッシュ確認（強制更新でない場合）
        if (!forceRefresh && _cachedManifest != null && DateTimeOffset.UtcNow < _cacheExpiry)
        {
            _logger.LogDebug("[Issue #256] Returning cached manifest (expires: {Expiry})", _cacheExpiry);
            return _cachedManifest;
        }

        await _fetchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // ロック取得後に再度キャッシュ確認（二重フェッチ防止）
            if (!forceRefresh && _cachedManifest != null && DateTimeOffset.UtcNow < _cacheExpiry)
            {
                return _cachedManifest;
            }

            _logger.LogInformation("[Issue #256] Fetching manifest from: {Url}", ManifestUrl);

            var response = await _httpClient.GetAsync(ManifestUrl, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[Issue #256] Manifest fetch failed: {StatusCode}",
                    response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<ComponentManifest>(json, JsonOptions);

            if (manifest == null)
            {
                _logger.LogWarning("[Issue #256] Failed to deserialize manifest");
                return null;
            }

            // キャッシュ更新
            _cachedManifest = manifest;
            _cacheExpiry = DateTimeOffset.UtcNow.Add(CacheDuration);

            _logger.LogInformation(
                "[Issue #256] Manifest fetched successfully. Version: {Version}, Components: {Count}",
                manifest.ManifestVersion,
                manifest.Components.Count);

            return manifest;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[Issue #256] Network error fetching manifest");
            return null;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning("[Issue #256] Timeout fetching manifest");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[Issue #256] Invalid manifest JSON");
            return null;
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    /// <summary>
    /// キャッシュをクリア
    /// </summary>
    public void ClearCache()
    {
        _cachedManifest = null;
        _cacheExpiry = DateTimeOffset.MinValue;
        _logger.LogDebug("[Issue #256] Manifest cache cleared");
    }

    /// <summary>
    /// 更新チェック結果を取得
    /// </summary>
    public async Task<IReadOnlyList<ComponentUpdateCheckResult>> CheckForUpdatesAsync(
        IComponentVersionService versionService,
        string appVersion,
        string preferredVariant = "cpu",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(versionService);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);

        var manifest = await GetManifestAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (manifest == null)
        {
            _logger.LogWarning("[Issue #256] Cannot check for updates: manifest unavailable");
            return [];
        }

        var installedVersions = await versionService.GetInstalledVersionsAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<ComponentUpdateCheckResult>();

        foreach (var (componentId, componentInfo) in manifest.Components)
        {
            var installedInfo = installedVersions.Components.GetValueOrDefault(componentId);
            var updateAvailable = versionService.IsUpdateAvailable(installedInfo?.Version, componentInfo.Version);
            var meetsRequirement = versionService.MeetsMinAppVersionRequirement(appVersion, componentInfo.MinAppVersion);

            // ダウンロードサイズ計算
            var variant = installedInfo?.Variant ?? preferredVariant;
            long totalSize = 0;
            if (componentInfo.Variants.TryGetValue(variant, out var variantInfo))
            {
                totalSize = variantInfo.Files.Sum(f => f.Size);
            }

            results.Add(new ComponentUpdateCheckResult
            {
                ComponentId = componentId,
                DisplayName = componentInfo.DisplayName,
                CurrentVersion = installedInfo?.Version,
                LatestVersion = componentInfo.Version,
                UpdateAvailable = updateAvailable,
                Changelog = componentInfo.Changelog,
                TotalDownloadSize = totalSize,
                MinAppVersion = componentInfo.MinAppVersion,
                MeetsAppVersionRequirement = meetsRequirement
            });
        }

        var updatesCount = results.Count(r => r.UpdateAvailable && r.MeetsAppVersionRequirement);
        _logger.LogInformation(
            "[Issue #256] Update check complete. {UpdateCount}/{TotalCount} updates available",
            updatesCount,
            results.Count);

        return results;
    }
}

/// <summary>
/// Issue #256: マニフェスト取得サービスインターフェース
/// </summary>
public interface IManifestFetcher
{
    /// <summary>
    /// マニフェストを取得
    /// </summary>
    Task<ComponentManifest?> GetManifestAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// キャッシュをクリア
    /// </summary>
    void ClearCache();

    /// <summary>
    /// 更新チェック結果を取得
    /// </summary>
    Task<IReadOnlyList<ComponentUpdateCheckResult>> CheckForUpdatesAsync(
        IComponentVersionService versionService,
        string appVersion,
        string preferredVariant = "cpu",
        CancellationToken cancellationToken = default);
}
