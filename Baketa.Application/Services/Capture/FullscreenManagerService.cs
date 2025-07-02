using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Abstractions.Events;

namespace Baketa.Application.Services.Capture;

/// <summary>
/// フルスクリーン管理サービス
/// フルスクリーン検出と最適化を統合管理し、
/// イベント集約機構を通じてシステム全体に通知する
/// </summary>
public sealed class FullscreenManagerService : IDisposable
{
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<FullscreenManagerService>? _logger;
    
    private bool _isRunning;
    private bool _disposed;
    
    /// <summary>
    /// フルスクリーン管理サービスが実行中かどうか
    /// </summary>
    public bool IsRunning => _isRunning && !_disposed;
    
    /// <summary>
    /// フルスクリーン検出サービス
    /// </summary>
    public IFullscreenDetectionService DetectionService { get; }
    
    /// <summary>
    /// フルスクリーン最適化サービス
    /// </summary>
    public IFullscreenOptimizationService OptimizationService { get; }
    
    public FullscreenManagerService(
        IFullscreenDetectionService detectionService,
        IFullscreenOptimizationService optimizationService,
        IEventAggregator eventAggregator,
        ILogger<FullscreenManagerService>? logger = null)
    {
        DetectionService = detectionService ?? throw new ArgumentNullException(nameof(detectionService));
        OptimizationService = optimizationService ?? throw new ArgumentNullException(nameof(optimizationService));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger;
        
        // イベントハンドラーを登録
        RegisterEventHandlers();
        
        _logger?.LogDebug("FullscreenManagerService initialized");
    }
    
    /// <summary>
    /// フルスクリーン管理を開始します
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FullscreenManagerService));
        
        if (_isRunning)
        {
            _logger?.LogWarning("Fullscreen manager is already running");
            return;
        }
        
        _logger?.LogInformation("Starting fullscreen manager service");
        
        try
        {
            // 検出開始イベントを発行
            var detectionSettings = DetectionService.Settings ?? new FullscreenDetectionSettings();
            await _eventAggregator.PublishAsync(new FullscreenDetectionStartedEvent(detectionSettings)).ConfigureAwait(false);
            
            // フルスクリーン最適化サービスを開始
            await OptimizationService.StartOptimizationAsync(cancellationToken).ConfigureAwait(false);
            
            // フルスクリーン検出サービスを開始
            await DetectionService.StartMonitoringAsync(cancellationToken).ConfigureAwait(false);
            
            _isRunning = true;
            
            _logger?.LogInformation("Fullscreen manager service started successfully");
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("Fullscreen manager service start was cancelled");
            throw;
        }
        catch (ObjectDisposedException ex)
        {
            _logger?.LogError(ex, "Object disposed while starting fullscreen manager service");
            await _eventAggregator.PublishAsync(new FullscreenOptimizationErrorEvent(ex, "FullscreenManagerService.StartAsync")).ConfigureAwait(false);
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Invalid operation while starting fullscreen manager service");
            await _eventAggregator.PublishAsync(new FullscreenOptimizationErrorEvent(ex, "FullscreenManagerService.StartAsync")).ConfigureAwait(false);
            throw;
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "Invalid argument while starting fullscreen manager service");
            await _eventAggregator.PublishAsync(new FullscreenOptimizationErrorEvent(ex, "FullscreenManagerService.StartAsync")).ConfigureAwait(false);
            throw;
        }
    }
    
    /// <summary>
    /// フルスクリーン管理を停止します
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning || _disposed)
        {
            return;
        }
        
        _logger?.LogInformation("Stopping fullscreen manager service");
        
        try
        {
            var startTime = DateTime.Now;
            
            // フルスクリーン最適化サービスを停止
            await OptimizationService.StopOptimizationAsync().ConfigureAwait(false);
            
            // フルスクリーン検出サービスを停止
            await DetectionService.StopMonitoringAsync().ConfigureAwait(false);
            
            var duration = DateTime.Now - startTime;
            
            // 検出停止イベントを発行
            await _eventAggregator.PublishAsync(new FullscreenDetectionStoppedEvent("Manual stop", duration)).ConfigureAwait(false);
            
            _isRunning = false;
            
            _logger?.LogInformation("Fullscreen manager service stopped successfully");
        }
        catch (ObjectDisposedException)
        {
            _logger?.LogDebug("Service already disposed during stop operation");
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Invalid operation while stopping fullscreen manager service");
            await _eventAggregator.PublishAsync(new FullscreenOptimizationErrorEvent(ex, "FullscreenManagerService.StopAsync")).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "Invalid argument while stopping fullscreen manager service");
            await _eventAggregator.PublishAsync(new FullscreenOptimizationErrorEvent(ex, "FullscreenManagerService.StopAsync")).ConfigureAwait(false);
        }
    }
    
    /// <summary>
    /// 現在のフルスクリーン状態を取得します
    /// </summary>
    /// <returns>フルスクリーン情報</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public async Task<FullscreenInfo> GetCurrentFullscreenStateAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FullscreenManagerService));
        
        return await DetectionService.DetectCurrentFullscreenAsync().ConfigureAwait(false);
    }
    
    /// <summary>
    /// 指定されたウィンドウのフルスクリーン状態を取得します
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>フルスクリーン情報</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public async Task<FullscreenInfo> GetWindowFullscreenStateAsync(IntPtr windowHandle)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FullscreenManagerService));
        
        return await DetectionService.DetectFullscreenAsync(windowHandle).ConfigureAwait(false);
    }
    
    /// <summary>
    /// フルスクリーン最適化を手動で適用します
    /// </summary>
    /// <param name="fullscreenInfo">フルスクリーン情報</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public async Task ApplyOptimizationAsync(FullscreenInfo fullscreenInfo)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FullscreenManagerService));
        
        try
        {
            await OptimizationService.ApplyOptimizationAsync(fullscreenInfo).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "Invalid fullscreen info provided for optimization");
            await _eventAggregator.PublishAsync(new FullscreenOptimizationErrorEvent(ex, "Manual optimization application")).ConfigureAwait(false);
            throw;
        }
        catch (ObjectDisposedException ex)
        {
            _logger?.LogError(ex, "Object disposed while applying fullscreen optimization");
            await _eventAggregator.PublishAsync(new FullscreenOptimizationErrorEvent(ex, "Manual optimization application")).ConfigureAwait(false);
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Invalid operation while applying fullscreen optimization");
            await _eventAggregator.PublishAsync(new FullscreenOptimizationErrorEvent(ex, "Manual optimization application")).ConfigureAwait(false);
            throw;
        }
    }
    
    /// <summary>
    /// フルスクリーン最適化を手動で解除します
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public async Task RemoveOptimizationAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FullscreenManagerService));
        
        try
        {
            await OptimizationService.RemoveOptimizationAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException ex)
        {
            _logger?.LogError(ex, "Object disposed while removing fullscreen optimization");
            await _eventAggregator.PublishAsync(new FullscreenOptimizationErrorEvent(ex, "Manual optimization removal")).ConfigureAwait(false);
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Invalid operation when removing fullscreen optimization");
            await _eventAggregator.PublishAsync(new FullscreenOptimizationErrorEvent(ex, "Manual optimization removal")).ConfigureAwait(false);
            throw;
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "Invalid argument while removing fullscreen optimization");
            await _eventAggregator.PublishAsync(new FullscreenOptimizationErrorEvent(ex, "Manual optimization removal")).ConfigureAwait(false);
            throw;
        }
    }
    
    /// <summary>
    /// フルスクリーン検出設定を更新します
    /// </summary>
    /// <param name="settings">新しい検出設定</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public void UpdateDetectionSettings(FullscreenDetectionSettings settings)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FullscreenManagerService));
        
        DetectionService.UpdateDetectionSettings(settings);
        _logger?.LogDebug("Fullscreen detection settings updated");
    }
    
    /// <summary>
    /// 最適化統計情報をリセットします
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public void ResetOptimizationStatistics()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FullscreenManagerService));
        
        OptimizationService.ResetStatistics();
        _logger?.LogDebug("Fullscreen optimization statistics reset");
    }
    
    /// <summary>
    /// フルスクリーン最適化を強制的にリセットします
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public async Task ForceResetOptimizationAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FullscreenManagerService));
        
        try
        {
            await OptimizationService.ForceResetAsync().ConfigureAwait(false);
            _logger?.LogInformation("Fullscreen optimization force reset completed");
        }
        catch (ObjectDisposedException ex)
        {
            _logger?.LogError(ex, "Object disposed during force reset");
            await _eventAggregator.PublishAsync(new FullscreenOptimizationErrorEvent(ex, "Force reset")).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Invalid operation during force reset");
            await _eventAggregator.PublishAsync(new FullscreenOptimizationErrorEvent(ex, "Force reset")).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "Invalid argument during force reset");
            await _eventAggregator.PublishAsync(new FullscreenOptimizationErrorEvent(ex, "Force reset")).ConfigureAwait(false);
        }
    }
    
    /// <summary>
    /// フルスクリーン最適化の有効/無効を切り替えます
    /// </summary>
    /// <param name="enabled">有効にするかどうか</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public void SetOptimizationEnabled(bool enabled)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FullscreenManagerService));
        
        OptimizationService.IsEnabled = enabled;
        _logger?.LogInformation("Fullscreen optimization {Status}", enabled ? "enabled" : "disabled");
    }
    
    /// <summary>
    /// イベントハンドラーを登録
    /// </summary>
    private void RegisterEventHandlers()
    {
        // フルスクリーン状態変更イベントの処理
        DetectionService.FullscreenStateChanged += async (sender, fullscreenInfo) =>
        {
            try
            {
                await _eventAggregator.PublishAsync(new FullscreenStateChangedEvent(fullscreenInfo)).ConfigureAwait(false);
            }
            catch (ObjectDisposedException ex)
            {
                _logger?.LogError(ex, "Object disposed while publishing fullscreen state changed event");
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "Invalid operation while publishing fullscreen state changed event");
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "Invalid argument while publishing fullscreen state changed event");
            }
        };
        
        // 最適化適用イベントの処理
        OptimizationService.OptimizationApplied += async (sender, args) =>
        {
            try
            {
                await _eventAggregator.PublishAsync(new FullscreenOptimizationAppliedEvent(
                    args.FullscreenInfo, args.OptimizedSettings, args.OriginalSettings)).ConfigureAwait(false);
            }
            catch (ObjectDisposedException ex)
            {
                _logger?.LogError(ex, "Object disposed while publishing optimization applied event");
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "Invalid operation while publishing optimization applied event");
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "Invalid argument while publishing optimization applied event");
            }
        };
        
        // 最適化解除イベントの処理
        OptimizationService.OptimizationRemoved += async (sender, args) =>
        {
            try
            {
                await _eventAggregator.PublishAsync(new FullscreenOptimizationRemovedEvent(
                    args.RestoredSettings, args.Reason)).ConfigureAwait(false);
            }
            catch (ObjectDisposedException ex)
            {
                _logger?.LogError(ex, "Object disposed while publishing optimization removed event");
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "Invalid operation while publishing optimization removed event");
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "Invalid argument while publishing optimization removed event");
            }
        };
        
        // 最適化エラーイベントの処理
        OptimizationService.OptimizationError += async (sender, args) =>
        {
            try
            {
                await _eventAggregator.PublishAsync(new FullscreenOptimizationErrorEvent(
                    args.Exception, args.Message)).ConfigureAwait(false);
            }
            catch (ObjectDisposedException ex)
            {
                _logger?.LogError(ex, "Object disposed while publishing optimization error event");
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "Invalid operation while publishing optimization error event");
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "Invalid argument while publishing optimization error event");
            }
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
        
        if (_isRunning)
        {
            // 非同期停止処理を同期実行（Disposeパターンのため）
            try
            {
                // 同期的な停止処理（Disposeパターンでは非同期待機不可）
                Task.Run(async () => await StopAsync().ConfigureAwait(false)).Wait(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException ex)
            {
                _logger?.LogError(ex, "Timeout during disposal");
            }
            catch (AggregateException ex)
            {
                _logger?.LogError(ex.InnerException ?? ex, "Error occurred during disposal");
            }
            catch (ObjectDisposedException ex)
            {
                _logger?.LogError(ex, "Object already disposed during disposal");
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "Invalid operation during disposal");
            }
        }
        
        _disposed = true;
        _logger?.LogDebug("FullscreenManagerService disposed");
    }
}
