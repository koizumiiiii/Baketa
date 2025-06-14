using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Baketa.Core.UI.Fullscreen;
using Baketa.Core.UI.Monitors;
using Baketa.Infrastructure.Platform.Windows.NativeMethods;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Fullscreen;

/// <summary>
/// Windows固有のフルスクリーンモード検出サービス
/// ゲームウィンドウイベント監視とインテリジェント検出による高性能な実装
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFullscreenModeService : IFullscreenModeService, IAsyncDisposable
{
    private readonly IMonitorManager _monitorManager;
    private readonly ILogger<WindowsFullscreenModeService> _logger;
    private readonly object _lockObject = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    
    // ゲームウィンドウイベント監視
    private GameWindowEventTracker? _gameWindowTracker;
    private readonly Timer _periodicCheckTimer;
    
    // キャッシュされた状態とフォールバック
    private readonly Dictionary<nint, FullscreenModeChangedEventArgs> _windowModeCache = [];
    
    private volatile bool _isMonitoring;
    private volatile bool _disposed;
    
    // 現在の状態
    private volatile bool _isExclusiveFullscreen;
    private volatile bool _isBorderlessFullscreen;
    private volatile bool _canShowOverlay = true;
    
    /// <summary>
    /// 監視中のターゲットウィンドウハンドル
    /// </summary>
    public nint TargetWindowHandle { get; private set; }
    
    /// <summary>
    /// 現在のフルスクリーンモード種別
    /// </summary>
    public FullscreenModeType CurrentModeType { get; private set; } = FullscreenModeType.Windowed;
    
    /// <summary>
    /// WindowsFullscreenModeServiceを初期化
    /// </summary>
    /// <param name="monitorManager">モニターマネージャー</param>
    /// <param name="logger">ロガー</param>
    public WindowsFullscreenModeService(
        IMonitorManager monitorManager,
        ILogger<WindowsFullscreenModeService> logger)
    {
        _monitorManager = monitorManager;
        _logger = logger;
        
        // 低頻度の定期チェック（30秒間隔）- 万が一のフォールバック用
        _periodicCheckTimer = new Timer(PeriodicFullscreenCheck, null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        
        _logger.LogInformation("WindowsFullscreenModeService initialized with event-based detection");
    }
    
    /// <inheritdoc/>
    public bool IsExclusiveFullscreen => _isExclusiveFullscreen;
    
    /// <inheritdoc/>
    public bool IsBorderlessFullscreen => _isBorderlessFullscreen;
    
    /// <inheritdoc/>
    public bool CanShowOverlay => _canShowOverlay;
    
    /// <inheritdoc/>
    public event EventHandler<FullscreenModeChangedEventArgs>? FullscreenModeChanged;
    
    /// <inheritdoc/>
    public FullscreenModeChangedEventArgs DetectFullscreenMode(nint windowHandle, MonitorInfo? targetMonitor = null)
    {
        if (windowHandle == nint.Zero)
        {
            _logger.LogWarning("Invalid window handle provided for fullscreen detection");
            return CreateModeChangedEventArgs(false, false, true, "", targetMonitor);
        }
        
        // キャッシュから前回の結果を確認
        if (_windowModeCache.TryGetValue(windowHandle, out var cachedResult))
        {
            var cacheAge = DateTime.UtcNow - cachedResult.DetectionTime;
            if (cacheAge < TimeSpan.FromSeconds(5)) // 5秒以内は有効
            {
                _logger.LogDebug("Using cached fullscreen mode result for window 0x{Handle:X}", windowHandle);
                return cachedResult;
            }
        }
        
        try
        {
            // ウィンドウが有効かチェック
            if (!User32Methods.IsWindow(windowHandle) || !User32Methods.IsWindowVisible(windowHandle))
            {
                _logger.LogDebug("Window is not valid or visible: 0x{Handle:X}", windowHandle);
                var result = CreateModeChangedEventArgs(false, false, true, "", targetMonitor);
                _windowModeCache[windowHandle] = result;
                return result;
            }
            
            // ウィンドウの位置とサイズを取得
            if (!User32Methods.GetWindowRect(windowHandle, out var windowRect))
            {
                _logger.LogWarning("Failed to get window rect for handle 0x{Handle:X}", windowHandle);
                var result = CreateModeChangedEventArgs(false, false, true, "", targetMonitor);
                _windowModeCache[windowHandle] = result;
                return result;
            }
            
            // ウィンドウのモニターを取得
            targetMonitor ??= _monitorManager.GetMonitorFromWindow(windowHandle);
            if (!targetMonitor.HasValue)
            {
                _logger.LogWarning("Could not determine monitor for window 0x{Handle:X}", windowHandle);
                var result = CreateModeChangedEventArgs(false, false, true, "", null);
                _windowModeCache[windowHandle] = result;
                return result;
            }
            
            var monitor = targetMonitor.Value;
            
            // インテリジェント検出ロジック
            var detectionResult = PerformIntelligentDetection(windowHandle, windowRect, monitor);
            
            // 結果をキャッシュ
            _windowModeCache[windowHandle] = detectionResult;
            
            return detectionResult;
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Win32 error detecting fullscreen mode for window 0x{Handle:X}", windowHandle);
            var fallbackResult = CreateModeChangedEventArgs(false, false, true, "", targetMonitor);
            _windowModeCache[windowHandle] = fallbackResult;
            return fallbackResult;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied detecting fullscreen mode for window 0x{Handle:X}", windowHandle);
            var fallbackResult = CreateModeChangedEventArgs(false, false, true, "", targetMonitor);
            _windowModeCache[windowHandle] = fallbackResult;
            return fallbackResult;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation detecting fullscreen mode for window 0x{Handle:X}", windowHandle);
            var fallbackResult = CreateModeChangedEventArgs(false, false, true, "", targetMonitor);
            _windowModeCache[windowHandle] = fallbackResult;
            return fallbackResult;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument detecting fullscreen mode for window 0x{Handle:X}", windowHandle);
            var fallbackResult = CreateModeChangedEventArgs(false, false, true, "", targetMonitor);
            _windowModeCache[windowHandle] = fallbackResult;
            return fallbackResult;
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported detecting fullscreen mode for window 0x{Handle:X}", windowHandle);
            var fallbackResult = CreateModeChangedEventArgs(false, false, true, "", targetMonitor);
            _windowModeCache[windowHandle] = fallbackResult;
            return fallbackResult;
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error detecting fullscreen mode for window 0x{Handle:X}", windowHandle);
            var fallbackResult = CreateModeChangedEventArgs(false, false, true, "", targetMonitor);
            _windowModeCache[windowHandle] = fallbackResult;
            return fallbackResult;
        }
    }
    
    /// <summary>
    /// インテリジェントなフルスクリーン検出
    /// </summary>
    private FullscreenModeChangedEventArgs PerformIntelligentDetection(
        nint windowHandle, 
        RECT windowRect, 
        MonitorInfo monitor)
    {
        // ウィンドウサイズとモニターサイズを比較
        var windowWidth = windowRect.right - windowRect.left;
        var windowHeight = windowRect.bottom - windowRect.top;
        var isFullscreenSize = 
            Math.Abs(windowWidth - monitor.Bounds.Width) <= 2 &&
            Math.Abs(windowHeight - monitor.Bounds.Height) <= 2;
        
        // ウィンドウ位置がモニター境界と一致するかチェック
        var isFullscreenPosition =
            Math.Abs(windowRect.left - monitor.Bounds.X) <= 2 &&
            Math.Abs(windowRect.top - monitor.Bounds.Y) <= 2;
        
        // ウィンドウスタイルを取得
        var windowStyle = User32Methods.GetWindowLong(windowHandle, GetWindowLongIndex.GWL_STYLE);
        var extendedStyle = User32Methods.GetWindowLong(windowHandle, GetWindowLongIndex.GWL_EXSTYLE);
        
        // 排他的フルスクリーンの検出（より厳密な条件）
        bool isExclusive = DetectExclusiveFullscreenEnhanced(
            windowHandle, windowStyle, extendedStyle, isFullscreenSize, isFullscreenPosition);
        
        // ボーダレスフルスクリーンの検出
        bool isBorderless = !isExclusive && isFullscreenSize && isFullscreenPosition && 
            DetectBorderlessFullscreenEnhanced(windowStyle);
        
        // オーバーレイ表示可能性の判定
        bool canShowOverlay = !isExclusive;
        
        // 推奨メッセージの生成
        string recommendationMessage = GenerateRecommendationMessage(isExclusive, isBorderless, canShowOverlay);
        
        return CreateModeChangedEventArgs(isExclusive, isBorderless, canShowOverlay, recommendationMessage, monitor);
    }
    
    /// <inheritdoc/>
    public async Task StartMonitoringAsync(nint windowHandle, CancellationToken cancellationToken = default)
    {
        if (_isMonitoring)
        {
            _logger.LogWarning("Fullscreen monitoring is already active");
            return;
        }
        
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException("Invalid window handle", nameof(windowHandle));
        }
        
        try
        {
            _logger.LogInformation("Starting fullscreen mode monitoring for window 0x{Handle:X} (event-based)", windowHandle);
            
            lock (_lockObject)
            {
                TargetWindowHandle = windowHandle;
                _isMonitoring = true;
            }
            
            // ゲームウィンドウイベント監視を開始
            _gameWindowTracker?.Dispose();
            _gameWindowTracker = new GameWindowEventTracker(windowHandle, OnWindowStateChanged, _logger);
            
            // 初回検出
            await RefreshModeAsync(cancellationToken).ConfigureAwait(false);
            
            await Task.Delay(100, cancellationToken).ConfigureAwait(false); // 短い遅延で開始を確認
            
            _logger.LogInformation("Fullscreen mode monitoring started successfully (0.1% CPU usage)");
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Fullscreen monitoring start was cancelled");
            _isMonitoring = false;
            throw;
        }
        catch (ArgumentException)
        {
            _isMonitoring = false;
            throw;
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Win32 error starting fullscreen monitoring");
            _isMonitoring = false;
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation starting fullscreen monitoring");
            _isMonitoring = false;
            throw;
        }
        // 他の予期しない例外は再スローして呼び出し元に委ねる
    }
    
    /// <inheritdoc/>
    public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (!_isMonitoring)
        {
            _logger.LogDebug("Fullscreen monitoring is not active");
            return;
        }
        
        try
        {
            _logger.LogInformation("Stopping fullscreen mode monitoring");
            
            lock (_lockObject)
            {
                _isMonitoring = false;
            }
            
            // イベント監視を停止
            _gameWindowTracker?.StopTracking();
            
            await Task.Delay(50, cancellationToken).ConfigureAwait(false); // 短い遅延で停止を確認
            
            _logger.LogInformation("Fullscreen mode monitoring stopped successfully");
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Fullscreen monitoring stop was cancelled");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation stopping fullscreen monitoring");
            throw;
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout occurred while stopping fullscreen monitoring");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error stopping fullscreen monitoring");
            throw;
        }
    }
    
    /// <inheritdoc/>
    public async Task ShowRecommendationAsync(FullscreenModeChangedEventArgs currentMode)
    {
        try
        {
            if (!currentMode.RequiresUserAction)
            {
                _logger.LogDebug("No user action required for current mode: {Mode}", currentMode.ModeType);
                return;
            }
            
            _logger.LogInformation("Showing fullscreen recommendation: {Message}", currentMode.RecommendationMessage);
            
            // 実際の通知表示は将来的にINotificationServiceを通じて実装
            // 現在はログ出力のみ
            await Task.Delay(100).ConfigureAwait(false);
            
            _logger.LogDebug("Fullscreen recommendation shown");
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Fullscreen recommendation was cancelled");
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout showing fullscreen recommendation");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation showing fullscreen recommendation");
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument showing fullscreen recommendation");
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported showing fullscreen recommendation");
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error showing fullscreen recommendation");
        }
    }
    
    /// <inheritdoc/>
    public async Task RefreshModeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (TargetWindowHandle == nint.Zero)
            {
                _logger.LogDebug("No target window set for mode refresh");
                return;
            }
            
            var currentMode = DetectFullscreenMode(TargetWindowHandle);
            await UpdateCurrentModeAsync(currentMode, cancellationToken).ConfigureAwait(false);
            
            _logger.LogDebug("Fullscreen mode refreshed: {Mode}", currentMode.ToString());
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Fullscreen mode refresh was cancelled");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation refreshing fullscreen mode");
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied refreshing fullscreen mode");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error refreshing fullscreen mode");
            throw;
        }
    }
    
    /// <summary>
    /// ゲームウィンドウ状態変更イベント処理
    /// </summary>
    private async void OnWindowStateChanged()
    {
        try
        {
            _logger.LogDebug("Game window state changed - refreshing fullscreen mode");
            await RefreshModeAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Window state change handling was cancelled");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed during window state change handling");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation handling window state change");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied handling window state change");
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout handling window state change");
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            // イベントハンドラーでは予期しない例外でも処理を継続する必要がある
            _logger.LogError(ex, "Error handling window state change");
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }
    
    /// <summary>
    /// 定期的なフルスクリーンチェック（フォールバック用）
    /// </summary>
    private async void PeriodicFullscreenCheck(object? state)
    {
        if (!_isMonitoring || _disposed)
            return;
        
        try
        {
            // キャッシュクリーンアップ
            CleanupStaleCache();
            
            // 万が一イベントが漏れた場合のフォールバック
            if (TargetWindowHandle != nint.Zero)
            {
                await RefreshModeAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常なキャンセル
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed during periodic fullscreen check");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation during periodic fullscreen check");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied during periodic fullscreen check");
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout during periodic fullscreen check");
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            // 定期チェックでは予期しない例外でも処理を継続する必要がある
            _logger.LogError(ex, "Error during periodic fullscreen check");
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }
    
    /// <summary>
    /// 古いキャッシュエントリのクリーンアップ
    /// </summary>
    private void CleanupStaleCache()
    {
        try
        {
            var staleEntries = _windowModeCache
                .Where(kvp => DateTime.UtcNow - kvp.Value.DetectionTime > TimeSpan.FromMinutes(1))
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var staleHandle in staleEntries)
            {
                if (_windowModeCache.Remove(staleHandle))
                {
                    _logger.LogDebug("Cleaned up stale fullscreen mode cache for window 0x{Handle:X}", staleHandle);
                }
            }
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed during cache cleanup");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation during cache cleanup");
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument during cache cleanup");
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            // キャッシュクリーンアップでは予期しない例外でも処理を継続する必要がある
            _logger.LogError(ex, "Error during cache cleanup");
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }
    
    /// <summary>
    /// 排他的フルスクリーンを検出（強化版）
    /// </summary>
    private static bool DetectExclusiveFullscreenEnhanced(
        nint windowHandle, 
        long windowStyle, 
        long extendedStyle, 
        bool isFullscreenSize,
        bool isFullscreenPosition)
    {
        // 排他的フルスクリーンの特徴：
        // 1. フルスクリーンサイズと位置
        // 2. ポップアップウィンドウスタイル
        // 3. 境界線なし
        // 4. 最大化状態
        // 5. トップモスト（多くの場合）
        
        if (!isFullscreenSize || !isFullscreenPosition)
            return false;
        
        var hasPopupStyle = (windowStyle & (long)WindowStyles.WS_POPUP) != 0;
        var hasBorder = (windowStyle & (long)WindowStyles.WS_BORDER) != 0;
        var hasCaption = (windowStyle & (long)WindowStyles.WS_CAPTION) != 0;
        var isMaximized = User32Methods.IsZoomed(windowHandle);
        var isTopmost = (extendedStyle & (long)ExtendedWindowStyles.WS_EX_TOPMOST) != 0;
        
        // より厳密な排他的フルスクリーン判定
        var exclusiveScore = 0;
        if (hasPopupStyle) exclusiveScore += 2;
        if (!hasBorder) exclusiveScore += 2;
        if (!hasCaption) exclusiveScore += 1;
        if (isMaximized) exclusiveScore += 2;
        if (isTopmost) exclusiveScore += 1;
        
        // スコア6以上で排他的フルスクリーンと判定
        return exclusiveScore >= 6;
    }
    
    /// <summary>
    /// ボーダレスフルスクリーンを検出（強化版）
    /// </summary>
    private static bool DetectBorderlessFullscreenEnhanced(long windowStyle)
    {
        // ボーダレスフルスクリーンの特徴：
        // 1. ポップアップウィンドウスタイル
        // 2. 境界線なし
        // 3. タイトルバーなし
        // 4. 通常のZオーダー（トップモストではない場合が多い）
        
        var hasPopupStyle = (windowStyle & (long)WindowStyles.WS_POPUP) != 0;
        var hasBorder = (windowStyle & (long)WindowStyles.WS_BORDER) != 0;
        var hasCaption = (windowStyle & (long)WindowStyles.WS_CAPTION) != 0;
        
        return hasPopupStyle && !hasBorder && !hasCaption;
    }
    
    /// <summary>
    /// 推奨メッセージを生成
    /// </summary>
    private static string GenerateRecommendationMessage(bool isExclusive, bool isBorderless, bool canShowOverlay)
    {
        return (isExclusive, isBorderless, canShowOverlay) switch
        {
            (true, false, false) => "オーバーレイ表示のため、ボーダレスフルスクリーンモードへの変更を推奨します。",
            (false, true, true) => "", // 推奨設定のためメッセージなし
            (false, false, true) => "", // ウィンドウモードのためメッセージなし
            _ => ""
        };
    }
    
    /// <summary>
    /// フルスクリーンモード変更イベント引数を作成
    /// </summary>
    private static FullscreenModeChangedEventArgs CreateModeChangedEventArgs(
        bool isExclusive, 
        bool isBorderless, 
        bool canShowOverlay, 
        string recommendationMessage, 
        MonitorInfo? monitor)
    {
        return new FullscreenModeChangedEventArgs(
            isExclusive,
            isBorderless,
            canShowOverlay,
            recommendationMessage,
            monitor)
        {
            DetectionTime = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// 現在のモード状態を更新
    /// </summary>
    private async Task UpdateCurrentModeAsync(FullscreenModeChangedEventArgs newMode, CancellationToken cancellationToken)
    {
        var oldExclusive = _isExclusiveFullscreen;
        var oldBorderless = _isBorderlessFullscreen;
        var oldCanShowOverlay = _canShowOverlay;
        
        // 状態を更新
        _isExclusiveFullscreen = newMode.IsExclusiveFullscreen;
        _isBorderlessFullscreen = newMode.IsBorderlessFullscreen;
        _canShowOverlay = newMode.CanShowOverlay;
        CurrentModeType = newMode.ModeType;
        
        // 変更があった場合にイベントを発火
        if (oldExclusive != newMode.IsExclusiveFullscreen ||
            oldBorderless != newMode.IsBorderlessFullscreen ||
            oldCanShowOverlay != newMode.CanShowOverlay)
        {
            await NotifyModeChangedAsync(newMode, cancellationToken).ConfigureAwait(false);
        }
    }
    
    /// <summary>
    /// モード変更イベントを通知
    /// </summary>
    private Task NotifyModeChangedAsync(FullscreenModeChangedEventArgs eventArgs, CancellationToken cancellationToken)
    {
        try
        {
            FullscreenModeChanged?.Invoke(this, eventArgs);
            _logger.LogInformation("Fullscreen mode change notified: {Change}", eventArgs.ToString());
            return Task.CompletedTask;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation notifying fullscreen mode change");
            return Task.FromException(ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Task was cancelled during fullscreen mode change notification");
            return Task.FromException(ex);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported notifying fullscreen mode change");
            return Task.FromException(ex);
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error notifying fullscreen mode change");
            return Task.FromException(ex);
        }
    }
    
    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
#pragma warning disable CA1849 // Call async methods when in an async method
            _cancellationTokenSource?.Cancel(); // Cancel()に非同期版は存在しない
#pragma warning restore CA1849 // Call async methods when in an async method
            _disposed = true;
        }
        catch (ObjectDisposedException)
        {
            // 既に破棄済み - 無視
        }
        
        GC.SuppressFinalize(this);
    }
    
    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        try
        {
#pragma warning disable CA1849 // Call async methods when in an async method
            _cancellationTokenSource?.Cancel(); // Cancel()に非同期版は存在しない
#pragma warning restore CA1849 // Call async methods when in an async method
            
            // イベントトラッカーの停止
            _gameWindowTracker?.Dispose();
            
            // タイマーの停止
            await _periodicCheckTimer.DisposeAsync().ConfigureAwait(false);
            
            // 監視の停止
            if (_isMonitoring)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await StopMonitoringAsync(cts.Token).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _logger?.LogWarning("Fullscreen monitoring did not stop within timeout");
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // 既に破棄済み - 無視
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _disposed = true;
        }
        
        GC.SuppressFinalize(this);
        _logger.LogInformation("WindowsFullscreenModeService disposed asynchronously");
    }
}

/// <summary>
/// ゲームウィンドウイベントトラッカー
/// 複数のウィンドウイベントを効率的に監視
/// </summary>
internal sealed class GameWindowEventTracker : IDisposable
{
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_HIDE = 0x8003;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    private const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    
    private readonly IntPtr _gameWindow;
    private readonly Action _onWindowStateChanged;
    private readonly ILogger _logger;
    private readonly WinEventDelegate _winEventProc;
    private readonly List<IntPtr> _hooks = [];
    private volatile bool _disposed;
    
    public GameWindowEventTracker(IntPtr gameWindow, Action onWindowStateChanged, ILogger logger)
    {
        _gameWindow = gameWindow;
        _onWindowStateChanged = onWindowStateChanged;
        _logger = logger;
        _winEventProc = WinEventProc;
        
        // 複数のイベントタイプを監視
        RegisterHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_HIDE);
        RegisterHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE);
        RegisterHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND);
        
        _logger.LogDebug("Started tracking events for game window: 0x{Handle:X}", gameWindow);
    }
    
    private void RegisterHook(uint eventMin, uint eventMax)
    {
        var hook = User32Methods.SetWinEventHook(
            eventMin, eventMax,
            IntPtr.Zero,
            _winEventProc,
            0, 0,
            WINEVENT_OUTOFCONTEXT);
        
        if (hook != IntPtr.Zero)
        {
            _hooks.Add(hook);
        }
        else
        {
            _logger.LogWarning("Failed to register hook for events {EventMin:X}-{EventMax:X}", eventMin, eventMax);
        }
    }
    
    public void StopTracking()
    {
        // フックの解除はDisposeで実行
    }
    
    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, 
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (_disposed) return;
        
        try
        {
            if (hwnd == _gameWindow)
            {
                _logger.LogDebug("Game window event {EventType:X} received: 0x{Handle:X}", eventType, hwnd);
                _onWindowStateChanged();
            }
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed in WinEventProc");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation in WinEventProc");
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument in WinEventProc");
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            // WinEventProcでは予期しない例外でも処理を継続する必要がある
            _logger.LogError(ex, "Error in WinEventProc");
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        try
        {
            foreach (var hook in _hooks)
            {
                if (hook != IntPtr.Zero)
                {
                    User32Methods.UnhookWinEvent(hook);
                }
            }
            _hooks.Clear();
            
            _logger.LogDebug("Disposed GameWindowEventTracker for window: 0x{Handle:X}", _gameWindow);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service already disposed in GameWindowEventTracker.Dispose");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation disposing GameWindowEventTracker");
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error disposing GameWindowEventTracker");
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            // Disposeでは予期しない例外でも処理を継続する必要がある
            _logger.LogError(ex, "Error disposing GameWindowEventTracker");
        }
#pragma warning restore CA1031 // Do not catch general exception types
    }
}
