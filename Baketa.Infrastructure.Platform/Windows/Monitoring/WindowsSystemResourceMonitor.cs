using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Monitoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Platform.Windows.Monitoring;

/// <summary>
/// Windows固有のシステムリソース監視実装
/// PerformanceCounterとWMIを使用してCPU・メモリ・GPU使用率をリアルタイム監視
/// </summary>
public sealed class WindowsSystemResourceMonitor : IResourceMonitor
{
    #region P/Invoke for GlobalMemoryStatusEx (IL Trimming互換)

    private const long BytesPerMegabyte = 1024 * 1024;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    #endregion

    private readonly ILogger<WindowsSystemResourceMonitor> _logger;
    private readonly IEventAggregator _eventAggregator;
    private readonly ResourceMonitoringSettings _settings;

    // パフォーマンスカウンター
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _memoryAvailableCounter;
    private readonly PerformanceCounter? _memoryCommittedCounter;
    private PerformanceCounter? _processCountCounter;
    private PerformanceCounter? _threadCountCounter;

    // GPU関連
    private ManagementObjectSearcher? _gpuSearcher;
    private string? _gpuInstanceName;
    private readonly Advanced.NvmlGpuMonitor? _nvmlGpuMonitor;

    // 監視状態管理
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Task? _monitoringTask;
    private readonly object _lockObject = new();
    private volatile bool _isDisposed;

    // メトリクス履歴（スレッドセーフなコレクション）
    private readonly ConcurrentQueue<ResourceMetrics> _metricsHistory = new();
    private ResourceMetrics? _currentMetrics;
    private ResourceMetrics? _previousMetrics;

    // システム情報キャッシュ
    private readonly Lazy<long> _totalMemoryMB;
    private volatile bool _isInitialized;

    public WindowsSystemResourceMonitor(
        ILogger<WindowsSystemResourceMonitor> logger,
        IEventAggregator eventAggregator,
        IOptions<ResourceMonitoringSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));

        if (!_settings.IsValid)
        {
            throw new ArgumentException("リソース監視設定が無効です", nameof(settings));
        }

        _totalMemoryMB = new Lazy<long>(GetTotalSystemMemoryMB);

        // NVML GPU監視の初期化（Phase 3強化）
        // ロガー型不一致を解決するため、ILoggerFactory経由で適切な型のロガーを作成
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        });
        var nvmlLogger = loggerFactory.CreateLogger<Advanced.NvmlGpuMonitor>();
        _nvmlGpuMonitor = new Advanced.NvmlGpuMonitor(nvmlLogger);

        _logger.LogInformation("WindowsSystemResourceMonitor初期化開始 - 監視間隔:{MonitoringInterval}ms",
            _settings.MonitoringIntervalMs);
    }

    /// <inheritdoc />
    public bool IsMonitoring { get; private set; }

    /// <inheritdoc />
    public int MonitoringIntervalMs
    {
        get => _settings.MonitoringIntervalMs;
        set => throw new NotSupportedException("監視間隔の動的変更はサポートされていません。設定ファイルを変更して再起動してください。");
    }

    /// <inheritdoc />
    public ResourceMetrics? CurrentMetrics => _currentMetrics;

    /// <inheritdoc />
    public bool IsInitialized => _isInitialized;

    /// <inheritdoc />
    public event EventHandler<ResourceMetricsChangedEventArgs>? ResourceMetricsChanged;

    /// <inheritdoc />
    public event EventHandler<ResourceWarningEventArgs>? ResourceWarning;

    /// <inheritdoc />
    public bool Initialize()
    {
        if (_isInitialized)
        {
            return true;
        }

        try
        {
            InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同期初期化エラー");
            return false;
        }
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        if (!_isInitialized)
        {
            return;
        }

        try
        {
            StopMonitoringAsync().GetAwaiter().GetResult();
            _isInitialized = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同期シャットダウンエラー");
        }
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_isInitialized)
        {
            return;
        }

        try
        {
            _logger.LogInformation("🔧 [PHASE3] Windowsリソース監視システム初期化開始");

            await InitializePerformanceCountersAsync(cancellationToken).ConfigureAwait(false);
            await InitializeGpuMonitoringAsync(cancellationToken).ConfigureAwait(false);

            _isInitialized = true;
            _logger.LogInformation("🔧 [PHASE3] Windowsリソース監視システム初期化完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "リソース監視システム初期化エラー");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        lock (_lockObject)
        {
            if (IsMonitoring)
            {
                // [Issue #218] 既に監視中の場合はDebugレベルでログ出力（ノイズ削減）
                _logger.LogDebug("リソース監視は既に開始されています - スキップ");
                return;
            }

            IsMonitoring = true;
        }

        try
        {
            // 初期メトリクス取得
            var initialMetrics = await GetCurrentMetricsAsync(cancellationToken).ConfigureAwait(false);
            _currentMetrics = initialMetrics;

            // 監視開始イベント発火
            var startEvent = ResourceMonitoringEvent.CreateMonitoringStarted(initialMetrics);
            await _eventAggregator.PublishAsync(startEvent).ConfigureAwait(false);

            // バックグラウンド監視タスク開始
            _monitoringTask = MonitoringLoopAsync(_cancellationTokenSource.Token);

            _logger.LogInformation("🚀 [PHASE3] リソース監視開始 - 初期状況: {InitialMetrics}", initialMetrics);
        }
        catch (Exception ex)
        {
            IsMonitoring = false;
            _logger.LogError(ex, "リソース監視開始エラー");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopMonitoringAsync()
    {
        if (!IsMonitoring)
        {
            return;
        }

        lock (_lockObject)
        {
            IsMonitoring = false;
        }

        try
        {
            // 監視ループ停止
            _cancellationTokenSource.Cancel();

            if (_monitoringTask != null)
            {
                await _monitoringTask.ConfigureAwait(false);
            }

            // 最終メトリクス取得・イベント発火
            if (_currentMetrics != null)
            {
                var stopEvent = ResourceMonitoringEvent.CreateMonitoringStopped(_currentMetrics);
                await _eventAggregator.PublishAsync(stopEvent).ConfigureAwait(false);
            }

            _logger.LogInformation("⏹️ [PHASE3] リソース監視停止完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "リソース監視停止エラー");
        }
    }

    /// <inheritdoc />
    public async Task<ResourceMetrics> GetCurrentMetricsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        try
        {
            var timestamp = DateTime.UtcNow;

            // CPU使用率取得（2回測定して精度向上）
            var cpuUsage = await GetCpuUsageAsync(cancellationToken).ConfigureAwait(false);

            // メモリ使用量取得
            var (availableMemoryMB, totalMemoryMB) = GetMemoryUsage();
            var memoryUsagePercent = totalMemoryMB > 0
                ? ((double)(totalMemoryMB - availableMemoryMB) / totalMemoryMB) * 100.0
                : 0.0;

            // GPU使用率・VRAM使用量取得（利用可能な場合）
            // Issue #229: GpuMemoryUsageMBが未設定だったためVRAM=0%問題を修正
            var (gpuUsage, gpuMemoryUsageMB, gpuTemperature) = _settings.EnableGpuMonitoring
                ? await GetGpuMetricsAsync(cancellationToken).ConfigureAwait(false)
                : (null, null, null);

            // プロセス・スレッド数取得
            var processCount = GetProcessCount();
            var threadCount = GetThreadCount();

            var metrics = new ResourceMetrics(
                timestamp,
                Math.Max(0, Math.Min(100, cpuUsage)),
                Math.Max(0, Math.Min(100, memoryUsagePercent)),
                availableMemoryMB,
                totalMemoryMB,
                gpuUsage.HasValue ? Math.Max(0, Math.Min(100, gpuUsage.Value)) : null,
                GpuMemoryUsageMB: gpuMemoryUsageMB,
                GpuTemperature: gpuTemperature,
                ProcessCount: processCount,
                ThreadCount: threadCount);

            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "現在のリソースメトリクス取得エラー");

            // フォールバックメトリクス
            return new ResourceMetrics(
                DateTime.UtcNow, 0, 0, 0, _totalMemoryMB.Value);
        }
    }

    /// <inheritdoc />
    public IEnumerable<ResourceMetrics> GetMetricsHistory(DateTime fromTime, DateTime toTime)
    {
        return _metricsHistory
            .Where(m => m.Timestamp >= fromTime && m.Timestamp <= toTime)
            .OrderBy(m => m.Timestamp);
    }

    /// <summary>
    /// リソース監視ループ（バックグラウンド実行）
    /// </summary>
    private async Task MonitoringLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("リソース監視ループ開始");

        try
        {
            while (!cancellationToken.IsCancellationRequested && IsMonitoring)
            {
                try
                {
                    // メトリクス取得
                    var newMetrics = await GetCurrentMetricsAsync(cancellationToken).ConfigureAwait(false);

                    // 履歴に追加（古い履歴は定期的に削除）
                    _metricsHistory.Enqueue(newMetrics);
                    CleanupOldMetrics();

                    // 前回メトリクスをバックアップ
                    _previousMetrics = _currentMetrics;
                    _currentMetrics = newMetrics;

                    // イベント発火
                    await NotifyMetricsChangedAsync(newMetrics, _previousMetrics).ConfigureAwait(false);
                    await CheckAndNotifyWarningsAsync(newMetrics).ConfigureAwait(false);

                    // 監視間隔待機
                    await Task.Delay(_settings.MonitoringIntervalMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "リソース監視ループでエラーが発生しました");

                    // エラーイベント発火
                    var errorEvent = ResourceMonitoringEvent.CreateMonitoringError(_currentMetrics, ex);
                    await _eventAggregator.PublishAsync(errorEvent).ConfigureAwait(false);

                    // 一時的な停止（エラー連発防止）
                    await Task.Delay(Math.Min(_settings.MonitoringIntervalMs * 2, 10000), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("リソース監視ループがキャンセルされました");
        }
        finally
        {
            _logger.LogDebug("リソース監視ループ終了");
        }
    }

    /// <summary>
    /// パフォーマンスカウンター初期化
    /// </summary>
    private async Task InitializePerformanceCountersAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                // CPU使用率
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
                _cpuCounter.NextValue(); // 初回読み込み（精度向上のため）

                // メモリ使用量
                _memoryAvailableCounter = new PerformanceCounter("Memory", "Available MBytes", readOnly: true);

                // プロセス・スレッド数
                _processCountCounter = new PerformanceCounter("System", "Processes", readOnly: true);
                _threadCountCounter = new PerformanceCounter("System", "Threads", readOnly: true);

                _logger.LogDebug("パフォーマンスカウンター初期化完了");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "パフォーマンスカウンター初期化エラー");
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// GPU監視機能初期化
    /// </summary>
    private async Task InitializeGpuMonitoringAsync(CancellationToken cancellationToken)
    {
        if (!_settings.EnableGpuMonitoring)
        {
            _logger.LogDebug("GPU監視は無効化されています");
            return;
        }

        // Phase 3: 高度なNVML GPU監視初期化
        var nvmlInitialized = false;
        if (_nvmlGpuMonitor != null)
        {
            try
            {
                _logger.LogInformation("🎯 [PHASE3] NVML GPU監視初期化開始");
                nvmlInitialized = await _nvmlGpuMonitor.InitializeAsync(cancellationToken).ConfigureAwait(false);

                if (nvmlInitialized)
                {
                    _logger.LogInformation("✅ [PHASE3] NVML GPU監視初期化成功 - デバイス数: {DeviceCount}",
                        _nvmlGpuMonitor.DetectedDeviceCount);
                    return; // NVML成功時はWMIフォールバック不要
                }
                else
                {
                    _logger.LogWarning("⚠️ [PHASE3] NVML初期化失敗 - WMIフォールバックに切り替え");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ [PHASE3] NVML初期化例外 - WMIフォールバックに切り替え");
            }
        }

        // フォールバック: 従来のWMI GPU監視
        await Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("🔄 [FALLBACK] WMI GPU監視初期化開始");

                // WMI経由でGPU情報を取得
                _gpuSearcher = new ManagementObjectSearcher("root\\CIMV2",
                    "SELECT Name, AdapterRAM FROM Win32_VideoController WHERE AdapterRAM > 0");

                using var gpuCollection = _gpuSearcher.Get();
                foreach (ManagementObject gpu in gpuCollection.Cast<ManagementObject>())
                {
                    var gpuName = gpu["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(gpuName))
                    {
                        _gpuInstanceName = gpuName;
                        _logger.LogInformation("✅ [FALLBACK] GPU検出: {GpuName}", gpuName);
                        break;
                    }
                }

                if (string.IsNullOrEmpty(_gpuInstanceName))
                {
                    _logger.LogWarning("⚠️ [FALLBACK] GPU監視: 対応GPUが見つかりませんでした");
                }
                else
                {
                    _logger.LogInformation("✅ [FALLBACK] WMI GPU監視初期化完了");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 [FALLBACK] GPU監視初期化エラー - GPU監視を無効化します");
                _gpuSearcher?.Dispose();
                _gpuSearcher = null;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// CPU使用率取得（精度向上のため2回測定）
    /// </summary>
    private async Task<double> GetCpuUsageAsync(CancellationToken cancellationToken)
    {
        if (_cpuCounter == null)
        {
            return 0.0;
        }

        try
        {
            // 1回目の測定（ベースライン）
            _cpuCounter.NextValue();

            // 短時間待機（測定精度向上）
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);

            // 2回目の測定（実際の値）
            return _cpuCounter.NextValue();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CPU使用率取得エラー");
            return 0.0;
        }
    }

    /// <summary>
    /// メモリ使用量取得
    /// </summary>
    private (long availableMemoryMB, long totalMemoryMB) GetMemoryUsage()
    {
        try
        {
            var availableMemoryMB = (long)(_memoryAvailableCounter?.NextValue() ?? 0);
            var totalMemoryMB = _totalMemoryMB.Value;

            return (availableMemoryMB, totalMemoryMB);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "メモリ使用量取得エラー");
            return (0, _totalMemoryMB.Value);
        }
    }

    /// <summary>
    /// GPU使用率・VRAM使用量・温度を取得
    /// Issue #229: GpuMemoryUsageMBが未設定だった問題を修正
    /// </summary>
    private async Task<(double? gpuUsage, long? gpuMemoryUsageMB, double? temperature)> GetGpuMetricsAsync(CancellationToken cancellationToken)
    {
        // Phase 3: 高度なNVML GPU監視を優先使用
        if (_nvmlGpuMonitor?.IsNvmlAvailable == true)
        {
            try
            {
                var detailedMetrics = await _nvmlGpuMonitor.GetDetailedGpuMetricsAsync(cancellationToken).ConfigureAwait(false);
                if (detailedMetrics != null)
                {
                    _logger.LogTrace("[NVML] GPUメトリクス取得成功: Usage={Usage:F1}%, VRAM={VramUsed}MB/{VramTotal}MB, Temp={Temp}℃",
                        detailedMetrics.GpuUtilizationPercent,
                        detailedMetrics.UsedMemoryMB,
                        detailedMetrics.TotalMemoryMB,
                        detailedMetrics.TemperatureCelsius);

                    return (
                        detailedMetrics.GpuUtilizationPercent,
                        (long)detailedMetrics.UsedMemoryMB,
                        detailedMetrics.TemperatureCelsius
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ [NVML] GPUメトリクス取得エラー - フォールバックに切り替え");
            }
        }

        // フォールバック: 従来のWMI GPU監視（基本的な可用性確認）
        if (_gpuSearcher == null || string.IsNullOrEmpty(_gpuInstanceName))
        {
            return (null, null, null);
        }

        return await Task.Run<(double?, long?, double?)>(() =>
        {
            try
            {
                _logger.LogTrace("[FALLBACK] WMI GPU監視 - 基本的な可用性確認のみ実行");
                // Note: Windows標準のWMIではGPU使用率・VRAM使用量の直接取得は制限があります
                // フォールバック時は基本的な状態のみ返却
                return (0.0, null, null); // プレースホルダー実装（GPU検出済みを示す）
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FALLBACK] GPUメトリクス取得エラー");
                return (null, null, null);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// プロセス数取得
    /// </summary>
    private int GetProcessCount()
    {
        try
        {
            return (int)(_processCountCounter?.NextValue() ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "プロセス数取得エラー");
            return Process.GetProcesses().Length; // フォールバック
        }
    }

    /// <summary>
    /// スレッド数取得
    /// </summary>
    private int GetThreadCount()
    {
        try
        {
            return (int)(_threadCountCounter?.NextValue() ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "スレッド数取得エラー");
            return 0;
        }
    }

    /// <summary>
    /// システム総メモリ容量取得（P/Invoke版 - IL Trimming互換）
    /// </summary>
    private long GetTotalSystemMemoryMB()
    {
        try
        {
            // P/Invoke: GlobalMemoryStatusEx（WMIより高速・IL Trimming互換）
            var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                return (long)(memStatus.ullTotalPhys / BytesPerMegabyte);
            }
            else
            {
                _logger.LogWarning("GlobalMemoryStatusEx失敗 - エラーコード: {ErrorCode}", Marshal.GetLastWin32Error());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "システム総メモリ容量取得エラー");
        }

        // フォールバック: 取得失敗を示す0を返す（呼び出し元でエラーハンドリング）
        return 0;
    }

    /// <summary>
    /// メトリクス変更イベント通知
    /// </summary>
    private async Task NotifyMetricsChangedAsync(ResourceMetrics newMetrics, ResourceMetrics? previousMetrics)
    {
        try
        {
            // イベントハンドラー呼び出し
            var eventArgs = new ResourceMetricsChangedEventArgs(newMetrics, previousMetrics);
            ResourceMetricsChanged?.Invoke(this, eventArgs);

            // イベントアグリゲーター通知
            var resourceEvent = ResourceMonitoringEvent.CreateMetricsChanged(newMetrics, previousMetrics);
            await _eventAggregator.PublishAsync(resourceEvent).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メトリクス変更イベント通知エラー");
        }
    }

    /// <summary>
    /// 警告チェック・通知
    /// </summary>
    private async Task CheckAndNotifyWarningsAsync(ResourceMetrics metrics)
    {
        try
        {
            var warnings = new List<ResourceWarning>();

            // CPU警告チェック
            if (metrics.CpuUsagePercent > _settings.CpuWarningThreshold)
            {
                warnings.Add(new ResourceWarning(
                    ResourceWarningType.HighCpuUsage,
                    $"CPU使用率が高い状態です: {metrics.CpuUsagePercent:F1}%",
                    metrics.CpuUsagePercent > 95 ? ResourceWarningSeverity.Critical : ResourceWarningSeverity.Warning,
                    _settings.CpuWarningThreshold,
                    metrics.CpuUsagePercent));
            }

            // メモリ警告チェック
            if (metrics.MemoryUsagePercent > _settings.MemoryWarningThreshold)
            {
                warnings.Add(new ResourceWarning(
                    ResourceWarningType.HighMemoryUsage,
                    $"メモリ使用率が高い状態です: {metrics.MemoryUsagePercent:F1}%",
                    metrics.MemoryUsagePercent > 95 ? ResourceWarningSeverity.Critical : ResourceWarningSeverity.Warning,
                    _settings.MemoryWarningThreshold,
                    metrics.MemoryUsagePercent));
            }

            // GPU警告チェック
            if (metrics.GpuUsagePercent.HasValue && metrics.GpuUsagePercent.Value > _settings.GpuWarningThreshold)
            {
                warnings.Add(new ResourceWarning(
                    ResourceWarningType.HighGpuUsage,
                    $"GPU使用率が高い状態です: {metrics.GpuUsagePercent.Value:F1}%",
                    metrics.GpuUsagePercent.Value > 98 ? ResourceWarningSeverity.Critical : ResourceWarningSeverity.Warning,
                    _settings.GpuWarningThreshold,
                    metrics.GpuUsagePercent.Value));
            }

            // 警告通知
            foreach (var warning in warnings)
            {
                var warningArgs = new ResourceWarningEventArgs(warning.Type, warning.Message, metrics);
                ResourceWarning?.Invoke(this, warningArgs);

                var warningEvent = ResourceMonitoringEvent.CreateWarning(metrics, warning);
                await _eventAggregator.PublishAsync(warningEvent).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "リソース警告チェックエラー");
        }
    }

    /// <summary>
    /// 古いメトリクス履歴のクリーンアップ
    /// </summary>
    private void CleanupOldMetrics()
    {
        var cutoffTime = DateTime.UtcNow.AddMinutes(-_settings.HistoryRetentionMinutes);

        while (_metricsHistory.TryPeek(out var oldestMetric) &&
               oldestMetric.Timestamp < cutoffTime)
        {
            _metricsHistory.TryDequeue(out _);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            // 監視停止
            if (IsMonitoring)
            {
                StopMonitoringAsync().GetAwaiter().GetResult();
            }

            // リソース解放
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            _cpuCounter?.Dispose();
            _memoryAvailableCounter?.Dispose();
            _memoryCommittedCounter?.Dispose();
            _processCountCounter?.Dispose();
            _threadCountCounter?.Dispose();

            _gpuSearcher?.Dispose();

            // Phase 3: NVML GPU監視のリソース解放
            _nvmlGpuMonitor?.Dispose();

            _logger.LogInformation("WindowsSystemResourceMonitor正常終了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WindowsSystemResourceMonitor終了処理エラー");
        }
        finally
        {
            _isDisposed = true;
        }
    }
}
