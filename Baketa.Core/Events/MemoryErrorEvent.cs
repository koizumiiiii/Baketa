using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events;

/// <summary>
/// メモリ不足エラーイベント
/// Issue #239: OutOfMemoryException発生時にUI通知をトリガー
/// </summary>
public sealed class MemoryErrorEvent : IEvent
{
    /// <inheritdoc />
    public Guid Id { get; }

    /// <inheritdoc />
    public DateTime Timestamp { get; }

    /// <inheritdoc />
    public string Name => "MemoryError";

    /// <inheritdoc />
    public string Category => "System";

    /// <summary>
    /// エラーが発生した操作名
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 現在のメモリ使用量（バイト）
    /// </summary>
    public long CurrentMemoryBytes { get; }

    /// <summary>
    /// 復旧が成功したか
    /// </summary>
    public bool RecoverySucceeded { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public MemoryErrorEvent(
        string operation,
        string message,
        long currentMemoryBytes,
        bool recoverySucceeded)
    {
        Id = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
        Operation = operation ?? "Unknown";
        Message = message ?? "Out of memory";
        CurrentMemoryBytes = currentMemoryBytes;
        RecoverySucceeded = recoverySucceeded;
    }

    /// <summary>
    /// メモリ使用量をMB単位で取得
    /// </summary>
    public double CurrentMemoryMB => CurrentMemoryBytes / (1024.0 * 1024.0);
}
