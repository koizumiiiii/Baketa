namespace Baketa.Core.Abstractions.Server;

/// <summary>
/// Issue #292: 統合AIサーバーマネージャーインターフェース
/// OCRと翻訳を単一プロセスで実行する統合サーバーの管理
/// </summary>
public interface IUnifiedAIServerManager : IAsyncDisposable
{
    /// <summary>
    /// サーバーが準備完了かどうか
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// サーバーポート
    /// </summary>
    int Port { get; }

    /// <summary>
    /// 統合サーバーが利用可能かどうか（exe/Pythonスクリプトが存在するか）
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 統合サーバーを起動し、準備完了まで待機
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>起動成功の場合true</returns>
    Task<bool> StartServerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// サーバー停止
    /// </summary>
    Task StopServerAsync();

    /// <summary>
    /// gRPCでサーバーの準備状態を確認
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>サーバーが準備完了の場合true</returns>
    Task<bool> CheckServerHealthAsync(CancellationToken cancellationToken = default);
}
