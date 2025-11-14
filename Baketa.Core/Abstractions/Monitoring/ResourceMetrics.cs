namespace Baketa.Core.Abstractions.Monitoring;

/// <summary>
/// システムリソースメトリクス情報
/// CPU、メモリ、GPU使用率などのシステムリソース状況を表す不変データ構造
/// </summary>
/// <param name="Timestamp">メトリクス取得時刻 (UTC)</param>
/// <param name="CpuUsagePercent">CPU使用率 (0.0-100.0%)</param>
/// <param name="MemoryUsagePercent">メモリ使用率 (0.0-100.0%)</param>
/// <param name="AvailableMemoryMB">利用可能メモリ (MB)</param>
/// <param name="TotalMemoryMB">総メモリ容量 (MB)</param>
/// <param name="GpuUsagePercent">GPU使用率 (0.0-100.0%, 取得不可の場合null)</param>
/// <param name="GpuMemoryUsageMB">GPU専用メモリ使用量 (MB, 取得不可の場合null)</param>
/// <param name="GpuTemperature">GPU温度 (℃, 取得不可の場合null)</param>
/// <param name="NetworkBandwidthUsagePercent">ネットワーク帯域使用率 (0.0-100.0%, 取得不可の場合null)</param>
/// <param name="DiskUsagePercent">ディスクI/O使用率 (0.0-100.0%, 取得不可の場合null)</param>
/// <param name="ProcessCount">実行中プロセス数</param>
/// <param name="ThreadCount">実行中スレッド数</param>
public sealed record ResourceMetrics(
    DateTime Timestamp,
    double CpuUsagePercent,
    double MemoryUsagePercent,
    long AvailableMemoryMB,
    long TotalMemoryMB,
    double? GpuUsagePercent = null,
    long? GpuMemoryUsageMB = null,
    double? GpuTemperature = null,
    double? NetworkBandwidthUsagePercent = null,
    double? DiskUsagePercent = null,
    int ProcessCount = 0,
    int ThreadCount = 0)
{
    /// <summary>
    /// 使用メモリ容量 (MB)
    /// </summary>
    public long UsedMemoryMB => TotalMemoryMB - AvailableMemoryMB;

    /// <summary>
    /// システム全体の負荷レベル算出
    /// CPU・メモリ使用率の重み付け平均
    /// </summary>
    public ResourceLoadLevel LoadLevel => CalculateLoadLevel();

    /// <summary>
    /// GPU加速が利用可能かどうか
    /// </summary>
    public bool IsGpuAvailable => GpuUsagePercent.HasValue;

    /// <summary>
    /// リソース警告が必要な状況かどうか
    /// </summary>
    public bool RequiresWarning =>
        CpuUsagePercent > 85.0 ||
        MemoryUsagePercent > 90.0 ||
        (GpuUsagePercent.HasValue && GpuUsagePercent.Value > 95.0);

    /// <summary>
    /// 翻訳処理に適した状況かどうか
    /// リソース使用率が適切な範囲内にある場合true
    /// </summary>
    public bool IsOptimalForTranslation =>
        CpuUsagePercent < 70.0 &&
        MemoryUsagePercent < 80.0 &&
        (!GpuUsagePercent.HasValue || GpuUsagePercent.Value < 85.0);

    /// <summary>
    /// リソース状況の文字列表現
    /// ログ出力用の簡潔な形式
    /// </summary>
    /// <returns>リソース状況の概要文字列</returns>
    public override string ToString()
    {
        var gpu = IsGpuAvailable ? $" GPU:{GpuUsagePercent:F1}%" : " GPU:N/A";
        return $"[{Timestamp:HH:mm:ss}] CPU:{CpuUsagePercent:F1}% MEM:{MemoryUsagePercent:F1}%({UsedMemoryMB:N0}/{TotalMemoryMB:N0}MB){gpu} Load:{LoadLevel}";
    }

    /// <summary>
    /// 負荷レベル算出の内部実装
    /// </summary>
    private ResourceLoadLevel CalculateLoadLevel()
    {
        // CPU・メモリ使用率の重み付け平均（CPU:60%, Memory:40%）
        var weightedAverage = (CpuUsagePercent * 0.6) + (MemoryUsagePercent * 0.4);

        // GPU使用率が利用可能な場合は追加考慮（CPU:50%, Memory:30%, GPU:20%）
        if (IsGpuAvailable && GpuUsagePercent.HasValue)
        {
            weightedAverage = (CpuUsagePercent * 0.5) + (MemoryUsagePercent * 0.3) + (GpuUsagePercent.Value * 0.2);
        }

        return weightedAverage switch
        {
            < 25.0 => ResourceLoadLevel.Low,
            < 50.0 => ResourceLoadLevel.Moderate,
            < 75.0 => ResourceLoadLevel.High,
            < 90.0 => ResourceLoadLevel.VeryHigh,
            _ => ResourceLoadLevel.Critical
        };
    }
}

/// <summary>
/// システムリソース負荷レベル
/// </summary>
public enum ResourceLoadLevel
{
    /// <summary>低負荷 (0-25%)</summary>
    Low = 0,
    /// <summary>中負荷 (25-50%)</summary>
    Moderate = 1,
    /// <summary>高負荷 (50-75%)</summary>
    High = 2,
    /// <summary>非常に高負荷 (75-90%)</summary>
    VeryHigh = 3,
    /// <summary>危険レベル (90%以上)</summary>
    Critical = 4
}

/// <summary>
/// リソース監視設定
/// </summary>
/// <param name="MonitoringIntervalMs">監視間隔 (ミリ秒, デフォルト: 5000ms)</param>
/// <param name="HistoryRetentionMinutes">履歴保持時間 (分, デフォルト: 60分)</param>
/// <param name="CpuWarningThreshold">CPU警告閾値 (%, デフォルト: 85%)</param>
/// <param name="MemoryWarningThreshold">メモリ警告閾値 (%, デフォルト: 90%)</param>
/// <param name="GpuWarningThreshold">GPU警告閾値 (%, デフォルト: 95%)</param>
/// <param name="EnableGpuMonitoring">GPU監視有効化フラグ (デフォルト: true)</param>
/// <param name="EnableNetworkMonitoring">ネットワーク監視有効化フラグ (デフォルト: false)</param>
/// <param name="EnableDiskMonitoring">ディスク監視有効化フラグ (デフォルト: false)</param>
public sealed record ResourceMonitoringSettings(
    int MonitoringIntervalMs = 5000,
    int HistoryRetentionMinutes = 60,
    double CpuWarningThreshold = 85.0,
    double MemoryWarningThreshold = 90.0,
    double GpuWarningThreshold = 95.0,
    bool EnableGpuMonitoring = true,
    bool EnableNetworkMonitoring = false,
    bool EnableDiskMonitoring = false)
{
    /// <summary>
    /// 設定値検証
    /// </summary>
    /// <returns>設定が有効な場合true</returns>
    public bool IsValid =>
        MonitoringIntervalMs >= 1000 && MonitoringIntervalMs <= 60000 &&
        HistoryRetentionMinutes >= 1 && HistoryRetentionMinutes <= 1440 &&
        CpuWarningThreshold >= 50.0 && CpuWarningThreshold <= 100.0 &&
        MemoryWarningThreshold >= 50.0 && MemoryWarningThreshold <= 100.0 &&
        GpuWarningThreshold >= 50.0 && GpuWarningThreshold <= 100.0;
}
