using System;
using System.Globalization;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Settings;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// フルスクリーン状態変更イベント
/// </summary>
public class FullscreenStateChangedEvent : EventBase
{
    /// <summary>
    /// フルスクリーン情報
    /// </summary>
    public FullscreenInfo FullscreenInfo { get; }
    
    /// <summary>
    /// 前回のフルスクリーン状態
    /// </summary>
    public bool? PreviousFullscreenState { get; }
    
    public FullscreenStateChangedEvent(FullscreenInfo fullscreenInfo, bool? previousFullscreenState = null)
    {
        FullscreenInfo = fullscreenInfo ?? throw new ArgumentNullException(nameof(fullscreenInfo));
        PreviousFullscreenState = previousFullscreenState;
    }
    
    public override string Name => "FullscreenStateChanged";
    public override string Category => "Capture";
    
    public override string ToString()
    {
        return $"{Name}: {FullscreenInfo.ProcessName} - {(FullscreenInfo.IsFullscreen ? "Fullscreen" : "Windowed")} " +
               $"(Confidence: {FullscreenInfo.Confidence:F2})";
    }
}

/// <summary>
/// フルスクリーン最適化適用イベント
/// </summary>
public class FullscreenOptimizationAppliedEvent : EventBase
{
    /// <summary>
    /// フルスクリーン情報
    /// </summary>
    public FullscreenInfo FullscreenInfo { get; }
    
    /// <summary>
    /// 最適化されたキャプチャ設定
    /// </summary>
    public CaptureSettings OptimizedSettings { get; }
    
    /// <summary>
    /// 元のキャプチャ設定
    /// </summary>
    public CaptureSettings? OriginalSettings { get; }
    
    public FullscreenOptimizationAppliedEvent(
        FullscreenInfo fullscreenInfo, 
        CaptureSettings optimizedSettings, 
        CaptureSettings? originalSettings = null)
    {
        FullscreenInfo = fullscreenInfo ?? throw new ArgumentNullException(nameof(fullscreenInfo));
        OptimizedSettings = optimizedSettings ?? throw new ArgumentNullException(nameof(optimizedSettings));
        OriginalSettings = originalSettings;
    }
    
    public override string Name => "FullscreenOptimizationApplied";
    public override string Category => "Capture";
    
    public override string ToString()
    {
        return $"{Name}: {FullscreenInfo.ProcessName} - Interval: {OptimizedSettings.CaptureIntervalMs}ms, " +
               $"Quality: {OptimizedSettings.CaptureQuality}%";
    }
}

/// <summary>
/// フルスクリーン最適化解除イベント
/// </summary>
public class FullscreenOptimizationRemovedEvent : EventBase
{
    /// <summary>
    /// 復元されたキャプチャ設定
    /// </summary>
    public CaptureSettings? RestoredSettings { get; }
    
    /// <summary>
    /// 解除理由
    /// </summary>
    public string Reason { get; }
    
    /// <summary>
    /// 対象ウィンドウ情報
    /// </summary>
    public string? WindowInfo { get; }
    
    public FullscreenOptimizationRemovedEvent(
        CaptureSettings? restoredSettings = null, 
        string reason = "", 
        string? windowInfo = null)
    {
        RestoredSettings = restoredSettings;
        Reason = reason;
        WindowInfo = windowInfo;
    }
    
    public override string Name => "FullscreenOptimizationRemoved";
    public override string Category => "Capture";
    
    public override string ToString()
    {
        var settingsInfo = RestoredSettings != null 
            ? $"Restored: Interval {RestoredSettings.CaptureIntervalMs}ms"
            : "No settings restored";
        return $"{Name}: {Reason} - {settingsInfo}";
    }
}

/// <summary>
/// フルスクリーン最適化エラーイベント
/// </summary>
public class FullscreenOptimizationErrorEvent : EventBase
{
    /// <summary>
    /// 発生した例外
    /// </summary>
    public Exception Exception { get; }
    
    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string ErrorMessage { get; }
    
    /// <summary>
    /// エラーコンテキスト
    /// </summary>
    public string Context { get; }
    
    public FullscreenOptimizationErrorEvent(Exception exception, string context = "", string? errorMessage = null)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        Context = context;
        ErrorMessage = errorMessage ?? exception.Message;
    }
    
    public override string Name => "FullscreenOptimizationError";
    public override string Category => "Capture";
    
    public override string ToString()
    {
        return $"{Name}: {ErrorMessage} (Context: {Context})";
    }
}

/// <summary>
/// フルスクリーン検出開始イベント
/// </summary>
public class FullscreenDetectionStartedEvent : EventBase
{
    /// <summary>
    /// 検出設定
    /// </summary>
    public FullscreenDetectionSettings Settings { get; }
    
    public FullscreenDetectionStartedEvent(FullscreenDetectionSettings settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }
    
    public override string Name => "FullscreenDetectionStarted";
    public override string Category => "Capture";
    
    public override string ToString()
    {
        return $"{Name}: Detection interval {Settings.DetectionIntervalMs}ms, " +
               $"Min confidence {Settings.MinConfidence:F2}";
    }
}

/// <summary>
/// フルスクリーン検出停止イベント
/// </summary>
public class FullscreenDetectionStoppedEvent : EventBase
{
    /// <summary>
    /// 停止理由
    /// </summary>
    public string Reason { get; }
    
    /// <summary>
    /// 実行時間
    /// </summary>
    public TimeSpan? RunDuration { get; }
    
    public FullscreenDetectionStoppedEvent(string reason = "", TimeSpan? runDuration = null)
    {
        Reason = reason;
        RunDuration = runDuration;
    }
    
    public override string Name => "FullscreenDetectionStopped";
    public override string Category => "Capture";
    
    public override string ToString()
    {
        var duration = RunDuration?.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) ?? "Unknown";
        return $"{Name}: {Reason} (Duration: {duration})";
    }
}
