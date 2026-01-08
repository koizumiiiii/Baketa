using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Baketa.Core.Abstractions.Services;
using Baketa.Infrastructure.Services.Setup.Models;
using Microsoft.Extensions.Logging;
using ComponentInfo = Baketa.Core.Abstractions.Services.ComponentInfo;

namespace Baketa.Infrastructure.Services.Setup;

/// <summary>
/// [Issue #256] Phase 3.5: コンポーネントのアトミックインストールとSHA256検証を担当
/// ダウンロードとインストールの責務を分離
/// </summary>
public sealed class ComponentInstallerService : IComponentInstallerService
{
    private const string TempSuffix = ".tmp";
    private const string BackupSuffix = ".backup";

    private readonly ILogger<ComponentInstallerService> _logger;
    private readonly IComponentVersionService _versionService;

    // [Gemini Review] スレッドセーフティ: コンポーネントIDごとの非同期ロック
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _installLocks = new();

    public ComponentInstallerService(
        ILogger<ComponentInstallerService> logger,
        IComponentVersionService versionService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _versionService = versionService ?? throw new ArgumentNullException(nameof(versionService));
    }

    /// <summary>
    /// マニフェストのコンポーネント情報をダウンロード用ComponentInfoに変換
    /// </summary>
    public ComponentInfo ConvertToDownloadComponentInfo(
        string componentId,
        Models.ComponentInfo manifestInfo,
        string variant,
        string baseUrl,
        string localBasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentNullException.ThrowIfNull(manifestInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);

        if (!manifestInfo.Variants.TryGetValue(variant, out var variantInfo))
        {
            throw new ArgumentException($"Variant '{variant}' not found for component '{componentId}'", nameof(variant));
        }

        var files = variantInfo.Files;
        if (files.Count == 0)
        {
            throw new InvalidOperationException($"No files defined for component '{componentId}' variant '{variant}'");
        }

        var localPath = Path.Combine(localBasePath, componentId);
        var totalSize = files.Sum(f => f.Size);

        // 単一ファイルの場合
        if (files.Count == 1)
        {
            var file = files[0];
            return new ComponentInfo(
                Id: componentId,
                DisplayName: manifestInfo.DisplayName,
                DownloadUrl: baseUrl + file.Filename,
                LocalPath: localPath,
                ExpectedSizeBytes: file.Size,
                Checksum: string.IsNullOrEmpty(file.Sha256) ? null : file.Sha256,
                IsRequired: true
            );
        }

        // 分割ファイルの場合
        var partChecksums = files
            .Select(f => string.IsNullOrEmpty(f.Sha256) ? "" : f.Sha256)
            .ToList();

        // .001, .002 形式を検出
        var firstFilename = files[0].Filename;
        var suffixFormat = firstFilename.EndsWith(".001", StringComparison.Ordinal) ? ".{0:D3}" : ".part{0}";

        // ベースURLからファイル名を除去してダウンロードURLを構築
        var baseFilename = firstFilename.EndsWith(".001", StringComparison.Ordinal)
            ? firstFilename[..^4] // Remove .001
            : firstFilename.Replace(".part1", "", StringComparison.Ordinal);

        return new ComponentInfo(
            Id: componentId,
            DisplayName: manifestInfo.DisplayName,
            DownloadUrl: baseUrl + baseFilename,
            LocalPath: localPath,
            ExpectedSizeBytes: totalSize,
            Checksum: null, // 分割ファイルは個別検証
            IsRequired: true,
            SplitParts: files.Count,
            PartChecksums: partChecksums,
            SplitPartSuffixFormat: suffixFormat
        );
    }

    /// <summary>
    /// ダウンロード済みファイルのSHA256を検証
    /// </summary>
    public async Task<bool> VerifyChecksumAsync(
        string filePath,
        string expectedChecksum,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(expectedChecksum))
        {
            _logger.LogDebug("[Issue #256] No checksum provided for {FilePath}, skipping verification", filePath);
            return true;
        }

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("[Issue #256] File not found for checksum verification: {FilePath}", filePath);
            return false;
        }

        _logger.LogDebug("[Issue #256] Verifying checksum for {FilePath}", filePath);

        await using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var actualChecksum = Convert.ToHexString(hashBytes);

        var isMatch = string.Equals(actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase);

        if (isMatch)
        {
            _logger.LogInformation("[Issue #256] Checksum verified: {FilePath}", filePath);
        }
        else
        {
            _logger.LogError(
                "[Issue #256] Checksum mismatch for {FilePath}. Expected: {Expected}, Actual: {Actual}",
                filePath, expectedChecksum, actualChecksum);
        }

        return isMatch;
    }

    /// <summary>
    /// コンポーネントをアトミックにインストール
    /// 同一ボリューム上で一時展開→リネームによるアトミック更新を実行
    /// </summary>
    public async Task InstallAtomicallyAsync(
        string componentId,
        string componentVersion,
        string variant,
        string zipFilePath,
        string finalPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(zipFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);

        // [Gemini Review] コンポーネントIDに対応するSemaphoreSlimを取得または新規作成
        var installLock = _installLocks.GetOrAdd(componentId, _ => new SemaphoreSlim(1, 1));

        _logger.LogDebug("[Issue #256] Waiting for installation lock for {ComponentId}", componentId);
        await installLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 同一ボリューム上の一時ディレクトリを使用（Directory.Moveのアトミック性を保証）
            var tempExtractPath = finalPath + TempSuffix;
            var backupPath = finalPath + BackupSuffix;

            _logger.LogInformation(
                "[Issue #256] Starting atomic installation: ComponentId={ComponentId}, Version={Version}, FinalPath={FinalPath}",
                componentId, componentVersion, finalPath);

            try
            {
                // 1. クリーンアップ: 以前の一時ディレクトリを削除
                if (Directory.Exists(tempExtractPath))
                {
                    _logger.LogDebug("[Issue #256] Cleaning up previous temp directory: {TempPath}", tempExtractPath);
                    Directory.Delete(tempExtractPath, true);
                }

                // 2. 一時ディレクトリに展開
                _logger.LogDebug("[Issue #256] Extracting to temp directory: {TempPath}", tempExtractPath);
                Directory.CreateDirectory(tempExtractPath);

                await Task.Run(() =>
                {
                    using var archive = ZipFile.OpenRead(zipFilePath);
                    archive.ExtractToDirectory(tempExtractPath, overwriteFiles: true);
                }, cancellationToken).ConfigureAwait(false);

                _logger.LogDebug("[Issue #256] Extraction complete: {TempPath}", tempExtractPath);

                // 3. 既存ディレクトリをバックアップ（存在する場合）
                if (Directory.Exists(finalPath))
                {
                    // 古いバックアップを削除
                    if (Directory.Exists(backupPath))
                    {
                        _logger.LogDebug("[Issue #256] Removing old backup: {BackupPath}", backupPath);
                        Directory.Delete(backupPath, true);
                    }

                    _logger.LogDebug("[Issue #256] Moving existing installation to backup: {FinalPath} -> {BackupPath}", finalPath, backupPath);

                    try
                    {
                        Directory.Move(finalPath, backupPath);
                    }
                    catch (IOException ex) when (ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning(
                            "[Issue #256] Cannot move directory (files in use). Consider restart-based update. Path: {FinalPath}",
                            finalPath);
                        throw new InvalidOperationException(
                            $"Component '{componentId}' is in use. Please restart the application to complete the update.", ex);
                    }
                }

                // 4. 新しいディレクトリを最終位置に移動（アトミック操作）
                _logger.LogDebug("[Issue #256] Moving new installation to final path: {TempPath} -> {FinalPath}", tempExtractPath, finalPath);
                Directory.Move(tempExtractPath, finalPath);

                // 5. バックアップ削除
                if (Directory.Exists(backupPath))
                {
                    _logger.LogDebug("[Issue #256] Removing backup: {BackupPath}", backupPath);
                    Directory.Delete(backupPath, true);
                }

                // 6. バージョン情報を記録
                await _versionService.RecordInstallationAsync(
                    componentId, componentVersion, variant, finalPath, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "[Issue #256] Atomic installation complete: ComponentId={ComponentId}, Version={Version}",
                    componentId, componentVersion);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[Issue #256] Installation cancelled: {ComponentId}", componentId);
                await RollbackAsync(tempExtractPath, backupPath, finalPath).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _logger.LogError(ex, "[Issue #256] Installation failed, rolling back: {ComponentId}", componentId);
                await RollbackAsync(tempExtractPath, backupPath, finalPath).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            installLock.Release();
            _logger.LogDebug("[Issue #256] Released installation lock for {ComponentId}", componentId);
        }
    }

    /// <summary>
    /// インストール失敗時のロールバック
    /// </summary>
    private async Task RollbackAsync(string tempExtractPath, string backupPath, string finalPath)
    {
        _logger.LogInformation("[Issue #256] Starting rollback...");

        try
        {
            // 一時ディレクトリを削除
            if (Directory.Exists(tempExtractPath))
            {
                _logger.LogDebug("[Issue #256] Rollback: Removing temp directory: {TempPath}", tempExtractPath);
                await Task.Run(() => Directory.Delete(tempExtractPath, true)).ConfigureAwait(false);
            }

            // バックアップから復元
            if (Directory.Exists(backupPath) && !Directory.Exists(finalPath))
            {
                _logger.LogDebug("[Issue #256] Rollback: Restoring from backup: {BackupPath} -> {FinalPath}", backupPath, finalPath);
                Directory.Move(backupPath, finalPath);
            }

            _logger.LogInformation("[Issue #256] Rollback complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #256] Rollback failed. Manual intervention may be required.");
        }
    }

    /// <summary>
    /// ディスク容量チェック（ダウンロード+展開+バックアップで約3倍必要）
    /// </summary>
    public bool HasSufficientDiskSpace(string targetPath, long requiredBytes, int multiplier = 3)
    {
        try
        {
            var driveLetter = Path.GetPathRoot(targetPath);
            if (string.IsNullOrEmpty(driveLetter))
            {
                _logger.LogWarning("[Issue #256] Cannot determine drive for path: {Path}", targetPath);
                return true; // 判定不能な場合は続行を許可
            }

            var driveInfo = new DriveInfo(driveLetter);
            var requiredSpace = requiredBytes * multiplier;
            var availableSpace = driveInfo.AvailableFreeSpace;

            if (availableSpace < requiredSpace)
            {
                _logger.LogWarning(
                    "[Issue #256] Insufficient disk space. Required: {Required:N0} bytes, Available: {Available:N0} bytes, Drive: {Drive}",
                    requiredSpace, availableSpace, driveLetter);
                return false;
            }

            _logger.LogDebug(
                "[Issue #256] Disk space check passed. Required: {Required:N0} bytes, Available: {Available:N0} bytes",
                requiredSpace, availableSpace);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #256] Disk space check failed for path: {Path}", targetPath);
            return true; // エラー時は続行を許可
        }
    }
}
