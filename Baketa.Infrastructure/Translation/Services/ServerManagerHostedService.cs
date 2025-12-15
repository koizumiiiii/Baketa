using System;
using System.IO;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// Python翻訳サーバーを起動し、ポート番号をGrpcPortProviderに設定するホストサービス
/// IHostedService + Providerパターンの実装
/// 最高優先度でアプリケーション起動時に実行され、GrpcTranslationClientの初期化前にサーバーを準備する
/// [Issue #198] IInitializationCompletionSignalを待機してから起動（ディスクI/O競合防止）
/// </summary>
public sealed class ServerManagerHostedService : IHostedService
{
    private readonly IPythonServerManager _serverManager;
    private readonly GrpcPortProvider _portProvider;
    private readonly ILogger<ServerManagerHostedService> _logger;
    private readonly IInitializationCompletionSignal? _initializationSignal;

    public ServerManagerHostedService(
        IPythonServerManager serverManager,
        GrpcPortProvider portProvider,
        ILogger<ServerManagerHostedService> logger,
        IInitializationCompletionSignal? initializationSignal = null)
    {
        _serverManager = serverManager;
        _portProvider = portProvider;
        _logger = logger;
        _initializationSignal = initializationSignal;
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
                        // タイムアウト時は警告ログを出力して続行
                        _logger.LogWarning("⚠️ [HOSTED_SERVICE] 初期化完了待機がタイムアウト（{Timeout}分）しました - サーバー起動を続行します",
                            timeout.TotalMinutes);
                    }
                }

                _logger.LogInformation("🔄 [HOSTED_SERVICE] Python翻訳サーバー起動開始");

                // gRPCサーバーは単一サーバーがすべての言語ペアを処理するため、固定の識別子を使用
                // GrpcTranslationEngineAdapterと同じキーを使用して、Dictionary での重複登録を防ぐ
                const string defaultLanguagePair = "grpc-all";

                var serverInfo = await _serverManager.StartServerAsync(defaultLanguagePair).ConfigureAwait(false);

                _logger.LogInformation("✅ [HOSTED_SERVICE] Python翻訳サーバー起動完了: Port {Port}", serverInfo.Port);

                // GrpcPortProviderにポート番号を設定（動的ポート管理用）
                _portProvider.SetPort(serverInfo.Port);

                _logger.LogInformation("🎯 [HOSTED_SERVICE] GrpcPortProvider設定完了: Port {Port}", serverInfo.Port);

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

            var exists = File.Exists(modelPath);
            _logger.LogDebug("[HOSTED_SERVICE] モデル存在チェック: {Path} = {Exists}", modelPath, exists);

            return !exists;
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
