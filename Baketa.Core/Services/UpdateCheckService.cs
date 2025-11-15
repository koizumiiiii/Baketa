using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Update;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Core.Services;

/// <summary>
/// GitHub Releases APIベースの更新チェックサービス実装
/// 堅牢なエラーハンドリング、キャッシュ、オフライン対応を提供
/// </summary>
public sealed class UpdateCheckService : IUpdateCheckService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<UpdateCheckSettings> _optionsMonitor;
    private readonly VersionComparisonService _versionComparison;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<UpdateCheckService> _logger;

    private readonly SemaphoreSlim _checkSemaphore = new(1, 1);
    private readonly CancellationTokenSource _periodicCheckCts = new();
    private readonly System.Threading.Timer _periodicTimer;

    private UpdateCheckSettings _currentSettings;
    private UpdateInfo? CachedUpdateInfo { get; set; }
    private DateTime? LastSuccessfulCheck { get; set; }
    private DateTime? CacheTimestamp { get; set; }
    private bool _disposed;

    public UpdateCheckService(
        HttpClient httpClient,
        IOptionsMonitor<UpdateCheckSettings> optionsMonitor,
        VersionComparisonService versionComparison,
        IFeatureFlagService featureFlagService,
        ILogger<UpdateCheckService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _versionComparison = versionComparison ?? throw new ArgumentNullException(nameof(versionComparison));
        _featureFlagService = featureFlagService ?? throw new ArgumentNullException(nameof(featureFlagService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _currentSettings = _optionsMonitor.CurrentValue;

        // HttpClient設定
        ConfigureHttpClient();

        // 設定変更の監視
        _optionsMonitor.OnChange((settings, _) => OnSettingsChanged(settings));

        // 定期チェックタイマー（開始は手動）
        _periodicTimer = new System.Threading.Timer(PeriodicCheckCallback, null, Timeout.Infinite, Timeout.Infinite);

        _logger.LogInformation("UpdateCheckService初期化完了");
    }

    #region IUpdateCheckService実装

    public bool IsUpdateAvailable => CachedUpdateInfo != null && IsVersionNewer(CachedUpdateInfo.Version);

    public Version CurrentVersion => GetCurrentApplicationVersion();

    public Version? LatestVersion => CachedUpdateInfo?.Version;

    public DateTime? LastCheckTime => LastSuccessfulCheck;

    public bool IsCheckInProgress { get; private set; }

    public UpdateInfo? LatestUpdateInfo => CachedUpdateInfo;

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool forceCheck = false, CancellationToken cancellationToken = default)
    {
        if (!_featureFlagService.IsAutoUpdateEnabled)
        {
            _logger.LogDebug("更新チェックはフィーチャーフラグで無効化されています");
            return UpdateCheckResult.Skipped;
        }

        if (!forceCheck && IsRecentCheckAvailable())
        {
            _logger.LogDebug("最近のチェック結果をキャッシュから返します");
            FireUpdateCheckCompleted(UpdateCheckResult.FromCache, CachedUpdateInfo, null, TimeSpan.Zero);
            return UpdateCheckResult.FromCache;
        }

        await _checkSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IsCheckInProgress = true;
            var startTime = DateTime.UtcNow;

            _logger.LogInformation("更新チェックを開始します（強制: {ForceCheck}）", forceCheck);

            try
            {
                var releases = await FetchReleasesFromGitHubAsync(cancellationToken).ConfigureAwait(false);
                var latestRelease = FindLatestCompatibleRelease(releases);

                if (latestRelease != null)
                {
                    var updateInfo = ConvertToUpdateInfo(latestRelease);
                    CachedUpdateInfo = updateInfo;
                    CacheTimestamp = DateTime.UtcNow;
                }
                else
                {
                    CachedUpdateInfo = null;
                }

                LastSuccessfulCheck = DateTime.UtcNow;
                var duration = DateTime.UtcNow - startTime;

                var result = CachedUpdateInfo != null && IsVersionNewer(CachedUpdateInfo.Version)
                    ? UpdateCheckResult.UpdateAvailable
                    : UpdateCheckResult.UpToDate;

                _logger.LogInformation("更新チェック完了: {Result} (所要時間: {Duration}ms)",
                    result, duration.TotalMilliseconds);

                FireUpdateCheckCompleted(result, CachedUpdateInfo, null, duration);
                FireUpdateStateChanged();

                return result;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                _logger.LogWarning(ex, "更新チェックでネットワークエラーが発生しました");

                var duration = DateTime.UtcNow - startTime;
                var result = HandleOfflineScenario();

                FireUpdateCheckCompleted(result, CachedUpdateInfo, ex, duration);
                return result;
            }
        }
        finally
        {
            IsCheckInProgress = false;
            _checkSemaphore.Release();
        }
    }

    public bool IsVersionNewer(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return version > CurrentVersion;
    }

    public bool IsVersionNewer(SemverVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var currentSemver = VersionComparisonService.FromSystemVersion(CurrentVersion);
        return _versionComparison.IsNewer(version, currentSemver);
    }

    public bool IsVersionNewer(string versionString)
    {
        if (!_versionComparison.IsValidSemver(versionString))
        {
            _logger.LogWarning("無効なバージョン文字列: {Version}", versionString);
            return false;
        }

        try
        {
            var currentSemver = VersionComparisonService.FromSystemVersion(CurrentVersion);
            var targetSemver = _versionComparison.ParseVersion(versionString);
            return _versionComparison.IsNewer(targetSemver, currentSemver);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "無効なバージョン形式です: {Version}", versionString);
            return false;
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "バージョンのフォーマットが正しくありません: {Version}", versionString);
            return false;
        }
    }

    public UpdateCheckSettings GetSettings() => _currentSettings;

    public Task UpdateSettingsAsync(UpdateCheckSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsValid())
        {
            throw new ArgumentException("無効な更新チェック設定です", nameof(settings));
        }

        _currentSettings = settings;
        ConfigureHttpClient();

        _logger.LogInformation("更新チェック設定が更新されました");

        // 自動チェックの再開
        if (settings.EnableAutoCheck)
        {
            _ = RestartPeriodicCheckAsync();
        }
        else
        {
            StopPeriodicCheck();
        }

        return Task.CompletedTask;
    }

    public Task StartPeriodicCheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_currentSettings.EnableAutoCheck)
        {
            _logger.LogDebug("自動更新チェックが無効化されているため、定期チェックを開始しません");
            return Task.CompletedTask;
        }

        var interval = TimeSpan.FromHours(_currentSettings.CheckIntervalHours);
        _periodicTimer.Change(interval, interval);

        _logger.LogInformation("定期更新チェックを開始しました（間隔: {Interval}時間）", _currentSettings.CheckIntervalHours);

        // 初回チェックを実行
        _ = Task.Run(async () =>
        {
            try
            {
                await CheckForUpdatesAsync(false, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "初回更新チェックがタイムアウトしました");
            }
            catch (OperationCanceledException)
            {
                // キャンセルは正常な時暴処理
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "初回更新チェックでネットワークエラーが発生しました");
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public void StopPeriodicCheck()
    {
        _periodicTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _logger.LogInformation("定期更新チェックを停止しました");
    }

    public event EventHandler<UpdateStateChangedEventArgs>? UpdateStateChanged;
    public event EventHandler<UpdateCheckCompletedEventArgs>? UpdateCheckCompleted;

    #endregion

    #region プライベートメソッド

    private void ConfigureHttpClient()
    {
        _httpClient.Timeout = TimeSpan.FromSeconds(_currentSettings.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", _currentSettings.UserAgent);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
    }

    private async Task<GitHubRelease[]> FetchReleasesFromGitHubAsync(CancellationToken cancellationToken)
    {
        var url = _currentSettings.GetReleasesApiUrl();
        var retryCount = 0;

        while (retryCount <= _currentSettings.RetryCount)
        {
            try
            {
                _logger.LogDebug("GitHub Releases APIを呼び出します: {Url} (試行 {Retry}/{MaxRetry})",
                    url, retryCount + 1, _currentSettings.RetryCount + 1);

                var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("GitHub API制限に達しました。しばらく待機します");
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
                }

                response.EnsureSuccessStatusCode();

                var releases = await response.Content.ReadFromJsonAsync<GitHubRelease[]>(cancellationToken).ConfigureAwait(false);

                return releases ?? [];
            }
            catch (Exception ex) when (retryCount < _currentSettings.RetryCount)
            {
                retryCount++;
                var delay = TimeSpan.FromSeconds((double)_currentSettings.RetryDelaySeconds * retryCount);

                _logger.LogWarning(ex, "GitHub API呼び出しが失敗しました。{Delay}秒後にリトライします（{Retry}/{MaxRetry}）",
                    delay.TotalSeconds, retryCount, _currentSettings.RetryCount);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new HttpRequestException($"GitHub API呼び出しが{_currentSettings.RetryCount + 1}回失敗しました");
    }

    private GitHubRelease? FindLatestCompatibleRelease(GitHubRelease[] releases)
    {
        return releases
            .Where(r => !r.Draft && (_currentSettings.IncludePrerelease || !r.Prerelease))
            .Where(r => _versionComparison.IsValidSemver(r.TagName))
            .OrderByDescending(r => _versionComparison.ParseVersion(r.TagName), new SemverComparer(_versionComparison))
            .FirstOrDefault();
    }

    private UpdateInfo ConvertToUpdateInfo(GitHubRelease release)
    {
        var version = _versionComparison.ParseVersion(release.TagName);

        return new UpdateInfo
        {
            Version = new Version(version.Major, version.Minor, version.Patch),
            ReleaseName = release.Name ?? release.TagName,
            ReleaseNotes = release.Body,
            DownloadUrl = new Uri(release.HtmlUrl),
            PublishedAt = release.PublishedAt,
            IsPrerelease = release.Prerelease,
            ContainsSecurityFixes = ContainsSecurityKeywords(release.Body),
            Importance = DetermineUpdateImportance(release)
        };
    }

    private static bool ContainsSecurityKeywords(string? releaseNotes)
    {
        if (string.IsNullOrEmpty(releaseNotes))
            return false;

        var keywords = new[] { "security", "vulnerability", "exploit", "patch", "fix", "セキュリティ", "脆弱性" };
        return keywords.Any(keyword => releaseNotes.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static UpdateImportance DetermineUpdateImportance(GitHubRelease release)
    {
        if (ContainsSecurityKeywords(release.Body))
            return UpdateImportance.Critical;

        if (release.Prerelease)
            return UpdateImportance.Low;

        return UpdateImportance.Normal;
    }

    private bool IsRecentCheckAvailable()
    {
        if (CacheTimestamp == null || CachedUpdateInfo == null)
            return false;

        var cacheAge = DateTime.UtcNow - CacheTimestamp.Value;
        return cacheAge.TotalMinutes < _currentSettings.CacheExpiryMinutes;
    }

    private UpdateCheckResult HandleOfflineScenario()
    {
        return _currentSettings.OfflineBehavior switch
        {
            OfflineBehavior.UseCache when CachedUpdateInfo != null => UpdateCheckResult.FromCache,
            OfflineBehavior.SilentFail => UpdateCheckResult.Skipped,
            _ => UpdateCheckResult.CheckFailed
        };
    }

    private static Version GetCurrentApplicationVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
    }

    private void OnSettingsChanged(UpdateCheckSettings newSettings)
    {
        _currentSettings = newSettings;
        ConfigureHttpClient();
        _logger.LogInformation("更新チェック設定が外部から変更されました");
    }

    private async void PeriodicCheckCallback(object? state)
    {
        try
        {
            await CheckForUpdatesAsync(false, _periodicCheckCts.Token).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "定期更新チェックがタイムアウトしました");
        }
        catch (OperationCanceledException)
        {
            // キャンセルは正常な時暴処理
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "定期更新チェックでネットワークエラーが発生しました");
        }
    }

    private async Task RestartPeriodicCheckAsync()
    {
        StopPeriodicCheck();
        await StartPeriodicCheckAsync(_periodicCheckCts.Token).ConfigureAwait(false);
    }

    private void FireUpdateStateChanged()
    {
        UpdateStateChanged?.Invoke(this, new UpdateStateChangedEventArgs
        {
            IsUpdateAvailable = IsUpdateAvailable,
            UpdateInfo = CachedUpdateInfo,
            Timestamp = DateTime.UtcNow
        });
    }

    private void FireUpdateCheckCompleted(UpdateCheckResult result, UpdateInfo? updateInfo, Exception? error, TimeSpan duration)
    {
        UpdateCheckCompleted?.Invoke(this, new UpdateCheckCompletedEventArgs
        {
            Result = result,
            UpdateInfo = updateInfo,
            Error = error,
            Timestamp = DateTime.UtcNow,
            Duration = duration
        });
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed)
            return;

        _periodicTimer.Dispose();
        _periodicCheckCts.Cancel();
        _periodicCheckCts.Dispose();
        _checkSemaphore.Dispose();

        _disposed = true;
        _logger.LogInformation("UpdateCheckService disposed");
    }

    #endregion
}

// GitHub API用のJSONモデル
internal sealed record GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public required string TagName { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("body")]
    public string? Body { get; init; }

    [JsonPropertyName("html_url")]
    public required string HtmlUrl { get; init; }

    [JsonPropertyName("published_at")]
    public required DateTime PublishedAt { get; init; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; init; }

    [JsonPropertyName("draft")]
    public bool Draft { get; init; }
}

// JSON Source Generation is not available in this context, using standard deserialization

// Semverソート用コンパレーター
internal sealed class SemverComparer(VersionComparisonService versionComparison) : IComparer<SemverVersion>
{
    private readonly VersionComparisonService _versionComparison = versionComparison;

    public int Compare(SemverVersion? x, SemverVersion? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        return VersionComparisonService.CompareVersions(x, y);
    }
}
