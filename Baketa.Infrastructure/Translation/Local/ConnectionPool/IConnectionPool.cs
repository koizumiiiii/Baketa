using System;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Infrastructure.Translation.Local.ConnectionPool;

/// <summary>
/// 接続プールのインターフェース
/// </summary>
public interface IConnectionPool : IAsyncDisposable
{
    /// <summary>
    /// アクティブな接続数を取得する
    /// </summary>
    int ActiveConnections { get; }

    /// <summary>
    /// 接続プールから接続を取得する
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>永続的な接続</returns>
    Task<PersistentConnection> GetConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 接続をプールに返却する
    /// </summary>
    /// <param name="connection">返却する接続</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task ReturnConnectionAsync(PersistentConnection connection, CancellationToken cancellationToken = default);

    /// <summary>
    /// すべての接続のヘルスチェックを実行する
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task PerformHealthCheckAsync(CancellationToken cancellationToken = default);
}