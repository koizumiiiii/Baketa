namespace Baketa.Core.Translation.Models;

/// <summary>
/// コンピュートデバイス情報
/// </summary>
public class ComputeDevice
{
    /// <summary>
    /// デバイスID
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;
    
    /// <summary>
    /// デバイス名
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// デバイスタイプ
    /// </summary>
    public ComputeDeviceType DeviceType { get; set; }
    
    /// <summary>
    /// メモリ容量（バイト）
    /// </summary>
    public long MemoryCapacity { get; set; }
    
    /// <summary>
    /// 利用可能かどうか
    /// </summary>
    public bool IsAvailable { get; set; }
    
    /// <summary>
    /// 優先度（低いほど優先）
    /// </summary>
    public int Priority { get; set; }
    
    /// <summary>
    /// CPUデバイスかどうか
    /// </summary>
    public bool IsCpu => DeviceType == ComputeDeviceType.Cpu;
    
    /// <summary>
    /// GPUデバイスかどうか
    /// </summary>
    public bool IsGpu => DeviceType == ComputeDeviceType.Cuda 
        || DeviceType == ComputeDeviceType.DirectML
        || DeviceType == ComputeDeviceType.OpenCL;
    
    /// <summary>
    /// デフォルトのCPUデバイスを作成
    /// </summary>
    public static ComputeDevice DefaultCpu => new ComputeDevice
    {
        DeviceId = "cpu-0",
        Name = "CPU",
        DeviceType = ComputeDeviceType.Cpu,
        IsAvailable = true,
        Priority = 100
    };
    
    /// <summary>
    /// CPUデバイスを作成
    /// </summary>
    /// <returns>CPUデバイス</returns>
    public static ComputeDevice CreateCpu() => new ComputeDevice
    {
        DeviceId = "cpu-0",
        Name = "CPU",
        DeviceType = ComputeDeviceType.Cpu,
        IsAvailable = true,
        Priority = 100
    };
    
    /// <summary>
    /// GPUデバイスを作成
    /// </summary>
    /// <param name="deviceId">デバイスID</param>
    /// <param name="name">デバイス名</param>
    /// <param name="deviceType">デバイスタイプ</param>
    /// <returns>GPUデバイス</returns>
    public static ComputeDevice CreateGpu(int deviceId, string name, ComputeDeviceType deviceType = ComputeDeviceType.Cuda) => new ComputeDevice
    {
        DeviceId = $"gpu-{deviceId}",
        Name = name,
        DeviceType = deviceType,
        IsAvailable = true,
        Priority = 1
    };
    
    /// <summary>
    /// CUDAデバイスを作成
    /// </summary>
    /// <param name="deviceId">デバイスID</param>
    /// <param name="name">デバイス名</param>
    /// <returns>CUDAデバイス</returns>
    public static ComputeDevice CreateGpu(int deviceId, string name) => CreateGpu(deviceId, name, ComputeDeviceType.Cuda);
}

/// <summary>
/// コンピュートデバイスタイプ
/// </summary>
public enum ComputeDeviceType
{
    /// <summary>
    /// CPU
    /// </summary>
    Cpu,
    
    /// <summary>
    /// CUDA (NVIDIA GPU)
    /// </summary>
    Cuda,
    
    /// <summary>
    /// DirectML (Microsoft DirectX Machine Learning)
    /// </summary>
    DirectML,
    
    /// <summary>
    /// OpenCL
    /// </summary>
    OpenCL,
    
    /// <summary>
    /// その他
    /// </summary>
    Other
}