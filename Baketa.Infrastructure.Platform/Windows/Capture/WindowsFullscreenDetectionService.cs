using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Services;
using Baketa.Infrastructure.Platform.Windows.NativeMethods;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Capture;

/// <summary>
/// Windows固有のフルスクリーン検出サービス実装
/// Win32 APIを使用してフルスクリーン状態を高精度で検出
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFullscreenDetectionService : IFullscreenDetectionService, IDisposable
{
    private readonly ILogger<WindowsFullscreenDetectionService>? _logger;
    /// <summary>
    /// 現在の検出設定
    /// </summary>
    public FullscreenDetectionSettings Settings { get; private set; }

    private bool _isRunning;
    private bool _disposed;
    private CancellationTokenSource? _monitoringCancellation;
    private FullscreenInfo? _lastDetectionResult;

    /// <summary>
    /// フルスクリーン状態変更イベント
    /// </summary>
    public event EventHandler<FullscreenInfo>? FullscreenStateChanged;

    /// <summary>
    /// 検出サービスが実行中かどうか
    /// </summary>
    public bool IsRunning => _isRunning && !_disposed;

    public WindowsFullscreenDetectionService(ILogger<WindowsFullscreenDetectionService>? logger = null)
    {
        _logger = logger;
        Settings = new FullscreenDetectionSettings();

        _logger?.LogDebug("WindowsFullscreenDetectionService initialized");
    }

    /// <summary>
    /// 指定されたウィンドウがフルスクリーンかどうかを検出します
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>フルスクリーン情報</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public async Task<FullscreenInfo> DetectFullscreenAsync(IntPtr windowHandle)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WindowsFullscreenDetectionService));

        if (windowHandle == IntPtr.Zero)
        {
            _logger?.LogWarning("Invalid window handle provided");
            return CreateEmptyFullscreenInfo();
        }

        // 非同期実行でUIスレッドをブロックしない
        return await Task.Run(() => DetectFullscreenInternal(windowHandle)).ConfigureAwait(false);
    }

    /// <summary>
    /// 現在のフォアグラウンドウィンドウがフルスクリーンかどうかを検出します
    /// </summary>
    /// <returns>フルスクリーン情報</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public async Task<FullscreenInfo> DetectCurrentFullscreenAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WindowsFullscreenDetectionService));

        var foregroundWindow = User32Methods.GetForegroundWindow();
        return await DetectFullscreenAsync(foregroundWindow).ConfigureAwait(false);
    }

    /// <summary>
    /// フルスクリーン状態の変更を監視します
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>フルスクリーン状態変更の通知</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public async IAsyncEnumerable<FullscreenInfo> MonitorFullscreenChangesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WindowsFullscreenDetectionService));

        _logger?.LogInformation("Starting fullscreen monitoring with interval {IntervalMs}ms", Settings.DetectionIntervalMs);

        FullscreenInfo? lastInfo = null;

        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            FullscreenInfo? currentInfo = null;
            bool shouldYield = false;

            try
            {
                currentInfo = await DetectCurrentFullscreenAsync().ConfigureAwait(false);

                // 状態が変更された場合のみ通知
                if (HasStateChanged(lastInfo, currentInfo))
                {
                    _logger?.LogDebug("Fullscreen state changed: {Info}", currentInfo);
                    _lastDetectionResult = currentInfo;
                    lastInfo = currentInfo;
                    shouldYield = true;

                    // イベント発行
                    FullscreenStateChanged?.Invoke(this, currentInfo);
                }

                await Task.Delay(Settings.DetectionIntervalMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogDebug("Fullscreen monitoring cancelled");
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "Invalid operation during fullscreen monitoring");
                await Task.Delay(Math.Min(Settings.DetectionIntervalMs * 2, 5000), cancellationToken).ConfigureAwait(false);
            }
            catch (Win32Exception ex)
            {
                _logger?.LogError(ex, "Win32 exception during fullscreen monitoring");
                await Task.Delay(Math.Min(Settings.DetectionIntervalMs * 2, 5000), cancellationToken).ConfigureAwait(false);
            }
            catch (ExternalException ex)
            {
                _logger?.LogError(ex, "External exception during fullscreen monitoring");
                await Task.Delay(Math.Min(Settings.DetectionIntervalMs * 2, 5000), cancellationToken).ConfigureAwait(false);
            }

            if (shouldYield && currentInfo != null)
            {
                yield return currentInfo;
            }
        }

        _logger?.LogInformation("Fullscreen monitoring stopped");
    }

    /// <summary>
    /// フルスクリーン検出設定を更新します
    /// </summary>
    /// <param name="settings">検出設定</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public void UpdateDetectionSettings(FullscreenDetectionSettings settings)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WindowsFullscreenDetectionService));

        ArgumentNullException.ThrowIfNull(settings);

        Settings = settings.Clone();
        _logger?.LogDebug("Fullscreen detection settings updated");
    }

    /// <summary>
    /// 監視を開始します
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WindowsFullscreenDetectionService));

        if (_isRunning)
        {
            _logger?.LogWarning("Fullscreen monitoring is already running");
            return;
        }

        _monitoringCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _isRunning = true;

        _logger?.LogInformation("Fullscreen monitoring started");

        try
        {
            var asyncEnumerator = MonitorFullscreenChangesAsync(_monitoringCancellation.Token).GetAsyncEnumerator(_monitoringCancellation.Token);
            try
            {
                while (await asyncEnumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    // 監視結果は自動的にイベントとして発行される
                    // asyncEnumerator.Currentは使用しないため無視
                }
            }
            finally
            {
                await asyncEnumerator.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Fullscreen monitoring was cancelled");
        }
        finally
        {
            _isRunning = false;
        }
    }

    /// <summary>
    /// 監視を停止します
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1849:Call async methods when in an async method", Justification = "CancellationTokenSource.Cancel() is a lightweight synchronous operation and is appropriate here")]
    public async Task StopMonitoringAsync()
    {
        if (!_isRunning || _disposed)
        {
            return;
        }

        _logger?.LogInformation("Stopping fullscreen monitoring");

        _monitoringCancellation?.Cancel();
        _isRunning = false;

        // 少し待って監視ループが終了するのを待つ
        if (_monitoringCancellation != null)
        {
            await Task.Delay(100).ConfigureAwait(false);
            _monitoringCancellation.Dispose();
            _monitoringCancellation = null;
        }

        _logger?.LogInformation("Fullscreen monitoring stopped");
    }

    /// <summary>
    /// フルスクリーン検出の内部実装
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>フルスクリーン情報</returns>
    private FullscreenInfo DetectFullscreenInternal(IntPtr windowHandle)
    {
        try
        {
            // ウィンドウの有効性確認
            if (!User32Methods.IsWindow(windowHandle) || !User32Methods.IsWindowVisible(windowHandle))
            {
                return CreateEmptyFullscreenInfo();
            }

            var info = new FullscreenInfo
            {
                WindowHandle = windowHandle,
                DetectionTime = DateTime.Now
            };

            // ウィンドウ情報取得
            GetWindowInfo(windowHandle, info);

            // モニター情報取得
            GetMonitorInfo(windowHandle, info);

            // フルスクリーン検出実行
            DetectFullscreenState(info);

            return info;
        }
        catch (Win32Exception ex)
        {
            _logger?.LogError(ex, "Win32 error while detecting fullscreen state for window {WindowHandle}", windowHandle);
            return CreateEmptyFullscreenInfo();
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "Invalid argument while detecting fullscreen state for window {WindowHandle}", windowHandle);
            return CreateEmptyFullscreenInfo();
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Invalid operation while detecting fullscreen state for window {WindowHandle}", windowHandle);
            return CreateEmptyFullscreenInfo();
        }
        catch (ExternalException ex)
        {
            _logger?.LogError(ex, "External exception while detecting fullscreen state for window {WindowHandle}", windowHandle);
            return CreateEmptyFullscreenInfo();
        }
    }

    /// <summary>
    /// ウィンドウ情報を取得
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <param name="info">フルスクリーン情報</param>
    private void GetWindowInfo(IntPtr windowHandle, FullscreenInfo info)
    {
        // ウィンドウ位置・サイズ取得
        if (User32Methods.GetWindowRect(windowHandle, out RECT windowRect))
        {
            info.WindowBounds = new Rectangle(windowRect.left, windowRect.top, windowRect.Width, windowRect.Height);
        }

        // ウィンドウタイトル取得
        var titleLength = User32Methods.GetWindowTextLength(windowHandle);
        if (titleLength > 0)
        {
            var titleBuilder = new System.Text.StringBuilder(titleLength + 1);
            User32Methods.GetWindowText(windowHandle, titleBuilder, titleLength + 1);
            info.WindowTitle = titleBuilder.ToString();
        }

        // プロセス情報取得
        if (User32Methods.GetWindowThreadProcessId(windowHandle, out uint processId) != 0)
        {
            try
            {
                using var process = Process.GetProcessById((int)processId);
                info.ProcessName = process.ProcessName;

                // ゲーム判定
                info.IsLikelyGame = IsLikelyGameProcess(process.ProcessName, info.WindowTitle);
            }
            catch (ArgumentException ex)
            {
                _logger?.LogDebug(ex, "Invalid process ID: {ProcessId}", processId);
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogDebug(ex, "Process {ProcessId} has exited", processId);
            }
            catch (Win32Exception ex)
            {
                _logger?.LogDebug(ex, "Win32 exception accessing process {ProcessId}", processId);
            }
        }
    }

    /// <summary>
    /// モニター情報を取得
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <param name="info">フルスクリーン情報</param>
    private void GetMonitorInfo(IntPtr windowHandle, FullscreenInfo info)
    {
        var monitor = User32Methods.MonitorFromWindow(windowHandle, MonitorFlags.MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = MONITORINFO.Create();
            if (User32Methods.GetMonitorInfo(monitor, ref monitorInfo))
            {
                var rect = monitorInfo.rcMonitor;
                info.MonitorBounds = new Rectangle(rect.left, rect.top, rect.Width, rect.Height);
            }
        }

        // フォールバック: プライマリモニター情報を使用
        if (info.MonitorBounds.IsEmpty)
        {
            var screenWidth = User32Methods.GetSystemMetrics(SystemMetric.SM_CXSCREEN);
            var screenHeight = User32Methods.GetSystemMetrics(SystemMetric.SM_CYSCREEN);
            info.MonitorBounds = new Rectangle(0, 0, screenWidth, screenHeight);
        }
    }

    /// <summary>
    /// フルスクリーン状態を検出
    /// </summary>
    /// <param name="info">フルスクリーン情報</param>
    private void DetectFullscreenState(FullscreenInfo info)
    {
        double confidence = 0.0;
        var detectionMethod = FullscreenDetectionMethod.Combined;

        // 1. ウィンドウサイズによる検出
        double sizeConfidence = DetectByWindowSize(info);

        // 2. ウィンドウスタイルによる検出
        double styleConfidence = DetectByWindowStyle(info.WindowHandle);

        // 3. 最大化状態による検出
        double maximizedConfidence = DetectByMaximizedState(info.WindowHandle);

        // 複合判定と最適な検出方法の決定
        if (sizeConfidence >= styleConfidence && sizeConfidence >= maximizedConfidence)
        {
            confidence = sizeConfidence;
            detectionMethod = FullscreenDetectionMethod.WindowSize;
        }
        else if (styleConfidence >= maximizedConfidence)
        {
            confidence = styleConfidence;
            detectionMethod = FullscreenDetectionMethod.WindowStyle;
        }
        else
        {
            confidence = maximizedConfidence;
            detectionMethod = FullscreenDetectionMethod.WindowSize; // Maximizedは内部的にWindowSizeとして分類
        }

        // 複数の方法で高い信頼度がある場合はCombinedとして扱う
        var highConfidenceMethods = new[] { sizeConfidence, styleConfidence, maximizedConfidence }
            .Count(c => c >= Settings.MinConfidence);
        if (highConfidenceMethods > 1)
        {
            confidence = Math.Max(sizeConfidence, Math.Max(styleConfidence, maximizedConfidence));
            detectionMethod = FullscreenDetectionMethod.Combined;
        }

        // ゲームプロセスの場合は信頼度を上げる
        if (info.IsLikelyGame && confidence > 0.5)
        {
            confidence = Math.Min(1.0, confidence + 0.1);
        }

        info.Confidence = confidence;
        info.DetectionMethod = detectionMethod;
        info.IsFullscreen = confidence >= Settings.MinConfidence;

        _logger?.LogDebug("Fullscreen detection: Window={Window}, Size={SizeConf:F2}, Style={StyleConf:F2}, " +
                         "Maximized={MaxConf:F2}, Final={FinalConf:F2}, IsFullscreen={IsFullscreen}",
            info.ProcessName, sizeConfidence, styleConfidence, maximizedConfidence, confidence, info.IsFullscreen);
    }

    /// <summary>
    /// ウィンドウサイズによるフルスクリーン検出
    /// </summary>
    /// <param name="info">フルスクリーン情報</param>
    /// <returns>信頼度（0.0-1.0）</returns>
    private double DetectByWindowSize(FullscreenInfo info)
    {
        if (info.WindowBounds.IsEmpty || info.MonitorBounds.IsEmpty)
        {
            return 0.0;
        }

        var widthDiff = Math.Abs(info.WindowBounds.Width - info.MonitorBounds.Width);
        var heightDiff = Math.Abs(info.WindowBounds.Height - info.MonitorBounds.Height);

        var positionMatch = Math.Abs(info.WindowBounds.X - info.MonitorBounds.X) <= Settings.SizeTolerance &&
                          Math.Abs(info.WindowBounds.Y - info.MonitorBounds.Y) <= Settings.SizeTolerance;

        if (widthDiff <= Settings.SizeTolerance && heightDiff <= Settings.SizeTolerance && positionMatch)
        {
            return 1.0; // 完全一致
        }

        // 部分的一致の評価
        var widthRatio = 1.0 - Math.Min(1.0, (double)widthDiff / info.MonitorBounds.Width);
        var heightRatio = 1.0 - Math.Min(1.0, (double)heightDiff / info.MonitorBounds.Height);

        return (widthRatio + heightRatio) / 2.0;
    }

    /// <summary>
    /// ウィンドウスタイルによるフルスクリーン検出
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>信頼度（0.0-1.0）</returns>
    private double DetectByWindowStyle(IntPtr windowHandle)
    {
        try
        {
            var style = (WindowStyles)User32Methods.GetWindowLong(windowHandle, GetWindowLongIndex.GWL_STYLE);

            // フルスクリーンの典型的なスタイル: WS_POPUP without borders
            bool isPopup = style.HasFlag(WindowStyles.WS_POPUP);
            bool hasBorder = style.HasFlag(WindowStyles.WS_BORDER);
            bool hasCaption = style.HasFlag(WindowStyles.WS_CAPTION);
            bool hasThickFrame = style.HasFlag(WindowStyles.WS_THICKFRAME);

            if (isPopup && !hasBorder && !hasCaption && !hasThickFrame)
            {
                return 0.9; // 高い信頼度
            }

            if (isPopup)
            {
                return 0.6; // 中程度の信頼度
            }

            return 0.0;
        }
        catch (Win32Exception ex)
        {
            _logger?.LogDebug(ex, "Win32 exception while checking window style for {WindowHandle}", windowHandle);
            return 0.0;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogDebug(ex, "Invalid operation while checking window style for {WindowHandle}", windowHandle);
            return 0.0;
        }
    }

    /// <summary>
    /// 最大化状態によるフルスクリーン検出
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>信頼度（0.0-1.0）</returns>
    private double DetectByMaximizedState(IntPtr windowHandle)
    {
        try
        {
            return User32Methods.IsZoomed(windowHandle) ? 0.7 : 0.0;
        }
        catch (Win32Exception ex)
        {
            _logger?.LogDebug(ex, "Win32 exception while checking maximized state for {WindowHandle}", windowHandle);
            return 0.0;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogDebug(ex, "Invalid operation while checking maximized state for {WindowHandle}", windowHandle);
            return 0.0;
        }
    }

    /// <summary>
    /// プロセスがゲームかどうかを判定
    /// </summary>
    /// <param name="processName">プロセス名</param>
    /// <param name="windowTitle">ウィンドウタイトル</param>
    /// <returns>ゲームかどうか</returns>
    private bool IsLikelyGameProcess(string processName, string windowTitle)
    {
        if (string.IsNullOrEmpty(processName))
        {
            return false;
        }

        // 除外プロセスのチェック
        if (Settings.ExcludedProcesses.Contains(processName))
        {
            return false;
        }

        // 既知のゲーム実行ファイルのチェック
        if (Settings.KnownGameExecutables.Any(exe =>
            processName.Contains(exe, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // ゲームらしいキーワードのチェック
        var gameKeywords = new[] { "game", "ゲーム", "play", "steam", "unity", "unreal" };
        return gameKeywords.Any(keyword =>
            processName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            windowTitle.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 状態変更があったかどうかを判定
    /// </summary>
    /// <param name="lastInfo">前回の情報</param>
    /// <param name="currentInfo">現在の情報</param>
    /// <returns>変更があったかどうか</returns>
    private static bool HasStateChanged(FullscreenInfo? lastInfo, FullscreenInfo currentInfo)
    {
        if (lastInfo == null)
        {
            return true;
        }

        return lastInfo.IsFullscreen != currentInfo.IsFullscreen ||
               lastInfo.WindowHandle != currentInfo.WindowHandle ||
               Math.Abs(lastInfo.Confidence - currentInfo.Confidence) > 0.1;
    }

    /// <summary>
    /// 空のフルスクリーン情報を作成
    /// </summary>
    /// <returns>空のフルスクリーン情報</returns>
    private static FullscreenInfo CreateEmptyFullscreenInfo()
    {
        return new FullscreenInfo
        {
            IsFullscreen = false,
            Confidence = 0.0,
            DetectionMethod = FullscreenDetectionMethod.WindowSize,
            WindowHandle = IntPtr.Zero,
            DetectionTime = DateTime.Now
        };
    }

    /// <summary>
    /// リソースを解放します
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _monitoringCancellation?.Cancel();
        _monitoringCancellation?.Dispose();

        _disposed = true;
        _logger?.LogDebug("WindowsFullscreenDetectionService disposed");
    }
}
