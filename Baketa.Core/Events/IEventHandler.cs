using System.Threading.Tasks;

namespace Baketa.Core.Events
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
        /// <returns>処理の完了を表すTask</returns>
        Task HandleAsync(TEvent @event);
    }
}