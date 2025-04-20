using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Events
{
    /// <summary>
    /// イベントハンドラインターフェース
    /// </summary>
    /// <typeparam name="TEvent">イベント型</typeparam>
    public interface IEventHandler<in TEvent> where TEvent : IEvent
    {
        /// <summary>
        /// イベント処理
        /// </summary>
        /// <param name="event">イベント</param>
        Task HandleAsync(TEvent @event);
        
        /// <summary>
        /// このハンドラーの優先度
        /// </summary>
        int Priority { get; }
        
        /// <summary>
        /// 処理を同期的に実行するかどうか
        /// </summary>
        bool SynchronousExecution { get; }
    }
}