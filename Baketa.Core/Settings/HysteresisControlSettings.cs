using System;
using System.ComponentModel.DataAnnotations;

namespace Baketa.Core.Settings;

/// <summary>
/// Phase 3: ヒステリシス制御設定
/// 動的並列度調整の上下限しきい値と安定化パラメータ
/// </summary>
public sealed record HysteresisControlSettings
{
    /// <summary>GPU使用率上限しきい値（この値を超えると並列度を下げる）</summary>
    [Range(50.0, 95.0)]
    public double GpuUpperThresholdPercent { get; init; } = 80.0;

    /// <summary>GPU使用率下限しきい値（この値を下回ると並列度を上げる）</summary>
    [Range(10.0, 70.0)]
    public double GpuLowerThresholdPercent { get; init; } = 50.0;

    /// <summary>VRAM使用率上限しきい値</summary>
    [Range(50.0, 95.0)]
    public double VramUpperThresholdPercent { get; init; } = 75.0;

    /// <summary>VRAM使用率下限しきい値</summary>
    [Range(10.0, 70.0)]
    public double VramLowerThresholdPercent { get; init; } = 40.0;

    /// <summary>並列度調整の最小間隔（フラッピング防止）</summary>
    public TimeSpan MinAdjustmentInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>並列度の最小値</summary>
    [Range(1, 10)]
    public int MinParallelism { get; init; } = 1;

    /// <summary>並列度の最大値</summary>
    [Range(2, 16)]
    public int MaxParallelism { get; init; } = 8;

    /// <summary>並列度調整の段階サイズ</summary>
    [Range(1, 4)]
    public int AdjustmentStep { get; init; } = 1;

    /// <summary>安定性確認に必要な連続測定回数</summary>
    [Range(2, 10)]
    public int StabilityRequiredMeasurements { get; init; } = 3;

    /// <summary>緊急時の並列度制限（GPU温度 >= 90°C時）</summary>
    [Range(1, 4)]
    public int EmergencyParallelismLimit { get; init; } = 2;

    /// <summary>ゲーミングモード時の並列度上限</summary>
    [Range(1, 8)]
    public int GamingModeMaxParallelism { get; init; } = 4;

    /// <summary>予測制御の有効化フラグ</summary>
    public bool EnablePredictiveControl { get; init; } = true;

    /// <summary>予測制御の先読み時間</summary>
    public TimeSpan PredictiveLookaheadTime { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>設定の妥当性検証</summary>
    public bool IsValid()
    {
        return GpuLowerThresholdPercent < GpuUpperThresholdPercent &&
               VramLowerThresholdPercent < VramUpperThresholdPercent &&
               MinParallelism < MaxParallelism &&
               MinAdjustmentInterval >= TimeSpan.FromSeconds(5) &&
               StabilityRequiredMeasurements >= 2 &&
               EmergencyParallelismLimit <= MaxParallelism &&
               GamingModeMaxParallelism <= MaxParallelism;
    }

    /// <summary>デフォルト設定</summary>
    public static HysteresisControlSettings Default => new();

    /// <summary>保守的設定（安定性重視）</summary>
    public static HysteresisControlSettings Conservative => new()
    {
        GpuUpperThresholdPercent = 70.0,
        GpuLowerThresholdPercent = 40.0,
        VramUpperThresholdPercent = 65.0,
        VramLowerThresholdPercent = 30.0,
        MinAdjustmentInterval = TimeSpan.FromSeconds(15),
        MaxParallelism = 4,
        AdjustmentStep = 1,
        StabilityRequiredMeasurements = 5
    };

    /// <summary>積極的設定（パフォーマンス重視）</summary>
    public static HysteresisControlSettings Aggressive => new()
    {
        GpuUpperThresholdPercent = 90.0,
        GpuLowerThresholdPercent = 60.0,
        VramUpperThresholdPercent = 85.0,
        VramLowerThresholdPercent = 50.0,
        MinAdjustmentInterval = TimeSpan.FromSeconds(5),
        MaxParallelism = 12,
        AdjustmentStep = 2,
        StabilityRequiredMeasurements = 2
    };

    /// <summary>RTX 4070特化設定</summary>
    public static HysteresisControlSettings Rtx4070Optimized => new()
    {
        GpuUpperThresholdPercent = 85.0,
        GpuLowerThresholdPercent = 55.0,
        VramUpperThresholdPercent = 80.0, // 12GB VRAMを活用
        VramLowerThresholdPercent = 45.0,
        MinAdjustmentInterval = TimeSpan.FromSeconds(8),
        MaxParallelism = 10,
        AdjustmentStep = 2,
        StabilityRequiredMeasurements = 3,
        EmergencyParallelismLimit = 3,
        GamingModeMaxParallelism = 6
    };
}

/// <summary>
/// Phase 3: 予測制御設定
/// ゲーム負荷パターン学習と予測的リソース制御
/// </summary>
public sealed record PredictiveControlSettings
{
    /// <summary>ゲーム負荷パターン学習の有効化</summary>
    public bool EnableGameLoadLearning { get; init; } = true;

    /// <summary>負荷パターンデータの最大保持期間</summary>
    public TimeSpan LoadPatternRetentionPeriod { get; init; } = TimeSpan.FromDays(7);

    /// <summary>負荷パターン学習に必要な最小セッション数</summary>
    [Range(3, 50)]
    public int MinLearningSessionCount { get; init; } = 5;

    /// <summary>負荷ピーク検出の感度</summary>
    [Range(0.1, 0.9)]
    public double PeakDetectionSensitivity { get; init; } = 0.3;

    /// <summary>予測精度の最小要求値</summary>
    [Range(0.5, 0.95)]
    public double MinPredictionAccuracy { get; init; } = 0.7;

    /// <summary>動的クールダウンの基本倍率</summary>
    [Range(0.5, 3.0)]
    public double CooldownBaseMultiplier { get; init; } = 1.0;

    /// <summary>温度考慮によるクールダウン追加倍率</summary>
    [Range(0.0, 2.0)]
    public double TemperatureAdjustmentMultiplier { get; init; } = 0.5;

    /// <summary>VRAM圧迫時のクールダウン追加倍率</summary>
    [Range(0.0, 3.0)]
    public double VramPressureAdjustmentMultiplier { get; init; } = 1.0;

    /// <summary>ゲームプロセス検出の更新間隔</summary>
    public TimeSpan GameProcessUpdateInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>負荷パターンの平滑化ウィンドウサイズ</summary>
    [Range(5, 60)]
    public int LoadSmoothingWindowSize { get; init; } = 15;

    /// <summary>デフォルト設定</summary>
    public static PredictiveControlSettings Default => new();

    /// <summary>高精度設定</summary>
    public static PredictiveControlSettings HighPrecision => new()
    {
        MinLearningSessionCount = 10,
        PeakDetectionSensitivity = 0.2,
        MinPredictionAccuracy = 0.8,
        LoadSmoothingWindowSize = 30
    };
}
