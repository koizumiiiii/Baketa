using System.Drawing;
using Baketa.Core.Abstractions.OCR;

namespace Baketa.Core.Abstractions.Performance;

/// <summary>
/// パフォーマンス最適化オーケストレーター
/// GPU加速 + TDR対策の統合管理
/// Issue #143 Week 3 Phase 2: 統合システム制御
/// </summary>
public interface IPerformanceOrchestrator
{
    /// <summary>
    /// 統合パフォーマンス最適化でOCR実行
    /// </summary>
    /// <param name="imageData">画像データ</param>
    /// <param name="options">最適化オプション</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>最適化されたOCR結果</returns>
    Task<OptimizedOcrResult> ExecuteOptimizedOcrAsync(
        byte[] imageData, 
        PerformanceOptimizationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// リアルタイムパフォーマンスメトリクス取得
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>統合パフォーマンスメトリクス</returns>
    Task<IntegratedPerformanceMetrics> GetPerformanceMetricsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 適応的最適化設定の更新
    /// </summary>
    /// <param name="metrics">現在のメトリクス</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>最適化調整結果</returns>
    Task<OptimizationAdjustmentResult> AdaptOptimizationAsync(
        IntegratedPerformanceMetrics metrics,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// システム状態の健全性チェック
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>システム健全性レポート</returns>
    Task<SystemHealthReport> CheckSystemHealthAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// パフォーマンス最適化オプション
/// </summary>
public record PerformanceOptimizationOptions
{
    /// <summary>
    /// GPU加速を優先するか
    /// </summary>
    public bool PreferGpuAcceleration { get; init; } = true;

    // [ROI_DELETION] スティッキーROI機能削除 - レガシー機能除去
    // public bool UseStickyRoi { get; init; } = true;

    /// <summary>
    /// TDR保護を有効にするか
    /// </summary>
    public bool EnableTdrProtection { get; init; } = true;

    /// <summary>
    /// パフォーマンス優先度
    /// </summary>
    public PerformancePriority Priority { get; init; } = PerformancePriority.Balanced;

    /// <summary>
    /// 品質vs速度のトレードオフ設定
    /// </summary>
    public QualitySpeedTradeoff QualitySettings { get; init; } = QualitySpeedTradeoff.Balanced;

    /// <summary>
    /// 最大処理時間（タイムアウト）
    /// </summary>
    public TimeSpan MaxProcessingTime { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// 最適化されたOCR結果
/// </summary>
public class OptimizedOcrResult
{
    /// <summary>
    /// 検出されたテキスト一覧
    /// </summary>
    public IReadOnlyList<DetectedText> DetectedTexts { get; init; } = [];

    /// <summary>
    /// 総処理時間
    /// </summary>
    public TimeSpan TotalProcessingTime { get; init; }

    /// <summary>
    /// 使用された最適化手法
    /// </summary>
    public OptimizationTechnique UsedTechnique { get; init; }

    /// <summary>
    /// パフォーマンス改善率
    /// </summary>
    public double PerformanceImprovement { get; init; }

    /// <summary>
    /// 品質スコア
    /// </summary>
    public double QualityScore { get; init; }

    /// <summary>
    /// 処理成功フラグ
    /// </summary>
    public bool IsSuccessful { get; init; }

    /// <summary>
    /// 詳細メタデータ
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
}

/// <summary>
/// 統合パフォーマンスメトリクス
/// </summary>
public class IntegratedPerformanceMetrics
{
    /// <summary>
    /// GPU使用率
    /// </summary>
    public double GpuUtilization { get; init; }

    // [ROI_DELETION] ROI効率率削除 - レガシー機能除去
    // public double RoiEfficiency { get; init; }

    /// <summary>
    /// 平均処理時間
    /// </summary>
    public TimeSpan AverageProcessingTime { get; init; }

    /// <summary>
    /// スループット（件/秒）
    /// </summary>
    public double Throughput { get; init; }

    /// <summary>
    /// メモリ使用量（MB）
    /// </summary>
    public long MemoryUsageMB { get; init; }

    /// <summary>
    /// TDR発生回数
    /// </summary>
    public int TdrOccurrences { get; init; }

    /// <summary>
    /// 品質vs速度バランス
    /// </summary>
    public double QualitySpeedBalance { get; init; }

    /// <summary>
    /// システム安定性スコア
    /// </summary>
    public double StabilityScore { get; init; }

    /// <summary>
    /// 測定時刻
    /// </summary>
    public DateTime MeasuredAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 最適化調整結果
/// </summary>
public class OptimizationAdjustmentResult
{
    /// <summary>
    /// 調整が実行されたか
    /// </summary>
    public bool AdjustmentExecuted { get; init; }

    /// <summary>
    /// 実行された調整内容
    /// </summary>
    public IReadOnlyList<string> ExecutedAdjustments { get; init; } = [];

    /// <summary>
    /// 予想される改善効果
    /// </summary>
    public double ExpectedImprovement { get; init; }

    /// <summary>
    /// 新しい設定
    /// </summary>
    public PerformanceOptimizationOptions NewSettings { get; init; } = new();
}

/// <summary>
/// システム健全性レポート
/// </summary>
public class SystemHealthReport
{
    /// <summary>
    /// 全体的な健全性スコア（0-1）
    /// </summary>
    public double OverallHealthScore { get; init; }

    /// <summary>
    /// GPU健全性
    /// </summary>
    public ComponentHealth GpuHealth { get; init; } = new();

    // [ROI_DELETION] ROIシステム健全性削除 - レガシー機能除去
    // public ComponentHealth RoiSystemHealth { get; init; } = new();

    /// <summary>
    /// メモリ健全性
    /// </summary>
    public ComponentHealth MemoryHealth { get; init; } = new();

    /// <summary>
    /// 検出された問題
    /// </summary>
    public IReadOnlyList<HealthIssue> DetectedIssues { get; init; } = [];

    /// <summary>
    /// 推奨アクション
    /// </summary>
    public IReadOnlyList<string> RecommendedActions { get; init; } = [];

    /// <summary>
    /// レポート生成時刻
    /// </summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// コンポーネント健全性
/// </summary>
public class ComponentHealth
{
    /// <summary>
    /// 健全性スコア（0-1）
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// 状態
    /// </summary>
    public HealthStatus Status { get; init; }

    /// <summary>
    /// 詳細メッセージ
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 最終チェック時刻
    /// </summary>
    public DateTime LastChecked { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 健全性の問題
/// </summary>
public class HealthIssue
{
    /// <summary>
    /// 問題の重要度
    /// </summary>
    public IssueSeverity Severity { get; init; }

    /// <summary>
    /// 問題の説明
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 影響を受けるコンポーネント
    /// </summary>
    public string Component { get; init; } = string.Empty;

    /// <summary>
    /// 推奨解決策
    /// </summary>
    public string RecommendedSolution { get; init; } = string.Empty;
}

/// <summary>
/// パフォーマンス優先度
/// </summary>
public enum PerformancePriority
{
    /// <summary>
    /// 品質優先
    /// </summary>
    Quality,
    
    /// <summary>
    /// バランス
    /// </summary>
    Balanced,
    
    /// <summary>
    /// 速度優先
    /// </summary>
    Speed
}

/// <summary>
/// 品質vs速度のトレードオフ
/// </summary>
public enum QualitySpeedTradeoff
{
    /// <summary>
    /// 最高品質
    /// </summary>
    MaxQuality,
    
    /// <summary>
    /// 高品質
    /// </summary>
    HighQuality,
    
    /// <summary>
    /// バランス
    /// </summary>
    Balanced,
    
    /// <summary>
    /// 高速
    /// </summary>
    HighSpeed,
    
    /// <summary>
    /// 最高速
    /// </summary>
    MaxSpeed
}


/// <summary>
/// 健全性状態
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// 正常
    /// </summary>
    Healthy,
    
    /// <summary>
    /// 警告
    /// </summary>
    Warning,
    
    /// <summary>
    /// エラー
    /// </summary>
    Error,
    
    /// <summary>
    /// クリティカル
    /// </summary>
    Critical
}

/// <summary>
/// 問題の重要度
/// </summary>
public enum IssueSeverity
{
    /// <summary>
    /// 情報
    /// </summary>
    Info,
    
    /// <summary>
    /// 警告
    /// </summary>
    Warning,
    
    /// <summary>
    /// エラー
    /// </summary>
    Error,
    
    /// <summary>
    /// クリティカル
    /// </summary>
    Critical
}

