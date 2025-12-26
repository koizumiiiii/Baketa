namespace Baketa.Core.Translation.Abstractions;

/// <summary>
/// Cloud AIエンジンの状態を一元管理するインターフェース
/// フォールバック状態の追跡と自動復帰を担当
/// </summary>
public interface IEngineStatusManager
{
    /// <summary>
    /// エンジンが利用可能かチェックする
    /// </summary>
    /// <param name="providerId">プロバイダーID（"primary", "secondary"等）</param>
    /// <returns>利用可能な場合true</returns>
    bool IsEngineAvailable(string providerId);

    /// <summary>
    /// エンジンを一時的に利用不可としてマークする
    /// </summary>
    /// <param name="providerId">プロバイダーID</param>
    /// <param name="duration">利用不可期間</param>
    /// <param name="reason">利用不可の理由</param>
    void MarkEngineUnavailable(string providerId, TimeSpan duration, string? reason = null);

    /// <summary>
    /// エンジンを利用可能としてマークする
    /// </summary>
    /// <param name="providerId">プロバイダーID</param>
    void MarkEngineAvailable(string providerId);

    /// <summary>
    /// 次回再試行時刻を取得する
    /// </summary>
    /// <param name="providerId">プロバイダーID</param>
    /// <returns>再試行時刻（利用可能な場合はnull）</returns>
    DateTime? GetNextRetryTime(string providerId);

    /// <summary>
    /// エンジンの詳細状態を取得する
    /// </summary>
    /// <param name="providerId">プロバイダーID</param>
    /// <returns>エンジン状態情報</returns>
    EngineStatus GetStatus(string providerId);

    /// <summary>
    /// 全エンジンの状態を取得する
    /// </summary>
    /// <returns>全エンジンの状態一覧</returns>
    IReadOnlyDictionary<string, EngineStatus> GetAllStatuses();

    /// <summary>
    /// 利用可能な最優先エンジンIDを取得する
    /// </summary>
    /// <returns>利用可能なエンジンID（すべて利用不可の場合はnull）</returns>
    string? GetAvailableEngineId();

    /// <summary>
    /// エンジン状態変更イベント
    /// </summary>
    event EventHandler<EngineStatusChangedEventArgs>? EngineStatusChanged;
}

/// <summary>
/// エンジン状態情報
/// </summary>
public sealed class EngineStatus
{
    /// <summary>
    /// プロバイダーID
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// 利用可能かどうか
    /// </summary>
    public bool IsAvailable { get; init; }

    /// <summary>
    /// 利用不可開始時刻
    /// </summary>
    public DateTime? UnavailableSince { get; init; }

    /// <summary>
    /// 次回再試行時刻
    /// </summary>
    public DateTime? NextRetryTime { get; init; }

    /// <summary>
    /// 利用不可の理由
    /// </summary>
    public string? UnavailableReason { get; init; }

    /// <summary>
    /// 連続失敗回数
    /// </summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>
    /// 最終成功時刻
    /// </summary>
    public DateTime? LastSuccessTime { get; init; }

    /// <summary>
    /// 最終失敗時刻
    /// </summary>
    public DateTime? LastFailureTime { get; init; }

    /// <summary>
    /// 利用可能な状態を作成
    /// </summary>
    public static EngineStatus CreateAvailable(string providerId) => new()
    {
        ProviderId = providerId,
        IsAvailable = true
    };

    /// <summary>
    /// 利用不可な状態を作成
    /// </summary>
    public static EngineStatus CreateUnavailable(
        string providerId,
        DateTime unavailableSince,
        DateTime nextRetryTime,
        string? reason = null,
        int consecutiveFailures = 1) => new()
    {
        ProviderId = providerId,
        IsAvailable = false,
        UnavailableSince = unavailableSince,
        NextRetryTime = nextRetryTime,
        UnavailableReason = reason,
        ConsecutiveFailures = consecutiveFailures,
        LastFailureTime = unavailableSince
    };
}

/// <summary>
/// エンジン状態変更イベント引数
/// </summary>
public sealed class EngineStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// プロバイダーID
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// 以前の状態
    /// </summary>
    public required EngineStatus PreviousStatus { get; init; }

    /// <summary>
    /// 新しい状態
    /// </summary>
    public required EngineStatus NewStatus { get; init; }

    /// <summary>
    /// 変更時刻
    /// </summary>
    public DateTime ChangedAt { get; init; } = DateTime.UtcNow;
}
