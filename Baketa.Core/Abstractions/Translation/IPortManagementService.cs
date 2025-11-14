namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// ポート管理サービスインターフェース
/// Issue #147 Phase 5: ポート競合防止機構
/// </summary>
public interface IPortManagementService : IDisposable
{
    /// <summary>
    /// 利用可能なポートを取得します
    /// </summary>
    /// <param name="startPort">検索開始ポート</param>
    /// <param name="endPort">検索終了ポート</param>
    /// <returns>取得したポート番号</returns>
    /// <exception cref="InvalidOperationException">利用可能なポートがない場合</exception>
    /// <exception cref="TimeoutException">Mutex取得がタイムアウトした場合</exception>
    Task<int> AcquireAvailablePortAsync(int startPort = 5556, int endPort = 5562);

    /// <summary>
    /// ポートを解放します
    /// </summary>
    /// <param name="port">解放するポート番号</param>
    Task ReleasePortAsync(int port);

    /// <summary>
    /// ポートが利用可能かチェックします
    /// </summary>
    /// <param name="port">チェックするポート番号</param>
    /// <returns>利用可能な場合true</returns>
    Task<bool> IsPortAvailableAsync(int port);

    /// <summary>
    /// アクティブなポート一覧を取得します
    /// </summary>
    /// <returns>アクティブポートのリスト</returns>
    Task<IReadOnlyList<int>> GetActivePortsAsync();

    /// <summary>
    /// 孤立プロセスをクリーンアップします
    /// </summary>
    Task CleanupOrphanedProcessesAsync();
}
