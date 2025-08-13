using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using Baketa.Core.Abstractions.GPU;

namespace Baketa.Infrastructure.Platform.Windows.GPU;

/// <summary>
/// Windows GPU デバイス管理実装
/// PNPDeviceIDベースのGPU個別管理とMulti-GPU負荷分散
/// Issue #143 Week 2: 高度なGPU環境管理システム
/// </summary>
public sealed class WindowsGpuDeviceManager : IGpuDeviceManager, IDisposable
{
    private readonly ILogger<WindowsGpuDeviceManager> _logger;
    private readonly ConcurrentDictionary<string, GpuDeviceInfo> _deviceCache = new();
    private readonly ConcurrentDictionary<string, GpuWorkloadStatus> _workloadCache = new();
    private readonly Timer _cacheRefreshTimer;
    private readonly object _cacheLock = new();
    private bool _disposed = false;

    public WindowsGpuDeviceManager(ILogger<WindowsGpuDeviceManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // キャッシュリフレッシュタイマー（30秒間隔）
        _cacheRefreshTimer = new Timer(RefreshCacheCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        
        _logger.LogInformation("🎮 WindowsGpuDeviceManager初期化完了 - Multi-GPU管理開始");
    }

    public async Task<IReadOnlyList<GpuDeviceInfo>> GetAvailableGpuDevicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("🔍 利用可能GPU デバイス検索開始");
            
            var devices = new List<GpuDeviceInfo>();
            
            await Task.Run(() =>
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                var collection = searcher.Get();
                
                int deviceIndex = 0;
                foreach (ManagementObject obj in collection.Cast<ManagementObject>())
                {
                    try
                    {
                        var deviceInfo = CreateGpuDeviceInfo(obj, deviceIndex++);
                        if (deviceInfo != null)
                        {
                            devices.Add(deviceInfo);
                            _deviceCache.TryAdd(deviceInfo.PnpDeviceId, deviceInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "GPU デバイス情報取得中に警告: {DeviceIndex}", deviceIndex);
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
            
            _logger.LogInformation("✅ GPU デバイス検索完了 - 発見数: {Count}", devices.Count);
            return devices.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ GPU デバイス検索失敗");
            return Array.Empty<GpuDeviceInfo>();
        }
    }

    public async Task<GpuEnvironmentInfo> SelectOptimalGpuAsync(GpuWorkloadType workloadType, int estimatedMemoryMB = 0, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("🎯 最適GPU選択開始 - 負荷タイプ: {WorkloadType}, メモリ: {MemoryMB}MB", workloadType, estimatedMemoryMB);
            
            var devices = await GetAvailableGpuDevicesAsync(cancellationToken).ConfigureAwait(false);
            if (!devices.Any())
            {
                _logger.LogWarning("利用可能なGPU デバイスがありません - CPU実行にフォールバック");
                return CreateCpuFallbackEnvironment();
            }
            
            // 負荷タイプに基づくスコアリング
            var scoredDevices = await Task.Run(() => devices
                .Select(device => new
                {
                    Device = device,
                    Score = CalculateWorkloadScore(device, workloadType, estimatedMemoryMB)
                })
                .OrderByDescending(x => x.Score)
                .ToList(), cancellationToken).ConfigureAwait(false);
            
            var bestDevice = scoredDevices.First().Device;
            
            _logger.LogInformation("✅ 最適GPU選択完了 - 選択GPU: {GpuName} (スコア: {Score})", 
                bestDevice.Name, scoredDevices.First().Score);
            
            return ConvertToGpuEnvironmentInfo(bestDevice);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 最適GPU選択失敗 - CPU実行にフォールバック");
            return CreateCpuFallbackEnvironment();
        }
    }

    public async Task<GpuEnvironmentInfo> SelectOptimalGpuAsync(string[] pnpDeviceIds, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("🎯 指定GPU選択開始 - 候補数: {Count}", pnpDeviceIds.Length);
            
            var allDevices = await GetAvailableGpuDevicesAsync(cancellationToken).ConfigureAwait(false);
            List<GpuDeviceInfo> candidateDevices = [.. allDevices.Where(d => pnpDeviceIds.Contains(d.PnpDeviceId))];
            
            if (!candidateDevices.Any())
            {
                _logger.LogWarning("指定されたPNPDeviceIDに該当するGPU が見つかりません");
                return CreateCpuFallbackEnvironment();
            }
            
            // 可用性とパフォーマンスでソート
            var bestDevice = candidateDevices
                .Where(d => d.AvailableMemoryMB > 1024) // 最低1GB空きメモリ
                .OrderByDescending(d => d.PerformanceScore)
                .ThenByDescending(d => d.AvailableMemoryMB)
                .FirstOrDefault();
            
            if (bestDevice == null)
            {
                _logger.LogWarning("十分なメモリを持つGPU が見つかりません");
                return CreateCpuFallbackEnvironment();
            }
            
            _logger.LogInformation("✅ 指定GPU選択完了 - 選択GPU: {GpuName}", bestDevice.Name);
            return ConvertToGpuEnvironmentInfo(bestDevice);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 指定GPU選択失敗");
            return CreateCpuFallbackEnvironment();
        }
    }

    public async Task<GpuAvailabilityStatus> ValidateGpuAvailabilityAsync(string pnpDeviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("🔍 GPU可用性検証開始 - PNP ID: {PnpDeviceId}", pnpDeviceId);
            
            var device = _deviceCache.GetValueOrDefault(pnpDeviceId);
            if (device == null)
            {
                var devices = await GetAvailableGpuDevicesAsync(cancellationToken).ConfigureAwait(false);
                device = devices.FirstOrDefault(d => d.PnpDeviceId == pnpDeviceId);
            }
            
            if (device == null)
            {
                return new GpuAvailabilityStatus
                {
                    IsAvailable = false,
                    StatusMessage = "指定されたGPU デバイスが見つかりません",
                    LastCheckedAt = DateTime.UtcNow
                };
            }
            
            // ドライバー状態とTDR状態をチェック
            var isDriverHealthy = await CheckDriverHealthAsync(pnpDeviceId, cancellationToken).ConfigureAwait(false);
            var isInTdrState = await CheckTdrStateAsync(pnpDeviceId, cancellationToken).ConfigureAwait(false);
            
            var status = new GpuAvailabilityStatus
            {
                IsAvailable = isDriverHealthy && !isInTdrState && device.AvailableMemoryMB > 512,
                StatusMessage = GenerateStatusMessage(isDriverHealthy, isInTdrState, device.AvailableMemoryMB),
                IsInTdrState = isInTdrState,
                IsDriverHealthy = isDriverHealthy,
                LastCheckedAt = DateTime.UtcNow
            };
            
            _logger.LogDebug("✅ GPU可用性検証完了 - 利用可能: {IsAvailable}", status.IsAvailable);
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ GPU可用性検証失敗");
            return new GpuAvailabilityStatus
            {
                IsAvailable = false,
                StatusMessage = $"検証中にエラーが発生: {ex.Message}",
                LastCheckedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<GpuWorkloadStatus> GetGpuWorkloadStatusAsync(string pnpDeviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            // キャッシュから取得を試行
            if (_workloadCache.TryGetValue(pnpDeviceId, out var cachedStatus))
            {
                var cacheAge = DateTime.UtcNow - DateTime.UtcNow.AddSeconds(-5); // 5秒キャッシュ
                if (cacheAge.TotalSeconds < 5)
                {
                    return cachedStatus;
                }
            }
            
            _logger.LogDebug("📊 GPU負荷状況取得開始 - PNP ID: {PnpDeviceId}", pnpDeviceId);
            
            var status = await Task.Run(async () =>
            {
                // Performance Counterを使用してGPU使用率を取得
                var gpuUtilization = await GetGpuUtilizationAsync(pnpDeviceId, cancellationToken).ConfigureAwait(false);
                var memoryUtilization = await GetMemoryUtilizationAsync(pnpDeviceId, cancellationToken).ConfigureAwait(false);
                var activeProcessCount = await GetActiveProcessCountAsync(pnpDeviceId, cancellationToken).ConfigureAwait(false);
                
                var device = _deviceCache.GetValueOrDefault(pnpDeviceId);
                var estimatedFreeMemory = device?.AvailableMemoryMB ?? 0;
                
                return new GpuWorkloadStatus
                {
                    GpuUtilization = gpuUtilization,
                    MemoryUtilization = memoryUtilization,
                    ActiveProcessCount = activeProcessCount,
                    EstimatedFreeMemoryMB = estimatedFreeMemory,
                    TemperatureCelsius = null // Windows APIでは取得困難
                };
            }, cancellationToken).ConfigureAwait(false);
            
            // キャッシュに保存
            _workloadCache.AddOrUpdate(pnpDeviceId, status, (_, _) => status);
            
            _logger.LogDebug("✅ GPU負荷状況取得完了 - GPU使用率: {GpuUtil}%, メモリ使用率: {MemUtil}%", 
                status.GpuUtilization, status.MemoryUtilization);
            
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ GPU負荷状況取得失敗");
            return new GpuWorkloadStatus
            {
                GpuUtilization = 0,
                MemoryUtilization = 0,
                ActiveProcessCount = 0,
                EstimatedFreeMemoryMB = 0
            };
        }
    }

    public async Task<GpuAllocationRecommendation> GetOptimalAllocationAsync(IReadOnlyList<GpuWorkloadRequest> workloads, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("🎲 GPU配置最適化開始 - 処理負荷数: {Count}", workloads.Count);
            
            var devices = await GetAvailableGpuDevicesAsync(cancellationToken).ConfigureAwait(false);
            var allocations = new List<GpuAllocationItem>();
            
            // 優先度でソートされた処理負荷
            List<GpuWorkloadRequest> sortedWorkloads = [.. workloads.OrderByDescending(w => w.Priority)];
            
            foreach (var workload in sortedWorkloads)
            {
                var bestDevice = await SelectBestDeviceForWorkload(devices, workload, allocations, cancellationToken).ConfigureAwait(false);
                if (bestDevice != null)
                {
                    var allocation = new GpuAllocationItem
                    {
                        WorkloadRequest = workload,
                        RecommendedPnpDeviceId = bestDevice.PnpDeviceId,
                        RecommendedProviders = bestDevice.SupportedProviders,
                        Confidence = CalculateAllocationConfidence(bestDevice, workload)
                    };
                    allocations.Add(allocation);
                }
            }
            
            var totalScore = allocations.Sum(a => a.Confidence * a.WorkloadRequest.Priority);
            
            var recommendation = new GpuAllocationRecommendation
            {
                Allocations = allocations,
                Reason = GenerateAllocationReason(allocations),
                TotalPerformanceScore = totalScore
            };
            
            _logger.LogInformation("✅ GPU配置最適化完了 - 配置数: {Count}, 総スコア: {Score:F2}", 
                allocations.Count, totalScore);
            
            return recommendation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ GPU配置最適化失敗");
            return new GpuAllocationRecommendation
            {
                Allocations = [],
                Reason = $"配置最適化中にエラーが発生: {ex.Message}",
                TotalPerformanceScore = 0
            };
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _cacheRefreshTimer?.Dispose();
        _disposed = true;
        
        _logger.LogInformation("🧹 WindowsGpuDeviceManager リソース解放完了");
    }

    private GpuDeviceInfo? CreateGpuDeviceInfo(ManagementObject obj, int deviceIndex)
    {
        try
        {
            var pnpDeviceId = obj["PNPDeviceID"]?.ToString() ?? string.Empty;
            var name = obj["Name"]?.ToString() ?? $"Unknown GPU {deviceIndex}";
            var adapterRAM = Convert.ToInt64(obj["AdapterRAM"] ?? 0);
            
            // ベンダー判定
            var vendor = DetermineVendor(name, pnpDeviceId);
            var isDedicated = adapterRAM > 1024 * 1024 * 1024; // 1GB以上は専用GPU扱い
            
            var supportedProviders = DetermineSupportedProviders(vendor, name);
            var performanceScore = CalculatePerformanceScore(vendor, name, adapterRAM);
            
            return new GpuDeviceInfo
            {
                PnpDeviceId = pnpDeviceId,
                Name = name,
                Vendor = vendor,
                DedicatedMemoryMB = adapterRAM / (1024 * 1024),
                SharedMemoryMB = isDedicated ? 0 : 2048, // 統合GPUは2GBシェア想定
                AvailableMemoryMB = (adapterRAM / (1024 * 1024)) * 85 / 100, // 85%利用可能想定
                DeviceIndex = deviceIndex,
                IsDedicatedGpu = isDedicated,
                SupportedProviders = supportedProviders,
                PerformanceScore = performanceScore,
                DriverVersion = obj["DriverVersion"]?.ToString() ?? "Unknown"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPU デバイス情報作成失敗: デバイスインデックス {DeviceIndex}", deviceIndex);
            return null;
        }
    }

    private string DetermineVendor(string name, string pnpDeviceId)
    {
        var nameUpper = name.ToUpperInvariant();
        var pnpUpper = pnpDeviceId.ToUpperInvariant();
        
        if (nameUpper.Contains("NVIDIA") || pnpUpper.Contains("VEN_10DE"))
            return "NVIDIA";
        if (nameUpper.Contains("AMD") || nameUpper.Contains("RADEON") || pnpUpper.Contains("VEN_1002"))
            return "AMD";
        if (nameUpper.Contains("INTEL") || pnpUpper.Contains("VEN_8086"))
            return "Intel";
        
        return "Unknown";
    }

    private List<ExecutionProvider> DetermineSupportedProviders(string vendor, string name)
    {
        List<ExecutionProvider> providers = [ExecutionProvider.CPU];
        
        // すべてのGPUでDirectMLサポート（Windows 10以降）
        providers.Add(ExecutionProvider.DirectML);
        
        if (vendor == "NVIDIA")
        {
            providers.Add(ExecutionProvider.CUDA);
            if (name.Contains("RTX") || name.Contains("GTX 16") || name.Contains("Tesla"))
            {
                providers.Add(ExecutionProvider.TensorRT);
            }
        }
        
        if (vendor == "Intel")
        {
            providers.Add(ExecutionProvider.OpenVINO);
        }
        
        return providers;
    }

    private int CalculatePerformanceScore(string vendor, string name, long adapterRAM)
    {
        var score = 10; // ベーススコア
        
        // メモリ容量ボーナス
        var memoryGB = adapterRAM / (1024 * 1024 * 1024);
        score += (int)Math.Min(memoryGB * 5, 40); // 最大40点
        
        // ベンダー・モデル固有ボーナス
        if (vendor == "NVIDIA")
        {
            if (name.Contains("RTX 40")) score += 30;
            else if (name.Contains("RTX 30")) score += 25;
            else if (name.Contains("RTX 20")) score += 20;
            else if (name.Contains("GTX 16")) score += 15;
        }
        else if (vendor == "AMD")
        {
            if (name.Contains("RX 7")) score += 25;
            else if (name.Contains("RX 6")) score += 20;
        }
        else if (vendor == "Intel")
        {
            if (name.Contains("Arc")) score += 15;
            else score += 5; // 統合GPU
        }
        
        return Math.Min(score, 100);
    }

    private double CalculateWorkloadScore(GpuDeviceInfo device, GpuWorkloadType workloadType, int estimatedMemoryMB)
    {
        var score = (double)device.PerformanceScore;
        
        // メモリ要件チェック
        if (device.AvailableMemoryMB < estimatedMemoryMB)
        {
            score *= 0.1; // 大幅減点
        }
        
        // 負荷タイプ固有調整
        score *= workloadType switch
        {
            GpuWorkloadType.TextDetection => device.Vendor == "NVIDIA" ? 1.2 : 1.0,
            GpuWorkloadType.TextRecognition => device.IsDedicatedGpu ? 1.1 : 0.8,
            GpuWorkloadType.ImagePreprocessing => 1.0,
            GpuWorkloadType.LanguageIdentification => 0.9, // CPUの方が効率的
            _ => 1.0
        };
        
        return score;
    }

    private GpuEnvironmentInfo ConvertToGpuEnvironmentInfo(GpuDeviceInfo device)
    {
        return new GpuEnvironmentInfo
        {
            GpuName = device.Name,
            GpuDeviceId = device.DeviceIndex,
            IsDedicatedGpu = device.IsDedicatedGpu,
            IsIntegratedGpu = !device.IsDedicatedGpu,
            SupportsCuda = device.SupportedProviders.Contains(ExecutionProvider.CUDA),
            SupportsTensorRT = device.SupportedProviders.Contains(ExecutionProvider.TensorRT),
            SupportsDirectML = device.SupportedProviders.Contains(ExecutionProvider.DirectML),
            SupportsOpenVINO = device.SupportedProviders.Contains(ExecutionProvider.OpenVINO),
            AvailableMemoryMB = (int)device.AvailableMemoryMB,
            ComputeCapability = device.Vendor == "NVIDIA" ? ComputeCapability.Compute75 : ComputeCapability.Unknown,
            RecommendedProviders = device.SupportedProviders
        };
    }

    private GpuEnvironmentInfo CreateCpuFallbackEnvironment()
    {
        return new GpuEnvironmentInfo
        {
            GpuName = "CPU Fallback",
            GpuDeviceId = -1,
            IsDedicatedGpu = false,
            IsIntegratedGpu = false,
            RecommendedProviders = [ExecutionProvider.CPU]
        };
    }

    private async Task<bool> CheckDriverHealthAsync(string pnpDeviceId, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken).ConfigureAwait(false); // プレースホルダー実装
        return true; // 実際の実装では詳細なドライバーチェックを行う
    }

    private async Task<bool> CheckTdrStateAsync(string pnpDeviceId, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken).ConfigureAwait(false); // プレースホルダー実装
        return false; // 実際の実装ではレジストリやイベントログをチェック
    }

    private string GenerateStatusMessage(bool isDriverHealthy, bool isInTdrState, long availableMemoryMB)
    {
        if (!isDriverHealthy) return "ドライバーに問題があります";
        if (isInTdrState) return "TDR状態です";
        if (availableMemoryMB < 512) return "利用可能メモリが不足しています";
        return "正常";
    }

    private async Task<double> GetGpuUtilizationAsync(string pnpDeviceId, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        return Random.Shared.NextDouble() * 100; // プレースホルダー実装
    }

    private async Task<double> GetMemoryUtilizationAsync(string pnpDeviceId, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        return Random.Shared.NextDouble() * 100; // プレースホルダー実装
    }

    private async Task<int> GetActiveProcessCountAsync(string pnpDeviceId, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        return Random.Shared.Next(0, 5); // プレースホルダー実装
    }

    private async Task<GpuDeviceInfo?> SelectBestDeviceForWorkload(IReadOnlyList<GpuDeviceInfo> devices, 
        GpuWorkloadRequest workload, List<GpuAllocationItem> existingAllocations, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        
        // 既存配置を考慮した最適デバイス選択
        var availableDevices = devices.Where(d => 
            d.AvailableMemoryMB >= workload.EstimatedMemoryMB &&
            !existingAllocations.Any(a => a.RecommendedPnpDeviceId == d.PnpDeviceId && 
                                        a.WorkloadRequest.EstimatedMemoryMB + workload.EstimatedMemoryMB > d.AvailableMemoryMB))
            .ToList();
        
        return availableDevices.OrderByDescending(d => CalculateWorkloadScore(d, workload.WorkloadType, workload.EstimatedMemoryMB))
                              .FirstOrDefault();
    }

    private double CalculateAllocationConfidence(GpuDeviceInfo device, GpuWorkloadRequest workload)
    {
        var memoryFit = (double)device.AvailableMemoryMB / Math.Max(workload.EstimatedMemoryMB, 1);
        var performanceFit = device.PerformanceScore / 100.0;
        
        return Math.Min(memoryFit * performanceFit, 1.0);
    }

    private string GenerateAllocationReason(List<GpuAllocationItem> allocations)
    {
        if (!allocations.Any()) return "配置可能なGPU が見つかりませんでした";
        
        var gpuCount = allocations.Select(a => a.RecommendedPnpDeviceId).Distinct().Count();
        var avgConfidence = allocations.Average(a => a.Confidence);
        
        return $"{gpuCount}個のGPU に{allocations.Count}個の処理を配置。平均信頼度: {avgConfidence:P1}";
    }

    private void RefreshCacheCallback(object? state)
    {
        try
        {
            _logger.LogDebug("🔄 GPU デバイスキャッシュリフレッシュ開始");
            _ = Task.Run(async () =>
            {
                try
                {
                    await GetAvailableGpuDevicesAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "キャッシュリフレッシュ中に警告が発生");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "キャッシュリフレッシュタイマーでエラーが発生");
        }
    }
}
