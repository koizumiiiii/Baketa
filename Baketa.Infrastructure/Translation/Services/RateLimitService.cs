using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// レート制限管理サービス
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// リクエストが許可されているかチェック
    /// </summary>
    /// <param name="engineName">エンジン名</param>
    /// <returns>許可されている場合はtrue</returns>
    Task<bool> IsAllowedAsync(string engineName);
    
    /// <summary>
    /// リクエスト実行を記録
    /// </summary>
    /// <param name="engineName">エンジン名</param>
    /// <param name="tokenCount">使用トークン数</param>
    /// <returns>記録完了を示すタスク</returns>
    Task RecordUsageAsync(string engineName, int tokenCount);
    
    /// <summary>
    /// 次のリクエスト可能時刻を取得
    /// </summary>
    /// <param name="engineName">エンジン名</param>
    /// <returns>次回可能時刻</returns>
    Task<DateTimeOffset> GetNextAvailableTimeAsync(string engineName);
}

/// <summary>
/// レート制限管理サービスの実装
/// </summary>
public class RateLimitService : IRateLimitService
{
    private readonly Dictionary<string, Queue<DateTimeOffset>> _requestHistory = [];
    private readonly Dictionary<string, int> _rateLimits = [];
    private readonly object _lock = new();
    
    /// <summary>
    /// エンジンのレート制限を設定
    /// </summary>
    /// <param name="engineName">エンジン名</param>
    /// <param name="limitPerMinute">1分あたりの制限数</param>
    public void SetRateLimit(string engineName, int limitPerMinute)
    {
        lock (_lock)
        {
            _rateLimits[engineName] = limitPerMinute;
            _requestHistory.TryAdd(engineName, new Queue<DateTimeOffset>());
        }
    }
    
    /// <inheritdoc/>
    public Task<bool> IsAllowedAsync(string engineName)
    {
        lock (_lock)
        {
            if (!_rateLimits.TryGetValue(engineName, out int limit) || 
                !_requestHistory.TryGetValue(engineName, out var history))
            {
                return Task.FromResult(true); // 制限未設定の場合は許可
            }
            
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
            
            // 1分以上前のリクエストを削除
            while (history.Count > 0 && history.Peek() < cutoff)
            {
                history.Dequeue();
            }
            
            return Task.FromResult(history.Count < limit);
        }
    }
    
    /// <inheritdoc/>
    public Task RecordUsageAsync(string engineName, int tokenCount)
    {
        lock (_lock)
        {
            if (_requestHistory.TryGetValue(engineName, out var history))
            {
                history.Enqueue(DateTimeOffset.UtcNow);
            }
        }
        
        return Task.CompletedTask;
    }
    
    /// <inheritdoc/>
    public Task<DateTimeOffset> GetNextAvailableTimeAsync(string engineName)
    {
        lock (_lock)
        {
            if (!_rateLimits.TryGetValue(engineName, out int limit) || 
                !_requestHistory.TryGetValue(engineName, out var history))
            {
                return Task.FromResult(DateTimeOffset.UtcNow); // 制限未設定の場合は即座に可能
            }
            
            if (history.Count < limit)
            {
                return Task.FromResult(DateTimeOffset.UtcNow); // 制限内の場合は即座に可能
            }
            
            var oldestRequest = history.Peek();
            var nextAvailable = oldestRequest.AddMinutes(1);
            
            return Task.FromResult(nextAvailable);
        }
    }
}