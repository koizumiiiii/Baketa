using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events.Setup;

/// <summary>
/// コンポーネントダウンロード失敗イベント
/// UIでユーザーに再起動を促す通知を表示するために使用
/// </summary>
public sealed class ComponentDownloadFailedEvent : IEvent
{
    /// <summary>
    /// 失敗したコンポーネントのID一覧
    /// </summary>
    public IReadOnlyList<string> FailedComponentIds { get; }

    /// <summary>
    /// 必須コンポーネントの失敗を含むかどうか
    /// </summary>
    public bool HasRequiredFailures { get; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// イベントID（IEvent実装）
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// イベント発生時刻（IEvent実装）
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    /// <summary>
    /// イベント名（IEvent実装）
    /// </summary>
    public string Name => nameof(ComponentDownloadFailedEvent);

    /// <summary>
    /// イベントカテゴリ（IEvent実装）
    /// </summary>
    public string Category => "Setup";

    public ComponentDownloadFailedEvent(
        IReadOnlyList<string> failedComponentIds,
        bool hasRequiredFailures,
        string errorMessage)
    {
        FailedComponentIds = failedComponentIds ?? throw new ArgumentNullException(nameof(failedComponentIds));
        HasRequiredFailures = hasRequiredFailures;
        ErrorMessage = errorMessage ?? string.Empty;
    }
}
