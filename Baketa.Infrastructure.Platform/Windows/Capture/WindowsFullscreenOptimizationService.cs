using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Settings;

namespace Baketa.Infrastructure.Platform.Windows.Capture;

/// <summary>
/// Windows固有のフルスクリーン最適化サービス実装
/// フルスクリーン検出時にキャプチャ設定を自動最適化し、パフォーマンスを向上
/// </summary>
public sealed class WindowsFullscreenOptimizationService : IFullscreenOptimizationService, IDisposable
{
    private readonly IFullscreenDetectionService _fullscreenDetection;
    private readonly IAdvancedCaptureService _captureService;
    private readonly ILogger<WindowsFullscreenOptimizationService>? _logger;
    
    private bool _isEnabled = true;
    private bool _isOptimizationActive;
    private CaptureSettings? _originalSettings;
    private bool _disposed;
    private CancellationTokenSource? _optimizationCancellation;
    
    /// <summary>
    /// 現在の最適化状態
    /// </summary>
    public FullscreenOptimizationStatus Status { get; private set; } = FullscreenOptimizationStatus.Disabled;
    
    /// <summary>
    /// 最適化統計情報
    /// </summary>
    public FullscreenOptimizationStats Statistics { get; } = new();
    
    /// <summary>
    /// 最適化サービスが有効かどうか
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                _logger?.LogInformation("Fullscreen optimization {Status}", value ? "enabled" : "disabled");
                
                if (!value && _isOptimizationActive)
                {
                    // 非同期処理は後でSetIsEnabledAsyncメソッドから実行
                    _ = Task.Run(async () => await RemoveOptimizationAsync().ConfigureAwait(false));
                }
            }
        }
    }
    
    /// <summary>
    /// 現在フルスクリーン最適化が適用されているかどうか
    /// </summary>
    public bool IsOptimizationActive => _isOptimizationActive && !_disposed;
    
    /// <summary>
    /// 現在のフルスクリーン情報
    /// </summary>
    public FullscreenInfo? CurrentFullscreenInfo { get; private set; }
    
    /// <summary>
    /// フルスクリーン最適化が適用されたときのイベント
    /// </summary>
    public event EventHandler<FullscreenOptimizationAppliedEventArgs>? OptimizationApplied;
    
    /// <summary>
    /// フルスクリーン最適化が解除されたときのイベント
    /// </summary>
    public event EventHandler<FullscreenOptimizationRemovedEventArgs>? OptimizationRemoved;
    
    /// <summary>
    /// 最適化処理でエラーが発生したときのイベント
    /// </summary>
    public event EventHandler<FullscreenOptimizationErrorEventArgs>? OptimizationError;
    
    public WindowsFullscreenOptimizationService(
        IFullscreenDetectionService fullscreenDetection,
        IAdvancedCaptureService captureService,
        ILogger<WindowsFullscreenOptimizationService>? logger = null)
    {
        _fullscreenDetection = fullscreenDetection ?? throw new ArgumentNullException(nameof(fullscreenDetection));
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _logger = logger;
        
        // フルスクリーン状態変更イベントを監視
        _fullscreenDetection.FullscreenStateChanged += OnFullscreenStateChanged;
        
        _logger?.LogDebug("WindowsFullscreenOptimizationService initialized");
    }
    
    /// <summary>
    /// フルスクリーン最適化を開始します
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public async Task StartOptimizationAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WindowsFullscreenOptimizationService));
        
        if (Status != FullscreenOptimizationStatus.Disabled)
        {
            _logger?.LogWarning("Fullscreen optimization is already running or in process");
            return;
        }
        
        _logger?.LogInformation("Starting fullscreen optimization service");
        
        try
        {
            Status = FullscreenOptimizationStatus.Standby;
            _optimizationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            // 元の設定をバックアップ
            _originalSettings = _captureService.GetCurrentSettings();
            
            // フルスクリーン検出サービスが実行されていない場合は開始
            if (!_fullscreenDetection.IsRunning)
            {
                await _fullscreenDetection.StartMonitoringAsync(_optimizationCancellation.Token).ConfigureAwait(false);
            }
            
            _logger?.LogInformation("Fullscreen optimization service started successfully");
        }
        catch (OperationCanceledException)
        {
            Status = FullscreenOptimizationStatus.Disabled;
            throw;
        }
        catch (ObjectDisposedException ex)
        {
            Status = FullscreenOptimizationStatus.Error;
            _logger?.LogError(ex, "Service disposed while starting optimization");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            Status = FullscreenOptimizationStatus.Error;
            Statistics.ErrorCount++;
            Statistics.LastErrorTime = DateTime.Now;
            
            _logger?.LogError(ex, "Invalid operation while starting fullscreen optimization service");
            OnOptimizationError(ex, "StartOptimizationAsync");
            throw;
        }
    }
    
    /// <summary>
    /// フルスクリーン最適化を停止します
    /// </summary>
    public async Task StopOptimizationAsync()
    {
        if (_disposed || Status == FullscreenOptimizationStatus.Disabled)
        {
            return;
        }
        
        _logger?.LogInformation("Stopping fullscreen optimization service");
        
        try
        {
            Status = FullscreenOptimizationStatus.Disabled;
            
            // 現在の最適化を解除
            if (_isOptimizationActive)
            {
                await RemoveOptimizationAsync().ConfigureAwait(false);
            }
            
            // 監視を停止
#pragma warning disable CA1849 // Call async methods when in an async method - Cancel() has no async version
            _optimizationCancellation?.Cancel();
#pragma warning restore CA1849
            _optimizationCancellation?.Dispose();
            _optimizationCancellation = null;
            
            _logger?.LogInformation("Fullscreen optimization service stopped");
        }
        catch (ObjectDisposedException ex)
        {
            _logger?.LogError(ex, "Object disposed while stopping fullscreen optimization service");
            OnOptimizationError(ex, "StopOptimizationAsync");
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Invalid operation while stopping fullscreen optimization service");
            OnOptimizationError(ex, "StopOptimizationAsync");
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "Invalid argument while stopping fullscreen optimization service");
            OnOptimizationError(ex, "StopOptimizationAsync");
        }
    }
    
    /// <summary>
    /// 指定されたフルスクリーン情報に基づいて最適化を手動適用します
    /// </summary>
    /// <param name="fullscreenInfo">フルスクリーン情報</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1513:Use ObjectDisposedException throw helper", Justification = "ThrowIfDisposed is not available in current .NET version")]
    public async Task ApplyOptimizationAsync(FullscreenInfo fullscreenInfo)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WindowsFullscreenOptimizationService));
        ArgumentNullException.ThrowIfNull(fullscreenInfo);
        
        if (!_isEnabled)
        {
            _logger?.LogDebug("Fullscreen optimization is disabled, skipping application");
            return;
        }
        
        if (_isOptimizationActive)
        {
            _logger?.LogDebug("Fullscreen optimization is already active");
            return;
        }
        
        try
        {
            Status = FullscreenOptimizationStatus.Optimizing;
            _logger?.LogInformation("Applying fullscreen optimization for {ProcessName} (Confidence: {Confidence:F2})", 
                fullscreenInfo.ProcessName, fullscreenInfo.Confidence);
            
            // 元の設定を保存（まだ保存されていない場合）
            _originalSettings ??= _captureService.GetCurrentSettings();
            
            // 最適化設定を作成・適用
            var optimizedSettings = CreateOptimizedSettings(fullscreenInfo, _originalSettings);
            _captureService.UpdateSettings(optimizedSettings);
            
            // 状態更新
            CurrentFullscreenInfo = fullscreenInfo;
            _isOptimizationActive = true;
            Status = FullscreenOptimizationStatus.Active;
            
            // 統計更新
            Statistics.OptimizationAppliedCount++;
            Statistics.LastOptimizationTime = DateTime.Now;
            Statistics.CurrentOptimizedWindow = $"{fullscreenInfo.ProcessName} - {fullscreenInfo.WindowTitle}";
            
            // イベント発行
            var eventArgs = new FullscreenOptimizationAppliedEventArgs(fullscreenInfo, optimizedSettings, _originalSettings);
            OptimizationApplied?.Invoke(this, eventArgs);
            
            _logger?.LogInformation("Fullscreen optimization applied successfully");
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (ObjectDisposedException ex)
        {
            Status = FullscreenOptimizationStatus.Error;
            Statistics.ErrorCount++;
            Statistics.LastErrorTime = DateTime.Now;
            
            _logger?.LogError(ex, "Object disposed while applying fullscreen optimization");
            OnOptimizationError(ex, "ApplyOptimizationAsync");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            Status = FullscreenOptimizationStatus.Error;
            Statistics.ErrorCount++;
            Statistics.LastErrorTime = DateTime.Now;
            
            _logger?.LogError(ex, "Invalid operation while applying fullscreen optimization");
            OnOptimizationError(ex, "ApplyOptimizationAsync");
            throw;
        }
        catch (ArgumentException ex)
        {
            Status = FullscreenOptimizationStatus.Error;
            Statistics.ErrorCount++;
            Statistics.LastErrorTime = DateTime.Now;
            
            _logger?.LogError(ex, "Invalid argument while applying fullscreen optimization");
            OnOptimizationError(ex, "ApplyOptimizationAsync");
            throw;
        }
    }
    
    /// <summary>
    /// 現在の最適化を手動で解除し、元の設定に復元します
    /// </summary>
    public async Task RemoveOptimizationAsync()
    {
        if (!_isOptimizationActive || _originalSettings == null)
        {
            return;
        }
        
        try
        {
            Status = FullscreenOptimizationStatus.Restoring;
            _logger?.LogInformation("Removing fullscreen optimization");
            
            // 元の設定に復元
            _captureService.UpdateSettings(_originalSettings);
            
            // 状態更新
            var restoredSettings = _originalSettings;
            var windowInfo = Statistics.CurrentOptimizedWindow;
            
            _isOptimizationActive = false;
            CurrentFullscreenInfo = null;
            Status = FullscreenOptimizationStatus.Standby;
            
            // 統計更新
            Statistics.OptimizationRemovedCount++;
            Statistics.CurrentOptimizedWindow = null;
            
            // イベント発行
            var eventArgs = new FullscreenOptimizationRemovedEventArgs(restoredSettings, "Manual removal");
            OptimizationRemoved?.Invoke(this, eventArgs);
            
            _logger?.LogInformation("Fullscreen optimization removed successfully");
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (ObjectDisposedException ex)
        {
            Status = FullscreenOptimizationStatus.Error;
            Statistics.ErrorCount++;
            Statistics.LastErrorTime = DateTime.Now;
            
            _logger?.LogError(ex, "Object disposed while removing fullscreen optimization");
            OnOptimizationError(ex, "RemoveOptimizationAsync");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            Status = FullscreenOptimizationStatus.Error;
            Statistics.ErrorCount++;
            Statistics.LastErrorTime = DateTime.Now;
            
            _logger?.LogError(ex, "Invalid operation while removing fullscreen optimization");
            OnOptimizationError(ex, "RemoveOptimizationAsync");
            throw;
        }
        catch (ArgumentException ex)
        {
            Status = FullscreenOptimizationStatus.Error;
            Statistics.ErrorCount++;
            Statistics.LastErrorTime = DateTime.Now;
            
            _logger?.LogError(ex, "Invalid argument while removing fullscreen optimization");
            OnOptimizationError(ex, "RemoveOptimizationAsync");
            throw;
        }
    }
    
    /// <summary>
    /// 最適化設定を強制的にリセットします
    /// </summary>
    public async Task ForceResetAsync()
    {
        _logger?.LogWarning("Force resetting fullscreen optimization");
        
        try
        {
            _isOptimizationActive = false;
            CurrentFullscreenInfo = null;
            Status = FullscreenOptimizationStatus.Standby;
            
            if (_originalSettings != null)
            {
                _captureService.UpdateSettings(_originalSettings);
            }
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (ObjectDisposedException ex)
        {
            _logger?.LogError(ex, "Object disposed during force reset");
            OnOptimizationError(ex, "ForceResetAsync");
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Invalid operation during force reset");
            OnOptimizationError(ex, "ForceResetAsync");
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "Invalid argument during force reset");
            OnOptimizationError(ex, "ForceResetAsync");
        }
    }
    
    /// <summary>
    /// 統計情報をリセットします
    /// </summary>
    public void ResetStatistics()
    {
        Statistics.Reset();
        _logger?.LogDebug("Fullscreen optimization statistics reset");
    }
    
    /// <summary>
    /// フルスクリーン状態変更イベントハンドラー
    /// </summary>
    /// <param name="sender">送信者</param>
    /// <param name="fullscreenInfo">フルスクリーン情報</param>
    private async void OnFullscreenStateChanged(object? sender, FullscreenInfo fullscreenInfo)
    {
        if (!_isEnabled || _disposed)
        {
            return;
        }
        
        try
        {
            if (fullscreenInfo.IsFullscreen && fullscreenInfo.Confidence >= 0.8)
            {
                // フルスクリーン検出時 - 最適化適用
                if (!_isOptimizationActive)
                {
                    await ApplyOptimizationAsync(fullscreenInfo).ConfigureAwait(false);
                }
            }
            else
            {
                // ウィンドウモード復帰時 - 最適化解除
                if (_isOptimizationActive)
                {
                    await RemoveOptimizationAsync().ConfigureAwait(false);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // サービスが破棄済みの場合は無視
            return;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Invalid operation during fullscreen state change handling");
            OnOptimizationError(ex, "OnFullscreenStateChanged");
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "Invalid argument during fullscreen state change handling");
            OnOptimizationError(ex, "OnFullscreenStateChanged");
        }
    }
    
    /// <summary>
    /// 最適化設定を作成
    /// </summary>
    /// <param name="fullscreenInfo">フルスクリーン情報</param>
    /// <param name="originalSettings">元の設定</param>
    /// <returns>最適化された設定</returns>
    private static CaptureSettings CreateOptimizedSettings(FullscreenInfo fullscreenInfo, CaptureSettings originalSettings)
    {
        var optimizedSettings = originalSettings.Clone();
        
        // フルスクリーン専用最適化を有効化
        optimizedSettings.FullscreenOptimization = true;
        optimizedSettings.AutoDetectCaptureArea = false; // 固定領域使用
        
        // 解像度に基づく最適化
        var screenArea = fullscreenInfo.MonitorBounds.Width * fullscreenInfo.MonitorBounds.Height;
        
        if (screenArea > 1920 * 1080) // 高解像度画面（1080p超）
        {
            // 高解像度では負荷軽減を重視
            optimizedSettings.CaptureIntervalMs = Math.Max(optimizedSettings.CaptureIntervalMs, 300);
            optimizedSettings.CaptureQuality = Math.Min(optimizedSettings.CaptureQuality, 75);
            optimizedSettings.DifferenceDetectionGridSize = Math.Max(optimizedSettings.DifferenceDetectionGridSize, 20);
            optimizedSettings.DifferenceDetectionSensitivity = Math.Max(optimizedSettings.DifferenceDetectionSensitivity, 40);
        }
        else if (screenArea > 1366 * 768) // 標準解像度（1080p以下）
        {
            // 標準解像度ではバランス重視
            optimizedSettings.CaptureIntervalMs = Math.Max(optimizedSettings.CaptureIntervalMs, 200);
            optimizedSettings.CaptureQuality = Math.Min(optimizedSettings.CaptureQuality, 85);
            optimizedSettings.DifferenceDetectionGridSize = Math.Max(optimizedSettings.DifferenceDetectionGridSize, 16);
        }
        else // 低解像度
        {
            // 低解像度では品質重視
            optimizedSettings.CaptureIntervalMs = Math.Max(optimizedSettings.CaptureIntervalMs, 150);
            optimizedSettings.CaptureQuality = Math.Min(optimizedSettings.CaptureQuality, 90);
            optimizedSettings.DifferenceDetectionGridSize = Math.Max(optimizedSettings.DifferenceDetectionGridSize, 12);
        }
        
        // フルスクリーン領域を固定設定
        optimizedSettings.FixedCaptureAreaX = fullscreenInfo.MonitorBounds.X;
        optimizedSettings.FixedCaptureAreaY = fullscreenInfo.MonitorBounds.Y;
        optimizedSettings.FixedCaptureAreaWidth = fullscreenInfo.MonitorBounds.Width;
        optimizedSettings.FixedCaptureAreaHeight = fullscreenInfo.MonitorBounds.Height;
        
        // ゲームの場合はさらに最適化
        if (fullscreenInfo.IsLikelyGame)
        {
            optimizedSettings.UseHardwareAcceleration = true;
            optimizedSettings.AutoOptimizeForGames = true;
        }
        
        return optimizedSettings;
    }
    
    /// <summary>
    /// 最適化エラーイベントを発行
    /// </summary>
    /// <param name="exception">例外</param>
    /// <param name="context">コンテキスト</param>
    private void OnOptimizationError(Exception exception, string context)
    {
        var eventArgs = new FullscreenOptimizationErrorEventArgs(exception, context);
        OptimizationError?.Invoke(this, eventArgs);
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
        
        _fullscreenDetection.FullscreenStateChanged -= OnFullscreenStateChanged;
        
        _optimizationCancellation?.Cancel();
        _optimizationCancellation?.Dispose();
        
        _disposed = true;
        _logger?.LogDebug("WindowsFullscreenOptimizationService disposed");
    }
}
