using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// Python翻訳サーバーを起動し、ポート番号をGrpcPortProviderに設定するホストサービス
/// IHostedService + Providerパターンの実装
/// 最高優先度でアプリケーション起動時に実行され、GrpcTranslationClientの初期化前にサーバーを準備する
/// </summary>
public sealed class ServerManagerHostedService : IHostedService
{
    private readonly IPythonServerManager _serverManager;
    private readonly GrpcPortProvider _portProvider;
    private readonly ILogger<ServerManagerHostedService> _logger;

    public ServerManagerHostedService(
        IPythonServerManager serverManager,
        GrpcPortProvider portProvider,
        ILogger<ServerManagerHostedService> logger)
    {
        _serverManager = serverManager;
        _portProvider = portProvider;
        _logger = logger;
    }

    /// <summary>
    /// アプリケーション起動時にPython翻訳サーバーを起動します。
    /// 🎯 UltraThink Solution: appsettings.jsonのポートでサーバーを起動し、UIをブロックしません。
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 [HOSTED_SERVICE] Python翻訳サーバーをバックグラウンドで起動します");

        // バックグラウンドタスクで非ブロッキング起動
        _ = Task.Run(async () =>
        {
            try
            {
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ [HOSTED_SERVICE] Python翻訳サーバー起動失敗");

                // GrpcPortProviderに例外を通知
                _portProvider.SetException(ex);
            }
        }, cancellationToken);

        // UIスレッドをブロックしないため、即座に完了を返す
        _logger.LogInformation("✅ [HOSTED_SERVICE] StartAsync完了 - バックグラウンド起動中");
        return Task.CompletedTask;
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
