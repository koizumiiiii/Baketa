using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetSparkleUpdater;
using NetSparkleUpdater.Interfaces;
using NetSparkleUpdater.SignatureVerifiers;

namespace Baketa.UI.Services;

/// <summary>
/// [Updater] ZIPファイルからの自動更新を処理するカスタムSparkleUpdater
/// Single-file appの自動更新に対応
/// </summary>
public sealed class ZipSparkleUpdater : SparkleUpdater
{
    private readonly Microsoft.Extensions.Logging.ILogger? _logger;
    private readonly string _appDirectory;
    private readonly string _appExecutablePath;
    private static int _isUpdating;

    public ZipSparkleUpdater(
        string appcastUrl,
        ISignatureVerifier signatureVerifier,
        string? referenceAssemblyPath = null,
        Microsoft.Extensions.Logging.ILogger? logger = null)
        : base(appcastUrl, signatureVerifier, referenceAssemblyPath)
    {
        _logger = logger;
        _appExecutablePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine application path");
        _appDirectory = Path.GetDirectoryName(_appExecutablePath)
            ?? throw new InvalidOperationException("Cannot determine application directory");
    }

    /// <summary>
    /// ZIPファイルのマジックナンバー (50 4B 03 04 = "PK\x03\x04")
    /// </summary>
    private static readonly byte[] ZipMagicNumber = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>
    /// ファイルがZIP形式かどうかをマジックナンバーで判定
    /// NetSparkleは拡張子なしの一時ファイル名を使用するため、拡張子での判定は不可
    /// </summary>
    private bool IsZipFile(string filePath)
    {
        try
        {
            // File.Existsは不要。FileStreamのコンストラクタがFileNotFoundExceptionをスローするため、
            // try-catchブロックでハンドリングできる。
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            // ファイルサイズがマジックナンバーの長さより小さい場合はZIPではない
            if (fs.Length < ZipMagicNumber.Length)
            {
                _logger?.LogDebug("[Updater] ファイルサイズが小さすぎます: {Size} bytes", fs.Length);
                return false;
            }

            // ReadExactlyを使って確実に4バイト読み込むことを保証する
            Span<byte> header = stackalloc byte[ZipMagicNumber.Length];
            fs.ReadExactly(header);

            var isZip = header.SequenceEqual(ZipMagicNumber);
            _logger?.LogDebug("[Updater] マジックナンバー: {Bytes:X2} {Bytes1:X2} {Bytes2:X2} {Bytes3:X2} -> IsZip={IsZip}",
                header[0], header[1], header[2], header[3], isZip);

            return isZip;
        }
        catch (Exception ex)
        {
            // FileNotFoundExceptionやEndOfStreamExceptionなどもここでキャッチされる
            _logger?.LogWarning(ex, "[Updater] マジックナンバー判定エラー: {Path}", filePath);
            return false;
        }
    }

    /// <summary>
    /// バッチファイル内で安全に使用するためのパスエスケープ
    /// </summary>
    private static string EscapeBatchPath(string path)
    {
        return path.Replace("^", "^^")
                   .Replace("&", "^&")
                   .Replace("|", "^|")
                   .Replace("<", "^<")
                   .Replace(">", "^>")
                   .Replace("%", "%%");
    }

    /// <summary>
    /// ダウンロードしたインストーラーを実行
    /// ZIPファイルの場合はカスタム処理を行う
    /// </summary>
    protected override async Task RunDownloadedInstaller(string downloadFilePath)
    {
        _logger?.LogInformation("[Updater] RunDownloadedInstaller: {Path}", downloadFilePath);

        // ZIPファイルかどうかをマジックナンバーで判定
        // NetSparkleは拡張子なしの一時ファイル名を使用するため、拡張子での判定は不可
        if (!IsZipFile(downloadFilePath))
        {
            _logger?.LogInformation("[Updater] ZIPファイルではないためデフォルト処理 (マジックナンバー不一致)");
            await base.RunDownloadedInstaller(downloadFilePath).ConfigureAwait(false);
            return;
        }

        _logger?.LogInformation("[Updater] ZIPファイルとして処理開始 (マジックナンバー確認済)");

        // 複数回の更新実行を防止
        if (Interlocked.CompareExchange(ref _isUpdating, 1, 0) != 0)
        {
            _logger?.LogWarning("[Updater] 更新処理が既に実行中です");
            return;
        }

        string? extractDir = null;
        try
        {
            // 一時ディレクトリに展開
            extractDir = Path.Combine(Path.GetTempPath(), $"Baketa_Update_{Guid.NewGuid():N}");
            Directory.CreateDirectory(extractDir);

            _logger?.LogInformation("[Updater] ZIP展開先: {Path}", extractDir);

            await Task.Run(() => ZipFile.ExtractToDirectory(downloadFilePath, extractDir, overwriteFiles: true))
                .ConfigureAwait(false);

            // 展開されたディレクトリ構造を確認
            // Baketa-X.X.X/ というサブディレクトリがある場合はその中を使用
            var sourceDir = extractDir;
            var subDirs = Directory.GetDirectories(extractDir);
            if (subDirs.Length == 1 && subDirs[0].Contains("Baketa", StringComparison.OrdinalIgnoreCase))
            {
                sourceDir = subDirs[0];
                _logger?.LogInformation("[Updater] サブディレクトリを使用: {Path}", sourceDir);
            }

            // 新しい実行ファイルのパスを確認
            var newExePath = Path.Combine(sourceDir, "Baketa.UI.exe");
            if (!File.Exists(newExePath))
            {
                // Baketa.exeを探す（リネームされている可能性）
                newExePath = Directory.GetFiles(sourceDir, "*.exe")
                    .FirstOrDefault(f => f.Contains("Baketa", StringComparison.OrdinalIgnoreCase))
                    ?? throw new FileNotFoundException("Baketa executable not found in update package");
            }

            _logger?.LogInformation("[Updater] 新しい実行ファイル: {Path}", newExePath);

            // バッチスクリプトを別の一時ディレクトリに作成
            // 重要: スクリプトがextractDirを削除するため、スクリプト自体はextractDir外に配置する必要がある
            var batchDir = Path.Combine(Path.GetTempPath(), $"Baketa_UpdateScript_{Guid.NewGuid():N}");
            Directory.CreateDirectory(batchDir);
            var batchPath = Path.Combine(batchDir, "update.bat");
            var batchContent = CreateUpdateBatchScript(sourceDir, _appDirectory, _appExecutablePath, extractDir, batchDir);
            await File.WriteAllTextAsync(batchPath, batchContent).ConfigureAwait(false);

            _logger?.LogInformation("[Updater] 更新スクリプト作成: {Path}", batchPath);

            // バッチスクリプトを実行（現在のプロセスが終了した後に実行される）
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchPath}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var process = Process.Start(psi);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start update batch script");
            }

            _logger?.LogInformation("[Updater] 更新スクリプト起動完了 - アプリケーション終了します");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Updater] ZIPアップデート失敗");

            // エラー時のクリーンアップ
            if (extractDir != null && Directory.Exists(extractDir))
            {
                try
                {
                    Directory.Delete(extractDir, recursive: true);
                    _logger?.LogDebug("[Updater] 一時ディレクトリを削除しました: {Path}", extractDir);
                }
                catch (Exception cleanupEx)
                {
                    _logger?.LogWarning(cleanupEx, "[Updater] 一時ディレクトリの削除に失敗: {Path}", extractDir);
                }
            }

            Interlocked.Exchange(ref _isUpdating, 0);
            throw;
        }
    }

    /// <summary>
    /// 更新用バッチスクリプトを生成
    /// </summary>
    /// <param name="sourceDir">展開されたファイルのソースディレクトリ</param>
    /// <param name="targetDir">アプリのインストール先ディレクトリ</param>
    /// <param name="appExePath">アプリの実行ファイルパス</param>
    /// <param name="extractDir">ZIP展開先ディレクトリ（削除対象）</param>
    /// <param name="batchDir">バッチスクリプトのディレクトリ（自己削除対象）</param>
    private string CreateUpdateBatchScript(string sourceDir, string targetDir, string appExePath, string extractDir, string batchDir)
    {
        var processId = Environment.ProcessId;
        var appExeName = Path.GetFileName(appExePath);

        // パスをエスケープしてコマンドインジェクションを防止
        var escapedSourceDir = EscapeBatchPath(sourceDir);
        var escapedTargetDir = EscapeBatchPath(targetDir);
        var escapedAppExePath = EscapeBatchPath(appExePath);
        var escapedExtractDir = EscapeBatchPath(extractDir);
        var escapedBatchDir = EscapeBatchPath(batchDir);

        // バッチスクリプト：
        // 1. 現在のプロセスが終了するまで待機
        // 2. 新しいファイルをコピー（リトライ付き）
        // 3. アプリを再起動
        // 4. 一時ファイルを削除（extractDirとbatchDir）

        return $"""
            @echo off
            chcp 65001 > nul

            echo Baketa Update Script
            echo Waiting for application to close...

            :: 現在のプロセスが終了するまで待機（最大30秒）
            set /a count=0
            :wait_loop
            tasklist /fi "PID eq {processId}" /fi "IMAGENAME eq {appExeName}" 2>nul | find /i "{processId}" >nul
            if not errorlevel 1 (
                timeout /t 1 /nobreak > nul
                set /a count+=1
                if %count% lss 30 goto wait_loop
            )

            echo Application closed. Starting update...

            :: 読み取り専用属性を解除
            attrib -R "{escapedTargetDir}\*" /S /D 2>nul

            :: 新しいファイルをコピー（リトライ機能付き）
            set /a retry=0
            :copy_retry
            xcopy "{escapedSourceDir}\*" "{escapedTargetDir}\" /E /Y /Q /R
            if errorlevel 1 (
                set /a retry+=1
                if %retry% lss 3 (
                    echo Retry %retry%/3...
                    timeout /t 2 /nobreak > nul
                    goto copy_retry
                )
                echo Update failed after 3 retries!
                pause
                exit /b 1
            )

            echo Update complete. Restarting application...

            :: アプリを再起動（startコマンドは非同期で実行されるため、後続のクリーンアップに影響しない）
            start "" "{escapedAppExePath}"

            :: 展開先ディレクトリを削除
            rd /s /q "{escapedExtractDir}" 2>nul

            :: バッチスクリプト自身のディレクトリを削除（自己削除）
            :: cmd.exeはバッチファイルを読み込んで実行するため、ファイル削除後も実行継続可能
            rd /s /q "{escapedBatchDir}" 2>nul

            exit /b 0
            """;
    }
}
