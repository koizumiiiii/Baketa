using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Models.Capture;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Exceptions.Capture;
using Baketa.Core.Settings;
using System.Runtime.InteropServices;

namespace Baketa.Infrastructure.Platform.Windows.GPU;

/// <summary>
/// GPU環境を検出する実装クラス
/// </summary>
public class GPUEnvironmentDetector : ICaptureEnvironmentDetector
{
    private readonly ILogger<GPUEnvironmentDetector> _logger;
    private readonly GpuSettings _gpuSettings;
    private GpuEnvironmentInfo? _cachedEnvironment;
    private readonly object _cacheLock = new();
    
    // Windows API とDirectX関連の定数
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    
    public GPUEnvironmentDetector(
        ILogger<GPUEnvironmentDetector> logger,
        IOptions<GpuSettings> gpuSettings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gpuSettings = gpuSettings?.Value ?? throw new ArgumentNullException(nameof(gpuSettings));
    }

    public async Task<GpuEnvironmentInfo> DetectEnvironmentAsync()
    {
        _logger.LogInformation("GPU環境検出を開始");
        
        try
        {
            // 1. DirectXサポートレベル確認
            var hasDirectX11 = await Task.Run(() => CheckDirectX11Available()).ConfigureAwait(false);
            var featureLevel = await Task.Run(() => GetDirectXFeatureLevel()).ConfigureAwait(false);
            
            // 2. 利用可能なGPUアダプター情報取得
            var (Name, MemoryMB, _) = await Task.Run(() => GetPrimaryGpuInfo()).ConfigureAwait(false);
            
            // 3. GPU種別判定（統合/専用） - 設定ファイルベースの判定を使用
            var isIntegrated = DetermineIfIntegratedGpu(Name);
            var isDedicated = !isIntegrated;
            
            // 4. テクスチャサイズ制限確認
            var maxTextureSize = await Task.Run(() => GetMaxTextureSize()).ConfigureAwait(false);
            
            // 5. HDR・色空間サポート確認
            var hasHdrSupport = await Task.Run(() => CheckHDRDisplaySupport()).ConfigureAwait(false);
            
            // 6. WDDM バージョン確認
            var wddmVersion = GetWDDMVersion();
            
            // 7. ソフトウェアレンダリング対応確認
            var supportsWarp = CheckWARPSupport();
            
            // GPU種別に基づく推奨プロバイダーの決定
            var recommendedProviders = DetermineRecommendedProviders(isIntegrated, isDedicated, hasDirectX11);
            
            // GPU環境情報を構築
            var info = new GpuEnvironmentInfo
            {
                GpuName = Name,
                GpuDeviceId = 0,
                IsIntegratedGpu = isIntegrated,
                IsDedicatedGpu = isDedicated,
                SupportsCuda = isDedicated && Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase),
                SupportsOpenCL = true, // 多くの現代GPUで対応
                SupportsDirectML = true, // Windows 10+で対応
                SupportsOpenVINO = Name.Contains("Intel", StringComparison.OrdinalIgnoreCase),
                SupportsTensorRT = isDedicated && Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase),
                AvailableMemoryMB = MemoryMB,
                MaximumTexture2DDimension = maxTextureSize,
                DirectXFeatureLevel = featureLevel,
                ComputeCapability = DetermineComputeCapability(Name),
                RecommendedProviders = recommendedProviders
            };
            
            // 結果をキャッシュ
            lock (_cacheLock)
            {
                _cachedEnvironment = info;
            }
            
            _logger.LogInformation("GPU環境検出完了: GPU={GpuName}, 統合={IsIntegrated}, 専用={IsDedicated}", 
                info.GpuName, info.IsIntegratedGpu, info.IsDedicatedGpu);
                
            return info;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPU環境検出中にエラーが発生");
            throw new GPUEnvironmentDetectionException("GPU環境検出に失敗しました", ex);
        }
    }

    public GpuEnvironmentInfo? GetCachedEnvironmentInfo()
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

    public async Task<string?> GetAdapterDetailsAsync(int adapterIndex)
    {
        try
        {
            // この実装は簡易版。実際のDXGI APIを使用した実装が必要
            var environment = await DetectEnvironmentAsync().ConfigureAwait(false);
            if (adapterIndex == 0 && !string.IsNullOrEmpty(environment.GpuName))
            {
                return $"GPU: {environment.GpuName}, Memory: {environment.AvailableMemoryMB}MB, " +
                       $"Integrated: {environment.IsIntegratedGpu}, Dedicated: {environment.IsDedicatedGpu}";
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
            return (uint)environment.MaximumTexture2DDimension;
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
            // HDRサポートは現在の実装では常にfalseを返す（実装予定）
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HDRサポート確認に失敗");
            return false;
        }
    }

    private IReadOnlyList<ExecutionProvider> DetermineRecommendedProviders(bool isIntegrated, bool isDedicated, bool hasDirectX11)
    {
        var providers = new List<ExecutionProvider>();
        
        if (isDedicated)
        {
            // 専用GPU環境の場合の優先順位
            providers.Add(ExecutionProvider.CUDA);
            providers.Add(ExecutionProvider.TensorRT);
            providers.Add(ExecutionProvider.DirectML);
        }
        else if (isIntegrated)
        {
            // 統合GPU環境の場合の優先順位
            providers.Add(ExecutionProvider.DirectML);
            providers.Add(ExecutionProvider.OpenVINO);
            providers.Add(ExecutionProvider.OpenCL);
        }
        
        // 共通のフォールバック
        providers.Add(ExecutionProvider.CPU);
        
        return providers.AsReadOnly();
    }
    
    private ComputeCapability DetermineComputeCapability(string gpuName)
    {
        // GPU名からCompute Capabilityを推定（簡易実装）
        if (gpuName.Contains("RTX 40", StringComparison.OrdinalIgnoreCase))
            return ComputeCapability.Compute89;
        if (gpuName.Contains("RTX 30", StringComparison.OrdinalIgnoreCase))
            return ComputeCapability.Compute86;
        if (gpuName.Contains("RTX 20", StringComparison.OrdinalIgnoreCase))
            return ComputeCapability.Compute75;
        if (gpuName.Contains("GTX 10", StringComparison.OrdinalIgnoreCase))
            return ComputeCapability.Compute61;
        if (gpuName.Contains("GTX 9", StringComparison.OrdinalIgnoreCase))
            return ComputeCapability.Compute50;
        if (gpuName.Contains("GTX 7", StringComparison.OrdinalIgnoreCase))
            return ComputeCapability.Compute35;
        if (gpuName.Contains("GTX 6", StringComparison.OrdinalIgnoreCase))
            return ComputeCapability.Compute30;
            
        return ComputeCapability.Unknown;
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
        return DirectXFeatureLevel.D3D111; // プレースホルダー
    }

    private (string Name, long MemoryMB, bool IsIntegrated) GetPrimaryGpuInfo()
    {
        // 実装: DXGI を使用してアダプター情報を取得
        return (
            Name: "検出されたGPU", // WMI やレジストリから実際の名前を取得
            MemoryMB: 4096, // 実際の値を取得
            IsIntegrated: true // 実際の判定ロジック
        );
    }
    
    private bool DetermineIfIntegratedGpu(string gpuName)
    {
        _logger.LogDebug("GPU種別判定開始: {GpuName}", gpuName);
        
        // まず専用GPUキーワードをチェック
        foreach (var keyword in _gpuSettings.DedicatedGpuKeywords)
        {
            if (gpuName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("専用GPU検出: {GpuName} (キーワード: '{Keyword}')", gpuName, keyword);
                return false; // 専用GPU
            }
        }
        
        // 次に統合GPUキーワードをチェック
        foreach (var keyword in _gpuSettings.IntegratedGpuKeywords)
        {
            if (gpuName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("統合GPU検出: {GpuName} (キーワード: '{Keyword}')", gpuName, keyword);
                return true; // 統合GPU
            }
        }
        
        // 一致するキーワードがない場合はフォールバック設定に従う
        var fallbackResult = !_gpuSettings.FallbackToDedicated; // FallbackToDedicated=trueなら専用GPU扱い（false返却）
        _logger.LogWarning("未知のGPU: {GpuName} - フォールバック設定により{GpuType}として処理", 
            gpuName, fallbackResult ? "統合GPU" : "専用GPU");
        
        return fallbackResult;
    }

    private int GetMaxTextureSize()
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
