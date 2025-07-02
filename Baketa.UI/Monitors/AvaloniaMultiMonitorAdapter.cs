using Avalonia.Platform;
using Baketa.Core.UI.Geometry;
using Baketa.Core.UI.Monitors;

namespace Baketa.UI.Monitors;

/// <summary>
/// AvaloniaUI統合型マルチモニターサポートアダプター
/// プラットフォーム固有実装とUI層の仲介、Avalonia Screensとの連携を提供
/// </summary>
public sealed class AvaloniaMultiMonitorAdapter : ReactiveObject, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AvaloniaMultiMonitorAdapter> _logger;
    private readonly Subject<MonitorChangedEventArgs> _monitorChangedSubject = new();
    private readonly object _lockObject = new();
    
    private IMonitorManager? _platformManager;
    private bool _disposed;
    
    /// <summary>
    /// AvaloniaMultiMonitorAdapterを初期化
    /// </summary>
    /// <param name="serviceProvider">サービスプロバイダー</param>
    /// <param name="logger">ロガー</param>
    public AvaloniaMultiMonitorAdapter(
        IServiceProvider serviceProvider, 
        ILogger<AvaloniaMultiMonitorAdapter> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        // 初期化タスクを非同期で実行
        _ = Task.Run(async () =>
        {
            try
            {
                await InitializeAsync().ConfigureAwait(false);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "AvaloniaMultiMonitorAdapter initialization was cancelled");
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "AvaloniaMultiMonitorAdapter initialization was cancelled");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Invalid operation during AvaloniaMultiMonitorAdapter initialization");
            }
#pragma warning disable CA1031
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during AvaloniaMultiMonitorAdapter initialization");
            }
#pragma warning restore CA1031
        });
    }
    
    /// <summary>
    /// プラットフォーム固有のマネージャーを遅延初期化で取得
    /// </summary>
    private IMonitorManager PlatformManager
    {
        get
        {
            lock (_lockObject)
            {
                return _platformManager ??= GetPlatformManager();
            }
        }
    }
    
    /// <summary>
    /// 利用可能なモニターのコレクション
    /// </summary>
    public IReadOnlyList<MonitorInfo> Monitors => PlatformManager.Monitors;
    
    /// <summary>
    /// プライマリモニター情報
    /// </summary>
    public MonitorInfo? PrimaryMonitor => PlatformManager.PrimaryMonitor;
    
    /// <summary>
    /// アクティブなモニター数
    /// </summary>
    public int MonitorCount => PlatformManager.MonitorCount;
    
    /// <summary>
    /// モニター監視が開始されているかどうか
    /// </summary>
    public bool IsMonitoring => PlatformManager.IsMonitoring;
    
    /// <summary>
    /// モニター変更のObservable（ReactiveUI統合）
    /// </summary>
    public IObservable<MonitorChangedEventArgs> MonitorChanged => _monitorChangedSubject.AsObservable();
    
    /// <summary>
    /// ウィンドウが表示されているモニターを取得
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>モニター情報、見つからない場合はnull</returns>
    public MonitorInfo? GetMonitorFromWindow(nint windowHandle)
    {
        try
        {
            return PlatformManager.GetMonitorFromWindow(windowHandle);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid window handle {Handle:X}", windowHandle);
            return PrimaryMonitor;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Platform manager not available for window {Handle:X}", windowHandle);
            return PrimaryMonitor;
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger.LogError(ex, "Platform not supported for window {Handle:X}", windowHandle);
            return PrimaryMonitor;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting monitor from window {Handle:X}", windowHandle);
            return PrimaryMonitor;
        }
#pragma warning restore CA1031
    }
    
    /// <summary>
    /// 座標が含まれるモニターを取得
    /// </summary>
    /// <param name="point">スクリーン座標</param>
    /// <returns>モニター情報、見つからない場合はnull</returns>
    public MonitorInfo? GetMonitorFromPoint(CorePoint point)
    {
        try
        {
            return PlatformManager.GetMonitorFromPoint(point);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogWarning(ex, "Point ({X}, {Y}) is out of valid screen range", point.X, point.Y);
            return PrimaryMonitor;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Platform manager not available for point ({X}, {Y})", point.X, point.Y);
            return PrimaryMonitor;
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger.LogError(ex, "Platform not supported for point ({X}, {Y})", point.X, point.Y);
            return PrimaryMonitor;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting monitor from point ({X}, {Y})", point.X, point.Y);
            return PrimaryMonitor;
        }
#pragma warning restore CA1031
    }
    
    /// <summary>
    /// Avaloniaスクリーンから最適なモニターを取得
    /// </summary>
    /// <param name="screen">Avaloniaスクリーン</param>
    /// <returns>対応するモニター情報</returns>
    public MonitorInfo? GetMonitorFromAvaloniaScreen(Screen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        
        try
        {
            var bounds = new CoreRect(
                screen.Bounds.X,
                screen.Bounds.Y,
                screen.Bounds.Width,
                screen.Bounds.Height);
            
            return Monitors.FirstOrDefault(m => 
                Math.Abs(m.Bounds.X - bounds.X) < 1 &&
                Math.Abs(m.Bounds.Y - bounds.Y) < 1 &&
                Math.Abs(m.Bounds.Width - bounds.Width) < 1 &&
                Math.Abs(m.Bounds.Height - bounds.Height) < 1);
        }
        catch (ArgumentNullException ex)
        {
            _logger.LogWarning(ex, "Screen parameter is null");
            return PrimaryMonitor;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Failed to access monitor collection from Avalonia screen");
            return PrimaryMonitor;
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger.LogError(ex, "Platform not supported for Avalonia screen");
            return PrimaryMonitor;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting monitor from Avalonia screen");
            return PrimaryMonitor;
        }
#pragma warning restore CA1031
    }
    
    /// <summary>
    /// ゲームウィンドウに最適なモニターを決定
    /// ウィンドウ中心点優先、表示面積フォールバック方式
    /// </summary>
    /// <param name="gameWindowHandle">ゲームウィンドウハンドル</param>
    /// <returns>最適なモニター</returns>
    public MonitorInfo DetermineOptimalMonitorForGame(nint gameWindowHandle)
    {
        try
        {
            return PlatformManager.DetermineOptimalMonitor(gameWindowHandle);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid game window handle {Handle:X}", gameWindowHandle);
            return PrimaryMonitor ?? throw new InvalidOperationException("プライマリモニターが見つかりません");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("プライマリモニター", StringComparison.Ordinal))
        {
            _logger.LogError(ex, "No primary monitor available for game window {Handle:X}", gameWindowHandle);
            throw;
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger.LogError(ex, "Platform not supported for determining optimal monitor for game window {Handle:X}", gameWindowHandle);
            return PrimaryMonitor ?? throw new InvalidOperationException("プライマリモニターが見つかりません");
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error determining optimal monitor for game window {Handle:X}", gameWindowHandle);
            return PrimaryMonitor ?? throw new InvalidOperationException("プライマリモニターが見つかりません");
        }
#pragma warning restore CA1031
    }
    
    /// <summary>
    /// モニター間でのオーバーレイ位置変換
    /// DPIスケーリングを考慮した座標変換
    /// </summary>
    /// <param name="overlayRect">オーバーレイ矩形</param>
    /// <param name="sourceMonitor">元のモニター</param>
    /// <param name="targetMonitor">対象のモニター</param>
    /// <returns>変換後の矩形</returns>
    public CoreRect TransformOverlayBetweenMonitors(
        CoreRect overlayRect,
        MonitorInfo sourceMonitor,
        MonitorInfo targetMonitor)
    {
        try
        {
            return PlatformManager.TransformRectBetweenMonitors(overlayRect, sourceMonitor, targetMonitor);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid monitor parameters for overlay transformation");
            return overlayRect; // フォールバック：元の矩形をそのまま返す
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Platform manager not available for overlay transformation");
            return overlayRect; // フォールバック：元の矩形をそのまま返す
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger.LogError(ex, "Platform not supported for transforming overlay between monitors");
            return overlayRect; // フォールバック：元の矩形をそのまま返す
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error transforming overlay between monitors");
            return overlayRect; // フォールバック：元の矩形をそのまま返す
        }
#pragma warning restore CA1031
    }
    
    /// <summary>
    /// DPI変更検出
    /// モニター間移動時のDPIスケール変更を検出
    /// </summary>
    /// <param name="oldMonitor">変更前のモニター</param>
    /// <param name="newMonitor">変更後のモニター</param>
    /// <returns>DPI変更があった場合true</returns>
    public bool HasDpiChanged(MonitorInfo oldMonitor, MonitorInfo newMonitor) =>
        MonitorManagerExtensions.HasDpiChanged(oldMonitor, newMonitor);
    
    /// <summary>
    /// モニター情報を手動更新
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>更新タスク</returns>
    public async Task RefreshMonitorsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await PlatformManager.RefreshMonitorsAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Monitor information refreshed via adapter");
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Monitor refresh cancelled");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation refreshing monitors via adapter");
            throw;
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger.LogError(ex, "Platform not supported for refreshing monitors via adapter");
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error refreshing monitors via adapter");
            throw;
        }
#pragma warning restore CA1031
    }
    
    /// <summary>
    /// モニター監視を開始
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>監視開始タスク</returns>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await PlatformManager.StartMonitoringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Monitor monitoring started via adapter");
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Monitor monitoring start cancelled");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation starting monitor monitoring via adapter");
            throw;
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger.LogError(ex, "Platform not supported for starting monitor monitoring via adapter");
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error starting monitor monitoring via adapter");
            throw;
        }
#pragma warning restore CA1031
    }
    
    /// <summary>
    /// モニター監視を停止
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>監視停止タスク</returns>
    public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await PlatformManager.StopMonitoringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Monitor monitoring stopped via adapter");
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Monitor monitoring stop cancelled");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation stopping monitor monitoring via adapter");
            throw;
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger.LogError(ex, "Platform not supported for stopping monitor monitoring via adapter");
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error stopping monitor monitoring via adapter");
            throw;
        }
#pragma warning restore CA1031
    }
    
    /// <summary>
    /// 初期化処理
    /// </summary>
    private async Task InitializeAsync()
    {
        try
        {
            _logger.LogDebug("Initializing AvaloniaMultiMonitorAdapter");
            
            // プラットフォームマネージャーの取得と初期化
            var manager = PlatformManager;
            
            // イベント購読
            manager.MonitorChanged += OnPlatformMonitorChanged;
            
            // 監視開始
            await manager.StartMonitoringAsync().ConfigureAwait(false);
            
            _logger.LogInformation("AvaloniaMultiMonitorAdapter initialized successfully with {Count} monitors", 
                manager.MonitorCount);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Monitor initialization was cancelled");
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Monitor initialization was cancelled");
            throw;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Platform monitor manager", StringComparison.Ordinal))
        {
            _logger.LogError(ex, "Platform monitor manager is not available");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation during monitor initialization");
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize AvaloniaMultiMonitorAdapter");
            throw;
        }
#pragma warning restore CA1031
    }
    
    /// <summary>
    /// プラットフォーム固有のマネージャーを取得
    /// </summary>
    private IMonitorManager GetPlatformManager()
    {
        try
        {
            var manager = _serviceProvider.GetRequiredService<IMonitorManager>();
            _logger.LogDebug("Platform monitor manager resolved: {Type}", manager.GetType().Name);
            return manager;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No service for type", StringComparison.Ordinal))
        {
            _logger.LogError(ex, "IMonitorManager service is not registered in DI container");
            throw new InvalidOperationException("Platform monitor manager is not available. Ensure IMonitorManager is registered in the DI container.", ex);
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve platform monitor manager");
            throw new InvalidOperationException("Platform monitor manager is not available", ex);
        }
#pragma warning restore CA1031
    }
    
    /// <summary>
    /// プラットフォームマネージャーからのモニター変更イベントを処理
    /// </summary>
    private void OnPlatformMonitorChanged(object? sender, MonitorChangedEventArgs e)
    {
        try
        {
            _logger.LogDebug("Monitor change detected: {Change}", e.ToString());
            
            // ReactiveUIのObservableストリームに通知
            _monitorChangedSubject.OnNext(e);
            
            // プロパティ変更通知（ReactiveUI）
            this.RaisePropertyChanged(nameof(Monitors));
            this.RaisePropertyChanged(nameof(PrimaryMonitor));
            this.RaisePropertyChanged(nameof(MonitorCount));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation handling platform monitor change");
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error handling platform monitor change");
        }
#pragma warning restore CA1031
    }
    
    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        
        _disposed = true;
        
        try
        {
            if (_platformManager is not null)
            {
                _platformManager.MonitorChanged -= OnPlatformMonitorChanged;
                _platformManager.Dispose();
            }
            
            _monitorChangedSubject.Dispose();
            
            _logger.LogInformation("AvaloniaMultiMonitorAdapter disposed");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation during AvaloniaMultiMonitorAdapter disposal");
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during AvaloniaMultiMonitorAdapter disposal");
        }
#pragma warning restore CA1031
    }
}
