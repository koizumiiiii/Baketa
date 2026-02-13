using System;
using System.IO;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// Python翻訳サーバーを起動し、ポート番号をGrpcPortProviderに設定するホストサービス
/// IHostedService + Providerパターンの実装
/// 最高優先度でアプリケーション起動時に実行され、GrpcTranslationClientの初期化前にサーバーを準備する
/// [Issue #198] IInitializationCompletionSignalを待機してから起動（ディスクI/O競合防止）
/// [Issue #292] 統合サーバーモード対応: ポート設定時にisUnifiedModeフラグを渡す
/// </summary>
public sealed class ServerManagerHostedService : IHostedService
{
    private readonly IPythonServerManager _serverManager;
    private readonly GrpcPortProvider _portProvider;
    private readonly ILogger<ServerManagerHostedService> _logger;
    private readonly IInitializationCompletionSignal? _initializationSignal;
    private readonly UnifiedServerSettings? _unifiedServerSettings;

    /// <summary>
    /// [Gemini Review] 翻訳サーバーexeのパス - 複数箇所で使用するため定数化
    /// </summary>
    private readonly string _translationServerExePath;

    /// <summary>
    /// [Fix] 統合サーバーexeのパス - 複数箇所で使用するため定数化
    /// </summary>
    private readonly string _unifiedServerExePath;

    public ServerManagerHostedService(
        IPythonServerManager serverManager,
        GrpcPortProvider portProvider,
        ILogger<ServerManagerHostedService> logger,
        IInitializationCompletionSignal? initializationSignal = null,
        UnifiedServerSettings? unifiedServerSettings = null)
    {
        _serverManager = serverManager;
        _portProvider = portProvider;
        _logger = logger;
        _initializationSignal = initializationSignal;
        _unifiedServerSettings = unifiedServerSettings;

        // [Gemini Review] パスをコンストラクタで一度だけ生成（DRY原則）
        _translationServerExePath = Path.Combine(AppContext.BaseDirectory, "grpc_server", "BaketaTranslationServer", "BaketaTranslationServer.exe");
        _unifiedServerExePath = Path.Combine(AppContext.BaseDirectory, "grpc_server", "BaketaUnifiedServer", "BaketaUnifiedServer.exe");
    }

    /// <summary>
    /// アプリケーション起動時にPython翻訳サーバーを起動します。
    /// 🎯 UltraThink Solution: appsettings.jsonのポートでサーバーを起動し、UIをブロックしません。
    /// [Issue #198] IInitializationCompletionSignalを待機してからサーバー起動（ディスクI/O競合防止）
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 [HOSTED_SERVICE] Python翻訳サーバーをバックグラウンドで起動します");

        // バックグラウンドタスクで非ブロッキング起動
        _ = Task.Run(async () =>
        {
            try
            {
                Console.WriteLine("🔧 [DEBUG] ServerManagerHostedService Task.Run開始");

                // [Issue #198] 初期化完了を待機（コンポーネントダウンロード・解凍完了まで待つ）
                // これにより、ディスクI/O高負荷時のサーバー起動を防止
                // [Gemini Review] 初回インストール時は長いタイムアウトを使用
                if (_initializationSignal != null)
                {
                    var isFirstTimeSetup = IsFirstTimeSetup();
                    var timeout = isFirstTimeSetup
                        ? TimeSpan.FromMinutes(10)  // 初回: 10分（~2.4GBダウンロード対応）
                        : TimeSpan.FromMinutes(5);   // 通常: 5分

                    _logger.LogInformation("⏳ [HOSTED_SERVICE] 初期化完了シグナルを待機中... (Mode: {Mode}, Timeout: {Timeout}分)",
                        isFirstTimeSetup ? "初回セットアップ" : "通常起動", timeout.TotalMinutes);

                    using var timeoutCts = new CancellationTokenSource(timeout);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                    try
                    {
                        await _initializationSignal.WaitForCompletionAsync(linkedCts.Token).ConfigureAwait(false);
                        _logger.LogInformation("✅ [HOSTED_SERVICE] 初期化完了シグナル受信 - サーバー起動を開始");
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        // 🔧 [Issue #228] タイムアウト時はサーバーexe存在確認を行う
                        // 低速回線でダウンロードが完了していない場合、サーバー起動を試みずにスキップ
                        _logger.LogWarning("⚠️ [HOSTED_SERVICE] 初期化完了待機がタイムアウト（{Timeout}分）しました",
                            timeout.TotalMinutes);

                        // [Fix] 統合サーバーモードでもexe存在チェックを実施
                        var isUnifiedModeForTimeout = _unifiedServerSettings?.Enabled ?? false;
                        if (isUnifiedModeForTimeout)
                        {
                            if (!File.Exists(_unifiedServerExePath))
                            {
                                _logger.LogWarning("⚠️ [HOSTED_SERVICE] 統合サーバーexeが見つかりません（ダウンロード/展開未完了の可能性） - サーバー起動を続行（ResolveServerExecutableWithRetryAsyncのリトライ待機に委ねる）");
                            }
                            else
                            {
                                _logger.LogInformation("✅ [HOSTED_SERVICE] 統合サーバーexe確認済み - サーバー起動を続行します");
                            }
                        }
                        else if (!File.Exists(_translationServerExePath))
                        {
                            _logger.LogWarning("⚠️ [HOSTED_SERVICE] 翻訳サーバーexeが見つかりません - ダウンロード未完了の可能性があります: {Path}", _translationServerExePath);
                            _logger.LogInformation("ℹ️ [HOSTED_SERVICE] サーバー起動をスキップします。ダウンロード完了後にアプリを再起動してください。");
                            return; // サーバー起動をスキップ
                        }
                        else
                        {
                            _logger.LogInformation("✅ [HOSTED_SERVICE] 翻訳サーバーexe確認済み - サーバー起動を続行します");
                        }
                    }
                }

                // 🔧 [Issue #228] サーバー起動前に必須コンポーネントの存在確認
                // ダウンロード失敗や中断後もアプリが続行した場合の早期検出
                // [Issue #292] 統合サーバーモードの場合はBaketaUnifiedServerを確認
                var isUnifiedMode = _unifiedServerSettings?.Enabled ?? false;
                if (!isUnifiedMode && !File.Exists(_translationServerExePath))
                {
                    _logger.LogWarning("⚠️ [HOSTED_SERVICE] 翻訳サーバーexeが見つかりません: {Path}", _translationServerExePath);
                    _logger.LogWarning("⚠️ [HOSTED_SERVICE] コンポーネントのダウンロードが完了していない可能性があります");
                    _logger.LogInformation("ℹ️ [HOSTED_SERVICE] 翻訳機能は使用できません。アプリを再起動してダウンロードを再試行してください。");
                    _portProvider.SetException(new InvalidOperationException(
                        "翻訳サーバーが見つかりません。コンポーネントのダウンロードが完了していない可能性があります。アプリを再起動してください。"));
                    return;
                }
                else if (isUnifiedMode)
                {
                    _logger.LogInformation("✅ [HOSTED_SERVICE] 統合サーバーモード - BaketaTranslationServer.exeの確認をスキップ");
                }

                _logger.LogInformation("🔄 [HOSTED_SERVICE] Python翻訳サーバー起動開始");
                Console.WriteLine("🔧 [DEBUG] ServerManagerHostedService: サーバー起動開始");

                // gRPCサーバーは単一サーバーがすべての言語ペアを処理するため、固定の識別子を使用
                // GrpcTranslationEngineAdapterと同じキーを使用して、Dictionary での重複登録を防ぐ
                const string defaultLanguagePair = "grpc-all";

                Console.WriteLine($"🔧 [DEBUG] ServerManagerHostedService: _serverManager.StartServerAsync呼び出し開始 (type={_serverManager.GetType().Name})");
                var serverInfo = await _serverManager.StartServerAsync(defaultLanguagePair).ConfigureAwait(false);
                Console.WriteLine($"🔧 [DEBUG] ServerManagerHostedService: _serverManager.StartServerAsync完了 Port={serverInfo.Port}");

                // [Issue #292] 統合サーバーモードの判定 (isUnifiedModeは104行目で既に定義済み)
                _logger.LogInformation("✅ [HOSTED_SERVICE] Python翻訳サーバー起動完了: Port {Port}, UnifiedMode={UnifiedMode}",
                    serverInfo.Port, isUnifiedMode);

                // GrpcPortProviderにポート番号を設定（動的ポート管理用）
                // [Issue #292] 統合サーバーモードフラグも渡す
                _portProvider.SetPort(serverInfo.Port, isUnifiedMode);

                _logger.LogInformation("🎯 [HOSTED_SERVICE] GrpcPortProvider設定完了: Port {Port}, UnifiedMode={UnifiedMode}",
                    serverInfo.Port, isUnifiedMode);

                // ヘルスチェックタイマー初期化
                _serverManager.InitializeHealthCheckTimer();

                _logger.LogInformation("🩺 [HOSTED_SERVICE] ヘルスチェックタイマー初期化完了");
            }
            catch (OperationCanceledException)
            {
                // [Gemini Review] シャットダウン時のキャンセルは正常動作として扱う
                _logger.LogInformation("ℹ️ [HOSTED_SERVICE] サーバー起動処理がキャンセルされました");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [HOSTED_SERVICE] Python翻訳サーバー起動中に予期せぬエラーが発生");

                // GrpcPortProviderに例外を通知
                _portProvider.SetException(ex);
            }
        }, cancellationToken);

        // UIスレッドをブロックしないため、即座に完了を返す
        _logger.LogInformation("✅ [HOSTED_SERVICE] StartAsync完了 - バックグラウンド起動中");
        return Task.CompletedTask;
    }

    /// <summary>
    /// [Gemini Review] 初回インストールかどうかを判定
    /// NLLBモデルが存在しない場合は初回インストールとみなす
    /// </summary>
    private bool IsFirstTimeSetup()
    {
        try
        {
            // %AppData%\Baketa\Models\nllb-200-distilled-600M-ct2\model.bin をチェック
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var modelPath = Path.Combine(appDataPath, "Baketa", "Models", "nllb-200-distilled-600M-ct2", "model.bin");

            if (!File.Exists(modelPath))
            {
                _logger.LogDebug("[HOSTED_SERVICE] NLLBモデル未検出 → 初回セットアップ");
                return true;
            }

            // [Fix] 統合サーバーexeもチェック（旧バージョンからのアップグレード対応）
            // NLLBモデルは存在するがunified_server.exeが新パスに未配置のケース
            var isUnifiedMode = _unifiedServerSettings?.Enabled ?? false;
            if (isUnifiedMode)
            {
                if (!File.Exists(_unifiedServerExePath))
                {
                    _logger.LogDebug("[HOSTED_SERVICE] 統合サーバーexe未検出 → 初回セットアップとみなす");
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HOSTED_SERVICE] 初回インストール判定中にエラー - 初回と仮定して続行");
            return true; // エラー時は安全側（初回）と仮定
        }
    }

    /// <summary>
    /// アプリケーション終了時にサーバーを停止します。
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🛑 [HOSTED_SERVICE] Python翻訳サーバー停止処理開始");

        // PythonServerManagerのDispose()で全サーバーが停止されるため、
        // ここでは明示的な停止処理は不要

        return Task.CompletedTask;
    }
}
