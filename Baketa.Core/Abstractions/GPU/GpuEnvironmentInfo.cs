namespace Baketa.Core.Abstractions.GPU;

/// <summary>
/// GPU環境情報
/// RTX4070のような専用GPUからIntel UHD統合GPUまで対応
/// </summary>
public sealed record GpuEnvironmentInfo
{
    /// <summary>
    /// 統合GPU（Intel UHD, AMD Radeon等）かどうか
    /// </summary>
    public bool IsIntegratedGpu { get; init; }
    
    /// <summary>
    /// 専用GPU（RTX4070, GTX1660等）かどうか
    /// </summary>
    public bool IsDedicatedGpu { get; init; }
    
    /// <summary>
    /// CUDA対応かどうか（NVIDIA GPU）
    /// </summary>
    public bool SupportsCuda { get; init; }
    
    /// <summary>
    /// OpenCL対応かどうか（AMD/Intel GPU）
    /// </summary>
    public bool SupportsOpenCL { get; init; }
    
    /// <summary>
    /// DirectML対応かどうか（Windows統合GPU）
    /// </summary>
    public bool SupportsDirectML { get; init; }
    
    /// <summary>
    /// OpenVINO対応かどうか（Intel GPU最適化）
    /// </summary>
    public bool SupportsOpenVINO { get; init; }
    
    /// <summary>
    /// TensorRT対応かどうか（RTXシリーズ最適化）
    /// </summary>
    public bool SupportsTensorRT { get; init; }
    
    /// <summary>
    /// 利用可能VRAM容量（MB）
    /// 統合GPUの場合はシステムRAM共有分
    /// </summary>
    public long AvailableMemoryMB { get; init; }
    
    /// <summary>
    /// 最大テクスチャ2Dサイズ（GPU制約チェック用）
    /// </summary>
    public int MaximumTexture2DDimension { get; init; }
    
    /// <summary>
    /// DirectXフィーチャーレベル
    /// </summary>
    public DirectXFeatureLevel DirectXFeatureLevel { get; init; }
    
    /// <summary>
    /// GPU名（識別・ログ用）
    /// </summary>
    public string GpuName { get; init; } = string.Empty;
    
    /// <summary>
    /// GPU デバイス ID（Multi-GPU環境での識別用）
    /// </summary>
    public int GpuDeviceId { get; init; } = 0;
    
    /// <summary>
    /// GPU Compute能力（CUDA Compute Capability）
    /// </summary>
    public ComputeCapability ComputeCapability { get; init; } = ComputeCapability.Unknown;
    
    /// <summary>
    /// 推奨ONNX Runtime Execution Provider
    /// 環境に最適なプロバイダーの優先順位リスト
    /// </summary>
    public IReadOnlyList<ExecutionProvider> RecommendedProviders { get; init; } = [];
    
    /// <summary>
    /// GPU環境が利用可能かどうか
    /// </summary>
    public bool IsGpuAvailable => IsDedicatedGpu || IsIntegratedGpu;
    
    /// <summary>
    /// 高性能GPU環境かどうか（RTX/GTX専用GPU）
    /// </summary>
    public bool IsHighPerformanceGpu => IsDedicatedGpu && AvailableMemoryMB >= 4096;
}

/// <summary>
/// DirectXフィーチャーレベル列挙
/// </summary>
public enum DirectXFeatureLevel
{
    Unknown = 0,
    D3D11_0 = 1,
    D3D11_1 = 2,
    D3D12_0 = 3,
    D3D12_1 = 4,
    D3D12_2 = 5
}

/// <summary>
/// GPU Compute能力
/// </summary>
public enum ComputeCapability
{
    Unknown = 0,
    Compute30 = 30,  // GTX 600シリーズ
    Compute35 = 35,  // GTX 700シリーズ  
    Compute50 = 50,  // GTX 900シリーズ
    Compute61 = 61,  // GTX 1000シリーズ
    Compute75 = 75,  // RTX 2000シリーズ
    Compute86 = 86,  // RTX 3000シリーズ
    Compute89 = 89   // RTX 4000シリーズ（RTX4070等）
}

/// <summary>
/// ONNX Runtime Execution Provider種類
/// </summary>
public enum ExecutionProvider
{
    CPU = 0,
    CUDA = 1,
    DirectML = 2,
    TensorRT = 3,
    OpenVINO = 4,
    OpenCL = 5
}