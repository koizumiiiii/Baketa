using System;
using System.Collections.Generic;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// 統合リアルタイム更新システムによるシステム状態更新イベント
/// Gemini改善提案: 統合システム状態の一元管理
/// </summary>
public class SystemStateUpdatedEvent : EventBase
{
    /// <summary>
    /// イベント名
    /// </summary>
    public override string Name => "SystemStateUpdated";
    
    /// <summary>
    /// イベントカテゴリ
    /// </summary>
    public override string Category => "System";
    
    /// <summary>
    /// イベント発生時刻（EventBaseのTimestampを隠す）
    /// </summary>
    public new DateTimeOffset Timestamp { get; }
    
    /// <summary>
    /// システムリソース状態
    /// </summary>
    public SystemResourceState? ResourceState { get; }
    
    /// <summary>
    /// ゲーム状態情報
    /// </summary>
    public GameInfo? GameState { get; }
    
    /// <summary>
    /// サーバーヘルス状態
    /// </summary>
    public string? ServerHealth { get; }
    
    /// <summary>
    /// 実行されたタスク結果
    /// </summary>
    public IReadOnlyDictionary<string, object> TaskResults { get; }
    
    /// <summary>
    /// 次回実行間隔（アダプティブ調整結果）
    /// </summary>
    public TimeSpan NextExecutionInterval { get; }
    
    /// <summary>
    /// パフォーマンス最適化が適用されたかどうか
    /// </summary>
    public bool OptimizationApplied { get; }

    public SystemStateUpdatedEvent(
        DateTimeOffset timestamp,
        SystemResourceState? resourceState = null,
        GameInfo? gameState = null,
        string? serverHealth = null,
        IReadOnlyDictionary<string, object>? taskResults = null,
        TimeSpan nextExecutionInterval = default,
        bool optimizationApplied = false)
    {
        Timestamp = timestamp;
        ResourceState = resourceState;
        GameState = gameState;
        ServerHealth = serverHealth;
        TaskResults = taskResults ?? new Dictionary<string, object>();
        NextExecutionInterval = nextExecutionInterval == default 
            ? TimeSpan.FromSeconds(5) 
            : nextExecutionInterval;
        OptimizationApplied = optimizationApplied;
    }

    /// <summary>
    /// イベントの詳細文字列表現
    /// </summary>
    public override string ToString()
    {
        var details = new List<string>();
        
        if (ResourceState != null)
            details.Add($"CPU:{ResourceState.CpuUsagePercent:F1}%");
            
        if (GameState != null)
            details.Add($"Game:{GameState.ProcessName}");
            
        if (!string.IsNullOrEmpty(ServerHealth))
            details.Add($"Server:{ServerHealth}");
            
        details.Add($"Tasks:{TaskResults.Count}");
        details.Add($"Next:{NextExecutionInterval.TotalSeconds}s");
        
        return $"SystemStateUpdated[{string.Join(", ", details)}]";
    }
}