namespace Baketa.Infrastructure.ResourceManagement;

/// <summary>
/// ハイブリッドリソース管理システムの設定
/// </summary>
public sealed class HybridResourceSettings
{
    // === チャネル設定 ===
    public int OcrChannelCapacity { get; set; } = 100;
    public int TranslationChannelCapacity { get; set; } = 50;

    // === 並列度設定 ===
    public int InitialOcrParallelism { get; set; } = 2;
    public int MaxOcrParallelism { get; set; } = 4;
    public int InitialTranslationParallelism { get; set; } = 1;
    public int MaxTranslationParallelism { get; set; } = 2;

    // === リソース閾値設定 ===
    public double CpuLowThreshold { get; set; } = 50;
    public double CpuHighThreshold { get; set; } = 80;
    public double MemoryLowThreshold { get; set; } = 60;
    public double MemoryHighThreshold { get; set; } = 85;
    public double GpuLowThreshold { get; set; } = 40;
    public double GpuHighThreshold { get; set; } = 75;
    public double VramLowThreshold { get; set; } = 50;
    public double VramHighThreshold { get; set; } = 80;

    // === クールダウン設定 ===
    public int MaxCooldownMs { get; set; } = 500;
    public int HysteresisTimeoutSeconds { get; set; } = 3;

    // === 監視設定 ===
    public int SamplingIntervalMs { get; set; } = 1000;
    public bool EnableGpuMonitoring { get; set; } = true;

    // === 制御設定 ===
    public bool EnableDynamicParallelism { get; set; } = true;
    public bool EnableDetailedLogging { get; set; } = false;
}