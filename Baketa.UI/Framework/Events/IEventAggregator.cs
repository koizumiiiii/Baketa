using System;
using System.Threading.Tasks;

namespace Baketa.UI.Framework.Events
{
    /// <summary>
    /// イベント集約器インターフェース
    /// </summary>
    internal interface IEventAggregator
    {
        /// <summary>
        /// イベントを発行します
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="eventData">イベントインスタンス</param>
        /// <returns>発行タスク</returns>
        Task PublishAsync<TEvent>(TEvent eventData) where TEvent : Baketa.Core.Events.IEvent;
        
        /// <summary>
        /// イベントをサブスクライブします
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="handler">ハンドラ</param>
        /// <returns>購読解除可能なDisposable</returns>
        IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : Baketa.Core.Events.IEvent;
    }
}