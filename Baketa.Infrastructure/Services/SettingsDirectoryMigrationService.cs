using System.IO;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// [Issue #459] 旧設定ディレクトリから新パスへのマイグレーションサービス
/// %APPDATA%\Baketa と %LOCALAPPDATA%\Baketa から %USERPROFILE%\.baketa へ移行
/// </summary>
public static class SettingsDirectoryMigrationService
{
    /// <summary>
    /// マイグレーションを実行（App起動時の最初期に呼ぶ）
    /// </summary>
    public static void Migrate(ILogger? logger = null)
    {
        try
        {
            // 必要なディレクトリを先に作成
            BaketaSettingsPaths.EnsureDirectoriesExist();

            MigrateFromAppData(logger);
            MigrateFromLocalAppData(logger);
        }
        catch (Exception ex)
        {
            // マイグレーション失敗はアプリ起動をブロックしない
            logger?.LogWarning(ex, "[Issue #459] Settings directory migration failed, continuing with new paths");
            Console.WriteLine($"[Issue #459] Migration warning: {ex.Message}");
        }
    }

    /// <summary>
    /// %APPDATA%\Baketa からのマイグレーション
    /// </summary>
    private static void MigrateFromAppData(ILogger? logger)
    {
        var oldBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Baketa");

        if (!Directory.Exists(oldBase))
            return;

        logger?.LogInformation("[Issue #459] Found legacy settings directory: {OldPath}", oldBase);

        // ファイルのマイグレーション
        MigrateFile(Path.Combine(oldBase, "settings.json"), BaketaSettingsPaths.MainSettingsPath, logger);
        MigrateFile(Path.Combine(oldBase, "first-run.flag"), BaketaSettingsPaths.FirstRunFlagPath, logger);
        MigrateFile(Path.Combine(oldBase, ".crash_pending"), BaketaSettingsPaths.CrashPendingFlagPath, logger);

        // ディレクトリのマイグレーション
        MigrateDirectory(Path.Combine(oldBase, "Reports"), BaketaSettingsPaths.ReportsDirectory, logger);
        MigrateDirectory(Path.Combine(oldBase, "Metrics"), BaketaSettingsPaths.MetricsDirectory, logger);
        MigrateDirectory(Path.Combine(oldBase, "GameProfiles"), BaketaSettingsPaths.GameProfilesDirectory, logger);
        MigrateDirectory(Path.Combine(oldBase, "component-metadata"), BaketaSettingsPaths.ComponentMetadataDirectory, logger);

        // 旧ディレクトリを削除
        TryDeleteDirectory(oldBase, logger);
    }

    /// <summary>
    /// %LOCALAPPDATA%\Baketa からのマイグレーション
    /// </summary>
    private static void MigrateFromLocalAppData(ILogger? logger)
    {
        var oldBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Baketa");

        if (!Directory.Exists(oldBase))
            return;

        logger?.LogInformation("[Issue #459] Found legacy local settings directory: {OldPath}", oldBase);

        // ファイルのマイグレーション
        MigrateFile(Path.Combine(oldBase, "gpu_cache.json"), BaketaSettingsPaths.GpuCachePath, logger);
        MigrateFile(Path.Combine(oldBase, "component-versions.json"), BaketaSettingsPaths.ComponentVersionsPath, logger);

        // ディレクトリのマイグレーション
        MigrateDirectory(Path.Combine(oldBase, "CaptureProfiles"), BaketaSettingsPaths.CaptureProfilesDirectory, logger);

        // 旧ディレクトリを削除
        TryDeleteDirectory(oldBase, logger);
    }

    /// <summary>
    /// ファイルをコピー（既存ファイルはスキップ）
    /// </summary>
    private static void MigrateFile(string source, string destination, ILogger? logger)
    {
        if (!File.Exists(source))
            return;

        if (File.Exists(destination))
        {
            logger?.LogDebug("[Issue #459] Skipping existing file: {Destination}", destination);
            return;
        }

        try
        {
            var destDir = Path.GetDirectoryName(destination);
            if (destDir != null)
                Directory.CreateDirectory(destDir);

            File.Copy(source, destination);
            logger?.LogInformation("[Issue #459] Migrated file: {Source} → {Destination}", source, destination);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[Issue #459] Failed to migrate file: {Source}", source);
        }
    }

    /// <summary>
    /// ディレクトリ内の全ファイルをコピー（既存ファイルはスキップ）
    /// </summary>
    private static void MigrateDirectory(string sourceDir, string destDir, ILogger? logger)
    {
        if (!Directory.Exists(sourceDir))
            return;

        try
        {
            Directory.CreateDirectory(destDir);

            foreach (var sourceFile in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDir, sourceFile);
                var destFile = Path.Combine(destDir, relativePath);

                if (File.Exists(destFile))
                    continue;

                var destFileDir = Path.GetDirectoryName(destFile);
                if (destFileDir != null)
                    Directory.CreateDirectory(destFileDir);

                File.Copy(sourceFile, destFile);
            }

            logger?.LogInformation("[Issue #459] Migrated directory: {Source} → {Destination}", sourceDir, destDir);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[Issue #459] Failed to migrate directory: {Source}", sourceDir);
        }
    }

    /// <summary>
    /// 旧ディレクトリを削除（失敗は無視）
    /// </summary>
    private static void TryDeleteDirectory(string path, ILogger? logger)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                logger?.LogInformation("[Issue #459] Deleted legacy directory: {Path}", path);
            }
        }
        catch (Exception ex)
        {
            // 削除失敗は致命的ではない（次回起動時に再試行される）
            logger?.LogDebug(ex, "[Issue #459] Could not delete legacy directory: {Path}", path);
        }
    }
}
