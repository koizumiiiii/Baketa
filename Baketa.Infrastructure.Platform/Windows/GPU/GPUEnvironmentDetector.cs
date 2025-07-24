using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Models.Capture;
using Baketa.Core.Exceptions.Capture;
using System.Runtime.InteropServices;

namespace Baketa.Infrastructure.Platform.Windows.GPU;

/// <summary>
/// GPU環境を検出する実装クラス
/// </summary>
public class GPUEnvironmentDetector : IGPUEnvironmentDetector
{
    private readonly ILogger<GPUEnvironmentDetector> _logger;
    private GPUEnvironmentInfo? _cachedEnvironment;
    private readonly object _cacheLock = new();
    
    // Windows API とDirectX関連の定数
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    
    public GPUEnvironmentDetector(ILogger<GPUEnvironmentDetector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GPUEnvironmentInfo> DetectEnvironmentAsync()
    {
        _logger.LogInformation("GPU環境検出を開始");
        
        try
        {
            var info = new GPUEnvironmentInfo
            {
                DetectionTime = DateTime.Now,
                DetectionSource = "GPUEnvironmentDetector"
            };
            
            // 1. DirectXサポートレベル確認
            await CheckDirectXSupportAsync(info).ConfigureAwait(false);
            
            // 2. 利用可能なGPUアダプター列挙
            await EnumerateAdaptersAsync(info).ConfigureAwait(false);
            
            // 3. GPU種別判定（統合/専用）
            DetermineGPUType(info);
            
            // 4. テクスチャサイズ制限確認
            await CheckTextureLimitsAsync(info).ConfigureAwait(false);
            
            // 5. HDR・色空間サポート確認
            await CheckDisplayCapabilitiesAsync(info).ConfigureAwait(false);
            
            // 6. WDDM バージョン確認
            CheckWDDMVersion(info);
            
            // 7. ソフトウェアレンダリング対応確認
            CheckSoftwareRenderingSupport(info);
            
            // 結果をキャッシュ
            lock (_cacheLock)
            {
                _cachedEnvironment = info;
            }
            
            _logger.LogInformation("GPU環境検出完了: GPU={GpuName}, 統合={IsIntegrated}, 専用={IsDedicated}", 
                info.GPUName, info.IsIntegratedGPU, info.IsDedicatedGPU);
                
            return info;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPU環境検出中にエラーが発生");
            throw new GPUEnvironmentDetectionException("GPU環境検出に失敗しました", ex);
        }
    }

    public GPUEnvironmentInfo? GetCachedEnvironmentInfo()
    {
        lock (_cacheLock)
        {
            return _cachedEnvironment;
        }
    }

    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _cachedEnvironment = null;
        }
        _logger.LogDebug("GPU環境キャッシュをクリア");
    }

    public async Task<GPUAdapter?> GetAdapterDetailsAsync(int adapterIndex)
    {
        try
        {
            // この実装は簡易版。実際のDXGI APIを使用した実装が必要
            var environment = await DetectEnvironmentAsync().ConfigureAwait(false);
            if (adapterIndex < environment.AvailableAdapters.Count)
            {
                return environment.AvailableAdapters[adapterIndex];
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "アダプター詳細取得に失敗: Index={AdapterIndex}", adapterIndex);
            return null;
        }
    }

    public async Task<uint> GetMaximumTexture2DDimensionAsync()
    {
        try
        {
            var environment = await DetectEnvironmentAsync().ConfigureAwait(false);
            return environment.MaximumTexture2DDimension;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "最大テクスチャサイズ取得に失敗");
            return 4096; // セーフティデフォルト値
        }
    }

    public async Task<bool> CheckHDRSupportAsync()
    {
        try
        {
            var environment = await DetectEnvironmentAsync().ConfigureAwait(false);
            return environment.HasHDRSupport;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HDRサポート確認に失敗");
            return false;
        }
    }

    private async Task CheckDirectXSupportAsync(GPUEnvironmentInfo info)
    {
        await Task.Run(() =>
        {
            try
            {
                // D3D11CreateDevice を使用してFeature Level確認
                // この実装では簡易チェック版
                info.HasDirectX11Support = CheckDirectX11Available();
                info.FeatureLevel = GetDirectXFeatureLevel();
                
                _logger.LogDebug("DirectXサポート確認完了: DX11={HasDx11}, FeatureLevel={FeatureLevel}", 
                    info.HasDirectX11Support, info.FeatureLevel);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DirectXサポート確認中にエラー");
                info.HasDirectX11Support = false;
                info.FeatureLevel = DirectXFeatureLevel.Unknown;
            }
        }).ConfigureAwait(false);
    }

    private async Task EnumerateAdaptersAsync(GPUEnvironmentInfo info)
    {
        await Task.Run(() =>
        {
            try
            {
                // IDXGIFactory1::EnumAdapters1 の代替実装
                // 実際の実装では P/Invoke を使用してDXGI APIを呼び出す必要があります
                var adapters = GetAvailableGraphicsAdapters();
                info.AvailableAdapters = adapters;
                
                if (adapters.Count > 0)
                {
                    info.GPUName = adapters[0].Name;
                    info.AvailableMemoryMB = adapters[0].DedicatedVideoMemoryMB;
                    info.IsMultiGPUEnvironment = adapters.Count > 1;
                }
                
                _logger.LogDebug("GPU アダプター列挙完了: {AdapterCount}個のアダプターを検出", adapters.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GPUアダプター列挙中にエラー");
                info.AvailableAdapters = [];
            }
        }).ConfigureAwait(false);
    }

    private void DetermineGPUType(GPUEnvironmentInfo info)
    {
        try
        {
            if (info.AvailableAdapters.Count == 0)
            {
                info.IsIntegratedGPU = false;
                info.IsDedicatedGPU = false;
                return;
            }

            // 統合GPUの判定ロジック
            var primaryAdapter = info.AvailableAdapters[0];
            info.IsIntegratedGPU = primaryAdapter.IsIntegrated;
            info.IsDedicatedGPU = !primaryAdapter.IsIntegrated;
            
            // 複数GPU環境での詳細判定
            if (info.IsMultiGPUEnvironment)
            {
                var hasIntegrated = info.AvailableAdapters.Any(a => a.IsIntegrated);
                var hasDedicated = info.AvailableAdapters.Any(a => !a.IsIntegrated);
                
                if (hasIntegrated && hasDedicated)
                {
                    // 統合GPU + 専用GPU の環境では、通常統合GPUを使用する方が効率的
                    info.IsIntegratedGPU = true;
                    info.IsDedicatedGPU = false;
                    _logger.LogInformation("マルチGPU環境: 統合GPUを優先選択");
                }
            }
            
            _logger.LogDebug("GPU種別判定完了: 統合={IsIntegrated}, 専用={IsDedicated}", 
                info.IsIntegratedGPU, info.IsDedicatedGPU);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPU種別判定中にエラー");
            info.IsIntegratedGPU = false;
            info.IsDedicatedGPU = false;
        }
    }

    private async Task CheckTextureLimitsAsync(GPUEnvironmentInfo info)
    {
        await Task.Run(() =>
        {
            try
            {
                // D3D11_FEATURE_DATA_D3D10_X_HARDWARE_OPTIONS取得の代替実装
                info.MaximumTexture2DDimension = GetMaxTextureSize();
                
                _logger.LogDebug("テクスチャ制限確認完了: MaxTexture2D={MaxSize}", info.MaximumTexture2DDimension);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "テクスチャ制限確認中にエラー");
                info.MaximumTexture2DDimension = 4096; // セーフティデフォルト
            }
        }).ConfigureAwait(false);
    }

    private async Task CheckDisplayCapabilitiesAsync(GPUEnvironmentInfo info)
    {
        await Task.Run(() =>
        {
            try
            {
                // HDRサポートと色空間の確認
                info.HasHDRSupport = CheckHDRDisplaySupport();
                info.ColorSpaceSupport = GetSupportedColorSpaces();
                
                _logger.LogDebug("ディスプレイ機能確認完了: HDR={HasHdr}, ColorSpace={ColorSpace}", 
                    info.HasHDRSupport, info.ColorSpaceSupport);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ディスプレイ機能確認中にエラー");
                info.HasHDRSupport = false;
                info.ColorSpaceSupport = "sRGB";
            }
        }).ConfigureAwait(false);
    }

    private void CheckWDDMVersion(GPUEnvironmentInfo info)
    {
        try
        {
            // WDDM バージョン確認（レジストリから）
            info.IsWDDMVersion2OrHigher = GetWDDMVersion() >= 2.0;
            
            _logger.LogDebug("WDDM バージョン確認完了: Version2Plus={IsVersion2Plus}", info.IsWDDMVersion2OrHigher);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WDDM バージョン確認中にエラー");
            info.IsWDDMVersion2OrHigher = true; // 多くの現代システムで true と仮定
        }
    }

    private void CheckSoftwareRenderingSupport(GPUEnvironmentInfo info)
    {
        try
        {
            // D3D_DRIVER_TYPE_WARPの利用可否判定
            info.SupportsSoftwareRendering = CheckWARPSupport();
            
            _logger.LogDebug("ソフトウェアレンダリング確認完了: Supported={Supported}", info.SupportsSoftwareRendering);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ソフトウェアレンダリング確認中にエラー");
            info.SupportsSoftwareRendering = true; // Windows 10+ では通常利用可能
        }
    }

    // 以下は実際の実装で P/Invoke や Windows API を使用する部分のプレースホルダー
    
    private bool CheckDirectX11Available()
    {
        // 実装: D3D11CreateDevice を呼び出してDirectX 11が利用可能かチェック
        return true; // プレースホルダー
    }

    private DirectXFeatureLevel GetDirectXFeatureLevel()
    {
        // 実装: 実際のDirectX Feature Levelを取得
        return DirectXFeatureLevel.Level110; // プレースホルダー
    }

    private List<GPUAdapter> GetAvailableGraphicsAdapters()
    {
        // 実装: DXGI を使用してアダプター情報を取得
        return [
            new() {
                Name = "検出されたGPU", // WMI やレジストリから実際の名前を取得
                DedicatedVideoMemoryMB = 4096, // 実際の値を取得
                IsIntegrated = true, // 実際の判定ロジック
                VendorId = 0x8086, // Intel の例
                MaximumTexture2DDimension = 16384
            }
        ];
    }

    private uint GetMaxTextureSize()
    {
        // 実装: D3D11 デバイスから最大テクスチャサイズを取得
        return 16384; // プレースホルダー
    }

    private bool CheckHDRDisplaySupport()
    {
        // 実装: HDR対応ディスプレイの検出
        return false; // プレースホルダー
    }

    private string GetSupportedColorSpaces()
    {
        // 実装: サポートされている色空間の取得
        return "sRGB"; // プレースホルダー
    }

    private double GetWDDMVersion()
    {
        // 実装: レジストリからWDDMバージョンを取得
        return 2.0; // プレースホルダー
    }

    private bool CheckWARPSupport()
    {
        // 実装: WARP (Windows Advanced Rasterization Platform) サポート確認
        return true; // プレースホルダー
    }
}
