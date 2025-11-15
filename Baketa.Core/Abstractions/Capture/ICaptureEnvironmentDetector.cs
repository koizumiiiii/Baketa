using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Models.Capture;

namespace Baketa.Core.Abstractions.Capture;

/// <summary>
/// キャプチャ用GPU環境検出器のインターフェース
/// </summary>
public interface ICaptureEnvironmentDetector
{
    /// <summary>
    /// GPU環境を検出
    /// </summary>
    Task<GpuEnvironmentInfo> DetectEnvironmentAsync();

    /// <summary>
    /// 特定のGPUアダプターの詳細情報を取得
    /// </summary>
    Task<string?> GetAdapterDetailsAsync(int adapterIndex);

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
    GpuEnvironmentInfo? GetCachedEnvironmentInfo();

    /// <summary>
    /// 環境情報キャッシュをクリア
    /// </summary>
    void ClearCache();
}
