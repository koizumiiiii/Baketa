using System.Threading.Tasks;

namespace Baketa.Core.Events
{
    /// <summary>
    /// イベント集約インターフェース
    /// </summary>
    public interface IEventAggregator
    {
        /// <summary>
        /// イベントの発行
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="eventData">イベント</param>
        /// <returns>イベント発行の完了を表すTask</returns>
        Task PublishAsync<TEvent>(TEvent eventData) where TEvent : IEvent;
        
        /// <summary>
        /// イベントプロセッサの登録
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="processor">イベントプロセッサ</param>
        void Subscribe<TEvent>(IEventProcessor<TEvent> processor) where TEvent : IEvent;
        
        /// <summary>
        /// イベントプロセッサの登録解除
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="processor">イベントプロセッサ</param>
        void Unsubscribe<TEvent>(IEventProcessor<TEvent> processor) where TEvent : IEvent;
    }
}