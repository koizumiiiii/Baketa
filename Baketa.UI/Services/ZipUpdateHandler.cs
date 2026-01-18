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
    private readonly Func<Task>? _onUpdateReadyCallback;
    private static int _isUpdating;

    /// <summary>アプリケーション名（ディレクトリ/ファイル名検索用）</summary>
    private const string AppName = "Baketa";

    /// <summary>実行ファイル名（優先）</summary>
    private const string AppExecutableNamePrimary = "Baketa.UI.exe";

    /// <summary>実行ファイル名（リネーム後）</summary>
    private const string AppExecutableNameSecondary = "Baketa.exe";

    /// <summary>
    /// ZipSparkleUpdaterを初期化します
    /// </summary>
    /// <param name="appcastUrl">AppCastのURL</param>
    /// <param name="signatureVerifier">署名検証オブジェクト</param>
    /// <param name="referenceAssemblyPath">参照アセンブリパス</param>
    /// <param name="logger">ロガー</param>
    /// <param name="onUpdateReadyCallback">
    /// 更新準備完了時のコールバック。
    /// バッチスクリプト起動後に呼び出され、アプリケーション終了処理を実行する。
    /// これが呼ばれないと、バッチスクリプトがアプリ終了を待機したままタイムアウトする。
    /// </param>
    public ZipSparkleUpdater(
        string appcastUrl,
        ISignatureVerifier signatureVerifier,
        string? referenceAssemblyPath = null,
        Microsoft.Extensions.Logging.ILogger? logger = null,
        Func<Task>? onUpdateReadyCallback = null)
        : base(appcastUrl, signatureVerifier, referenceAssemblyPath)
    {
        _logger = logger;
        _onUpdateReadyCallback = onUpdateReadyCallback;
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
            // [Gemini Review] LINQで複数サブディレクトリにも対応、StartsWith で厳密判定
            var sourceDir = extractDir;
            var baketaSourceDir = Directory
                .GetDirectories(extractDir)
                .FirstOrDefault(d => Path.GetFileName(d).StartsWith(AppName, StringComparison.OrdinalIgnoreCase));

            if (baketaSourceDir != null)
            {
                sourceDir = baketaSourceDir;
                _logger?.LogInformation("[Updater] サブディレクトリをソースとして使用: {Path}", sourceDir);
            }

            // 新しい実行ファイルのパスを確認
            // [Gemini Review] 定数を使用してマジックストリングを排除
            var newExePath = Path.Combine(sourceDir, AppExecutableNamePrimary);
            if (!File.Exists(newExePath))
            {
                // Baketa.exeを探す（リネームされている可能性）
                newExePath = Path.Combine(sourceDir, AppExecutableNameSecondary);
                if (!File.Exists(newExePath))
                {
                    // [Gemini Review] エラーメッセージに検索パスを含める
                    throw new FileNotFoundException(
                        $"{AppName} executable not found in update package. Searched directory: '{sourceDir}'");
                }
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

            _logger?.LogInformation("[Updater] 更新スクリプト起動完了");

            // アプリケーション終了コールバックを呼び出し
            // 重要: これが呼ばれないとバッチスクリプトがアプリ終了を30秒待機してタイムアウトする
            if (_onUpdateReadyCallback != null)
            {
                _logger?.LogInformation("[Updater] アプリケーション終了コールバック呼び出し");
                try
                {
                    await _onUpdateReadyCallback().ConfigureAwait(false);
                }
                catch (Exception callbackEx)
                {
                    // コールバック失敗時もログを出力して続行
                    // Environment.Exit(0)をフォールバックとして使用
                    _logger?.LogError(callbackEx, "[Updater] 終了コールバック失敗 - Environment.Exit(0)にフォールバック");
                    Environment.Exit(0);
                }
            }
            else
            {
                // コールバック未設定の場合は直接終了（フォールバック）
                _logger?.LogWarning("[Updater] 終了コールバック未設定 - Environment.Exit(0)を使用");
                Environment.Exit(0);
            }
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

        // [Issue #306] Phase 1: robocopy + 100ms待機間隔で高速化
        // $$"""を使用: 補間には {{...}} を使い、単一の { } はリテラルとして扱われる
        return $$"""
            @echo off
            chcp 65001 > nul

            echo Baketa Update Script
            echo Waiting for application to close...

            :: [Issue #306] 現在のプロセスが終了するまで待機（100ms間隔、最大30秒）
            :: PowerShellを使用して高速なプロセス終了検知を実現
            powershell -NoProfile -Command "$p={{processId}}; 1..300 | ForEach-Object -Process { if (-not (Get-Process -Id $p -EA 0)) { exit 0 }; Start-Sleep -Milliseconds 100 }"

            echo Application closed. Starting update...

            :: 読み取り専用属性を解除
            attrib -R "{{escapedTargetDir}}\*" /S /D 2>nul

            :: [Issue #306] robocopyで高速コピー（差分のみ、4スレッド並列）
            :: /MIR: ミラーリング（差分のみコピー）
            :: /MT:4: 4スレッド並列処理
            :: /NP /NFL /NDL: 進捗・ファイル名・ディレクトリ名表示抑制
            :: /R:3 /W:1: リトライ3回、待機1秒
            :: /XD: 除外ディレクトリ（grpc_serverディレクトリ内の既存ファイルを保護）
            robocopy "{{escapedSourceDir}}" "{{escapedTargetDir}}" /MIR /MT:4 /NP /NFL /NDL /R:3 /W:1 /XD "grpc_server\dist" "grpc_server\venv*"

            :: robocopyの終了コード: 0-7は成功、8以上はエラー
            if errorlevel 8 (
                echo Update failed! Error code: %%errorlevel%%
                pause
                exit /b 1
            )

            echo Update complete. Restarting application...

            :: アプリを再起動（startコマンドは非同期で実行されるため、後続のクリーンアップに影響しない）
            start "" "{{escapedAppExePath}}"

            :: 展開先ディレクトリを削除
            rd /s /q "{{escapedExtractDir}}" 2>nul

            :: バッチスクリプト自身のディレクトリを削除（自己削除）
            :: cmd.exeはバッチファイルを読み込んで実行するため、ファイル削除後も実行継続可能
            rd /s /q "{{escapedBatchDir}}" 2>nul

            exit /b 0
            """;
    }
}
