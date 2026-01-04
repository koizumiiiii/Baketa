using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetSparkleUpdater;
using NetSparkleUpdater.Interfaces;
using NetSparkleUpdater.SignatureVerifiers;

namespace Baketa.UI.Services;

/// <summary>
/// [Issue #249] ZIPファイルからの自動更新を処理するカスタムSparkleUpdater
/// Single-file appの自動更新に対応
/// </summary>
public sealed class ZipSparkleUpdater : SparkleUpdater
{
    private readonly Microsoft.Extensions.Logging.ILogger? _logger;
    private readonly string _appDirectory;
    private readonly string _appExecutablePath;

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
    /// ダウンロードしたインストーラーを実行
    /// ZIPファイルの場合はカスタム処理を行う
    /// </summary>
    protected override async Task RunDownloadedInstaller(string downloadFilePath)
    {
        _logger?.LogInformation("[Issue #249] RunDownloadedInstaller: {Path}", downloadFilePath);

        // ZIPファイルでない場合はデフォルト処理
        if (!downloadFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation("[Issue #249] ZIPファイルではないためデフォルト処理");
            await base.RunDownloadedInstaller(downloadFilePath).ConfigureAwait(false);
            return;
        }

        try
        {
            // 一時ディレクトリに展開
            var extractDir = Path.Combine(Path.GetTempPath(), $"Baketa_Update_{Guid.NewGuid():N}");
            Directory.CreateDirectory(extractDir);

            _logger?.LogInformation("[Issue #249] ZIP展開先: {Path}", extractDir);

            await Task.Run(() => ZipFile.ExtractToDirectory(downloadFilePath, extractDir, overwriteFiles: true))
                .ConfigureAwait(false);

            // 展開されたディレクトリ構造を確認
            // Baketa-X.X.X/ というサブディレクトリがある場合はその中を使用
            var sourceDir = extractDir;
            var subDirs = Directory.GetDirectories(extractDir);
            if (subDirs.Length == 1 && subDirs[0].Contains("Baketa", StringComparison.OrdinalIgnoreCase))
            {
                sourceDir = subDirs[0];
                _logger?.LogInformation("[Issue #249] サブディレクトリを使用: {Path}", sourceDir);
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

            _logger?.LogInformation("[Issue #249] 新しい実行ファイル: {Path}", newExePath);

            // バッチスクリプトを作成してファイルを置き換え
            var batchPath = Path.Combine(extractDir, "update.bat");
            var batchContent = CreateUpdateBatchScript(sourceDir, _appDirectory, _appExecutablePath);
            await File.WriteAllTextAsync(batchPath, batchContent).ConfigureAwait(false);

            _logger?.LogInformation("[Issue #249] 更新スクリプト作成: {Path}", batchPath);

            // バッチスクリプトを実行（現在のプロセスが終了した後に実行される）
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchPath}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);

            _logger?.LogInformation("[Issue #249] 更新スクリプト起動完了 - アプリケーション終了します");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Issue #249] ZIPアップデート失敗");
            throw;
        }
    }

    /// <summary>
    /// 更新用バッチスクリプトを生成
    /// </summary>
    private string CreateUpdateBatchScript(string sourceDir, string targetDir, string appExePath)
    {
        var processId = Environment.ProcessId;

        // バッチスクリプト：
        // 1. 現在のプロセスが終了するまで待機
        // 2. 新しいファイルをコピー
        // 3. アプリを再起動
        // 4. 一時ファイルを削除

        return $"""
            @echo off
            chcp 65001 > nul

            echo Baketa Update Script
            echo Waiting for application to close...

            :: 現在のプロセスが終了するまで待機（最大30秒）
            set /a count=0
            :wait_loop
            tasklist /fi "PID eq {processId}" 2>nul | find /i "{processId}" >nul
            if not errorlevel 1 (
                timeout /t 1 /nobreak > nul
                set /a count+=1
                if %count% lss 30 goto wait_loop
            )

            echo Application closed. Starting update...

            :: 新しいファイルをコピー（/Y で上書き確認なし）
            xcopy "{sourceDir}\*" "{targetDir}\" /E /Y /Q

            if errorlevel 1 (
                echo Update failed! Error copying files.
                pause
                exit /b 1
            )

            echo Update complete. Restarting application...

            :: アプリを再起動
            start "" "{appExePath}"

            :: 一時ファイルを削除（少し待ってから）
            timeout /t 2 /nobreak > nul
            rd /s /q "{Path.GetDirectoryName(sourceDir)}" 2>nul

            exit /b 0
            """;
    }
}
