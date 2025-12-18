using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Baketa.Core.Abstractions.GPU;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.GPU;

/// <summary>
/// Windows環境でのGPU検出実装
/// WMI + DirectX APIを使用したハードウェア情報取得
/// [Issue #213] ディスクキャッシュ対応（次回起動時5秒短縮）
/// [Gemini Review] 非同期I/O化、競合制御、キャッシュ無効化API追加
/// </summary>
public sealed class WindowsGpuEnvironmentDetector : IGpuEnvironmentDetector, IDisposable
{
    private readonly ILogger<WindowsGpuEnvironmentDetector> _logger;
    private readonly string _cacheFilePath;
    private readonly SemaphoreSlim _detectionSemaphore = new(1, 1);
    private GpuEnvironmentInfo? _cachedEnvironment;
    private bool _cacheLoaded;
    private bool _disposed;

    // [Issue #213] キャッシュ有効期限（7日）
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromDays(7);

    public WindowsGpuEnvironmentDetector(ILogger<WindowsGpuEnvironmentDetector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // [Issue #213] キャッシュファイルパスを設定（AppData/Local/Baketa/gpu_cache.json）
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Baketa");
        Directory.CreateDirectory(appDataPath);
        _cacheFilePath = Path.Combine(appDataPath, "gpu_cache.json");

        // 注: 非同期キャッシュ読み込みはDetectEnvironmentAsync内で遅延実行
    }

    public async Task<GpuEnvironmentInfo> DetectEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        // [Gemini Review] 高速パス: キャッシュ済みの場合は即座に返却（ロック不要）
        var cached = Volatile.Read(ref _cachedEnvironment);
        if (cached != null)
        {
            _logger.LogDebug("キャッシュ済みGPU環境情報を返却: {GpuName}", cached.GpuName);
            return cached;
        }

        // [Gemini Review] SemaphoreSlimで競合制御（async対応）
        await _detectionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // ダブルチェックロッキング: セマフォ取得後に再確認
            cached = Volatile.Read(ref _cachedEnvironment);
            if (cached != null)
            {
                _logger.LogDebug("キャッシュ済みGPU環境情報を返却（競合解決後）: {GpuName}", cached.GpuName);
                return cached;
            }

            // [Gemini Review] 非同期でディスクキャッシュを読み込み（初回のみ）
            if (!_cacheLoaded)
            {
                await TryLoadCacheFromDiskAsync(cancellationToken).ConfigureAwait(false);
                _cacheLoaded = true;

                // ディスクキャッシュ読み込み成功時
                cached = Volatile.Read(ref _cachedEnvironment);
                if (cached != null)
                {
                    _logger.LogDebug("ディスクキャッシュからGPU環境情報を復元: {GpuName}", cached.GpuName);
                    return cached;
                }
            }

            _logger.LogInformation("GPU環境検出開始");

            var environment = await Task.Run(() => DetectGpuEnvironmentInternal(), cancellationToken).ConfigureAwait(false);

            Volatile.Write(ref _cachedEnvironment, environment);

            // [Issue #213] ディスクにキャッシュを保存（次回起動時の高速化）
            await SaveCacheToDiskAsync(environment, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("GPU環境検出完了: {GpuName}, CUDA:{SupportsCuda}, DirectML:{SupportsDirectML}, VRAM:{AvailableMemoryMB}MB",
                environment.GpuName, environment.SupportsCuda, environment.SupportsDirectML, environment.AvailableMemoryMB);

            return environment;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPU環境検出失敗");

            // フォールバック環境（CPU専用）を返却
            var fallbackEnvironment = CreateFallbackEnvironment();
            Volatile.Write(ref _cachedEnvironment, fallbackEnvironment);

            return fallbackEnvironment;
        }
        finally
        {
            _detectionSemaphore.Release();
        }
    }

    public GpuEnvironmentInfo? GetCachedEnvironment()
    {
        return Volatile.Read(ref _cachedEnvironment);
    }

    public async Task<GpuEnvironmentInfo> RefreshEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GPU環境情報強制更新");

        // [Gemini Review] SemaphoreSlimで競合制御
        await _detectionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _cachedEnvironment, null);
            _cacheLoaded = false; // 強制再読み込みのためフラグリセット
        }
        finally
        {
            _detectionSemaphore.Release();
        }

        return await DetectEnvironmentAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// [Gemini Review] キャッシュを無効化する
    /// </summary>
    /// <param name="reason">無効化の理由（ログ出力用）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async Task InvalidateCacheAsync(string reason, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Gemini Review] キャッシュ無効化: {Reason}", reason);

        await _detectionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _cachedEnvironment, null);
            _cacheLoaded = false;

            // ディスクキャッシュも削除
            if (File.Exists(_cacheFilePath))
            {
                try
                {
                    File.Delete(_cacheFilePath);
                    _logger.LogDebug("[Gemini Review] ディスクキャッシュを削除: {Path}", _cacheFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Gemini Review] ディスクキャッシュ削除失敗: {Path}", _cacheFilePath);
                }
            }
        }
        finally
        {
            _detectionSemaphore.Release();
        }
    }

    private GpuEnvironmentInfo DetectGpuEnvironmentInternal()
    {
        var gpuInfo = DetectPrimaryGpu();
        var directXLevel = DetectDirectXFeatureLevel();
        var (AvailableMemoryMB, MaxTexture2DDimension) = DetectGpuMemory(gpuInfo.IsIntegratedGpu);
        var capabilities = DetectGpuCapabilities(gpuInfo);

        return new GpuEnvironmentInfo
        {
            IsIntegratedGpu = gpuInfo.IsIntegratedGpu,
            IsDedicatedGpu = gpuInfo.IsDedicatedGpu,
            SupportsCuda = capabilities.SupportsCuda,
            SupportsOpenCL = capabilities.SupportsOpenCL,
            SupportsDirectML = capabilities.SupportsDirectML,
            SupportsOpenVINO = capabilities.SupportsOpenVINO,
            SupportsTensorRT = capabilities.SupportsTensorRT,
            AvailableMemoryMB = AvailableMemoryMB,
            MaximumTexture2DDimension = MaxTexture2DDimension,
            DirectXFeatureLevel = directXLevel,
            GpuName = gpuInfo.GpuName,
            ComputeCapability = capabilities.ComputeCapability,
            RecommendedProviders = DetermineRecommendedProviders(capabilities, gpuInfo)
        };
    }

    private (string GpuName, bool IsIntegratedGpu, bool IsDedicatedGpu) DetectPrimaryGpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController WHERE Availability = 3");
            using var results = searcher.Get();

            var gpus = new List<(string Name, uint AdapterRAM, string PNPDeviceID)>();

            foreach (ManagementObject gpu in results.Cast<ManagementObject>())
            {
                var name = gpu["Name"]?.ToString() ?? "Unknown GPU";
                var adapterRAM = Convert.ToUInt32(gpu["AdapterRAM"] ?? 0, CultureInfo.InvariantCulture);
                var pnpDeviceID = gpu["PNPDeviceID"]?.ToString() ?? "";

                gpus.Add((name, adapterRAM, pnpDeviceID));
            }

            // 専用GPU優先（VRAM容量で判定）
            var (Name, AdapterRAM, PNPDeviceID) = gpus.OrderByDescending(g => g.AdapterRAM).First();
            var isIntegrated = IsIntegratedGpu(Name, PNPDeviceID);

            return (Name, isIntegrated, !isIntegrated);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI GPU検出失敗");
            return ("Unknown GPU", false, false);
        }
    }

    private static bool IsIntegratedGpu(string gpuName, string pnpDeviceID)
    {
        string[] integratedIndicators =
        [
            "Intel", "UHD", "Iris", "HD Graphics",
            "AMD Radeon Graphics", "Vega",
            "Microsoft Basic"
        ];

        return integratedIndicators.Any(indicator =>
            gpuName.Contains(indicator, StringComparison.OrdinalIgnoreCase)) ||
               pnpDeviceID.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase); // Intel Vendor ID
    }

    private DirectXFeatureLevel DetectDirectXFeatureLevel()
    {
        try
        {
            // D3D11CreateDevice を使用してフィーチャーレベル検出
            var featureLevel = NativeMethods.GetDirectXFeatureLevel();
            _logger.LogDebug("DirectX Feature Level raw value: 0x{FeatureLevel:X}", featureLevel);

            // Issue #181: D3D_FEATURE_LEVEL 定数マッピング
            // https://docs.microsoft.com/en-us/windows/win32/api/d3dcommon/ne-d3dcommon-d3d_feature_level
            return featureLevel switch
            {
                0xc200 => DirectXFeatureLevel.D3D122, // D3D_FEATURE_LEVEL_12_2
                0xc100 => DirectXFeatureLevel.D3D122, // D3D_FEATURE_LEVEL_12_1 (RTX 4070対応)
                0xc000 => DirectXFeatureLevel.D3D121, // D3D_FEATURE_LEVEL_12_0
                0xb100 => DirectXFeatureLevel.D3D120, // D3D_FEATURE_LEVEL_11_1
                0xb000 => DirectXFeatureLevel.D3D111, // D3D_FEATURE_LEVEL_11_0
                0xa100 => DirectXFeatureLevel.D3D111, // D3D_FEATURE_LEVEL_10_1
                0xa000 => DirectXFeatureLevel.D3D110, // D3D_FEATURE_LEVEL_10_0
                _ when featureLevel >= 0xb000 => DirectXFeatureLevel.D3D111, // 11.0以上ならOK
                _ => DirectXFeatureLevel.Unknown
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DirectXフィーチャーレベル検出失敗");
            return DirectXFeatureLevel.D3D111; // Issue #181: DirectML最低要件を満たすフォールバック
        }
    }

    private (long AvailableMemoryMB, int MaxTexture2DDimension) DetectGpuMemory(bool isIntegratedGpu)
    {
        try
        {
            if (isIntegratedGpu)
            {
                // 統合GPU: システムRAMの1/4を利用可能とみなす
                var totalRamMB = GC.GetTotalMemory(false) / (1024 * 1024);
                var availableMemoryMB = Math.Max(1024, totalRamMB / 4); // 最低1GB
                return (availableMemoryMB, 8192); // 統合GPUは通常8Kテクスチャまで
            }
            else
            {
                // [Issue #218] 専用GPU: NVML優先、WMIフォールバック
                // WMIのAdapterRAMは32-bit制限（最大4095MB）があるため、
                // NVIDIA GPUはNVMLで正確なVRAM容量を取得

                // 1. NVML経由でVRAM取得を試行（NVIDIA GPU専用、64-bit正確値）
                var nvmlVramMB = TryGetNvmlVramMB();
                if (nvmlVramMB > 0)
                {
                    _logger.LogInformation("[NVML] VRAM容量取得成功: {VramMB}MB", nvmlVramMB);
                    var maxTexture = nvmlVramMB >= 8192 ? 16384 : 8192;
                    return (nvmlVramMB, maxTexture);
                }

                // 2. WMIフォールバック（32-bit制限あり、非NVIDIA GPUまたはNVML失敗時）
                using var searcher = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController WHERE Availability = 3");
                using var results = searcher.Get();

                var maxVram = results.Cast<ManagementObject>()
                    .Select(gpu => Convert.ToUInt64(gpu["AdapterRAM"] ?? 0, CultureInfo.InvariantCulture))
                    .Max();

                var availableMemoryMB = maxVram > 0 ? (long)(maxVram / (1024 * 1024)) : 4096; // フォールバック4GB

                // 32-bit制限警告（4095MBでカンストしている場合）
                if (availableMemoryMB == 4095)
                {
                    _logger.LogWarning("[WMI] VRAM容量が4095MBで上限に達しています。実際のVRAM容量はこれより大きい可能性があります（WMI 32-bit制限）");
                }

                var maxTextureDim = availableMemoryMB >= 8192 ? 16384 : 8192;
                return (availableMemoryMB, maxTextureDim);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPU メモリ情報取得失敗");
            return (4096, 8192); // 安全なフォールバック
        }
    }

    /// <summary>
    /// [Issue #218] NVML経由でNVIDIA GPUのVRAM容量を取得（64-bit正確値）
    /// WMIのAdapterRAMは32-bit制限があるため、4GB超のVRAMを持つGPUでは不正確
    /// </summary>
    /// <returns>VRAM容量（MB）、失敗時は0</returns>
    private long TryGetNvmlVramMB()
    {
        IntPtr nvmlLib = IntPtr.Zero;
        try
        {
            _logger.LogInformation("[NVML] VRAM検出を開始");

            // NVML DLL検索パス（NVIDIA GPUドライバに付属）
            var nvmlPaths = new[]
            {
                "nvml.dll",
                @"C:\Windows\System32\nvml.dll",
                @"C:\Program Files\NVIDIA Corporation\NVSMI\nvml.dll",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll")
            };

            foreach (var path in nvmlPaths)
            {
                _logger.LogDebug("[NVML] DLL検索: {Path}", path);
                nvmlLib = NvmlNativeMethods.LoadLibrary(path);
                if (nvmlLib != IntPtr.Zero)
                {
                    _logger.LogInformation("[NVML] ライブラリロード成功: {Path}", path);
                    break;
                }
            }

            if (nvmlLib == IntPtr.Zero)
            {
                _logger.LogWarning("[NVML] ライブラリが見つかりません - WMIフォールバックを使用（4GB制限あり）");
                return 0;
            }

            // 必要な関数のアドレスを取得（_v2サフィックスも試行）
            var initAddr = NvmlNativeMethods.GetProcAddress(nvmlLib, "nvmlInit_v2");
            if (initAddr == IntPtr.Zero)
                initAddr = NvmlNativeMethods.GetProcAddress(nvmlLib, "nvmlInit");

            var shutdownAddr = NvmlNativeMethods.GetProcAddress(nvmlLib, "nvmlShutdown");

            var getCountAddr = NvmlNativeMethods.GetProcAddress(nvmlLib, "nvmlDeviceGetCount_v2");
            if (getCountAddr == IntPtr.Zero)
                getCountAddr = NvmlNativeMethods.GetProcAddress(nvmlLib, "nvmlDeviceGetCount");

            var getHandleAddr = NvmlNativeMethods.GetProcAddress(nvmlLib, "nvmlDeviceGetHandleByIndex_v2");
            if (getHandleAddr == IntPtr.Zero)
                getHandleAddr = NvmlNativeMethods.GetProcAddress(nvmlLib, "nvmlDeviceGetHandleByIndex");

            var getMemoryAddr = NvmlNativeMethods.GetProcAddress(nvmlLib, "nvmlDeviceGetMemoryInfo");

            _logger.LogDebug("[NVML] 関数アドレス: init={Init}, shutdown={Shutdown}, count={Count}, handle={Handle}, memory={Memory}",
                initAddr != IntPtr.Zero, shutdownAddr != IntPtr.Zero, getCountAddr != IntPtr.Zero,
                getHandleAddr != IntPtr.Zero, getMemoryAddr != IntPtr.Zero);

            if (initAddr == IntPtr.Zero || shutdownAddr == IntPtr.Zero ||
                getCountAddr == IntPtr.Zero || getHandleAddr == IntPtr.Zero || getMemoryAddr == IntPtr.Zero)
            {
                _logger.LogWarning("[NVML] 関数アドレス取得失敗 - WMIフォールバックを使用");
                return 0;
            }

            // デリゲート作成
            var nvmlInit = Marshal.GetDelegateForFunctionPointer<NvmlNativeMethods.NvmlInitDelegate>(initAddr);
            var nvmlShutdown = Marshal.GetDelegateForFunctionPointer<NvmlNativeMethods.NvmlShutdownDelegate>(shutdownAddr);
            var nvmlDeviceGetCount = Marshal.GetDelegateForFunctionPointer<NvmlNativeMethods.NvmlDeviceGetCountDelegate>(getCountAddr);
            var nvmlDeviceGetHandleByIndex = Marshal.GetDelegateForFunctionPointer<NvmlNativeMethods.NvmlDeviceGetHandleByIndexDelegate>(getHandleAddr);
            var nvmlDeviceGetMemoryInfo = Marshal.GetDelegateForFunctionPointer<NvmlNativeMethods.NvmlDeviceGetMemoryInfoDelegate>(getMemoryAddr);

            // NVML初期化
            var initResult = nvmlInit();
            if (initResult != 0)
            {
                _logger.LogWarning("[NVML] 初期化失敗 (エラーコード: {ErrorCode}) - WMIフォールバックを使用", initResult);
                return 0;
            }
            _logger.LogInformation("[NVML] 初期化成功");

            try
            {
                // デバイス数取得
                var countResult = nvmlDeviceGetCount(out var deviceCount);
                if (countResult != 0 || deviceCount == 0)
                {
                    _logger.LogWarning("[NVML] デバイス数取得失敗 (結果: {Result}, 数: {Count})", countResult, deviceCount);
                    return 0;
                }
                _logger.LogInformation("[NVML] デバイス数: {DeviceCount}", deviceCount);

                // 最大VRAM容量を持つデバイスを検索
                long maxVramMB = 0;
                for (uint i = 0; i < deviceCount; i++)
                {
                    var handleResult = nvmlDeviceGetHandleByIndex(i, out var deviceHandle);
                    if (handleResult != 0)
                    {
                        _logger.LogDebug("[NVML] デバイス{Index}のハンドル取得失敗: {Result}", i, handleResult);
                        continue;
                    }

                    var memResult = nvmlDeviceGetMemoryInfo(deviceHandle, out var memoryInfo);
                    if (memResult != 0)
                    {
                        _logger.LogDebug("[NVML] デバイス{Index}のメモリ情報取得失敗: {Result}", i, memResult);
                        continue;
                    }

                    var vramMB = (long)(memoryInfo.Total / (1024 * 1024));
                    _logger.LogInformation("[NVML] デバイス{Index}: VRAM={VramMB}MB (Total={TotalBytes}bytes)", i, vramMB, memoryInfo.Total);
                    if (vramMB > maxVramMB)
                        maxVramMB = vramMB;
                }

                if (maxVramMB > 0)
                {
                    _logger.LogInformation("[NVML] 最大VRAM容量: {MaxVramMB}MB", maxVramMB);
                }
                return maxVramMB;
            }
            finally
            {
                nvmlShutdown();
                _logger.LogDebug("[NVML] シャットダウン完了");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[NVML] VRAM取得中にエラー発生 - WMIフォールバックを使用");
            return 0;
        }
        finally
        {
            if (nvmlLib != IntPtr.Zero)
                NvmlNativeMethods.FreeLibrary(nvmlLib);
        }
    }

    private (bool SupportsCuda, bool SupportsOpenCL, bool SupportsDirectML, bool SupportsOpenVINO, bool SupportsTensorRT, ComputeCapability ComputeCapability) DetectGpuCapabilities(
        (string GpuName, bool IsIntegratedGpu, bool IsDedicatedGpu) gpuInfo)
    {
        var capabilities = (
            SupportsCuda: false,
            SupportsOpenCL: false,
            SupportsDirectML: true, // Windows 10以降は基本的に対応
            SupportsOpenVINO: false,
            SupportsTensorRT: false,
            ComputeCapability: ComputeCapability.Unknown
        );

        var gpuName = gpuInfo.GpuName.ToLowerInvariant();

        // NVIDIA GPU検出
        if (gpuName.Contains("nvidia") || gpuName.Contains("geforce") || gpuName.Contains("rtx") || gpuName.Contains("gtx"))
        {
            capabilities.SupportsCuda = true;
            capabilities.SupportsOpenCL = true;
            capabilities.ComputeCapability = DetectNvidiaComputeCapability(gpuName);

            // RTXシリーズはTensorRT対応
            if (gpuName.Contains("rtx"))
            {
                capabilities.SupportsTensorRT = true;
            }
        }
        // Intel GPU検出  
        else if (gpuName.Contains("intel") || gpuName.Contains("uhd") || gpuName.Contains("iris"))
        {
            capabilities.SupportsOpenCL = true;
            capabilities.SupportsOpenVINO = true; // Intel最適化
        }
        // AMD GPU検出
        else if (gpuName.Contains("amd") || gpuName.Contains("radeon"))
        {
            capabilities.SupportsOpenCL = true;
        }

        return capabilities;
    }

    private static ComputeCapability DetectNvidiaComputeCapability(string gpuName)
    {
        // RTX 4000シリーズ (Ada Lovelace) - RTX4070等
        if (gpuName.Contains("rtx 40") || gpuName.Contains("rtx40"))
            return ComputeCapability.Compute89;

        // RTX 3000シリーズ (Ampere)
        if (gpuName.Contains("rtx 30") || gpuName.Contains("rtx30"))
            return ComputeCapability.Compute86;

        // RTX 2000シリーズ (Turing)
        if (gpuName.Contains("rtx 20") || gpuName.Contains("rtx20"))
            return ComputeCapability.Compute75;

        // GTX 1000シリーズ (Pascal)  
        if (gpuName.Contains("gtx 16") || gpuName.Contains("gtx16") ||
            gpuName.Contains("gtx 10") || gpuName.Contains("gtx10"))
            return ComputeCapability.Compute61;

        // GTX 900シリーズ (Maxwell)
        if (gpuName.Contains("gtx 9") || gpuName.Contains("gtx9"))
            return ComputeCapability.Compute50;

        return ComputeCapability.Unknown;
    }

    private static IReadOnlyList<ExecutionProvider> DetermineRecommendedProviders(
        (bool SupportsCuda, bool SupportsOpenCL, bool SupportsDirectML, bool SupportsOpenVINO, bool SupportsTensorRT, ComputeCapability ComputeCapability) capabilities,
        (string GpuName, bool IsIntegratedGpu, bool IsDedicatedGpu) gpuInfo)
    {
        var providers = new List<ExecutionProvider>();

        if (gpuInfo.IsDedicatedGpu)
        {
            // RTX専用GPU - 最高性能順
            if (capabilities.SupportsTensorRT)
                providers.Add(ExecutionProvider.TensorRT);

            if (capabilities.SupportsCuda)
                providers.Add(ExecutionProvider.CUDA);
        }

        if (gpuInfo.IsIntegratedGpu)
        {
            // Intel統合GPU最適化
            if (capabilities.SupportsOpenVINO)
                providers.Add(ExecutionProvider.OpenVINO);
        }

        // 共通プロバイダー
        if (capabilities.SupportsDirectML)
            providers.Add(ExecutionProvider.DirectML);

        if (capabilities.SupportsOpenCL)
            providers.Add(ExecutionProvider.OpenCL);

        // 最終フォールバック
        providers.Add(ExecutionProvider.CPU);

        return providers.AsReadOnly();
    }

    private static GpuEnvironmentInfo CreateFallbackEnvironment()
    {
        return new GpuEnvironmentInfo
        {
            IsIntegratedGpu = false,
            IsDedicatedGpu = false,
            SupportsCuda = false,
            SupportsOpenCL = false,
            SupportsDirectML = false,
            SupportsOpenVINO = false,
            SupportsTensorRT = false,
            AvailableMemoryMB = 0,
            MaximumTexture2DDimension = 4096,
            DirectXFeatureLevel = DirectXFeatureLevel.Unknown,
            GpuName = "Fallback CPU",
            ComputeCapability = ComputeCapability.Unknown,
            RecommendedProviders = [ExecutionProvider.CPU]
        };
    }

    /// <summary>
    /// [Issue #213] ディスクからキャッシュを読み込む（非同期I/O）
    /// [Gemini Review] FileStreamを使用した非同期読み込みに変更
    /// </summary>
    private async Task TryLoadCacheFromDiskAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
            {
                _logger.LogDebug("[Issue #213] GPU キャッシュファイルが存在しません: {Path}", _cacheFilePath);
                return;
            }

            await using var fileStream = new FileStream(
                _cacheFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            var cacheData = await JsonSerializer.DeserializeAsync<GpuCacheData>(
                fileStream,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (cacheData == null)
            {
                _logger.LogDebug("[Issue #213] GPU キャッシュデータのデシリアライズに失敗");
                return;
            }

            // キャッシュの有効期限をチェック
            if (DateTime.UtcNow - cacheData.CachedAt > CacheExpiration)
            {
                _logger.LogInformation("[Issue #213] GPU キャッシュが期限切れ（{Days}日経過）、再検出します",
                    (DateTime.UtcNow - cacheData.CachedAt).Days);
                File.Delete(_cacheFilePath);
                return;
            }

            Volatile.Write(ref _cachedEnvironment, cacheData.Environment);

            _logger.LogInformation("[Issue #213] GPU キャッシュをディスクから読み込み: {GpuName}, CUDA:{SupportsCuda} (キャッシュ日時: {CachedAt})",
                cacheData.Environment?.GpuName,
                cacheData.Environment?.SupportsCuda,
                cacheData.CachedAt.ToLocalTime());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #213] GPU キャッシュ読み込み失敗");
        }
    }

    /// <summary>
    /// [Issue #213] キャッシュをディスクに保存する（非同期I/O）
    /// [Gemini Review] FileStreamを使用した非同期書き込みに変更
    /// </summary>
    private async Task SaveCacheToDiskAsync(GpuEnvironmentInfo environment, CancellationToken cancellationToken)
    {
        try
        {
            var cacheData = new GpuCacheData
            {
                Environment = environment,
                CachedAt = DateTime.UtcNow
            };

            await using var fileStream = new FileStream(
                _cacheFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);

            await JsonSerializer.SerializeAsync(
                fileStream,
                cacheData,
                new JsonSerializerOptions { WriteIndented = true },
                cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("[Issue #213] GPU キャッシュをディスクに保存: {Path}", _cacheFilePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #213] GPU キャッシュ保存失敗");
        }
    }

    /// <summary>
    /// [Issue #213] GPU キャッシュデータ（ディスク永続化用）
    /// </summary>
    private sealed class GpuCacheData
    {
        public GpuEnvironmentInfo? Environment { get; set; }
        public DateTime CachedAt { get; set; }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _detectionSemaphore.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Windows Native API呼び出し
/// </summary>
internal static class NativeMethods
{
    [DllImport("d3d11.dll", SetLastError = true)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out IntPtr ppDevice,
        out uint pFeatureLevel,
        out IntPtr ppImmediateContext);

    internal static uint GetDirectXFeatureLevel()
    {
        try
        {
            // Issue #181 Fix: driverType=1 (D3D_DRIVER_TYPE_HARDWARE) を使用
            // driverType=0 (UNKNOWN) だとpAdapter=null時に失敗する
            var hr = D3D11CreateDevice(
                IntPtr.Zero,
                1,  // D3D_DRIVER_TYPE_HARDWARE
                IntPtr.Zero,
                0,
                IntPtr.Zero,
                0,
                7,  // D3D11_SDK_VERSION
                out var device,
                out var featureLevel,
                out var context);

            if (hr >= 0)
            {
                // COM リソース解放
                if (device != IntPtr.Zero) Marshal.Release(device);
                if (context != IntPtr.Zero) Marshal.Release(context);

                return featureLevel;
            }
        }
        catch
        {
            // 失敗時は安全な値を返す
        }

        // Issue #181: フォールバックをD3D11.1に変更（DirectML最低要件）
        return 0xb000; // D3D_FEATURE_LEVEL_11_0
    }
}

/// <summary>
/// [Issue #218] NVML Native API呼び出し
/// NVIDIA GPUのVRAM容量を正確に取得するために使用
/// </summary>
internal static class NvmlNativeMethods
{
    // Windows DLL読み込み関数
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    internal static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    // NVML関数デリゲート
    internal delegate int NvmlInitDelegate();
    internal delegate int NvmlShutdownDelegate();
    internal delegate int NvmlDeviceGetCountDelegate(out uint deviceCount);
    internal delegate int NvmlDeviceGetHandleByIndexDelegate(uint index, out IntPtr device);
    internal delegate int NvmlDeviceGetMemoryInfoDelegate(IntPtr device, out NvmlMemory memory);

    /// <summary>
    /// NVML メモリ情報構造体（64-bit対応）
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NvmlMemory
    {
        public ulong Total;   // 総VRAM容量 (bytes)
        public ulong Free;    // 空きVRAM容量 (bytes)
        public ulong Used;    // 使用中VRAM容量 (bytes)
    }
}
