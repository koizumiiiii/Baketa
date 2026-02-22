using System.IO;
using System.Text.Json;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Services.Setup.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services.Setup;

/// <summary>
/// Issue #256: ローカルコンポーネントバージョン管理サービス
/// %LOCALAPPDATA%/Baketa/component-versions.json を管理
/// </summary>
public sealed class ComponentVersionService : IComponentVersionService, IDisposable
{
    private readonly ILogger<ComponentVersionService> _logger;
    private readonly string _versionsFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ComponentVersionService(ILogger<ComponentVersionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // [Issue #459] BaketaSettingsPaths経由に統一
        _versionsFilePath = BaketaSettingsPaths.ComponentVersionsPath;

        _logger.LogDebug("[Issue #256] ComponentVersionService initialized. Path: {Path}", _versionsFilePath);
    }

    /// <summary>
    /// インストール済みコンポーネントのバージョン情報を取得
    /// </summary>
    public async Task<LocalComponentVersions> GetInstalledVersionsAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_versionsFilePath))
            {
                _logger.LogDebug("[Issue #256] Versions file not found, returning empty");
                return new LocalComponentVersions();
            }

            var json = await File.ReadAllTextAsync(_versionsFilePath, cancellationToken).ConfigureAwait(false);
            var versions = JsonSerializer.Deserialize<LocalComponentVersions>(json, JsonOptions);

            return versions ?? new LocalComponentVersions();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #256] Failed to read versions file, returning empty");
            return new LocalComponentVersions();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// 特定コンポーネントのインストール済みバージョンを取得
    /// </summary>
    public async Task<InstalledComponentInfo?> GetInstalledVersionAsync(
        string componentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);

        var versions = await GetInstalledVersionsAsync(cancellationToken).ConfigureAwait(false);
        return versions.Components.GetValueOrDefault(componentId);
    }

    /// <summary>
    /// コンポーネントのインストール情報を記録
    /// </summary>
    public async Task RecordInstallationAsync(
        string componentId,
        string version,
        string variant,
        string? installPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var versions = await LoadVersionsInternalAsync(cancellationToken).ConfigureAwait(false);

            versions.Components[componentId] = new InstalledComponentInfo
            {
                Version = version,
                Variant = variant,
                InstalledAt = DateTimeOffset.UtcNow,
                InstallPath = installPath
            };

            await SaveVersionsInternalAsync(versions, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "[Issue #256] Recorded installation: {ComponentId} v{Version} ({Variant})",
                componentId, version, variant);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// コンポーネントのインストール情報を削除
    /// </summary>
    public async Task RemoveInstallationRecordAsync(
        string componentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var versions = await LoadVersionsInternalAsync(cancellationToken).ConfigureAwait(false);

            if (versions.Components.Remove(componentId))
            {
                await SaveVersionsInternalAsync(versions, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("[Issue #256] Removed installation record: {ComponentId}", componentId);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// バージョン比較（SemVer形式）
    /// </summary>
    public bool IsUpdateAvailable(string? installedVersion, string latestVersion)
    {
        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            return true; // 未インストール = 更新が必要
        }

        var installed = TryParseVersion(installedVersion);
        var latest = TryParseVersion(latestVersion);

        // パース失敗時は安全のため更新なしと判断（Geminiレビュー指摘）
        if (installed is null || latest is null)
        {
            _logger.LogWarning(
                "[Issue #256] バージョン比較失敗。Installed: '{Installed}', Latest: '{Latest}'",
                installedVersion, latestVersion);
            return false;
        }

        return latest > installed;
    }

    /// <summary>
    /// アプリバージョンが最低要件を満たしているかチェック
    /// </summary>
    public bool MeetsMinAppVersionRequirement(string appVersion, string? minAppVersion)
    {
        if (string.IsNullOrWhiteSpace(minAppVersion))
        {
            return true; // 要件なし
        }

        var app = TryParseVersion(appVersion);
        var min = TryParseVersion(minAppVersion);

        // パース失敗時は安全のため要件を満たさないと判断（Geminiレビュー指摘）
        if (app is null || min is null)
        {
            _logger.LogWarning(
                "[Issue #256] バージョン要件チェック失敗。App: '{App}', Min: '{Min}'",
                appVersion, minAppVersion);
            return false;
        }

        return app >= min;
    }

    /// <summary>
    /// バージョン文字列をパース（TryParseパターン）
    /// </summary>
    private static Version? TryParseVersion(string versionString)
    {
        // "v" プレフィックスを除去
        var normalized = versionString.TrimStart('v', 'V');

        // プレリリースサフィックスを除去（例: "0.2.10-beta.1" → "0.2.10"）
        var dashIndex = normalized.IndexOf('-');
        if (dashIndex > 0)
        {
            normalized = normalized[..dashIndex];
        }

        // TryParseを使用して例外を発生させない（Geminiレビュー指摘）
        return Version.TryParse(normalized, out var version) ? version : null;
    }

    private async Task<LocalComponentVersions> LoadVersionsInternalAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_versionsFilePath))
        {
            return new LocalComponentVersions { Components = [] };
        }

        var json = await File.ReadAllTextAsync(_versionsFilePath, cancellationToken).ConfigureAwait(false);
        var versions = JsonSerializer.Deserialize<LocalComponentVersions>(json, JsonOptions);

        // immutable Dictionaryをmutableに変換
        return new LocalComponentVersions
        {
            Components = versions?.Components != null
                ? new Dictionary<string, InstalledComponentInfo>(versions.Components)
                : []
        };
    }

    private async Task SaveVersionsInternalAsync(LocalComponentVersions versions, CancellationToken cancellationToken)
    {
        // フォルダが存在しない場合は作成
        var directory = Path.GetDirectoryName(_versionsFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(versions, JsonOptions);
        await File.WriteAllTextAsync(_versionsFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _fileLock.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Issue #256: コンポーネントバージョン管理サービスインターフェース
/// </summary>
public interface IComponentVersionService
{
    /// <summary>
    /// インストール済みコンポーネントのバージョン情報を取得
    /// </summary>
    Task<LocalComponentVersions> GetInstalledVersionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 特定コンポーネントのインストール済みバージョンを取得
    /// </summary>
    Task<InstalledComponentInfo?> GetInstalledVersionAsync(string componentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// コンポーネントのインストール情報を記録
    /// </summary>
    Task RecordInstallationAsync(string componentId, string version, string variant, string? installPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// コンポーネントのインストール情報を削除
    /// </summary>
    Task RemoveInstallationRecordAsync(string componentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// バージョン比較
    /// </summary>
    bool IsUpdateAvailable(string? installedVersion, string latestVersion);

    /// <summary>
    /// アプリバージョンが最低要件を満たしているかチェック
    /// </summary>
    bool MeetsMinAppVersionRequirement(string appVersion, string? minAppVersion);
}
