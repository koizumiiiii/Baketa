namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// Issue #292: OCRサーバー管理インターフェース
/// SuryaServerManagerと統合サーバーの共通抽象化
/// </summary>
public interface IOcrServerManager : IAsyncDisposable
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
    /// サーバーを起動し、準備完了まで待機
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>起動成功の場合true</returns>
    Task<bool> StartServerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// サーバーを停止
    /// </summary>
    Task StopServerAsync();
}
