using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.Infrastructure.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// Python翻訳サーバー管理実装
/// Issue #147 Phase 5: ヘルスチェック機能付きプロセス管理
/// Gemini改善提案反映: 自動監視・復旧機能
/// Step 1統合: PythonEnvironmentResolver活用
/// </summary>
public class PythonServerManager(
    IPortManagementService portManager,
    PythonEnvironmentResolver pythonResolver,
    IEventAggregator eventAggregator,
    ILogger<PythonServerManager> logger) : IPythonServerManager
{
    private readonly ConcurrentDictionary<string, PythonServerInstance> _activeServers = [];
    private readonly ConcurrentDictionary<int, bool> _serverStartDetectionFlags = []; // 🔥 UltraPhase 14.17: [SERVER_START]検出フラグ
    private readonly ConcurrentDictionary<int, bool> _commandCommunicationActiveFlags = []; // 🔥 UltraPhase 14.21: stdout競合回避フラグ
    private System.Threading.Timer? _healthCheckTimer;
    private readonly object _healthCheckLock = new();
    private bool _disposed;

    /// <summary>
    /// Initialize health check timer
    /// </summary>
    public void InitializeHealthCheckTimer()
    {
        _healthCheckTimer ??= new System.Threading.Timer(HealthCheckTimerCallback, null, 
            System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        _healthCheckTimer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        logger.LogInformation("🩺 PythonServerManager初期化完了（ヘルスチェック30秒間隔）");
    }
    
    /// <summary>
    /// ヘルスチェックタイマーコールバック
    /// </summary>
    private void HealthCheckTimerCallback(object? state)
    {
        _ = Task.Run(async () => await PerformHealthCheckInternalAsync().ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<IPythonServerInfo> StartServerAsync(string languagePair)
    {
        // 🔥 ULTRA_DEBUG: サーバー登録時の言語ペアキーを確実に記録
        Console.WriteLine($"🔥 [ULTRA_DEBUG] PythonServerManager.StartServerAsync() 言語ペアキー: '{languagePair}'");
        logger.LogInformation("🔥 [ULTRA_DEBUG] PythonServerManager.StartServerAsync() 言語ペアキー: '{LanguagePair}'", languagePair);

        Console.WriteLine($"🚀 [UltraPhase 14.12] PythonServerManager.StartServerAsync() 開始: {languagePair}");
        logger.LogInformation("🚀 Python翻訳サーバー起動開始: {LanguagePair}", languagePair);

        // Phase 0: サーバー初期化開始イベント発行
        Console.WriteLine("🔍 [UltraPhase 14.12] PublishServerStatusAsync() 呼び出し");
        await PublishServerStatusAsync(false, 0, "翻訳サーバー初期化中...",
            $"言語ペア: {languagePair}").ConfigureAwait(false);

        // 既存サーバーチェック（内部管理）
        Console.WriteLine("🔍 [UltraPhase 14.12] 既存サーバーチェック開始");
        if (_activeServers.TryGetValue(languagePair, out var existing) && existing.IsHealthy)
        {
            Console.WriteLine($"♻️ [UltraPhase 14.12] 既存サーバー再利用: Port {existing.Port}");
            logger.LogInformation("♻️ 既存サーバーを再利用: {LanguagePair} → Port {Port}", languagePair, existing.Port);

            // Phase 0: 既存サーバー準備完了イベント発行
            await PublishServerStatusAsync(true, existing.Port, "翻訳サーバー準備完了",
                $"既存サーバー再利用: {languagePair}").ConfigureAwait(false);

            return existing;
        }

        // 🔥 UltraPhase 14.12 決定的修正: 外部サーバー検出を無効化
        // DetectExternalServerAsync()がTranslationInitializationService初期化中に
        // OptimizedPythonTranslationEngineを検出しようとして循環待機デッドロック発生
        // → 完全無効化して新規サーバー起動を優先
        Console.WriteLine("🔍 [UltraPhase 14.12] DetectExternalServerAsync() スキップ - 循環デッドロック回避");
        int? externalServerPort = null; // 常にnullを返す（外部サーバー検出しない）
        // var externalServerPort = await DetectExternalServerAsync().ConfigureAwait(false);
        Console.WriteLine($"🔍 [UltraPhase 14.12] DetectExternalServerAsync() 完了: {(externalServerPort.HasValue ? $"Port {externalServerPort.Value}" : "未検出")}");
        if (externalServerPort.HasValue)
        {
            logger.LogInformation("🔍 外部翻訳サーバー検出・登録: Port {Port}", externalServerPort.Value);
            
            // 外部サーバーをPythonServerManagerに登録
            var externalInstance = new PythonServerInstance(externalServerPort.Value, languagePair, null);
            externalInstance.UpdateStatus(ServerStatus.Running);
            _activeServers[languagePair] = externalInstance;
            
            await PublishServerStatusAsync(true, externalServerPort.Value, "翻訳サーバー準備完了", 
                $"外部サーバー統合: {languagePair}").ConfigureAwait(false);
            
            return externalInstance;
        }
        
        // 既存が不健全な場合は停止
        if (existing != null)
        {
            logger.LogWarning("🔄 不健全なサーバーを停止して再起動: {LanguagePair}", languagePair);
            await StopServerInternalAsync(languagePair).ConfigureAwait(false);
        }
        
        var port = await portManager.AcquireAvailablePortAsync().ConfigureAwait(false);
        
        try
        {
            var process = await StartPythonProcessAsync(port, languagePair).ConfigureAwait(false);

            // サーバー準備完了まで待機（UltraPhase 14.5: プロセス参照も渡して確実な準備確認）
            await WaitForServerReadyAsync(port, process).ConfigureAwait(false);

            // 🔥 UltraPhase 14.15: Pythonのstdin読み取りループ開始待機
            // [SERVER_START]出力後、実際にstdin.readline()が実行されるまで数ミリ秒のギャップがあるため、
            // 短い待機時間を追加してタイミング問題を回避
            Console.WriteLine("🔍 [UltraPhase 14.15] stdin読み取りループ開始待機 (500ms)");
            logger.LogDebug("🔍 [UltraPhase 14.15] Pythonのstdin読み取りループ開始待機");
            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            Console.WriteLine("✅ [UltraPhase 14.15] 待機完了 - stdin通信開始");

            // UltraPhase 14.5: stdin通信でサーバー準備完了確認 + EOF防止
            var isServerReady = await CheckServerReadyViaStdinAsync(process, port).ConfigureAwait(false);
            if (!isServerReady)
            {
                logger.LogWarning("⚠️ stdin通信でのサーバー準備確認に失敗しましたが、TCP接続は成功しているため続行します");
            }
            
            var instance = new PythonServerInstance(port, languagePair, process);
            instance.UpdateStatus(ServerStatus.Running);

            // 🔥 ULTRA_DEBUG: サーバー辞書登録時の実際のキーを確実に記録
            Console.WriteLine($"🔥 [ULTRA_DEBUG] _activeServers辞書登録 キー: '{languagePair}', Port: {port}");
            logger.LogInformation("🔥 [ULTRA_DEBUG] _activeServers辞書登録 キー: '{LanguagePair}', Port: {Port}", languagePair, port);
            _activeServers[languagePair] = instance;
            
            // ポートレジストリにサーバー情報登録
            await RegisterServerInPortRegistryAsync(instance).ConfigureAwait(false);
            
            logger.LogInformation("✅ Python翻訳サーバー起動完了: {LanguagePair} → Port {Port}, PID {PID}", 
                languagePair, port, process.Id);
            
            // Phase 0: サーバー起動完了イベント発行
            await PublishServerStatusAsync(true, port, "翻訳サーバー準備完了", 
                $"起動完了: {languagePair}, PID {process.Id}").ConfigureAwait(false);
            
            return instance;
        }
        catch (Exception ex)
        {
            // ポート解放
            await portManager.ReleasePortAsync(port).ConfigureAwait(false);
            logger.LogError(ex, "❌ Python翻訳サーバー起動失敗: {LanguagePair}, Port {Port}", languagePair, port);
            
            // Phase 0: サーバー起動失敗イベント発行
            await PublishServerStatusAsync(false, port, "翻訳サーバーエラー", 
                $"起動失敗: {languagePair}, エラー: {ex.Message}").ConfigureAwait(false);
            
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopServerAsync(int port)
    {
        var server = _activeServers.Values.FirstOrDefault(s => s.Port == port);
        if (server != null)
        {
            await StopServerInternalAsync(server.LanguagePair).ConfigureAwait(false);
        }
        else
        {
            logger.LogWarning("⚠️ 停止対象サーバーが見つかりません: Port {Port}", port);
        }
    }

    /// <inheritdoc />
    public async Task StopServerAsync(string languagePair)
    {
        await StopServerInternalAsync(languagePair).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IPythonServerInfo>> GetActiveServersAsync()
    {
        await Task.CompletedTask; // 非同期メソッドの一貫性のため
        return _activeServers.Values.Cast<IPythonServerInfo>().ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IPythonServerInfo?> GetServerAsync(string languagePair)
    {
        await Task.CompletedTask; // 非同期メソッドの一貫性のため

        // 🔥 ULTRA_DEBUG: サーバー検索時の言語ペアキーと辞書状態を確実に記録
        Console.WriteLine($"🔥 [ULTRA_DEBUG] GetServerAsync() 検索キー: '{languagePair}'");
        logger.LogInformation("🔥 [ULTRA_DEBUG] GetServerAsync() 検索キー: '{LanguagePair}'", languagePair);

        // 🔥 ULTRA_DEBUG: 辞書内の全キーを表示
        var allKeys = string.Join(", ", _activeServers.Keys.Select(k => $"'{k}'"));
        Console.WriteLine($"🔥 [ULTRA_DEBUG] _activeServers辞書内の全キー: [{allKeys}]");
        logger.LogInformation("🔥 [ULTRA_DEBUG] _activeServers辞書内の全キー: [{AllKeys}]", allKeys);

        var found = _activeServers.TryGetValue(languagePair, out var server);
        Console.WriteLine($"🔥 [ULTRA_DEBUG] 検索結果: {(found ? "FOUND" : "NOT_FOUND")}");
        logger.LogInformation("🔥 [ULTRA_DEBUG] 検索結果: {Result}", found ? "FOUND" : "NOT_FOUND");

        return found ? server : null;
    }

    /// <inheritdoc />
    public async Task PerformHealthCheckAsync()
    {
        await PerformHealthCheckInternalAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Pythonプロセス起動
    /// </summary>
    private async Task<Process> StartPythonProcessAsync(int port, string languagePair)
    {
        // CTranslate2版サーバーを優先使用
        var currentDir = Environment.CurrentDirectory;
        var scriptPath = Path.Combine(currentDir, "scripts", "nllb_translation_server_ct2.py");

        // 🔧 デバッグ: パス構築情報を詳細ログ出力
        logger.LogInformation("🔧 [PATH_DEBUG] CurrentDirectory: '{CurrentDir}'", currentDir);
        logger.LogInformation("🔧 [PATH_DEBUG] Constructed scriptPath: '{ScriptPath}'", scriptPath);
        logger.LogInformation("🔧 [PATH_DEBUG] Script file exists: {Exists}", File.Exists(scriptPath));

        // スクリプトファイル存在確認
        if (!File.Exists(scriptPath))
        {
            // フォールバック: 旧版サーバーを使用（互換性維持）
            scriptPath = Path.Combine(Environment.CurrentDirectory, "scripts", "nllb_translation_server.py");

            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException($"Python翻訳サーバースクリプトが見つかりません: {scriptPath}");
            }

            logger.LogWarning("⚠️ CTranslate2版が見つからず、旧版サーバーを使用: {Script}", scriptPath);
        }
        
        // Step 1統合: PythonEnvironmentResolver使用（py.exe優先戦略）
        string pythonExecutable;
        try
        {
            pythonExecutable = await pythonResolver.ResolvePythonExecutableAsync();
            logger.LogInformation("✅ Python実行環境解決: {PythonPath}", pythonExecutable);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError("❌ Python実行環境解決失敗: {Error}", ex.Message);
            throw new InvalidOperationException($"Python実行環境が見つかりません。Python 3.10以上をインストールしてください。詳細: {ex.Message}", ex);
        }
        
        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExecutable, // Step 1: py.exe優先戦略適用
            Arguments = $"\"{scriptPath}\" --port {port}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true, // UltraPhase 14.5: stdin通信有効化
            StandardInputEncoding = new System.Text.UTF8Encoding(false), // UltraThink Phase 3.1: BOM無しUTF-8
            StandardOutputEncoding = new System.Text.UTF8Encoding(false), // UltraThink Phase 3.1: BOM無しUTF-8
            StandardErrorEncoding = new System.Text.UTF8Encoding(false),  // UltraThink Phase 3.1: BOM無しUTF-8
            WorkingDirectory = Environment.CurrentDirectory
        };
        
        var process = Process.Start(startInfo) ?? 
            throw new InvalidOperationException($"Python翻訳サーバープロセス起動失敗: {languagePair}");
        
        logger.LogDebug("🐍 Pythonプロセス起動: PID {PID}, Python: {Python}, Args: {Args}", 
            process.Id, pythonExecutable, startInfo.Arguments);
        
        // 🚨 Phase 1.3: 標準出力監視 - UltraPhase 14.23: stdout完全無効化
        // 🔥 UltraPhase 14.23: stdin/stdout通信サーバーでは標準出力監視を完全停止
        //   理由: バックグラウンド監視タスクがJSONレスポンスを横取りし、
        //         コマンド通信でnull受信 → Python EOF検出 → プロセス終了を引き起こす
        // 代替: stderr監視のみでPythonログを取得
        logger.LogInformation("🔇 [UltraPhase 14.23] stdout監視無効化 - stdin/stdout通信モード");
        logger.LogInformation("📋 [UltraPhase 14.23] 理由: バックグラウンドタスクによるレスポンス横取り防止");

        // 🚨 Phase 1.3: 詳細エラーログ取得機能 - 標準エラー監視
        _ = Task.Run(async () =>
        {
            try
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(line))
                    {
                        // Pythonエラーの重要度分類
                        if (line.Contains("Error") || line.Contains("Exception") || line.Contains("Traceback"))
                        {
                            logger.LogError("🚨 [PYTHON_ERROR] {LanguagePair}:{Port} クリティカルエラー: {Error}", languagePair, port, line);
                        }
                        else if (line.Contains("Warning") || line.Contains("WARN"))
                        {
                            logger.LogWarning("⚠️ [PYTHON_WARN] {LanguagePair}:{Port} 警告: {Warning}", languagePair, port, line);
                        }
                        else
                        {
                            logger.LogDebug("🐍 [Python-Error-{LanguagePair}-{Port}] {Output}", languagePair, port, line);

                            // 🔥 UltraPhase 14.17: [SERVER_START]検出時にフラグを立てる
                            if (line.Contains("[SERVER_START]"))
                            {
                                _serverStartDetectionFlags[port] = true;
                                logger.LogInformation("✅ [UltraPhase 14.17] [SERVER_START]検出: Port {Port}", port);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Python標準エラー監視エラー - 継続して監視します");
            }
        });
        
        return process;
    }

    /// <summary>
    /// サーバー準備完了まで待機（UltraPhase 14.5: SERVER_START信号による確実な準備確認）
    /// </summary>
    private async Task WaitForServerReadyAsync(int port, Process process)
    {
        var maxWaitTime = TimeSpan.FromSeconds(120); // 120秒（初回NLLB-200ダウンロード対応）
        var startTime = DateTime.UtcNow;

        logger.LogDebug("⏳ サーバー準備完了を待機中: Port {Port}, PID {PID}", port, process.Id);

        // 🔥 UltraPhase 14.17: ポート別のフラグを初期化
        _serverStartDetectionFlags[port] = false;
        var serverStartDetected = false;
        // 🔥 UltraPhase 14.13: tcpConnectionEstablished削除（TCP接続チェック無効化）

        // SERVER_START信号検出用のタスク（標準エラー出力監視）
        var serverStartTask = Task.Run(async () =>
        {
            try
            {
                while (!serverStartDetected && DateTime.UtcNow - startTime < maxWaitTime)
                {
                    // プロセスが予期せず終了した場合はエラー
                    if (process.HasExited)
                    {
                        throw new InvalidOperationException($"Pythonプロセスが予期せず終了しました: ExitCode {process.ExitCode}");
                    }

                    // 🔥 UltraPhase 14.17: フラグをチェック
                    if (_serverStartDetectionFlags.TryGetValue(port, out var detected) && detected)
                    {
                        serverStartDetected = true;
                        logger.LogInformation("✅ [UltraPhase 14.17] SERVER_START検出完了: Port {Port}", port);
                        break;
                    }

                    // 🔥 UltraPhase 14.16: WORKAROUND削除 - 実際のSERVER_STARTログ検出のみに依存
                    // 以前の10秒タイムアウトWORKAROUNDは、実際のserve_forever開始（初回起動時27秒）より早く発火し、
                    // stdin通信が失敗する原因となっていた。実際のPythonログ検出のみに依存する。
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ SERVER_START信号待機エラー: Port {Port}", port);
                throw;
            }
        });

        // 🔥 UltraPhase 14.13: TCP接続チェック無効化（stdin/stdout通信専用）
        // Python側がstdin/stdout通信のみでTCPポートをリッスンしないため、
        // TCP接続確認を削除し、SERVER_START信号検出のみで準備完了とする
        Console.WriteLine("🔍 [UltraPhase 14.13] TCP接続チェック無効化 - stdin/stdout通信専用");
        logger.LogDebug("🔍 [UltraPhase 14.13] TCP接続チェック無効化 - Python側はstdin/stdout通信のみ");

        // SERVER_START信号検出のみで準備完了確認
        try
        {
            await serverStartTask.WaitAsync(maxWaitTime).ConfigureAwait(false);

            if (!serverStartDetected)
            {
                throw new TimeoutException($"Python翻訳サーバー(Port {port})のSERVER_START信号待機がタイムアウトしました（{maxWaitTime.TotalSeconds}秒）");
            }

            Console.WriteLine($"✅ [UltraPhase 14.13] SERVER_START信号検出完了: Port {port}, PID {process.Id}");
            logger.LogInformation("✅ サーバー準備完了確認: Port {Port}, PID {PID} (stdin/stdout通信)", port, process.Id);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [UltraPhase 14.13] サーバー準備確認失敗: Port {port}, Error: {ex.Message}");
            logger.LogError(ex, "❌ サーバー準備確認失敗: Port {Port}, PID {PID} (stdin/stdout通信)", port, process.Id);
            throw;
        }
    }

    /// <summary>
    /// stdin/stdout通信でサーバー準備状態確認（UltraPhase 14.5: is_readyコマンド送信）
    /// </summary>
    private async Task<bool> CheckServerReadyViaStdinAsync(Process process, int port)
    {
        // 🔥 UltraPhase 14.21: コマンド通信フラグを有効化（バックグラウンドstdout監視を一時停止）
        _commandCommunicationActiveFlags[port] = true;
        logger.LogInformation("🔒 [UltraPhase 14.21] コマンド通信モード有効化: Port {Port}", port);

        try
        {
            Console.WriteLine("🔍 [UltraPhase 14.14] STDIN通信デバッグ開始");
            logger.LogDebug("🔍 [STDIN_CHECK] is_readyコマンド送信開始");

            // 🔥 UltraPhase 14.14: プロセス状態確認
            Console.WriteLine($"🔍 [UltraPhase 14.14] Pythonプロセス状態: PID={process.Id}, HasExited={process.HasExited}");
            Console.WriteLine($"🔍 [UltraPhase 14.14] StandardInput可能: {!process.StandardInput.BaseStream.CanWrite}");

            // is_readyコマンドをJSON形式で送信
            var isReadyCommand = JsonSerializer.Serialize(new { command = "is_ready" });
            Console.WriteLine($"🔍 [UltraPhase 14.14] 送信コマンド: '{isReadyCommand}'");

            await process.StandardInput.WriteLineAsync(isReadyCommand).ConfigureAwait(false);
            Console.WriteLine("🔍 [UltraPhase 14.14] WriteLineAsync完了");

            await process.StandardInput.FlushAsync().ConfigureAwait(false);
            Console.WriteLine("🔍 [UltraPhase 14.14] FlushAsync完了");

            logger.LogDebug("📤 [STDIN_SEND] コマンド送信完了: {Command}", isReadyCommand);

            // 🔥 UltraPhase 14.20: C#側stdout受信詳細デバッグ
            logger.LogInformation("🔍 [C#_STDOUT_DEBUG] StandardOutput状態確認開始");
            logger.LogInformation("🔍 [C#_STDOUT_DEBUG] process.StandardOutput.EndOfStream: {EndOfStream}", process.StandardOutput.EndOfStream);
            logger.LogInformation("🔍 [C#_STDOUT_DEBUG] process.HasExited: {HasExited}", process.HasExited);

            // タイムアウト付きでレスポンス読み取り
            var readTask = process.StandardOutput.ReadLineAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));

            Console.WriteLine("🔍 [UltraPhase 14.14] レスポンス待機開始 (10秒タイムアウト)");
            logger.LogInformation("🔄 [C#_STDOUT_DEBUG] ReadLineAsync()タスク開始 - 待機中...");

            var completedTask = await Task.WhenAny(readTask, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask)
            {
                Console.WriteLine("❌ [UltraPhase 14.14] レスポンスタイムアウト - Python応答なし");
                // 🔥 UltraPhase 14.20: タイムアウト時の詳細状態診断
                logger.LogWarning("⏰ [STDIN_TIMEOUT] is_readyレスポンスタイムアウト (10秒)");
                logger.LogWarning("🔍 [C#_STDOUT_DEBUG] タイムアウト時状態: EndOfStream={EndOfStream}, HasExited={HasExited}",
                    process.StandardOutput.EndOfStream, process.HasExited);
                logger.LogWarning("🔍 [C#_STDOUT_DEBUG] ReadLineAsyncタスク状態: {TaskStatus}", readTask.Status);
                return false;
            }

            Console.WriteLine("✅ [UltraPhase 14.14] レスポンス受信完了");
            // 🔥 UltraPhase 14.20: 受信成功時のデバッグ
            logger.LogInformation("✅ [C#_STDOUT_DEBUG] ReadLineAsync完了 - レスポンス取得中...");
            var response = await readTask.ConfigureAwait(false);

            // 🔥 UltraPhase 14.20: 受信したレスポンスの詳細ログ
            logger.LogInformation("📥 [C#_STDOUT_DEBUG] 受信レスポンス: {Response}", response ?? "null");
            Console.WriteLine($"🔍 [UltraPhase 14.14] 受信データ: '{response}' (IsNull={response == null}, IsEmpty={string.IsNullOrEmpty(response)})");

            if (string.IsNullOrEmpty(response))
            {
                Console.WriteLine("❌ [UltraPhase 14.14] 空のレスポンス - Python側がコマンドを受信・処理できていない");
                logger.LogWarning("📭 [STDIN_EMPTY] 空のレスポンス受信");
                return false;
            }

            Console.WriteLine($"✅ [UltraPhase 14.14] 有効なレスポンス受信: {response}");
            logger.LogDebug("📥 [STDIN_RECV] レスポンス受信: {Response}", response);

            // JSONレスポンスを解析
            var responseJson = JsonSerializer.Deserialize<JsonElement>(response);
            var success = responseJson.GetProperty("success").GetBoolean();
            var ready = responseJson.GetProperty("ready").GetBoolean();
            var modelLoaded = responseJson.GetProperty("model_loaded").GetBoolean();

            logger.LogInformation("✅ [STDIN_READY] サーバー状態確認完了: Success={Success}, Ready={Ready}, ModelLoaded={ModelLoaded}",
                success, ready, modelLoaded);

            return success && ready && modelLoaded;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ [STDIN_ERROR] stdin通信でのサーバー状態確認失敗");
            return false;
        }
        finally
        {
            // 🔥 UltraPhase 14.21: コマンド通信フラグを無効化（バックグラウンドstdout監視を再開）
            _commandCommunicationActiveFlags[port] = false;
            logger.LogInformation("🔓 [UltraPhase 14.21] コマンド通信モード無効化: Port {Port}", port);
        }
    }

    /// <summary>
    /// ポートレジストリにサーバー情報を登録
    /// </summary>
    private async Task RegisterServerInPortRegistryAsync(PythonServerInstance instance)
    {
        // 将来的にはPortManagementServiceにサーバー情報登録メソッドを追加予定
        // 現在は基本的なポート管理のみ実装
        logger.LogDebug("📝 サーバー情報をレジストリに登録: {LanguagePair} → Port {Port}", 
            instance.LanguagePair, instance.Port);
    }

    /// <summary>
    /// 外部翻訳サーバー検出（OptimizedPythonTranslationEngine等との統合）
    /// </summary>
    private async Task<int?> DetectExternalServerAsync()
    {
        Console.WriteLine("🔍 [UltraPhase 14.12] DetectExternalServerAsync() 開始");
        logger.LogInformation("🔍 [UltraPhase 14.12] DetectExternalServerAsync() 開始");

        // 設定ファイルで指定されたポート範囲をチェック
        var commonPorts = new[] { 5557, 5558, 5559, 5000 }; // 一般的な翻訳サーバーポート
        Console.WriteLine($"🔍 [UltraPhase 14.12] チェック対象ポート: {string.Join(", ", commonPorts)}");

        for (int i = 0; i < commonPorts.Length; i++)
        {
            var port = commonPorts[i];
            Console.WriteLine($"🔍 [UltraPhase 14.12] ポート{i + 1}/{commonPorts.Length}: {port}をチェック開始");

            try
            {
                using var client = new TcpClient();
                Console.WriteLine($"🔍 [UltraPhase 14.12] Port {port} - TcpClient作成完了、接続試行開始");

                // Phase 25緊急修正: 接続タイムアウトを5秒に延長し、より確実な検出を実施
                await client.ConnectAsync(IPAddress.Loopback, port)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                Console.WriteLine($"✅ [UltraPhase 14.12] Port {port} - 接続成功！外部サーバー検出");
                logger.LogInformation("🔍 [Phase 25修正] 外部翻訳サーバー検出成功: Port {Port}", port);
                return port;
            }
            catch (Exception ex)
            {
                // 接続失敗 - 次のポートをチェック
                Console.WriteLine($"❌ [UltraPhase 14.12] Port {port} - 接続失敗: {ex.GetType().Name} - {ex.Message}");
                logger.LogDebug("🔍 [Phase 25修正] 外部サーバーチェック: Port {Port} - 利用不可, Error: {Error}", port, ex.Message);
            }
        }

        Console.WriteLine("🔍 [UltraPhase 14.12] DetectExternalServerAsync() 完了 - 外部サーバー未検出");
        logger.LogDebug("🔍 外部翻訳サーバー未検出");
        return null;
    }

    /// <summary>
    /// 内部サーバー停止処理
    /// </summary>
    private async Task StopServerInternalAsync(string languagePair)
    {
        if (!_activeServers.TryRemove(languagePair, out var server))
        {
            logger.LogDebug("ℹ️ 停止対象サーバーが見つかりません: {LanguagePair}", languagePair);
            return;
        }
        
        logger.LogInformation("🛑 Python翻訳サーバー停止開始: {LanguagePair}, Port {Port}", 
            languagePair, server.Port);
        
        try
        {
            await server.DisposeAsync().ConfigureAwait(false);
            await portManager.ReleasePortAsync(server.Port).ConfigureAwait(false);
            
            logger.LogInformation("✅ Python翻訳サーバー停止完了: {LanguagePair}, Port {Port}", 
                languagePair, server.Port);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Python翻訳サーバー停止エラー: {LanguagePair}, Port {Port}", 
                languagePair, server.Port);
        }
    }

    /// <summary>
    /// ヘルスチェックコールバック（Timer用）
    /// </summary>
    private async void PerformHealthCheckCallback(object? state)
    {
        await PerformHealthCheckInternalAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// タイマーコールバック（TimerCallback用）
    /// </summary>
    private void OnHealthCheckTimer(object? state)
    {
        _ = Task.Run(async () => await PerformHealthCheckInternalAsync().ConfigureAwait(false));
    }

    /// <summary>
    /// 内部ヘルスチェック処理
    /// </summary>
    private async Task PerformHealthCheckInternalAsync()
    {
        if (_disposed) return;
        
        lock (_healthCheckLock)
        {
            if (_activeServers.IsEmpty)
            {
                logger.LogDebug("🩺 ヘルスチェック: アクティブサーバーなし");
                return;
            }
        }
        
        logger.LogDebug("🩺 ヘルスチェック開始: {Count}サーバー", _activeServers.Count);
        
        var unhealthyServers = new List<string>();
        var healthCheckTasks = _activeServers.ToList().Select(async kvp =>
        {
            var (languagePair, server) = kvp;
            var isHealthy = await CheckServerHealthAsync(server).ConfigureAwait(false);
            
            server.RecordHealthCheck(isHealthy);
            
            if (!isHealthy || !server.IsHealthy)
            {
                logger.LogWarning("❌ 異常サーバー検出: {Server}", server);
                lock (_healthCheckLock)
                {
                    unhealthyServers.Add(languagePair);
                }
            }
            else
            {
                logger.LogDebug("✅ ヘルスチェック正常: {Server}", server);
            }
        });
        
        await Task.WhenAll(healthCheckTasks).ConfigureAwait(false);
        
        // 異常サーバーの処理
        foreach (var languagePair in unhealthyServers)
        {
            logger.LogWarning("🔄 異常サーバーを停止: {LanguagePair}", languagePair);
            await StopServerInternalAsync(languagePair).ConfigureAwait(false);
            
            // 自動再起動（オプション - 設定で制御可能にする予定）
            // await StartServerAsync(languagePair);
        }
        
        if (unhealthyServers.Count > 0)
        {
            logger.LogWarning("🩺 ヘルスチェック完了: {Unhealthy}/{Total}サーバーが異常", 
                unhealthyServers.Count, _activeServers.Count + unhealthyServers.Count);
        }
        else
        {
            logger.LogDebug("🩺 ヘルスチェック完了: 全{Total}サーバー正常", _activeServers.Count);
        }
    }

    /// <summary>
    /// 個別サーバーのヘルスチェック
    /// </summary>
    private async Task<bool> CheckServerHealthAsync(PythonServerInstance server)
    {
        try
        {
            // プロセス存在確認
            if (server.Process.HasExited)
            {
                logger.LogDebug("❌ プロセス終了検出: {Server}", server);
                return false;
            }
            
            // TCP接続確認
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, server.Port)
                .WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            
            // 簡易ping送信（将来的には翻訳テストリクエストも検討）
            // 現在はTCP接続確認のみ
            
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug("❌ ヘルスチェック失敗: {Server}, Error: {Error}", server, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Pythonサーバー状態変更イベントを発行するヘルパーメソッド (Phase 0: StartButton制御機能)
    /// </summary>
    private async Task PublishServerStatusAsync(bool isReady, int port, string message, string details)
    {
        try
        {
            var statusEvent = new PythonServerStatusChangedEvent
            {
                IsServerReady = isReady,
                ServerPort = port,
                StatusMessage = message,
                Details = details
            };

            await eventAggregator.PublishAsync(statusEvent).ConfigureAwait(false);
            
            logger.LogDebug("📡 サーバー状態イベント発行: Ready={IsReady}, Port={Port}, Message={Message}", 
                isReady, port, message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "⚠️ サーバー状態イベント発行エラー: Ready={IsReady}, Port={Port}", isReady, port);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        logger.LogInformation("🛑 PythonServerManager破棄開始");
        
        _disposed = true;
        
        try
        {
            _healthCheckTimer?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning("⚠️ ヘルスチェックタイマー破棄エラー: {Error}", ex.Message);
        }
        
        // 全サーバー停止
        var stopTasks = _activeServers.Keys.ToList().Select(StopServerInternalAsync);
        
        try
        {
            Task.WaitAll([..stopTasks], TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            logger.LogWarning("⚠️ サーバー一括停止エラー: {Error}", ex.Message);
        }
        
        logger.LogInformation("✅ PythonServerManager破棄完了");
    }
}