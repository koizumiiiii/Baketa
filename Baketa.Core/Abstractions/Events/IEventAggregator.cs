using System;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Events
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
        /// <param name="event">イベント</param>
        Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent;
        
        /// <summary>
        /// イベントハンドラの登録
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="handler">ハンドラ</param>
        void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
        
        /// <summary>
        /// イベントハンドラの登録解除
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="handler">ハンドラ</param>
        void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;
        
        /// <summary>
        /// すべてのハンドラの登録解除
        /// </summary>
        void UnsubscribeAll();
        
        /// <summary>
        /// 特定のイベント型に対するすべてのハンドラの登録解除
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        void UnsubscribeAllForEvent<TEvent>() where TEvent : IEvent;
        
        /// <summary>
        /// オブジェクトに関連するすべてのハンドラの登録解除
        /// </summary>
        /// <param name="subscriber">購読者オブジェクト</param>
        void UnsubscribeAllForSubscriber(object subscriber);
        
        /// <summary>
        /// イベントハンドラーの実行時に発生するエラーイベント
        /// </summary>
        event EventHandler<EventHandlerErrorEventArgs> EventHandlerError;
    }
    
    /// <summary>
    /// イベントハンドラーエラーイベント引数
    /// </summary>
    public class EventHandlerErrorEventArgs : EventArgs
    {
        /// <summary>
        /// 発生した例外
        /// </summary>
        public Exception Exception { get; }
        
        /// <summary>
        /// 処理しようとしていたイベント
        /// </summary>
        public IEvent Event { get; }
        
        /// <summary>
        /// 例外を発生させたハンドラー
        /// </summary>
        public object Handler { get; }
        
        /// <summary>
        /// コンストラクター
        /// </summary>
        /// <param name="exception">発生した例外</param>
        /// <param name="event">処理しようとしていたイベント</param>
        /// <param name="handler">例外を発生させたハンドラー</param>
        public EventHandlerErrorEventArgs(Exception exception, IEvent @event, object handler)
        {
            Exception = exception;
            Event = @event;
            Handler = handler;
        }
    }
}