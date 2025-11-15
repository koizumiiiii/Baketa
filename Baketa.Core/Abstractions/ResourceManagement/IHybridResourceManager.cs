using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Common;
using Baketa.Core.Abstractions.Monitoring;

namespace Baketa.Core.Abstractions.ResourceManagement;

/// <summary>
/// Phase 3: 高度なハイブリッドリソース管理インターフェース
/// GPU/VRAM監視、ヒステリシス制御、動的並列度調整、予測的クールダウンを統合
/// </summary>
public interface IHybridResourceManager : IInitializable, IDisposable
{
    /// <summary>リアルタイムシステムメトリクス取得</summary>
    Task<ResourceMetrics> GetSystemMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>GPU/VRAM統合監視メトリクス取得</summary>
    Task<GpuVramMetrics> GetGpuVramMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>現在のリソース状態に基づく最適並列度計算</summary>
    Task<int> CalculateOptimalParallelismAsync(SystemLoad systemLoad, CancellationToken cancellationToken = default);

    /// <summary>ヒステリシス制御による安定化された並列度調整</summary>
    Task AdjustParallelismWithHysteresisAsync(SystemLoad systemLoad, CancellationToken cancellationToken = default);

    /// <summary>予測的動的クールダウン計算（ゲーム負荷パターン学習統合）</summary>
    Task<TimeSpan> CalculatePredictiveCooldownAsync(GameLoadPattern gamePattern, CancellationToken cancellationToken = default);

    /// <summary>ゲーム別プロファイル適用</summary>
    Task ApplyGameProfileAsync(string gameProcessName, CancellationToken cancellationToken = default);

    /// <summary>A/Bテスト設定の動的切り替え</summary>
    Task<string> GetActiveConfigurationVariantAsync(CancellationToken cancellationToken = default);

    /// <summary>リソース競合検出と自動制御</summary>
    Task<ResourceConflictResult> DetectAndResolveConflictsAsync(CancellationToken cancellationToken = default);

    /// <summary>リアルタイムリソース状態変更イベント</summary>
    event EventHandler<ResourceStateChangedEventArgs> ResourceStateChanged;

    /// <summary>ヒステリシス制御状態変更イベント</summary>
    event EventHandler<HysteresisStateChangedEventArgs> HysteresisStateChanged;

    /// <summary>予測制御トリガーイベント</summary>
    event EventHandler<PredictiveControlTriggeredEventArgs> PredictiveControlTriggered;
}

/// <summary>
/// GPU/VRAM統合監視メトリクス
/// NVML + Windows Performance API統合データ
/// </summary>
public sealed record GpuVramMetrics(
    double GpuUtilizationPercent,
    double VramUsagePercent,
    long VramUsedMB,
    long VramTotalMB,
    double GpuTemperatureCelsius,
    double PowerUsageWatts,
    int GpuClockMhz,
    int MemoryClockMhz,
    bool IsOptimalForProcessing,
    DateTime MeasuredAt
)
{
    /// <summary>VRAM圧迫度レベル（5段階）</summary>
    public VramPressureLevel GetVramPressureLevel() => VramUsagePercent switch
    {
        < 50 => VramPressureLevel.Low,
        < 70 => VramPressureLevel.Moderate,
        < 85 => VramPressureLevel.High,
        < 95 => VramPressureLevel.Critical,
        _ => VramPressureLevel.Emergency
    };

    /// <summary>GPU温度状態</summary>
    public GpuTemperatureState GetTemperatureState() => GpuTemperatureCelsius switch
    {
        < 60 => GpuTemperatureState.Cool,
        < 75 => GpuTemperatureState.Normal,
        < 85 => GpuTemperatureState.Warm,
        < 95 => GpuTemperatureState.Hot,
        _ => GpuTemperatureState.Critical
    };
}

/// <summary>VRAM圧迫度レベル</summary>
public enum VramPressureLevel
{
    Low,        // < 50%: 余裕あり
    Moderate,   // 50-70%: 通常範囲
    High,       // 70-85%: 注意
    Critical,   // 85-95%: 危険
    Emergency   // >= 95%: 緊急
}

/// <summary>GPU温度状態</summary>
public enum GpuTemperatureState
{
    Cool,       // < 60°C
    Normal,     // 60-75°C
    Warm,       // 75-85°C
    Hot,        // 85-95°C
    Critical    // >= 95°C
}

/// <summary>
/// システム負荷パターン
/// リソース監視データの統合表現
/// </summary>
public sealed record SystemLoad(
    double CpuUsagePercent,
    double MemoryUsagePercent,
    double GpuUsagePercent,
    double VramUsagePercent,
    int ActiveProcessCount,
    bool IsGamingActive,
    DateTime MeasuredAt
)
{
    /// <summary>総合負荷レベル計算</summary>
    public SystemLoadLevel GetLoadLevel()
    {
        var avgLoad = (CpuUsagePercent + MemoryUsagePercent + GpuUsagePercent) / 3;
        return avgLoad switch
        {
            < 30 => SystemLoadLevel.Low,
            < 60 => SystemLoadLevel.Medium,
            < 85 => SystemLoadLevel.High,
            _ => SystemLoadLevel.Critical
        };
    }
}

/// <summary>システム負荷レベル</summary>
public enum SystemLoadLevel
{
    Low,        // < 30%
    Medium,     // 30-60%
    High,       // 60-85%
    Critical    // >= 85%
}

/// <summary>
/// ゲーム負荷パターン学習データ
/// 特定ゲームの負荷パターンを学習・予測
/// </summary>
public sealed record GameLoadPattern(
    string GameProcessName,
    Dictionary<TimeSpan, double> LoadProfile,    // 時間経過 → 負荷率
    double AverageLoad,
    double PeakLoad,
    TimeSpan PredictedPeakTime,
    int LearningSessionCount
)
{
    /// <summary>指定時刻の予測負荷を取得</summary>
    public double GetPredictedLoad(TimeSpan gameTime)
    {
        // 最も近い時刻の負荷データを線形補間
        var nearestPoints = LoadProfile
            .Where(kv => Math.Abs((kv.Key - gameTime).TotalSeconds) < 300) // 5分以内
            .OrderBy(kv => Math.Abs((kv.Key - gameTime).TotalSeconds))
            .Take(2)
            .ToArray();

        return nearestPoints.Length switch
        {
            0 => AverageLoad, // データがない場合は平均負荷
            1 => nearestPoints[0].Value, // 1つのみの場合はそのまま
            _ => InterpolateLoad(nearestPoints[0], nearestPoints[1], gameTime) // 線形補間
        };
    }

    private static double InterpolateLoad(
        KeyValuePair<TimeSpan, double> point1,
        KeyValuePair<TimeSpan, double> point2,
        TimeSpan targetTime)
    {
        var timeDiff = (point2.Key - point1.Key).TotalSeconds;
        if (Math.Abs(timeDiff) < 0.001) return point1.Value;

        var ratio = (targetTime - point1.Key).TotalSeconds / timeDiff;
        return point1.Value + (point2.Value - point1.Value) * ratio;
    }
}

/// <summary>
/// リソース競合検出結果
/// 他アプリケーションとの競合状況分析
/// </summary>
public sealed record ResourceConflictResult(
    bool HasConflict,
    List<ConflictingProcess> ConflictingProcesses,
    RecommendedAction RecommendedAction,
    int RecommendedParallelism,
    TimeSpan RecommendedCooldown
);

/// <summary>競合プロセス情報</summary>
public sealed record ConflictingProcess(
    string ProcessName,
    int ProcessId,
    double CpuUsage,
    double MemoryUsageMB,
    double GpuUsage,
    ConflictSeverity Severity
);

/// <summary>競合重要度</summary>
public enum ConflictSeverity
{
    Low,        // 軽微な競合
    Moderate,   // 中程度の競合
    High,       // 重大な競合
    Critical    // 致命的な競合
}

/// <summary>推奨アクション</summary>
public enum RecommendedAction
{
    ScaleUp,            // リソース使用量増加
    Maintain,           // 現状維持
    ScaleDown,          // リソース使用量削減
    FallbackToCpu,      // CPU処理にフォールバック
    EmergencyFallback,  // 緊急フォールバック
    PauseProcessing     // 処理一時停止
}

/// <summary>リソース状態変更イベント引数</summary>
public sealed class ResourceStateChangedEventArgs : EventArgs
{
    public ResourceMetrics PreviousMetrics { get; init; } = null!;
    public ResourceMetrics CurrentMetrics { get; init; } = null!;
    public SystemLoad SystemLoad { get; init; } = null!;
    public RecommendedAction RecommendedAction { get; init; }
    public DateTime ChangedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>ヒステリシス状態変更イベント引数</summary>
public sealed class HysteresisStateChangedEventArgs : EventArgs
{
    public int PreviousParallelism { get; init; }
    public int NewParallelism { get; init; }
    public bool IsIncreasing { get; init; }
    public double CurrentLoad { get; init; }
    public double UpperThreshold { get; init; }
    public double LowerThreshold { get; init; }
    public DateTime ChangedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>予測制御トリガーイベント引数</summary>
public sealed class PredictiveControlTriggeredEventArgs : EventArgs
{
    public GameLoadPattern GamePattern { get; init; } = null!;
    public TimeSpan PredictedPeakTime { get; init; }
    public double PredictedPeakLoad { get; init; }
    public TimeSpan RecommendedCooldown { get; init; }
    public RecommendedAction PreemptiveAction { get; init; }
    public DateTime TriggeredAt { get; init; } = DateTime.UtcNow;
}
