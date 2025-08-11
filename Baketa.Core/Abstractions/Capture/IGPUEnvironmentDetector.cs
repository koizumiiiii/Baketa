using Baketa.Core.Models.Capture;

namespace Baketa.Core.Abstractions.Capture;

/// <summary>
/// GPU環境検出器のインターフェース
/// </summary>
public interface IGPUEnvironmentDetector
{
    /// <summary>
    /// GPU環境を検出
    /// </summary>
    Task<GPUEnvironmentInfo> DetectEnvironmentAsync();
    
    /// <summary>
    /// 特定のGPUアダプターの詳細情報を取得
    /// </summary>
    Task<GPUAdapter?> GetAdapterDetailsAsync(int adapterIndex);
    
    /// <summary>
    /// DirectXの最大テクスチャサイズを確認
    /// </summary>
    Task<uint> GetMaximumTexture2DDimensionAsync();
    
    /// <summary>
    /// HDRサポート状況を確認
    /// </summary>
    Task<bool> CheckHDRSupportAsync();
    
    /// <summary>
    /// 環境検出結果をキャッシュから取得（高速）
    /// </summary>
    GPUEnvironmentInfo? GetCachedEnvironmentInfo();
    
    /// <summary>
    /// 環境情報キャッシュをクリア
    /// </summary>
    void ClearCache();
}