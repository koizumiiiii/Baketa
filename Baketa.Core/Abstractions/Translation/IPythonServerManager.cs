namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// Python翻訳サーバー情報インターフェース
/// Clean Architecture: Core層での抽象化
/// </summary>
public interface IPythonServerInfo
{
    /// <summary>ポート番号</summary>
    int Port { get; }
    
    /// <summary>言語ペア</summary>
    string LanguagePair { get; }
    
    /// <summary>サーバー開始時刻</summary>
    DateTime StartedAt { get; }
    
    /// <summary>サーバーが健全かどうか</summary>
    bool IsHealthy { get; }
    
    /// <summary>稼働時間</summary>
    TimeSpan Uptime { get; }
}

/// <summary>
/// Python翻訳サーバー管理インターフェース
/// Issue #147 Phase 5: ポート競合防止機構
/// </summary>
public interface IPythonServerManager : IDisposable
{
    /// <summary>
    /// 指定された言語ペアのサーバーを起動します
    /// </summary>
    /// <param name="languagePair">言語ペア（例: "ja-en", "en-ja"）</param>
    /// <returns>起動したサーバー情報</returns>
    Task<IPythonServerInfo> StartServerAsync(string languagePair);
    
    /// <summary>
    /// 指定されたポートのサーバーを停止します
    /// </summary>
    /// <param name="port">停止するサーバーのポート</param>
    Task StopServerAsync(int port);
    
    /// <summary>
    /// 指定された言語ペアのサーバーを停止します
    /// </summary>
    /// <param name="languagePair">停止するサーバーの言語ペア</param>
    Task StopServerAsync(string languagePair);
    
    /// <summary>
    /// アクティブなサーバー一覧を取得します
    /// </summary>
    /// <returns>アクティブサーバーのリスト</returns>
    Task<IReadOnlyList<IPythonServerInfo>> GetActiveServersAsync();
    
    /// <summary>
    /// 指定された言語ペアのサーバーを取得します
    /// </summary>
    /// <param name="languagePair">言語ペア</param>
    /// <returns>サーバーインスタンス（存在しない場合はnull）</returns>
    Task<IPythonServerInfo?> GetServerAsync(string languagePair);
    
    /// <summary>
    /// サーバーのヘルスチェックを実行します
    /// </summary>
    Task PerformHealthCheckAsync();
}