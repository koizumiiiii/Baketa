using System.Net.Http;
using Baketa.Infrastructure.Services.Setup.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services.Setup;

/// <summary>
/// Issue #256: コンポーネントマニフェスト取得サービス
/// Final release: remote component update checks are disabled.
/// </summary>
public sealed class ManifestFetcher : IManifestFetcher, IDisposable
{
    private readonly ILogger<ManifestFetcher> _logger;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private bool _disposed;

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
    public Task<ComponentManifest?> GetManifestAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        // Final release: components are pinned to models-v3; no remote update manifest exists.
        _logger.LogDebug("Component update checks are disabled for the final release");
        return Task.FromResult<ComponentManifest?>(null);
    }

    /// <summary>
    /// キャッシュをクリア
    /// </summary>
    public void ClearCache()
    {
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
            _logger.LogDebug("Component updates are disabled");
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

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _fetchLock.Dispose();
        _disposed = true;
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
