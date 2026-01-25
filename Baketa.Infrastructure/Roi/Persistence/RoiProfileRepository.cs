using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Roi;
using Baketa.Core.Models.Roi;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Roi.Persistence;

/// <summary>
/// [Issue #293] ROIプロファイルのJSON永続化リポジトリ
/// </summary>
/// <remarks>
/// プロファイルをJSONファイルとして保存・読み込みします。
/// ファイル名はプロファイルIDをベースに生成されます。
/// </remarks>
public sealed class RoiProfileRepository : IRoiProfileService, IDisposable
{
    private readonly ILogger<RoiProfileRepository> _logger;
    private readonly string _profilesDirectory;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public RoiProfileRepository(ILogger<RoiProfileRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _profilesDirectory = BaketaSettingsPaths.RoiProfilesDirectory;

        EnsureDirectoryExists();

        _logger.LogInformation(
            "[Issue #293] RoiProfileRepository initialized: Directory={Directory}",
            _profilesDirectory);
    }

    /// <inheritdoc />
    public string ProfilesDirectoryPath => _profilesDirectory;

    /// <inheritdoc />
    public async Task SaveProfileAsync(RoiProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!profile.IsValid())
        {
            throw new ArgumentException("Invalid profile", nameof(profile));
        }

        var filePath = GetProfileFilePath(profile.Id);

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureDirectoryExists();

            var json = JsonSerializer.Serialize(profile, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug(
                "[Issue #293] Profile saved: Id={ProfileId}, Path={Path}, Size={Size}bytes",
                profile.Id,
                filePath,
                json.Length);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<RoiProfile?> LoadProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var filePath = GetProfileFilePath(profileId);

        if (!File.Exists(filePath))
        {
            _logger.LogDebug(
                "[Issue #293] Profile not found: Id={ProfileId}, Path={Path}",
                profileId,
                filePath);
            return null;
        }

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var profile = JsonSerializer.Deserialize<RoiProfile>(json, JsonOptions);

            if (profile == null)
            {
                _logger.LogWarning(
                    "[Issue #293] Failed to deserialize profile: Id={ProfileId}, Path={Path}",
                    profileId,
                    filePath);
                return null;
            }

            _logger.LogDebug(
                "[Issue #293] Profile loaded: Id={ProfileId}, Regions={RegionCount}",
                profile.Id,
                profile.Regions.Count);

            return profile;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "[Issue #293] JSON parse error for profile: Id={ProfileId}, Path={Path}",
                profileId,
                filePath);
            return null;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public Task<bool> ProfileExistsAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var filePath = GetProfileFilePath(profileId);
        return Task.FromResult(File.Exists(filePath));
    }

    /// <inheritdoc />
    public async Task<bool> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var filePath = GetProfileFilePath(profileId);

        if (!File.Exists(filePath))
        {
            return false;
        }

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            File.Delete(filePath);

            _logger.LogInformation(
                "[Issue #293] Profile deleted: Id={ProfileId}, Path={Path}",
                profileId,
                filePath);

            return true;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex,
                "[Issue #293] Failed to delete profile: Id={ProfileId}, Path={Path}",
                profileId,
                filePath);
            return false;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoiProfileSummary>> GetAllProfileSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        var summaries = new List<RoiProfileSummary>();

        if (!Directory.Exists(_profilesDirectory))
        {
            return summaries;
        }

        var profileFiles = Directory.GetFiles(_profilesDirectory, "*.json");

        foreach (var filePath in profileFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var profileId = Path.GetFileNameWithoutExtension(filePath);
                var profile = await LoadProfileAsync(profileId, cancellationToken).ConfigureAwait(false);

                if (profile != null)
                {
                    var fileInfo = new FileInfo(filePath);
                    summaries.Add(RoiProfileSummary.FromProfile(profile, fileInfo.Length));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[Issue #293] Failed to load profile summary: Path={Path}",
                    filePath);
            }
        }

        return [.. summaries.OrderByDescending(s => s.UpdatedAt)];
    }

    /// <inheritdoc />
    public async Task<RoiProfile?> FindProfileByExecutablePathAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        // プロファイルIDを実行ファイルパスから計算
        var profileId = ComputeProfileId(executablePath);

        return await LoadProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> CleanupOldProfilesAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_profilesDirectory))
        {
            return 0;
        }

        var deletedCount = 0;
        var cutoffDate = DateTime.UtcNow - maxAge;
        var profileFiles = Directory.GetFiles(_profilesDirectory, "*.json");

        foreach (var filePath in profileFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.LastWriteTimeUtc < cutoffDate)
                {
                    var profileId = Path.GetFileNameWithoutExtension(filePath);
                    if (await DeleteProfileAsync(profileId, cancellationToken).ConfigureAwait(false))
                    {
                        deletedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[Issue #293] Failed to cleanup profile: Path={Path}",
                    filePath);
            }
        }

        if (deletedCount > 0)
        {
            _logger.LogInformation(
                "[Issue #293] Cleanup completed: DeletedCount={Count}, MaxAge={MaxAge}",
                deletedCount,
                maxAge);
        }

        return deletedCount;
    }

    /// <summary>
    /// 実行ファイルパスからプロファイルIDを計算
    /// </summary>
    /// <param name="executablePath">実行ファイルパス</param>
    /// <returns>プロファイルID（SHA256ハッシュのプレフィックス）</returns>
    private static string ComputeProfileId(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return string.Empty;
        }

        // パスを正規化（小文字化、パス区切り統一）
        var normalizedPath = executablePath
            .ToLowerInvariant()
            .Replace('/', '\\')
            .TrimEnd('\\');

        // SHA256ハッシュを計算
        var bytes = System.Text.Encoding.UTF8.GetBytes(normalizedPath);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);

        // 最初の16バイト（32文字）をプロファイルIDとして使用
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    /// <summary>
    /// プロファイルファイルパスを取得
    /// </summary>
    private string GetProfileFilePath(string profileId)
    {
        // ファイル名として安全な文字のみを使用
        var safeFileName = SanitizeFileName(profileId);
        return Path.Combine(_profilesDirectory, $"{safeFileName}.json");
    }

    /// <summary>
    /// ファイル名を安全な形式にサニタイズ
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Length > 100 ? sanitized[..100] : sanitized;
    }

    /// <summary>
    /// ディレクトリが存在しない場合は作成
    /// </summary>
    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_profilesDirectory))
        {
            Directory.CreateDirectory(_profilesDirectory);
            _logger.LogDebug(
                "[Issue #293] Created profiles directory: {Directory}",
                _profilesDirectory);
        }
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _ioLock.Dispose();
            _disposed = true;
        }
    }
}
