using System;
using System.Globalization;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Settings;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// フルスクリーン状態変更イベント
/// </summary>
public class FullscreenStateChangedEvent(FullscreenInfo fullscreenInfo, bool? previousFullscreenState = null) : EventBase
{
    /// <summary>
    /// フルスクリーン情報
    /// </summary>
    public FullscreenInfo FullscreenInfo { get; } = fullscreenInfo ?? throw new ArgumentNullException(nameof(fullscreenInfo));

    /// <summary>
    /// 前回のフルスクリーン状態
    /// </summary>
    public bool? PreviousFullscreenState { get; } = previousFullscreenState;

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
public class FullscreenOptimizationAppliedEvent(
    FullscreenInfo fullscreenInfo,
    CaptureSettings optimizedSettings,
    CaptureSettings? originalSettings = null) : EventBase
{
    /// <summary>
    /// フルスクリーン情報
    /// </summary>
    public FullscreenInfo FullscreenInfo { get; } = fullscreenInfo ?? throw new ArgumentNullException(nameof(fullscreenInfo));

    /// <summary>
    /// 最適化されたキャプチャ設定
    /// </summary>
    public CaptureSettings OptimizedSettings { get; } = optimizedSettings ?? throw new ArgumentNullException(nameof(optimizedSettings));

    /// <summary>
    /// 元のキャプチャ設定
    /// </summary>
    public CaptureSettings? OriginalSettings { get; } = originalSettings;

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
public class FullscreenOptimizationRemovedEvent(
    CaptureSettings? restoredSettings = null,
    string reason = "",
    string? windowInfo = null) : EventBase
{
    /// <summary>
    /// 復元されたキャプチャ設定
    /// </summary>
    public CaptureSettings? RestoredSettings { get; } = restoredSettings;

    /// <summary>
    /// 解除理由
    /// </summary>
    public string Reason { get; } = reason;

    /// <summary>
    /// 対象ウィンドウ情報
    /// </summary>
    public string? WindowInfo { get; } = windowInfo;

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
public class FullscreenOptimizationErrorEvent(Exception exception, string context = "", string? errorMessage = null) : EventBase
{
    /// <summary>
    /// 発生した例外
    /// </summary>
    public Exception Exception { get; } = exception ?? throw new ArgumentNullException(nameof(exception));

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string ErrorMessage { get; } = errorMessage ?? exception.Message;

    /// <summary>
    /// エラーコンテキスト
    /// </summary>
    public string Context { get; } = context;

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
public class FullscreenDetectionStartedEvent(FullscreenDetectionSettings settings) : EventBase
{
    /// <summary>
    /// 検出設定
    /// </summary>
    public FullscreenDetectionSettings Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));

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
public class FullscreenDetectionStoppedEvent(string reason = "", TimeSpan? runDuration = null) : EventBase
{
    /// <summary>
    /// 停止理由
    /// </summary>
    public string Reason { get; } = reason;

    /// <summary>
    /// 実行時間
    /// </summary>
    public TimeSpan? RunDuration { get; } = runDuration;

    public override string Name => "FullscreenDetectionStopped";
    public override string Category => "Capture";

    public override string ToString()
    {
        var duration = RunDuration?.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) ?? "Unknown";
        return $"{Name}: {Reason} (Duration: {duration})";
    }
}
