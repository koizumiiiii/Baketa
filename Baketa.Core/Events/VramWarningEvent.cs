using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events;

/// <summary>
/// VRAM警告イベント
/// Issue #300: VRAM使用率が危険レベルに達した場合にUI通知をトリガー
/// </summary>
public sealed class VramWarningEvent : IEvent
{
    /// <inheritdoc />
    public Guid Id { get; }

    /// <inheritdoc />
    public DateTime Timestamp { get; }

    /// <inheritdoc />
    public string Name => "VramWarning";

    /// <inheritdoc />
    public string Category => "System";

    /// <summary>
    /// 警告レベル
    /// </summary>
    public VramWarningLevel Level { get; }

    /// <summary>
    /// 現在のVRAM使用率 (%)
    /// </summary>
    public double VramUsagePercent { get; }

    /// <summary>
    /// 使用中VRAM (MB)
    /// </summary>
    public long UsedVramMB { get; }

    /// <summary>
    /// 合計VRAM (MB)
    /// </summary>
    public long TotalVramMB { get; }

    /// <summary>
    /// ユーザー向けメッセージ
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public VramWarningEvent(
        VramWarningLevel level,
        double vramUsagePercent,
        long usedVramMB,
        long totalVramMB,
        string message)
    {
        Id = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
        Level = level;
        VramUsagePercent = vramUsagePercent;
        UsedVramMB = usedVramMB;
        TotalVramMB = totalVramMB;
        Message = message ?? $"VRAM usage: {vramUsagePercent:F1}%";
    }

    /// <summary>
    /// Critical状態イベント作成 (75-90%)
    /// </summary>
    public static VramWarningEvent CreateCritical(double usagePercent, long usedMB, long totalMB)
    {
        return new VramWarningEvent(
            VramWarningLevel.Critical,
            usagePercent,
            usedMB,
            totalMB,
            $"GPUメモリ使用率が高くなっています ({usagePercent:F0}%)。ゲームの設定を下げるか、他のアプリを閉じてください。");
    }

    /// <summary>
    /// Emergency状態イベント作成 (> 90%)
    /// </summary>
    public static VramWarningEvent CreateEmergency(double usagePercent, long usedMB, long totalMB)
    {
        return new VramWarningEvent(
            VramWarningLevel.Emergency,
            usagePercent,
            usedMB,
            totalMB,
            $"GPUメモリが不足しています ({usagePercent:F0}%)。翻訳が正常に動作しない可能性があります。");
    }

    /// <summary>
    /// 回復状態イベント作成 (< 75%)
    /// </summary>
    public static VramWarningEvent CreateRecovered(double usagePercent, long usedMB, long totalMB)
    {
        return new VramWarningEvent(
            VramWarningLevel.Normal,
            usagePercent,
            usedMB,
            totalMB,
            string.Empty);
    }
}

/// <summary>
/// VRAM警告レベル
/// </summary>
public enum VramWarningLevel
{
    /// <summary>正常 (< 75%)</summary>
    Normal = 0,
    /// <summary>危険 (75-90%)</summary>
    Critical = 1,
    /// <summary>緊急 (> 90%)</summary>
    Emergency = 2
}
