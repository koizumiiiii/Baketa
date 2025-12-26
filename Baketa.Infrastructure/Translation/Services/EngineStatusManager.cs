using System.Collections.Concurrent;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// Cloud AIエンジンの状態を一元管理するサービス
/// フォールバック状態の追跡と自動復帰を担当
/// </summary>
public sealed class EngineStatusManager : IEngineStatusManager
{
    private readonly ILogger<EngineStatusManager> _logger;
    private readonly ConcurrentDictionary<string, EngineStatusEntry> _engineStatuses;
    private readonly object _eventLock = new();

    // エンジンの優先度順序（低いほど優先）
    private static readonly IReadOnlyDictionary<string, int> EnginePriorities =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["primary"] = 1,    // Gemini 2.5 Flash-Lite
            ["secondary"] = 2,  // GPT-4.1-nano
            ["local"] = 3       // NLLB-200 (ローカルフォールバック)
        };

    // デフォルトの利用不可期間
    private static readonly TimeSpan DefaultUnavailableDuration = TimeSpan.FromMinutes(5);

    /// <inheritdoc/>
    public event EventHandler<EngineStatusChangedEventArgs>? EngineStatusChanged;

    /// <summary>
    /// EngineStatusManagerを初期化
    /// </summary>
    public EngineStatusManager(ILogger<EngineStatusManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _engineStatuses = new ConcurrentDictionary<string, EngineStatusEntry>(
            StringComparer.OrdinalIgnoreCase);

        // 全エンジンを初期状態（利用可能）で登録
        foreach (var (providerId, _) in EnginePriorities)
        {
            _engineStatuses[providerId] = new EngineStatusEntry
            {
                ProviderId = providerId,
                IsAvailable = true,
                LastSuccessTime = null,
                ConsecutiveFailures = 0
            };
        }

        _logger.LogInformation(
            "EngineStatusManager初期化完了: Engines=[{Engines}]",
            string.Join(", ", EnginePriorities.Keys));
    }

    /// <inheritdoc/>
    public bool IsEngineAvailable(string providerId)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);

        if (!_engineStatuses.TryGetValue(providerId, out var entry))
        {
            _logger.LogWarning("不明なエンジンID: {ProviderId}", providerId);
            return false;
        }

        // 利用不可期間が過ぎていれば自動復帰
        if (!entry.IsAvailable && entry.NextRetryTime.HasValue)
        {
            if (DateTime.UtcNow >= entry.NextRetryTime.Value)
            {
                _logger.LogInformation(
                    "エンジン自動復帰: {ProviderId} (利用不可期間終了)",
                    providerId);

                // 自動復帰
                MarkEngineAvailable(providerId);
                return true;
            }
        }

        return entry.IsAvailable;
    }

    /// <inheritdoc/>
    public void MarkEngineUnavailable(string providerId, TimeSpan duration, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);

        if (duration <= TimeSpan.Zero)
        {
            duration = DefaultUnavailableDuration;
        }

        var now = DateTime.UtcNow;
        var nextRetryTime = now.Add(duration);

        // 状態変更を追跡するための変数（AddOrUpdate外で使用）
        EngineStatus? previousStatus = null;
        EngineStatus? newStatus = null;
        var wasAvailable = false;

        _engineStatuses.AddOrUpdate(
            providerId,
            key => new EngineStatusEntry
            {
                ProviderId = key,
                IsAvailable = false,
                UnavailableSince = now,
                NextRetryTime = nextRetryTime,
                UnavailableReason = reason,
                ConsecutiveFailures = 1,
                LastFailureTime = now
            },
            (key, existing) =>
            {
                wasAvailable = existing.IsAvailable;
                if (wasAvailable)
                {
                    previousStatus = CreateStatusFromEntry(existing);
                }

                return new EngineStatusEntry
                {
                    ProviderId = key,
                    IsAvailable = false,
                    UnavailableSince = existing.UnavailableSince ?? now,
                    NextRetryTime = nextRetryTime,
                    UnavailableReason = reason ?? existing.UnavailableReason,
                    ConsecutiveFailures = existing.ConsecutiveFailures + 1,
                    LastSuccessTime = existing.LastSuccessTime,
                    LastFailureTime = now
                };
            });

        // イベント発火はAddOrUpdate完了後に行う（競合状態回避）
        if (wasAvailable && previousStatus is not null)
        {
            if (_engineStatuses.TryGetValue(providerId, out var currentEntry))
            {
                newStatus = CreateStatusFromEntry(currentEntry);
                RaiseStatusChangedEvent(previousStatus, newStatus);
            }
        }

        _logger.LogWarning(
            "エンジンを利用不可にマーク: {ProviderId}, Duration={Duration}, Reason={Reason}, NextRetry={NextRetry}",
            providerId, duration, reason ?? "不明", nextRetryTime);
    }

    /// <inheritdoc/>
    public void MarkEngineAvailable(string providerId)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);

        var now = DateTime.UtcNow;

        // 状態変更を追跡するための変数（AddOrUpdate外で使用）
        EngineStatus? previousStatus = null;
        var wasUnavailable = false;

        _engineStatuses.AddOrUpdate(
            providerId,
            key => new EngineStatusEntry
            {
                ProviderId = key,
                IsAvailable = true,
                LastSuccessTime = now,
                ConsecutiveFailures = 0
            },
            (key, existing) =>
            {
                wasUnavailable = !existing.IsAvailable;
                if (wasUnavailable)
                {
                    previousStatus = CreateStatusFromEntry(existing);
                }

                return new EngineStatusEntry
                {
                    ProviderId = key,
                    IsAvailable = true,
                    UnavailableSince = null,
                    NextRetryTime = null,
                    UnavailableReason = null,
                    ConsecutiveFailures = 0,
                    LastSuccessTime = now,
                    LastFailureTime = existing.LastFailureTime
                };
            });

        // イベント発火はAddOrUpdate完了後に行う（競合状態回避）
        if (wasUnavailable && previousStatus is not null)
        {
            if (_engineStatuses.TryGetValue(providerId, out var currentEntry))
            {
                var newStatus = CreateStatusFromEntry(currentEntry);
                RaiseStatusChangedEvent(previousStatus, newStatus);
            }
        }

        _logger.LogInformation("エンジンを利用可能にマーク: {ProviderId}", providerId);
    }

    /// <inheritdoc/>
    public DateTime? GetNextRetryTime(string providerId)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);

        if (_engineStatuses.TryGetValue(providerId, out var entry))
        {
            return entry.NextRetryTime;
        }

        return null;
    }

    /// <inheritdoc/>
    public EngineStatus GetStatus(string providerId)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);

        if (_engineStatuses.TryGetValue(providerId, out var entry))
        {
            return CreateStatusFromEntry(entry);
        }

        // 不明なエンジンの場合は利用不可として返す
        return new EngineStatus
        {
            ProviderId = providerId,
            IsAvailable = false,
            UnavailableReason = "不明なエンジンID"
        };
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, EngineStatus> GetAllStatuses()
    {
        var result = new Dictionary<string, EngineStatus>(StringComparer.OrdinalIgnoreCase);

        foreach (var (providerId, entry) in _engineStatuses)
        {
            result[providerId] = CreateStatusFromEntry(entry);
        }

        return result;
    }

    /// <inheritdoc/>
    public string? GetAvailableEngineId()
    {
        // 優先度順にソートして、利用可能な最初のエンジンを返す
        var sortedEngines = _engineStatuses
            .Where(kvp => EnginePriorities.ContainsKey(kvp.Key))
            .OrderBy(kvp => EnginePriorities[kvp.Key]);

        foreach (var (providerId, entry) in sortedEngines)
        {
            // 利用可能性をチェック（自動復帰も考慮）
            if (IsEngineAvailable(providerId))
            {
                return providerId;
            }
        }

        // すべて利用不可の場合
        _logger.LogWarning("利用可能なCloud AIエンジンがありません");
        return null;
    }

    /// <summary>
    /// エントリからステータスオブジェクトを作成
    /// </summary>
    private static EngineStatus CreateStatusFromEntry(EngineStatusEntry entry)
    {
        return new EngineStatus
        {
            ProviderId = entry.ProviderId,
            IsAvailable = entry.IsAvailable,
            UnavailableSince = entry.UnavailableSince,
            NextRetryTime = entry.NextRetryTime,
            UnavailableReason = entry.UnavailableReason,
            ConsecutiveFailures = entry.ConsecutiveFailures,
            LastSuccessTime = entry.LastSuccessTime,
            LastFailureTime = entry.LastFailureTime
        };
    }

    /// <summary>
    /// 状態変更イベントを発火
    /// </summary>
    private void RaiseStatusChangedEvent(EngineStatus previousStatus, EngineStatus newStatus)
    {
        lock (_eventLock)
        {
            try
            {
                EngineStatusChanged?.Invoke(this, new EngineStatusChangedEventArgs
                {
                    ProviderId = newStatus.ProviderId,
                    PreviousStatus = previousStatus,
                    NewStatus = newStatus,
                    ChangedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EngineStatusChangedイベントハンドラでエラー発生");
            }
        }
    }

    /// <summary>
    /// 内部エントリクラス（スレッドセーフな更新のため）
    /// </summary>
    private sealed class EngineStatusEntry
    {
        public required string ProviderId { get; init; }
        public bool IsAvailable { get; init; }
        public DateTime? UnavailableSince { get; init; }
        public DateTime? NextRetryTime { get; init; }
        public string? UnavailableReason { get; init; }
        public int ConsecutiveFailures { get; init; }
        public DateTime? LastSuccessTime { get; init; }
        public DateTime? LastFailureTime { get; init; }
    }
}
