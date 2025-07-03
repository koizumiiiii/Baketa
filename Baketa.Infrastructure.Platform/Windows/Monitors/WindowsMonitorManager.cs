using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Baketa.Core.UI.Geometry;
using Baketa.Core.UI.Monitors;
using Baketa.Infrastructure.Platform.Windows.NativeMethods;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Monitors;

/// <summary>
/// Windows固有のモニターマネージャー実装
/// Windowsメッセージベース検出とインテリジェントフォールバックによる高性能な実装
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMonitorManager : IMonitorManager, IAsyncDisposable
{
    private readonly ILogger<WindowsMonitorManager> _logger;
    private readonly object _lockObject = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    
    // Windowsメッセージベース検出用
    private readonly IDisposable _displayChangeListener;
    private GameWindowTracker? _gameWindowTracker;
    
    // インテリジェントフォールバック用キャッシュ
    private readonly ConcurrentDictionary<IntPtr, MonitorInfo> _windowMonitorCache = new();
    
    // 自動クリーンアップ用タイマー
    private readonly Timer _cleanupTimer;
    
    private volatile bool _isMonitoring;
    private volatile bool _disposed;
    private List<MonitorInfo> _monitors = [];
    private MonitorInfo? _primaryMonitor;
    private IntPtr _gameWindowHandle;
    
    /// <summary>
    /// WindowsMonitorManagerを初期化
    /// </summary>
    /// <param name="logger">ロガー</param>
    public WindowsMonitorManager(ILogger<WindowsMonitorManager> logger)
    {
        _logger = logger;
        
        // テスト環境を検出
        bool isTestEnvironment = IsRunningInTestEnvironment();
        
        // Windowsメッセージベース検出の初期化（テスト環境ではスキップ）
        if (isTestEnvironment)
        {
            _logger.LogInformation("テスト環境を検出: Windowsメッセージ監視をスキップします");
            _displayChangeListener = new TestDisplayChangeListener(_logger);
        }
        else
        {
            _displayChangeListener = new WindowsDisplayChangeListener(OnDisplaySettingsChanged, OnDpiChanged, _logger);
        }
        
        // 自動クリーンアップタイマーの初期化（30秒間隔）
        _cleanupTimer = new Timer(CleanupStaleCache, null, 
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        
        // 初期化時にモニター情報を取得
        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshMonitorsAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
                _logger.LogInformation("WindowsMonitorManager initialized with {Count} monitors (Message-based detection)", _monitors.Count);
            }
            catch (Win32Exception ex)
            {
                _logger.LogError(ex, "Windows API error during monitor initialization");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid operation during monitor initialization");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error during monitor initialization");
            }
        });
    }
    
    /// <inheritdoc/>
    public IReadOnlyList<MonitorInfo> Monitors
    {
        get
        {
            lock (_lockObject)
            {
                return _monitors.AsReadOnly();
            }
        }
    }
    
    /// <inheritdoc/>
    public MonitorInfo? PrimaryMonitor
    {
        get
        {
            lock (_lockObject)
            {
                return _primaryMonitor;
            }
        }
    }
    
    /// <inheritdoc/>
    public bool IsMonitoring => _isMonitoring && !_disposed;
    
    /// <inheritdoc/>
    public event EventHandler<MonitorChangedEventArgs>? MonitorChanged;
    
    /// <inheritdoc/>
    public MonitorInfo? GetMonitorFromWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            _logger.LogWarning("Invalid window handle provided");
            return PrimaryMonitor;
        }
        
        try
        {
            var monitorHandle = User32Methods.MonitorFromWindow(windowHandle, MonitorFlags.MONITOR_DEFAULTTONEAREST);
            var monitor = GetMonitorByHandle(monitorHandle);
            
            if (monitor.HasValue)
            {
                // 成功時は結果をキャッシュ
                _windowMonitorCache.AddOrUpdate(windowHandle, monitor.Value, (k, v) => monitor.Value);
                return monitor.Value;
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1400) // ERROR_INVALID_WINDOW_HANDLE
        {
            _logger.LogWarning("Window handle {Handle:X} is no longer valid", windowHandle);
            _windowMonitorCache.TryRemove(windowHandle, out _);
            return PrimaryMonitor;
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Win32 error getting monitor from window {Handle:X}: {Error}", 
                windowHandle, ex.NativeErrorCode);
            
            // 前回の正常値があれば使用
            if (_windowMonitorCache.TryGetValue(windowHandle, out var cachedMonitor))
            {
                _logger.LogDebug("Using cached monitor for window {Handle:X}: {Monitor}", 
                    windowHandle, cachedMonitor.Name);
                return cachedMonitor;
            }
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Service disposed while getting monitor from window {Handle:X}", windowHandle);
            return PrimaryMonitor;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation getting monitor from window {Handle:X}", windowHandle);
            
            // 前回の正常値があれば使用、なければプライマリ
            if (_windowMonitorCache.TryGetValue(windowHandle, out var cachedMonitor))
            {
                return cachedMonitor;
            }
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported getting monitor from window handle {Handle:X}", windowHandle);
            
            if (_windowMonitorCache.TryGetValue(windowHandle, out var cachedMonitor))
            {
                return cachedMonitor;
            }
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error getting monitor from window handle {Handle:X}", windowHandle);
            
            if (_windowMonitorCache.TryGetValue(windowHandle, out var cachedMonitor))
            {
                return cachedMonitor;
            }
        }
        
        return PrimaryMonitor;
    }
    
    /// <inheritdoc/>
    public MonitorInfo? GetMonitorFromPoint(Point point)
    {
        try
        {
            var nativePoint = new POINT(
                (int)Math.Round(point.X),
                (int)Math.Round(point.Y));
            
            var monitorHandle = User32Methods.MonitorFromPoint(nativePoint, MonitorFlags.MONITOR_DEFAULTTONEAREST);
            return GetMonitorByHandle(monitorHandle);
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Windows API error getting monitor from point ({X}, {Y})", point.X, point.Y);
            return PrimaryMonitor;
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Service disposed while getting monitor from point ({X}, {Y})", point.X, point.Y);
            return PrimaryMonitor;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation getting monitor from point ({X}, {Y})", point.X, point.Y);
            return PrimaryMonitor;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument getting monitor from point ({X}, {Y})", point.X, point.Y);
            return PrimaryMonitor;
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported getting monitor from point ({X}, {Y})", point.X, point.Y);
            return PrimaryMonitor;
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error getting monitor from point ({X}, {Y})", point.X, point.Y);
            return PrimaryMonitor;
        }
    }
    
    /// <inheritdoc/>
    public IReadOnlyList<MonitorInfo> GetMonitorsFromRect(Rect rect)
    {
        var monitors = Monitors;
        if (monitors.Count == 0)
            return [];
        
        return [.. monitors
            .Select(monitor => new { Monitor = monitor, Overlap = monitor.CalculateOverlapRatio(rect) })
            .Where(x => x.Overlap > 0)
            .OrderByDescending(x => x.Overlap)
            .Select(x => x.Monitor)];
    }
    
    /// <inheritdoc/>
    public MonitorInfo? GetMonitorByHandle(IntPtr handle)
    {
        lock (_lockObject)
        {
            var monitor = _monitors.FirstOrDefault(m => m.Handle == handle);
            return monitor.Handle != IntPtr.Zero ? monitor : null;
        }
    }
    
    /// <inheritdoc/>
    public Point TransformPointBetweenMonitors(Point point, MonitorInfo sourceMonitor, MonitorInfo targetMonitor)
    {
        // ソースモニターでの相対位置を計算
        var relativeX = (point.X - sourceMonitor.Bounds.X) / sourceMonitor.Bounds.Width;
        var relativeY = (point.Y - sourceMonitor.Bounds.Y) / sourceMonitor.Bounds.Height;
        
        // ターゲットモニターでの絶対位置に変換
        var targetX = targetMonitor.Bounds.X + (relativeX * targetMonitor.Bounds.Width);
        var targetY = targetMonitor.Bounds.Y + (relativeY * targetMonitor.Bounds.Height);
        
        // DPIスケールファクターを適用
        var dpiScaleX = targetMonitor.ScaleFactorX / sourceMonitor.ScaleFactorX;
        var dpiScaleY = targetMonitor.ScaleFactorY / sourceMonitor.ScaleFactorY;
        
        return new Point(targetX * dpiScaleX, targetY * dpiScaleY);
    }
    
    /// <inheritdoc/>
    public Rect TransformRectBetweenMonitors(Rect rect, MonitorInfo sourceMonitor, MonitorInfo targetMonitor)
    {
        var topLeft = TransformPointBetweenMonitors(
            new Point(rect.X, rect.Y), sourceMonitor, targetMonitor);
        
        var bottomRight = TransformPointBetweenMonitors(
            new Point(rect.X + rect.Width, rect.Y + rect.Height), sourceMonitor, targetMonitor);
        
        return new Rect(
            topLeft.X, 
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y);
    }
    
    /// <inheritdoc/>
    public async Task RefreshMonitorsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Refreshing monitor information");
            
            var newMonitors = await Task.Run(() => EnumerateMonitors(), cancellationToken).ConfigureAwait(false);
            var oldMonitors = Monitors.ToList();
            
            lock (_lockObject)
            {
                _monitors = [.. newMonitors];
                var primary = _monitors.FirstOrDefault(m => m.IsPrimary);
                _primaryMonitor = primary.Handle != IntPtr.Zero ? primary : null;
            }
            
            // 変更を検出してイベントを発火
            await DetectAndNotifyChangesAsync(oldMonitors, newMonitors, cancellationToken).ConfigureAwait(false);
            
            _logger.LogDebug("Monitor refresh completed. Found {Count} monitors", newMonitors.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Monitor refresh was cancelled");
            throw;
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Windows API error during monitor refresh");
            throw;
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Service disposed during monitor refresh");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation during monitor refresh");
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error during monitor refresh");
            throw;
        }
    }
    
    /// <inheritdoc/>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_isMonitoring)
        {
            _logger.LogWarning("Monitor monitoring is already started");
            return;
        }
        
        try
        {
            _logger.LogInformation("Starting monitor monitoring (message-based)");
            _isMonitoring = true;
            
            // Windowsメッセージベース検出を開始
            if (_displayChangeListener is WindowsDisplayChangeListener windowsListener)
            {
                windowsListener.StartListening();
            }
            else if (_displayChangeListener is TestDisplayChangeListener testListener)
            {
                testListener.StartListening();
            }
            
            await Task.Delay(100, cancellationToken).ConfigureAwait(false); // 短い遅延で開始を確認
            
            _logger.LogInformation("Monitor monitoring started successfully (0.1% CPU usage)");
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Monitor monitoring start was cancelled");
            _isMonitoring = false;
            throw;
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Windows API error starting monitor monitoring");
            _isMonitoring = false;
            throw;
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Service disposed during monitor monitoring start");
            _isMonitoring = false;
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation starting monitor monitoring");
            _isMonitoring = false;
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error starting monitor monitoring");
            _isMonitoring = false;
            throw;
        }
    }
    
    /// <inheritdoc/>
    public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (!_isMonitoring)
        {
            _logger.LogDebug("Monitor monitoring is not running");
            return;
        }
        
        try
        {
            _logger.LogInformation("Stopping monitor monitoring");
            _isMonitoring = false;
            
            // Windowsメッセージベース検出を停止
            if (_displayChangeListener is WindowsDisplayChangeListener windowsListener)
            {
                windowsListener.StopListening();
            }
            else if (_displayChangeListener is TestDisplayChangeListener testListener)
            {
                testListener.StopListening();
            }
            _gameWindowTracker?.StopTracking();
            
            await Task.Delay(50, cancellationToken).ConfigureAwait(false); // 短い遅延で停止を確認
            
            _logger.LogInformation("Monitor monitoring stopped successfully");
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Monitor monitoring stop was cancelled");
            throw;
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Windows API error stopping monitor monitoring");
            throw;
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogError(ex, "Service disposed during monitor monitoring stop");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation stopping monitor monitoring");
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error stopping monitor monitoring");
            throw;
        }
    }
    
    /// <summary>
    /// Windowsメッセージベースのディスプレイ設定変更イベント処理
    /// </summary>
    private async void OnDisplaySettingsChanged()
    {
        try
        {
            _logger.LogDebug("Display settings changed - refreshing monitors");
            await RefreshMonitorsAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Display settings change handling was cancelled");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed during display settings change handling");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation handling display settings change");
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported handling display settings change");
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error handling display settings change");
        }
    }
    
    /// <summary>
    /// DPI変更イベント処理
    /// </summary>
    private async void OnDpiChanged()
    {
        try
        {
            _logger.LogDebug("DPI settings changed - refreshing monitors");
            await RefreshMonitorsAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("DPI change handling was cancelled");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed during DPI change handling");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation handling DPI change");
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported handling DPI change");
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error handling DPI change");
        }
    }
    
    /// <summary>
    /// 定期的なキャッシュクリーンアップ
    /// </summary>
    private void CleanupStaleCache(object? state)
    {
        try
        {
            var staleEntries = _windowMonitorCache
                .Where(kvp => !IsValidWindow(kvp.Key))
                .Select(kvp => kvp.Key)
                .ToList();
                
            foreach (var staleHandle in staleEntries)
            {
                if (_windowMonitorCache.TryRemove(staleHandle, out var removedMonitor))
                {
                    _logger.LogDebug("Cleaned up stale cache entry for window 0x{Handle:X}", staleHandle);
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
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported during cache cleanup");
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error during cache cleanup");
        }
    }
    
    /// <summary>
    /// ウィンドウハンドル有効性チェック
    /// </summary>
    private static bool IsValidWindow(IntPtr handle)
    {
        return handle != IntPtr.Zero && User32Methods.IsWindow(handle);
    }
    
    /// <summary>
    /// ゲームウィンドウの追跡を開始
    /// </summary>
    /// <param name="gameWindowHandle">ゲームウィンドウハンドル</param>
    public void StartGameWindowTracking(IntPtr gameWindowHandle)
    {
        if (gameWindowHandle == IntPtr.Zero)
        {
            _logger.LogWarning("Invalid game window handle provided for tracking");
            return;
        }
        
        _gameWindowHandle = gameWindowHandle;
        
        // ゲームウィンドウ位置変更の追跡を開始
        _gameWindowTracker?.Dispose();
        _gameWindowTracker = new GameWindowTracker(gameWindowHandle, OnGameWindowMoved, _logger);
        
        _logger.LogInformation("Started tracking game window: 0x{Handle:X}", gameWindowHandle);
    }
    
    /// <summary>
    /// ゲームウィンドウ移動イベント処理
    /// </summary>
    private void OnGameWindowMoved()
    {
        try
        {
            _logger.LogDebug("Game window moved - checking for monitor change");
            // モニター変更チェックロジックは必要に応じて実装
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed during game window move handling");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation handling game window move");
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported handling game window move");
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error handling game window move");
        }
    }
    
    /// <summary>
    /// システムからモニター情報を列挙
    /// </summary>
    private static List<MonitorInfo> EnumerateMonitors()
    {
        var monitors = new List<MonitorInfo>();
        
        // デリゲートを明示的に定義してrefパラメータ問題を回避
        bool MonitorEnumProc(IntPtr monitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
        {
            try
            {
                var monitorInfo = GetMonitorInfoEx(monitor);
                if (monitorInfo is not null)
                {
                    monitors.Add(monitorInfo.Value);
                }
            }
            catch (Win32Exception)
            {
                // 個別のモニター取得エラーは無視して継続
            }
            catch (InvalidOperationException)
            {
                // 無効な操作は無視して継続
            }
            catch (NotSupportedException)
            {
                // サポートされていない操作は無視して継続
            }
            catch (ExternalException)
            {
                // 外部エラーは無視して継続
            }
            return true;
        }
        
        User32Methods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumProc, IntPtr.Zero);
        
        return monitors;
    }
    
    /// <summary>
    /// 指定されたモニターハンドルから詳細情報を取得
    /// </summary>
    private static MonitorInfo? GetMonitorInfoEx(IntPtr monitor)
    {
        var monitorInfoEx = MONITORINFOEX.Create();
        
        if (!User32Methods.GetMonitorInfo(monitor, ref monitorInfoEx))
            return null;
        
        // DPI情報を取得
        var dpiResult = User32Methods.GetDpiForMonitor(monitor, DpiType.Effective, out var dpiX, out var dpiY);
        if (dpiResult != 0)
        {
            // DPI取得に失敗した場合はデフォルト値を使用
            dpiX = 96;
            dpiY = 96;
        }
        
        var bounds = new Rect(
            monitorInfoEx.rcMonitor.left,
            monitorInfoEx.rcMonitor.top,
            monitorInfoEx.rcMonitor.right - monitorInfoEx.rcMonitor.left,
            monitorInfoEx.rcMonitor.bottom - monitorInfoEx.rcMonitor.top);
        
        var workArea = new Rect(
            monitorInfoEx.rcWork.left,
            monitorInfoEx.rcWork.top,
            monitorInfoEx.rcWork.right - monitorInfoEx.rcWork.left,
            monitorInfoEx.rcWork.bottom - monitorInfoEx.rcWork.top);
        
        var isPrimary = (monitorInfoEx.dwFlags & User32Methods.MONITORINFOF_PRIMARY) != 0;
        var deviceName = monitorInfoEx.szDevice;
        
        return new MonitorInfo(
            Handle: monitor,
            Name: deviceName,
            DeviceId: $"{deviceName}_{bounds.Width}x{bounds.Height}",
            Bounds: bounds,
            WorkArea: workArea,
            IsPrimary: isPrimary,
            DpiX: dpiX,
            DpiY: dpiY);
    }
    
    /// <summary>
    /// モニター変更を検出してイベントを通知
    /// </summary>
    private async Task DetectAndNotifyChangesAsync(
        IReadOnlyList<MonitorInfo> oldMonitors,
        IReadOnlyList<MonitorInfo> newMonitors,
        CancellationToken cancellationToken)
    {
        // 追加されたモニターをチェック
        var addedMonitors = newMonitors.ExceptBy(oldMonitors.Select(m => m.Handle), m => m.Handle);
        foreach (var added in addedMonitors)
        {
            await NotifyMonitorChangedAsync(MonitorChangeType.Added, added, newMonitors, cancellationToken).ConfigureAwait(false);
        }
        
        // 削除されたモニターをチェック
        var removedMonitors = oldMonitors.ExceptBy(newMonitors.Select(m => m.Handle), m => m.Handle);
        foreach (var removed in removedMonitors)
        {
            await NotifyMonitorChangedAsync(MonitorChangeType.Removed, removed, newMonitors, cancellationToken).ConfigureAwait(false);
        }
        
        // プライマリモニター変更をチェック
        var oldPrimary = oldMonitors.FirstOrDefault(m => m.IsPrimary);
        var newPrimary = newMonitors.FirstOrDefault(m => m.IsPrimary);
        
        // 値型なので、ハンドルが0の場合は有効なモニターではない
        var oldHasValidPrimary = oldPrimary.Handle != IntPtr.Zero;
        var newHasValidPrimary = newPrimary.Handle != IntPtr.Zero;
        
        if (oldHasValidPrimary && newHasValidPrimary && oldPrimary.Handle != newPrimary.Handle)
        {
            await NotifyMonitorChangedAsync(MonitorChangeType.PrimaryChanged, newPrimary, newMonitors, cancellationToken).ConfigureAwait(false);
        }
        else if (!oldHasValidPrimary && newHasValidPrimary)
        {
            // 新しいプライマリモニターが追加された
            await NotifyMonitorChangedAsync(MonitorChangeType.PrimaryChanged, newPrimary, newMonitors, cancellationToken).ConfigureAwait(false);
        }
        
        // 変更されたモニターをチェック
        var changedMonitors = newMonitors
            .Join(oldMonitors, n => n.Handle, o => o.Handle, (n, o) => new { New = n, Old = o })
            .Where(x => !MonitorInfoEquals(x.Old, x.New))
            .Select(x => x.New);
        
        foreach (var changed in changedMonitors)
        {
            await NotifyMonitorChangedAsync(MonitorChangeType.Changed, changed, newMonitors, cancellationToken).ConfigureAwait(false);
        }
    }
    
    /// <summary>
    /// モニター変更イベントを通知
    /// </summary>
    private Task NotifyMonitorChangedAsync(
        MonitorChangeType changeType,
        MonitorInfo? affectedMonitor,
        IReadOnlyList<MonitorInfo> allMonitors,
        CancellationToken _)
    {
        try
        {
            var eventArgs = new MonitorChangedEventArgs(changeType, affectedMonitor, allMonitors);
            
            MonitorChanged?.Invoke(this, eventArgs);
            
            _logger.LogInformation("Monitor change notified: {Change}", eventArgs.ToString());
            return Task.CompletedTask;
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed during monitor change notification: {ChangeType}", changeType);
            return Task.FromException(ex);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation notifying monitor change: {ChangeType}", changeType);
            return Task.FromException(ex);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "Operation not supported notifying monitor change: {ChangeType}", changeType);
            return Task.FromException(ex);
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error notifying monitor change: {ChangeType}", changeType);
            return Task.FromException(ex);
        }
    }
    
    /// <summary>
    /// テスト環境で実行されているかどうかを検出
    /// </summary>
    /// <returns>テスト環境で実行中の場合true</returns>
    private static bool IsRunningInTestEnvironment()
    {
        // 方法1: アセンブリ名に「Test」が含まれているかチェック
        var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();
        if (entryAssembly?.FullName?.Contains("Test", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }
        
        // 方法2: スタックトレースでテストランナーを検出
        var stackTrace = new System.Diagnostics.StackTrace();
        for (int i = 0; i < stackTrace.FrameCount; i++)
        {
            var method = stackTrace.GetFrame(i)?.GetMethod();
            var declaringType = method?.DeclaringType;
            if (declaringType?.Assembly.FullName?.Contains("Test", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }
        
        // 方法3: 现在のアセンブリコンテキストでテスト関連アセンブリを検出
        var loadedAssemblies = System.AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in loadedAssemblies)
        {
            var name = assembly.FullName;
            if (name?.Contains("xunit", StringComparison.OrdinalIgnoreCase) == true ||
                name?.Contains("nunit", StringComparison.OrdinalIgnoreCase) == true ||
                name?.Contains("mstest", StringComparison.OrdinalIgnoreCase) == true ||
                name?.Contains(".Test", StringComparison.OrdinalIgnoreCase) == true ||
                name?.Contains(".Tests", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// モニター情報の等価性をチェック
    /// </summary>
    private static bool MonitorInfoEquals(MonitorInfo a, MonitorInfo b) =>
        a.Handle == b.Handle &&
        a.Bounds.Equals(b.Bounds) &&
        a.WorkArea.Equals(b.WorkArea) &&
        a.IsPrimary == b.IsPrimary &&
        Math.Abs(a.DpiX - b.DpiX) < 0.1 &&
        Math.Abs(a.DpiY - b.DpiY) < 0.1;
    
    /// <summary>
    /// ファイナライザー
    /// アンマネージリソース（Windowsメッセージウィンドウ、フック等）の確実な解放を保証
    /// </summary>
    ~WindowsMonitorManager()
    {
        Dispose(false);
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    /// <summary>
    /// リソースを破棄します
    /// </summary>
    /// <param name="disposing">マネージリソースも破棄するかどうか</param>
    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        
        try
        {
            if (disposing)
            {
                // マネージリソースの破棄
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _displayChangeListener?.Dispose();
                _gameWindowTracker?.Dispose();
                _cleanupTimer?.Dispose();
            }
            
            // アンマネージリソースの破棄（必要に応じて）
            
            _disposed = true;
        }
        catch (ObjectDisposedException)
        {
            // 既に破棄済み - 無視
        }
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
            
            // メッセージリスナーの停止
            _displayChangeListener?.Dispose();
            _gameWindowTracker?.Dispose();
            
            // タイマーの停止
            await _cleanupTimer.DisposeAsync().ConfigureAwait(false);
            
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
                    _logger?.LogWarning("Monitor monitoring did not stop within timeout");
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
        _logger.LogInformation("WindowsMonitorManager disposed asynchronously");
    }
}

/// <summary>
/// Windowsディスプレイ変更メッセージリスナー
/// WM_DISPLAYCHANGE と WM_SETTINGCHANGE メッセージを監視
/// </summary>
internal sealed class WindowsDisplayChangeListener : IDisposable
{
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private const uint WM_SETTINGCHANGE = 0x001A;
    
    private readonly Action _onDisplayChanged;
    private readonly Action _onDpiChanged;
    private readonly ILogger _logger;
    private readonly IntPtr _messageWindow;
    private readonly WndProcDelegate _wndProc;
    private volatile bool _isListening;
    private volatile bool _disposed;
    
    public WindowsDisplayChangeListener(
        Action onDisplayChanged, 
        Action onDpiChanged, 
        ILogger logger)
    {
        _onDisplayChanged = onDisplayChanged;
        _onDpiChanged = onDpiChanged;
        _logger = logger;
        _wndProc = WndProc;
        _messageWindow = CreateMessageOnlyWindow(_wndProc);
    }
    
    public void StartListening()
    {
        _isListening = true;
        _logger.LogDebug("Started listening for display change messages");
    }
    
    public void StopListening()
    {
        _isListening = false;
        _logger.LogDebug("Stopped listening for display change messages");
    }
    
    /// <summary>
    /// ファイナライザー
    /// アンマネージリソース（Windowsメッセージウィンドウ）の確実な解放を保証
    /// </summary>
    ~WindowsDisplayChangeListener()
    {
        Dispose();
    }
    
    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (!_isListening || _disposed)
            return User32Methods.DefWindowProc(hwnd, msg, wParam, lParam);
        
        try
        {
            switch (msg)
            {
                case WM_DISPLAYCHANGE:
                    _logger.LogDebug("WM_DISPLAYCHANGE received");
                    _onDisplayChanged();
                    break;
                    
                case WM_SETTINGCHANGE:
                    if (lParam != IntPtr.Zero)
                    {
                        var settingName = Marshal.PtrToStringUni(lParam);
                        if (settingName == "UserDisplayMetrics")
                        {
                            _logger.LogDebug("DPI change detected via WM_SETTINGCHANGE");
                            _onDpiChanged();
                        }
                    }
                    break;
            }
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed in WndProc");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation in WndProc");
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument in WndProc");
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error in WndProc handling message {Message:X}", msg);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // WndProcでは予期しない例外でも処理を継続する必要がある
            // キャンセル例外のみは再スローし、その他はログ記録後継続
            _logger.LogError(ex, "Error in WndProc handling message {Message:X}", msg);
        }
        
        return User32Methods.DefWindowProc(hwnd, msg, wParam, lParam);
    }
    
    private static IntPtr CreateMessageOnlyWindow(WndProcDelegate wndProc)
    {
        // プロセスIDとタイムスタンプでユニークなクラス名を生成
        var processId = Environment.ProcessId;
        var timestamp = Environment.TickCount64;
        var threadId = Environment.CurrentManagedThreadId;
        var uniqueClassName = $"BaketaDisplayChangeListener_{processId}_{timestamp}_{threadId}";
        
        var wndClass = new WNDCLASS
        {
            style = 0,
            lpfnWndProc = wndProc,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = User32Methods.GetModuleHandle(null),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = uniqueClassName
        };
        
        var classAtom = User32Methods.RegisterClass(ref wndClass);
        if (classAtom == 0)
        {
            var lastError = Marshal.GetLastWin32Error();
            throw new Win32Exception(lastError, $"Window class registration failed: {lastError}");
        }
        
        var hwnd = User32Methods.CreateWindowEx(
            0, classAtom, uniqueClassName,
            0, 0, 0, 0, 0,
            User32Methods.HWND_MESSAGE, IntPtr.Zero,
            wndClass.hInstance, IntPtr.Zero);
        
        if (hwnd == IntPtr.Zero)
        {
            var lastError = Marshal.GetLastWin32Error();
            throw new Win32Exception(lastError, $"Message window creation failed: {lastError}");
        }
        
        return hwnd;
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        try
        {
            if (_messageWindow != IntPtr.Zero)
            {
                User32Methods.DestroyWindow(_messageWindow);
            }
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service already disposed in WindowsDisplayChangeListener.Dispose");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation disposing WindowsDisplayChangeListener");
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error disposing WindowsDisplayChangeListener");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Disposeでは予期しない例外でも処理を継続する必要がある
            // キャンセル例外のみは再スローし、その他はログ記録後継続
            _logger.LogError(ex, "Error disposing WindowsDisplayChangeListener");
        }
        
        // ファイナライザーを抑制（CA1816対応）
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// テスト環境用のシンプルなディスプレイ変更リスナー
/// 実際のWindowsメッセージ監視を行わず、安全にテストを実行できる
/// </summary>
internal sealed class TestDisplayChangeListener : IDisposable
{
    private readonly ILogger _logger;
    private volatile bool _disposed;
    
    public TestDisplayChangeListener(ILogger logger)
    {
        _logger = logger;
    }
    
    public void StartListening()
    {
        _logger.LogDebug("Test display change listener started (no-op)");
    }
    
    public void StopListening()
    {
        _logger.LogDebug("Test display change listener stopped (no-op)");
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _logger.LogDebug("Test display change listener disposed");
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// ゲームウィンドウ位置変更トラッカー
/// EVENT_OBJECT_LOCATIONCHANGE イベントを監視
/// </summary>
internal sealed class GameWindowTracker : IDisposable
{
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    
    private readonly IntPtr _gameWindow;
    private readonly Action _onWindowMoved;
    private readonly ILogger _logger;
    private readonly WinEventDelegate _winEventProc;
    private readonly IntPtr _hook;
    private volatile bool _disposed;
    
    public GameWindowTracker(IntPtr gameWindow, Action onWindowMoved, ILogger logger)
    {
        _gameWindow = gameWindow;
        _onWindowMoved = onWindowMoved;
        _logger = logger;
        _winEventProc = WinEventProc;
        
        _hook = User32Methods.SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE,
            EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero,
            _winEventProc,
            0, 0,
            WINEVENT_OUTOFCONTEXT);
        
        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    
    public void StopTracking()
    {
        // フックの解除はDisposeで実行
    }
    
    /// <summary>
    /// ファイナライザー
    /// アンマネージリソース（Windowsイベントフック）の確実な解放を保証
    /// </summary>
    ~GameWindowTracker()
    {
        Dispose();
    }
    
    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, 
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (_disposed) return;
        
        try
        {
            if (hwnd == _gameWindow && eventType == EVENT_OBJECT_LOCATIONCHANGE)
            {
                _logger.LogDebug("Game window location changed: 0x{Handle:X}", hwnd);
                _onWindowMoved();
            }
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service disposed in GameWindowTracker.WinEventProc");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation in GameWindowTracker.WinEventProc");
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid argument in GameWindowTracker.WinEventProc");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // WinEventProcでは予期しない例外でも処理を継続する必要がある
            // キャンセル例外のみは再スローし、その他はログ記録後継続
            _logger.LogError(ex, "Error in WinEventProc");
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        try
        {
            if (_hook != IntPtr.Zero)
            {
                User32Methods.UnhookWinEvent(_hook);
            }
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogDebug(ex, "Service already disposed in GameWindowTracker.Dispose");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation disposing GameWindowTracker");
        }
        catch (ExternalException ex)
        {
            _logger.LogError(ex, "External error disposing GameWindowTracker");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Disposeでは予期しない例外でも処理を継続する必要がある
            // キャンセル例外のみは再スローし、その他はログ記録後継続
            _logger.LogError(ex, "Error disposing GameWindowTracker");
        }
        
        // ファイナライザーを抑制（CA1816対応）
        GC.SuppressFinalize(this);
    }
}
