using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Events.CaptureEvents;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Platform.Adapters;
using Baketa.Infrastructure.Platform.Windows.Capture;
using Microsoft.Extensions.Logging;
using static Baketa.Core.Abstractions.Services.CaptureServiceStatus;
using CoreIDifferenceDetector = Baketa.Core.Abstractions.Capture.IDifferenceDetector;
using CorePlatform = Baketa.Core.Abstractions.Platform;
using ServicesCaptureOptions = Baketa.Core.Abstractions.Services.CaptureOptions;
using SettingsGameCaptureProfile = Baketa.Core.Settings.GameCaptureProfile;
using WindowsImageAdapter = Baketa.Infrastructure.Platform.Adapters.WindowsImageAdapter;

namespace Baketa.Application.Services.Capture;

/// <summary>
/// 拡張キャプチャサービスの実装
/// 連続キャプチャ、パフォーマンス最適化、ステータス管理機能を提供
/// </summary>
public sealed class AdvancedCaptureService : IAdvancedCaptureService, IDisposable
{
    private readonly IGdiScreenCapturer _screenCapturer;
    private readonly CoreIDifferenceDetector _differenceDetector;
    private readonly IEventAggregator _eventAggregator;
    private readonly Baketa.Core.Abstractions.Platform.Windows.IWindowManager _windowManager;
    private readonly ILogger<AdvancedCaptureService>? _logger;

    private CaptureSettings _currentSettings = new();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0032:Use auto property",
        Justification = "Field is modified by internal methods and lock synchronization, auto-property not suitable")]
    private CaptureServiceStatus _status = CaptureServiceStatus.Stopped;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0032:Use auto property",
        Justification = "Field is modified by internal methods and lock synchronization, auto-property not suitable")]
    private IImage? _lastCapturedImage;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0032:Use auto property",
        Justification = "Field is modified by internal methods and lock synchronization, auto-property not suitable")]
    private DateTime? _lastCaptureTime;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0032:Use auto property",
        Justification = "Field is modified by internal methods and lock synchronization, auto-property not suitable")]
    private readonly CapturePerformanceInfo _performanceInfo = new();

    private Task? _continuousCaptureTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly object _syncLock = new();

    // パフォーマンス監視用
    private readonly PerformanceCounter? _cpuCounter;
    private readonly PerformanceCounter? _memoryCounter;
    private readonly Stopwatch _captureStopwatch = new();
    private readonly Queue<double> _captureTimes = new(100); // 直近100回の計測値
    private readonly Queue<DateTime> _captureTimestamps = new(100);

    // ゲームプロファイル管理
    private SettingsGameCaptureProfile? _currentGameProfile;
    private IntPtr _monitoredWindowHandle = IntPtr.Zero;

    // 自適応最適化
    private DateTime _lastOptimizationTime = DateTime.MinValue;
    private readonly TimeSpan _optimizationInterval = TimeSpan.FromMinutes(2);

    public CaptureServiceStatus Status => _status;
    public IImage? LastCapturedImage => _lastCapturedImage;
    public DateTime? LastCaptureTime => _lastCaptureTime;
    public CapturePerformanceInfo PerformanceInfo => _performanceInfo;

    public AdvancedCaptureService(
        IGdiScreenCapturer screenCapturer,
        CoreIDifferenceDetector differenceDetector,
        IEventAggregator eventAggregator,
        Baketa.Core.Abstractions.Platform.Windows.IWindowManager windowManager,
        ILogger<AdvancedCaptureService>? logger = null)
    {
        _screenCapturer = screenCapturer ?? throw new ArgumentNullException(nameof(screenCapturer));
        _differenceDetector = differenceDetector ?? throw new ArgumentNullException(nameof(differenceDetector));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _logger = logger;

        // パフォーマンスカウンター初期化
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogWarning(ex, "パフォーマンスカウンターの初期化に失敗しました");
            _cpuCounter = null;
            _memoryCounter = null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, "パフォーマンスカウンターへのアクセスが拒否されました");
            _cpuCounter = null;
            _memoryCounter = null;
        }
    }

    #region ICaptureService基本実装

    public async Task<IImage> CaptureScreenAsync()
    {
        var windowsImage = await _screenCapturer.CaptureScreenAsync().ConfigureAwait(false);
        return new WindowsImageAdapter(windowsImage);
    }

    public async Task<IImage> CaptureRegionAsync(Rectangle region)
    {
        var windowsImage = await _screenCapturer.CaptureRegionAsync(region).ConfigureAwait(false);
        return new WindowsImageAdapter(windowsImage);
    }

    public async Task<IImage> CaptureWindowAsync(IntPtr windowHandle)
    {
        var windowsImage = await _screenCapturer.CaptureWindowAsync(windowHandle).ConfigureAwait(false);
        return new WindowsImageAdapter(windowsImage);
    }

    public async Task<IImage> CaptureClientAreaAsync(IntPtr windowHandle)
    {
        // クライアント領域のキャプチャ（今後実装）
        return await CaptureWindowAsync(windowHandle).ConfigureAwait(false);
    }

    public async Task<bool> DetectChangesAsync(IImage previousImage, IImage currentImage, float threshold = 0.05f)
    {
        ArgumentNullException.ThrowIfNull(previousImage);
        ArgumentNullException.ThrowIfNull(currentImage);
        var settings = _differenceDetector.GetSettings();
        var originalThreshold = settings.Threshold;

        try
        {
            _differenceDetector.SetThreshold(threshold);
            return await _differenceDetector.HasSignificantChangeAsync(previousImage, currentImage).ConfigureAwait(false);
        }
        finally
        {
            _differenceDetector.SetThreshold(originalThreshold);
        }
    }

    public void SetCaptureOptions(ServicesCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // レガシーCaptureOptionsから新しいCaptureSettingsに変換
        _currentSettings.CaptureIntervalMs = options.CaptureInterval;
        _currentSettings.CaptureQuality = options.Quality;

        _logger?.LogDebug("キャプチャオプションを更新: 間隔={Interval}ms, 品質={Quality}",
            options.CaptureInterval, options.Quality);
    }

    public ServicesCaptureOptions GetCaptureOptions()
    {
        return new ServicesCaptureOptions
        {
            CaptureInterval = _currentSettings.CaptureIntervalMs,
            Quality = _currentSettings.CaptureQuality,
            OptimizationLevel = _currentSettings.AutoOptimizeForGames ? 3 : 1
        };
    }

    #endregion

    #region IAdvancedCaptureService拡張実装

    public async Task StartContinuousCaptureAsync(CaptureSettings? settings = null, CancellationToken cancellationToken = default)
    {
        // 初期化状態に設定
        await ChangeStatusAsync(CaptureServiceStatus.Initializing, "連続キャプチャ初期化中").ConfigureAwait(false);

        lock (_syncLock)
        {
            if (_status == CaptureServiceStatus.Running || _status == CaptureServiceStatus.Initializing)
            {
                _logger?.LogWarning("連続キャプチャは既に実行中または初期化中です");
                return;
            }

            if (settings != null)
            {
                _currentSettings = settings;
                ApplySettingsToDifferenceDetector();
            }

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _logger?.LogInformation("連続キャプチャを開始: 間隔={Interval}ms", _currentSettings.CaptureIntervalMs);
        }

        // ステータス変更イベントを発行
        await ChangeStatusAsync(CaptureServiceStatus.Running, "連続キャプチャ開始要求").ConfigureAwait(false);

        // パフォーマンス統計をリセット
        ResetPerformanceStatistics();

        // 連続キャプチャタスクを開始
        _continuousCaptureTask = RunContinuousCaptureAsync(_cancellationTokenSource.Token);

        try
        {
            // 最初のキャプチャを即時実行
            await CaptureOnceInternalAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "最初のキャプチャに失敗");
            await ChangeStatusAsync(CaptureServiceStatus.Error, $"初期キャプチャ失敗: {ex.Message}").ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopCaptureAsync()
    {
        // 停止処理中状態に設定
        await ChangeStatusAsync(CaptureServiceStatus.Stopping, "停止処理開始").ConfigureAwait(false);

        lock (_syncLock)
        {
            if (_status == CaptureServiceStatus.Stopped || _status == CaptureServiceStatus.Stopping)
                return;

#pragma warning disable CA1849 // CancellationTokenSource.Cancel() is synchronous by design
            _cancellationTokenSource?.Cancel();
#pragma warning restore CA1849
            _logger?.LogInformation("キャプチャを停止");
        }

        if (_continuousCaptureTask != null)
        {
            try
            {
                await _continuousCaptureTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常なキャンセル
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "キャプチャタスクの停止中にエラーが発生");
            }
            catch (TimeoutException ex)
            {
                _logger?.LogError(ex, "キャプチャタスクの停止中にタイムアウトが発生");
            }
        }

        // 最終的に停止状態に設定
        await ChangeStatusAsync(CaptureServiceStatus.Stopped, "停止完了").ConfigureAwait(false);
    }

    public async Task PauseCaptureAsync()
    {
        lock (_syncLock)
        {
            if (_status != CaptureServiceStatus.Running)
                return;

            _logger?.LogInformation("キャプチャを一時停止");
        }

        await ChangeStatusAsync(CaptureServiceStatus.Paused, "一時停止要求").ConfigureAwait(false);
    }

    public async Task ResumeCaptureAsync()
    {
        lock (_syncLock)
        {
            if (_status != CaptureServiceStatus.Paused)
                return;

            _logger?.LogInformation("キャプチャを再開");
        }

        await ChangeStatusAsync(CaptureServiceStatus.Running, "再開要求").ConfigureAwait(false);
    }

    public CaptureSettings GetCurrentSettings()
    {
        lock (_syncLock)
        {
            // 設定の安全なコピーを返す
            return new CaptureSettings
            {
                IsEnabled = _currentSettings.IsEnabled,
                CaptureIntervalMs = _currentSettings.CaptureIntervalMs,
                CaptureQuality = _currentSettings.CaptureQuality,
                AutoDetectCaptureArea = _currentSettings.AutoDetectCaptureArea,
                FixedCaptureAreaX = _currentSettings.FixedCaptureAreaX,
                FixedCaptureAreaY = _currentSettings.FixedCaptureAreaY,
                FixedCaptureAreaWidth = _currentSettings.FixedCaptureAreaWidth,
                FixedCaptureAreaHeight = _currentSettings.FixedCaptureAreaHeight,
                TargetMonitor = _currentSettings.TargetMonitor,
                ConsiderDpiScaling = _currentSettings.ConsiderDpiScaling,
                UseHardwareAcceleration = _currentSettings.UseHardwareAcceleration,
                DifferenceDetectionSensitivity = _currentSettings.DifferenceDetectionSensitivity,
                DifferenceDetectionGridSize = _currentSettings.DifferenceDetectionGridSize,
                SaveCaptureHistory = _currentSettings.SaveCaptureHistory,
                MaxCaptureHistoryCount = _currentSettings.MaxCaptureHistoryCount,
                FullscreenOptimization = _currentSettings.FullscreenOptimization,
                AutoOptimizeForGames = _currentSettings.AutoOptimizeForGames,
                SaveDebugCaptures = _currentSettings.SaveDebugCaptures,
                DebugCaptureSavePath = _currentSettings.DebugCaptureSavePath
            };
        }
    }

    public void UpdateSettings(CaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_syncLock)
        {
            _currentSettings = settings;
            ApplySettingsToDifferenceDetector();

            _logger?.LogInformation("キャプチャ設定を更新: 間隔={Interval}ms, 品質={Quality}",
                _currentSettings.CaptureIntervalMs, _currentSettings.CaptureQuality);
        }
    }

    public void ApplyGameProfile(SettingsGameCaptureProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _currentGameProfile = profile;
        UpdateSettings(profile.CaptureSettings);

        _logger?.LogInformation("ゲームプロファイル '{ProfileName}' を適用", profile.Name);
    }

    public void ResetPerformanceStatistics()
    {
        lock (_captureTimes)
        {
            _performanceInfo.Reset();
            _captureTimes.Clear();
            _captureTimestamps.Clear();
        }

        _logger?.LogDebug("パフォーマンス統計をリセット");
    }

    public async Task OptimizeCaptureAsync(CancellationToken cancellationToken = default)
    {
        if (DateTime.Now - _lastOptimizationTime < _optimizationInterval)
        {
            _logger?.LogTrace("最適化間隔が短すぎるためスキップ");
            return;
        }

        _lastOptimizationTime = DateTime.Now;

        try
        {
            await PerformCaptureOptimizationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "キャプチャ最適化中にエラーが発生");
        }
        catch (TimeoutException ex)
        {
            _logger?.LogError(ex, "キャプチャ最適化中にタイムアウトが発生");
        }
    }

    public async Task StartWindowMonitoringAsync(IntPtr windowHandle, CancellationToken cancellationToken = default)
    {
        if (windowHandle == IntPtr.Zero)
            throw new ArgumentException("無効なウィンドウハンドル", nameof(windowHandle));

        _monitoredWindowHandle = windowHandle;

        // TODO: ウィンドウの状態変更監視の実装
        await Task.CompletedTask.ConfigureAwait(false);

        _logger?.LogInformation("ウィンドウ監視を開始: HWND={WindowHandle}", windowHandle);
    }

    public Task StopWindowMonitoringAsync()
    {
        _monitoredWindowHandle = IntPtr.Zero;

        _logger?.LogInformation("ウィンドウ監視を停止");
        return Task.CompletedTask;
    }

    #endregion

    #region ステータス管理メソッド

    /// <summary>
    /// サービスステータスを変更し、イベントを発行します
    /// </summary>
    /// <param name="newStatus">新しいステータス</param>
    /// <param name="reason">変更理由</param>
    private async Task ChangeStatusAsync(CaptureServiceStatus newStatus, string? reason = null)
    {
        CaptureServiceStatus previousStatus;

        lock (_syncLock)
        {
            previousStatus = _status;
            _status = newStatus;
        }

        if (previousStatus != newStatus)
        {
            var statusEvent = new AdvancedCaptureServiceStatusChangedEvent(
                previousStatus, newStatus, reason, GetCurrentSettings());

            await _eventAggregator.PublishAsync(statusEvent).ConfigureAwait(false);

            _logger?.LogDebug("キャプチャサービスステータスが {PreviousStatus} から {NewStatus} に変更: {Reason}",
                previousStatus, newStatus, reason ?? "明示的な理由なし");
        }
    }

    #endregion

    #region 内部実装メソッド

    private async Task RunContinuousCaptureAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_status == CaptureServiceStatus.Running)
                {
                    try
                    {
                        await CaptureOnceInternalAsync().ConfigureAwait(false);

                        // 自動最適化チェック
                        if (_currentSettings.AutoOptimizeForGames &&
                            DateTime.Now - _lastOptimizationTime > _optimizationInterval)
                        {
                            _ = Task.Run(() => OptimizeCaptureAsync(cancellationToken), cancellationToken);
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger?.LogError(ex, "連続キャプチャ処理中にエラーが発生");
                        UpdatePerformanceStats(failed: true);

                        await _eventAggregator.PublishAsync(new CaptureFailedEvent(
                            Rectangle.Empty, ex, ex.Message)).ConfigureAwait(false);
                    }
                    catch (TimeoutException ex)
                    {
                        _logger?.LogError(ex, "連続キャプチャ処理中にタイムアウトが発生");
                        UpdatePerformanceStats(failed: true);

                        await _eventAggregator.PublishAsync(new CaptureFailedEvent(
                            Rectangle.Empty, ex, ex.Message)).ConfigureAwait(false);
                    }
                }

                // 次のキャプチャまで待機
                try
                {
                    await Task.Delay(_currentSettings.CaptureIntervalMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("連続キャプチャがキャンセルされました");
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "連続キャプチャタスクでエラーが発生");
            await ChangeStatusAsync(CaptureServiceStatus.Error, $"連続キャプチャエラー: {ex.Message}").ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            _logger?.LogError(ex, "連続キャプチャタスクでタイムアウトが発生");
            await ChangeStatusAsync(CaptureServiceStatus.Error, $"連続キャプチャエラー: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            await ChangeStatusAsync(CaptureServiceStatus.Stopped, "タスク終了").ConfigureAwait(false);
        }
    }

    private async Task<IImage?> CaptureOnceInternalAsync()
    {
        _captureStopwatch.Restart();

        try
        {
            IImage? capturedImage = null;
            Rectangle captureRegion;

            // キャプチャ実行
            if (_monitoredWindowHandle != IntPtr.Zero)
            {
                capturedImage = await CaptureWindowAsync(_monitoredWindowHandle).ConfigureAwait(false);
                // ウィンドウの領域を取得（今後実装）
                captureRegion = new Rectangle(0, 0, capturedImage.Width, capturedImage.Height);
            }
            else if (!_currentSettings.AutoDetectCaptureArea)
            {
                captureRegion = new Rectangle(
                    _currentSettings.FixedCaptureAreaX,
                    _currentSettings.FixedCaptureAreaY,
                    _currentSettings.FixedCaptureAreaWidth,
                    _currentSettings.FixedCaptureAreaHeight);
                capturedImage = await CaptureRegionAsync(captureRegion).ConfigureAwait(false);
            }
            else
            {
                capturedImage = await CaptureScreenAsync().ConfigureAwait(false);
                captureRegion = new Rectangle(0, 0, capturedImage.Width, capturedImage.Height);
            }

            _captureStopwatch.Stop();

            // 差分検出
            bool hasChange = true;
            if (_lastCapturedImage != null)
            {
                hasChange = await _differenceDetector.HasSignificantChangeAsync(
                    _lastCapturedImage, capturedImage).ConfigureAwait(false);

                if (!hasChange)
                {
                    UpdatePerformanceStats(skipped: true);
                    capturedImage?.Dispose();
                    return null;
                }
            }

            // 前回の画像を破棄して新しい画像を保存
            _lastCapturedImage?.Dispose();
            _lastCapturedImage = capturedImage;
            _lastCaptureTime = DateTime.Now;

            // パフォーマンス統計更新
            UpdatePerformanceStats(captureTimeMs: _captureStopwatch.Elapsed.TotalMilliseconds);

            _logger?.LogTrace("キャプチャ完了: {Width}x{Height}, 処理時間={ElapsedMs}ms",
                capturedImage.Width, capturedImage.Height, _captureStopwatch.ElapsedMilliseconds);

            // イベント発行
            await _eventAggregator.PublishAsync(new CaptureCompletedEvent(
                capturedImage, captureRegion, _captureStopwatch.Elapsed)).ConfigureAwait(false);

            return capturedImage;
        }
        catch (InvalidOperationException ex)
        {
            _captureStopwatch.Stop();
            UpdatePerformanceStats(failed: true);

            _logger?.LogError(ex, "キャプチャ処理中にエラーが発生");
            await ChangeStatusAsync(CaptureServiceStatus.Error, $"キャプチャ処理エラー: {ex.Message}").ConfigureAwait(false);

            throw;
        }
        catch (TimeoutException ex)
        {
            _captureStopwatch.Stop();
            UpdatePerformanceStats(failed: true);

            _logger?.LogError(ex, "キャプチャ処理中にタイムアウトが発生");
            await ChangeStatusAsync(CaptureServiceStatus.Error, $"キャプチャ処理エラー: {ex.Message}").ConfigureAwait(false);

            throw;
        }
    }

    private void UpdatePerformanceStats(double captureTimeMs = 0, bool failed = false, bool skipped = false)
    {
        lock (_captureTimes)
        {
            _performanceInfo.TotalCaptureCount++;

            if (failed)
            {
                _performanceInfo.FailedCaptureCount++;
            }
            else if (skipped)
            {
                _performanceInfo.SkippedCaptureCount++;
            }
            else
            {
                _performanceInfo.SuccessfulCaptureCount++;

                if (captureTimeMs > 0)
                {
                    _captureTimes.Enqueue(captureTimeMs);
                    _captureTimestamps.Enqueue(DateTime.Now);

                    // 最大100回分の履歴を保持
                    if (_captureTimes.Count > 100)
                    {
                        _captureTimes.Dequeue();
                        _captureTimestamps.Dequeue();
                    }

                    // 統計値更新
                    _performanceInfo.AverageCaptureTimeMs = _captureTimes.Average();
                    _performanceInfo.MaxCaptureTimeMs = _captureTimes.Max();
                    _performanceInfo.MinCaptureTimeMs = _captureTimes.Min();

                    // 現在のキャプチャレート計算（直近30秒間）
                    var recentTimestamps = _captureTimestamps.Where(t => DateTime.Now - t < TimeSpan.FromSeconds(30));
                    _performanceInfo.CurrentCaptureRate = recentTimestamps.Count() / 30.0;
                }
            }

            // システムリソース情報更新
            UpdateSystemResourceInfo();

            // 更新時刻を記録
            _performanceInfo.LastUpdateTime = DateTime.Now;
        }
    }

    private void UpdateSystemResourceInfo()
    {
        try
        {
            if (_cpuCounter != null)
            {
                var cpuUsage = _cpuCounter.NextValue();
                _performanceInfo.AverageCpuUsage = (_performanceInfo.AverageCpuUsage + cpuUsage) / 2.0;
            }

            if (_memoryCounter != null)
            {
                var availableMemory = _memoryCounter.NextValue();
                // 概算の使用メモリ量を計算（実際にはもっと精密な計算が必要）
                _performanceInfo.AverageMemoryUsageMB = Math.Max(0, 8192 - availableMemory); // 8GB想定
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogTrace(ex, "システムリソース情報の更新に失敗");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogTrace(ex, "システムリソース情報へのアクセスが拒否されました");
        }
    }

    private void ApplySettingsToDifferenceDetector()
    {
        var detectionSettings = _differenceDetector.GetSettings();

        // 感度設定を0-100から0.0-1.0に変換
        detectionSettings.Threshold = 1.0 - (_currentSettings.DifferenceDetectionSensitivity / 100.0);
        detectionSettings.BlockSize = _currentSettings.DifferenceDetectionGridSize;
        detectionSettings.FocusOnTextRegions = true; // ゲーム翻訳用途なのでテキスト重視

        _differenceDetector.ApplySettings(detectionSettings);
    }

    private async Task PerformCaptureOptimizationAsync(CancellationToken _)
    {
        _logger?.LogDebug("キャプチャ最適化を実行");

        // CPU使用率が高い場合はキャプチャ間隔を増加
        if (_performanceInfo.AverageCpuUsage > 80.0)
        {
            _currentSettings.CaptureIntervalMs = Math.Min(
                _currentSettings.CaptureIntervalMs + 50,
                2000);

            _logger?.LogDebug("高CPU使用率を検出: キャプチャ間隔を {Interval}ms に調整",
                _currentSettings.CaptureIntervalMs);
        }
        // CPU使用率が低く、キャプチャ成功率が高い場合は間隔を短縮
        else if (_performanceInfo.AverageCpuUsage < 50.0 && _performanceInfo.SuccessRate > 95.0)
        {
            _currentSettings.CaptureIntervalMs = Math.Max(
                _currentSettings.CaptureIntervalMs - 25,
                100);

            _logger?.LogDebug("良好なパフォーマンスを検出: キャプチャ間隔を {Interval}ms に調整",
                _currentSettings.CaptureIntervalMs);
        }

        // 平均キャプチャ時間が間隔より長い場合は調整
        if (_performanceInfo.AverageCaptureTimeMs > _currentSettings.CaptureIntervalMs * 0.8)
        {
            _currentSettings.CaptureIntervalMs = (int)(_performanceInfo.AverageCaptureTimeMs * 1.5);

            _logger?.LogDebug("キャプチャ時間に基づき間隔を {Interval}ms に調整",
                _currentSettings.CaptureIntervalMs);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    #endregion

    #region IDisposable実装

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                // 同期コンテキストでのDisposeのため、同期的に実行
                if (_status != CaptureServiceStatus.Stopped)
                {
                    lock (_syncLock)
                    {
                        _cancellationTokenSource?.Cancel();
                        _status = CaptureServiceStatus.Stopped;
                    }

                    _continuousCaptureTask?.Wait(TimeSpan.FromSeconds(5));
                }
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                // キャンセルは正常終了
            }
            catch (TimeoutException)
            {
                // タイムアウトはログしない（Dispose中なので）
            }
            catch (InvalidOperationException)
            {
                // Dispose中なのでエラーログを出さない
            }

            _cancellationTokenSource?.Dispose();
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
            _lastCapturedImage?.Dispose();
        }
    }

    #endregion
}
