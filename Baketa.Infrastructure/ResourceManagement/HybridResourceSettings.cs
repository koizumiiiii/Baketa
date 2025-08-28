namespace Baketa.Infrastructure.ResourceManagement;

/// <summary>
/// ハイブリッドリソース管理システムの設定
/// </summary>
/// <summary>
/// ハイブリッドリソース管理システムの設定
/// Phase 3: ホットリロード対応の設定クラス
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
    
    // === Phase 3: 高度制御設定 ===
    public bool EnableVerboseLogging { get; set; } = false;
    
    // === Phase 3: ホットリロード設定 ===
    public bool EnableHotReload { get; set; } = true;
    public int ConfigurationPollingIntervalMs { get; set; } = 5000;
    
    /// <summary>
    /// 設定値の妥当性を検証する
    /// </summary>
    public bool IsValid()
    {
        return OcrChannelCapacity > 0 &&
               TranslationChannelCapacity > 0 &&
               InitialOcrParallelism > 0 &&
               MaxOcrParallelism >= InitialOcrParallelism &&
               InitialTranslationParallelism > 0 &&
               MaxTranslationParallelism >= InitialTranslationParallelism &&
               CpuLowThreshold >= 0 && CpuLowThreshold <= 100 &&
               CpuHighThreshold >= CpuLowThreshold && CpuHighThreshold <= 100 &&
               MemoryLowThreshold >= 0 && MemoryLowThreshold <= 100 &&
               MemoryHighThreshold >= MemoryLowThreshold && MemoryHighThreshold <= 100 &&
               GpuLowThreshold >= 0 && GpuLowThreshold <= 100 &&
               GpuHighThreshold >= GpuLowThreshold && GpuHighThreshold <= 100 &&
               VramLowThreshold >= 0 && VramLowThreshold <= 100 &&
               VramHighThreshold >= VramLowThreshold && VramHighThreshold <= 100 &&
               MaxCooldownMs >= 0 &&
               HysteresisTimeoutSeconds >= 0 &&
               SamplingIntervalMs > 0 &&
               ConfigurationPollingIntervalMs > 0;
    }
    
    /// <summary>
    /// 設定の差分を取得する（ホットリロード用）
    /// </summary>
    public IEnumerable<string> GetDifferences(HybridResourceSettings other)
    {
        var differences = new List<string>();
        
        if (OcrChannelCapacity != other.OcrChannelCapacity)
            differences.Add($"OcrChannelCapacity: {OcrChannelCapacity} → {other.OcrChannelCapacity}");
        if (TranslationChannelCapacity != other.TranslationChannelCapacity)
            differences.Add($"TranslationChannelCapacity: {TranslationChannelCapacity} → {other.TranslationChannelCapacity}");
        if (MaxOcrParallelism != other.MaxOcrParallelism)
            differences.Add($"MaxOcrParallelism: {MaxOcrParallelism} → {other.MaxOcrParallelism}");
        if (MaxTranslationParallelism != other.MaxTranslationParallelism)
            differences.Add($"MaxTranslationParallelism: {MaxTranslationParallelism} → {other.MaxTranslationParallelism}");
        if (Math.Abs(CpuHighThreshold - other.CpuHighThreshold) > 0.1)
            differences.Add($"CpuHighThreshold: {CpuHighThreshold:F1} → {other.CpuHighThreshold:F1}");
        if (Math.Abs(MemoryHighThreshold - other.MemoryHighThreshold) > 0.1)
            differences.Add($"MemoryHighThreshold: {MemoryHighThreshold:F1} → {other.MemoryHighThreshold:F1}");
        if (Math.Abs(GpuHighThreshold - other.GpuHighThreshold) > 0.1)
            differences.Add($"GpuHighThreshold: {GpuHighThreshold:F1} → {other.GpuHighThreshold:F1}");
        if (Math.Abs(VramHighThreshold - other.VramHighThreshold) > 0.1)
            differences.Add($"VramHighThreshold: {VramHighThreshold:F1} → {other.VramHighThreshold:F1}");
        if (EnableVerboseLogging != other.EnableVerboseLogging)
            differences.Add($"EnableVerboseLogging: {EnableVerboseLogging} → {other.EnableVerboseLogging}");
        if (EnableHotReload != other.EnableHotReload)
            differences.Add($"EnableHotReload: {EnableHotReload} → {other.EnableHotReload}");
            
        return differences;
    }
}