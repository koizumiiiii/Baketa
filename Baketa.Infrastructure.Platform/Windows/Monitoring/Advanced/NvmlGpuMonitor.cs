using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.GPU;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Monitoring.Advanced;

/// <summary>
/// NVIDIA Management Library (NVML) API を使用した高度なGPU監視
/// RTX 4070等のNVIDIA GPU専用の詳細監視機能を提供
/// </summary>
public sealed class NvmlGpuMonitor : IDisposable
{
    private readonly ILogger<NvmlGpuMonitor> _logger;
    private bool _isInitialized;
    private bool _isNvmlAvailable;
    private readonly object _initLock = new();

    // NVML関数のP/Invokeデリゲート
    private delegate int NvmlInitDelegate();
    private delegate int NvmlShutdownDelegate();
    private delegate int NvmlDeviceGetCountDelegate(out uint deviceCount);
    private delegate int NvmlDeviceGetHandleByIndexDelegate(uint index, out IntPtr device);
    private delegate int NvmlDeviceGetNameDelegate(IntPtr device, byte[] name, uint length);
    private delegate int NvmlDeviceGetUtilizationRatesDelegate(IntPtr device, out NvmlUtilization utilization);
    private delegate int NvmlDeviceGetMemoryInfoDelegate(IntPtr device, out NvmlMemory memory);
    private delegate int NvmlDeviceGetTemperatureDelegate(IntPtr device, int sensorType, out uint temperature);
    private delegate int NvmlDeviceGetPowerUsageDelegate(IntPtr device, out uint power);

    // NVML関数のデリゲートインスタンス
    private NvmlInitDelegate? _nvmlInit;
    private NvmlShutdownDelegate? _nvmlShutdown;
    private NvmlDeviceGetCountDelegate? _nvmlDeviceGetCount;
    private NvmlDeviceGetHandleByIndexDelegate? _nvmlDeviceGetHandleByIndex;
    private NvmlDeviceGetNameDelegate? _nvmlDeviceGetName;
    private NvmlDeviceGetUtilizationRatesDelegate? _nvmlDeviceGetUtilizationRates;
    private NvmlDeviceGetMemoryInfoDelegate? _nvmlDeviceGetMemoryInfo;
    private NvmlDeviceGetTemperatureDelegate? _nvmlDeviceGetTemperature;
    private NvmlDeviceGetPowerUsageDelegate? _nvmlDeviceGetPowerUsage;

    private IntPtr _nvmlLibraryHandle = IntPtr.Zero;
    private readonly List<NvmlDeviceInfo> _detectedDevices = [];

    // NVML構造体
    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint gpu;      // GPU使用率 (%)
        public uint memory;   // メモリ使用率 (%)
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong total;   // 総VRAM容量 (bytes)
        public ulong free;    // 空きVRAM容量 (bytes)
        public ulong used;    // 使用中VRAM容量 (bytes)
    }

    private record NvmlDeviceInfo(
        IntPtr Handle,
        string Name,
        uint Index);

    // Windows DLL読み込み関数
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    public NvmlGpuMonitor(ILogger<NvmlGpuMonitor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// NVML GPU監視システムを初期化
    /// </summary>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return _isNvmlAvailable;

        // lock内でのawait問題を解決するため、初期化ロジックを分離
        bool initializationResult = false;
        lock (_initLock)
        {
            if (_isInitialized)
                return _isNvmlAvailable;

            // 同期処理のみをlock内で実行
            try
            {
                _logger.LogInformation("🔧 [NVML] GPU監視システム初期化開始");

                // NVML DLLの動的ロード試行（複数パス対応）
                var nvmlPaths = new[]
                {
                    "nvml.dll",                    // システムパス
                    @"C:\Program Files\NVIDIA Corporation\NVSMI\nvml.dll",  // 標準パス
                    @"C:\Windows\System32\nvml.dll"  // システム32
                };

                foreach (var path in nvmlPaths)
                {
                    _nvmlLibraryHandle = LoadLibrary(path);
                    if (_nvmlLibraryHandle != IntPtr.Zero)
                    {
                        _logger.LogInformation("✅ [NVML] ライブラリロード成功: {Path}", path);
                        break;
                    }
                }

                if (_nvmlLibraryHandle == IntPtr.Zero)
                {
                    _logger.LogWarning("⚠️ [NVML] ライブラリが見つかりません - Windows API フォールバックを使用");
                    _isNvmlAvailable = false;
                    _isInitialized = true;
                    return false;
                }

                // NVML関数のアドレス取得とデリゲート設定
                if (!LoadNvmlFunctions())
                {
                    _logger.LogWarning("⚠️ [NVML] 関数ロードに失敗 - フォールバックを使用");
                    _isNvmlAvailable = false;
                    _isInitialized = true;
                    return false;
                }

                // NVML初期化
                var result = _nvmlInit!();
                if (result != 0) // NVML_SUCCESS = 0
                {
                    _logger.LogWarning("⚠️ [NVML] 初期化失敗 (エラーコード: {ErrorCode}) - フォールバックを使用", result);
                    _isNvmlAvailable = false;
                    _isInitialized = true;
                    return false;
                }

                // 初期化成功のマーク（GPU検出は非同期なのでlock外で実行）
                initializationResult = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 [NVML] GPU監視システム初期化エラー");
                _isNvmlAvailable = false;
                _isInitialized = true;
                return false;
            }
        }

        // lock外でGPU検出の非同期処理を実行
        if (initializationResult)
        {
            try
            {
                // GPU デバイス検出
                await DetectGpuDevicesAsync(cancellationToken).ConfigureAwait(false);

                _isNvmlAvailable = true;
                _isInitialized = true;

                _logger.LogInformation("✅ [NVML] GPU監視システム初期化完了 - 検出デバイス数: {DeviceCount}", _detectedDevices.Count);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 [NVML] GPU デバイス検出エラー");
                _isNvmlAvailable = false;
                _isInitialized = true;
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// NVML関数をロード
    /// </summary>
    private bool LoadNvmlFunctions()
    {
        try
        {
            var functions = new Dictionary<string, Action<IntPtr>>
            {
                ["nvmlInit"] = addr => _nvmlInit = Marshal.GetDelegateForFunctionPointer<NvmlInitDelegate>(addr),
                ["nvmlShutdown"] = addr => _nvmlShutdown = Marshal.GetDelegateForFunctionPointer<NvmlShutdownDelegate>(addr),
                ["nvmlDeviceGetCount"] = addr => _nvmlDeviceGetCount = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetCountDelegate>(addr),
                ["nvmlDeviceGetHandleByIndex"] = addr => _nvmlDeviceGetHandleByIndex = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetHandleByIndexDelegate>(addr),
                ["nvmlDeviceGetName"] = addr => _nvmlDeviceGetName = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetNameDelegate>(addr),
                ["nvmlDeviceGetUtilizationRates"] = addr => _nvmlDeviceGetUtilizationRates = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetUtilizationRatesDelegate>(addr),
                ["nvmlDeviceGetMemoryInfo"] = addr => _nvmlDeviceGetMemoryInfo = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetMemoryInfoDelegate>(addr),
                ["nvmlDeviceGetTemperature"] = addr => _nvmlDeviceGetTemperature = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetTemperatureDelegate>(addr),
                ["nvmlDeviceGetPowerUsage"] = addr => _nvmlDeviceGetPowerUsage = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetPowerUsageDelegate>(addr)
            };

            foreach (var (functionName, setter) in functions)
            {
                var functionAddress = GetProcAddress(_nvmlLibraryHandle, functionName);
                if (functionAddress == IntPtr.Zero)
                {
                    _logger.LogWarning("⚠️ [NVML] 関数が見つかりません: {FunctionName}", functionName);
                    return false;
                }
                setter(functionAddress);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 [NVML] 関数ロードエラー");
            return false;
        }
    }

    /// <summary>
    /// GPU デバイスを検出
    /// </summary>
    private async Task DetectGpuDevicesAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                if (_nvmlDeviceGetCount!(out var deviceCount) != 0)
                {
                    _logger.LogWarning("⚠️ [NVML] デバイス数取得失敗");
                    return;
                }

                _logger.LogInformation("🔍 [NVML] GPU検出開始 - デバイス数: {DeviceCount}", deviceCount);

                for (uint i = 0; i < deviceCount; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (_nvmlDeviceGetHandleByIndex!(i, out var deviceHandle) == 0)
                    {
                        var nameBuffer = new byte[256];
                        if (_nvmlDeviceGetName!(deviceHandle, nameBuffer, 256) == 0)
                        {
                            var deviceName = System.Text.Encoding.UTF8.GetString(nameBuffer).TrimEnd('\0');
                            var deviceInfo = new NvmlDeviceInfo(deviceHandle, deviceName, i);
                            _detectedDevices.Add(deviceInfo);

                            _logger.LogInformation("✅ [NVML] GPU検出成功: {Index} - {Name}", i, deviceName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 [NVML] GPU検出エラー");
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 詳細なGPU使用状況を取得（NVML使用）
    /// </summary>
    public async Task<DetailedGpuMetrics?> GetDetailedGpuMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isNvmlAvailable || !_detectedDevices.Any())
            return null;

        return await Task.Run<DetailedGpuMetrics?>(() =>
        {
            try
            {
                var primaryDevice = _detectedDevices[0]; // プライマリGPU

                // GPU使用率とメモリ使用率
                if (_nvmlDeviceGetUtilizationRates!(primaryDevice.Handle, out var utilization) != 0)
                {
                    _logger.LogWarning("⚠️ [NVML] GPU使用率取得失敗");
                    return null;
                }

                // メモリ情報
                if (_nvmlDeviceGetMemoryInfo!(primaryDevice.Handle, out var memory) != 0)
                {
                    _logger.LogWarning("⚠️ [NVML] GPU メモリ情報取得失敗");
                    return null;
                }

                // 温度（オプション）
                var temperature = 0u;
                _nvmlDeviceGetTemperature?.Invoke(primaryDevice.Handle, 0, out temperature);

                // 電力使用量（オプション）
                var powerUsage = 0u;
                _nvmlDeviceGetPowerUsage?.Invoke(primaryDevice.Handle, out powerUsage);

                return new DetailedGpuMetrics
                {
                    GpuUtilizationPercent = utilization.gpu,
                    MemoryUtilizationPercent = utilization.memory,
                    TotalMemoryMB = memory.total / (1024 * 1024),
                    UsedMemoryMB = memory.used / (1024 * 1024),
                    FreeMemoryMB = memory.free / (1024 * 1024),
                    TemperatureCelsius = temperature,
                    PowerUsageWatts = powerUsage,
                    DeviceName = primaryDevice.Name,
                    DeviceIndex = (int)primaryDevice.Index,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 [NVML] GPU メトリクス取得エラー");
                return null;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// NVML が利用可能かどうか
    /// </summary>
    public bool IsNvmlAvailable => _isNvmlAvailable;

    /// <summary>
    /// 検出されたGPUデバイス数
    /// </summary>
    public int DetectedDeviceCount => _detectedDevices.Count;

    public void Dispose()
    {
        try
        {
            _nvmlShutdown?.Invoke();

            if (_nvmlLibraryHandle != IntPtr.Zero)
            {
                FreeLibrary(_nvmlLibraryHandle);
                _nvmlLibraryHandle = IntPtr.Zero;
            }

            _logger.LogInformation("🧹 [NVML] GPU監視システム終了完了");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [NVML] 終了処理中の警告");
        }
    }
}

/// <summary>
/// 詳細なGPU メトリクス
/// </summary>
public sealed record DetailedGpuMetrics
{
    /// <summary>GPU使用率 (%)</summary>
    public uint GpuUtilizationPercent { get; init; }

    /// <summary>VRAM使用率 (%)</summary>
    public uint MemoryUtilizationPercent { get; init; }

    /// <summary>総VRAM容量 (MB)</summary>
    public ulong TotalMemoryMB { get; init; }

    /// <summary>使用中VRAM容量 (MB)</summary>
    public ulong UsedMemoryMB { get; init; }

    /// <summary>空きVRAM容量 (MB)</summary>
    public ulong FreeMemoryMB { get; init; }

    /// <summary>GPU温度 (℃)</summary>
    public uint TemperatureCelsius { get; init; }

    /// <summary>電力使用量 (W)</summary>
    public uint PowerUsageWatts { get; init; }

    /// <summary>デバイス名</summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>デバイスインデックス</summary>
    public int DeviceIndex { get; init; }

    /// <summary>取得タイムスタンプ</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// VRAM使用率をパーセンテージで取得
    /// </summary>
    public double VramUsagePercent => TotalMemoryMB > 0 ? (double)UsedMemoryMB / TotalMemoryMB * 100.0 : 0.0;
}
