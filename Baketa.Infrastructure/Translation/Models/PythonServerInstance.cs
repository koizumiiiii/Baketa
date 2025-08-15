using System.Diagnostics;
using Baketa.Core.Abstractions.Translation;

namespace Baketa.Infrastructure.Translation.Models;

/// <summary>
/// Python翻訳サーバーインスタンス
/// Issue #147 Phase 5: C# 12最適化（record + primary constructor）
/// </summary>
public record PythonServerInstance(
    int Port,
    string LanguagePair,
    Process Process) : IPythonServerInfo, IAsyncDisposable
{
    /// <summary>
    /// サーバー開始時刻
    /// </summary>
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// サーバーステータス
    /// </summary>
    public ServerStatus Status { get; private set; } = ServerStatus.Starting;
    
    /// <summary>
    /// 最後のヘルスチェック時刻
    /// </summary>
    public DateTime? LastHealthCheck { get; set; }
    
    /// <summary>
    /// ヘルスチェック成功回数
    /// </summary>
    public int HealthCheckSuccessCount { get; set; }
    
    /// <summary>
    /// ヘルスチェック失敗回数
    /// </summary>
    public int HealthCheckFailureCount { get; set; }
    
    /// <summary>
    /// サーバーが健全かどうか
    /// </summary>
    public bool IsHealthy => Status == ServerStatus.Running && 
                           !Process.HasExited && 
                           HealthCheckFailureCount <= 3;
    
    /// <summary>
    /// 稼働時間
    /// </summary>
    public TimeSpan Uptime => DateTime.UtcNow - StartedAt;
    
    /// <summary>
    /// サーバーステータスを更新
    /// </summary>
    /// <param name="newStatus">新しいステータス</param>
    public void UpdateStatus(ServerStatus newStatus)
    {
        Status = newStatus;
    }
    
    /// <summary>
    /// ヘルスチェック結果を記録
    /// </summary>
    /// <param name="success">ヘルスチェック成功フラグ</param>
    public void RecordHealthCheck(bool success)
    {
        LastHealthCheck = DateTime.UtcNow;
        
        if (success)
        {
            HealthCheckSuccessCount++;
            HealthCheckFailureCount = 0; // 成功時は失敗カウントリセット
        }
        else
        {
            HealthCheckFailureCount++;
        }
    }
    
    /// <summary>
    /// サーバーインスタンスの非同期破棄
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        UpdateStatus(ServerStatus.Stopping);
        
        try
        {
            if (!Process.HasExited)
            {
                // 正常終了を試行
                try
                {
                    Process.Kill();
                    await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    // プロセスが既に終了している場合
                }
                catch (OperationCanceledException)
                {
                    // タイムアウト時は強制終了
                    try
                    {
                        Process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                        // プロセスが既に終了している場合
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"プロセス終了エラー Port={Port}, LanguagePair={LanguagePair}: {ex.Message}");
        }
        finally
        {
            UpdateStatus(ServerStatus.Stopped);
            Process.Dispose();
        }
    }
    
    /// <summary>
    /// サーバー情報の文字列表現
    /// </summary>
    public override string ToString()
    {
        return $"PythonServer[{LanguagePair}] Port={Port}, Status={Status}, Uptime={Uptime:hh\\:mm\\:ss}, " +
               $"Health={HealthCheckSuccessCount}✅/{HealthCheckFailureCount}❌";
    }
}