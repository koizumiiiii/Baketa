using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// OPUS-MT翻訳エンジン事前ウォームアップサービス実装
/// 🔧 [TCP_STABILIZATION] 高優先タスク: 事前サーバー起動による60秒→0秒削減
/// </summary>
public class OpusMtPrewarmService : IOpusMtPrewarmService, IDisposable
{
    private readonly ILogger<OpusMtPrewarmService> _logger;
    private readonly TransformersOpusMtEngine _opusMtEngine;
    private volatile bool _isPrewarmed;
    private volatile string _prewarmStatus = "未開始";
    private bool _disposed;
    
    // 🚀 Python サーバープロセス管理
    private Process? _pythonServerProcess;
    private readonly string _scriptPath;
    private const int ServerPort = 7860;
    private const string ServerHost = "127.0.0.1";

    public OpusMtPrewarmService(
        ILogger<OpusMtPrewarmService> logger,
        TransformersOpusMtEngine opusMtEngine)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _opusMtEngine = opusMtEngine ?? throw new ArgumentNullException(nameof(opusMtEngine));
        
        // Pythonスクリプトパスを設定
        _scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "scripts", "opus_mt_persistent_server.py");
        if (!File.Exists(_scriptPath))
        {
            // フォールバック: 相対パス
            _scriptPath = Path.Combine("scripts", "opus_mt_persistent_server.py");
        }
        
        Console.WriteLine("🔥 [PREWARM_DEBUG] OpusMtPrewarmService作成完了");
        Console.WriteLine($"🔍 [PYTHON_SERVER] スクリプトパス: {_scriptPath}");
        _logger.LogInformation("OPUS-MT事前ウォームアップサービスが初期化されました");
    }

    /// <inheritdoc/>
    public bool IsPrewarmed => _isPrewarmed;

    /// <inheritdoc/>
    public string PrewarmStatus => _prewarmStatus;

    /// <inheritdoc/>
    public async Task StartPrewarmingAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            _logger.LogWarning("サービスが破棄されているため、プリウォーミングを開始できません");
            return;
        }

        _logger.LogInformation("🔥 [PREWARMING] OPUS-MT事前ウォームアップを開始します...");
        Console.WriteLine("🔥 [PREWARMING] OPUS-MT事前ウォームアップを開始します...");
        _prewarmStatus = "サーバー起動中...";

        // バックグラウンドでウォームアップを実行（メインアプリケーション起動をブロックしない）
        _ = Task.Run(async () =>
        {
            try
            {
                await PerformPrewarmingAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "事前ウォームアップ中にエラーが発生しました");
                _prewarmStatus = $"エラー: {ex.Message}";
            }
        }, cancellationToken);
    }

    private async Task PerformPrewarmingAsync(CancellationToken cancellationToken)
    {
        try
        {
            // フェーズ0: Pythonサーバー起動確認・起動
            Console.WriteLine("🔥 [PREWARMING] フェーズ0: Pythonサーバー確認・起動");
            _prewarmStatus = "Pythonサーバー起動中...";
            
            if (!await IsServerRunningAsync().ConfigureAwait(false))
            {
                Console.WriteLine("🚀 [PYTHON_SERVER] サーバーが起動していません。新しいサーバープロセスを開始します");
                await StartPythonServerAsync(cancellationToken).ConfigureAwait(false);
                
                // サーバー起動を待つ（最大30秒）
                Console.WriteLine("⏳ [PYTHON_SERVER] サーバー起動を待機中...");
                if (!await WaitForServerStartupAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Pythonサーバーの起動に失敗しました");
                }
                Console.WriteLine("✅ [PYTHON_SERVER] サーバー起動完了");
            }
            else
            {
                Console.WriteLine("✅ [PYTHON_SERVER] サーバーは既に起動済みです");
            }
            
            // フェーズ1: OPUS-MTエンジンの初期化
            Console.WriteLine("🔥 [PREWARMING] フェーズ1: OPUS-MTエンジン初期化開始");
            _prewarmStatus = "OPUS-MTエンジン初期化中...";
            
            await _opusMtEngine.InitializeAsync().ConfigureAwait(false);
            
            Console.WriteLine("✅ [PREWARMING] フェーズ1完了: OPUS-MTエンジン初期化完了");
            
            // フェーズ2: テスト翻訳実行（モデルロード確認）
            Console.WriteLine("🔥 [PREWARMING] フェーズ2: テスト翻訳開始");
            _prewarmStatus = "モデルウォームアップ中...";
            
            // 短いテスト文で英→日翻訳を実行
            var testText = "Hello";
            var testRequest = new TranslationRequest
            {
                SourceText = testText,
                SourceLanguage = Language.English,
                TargetLanguage = Language.Japanese
            };
            var testResult = await _opusMtEngine.TranslateAsync(testRequest, cancellationToken).ConfigureAwait(false);
            
            if (testResult.IsSuccess)
            {
                Console.WriteLine($"✅ [PREWARMING] フェーズ2完了: テスト翻訳成功 '{testText}' → '{testResult.TranslatedText}'");
                _prewarmStatus = "ウォームアップ完了";
                _isPrewarmed = true;
                
                _logger.LogInformation("🎉 [PREWARMING] OPUS-MT事前ウォームアップが正常に完了しました");
                Console.WriteLine("🎉 [PREWARMING] OPUS-MT事前ウォームアップが正常に完了しました");
            }
            else
            {
                throw new InvalidOperationException($"テスト翻訳が失敗しました: {testResult.Error?.Message ?? "不明なエラー"}");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("事前ウォームアップがキャンセルされました");
            _prewarmStatus = "キャンセル済み";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "事前ウォームアップでエラーが発生しました: {Error}", ex.Message);
            _prewarmStatus = $"失敗: {ex.Message}";
            
            Console.WriteLine($"❌ [PREWARMING] ウォームアップエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// Pythonサーバーが起動しているかを確認
    /// </summary>
    private async Task<bool> IsServerRunningAsync()
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connectTask = client.ConnectAsync(ServerHost, ServerPort);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2));
            
            var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
            
            if (completedTask == connectTask && client.Connected)
            {
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Pythonサーバープロセスを起動
    /// </summary>
    private async Task StartPythonServerAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 既存のプロセスがある場合は停止
            if (_pythonServerProcess != null && !_pythonServerProcess.HasExited)
            {
                Console.WriteLine("🔧 [PYTHON_SERVER] 既存プロセスを停止中...");
                StopPythonServer();
            }
            
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "py",
                Arguments = _scriptPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(_scriptPath) ?? Environment.CurrentDirectory
            };
            
            Console.WriteLine($"🚀 [PYTHON_SERVER] プロセス開始: py {_scriptPath}");
            _pythonServerProcess = new Process
            {
                StartInfo = processStartInfo,
                EnableRaisingEvents = true
            };
            
            // プロセス終了イベントハンドラー
            _pythonServerProcess.Exited += (sender, args) =>
            {
                Console.WriteLine($"⚠️ [PYTHON_SERVER] Pythonサーバープロセスが終了しました (ExitCode: {_pythonServerProcess?.ExitCode})");
                _logger.LogWarning("Pythonサーバープロセスが予期せず終了しました");
            };
            
            // 標準出力/エラー出力のハンドリング
            _pythonServerProcess.OutputDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    Console.WriteLine($"[PYTHON_SERVER] {args.Data}");
                }
            };
            
            _pythonServerProcess.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    Console.WriteLine($"[PYTHON_SERVER_ERR] {args.Data}");
                }
            };
            
            _pythonServerProcess.Start();
            _pythonServerProcess.BeginOutputReadLine();
            _pythonServerProcess.BeginErrorReadLine();
            
            Console.WriteLine($"✅ [PYTHON_SERVER] プロセス開始成功 (PID: {_pythonServerProcess.Id})");
            _logger.LogInformation("Pythonサーバープロセスを開始しました (PID: {ProcessId})", _pythonServerProcess.Id);
            
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false); // 起動時間を確保
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [PYTHON_SERVER] プロセス開始失敗: {ex.Message}");
            _logger.LogError(ex, "Pythonサーバープロセスの開始に失敗しました");
            throw;
        }
    }
    
    /// <summary>
    /// サーバー起動を待機
    /// </summary>
    private async Task<bool> WaitForServerStartupAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        while (stopwatch.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
        {
            if (await IsServerRunningAsync().ConfigureAwait(false))
            {
                Console.WriteLine($"✅ [PYTHON_SERVER] サーバー起動確認 (経過時間: {stopwatch.Elapsed.TotalSeconds:F1}s)");
                return true;
            }
            
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }
        
        Console.WriteLine($"⏰ [PYTHON_SERVER] サーバー起動タイムアウト (経過時間: {stopwatch.Elapsed.TotalSeconds:F1}s)");
        return false;
    }
    
    /// <summary>
    /// Pythonサーバープロセスを停止
    /// </summary>
    private void StopPythonServer()
    {
        try
        {
            if (_pythonServerProcess != null && !_pythonServerProcess.HasExited)
            {
                Console.WriteLine($"🔧 [PYTHON_SERVER] プロセス停止開始 (PID: {_pythonServerProcess.Id})");
                
                _pythonServerProcess.Kill();
                _pythonServerProcess.WaitForExit(5000); // 5秒待機
                
                Console.WriteLine("✅ [PYTHON_SERVER] プロセス停止完了");
                _logger.LogInformation("Pythonサーバープロセスを停止しました");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ [PYTHON_SERVER] プロセス停止エラー: {ex.Message}");
            _logger.LogWarning(ex, "Pythonサーバープロセスの停止中にエラーが発生しました");
        }
        finally
        {
            _pythonServerProcess?.Dispose();
            _pythonServerProcess = null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Console.WriteLine("🔧 [PYTHON_SERVER] OpusMtPrewarmService.Dispose() - Pythonプロセス停止開始");
            StopPythonServer();
            
            _disposed = true;
            _logger.LogDebug("OpusMtPrewarmServiceがディスポーズされました");
        }
    }
}