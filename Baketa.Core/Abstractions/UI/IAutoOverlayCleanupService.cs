using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.UI;

/// <summary>
/// オーバーレイ自動削除サービスのインターフェース
/// UltraThink Phase 1: オーバーレイ自動消去システム
/// 
/// TextDisappearanceEventを受信してオーバーレイを自動削除する責任を持つ
/// Circuit Breaker パターンによる誤検知防止機能付き
/// </summary>
public interface IAutoOverlayCleanupService : IDisposable
{
    /// <summary>
    /// サービスを初期化
    /// EventAggregatorへの購読を開始
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 指定領域のオーバーレイを手動で削除
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <param name="regions">削除対象領域</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>削除されたオーバーレイ数</returns>
    Task<int> CleanupOverlaysInRegionAsync(
        IntPtr windowHandle, 
        IReadOnlyList<Rectangle> regions, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// サービスの統計情報を取得
    /// </summary>
    AutoOverlayCleanupStatistics GetStatistics();
    
    /// <summary>
    /// Circuit Breaker設定を更新
    /// </summary>
    /// <param name="minConfidenceScore">最小信頼度スコア (0.0-1.0)</param>
    /// <param name="maxCleanupRate">最大削除レート（毎秒）</param>
    void UpdateCircuitBreakerSettings(float minConfidenceScore, int maxCleanupRate);
}

/// <summary>
/// オーバーレイ自動削除の統計情報
/// </summary>
public sealed class AutoOverlayCleanupStatistics
{
    /// <summary>
    /// 処理したイベント総数
    /// </summary>
    public int TotalEventsProcessed { get; init; }
    
    /// <summary>
    /// 実際に削除したオーバーレイ数
    /// </summary>
    public int OverlaysCleanedUp { get; init; }
    
    /// <summary>
    /// 信頼度不足で却下した削除要求数
    /// </summary>
    public int RejectedByConfidence { get; init; }
    
    /// <summary>
    /// レート制限により却下した削除要求数
    /// </summary>
    public int RejectedByRateLimit { get; init; }
    
    /// <summary>
    /// 平均処理時間（ミリ秒）
    /// </summary>
    public double AverageProcessingTimeMs { get; init; }
    
    /// <summary>
    /// 最後のイベント処理時刻
    /// </summary>
    public DateTime? LastEventProcessedAt { get; init; }
    
    /// <summary>
    /// エラー発生数
    /// </summary>
    public int ErrorCount { get; init; }
}