using Baketa.Core.Models.Capture;

namespace Baketa.Core.Abstractions.Capture;

/// <summary>
/// 適応的キャプチャサービスの抽象インターフェース
/// </summary>
public interface IAdaptiveCaptureService
{
    /// <summary>
    /// 環境に応じた最適手法でキャプチャを実行
    /// </summary>
    Task<AdaptiveCaptureResult> CaptureAsync(IntPtr hwnd, CaptureOptions options);
    
    /// <summary>
    /// GPU環境を検出
    /// </summary>
    Task<GPUEnvironmentInfo> DetectGPUEnvironmentAsync();
    
    /// <summary>
    /// 最適な戦略を選択
    /// </summary>
    Task<ICaptureStrategy> SelectOptimalStrategyAsync(GPUEnvironmentInfo environment);
    
    /// <summary>
    /// 現在キャッシュされているGPU環境情報を取得
    /// </summary>
    GPUEnvironmentInfo? GetCachedEnvironmentInfo();
    
    /// <summary>
    /// GPU環境情報のキャッシュをクリア（再検出を強制）
    /// </summary>
    void ClearEnvironmentCache();
    
    /// <summary>
    /// キャプチャサービスを停止し、リソースをクリーンアップ
    /// </summary>
    Task StopAsync();
    
    /// <summary>
    /// 現在実行中のキャプチャ操作をキャンセル
    /// </summary>
    Task CancelCurrentCaptureAsync();
}