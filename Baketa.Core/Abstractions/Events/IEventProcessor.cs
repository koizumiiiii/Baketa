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
        Task HandleAsync(TEvent eventData);
        
        /// <summary>
        /// このハンドラーの優先度
        /// </summary>
        int Priority { get; }
        
        /// <summary>
        /// 処理を同期的に実行するかどうか
        /// </summary>
        bool SynchronousExecution { get; }
    }
