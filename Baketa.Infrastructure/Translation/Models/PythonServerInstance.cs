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
    /// 🔧 [GEMINI_FIX] 段階的プロセス終了実装 - データ損失・リソースリーク防止
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        UpdateStatus(ServerStatus.Stopping);

        try
        {
            if (!Process.HasExited)
            {
                Console.WriteLine($"🛑 [GRACEFUL_SHUTDOWN] プロセス終了開始: PID={Process.Id}, Port={Port}");

                // 🔧 [GEMINI_RECOMMENDED] Phase 1: 自主終了を待機（グレースフルシャットダウン）
                // Pythonプロセスは通常ウィンドウを持たないため、まず自主終了の機会を与える
                Console.WriteLine($"⏳ [GRACEFUL_SHUTDOWN] Phase 1: 自主終了待機開始（5秒）");
                try
                {
                    using var cts1 = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await Process.WaitForExitAsync(cts1.Token).ConfigureAwait(false);

                    Console.WriteLine($"✅ [GRACEFUL_SHUTDOWN] プロセス自主終了成功: Port={Port}");
                    return; // 自主終了成功
                }
                catch (OperationCanceledException)
                {
                    // 5秒経過してもプロセスが終了しない → Phase 2へ
                    Console.WriteLine($"⚠️ [GRACEFUL_SHUTDOWN] Phase 1タイムアウト（5秒経過） - Phase 2へ");
                }
                catch (InvalidOperationException)
                {
                    // プロセスが既に終了
                    Console.WriteLine($"✅ [GRACEFUL_SHUTDOWN] プロセス既に終了: Port={Port}");
                    return;
                }

                // 🔧 [GEMINI_RECOMMENDED] Phase 2: Process.Kill()で終了シグナル送信
                if (!Process.HasExited)
                {
                    Console.WriteLine($"🔥 [GRACEFUL_SHUTDOWN] Phase 2: Process.Kill()実行");
                    try
                    {
                        Process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                        // プロセスが既に終了している場合
                        Console.WriteLine($"✅ [GRACEFUL_SHUTDOWN] プロセス既に終了（Kill前）: Port={Port}");
                        return;
                    }

                    // Kill()後、プロセスが完全に終了するまで待機（3秒）
                    Console.WriteLine($"⏳ [GRACEFUL_SHUTDOWN] Phase 2: Kill()後の終了待機（3秒）");
                    try
                    {
                        using var cts2 = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
                        await Process.WaitForExitAsync(cts2.Token).ConfigureAwait(false);

                        Console.WriteLine($"✅ [GRACEFUL_SHUTDOWN] Kill()後にプロセス終了成功: Port={Port}");
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine($"❌ [GRACEFUL_SHUTDOWN] Kill()後も3秒経過してプロセス未終了: PID={Process.Id}");
                        // ここでもう一度Kill()は実行しない（既にKill済み）
                        // OSがプロセスを終了させるのを待つしかない
                    }
                    catch (InvalidOperationException)
                    {
                        Console.WriteLine($"✅ [GRACEFUL_SHUTDOWN] プロセス終了完了: Port={Port}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"✅ [GRACEFUL_SHUTDOWN] プロセス既に終了状態: Port={Port}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [GRACEFUL_SHUTDOWN] プロセス終了エラー Port={Port}, LanguagePair={LanguagePair}: {ex.GetType().Name} - {ex.Message}");
        }
        finally
        {
            UpdateStatus(ServerStatus.Stopped);

            try
            {
                Process.Dispose();
                Console.WriteLine($"✅ [GRACEFUL_SHUTDOWN] Processリソース破棄完了: Port={Port}");
            }
            catch (Exception disposeEx)
            {
                Console.WriteLine($"⚠️ [GRACEFUL_SHUTDOWN] Process.Dispose()エラー: {disposeEx.Message}");
            }
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