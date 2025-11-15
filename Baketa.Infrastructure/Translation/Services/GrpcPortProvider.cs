using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// gRPCポート番号の非同期解決を提供するプロバイダー
/// IHostedService + Providerパターンの実装
/// ServerManagerHostedServiceがサーバー起動後にポート番号を設定し、
/// GrpcTranslationClientがDI解決時に非同期でポート番号を取得する
/// </summary>
public sealed class GrpcPortProvider
{
    private readonly TaskCompletionSource<int> _portSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ILogger<GrpcPortProvider> _logger;

    public GrpcPortProvider(ILogger<GrpcPortProvider> logger)
    {
        _logger = logger;
        _logger.LogDebug("🎯 [PROVIDER] GrpcPortProvider初期化完了");
    }

    /// <summary>
    /// gRPCサーバーのポート番号を非同期で取得します。
    /// ServerManagerHostedServiceがSetPort()を呼び出すまで待機します。
    /// </summary>
    /// <returns>gRPCサーバーのポート番号</returns>
    public Task<int> GetPortAsync()
    {
        _logger.LogDebug("🔍 [PROVIDER] GetPortAsync呼び出し - ポート番号待機中");
        return _portSource.Task;
    }

    /// <summary>
    /// gRPCサーバーのポート番号を設定します。
    /// ServerManagerHostedServiceによってサーバー起動後に呼び出されます。
    /// </summary>
    /// <param name="port">gRPCサーバーのポート番号</param>
    public void SetPort(int port)
    {
        if (_portSource.Task.IsCompleted)
        {
            _logger.LogWarning("⚠️ [PROVIDER] ポート番号は既に設定されています: {Port}", port);
            return;
        }

        if (_portSource.TrySetResult(port))
        {
            _logger.LogInformation("✅ [PROVIDER] gRPCポート番号設定完了: {Port}", port);
        }
        else
        {
            _logger.LogError("❌ [PROVIDER] ポート番号設定失敗: {Port}", port);
        }
    }

    /// <summary>
    /// ポート番号の設定に失敗したことを通知します。
    /// </summary>
    /// <param name="exception">発生した例外</param>
    public void SetException(Exception exception)
    {
        if (_portSource.Task.IsCompleted)
        {
            _logger.LogWarning("⚠️ [PROVIDER] ポート番号設定タスクは既に完了しています");
            return;
        }

        if (_portSource.TrySetException(exception))
        {
            _logger.LogError(exception, "❌ [PROVIDER] ポート番号設定エラー通知");
        }
    }
}
