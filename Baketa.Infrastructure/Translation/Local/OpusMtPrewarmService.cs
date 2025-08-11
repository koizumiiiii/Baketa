using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Sockets;
using System.IO;

namespace Baketa.Infrastructure.Translation.Local;

/// <summary>
/// OPUS-MTサーバーの事前起動サービス
/// アプリケーション起動時にバックグラウンドでサーバーを準備し、翻訳要求時の待機時間を削減する
/// </summary>
public sealed class OpusMtPrewarmService : IDisposable
{
    private readonly ILogger<OpusMtPrewarmService> _logger;
    private readonly string _pythonPath = "py";
    private readonly string _serverScriptPath;
    private readonly int _serverPort = 7860;
    private Process? _serverProcess;

    public OpusMtPrewarmService(ILogger<OpusMtPrewarmService> logger)
    {
        _logger = logger;
        _serverScriptPath = Path.GetFullPath("scripts/opus_mt_persistent_server.py");
        _logger.LogInformation("🚀 [PREWARM] OpusMtPrewarmService初期化完了 - スクリプト: {ScriptPath}", _serverScriptPath);
    }

    /// <summary>
    /// OPUS-MTサーバーの事前起動を開始
    /// </summary>
    public async Task StartAsync()
    {
        _logger.LogInformation("🔥 [PREWARM] バックグラウンドサーバー事前起動開始");

        try
        {
            // サーバーが既に起動しているかチェック
            if (await IsServerRunningAsync().ConfigureAwait(false))
            {
                _logger.LogInformation("✅ [PREWARM] OPUS-MTサーバーは既に起動済み - スキップ");
                return;
            }

            // サーバースクリプトの存在確認
            if (!File.Exists(_serverScriptPath))
            {
                _logger.LogError("❌ [PREWARM] サーバースクリプトが見つかりません: {Path}", _serverScriptPath);
                return;
            }

            // サーバープロセスを起動（非同期で開始）
            _ = Task.Run(async () =>
            {
                await StartServerProcessAsync().ConfigureAwait(false);
                await WaitForServerStartupAsync().ConfigureAwait(false);
                _logger.LogInformation("✅ [PREWARM] OPUS-MTサーバー事前起動完了");
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 [PREWARM] サーバー事前起動中にエラーが発生: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// サーバープロセスを起動
    /// </summary>
    private async Task StartServerProcessAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _pythonPath,
            Arguments = $"\"{_serverScriptPath}\"",
            WorkingDirectory = Path.GetDirectoryName(_serverScriptPath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _serverProcess = new Process { StartInfo = startInfo };
        
        _logger.LogInformation("🚀 [PREWARM] サーバープロセス開始: {Python} \"{Script}\"", _pythonPath, _serverScriptPath);
        
        _serverProcess.Start();
        
        // プロセス出力の非同期読み取り
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_serverProcess.HasExited)
                {
                    var line = await _serverProcess.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                    if (line != null)
                    {
                        _logger.LogDebug("📄 [PREWARM-OUT] {Output}", line);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("⚠️ [PREWARM] プロセス出力読み取りエラー: {Error}", ex.Message);
            }
        });

        // エラー出力の非同期読み取り
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_serverProcess.HasExited)
                {
                    var line = await _serverProcess.StandardError.ReadLineAsync().ConfigureAwait(false);
                    if (line != null)
                    {
                        _logger.LogWarning("⚠️ [PREWARM-ERR] {Error}", line);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("⚠️ [PREWARM] プロセスエラー読み取りエラー: {Error}", ex.Message);
            }
        });
    }

    /// <summary>
    /// サーバーの起動完了を待機
    /// </summary>
    private async Task WaitForServerStartupAsync()
    {
        const int maxRetries = 30; // 15秒間待機（500ms間隔）
        
        for (int i = 0; i < maxRetries; i++)
        {
            if (await IsServerRunningAsync().ConfigureAwait(false))
            {
                _logger.LogInformation("✅ [PREWARM] サーバー起動確認完了 - 試行回数: {Attempts}", i + 1);
                return;
            }
            
            await Task.Delay(500).ConfigureAwait(false);
        }

        _logger.LogWarning("⚠️ [PREWARM] サーバー起動確認がタイムアウト - 最大試行回数に到達");
    }

    /// <summary>
    /// サーバーが起動しているかチェック
    /// </summary>
    private async Task<bool> IsServerRunningAsync()
    {
        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync("127.0.0.1", _serverPort).ConfigureAwait(false);
            return tcpClient.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// サービス停止時にサーバープロセスをクリーンアップ
    /// </summary>
    public async Task StopAsync()
    {
        _logger.LogInformation("🛑 [PREWARM] サーバー停止処理開始");

        try
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill(true);
                await _serverProcess.WaitForExitAsync().ConfigureAwait(false);
                _logger.LogInformation("✅ [PREWARM] サーバープロセス正常終了");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 [PREWARM] サーバー停止処理エラー: {Error}", ex.Message);
        }
        finally
        {
            _serverProcess?.Dispose();
            _serverProcess = null;
        }
    }

    public void Dispose()
    {
        _logger.LogInformation("🛑 [PREWARM] Dispose呼び出し - サーバー停止処理開始");
        try
        {
            // StopAsyncの同期版のような処理
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _logger.LogWarning("⚠️ [PREWARM] Dispose中にプロセスを強制終了します");
                _serverProcess.Kill(true); // アプリ終了時なのでKillはやむを得ない
                _serverProcess.WaitForExit(5000); // タイムアウト付きで待機
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 [PREWARM] Dispose中のサーバー停止処理エラー");
        }
        finally
        {
            _serverProcess?.Dispose();
            _serverProcess = null;
        }
    }
}