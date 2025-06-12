using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events;

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
        /// <returns>処理の完了を表すTask</returns>
        Task HandleAsync(TEvent eventData);
    }
