namespace Baketa.Core.Abstractions.GPU;

/// <summary>
/// GPU デバイス管理の抽象化
/// Multi-GPU環境での負荷分散と最適化
/// Issue #143 Week 2: PNPDeviceIDベースのGPU個別管理
/// </summary>
public interface IGpuDeviceManager
{
    /// <summary>
    /// 利用可能なGPUデバイス一覧を取得
    /// PNPDeviceIDとパフォーマンス情報を含む
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>GPU デバイス情報一覧</returns>
    Task<IReadOnlyList<GpuDeviceInfo>> GetAvailableGpuDevicesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 指定された処理負荷に最適なGPUを選択
    /// 現在の負荷状況とGPU性能を考慮した自動選択
    /// </summary>
    /// <param name="workloadType">処理負荷タイプ</param>
    /// <param name="estimatedMemoryMB">推定メモリ使用量（MB）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>最適なGPU環境情報</returns>
    Task<GpuEnvironmentInfo> SelectOptimalGpuAsync(GpuWorkloadType workloadType, int estimatedMemoryMB = 0, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 複数のPNPDeviceIDから最適なGPUを選択
    /// 明示的なデバイス指定での最適化
    /// </summary>
    /// <param name="pnpDeviceIds">候補PNPDeviceID配列</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>選択されたGPU環境情報</returns>
    Task<GpuEnvironmentInfo> SelectOptimalGpuAsync(string[] pnpDeviceIds, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 指定されたGPUの可用性を検証
    /// TDR状態やドライバー問題の検出
    /// </summary>
    /// <param name="pnpDeviceId">検証対象PNPDeviceID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>GPU可用性情報</returns>
    Task<GpuAvailabilityStatus> ValidateGpuAvailabilityAsync(string pnpDeviceId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// GPUの現在の負荷状況を取得
    /// リアルタイム監視とスケジューリング用
    /// </summary>
    /// <param name="pnpDeviceId">監視対象PNPDeviceID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>GPU負荷状況</returns>
    Task<GpuWorkloadStatus> GetGpuWorkloadStatusAsync(string pnpDeviceId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// GPU負荷分散のための推奨配置を取得
    /// 複数処理の効率的なGPU配置戦略
    /// </summary>
    /// <param name="workloads">処理負荷一覧</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>GPU配置推奨事項</returns>
    Task<GpuAllocationRecommendation> GetOptimalAllocationAsync(IReadOnlyList<GpuWorkloadRequest> workloads, CancellationToken cancellationToken = default);
}

/// <summary>
/// GPU デバイス情報
/// </summary>
public class GpuDeviceInfo
{
    /// <summary>
    /// PNP Device ID（一意識別子）
    /// </summary>
    public string PnpDeviceId { get; init; } = string.Empty;
    
    /// <summary>
    /// GPU 名称
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// ベンダー名
    /// </summary>
    public string Vendor { get; init; } = string.Empty;
    
    /// <summary>
    /// 専用VRAM容量（MB）
    /// </summary>
    public long DedicatedMemoryMB { get; init; }
    
    /// <summary>
    /// 共有メモリ容量（MB）
    /// </summary>
    public long SharedMemoryMB { get; init; }
    
    /// <summary>
    /// 現在の利用可能メモリ（MB）
    /// </summary>
    public long AvailableMemoryMB { get; init; }
    
    /// <summary>
    /// デバイスインデックス
    /// </summary>
    public int DeviceIndex { get; init; }
    
    /// <summary>
    /// 専用GPU（統合GPUでない）
    /// </summary>
    public bool IsDedicatedGpu { get; init; }
    
    /// <summary>
    /// 対応実行プロバイダー
    /// </summary>
    public List<ExecutionProvider> SupportedProviders { get; init; } = new();
    
    /// <summary>
    /// パフォーマンス評価スコア（0-100）
    /// </summary>
    public int PerformanceScore { get; init; }
    
    /// <summary>
    /// ドライバーバージョン
    /// </summary>
    public string DriverVersion { get; init; } = string.Empty;
}

/// <summary>
/// GPU 処理負荷タイプ
/// </summary>
public enum GpuWorkloadType
{
    /// <summary>
    /// テキスト検出処理
    /// </summary>
    TextDetection,
    
    /// <summary>
    /// テキスト認識処理
    /// </summary>
    TextRecognition,
    
    /// <summary>
    /// 画像前処理
    /// </summary>
    ImagePreprocessing,
    
    /// <summary>
    /// 言語識別
    /// </summary>
    LanguageIdentification,
    
    /// <summary>
    /// 汎用推論処理
    /// </summary>
    GeneralInference
}

/// <summary>
/// GPU 可用性状況
/// </summary>
public class GpuAvailabilityStatus
{
    /// <summary>
    /// 利用可能かどうか
    /// </summary>
    public bool IsAvailable { get; init; }
    
    /// <summary>
    /// 詳細ステータス
    /// </summary>
    public string StatusMessage { get; init; } = string.Empty;
    
    /// <summary>
    /// TDR状態にあるかどうか
    /// </summary>
    public bool IsInTdrState { get; init; }
    
    /// <summary>
    /// ドライバーが正常かどうか
    /// </summary>
    public bool IsDriverHealthy { get; init; }
    
    /// <summary>
    /// 最後の可用性チェック時刻
    /// </summary>
    public DateTime LastCheckedAt { get; init; }
}

/// <summary>
/// GPU 負荷状況
/// </summary>
public class GpuWorkloadStatus
{
    /// <summary>
    /// GPU使用率（0-100%）
    /// </summary>
    public double GpuUtilization { get; init; }
    
    /// <summary>
    /// メモリ使用率（0-100%）
    /// </summary>
    public double MemoryUtilization { get; init; }
    
    /// <summary>
    /// 現在実行中のプロセス数
    /// </summary>
    public int ActiveProcessCount { get; init; }
    
    /// <summary>
    /// 推定空き容量（MB）
    /// </summary>
    public long EstimatedFreeMemoryMB { get; init; }
    
    /// <summary>
    /// GPU温度（摂氏、取得可能な場合）
    /// </summary>
    public double? TemperatureCelsius { get; init; }
}

/// <summary>
/// GPU 処理負荷要求
/// </summary>
public class GpuWorkloadRequest
{
    /// <summary>
    /// 処理負荷タイプ
    /// </summary>
    public GpuWorkloadType WorkloadType { get; init; }
    
    /// <summary>
    /// 推定メモリ使用量（MB）
    /// </summary>
    public int EstimatedMemoryMB { get; init; }
    
    /// <summary>
    /// 優先度（1-10、高いほど優先）
    /// </summary>
    public int Priority { get; init; } = 5;
    
    /// <summary>
    /// 要求ID（追跡用）
    /// </summary>
    public string RequestId { get; init; } = Guid.NewGuid().ToString();
}

/// <summary>
/// GPU 配置推奨事項
/// </summary>
public class GpuAllocationRecommendation
{
    /// <summary>
    /// 推奨GPU配置一覧
    /// </summary>
    public List<GpuAllocationItem> Allocations { get; init; } = new();
    
    /// <summary>
    /// 配置理由
    /// </summary>
    public string Reason { get; init; } = string.Empty;
    
    /// <summary>
    /// 総予想パフォーマンススコア
    /// </summary>
    public double TotalPerformanceScore { get; init; }
}

/// <summary>
/// GPU 配置項目
/// </summary>
public class GpuAllocationItem
{
    /// <summary>
    /// 処理負荷要求
    /// </summary>
    public GpuWorkloadRequest WorkloadRequest { get; init; } = new();
    
    /// <summary>
    /// 推奨GPU Device ID
    /// </summary>
    public string RecommendedPnpDeviceId { get; init; } = string.Empty;
    
    /// <summary>
    /// 推奨実行プロバイダー
    /// </summary>
    public List<ExecutionProvider> RecommendedProviders { get; init; } = new();
    
    /// <summary>
    /// 配置信頼度（0-1）
    /// </summary>
    public double Confidence { get; init; }
}