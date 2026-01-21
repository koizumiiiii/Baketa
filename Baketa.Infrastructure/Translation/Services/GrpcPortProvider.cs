using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// gRPCポート番号の非同期解決を提供するプロバイダー
/// IHostedService + Providerパターンの実装
/// ServerManagerHostedServiceがサーバー起動後にポート番号を設定し、
/// GrpcTranslationClientがDI解決時に非同期でポート番号を取得する
///
/// [Issue #292] 統合サーバーモード対応:
/// - 統合サーバー(50053)と分離サーバー(50051/50052)の両方に対応
/// - TryGetPort()で同期的にポート取得可能
/// </summary>
public sealed class GrpcPortProvider
{
    private readonly TaskCompletionSource<int> _portSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ILogger<GrpcPortProvider> _logger;
    private int _currentPort;
    private bool _isUnifiedMode;

    public GrpcPortProvider(ILogger<GrpcPortProvider> logger)
    {
        _logger = logger;
        _logger.LogDebug("🎯 [PROVIDER] GrpcPortProvider初期化完了");
    }

    /// <summary>
    /// [Issue #292] 統合サーバーモードかどうか
    /// </summary>
    public bool IsUnifiedMode => _isUnifiedMode;

    /// <summary>
    /// [Issue #292] 現在設定されているポート番号（設定前は0）
    /// </summary>
    public int CurrentPort => _currentPort;

    /// <summary>
    /// [Issue #292] ポート番号が設定済みかどうか
    /// </summary>
    public bool IsPortSet => _portSource.Task.IsCompleted && _currentPort > 0;

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
    /// <param name="isUnifiedMode">[Issue #292] 統合サーバーモードかどうか</param>
    public void SetPort(int port, bool isUnifiedMode = false)
    {
        if (_portSource.Task.IsCompleted)
        {
            _logger.LogWarning("⚠️ [PROVIDER] ポート番号は既に設定されています: {Port}", port);
            return;
        }

        _currentPort = port;
        _isUnifiedMode = isUnifiedMode;

        if (_portSource.TrySetResult(port))
        {
            _logger.LogInformation("✅ [PROVIDER] gRPCポート番号設定完了: {Port}, UnifiedMode={IsUnifiedMode}", port, isUnifiedMode);
        }
        else
        {
            _logger.LogError("❌ [PROVIDER] ポート番号設定失敗: {Port}", port);
        }
    }

    /// <summary>
    /// [Issue #292] ポート番号を同期的に取得します。
    /// 設定されていない場合はfalseを返します。
    /// </summary>
    /// <param name="port">取得したポート番号</param>
    /// <returns>ポート番号が設定済みの場合はtrue</returns>
    public bool TryGetPort(out int port)
    {
        if (IsPortSet)
        {
            port = _currentPort;
            return true;
        }

        port = 0;
        return false;
    }

    /// <summary>
    /// [Issue #292] gRPCサーバーアドレスを取得します。
    /// </summary>
    /// <returns>サーバーアドレス（例: "http://127.0.0.1:50053"）、未設定の場合はnull</returns>
    public string? GetServerAddress()
    {
        if (TryGetPort(out var port))
        {
            return $"http://127.0.0.1:{port}";
        }
        return null;
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
