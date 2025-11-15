using System;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Settings;

namespace Baketa.Core.Events.CaptureEvents;

/// <summary>
/// 拡張キャプチャサービスのステータス変更イベント
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="previousStatus">変更前のステータス</param>
/// <param name="currentStatus">変更後のステータス</param>
/// <param name="reason">ステータス変更の理由</param>
/// <param name="currentSettings">現在のキャプチャ設定</param>
public class AdvancedCaptureServiceStatusChangedEvent(
    CaptureServiceStatus previousStatus,
    CaptureServiceStatus currentStatus,
    string? reason = null,
    CaptureSettings? currentSettings = null) : EventBase
{
    /// <summary>
    /// 変更前のステータス
    /// </summary>
    public CaptureServiceStatus PreviousStatus { get; } = previousStatus;

    /// <summary>
    /// 変更後のステータス
    /// </summary>
    public CaptureServiceStatus CurrentStatus { get; } = currentStatus;

    /// <summary>
    /// ステータス変更の理由（オプション）
    /// </summary>
    public string? Reason { get; } = reason;

    /// <summary>
    /// ステータス変更が発生した時刻
    /// </summary>
    public DateTime StatusChangedAt { get; } = DateTime.Now;

    /// <summary>
    /// 現在のキャプチャ設定
    /// </summary>
    public CaptureSettings? CurrentSettings { get; } = currentSettings;

    /// <inheritdoc />
    public override string Name => "AdvancedCaptureServiceStatusChanged";

    /// <inheritdoc />
    public override string Category => "Capture";

    /// <summary>
    /// ステータス変更がエラー関連かどうかを判定します
    /// </summary>
    public bool IsErrorStatus => CurrentStatus == CaptureServiceStatus.Error;

    /// <summary>
    /// キャプチャが実行中かどうかを判定します
    /// </summary>
    public bool IsRunning => CurrentStatus == CaptureServiceStatus.Running;

    /// <summary>
    /// キャプチャが停止中かどうかを判定します
    /// </summary>
    public bool IsStopped => CurrentStatus == CaptureServiceStatus.Stopped;

    /// <summary>
    /// キャプチャが一時停止中かどうかを判定します
    /// </summary>
    public bool IsPaused => CurrentStatus == CaptureServiceStatus.Paused;
}
