using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Abstractions.Events;

namespace Baketa.Application.EventHandlers.Capture;

/// <summary>
/// フルスクリーン状態変更イベントハンドラー
/// フルスクリーン状態の変更を処理し、ログ記録やシステム通知を行う
/// </summary>
public class FullscreenStateChangedEventHandler(ILogger<FullscreenStateChangedEventHandler>? logger = null) : IEventProcessor<FullscreenStateChangedEvent>
{
    private readonly ILogger<FullscreenStateChangedEventHandler>? _logger = logger;

    /// <summary>
    /// フルスクリーン状態変更イベントを処理します
    /// </summary>
    /// <param name="eventData">イベントデータ</param>
    public async Task HandleAsync(FullscreenStateChangedEvent eventData)
    {
        await HandleInternal(eventData).ConfigureAwait(false);
    }
    
    public int Priority => 0;
    public bool SynchronousExecution => false;
    
    private async Task HandleInternal(FullscreenStateChangedEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        
        var info = eventData.FullscreenInfo;
        
        if (info.IsFullscreen)
        {
            _logger?.LogInformation("Fullscreen detected: {ProcessName} ({WindowTitle}) - Confidence: {Confidence:F2}",
                info.ProcessName, info.WindowTitle, info.Confidence);
            
            if (info.IsLikelyGame)
            {
                _logger?.LogInformation("Game detected in fullscreen mode: {ProcessName}", info.ProcessName);
            }
        }
        else
        {
            _logger?.LogInformation("Windowed mode detected: {ProcessName}", info.ProcessName);
        }
        
        // 追加の処理（必要に応じて）
        // 例: システム通知、統計記録、他のサービスへの通知など
        
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>
/// フルスクリーン最適化適用イベントハンドラー
/// 最適化が適用されたときの処理を行う
/// </summary>
public class FullscreenOptimizationAppliedEventHandler(ILogger<FullscreenOptimizationAppliedEventHandler>? logger = null) : IEventProcessor<FullscreenOptimizationAppliedEvent>
{
    private readonly ILogger<FullscreenOptimizationAppliedEventHandler>? _logger = logger;

    /// <summary>
    /// フルスクリーン最適化適用イベントを処理します
    /// </summary>
    /// <param name="eventData">イベントデータ</param>
    public async Task HandleAsync(FullscreenOptimizationAppliedEvent eventData)
    {
        await HandleInternal(eventData).ConfigureAwait(false);
    }
    
    public int Priority => 0;
    public bool SynchronousExecution => false;
    
    private async Task HandleInternal(FullscreenOptimizationAppliedEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        
        var info = eventData.FullscreenInfo;
        var settings = eventData.OptimizedSettings;
        
        _logger?.LogInformation("Fullscreen optimization applied for {ProcessName}: " +
                               "Interval={IntervalMs}ms, Quality={Quality}%, GridSize={GridSize}",
            info.ProcessName, settings.CaptureIntervalMs, settings.CaptureQuality, settings.DifferenceDetectionGridSize);
        
        // パフォーマンス向上の通知や記録
        if (eventData.OriginalSettings != null)
        {
            var intervalImprovement = settings.CaptureIntervalMs - eventData.OriginalSettings.CaptureIntervalMs;
            if (intervalImprovement > 0)
            {
                _logger?.LogDebug("Capture interval optimized: +{Improvement}ms for performance", intervalImprovement);
            }
        }
        
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>
/// フルスクリーン最適化解除イベントハンドラー
/// 最適化が解除されたときの処理を行う
/// </summary>
public class FullscreenOptimizationRemovedEventHandler(ILogger<FullscreenOptimizationRemovedEventHandler>? logger = null) : IEventProcessor<FullscreenOptimizationRemovedEvent>
{
    private readonly ILogger<FullscreenOptimizationRemovedEventHandler>? _logger = logger;

    /// <summary>
    /// フルスクリーン最適化解除イベントを処理します
    /// </summary>
    /// <param name="eventData">イベントデータ</param>
    public async Task HandleAsync(FullscreenOptimizationRemovedEvent eventData)
    {
        await HandleInternal(eventData).ConfigureAwait(false);
    }
    
    public int Priority => 0;
    public bool SynchronousExecution => false;
    
    private async Task HandleInternal(FullscreenOptimizationRemovedEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        
        _logger?.LogInformation("Fullscreen optimization removed: {Reason}", eventData.Reason);
        
        if (eventData.RestoredSettings != null)
        {
            _logger?.LogDebug("Settings restored: Interval={IntervalMs}ms, Quality={Quality}%",
                eventData.RestoredSettings.CaptureIntervalMs, eventData.RestoredSettings.CaptureQuality);
        }
        
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>
/// フルスクリーン最適化エラーイベントハンドラー
/// 最適化でエラーが発生したときの処理を行う
/// </summary>
public class FullscreenOptimizationErrorEventHandler(ILogger<FullscreenOptimizationErrorEventHandler>? logger = null) : IEventProcessor<FullscreenOptimizationErrorEvent>
{
    private readonly ILogger<FullscreenOptimizationErrorEventHandler>? _logger = logger;

    /// <summary>
    /// フルスクリーン最適化エラーイベントを処理します
    /// </summary>
    /// <param name="eventData">イベントデータ</param>
    public async Task HandleAsync(FullscreenOptimizationErrorEvent eventData)
    {
        await HandleInternal(eventData).ConfigureAwait(false);
    }
    
    public int Priority => 0;
    public bool SynchronousExecution => false;
    
    private async Task HandleInternal(FullscreenOptimizationErrorEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        
        _logger?.LogError(eventData.Exception, "Fullscreen optimization error in {Context}: {Message}",
            eventData.Context, eventData.ErrorMessage);
        
        // エラー統計の記録や復旧処理（必要に応じて）
        // 例: エラー回数のカウント、自動復旧の試行など
        
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>
/// フルスクリーン検出開始イベントハンドラー
/// </summary>
public class FullscreenDetectionStartedEventHandler(ILogger<FullscreenDetectionStartedEventHandler>? logger = null) : IEventProcessor<FullscreenDetectionStartedEvent>
{
    private readonly ILogger<FullscreenDetectionStartedEventHandler>? _logger = logger;

    public async Task HandleAsync(FullscreenDetectionStartedEvent eventData)
    {
        await HandleInternal(eventData).ConfigureAwait(false);
    }
    
    public int Priority => 0;
    public bool SynchronousExecution => false;
    
    private async Task HandleInternal(FullscreenDetectionStartedEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        
        _logger?.LogInformation("Fullscreen detection started: Interval={IntervalMs}ms, MinConfidence={MinConfidence:F2}",
            eventData.Settings.DetectionIntervalMs, eventData.Settings.MinConfidence);
        
        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>
/// フルスクリーン検出停止イベントハンドラー
/// </summary>
public class FullscreenDetectionStoppedEventHandler(ILogger<FullscreenDetectionStoppedEventHandler>? logger = null) : IEventProcessor<FullscreenDetectionStoppedEvent>
{
    private readonly ILogger<FullscreenDetectionStoppedEventHandler>? _logger = logger;

    public async Task HandleAsync(FullscreenDetectionStoppedEvent eventData)
    {
        await HandleInternal(eventData).ConfigureAwait(false);
    }
    
    public int Priority => 0;
    public bool SynchronousExecution => false;
    
    private async Task HandleInternal(FullscreenDetectionStoppedEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        
        var duration = eventData.RunDuration?.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) ?? "Unknown";
        _logger?.LogInformation("Fullscreen detection stopped: {Reason} (Duration: {Duration})",
            eventData.Reason, duration);
        
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
