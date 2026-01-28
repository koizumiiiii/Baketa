using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Settings;

namespace Baketa.UI.Utils;

/// <summary>
/// ファイル共有を考慮した安全なログ書き込み用ユーティリティ
/// 複数のプロセスが同時にアクセスしてもファイルロックエラーを回避
/// </summary>
/// <remarks>
/// [Issue #329] ログファイルはLogs/ディレクトリに統一
/// [Issue #345] ログローテーション機能追加
/// </remarks>
public static class SafeFileLogger
{
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private const int MaxRetryAttempts = 3;
    private const int RetryDelayMs = 10;

    // [Issue #345] ログローテーション設定
    private static long _maxFileSizeBytes = 10 * 1024 * 1024; // デフォルト: 10MB
    private static int _retainedFileCount = 7; // デフォルト: 7世代
    private static bool _cleanupExecuted = false;
    private static readonly object _cleanupLock = new();

    /// <summary>
    /// [Issue #345] ログローテーション設定を初期化
    /// </summary>
    /// <param name="maxFileSizeMB">最大ファイルサイズ（MB）</param>
    /// <param name="retainedFileCount">保持する世代数</param>
    public static void ConfigureRotation(int maxFileSizeMB, int retainedFileCount)
    {
        _maxFileSizeBytes = maxFileSizeMB * 1024L * 1024L;
        _retainedFileCount = Math.Max(1, retainedFileCount);
    }

    /// <summary>
    /// [Issue #329] ファイル名からLogs/ディレクトリ内のフルパスを取得
    /// </summary>
    /// <param name="fileName">ファイル名（パスなし）</param>
    /// <returns>Logs/ディレクトリ内のフルパス</returns>
    public static string GetLogFilePath(string fileName)
    {
        // ディレクトリが存在しない場合は作成
        if (!Directory.Exists(BaketaSettingsPaths.LogDirectory))
        {
            Directory.CreateDirectory(BaketaSettingsPaths.LogDirectory);
        }

        // 既にフルパスの場合はそのまま返す（後方互換性）
        if (Path.IsPathRooted(fileName))
        {
            return fileName;
        }

        return Path.Combine(BaketaSettingsPaths.LogDirectory, fileName);
    }

    /// <summary>
    /// ファイルに安全にログを追記（同期版）
    /// </summary>
    /// <param name="fileName">ファイル名</param>
    /// <param name="message">ログメッセージ</param>
    public static void AppendLog(string fileName, string message)
    {
        AppendLogAsync(fileName, message).GetAwaiter().GetResult();
    }

    /// <summary>
    /// ファイルに安全にログを追記（非同期版）
    /// </summary>
    /// <param name="fileName">ファイル名（パスなしの場合はLogs/ディレクトリに保存）</param>
    /// <param name="message">ログメッセージ</param>
    /// <remarks>
    /// [Issue #345] サイズベースのログローテーション対応
    /// </remarks>
    public static async Task AppendLogAsync(string fileName, string message)
    {
        // [Issue #329] ファイル名をLogs/ディレクトリのパスに変換
        // [Issue #345] 日付ベースのファイル名に変換
        var filePath = GetRotatedLogFilePath(fileName);

        // [Issue #345] 起動時に古いログをクリーンアップ（1回のみ）
        EnsureCleanupExecuted(fileName);

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            // [Issue #345] サイズチェックとローテーション
            await CheckAndRotateIfNeededAsync(filePath).ConfigureAwait(false);

            for (int attempt = 0; attempt < MaxRetryAttempts; attempt++)
            {
                try
                {
                    using var fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
                    using var writer = new StreamWriter(fileStream);
                    await writer.WriteLineAsync(message).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                    return; // 成功
                }
                catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020) && attempt < MaxRetryAttempts - 1)
                {
                    // ファイルが他のプロセスによって使用中の場合のリトライ
                    await Task.Delay(RetryDelayMs * (attempt + 1)).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException) when (attempt < MaxRetryAttempts - 1)
                {
                    await Task.Delay(RetryDelayMs * (attempt + 1)).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            // 最終的にログ書き込み失敗時はコンソールに出力
            Console.WriteLine($"⚠️ ログ書き込み失敗 ({filePath}): {ex.Message}");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// [Issue #345] 日付ベースのログファイルパスを取得
    /// </summary>
    /// <param name="baseFileName">ベースファイル名（例: baketa_app.log）</param>
    /// <returns>日付付きファイルパス（例: baketa_app_20260128.log）</returns>
    private static string GetRotatedLogFilePath(string baseFileName)
    {
        var logDir = BaketaSettingsPaths.LogDirectory;

        // ディレクトリが存在しない場合は作成
        if (!Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }

        // 既にフルパスの場合
        if (Path.IsPathRooted(baseFileName))
        {
            var dir = Path.GetDirectoryName(baseFileName) ?? logDir;
            var name = Path.GetFileNameWithoutExtension(baseFileName);
            var ext = Path.GetExtension(baseFileName);
            return Path.Combine(dir, $"{name}_{DateTime.Now:yyyyMMdd}{ext}");
        }

        // ファイル名のみの場合
        var baseName = Path.GetFileNameWithoutExtension(baseFileName);
        var extension = Path.GetExtension(baseFileName);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".log";
        }

        return Path.Combine(logDir, $"{baseName}_{DateTime.Now:yyyyMMdd}{extension}");
    }

    /// <summary>
    /// [Issue #345] ファイルサイズをチェックし、必要に応じてローテーション
    /// </summary>
    private static async Task CheckAndRotateIfNeededAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length < _maxFileSizeBytes)
            {
                return;
            }

            // サイズ超過: 連番付きファイルにリネーム
            var dir = Path.GetDirectoryName(filePath) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(filePath);
            var ext = Path.GetExtension(filePath);

            // 既存の連番ファイルを検索して次の番号を決定
            var existingFiles = Directory.GetFiles(dir, $"{baseName}_*{ext}")
                .Where(f => !f.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var nextNumber = 1;
            foreach (var existing in existingFiles)
            {
                var existingName = Path.GetFileNameWithoutExtension(existing);
                var suffix = existingName.Replace(baseName + "_", "");
                if (int.TryParse(suffix, out var num) && num >= nextNumber)
                {
                    nextNumber = num + 1;
                }
            }

            var rotatedPath = Path.Combine(dir, $"{baseName}_{nextNumber}{ext}");

            // ファイルを移動（リネーム）
            File.Move(filePath, rotatedPath);
            Console.WriteLine($"📁 [Issue #345] ログローテーション: {Path.GetFileName(filePath)} → {Path.GetFileName(rotatedPath)}");
        }
        catch (Exception ex)
        {
            // ローテーション失敗は無視（ログ書き込みを継続）
            Console.WriteLine($"⚠️ ログローテーション失敗: {ex.Message}");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// [Issue #345] 起動時のクリーンアップを確実に1回だけ実行
    /// </summary>
    private static void EnsureCleanupExecuted(string baseFileName)
    {
        if (_cleanupExecuted)
        {
            return;
        }

        lock (_cleanupLock)
        {
            if (_cleanupExecuted)
            {
                return;
            }

            try
            {
                CleanupOldLogFiles(baseFileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ ログクリーンアップ失敗: {ex.Message}");
            }

            _cleanupExecuted = true;
        }
    }

    /// <summary>
    /// [Issue #345] 古いログファイルを削除
    /// </summary>
    /// <param name="baseFileName">ベースファイル名</param>
    public static void CleanupOldLogFiles(string baseFileName)
    {
        try
        {
            var logDir = BaketaSettingsPaths.LogDirectory;
            if (!Directory.Exists(logDir))
            {
                return;
            }

            var baseName = Path.GetFileNameWithoutExtension(baseFileName);
            var ext = Path.GetExtension(baseFileName);
            if (string.IsNullOrEmpty(ext))
            {
                ext = ".log";
            }

            // パターン: baseName_YYYYMMDD.log または baseName_YYYYMMDD_N.log
            var pattern = $"{baseName}_*{ext}";
            var logFiles = Directory.GetFiles(logDir, pattern)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();

            if (logFiles.Count <= _retainedFileCount)
            {
                return;
            }

            // 保持数を超えた古いファイルを削除
            var filesToDelete = logFiles.Skip(_retainedFileCount).ToList();
            foreach (var file in filesToDelete)
            {
                try
                {
                    file.Delete();
                    Console.WriteLine($"🗑️ [Issue #345] 古いログ削除: {file.Name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ ログ削除失敗 ({file.Name}): {ex.Message}");
                }
            }

            if (filesToDelete.Count > 0)
            {
                Console.WriteLine($"📊 [Issue #345] ログクリーンアップ完了: {filesToDelete.Count}件削除、{Math.Min(logFiles.Count, _retainedFileCount)}件保持");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ ログクリーンアップエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// タイムスタンプ付きでログを追記（同期版）
    /// </summary>
    /// <param name="fileName">ファイル名</param>
    /// <param name="message">ログメッセージ</param>
    public static void AppendLogWithTimestamp(string fileName, string message)
    {
        var timestampedMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        AppendLog(fileName, timestampedMessage);
    }

    /// <summary>
    /// タイムスタンプ付きでログを追記（非同期版）
    /// </summary>
    /// <param name="fileName">ファイル名</param>
    /// <param name="message">ログメッセージ</param>
    public static async Task AppendLogWithTimestampAsync(string fileName, string message)
    {
        var timestampedMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        await AppendLogAsync(fileName, timestampedMessage).ConfigureAwait(false);
    }
}
