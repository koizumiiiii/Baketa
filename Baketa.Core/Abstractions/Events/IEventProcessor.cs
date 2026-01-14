using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Events;

/// <summary>
/// イベント処理インターフェース
/// </summary>
/// <typeparam name="TEvent">イベント型</typeparam>
public interface IEventProcessor<in TEvent> where TEvent : IEvent
{
    /// <summary>
    /// イベント処理
    /// </summary>
    /// <param name="eventData">イベントデータ</param>
    /// <param name="cancellationToken">キャンセレーショントークン（Issue #291: 翻訳停止時のキャンセル伝播用）</param>
    Task HandleAsync(TEvent eventData, CancellationToken cancellationToken = default);

    /// <summary>
    /// このハンドラーの優先度
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 処理を同期的に実行するかどうか
    /// </summary>
    bool SynchronousExecution { get; }
}
