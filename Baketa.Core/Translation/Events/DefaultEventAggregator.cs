using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Translation.Events;

    /// <summary>
    /// デフォルトイベント集約器
    /// </summary>
    public class DefaultEventAggregator : IEventAggregator
    {
        private readonly ILogger<DefaultEventAggregator> _logger;
        private readonly ConcurrentDictionary<Type, List<object>> _handlers = [];

        /// <summary>
        /// イベント処理の実行時に発生するエラーイベント
        /// </summary>
        public event EventHandler<EventProcessorErrorEventArgs>? EventProcessorError;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="logger">ロガー</param>
        public DefaultEventAggregator(ILogger<DefaultEventAggregator> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// イベントを発行します
        /// </summary>
        /// <typeparam name="TEvent">イベントの型</typeparam>
        /// <param name="eventData">発行するイベント</param>
        /// <returns>完了タスク</returns>
        public async Task PublishAsync<TEvent>(TEvent eventData) where TEvent : IEvent
        {
            ArgumentNullException.ThrowIfNull(eventData);
            
            var eventType = typeof(TEvent);
            _logger.LogDebug("イベント {EventType} を発行します", eventType.Name);
            
            if (!_handlers.TryGetValue(eventType, out var handlers))
            {
                _logger.LogDebug("イベント {EventType} に対するハンドラーはありません", eventType.Name);
                return;
            }
            
            var handlerTasks = new List<Task>();
            
            foreach (var handler in handlers)
            {
                if (handler is ITranslationEventHandler<TEvent> typedHandler)
                {
                    try
                    {
                        handlerTasks.Add(typedHandler.HandleAsync(eventData));
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogError(ex, "イベント {EventType} のハンドリング中に操作エラーが発生しました", eventType.Name);
                        EventProcessorError?.Invoke(this, new EventProcessorErrorEventArgs(ex, eventData, typedHandler));
                    }
                    catch (ArgumentException ex)
                    {
                        _logger.LogError(ex, "イベント {EventType} のハンドリング中に引数エラーが発生しました", eventType.Name);
                        EventProcessorError?.Invoke(this, new EventProcessorErrorEventArgs(ex, eventData, typedHandler));
                    }
                    catch (TimeoutException ex)
                    {
                        _logger.LogError(ex, "イベント {EventType} のハンドリング中にタイムアウトが発生しました", eventType.Name);
                        EventProcessorError?.Invoke(this, new EventProcessorErrorEventArgs(ex, eventData, typedHandler));
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
                    {
                        _logger.LogError(ex, "イベント {EventType} のハンドリング中に予期しないエラーが発生しました", eventType.Name);
                        EventProcessorError?.Invoke(this, new EventProcessorErrorEventArgs(ex, eventData, typedHandler));
                    }
                }
            }
            
            await Task.WhenAll(handlerTasks).ConfigureAwait(false);
            _logger.LogDebug("イベント {EventType} の発行が完了しました ({Count} ハンドラー)", eventType.Name, handlerTasks.Count);
        }

        /// <summary>
        /// イベントハンドラーを登録します
        /// </summary>
        /// <typeparam name="TEvent">イベントの型</typeparam>
        /// <param name="handler">イベントハンドラー</param>
        public void Subscribe<TEvent>(ITranslationEventHandler<TEvent> handler) where TEvent : IEvent
        {
            ArgumentNullException.ThrowIfNull(handler);
            
            var eventType = typeof(TEvent);
            _handlers.AddOrUpdate(
                eventType,
                _ => [handler],
                (_, list) =>
                {
                    lock (list)
                    {
                        if (!list.Contains(handler))
                        {
                            list.Add(handler);
                        }
                        return list;
                    }
                });
            
            _logger.LogDebug("イベント {EventType} に対するハンドラー {HandlerType} を登録しました",
                eventType.Name, handler.GetType().Name);
        }

        /// <summary>
        /// イベントハンドラーの登録を解除します
        /// </summary>
        /// <typeparam name="TEvent">イベントの型</typeparam>
        /// <param name="handler">イベントハンドラー</param>
        public void Unsubscribe<TEvent>(ITranslationEventHandler<TEvent> handler) where TEvent : IEvent
        {
            ArgumentNullException.ThrowIfNull(handler);
            
            var eventType = typeof(TEvent);
            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                lock (handlers)
                {
                    handlers.Remove(handler);
                }
                
                _logger.LogDebug("イベント {EventType} に対するハンドラー {HandlerType} の登録を解除しました",
                    eventType.Name, handler.GetType().Name);
            }
        }

        /// <summary>
        /// イベントプロセッサの登録
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="processor">イベントプロセッサ</param>
        public void Subscribe<TEvent>(IEventProcessor<TEvent> processor) where TEvent : IEvent
        {
            ArgumentNullException.ThrowIfNull(processor);
            
            var eventType = typeof(TEvent);
            _handlers.AddOrUpdate(
                eventType,
                _ => [processor],
                (_, list) =>
                {
                    lock (list)
                    {
                        if (!list.Contains(processor))
                        {
                            list.Add(processor);
                        }
                        return list;
                    }
                });
            
            _logger.LogDebug("イベント {EventType} に対するプロセッサ {ProcessorType} を登録しました",
                eventType.Name, processor.GetType().Name);
        }

        /// <summary>
        /// イベントプロセッサの登録解除
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="processor">イベントプロセッサ</param>
        public void Unsubscribe<TEvent>(IEventProcessor<TEvent> processor) where TEvent : IEvent
        {
            ArgumentNullException.ThrowIfNull(processor);
            
            var eventType = typeof(TEvent);
            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                lock (handlers)
                {
                    handlers.Remove(processor);
                }
                
                _logger.LogDebug("イベント {EventType} に対するプロセッサ {ProcessorType} の登録を解除しました",
                    eventType.Name, processor.GetType().Name);
            }
        }

        /// <summary>
        /// すべてのイベントプロセッサの登録解除
        /// </summary>
        public void UnsubscribeAll()
        {
            _handlers.Clear();
            _logger.LogDebug("すべてのイベントプロセッサの登録を解除しました");
        }

        /// <summary>
        /// 特定のイベント型に対するすべてのイベントプロセッサの登録解除
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        public void UnsubscribeAllForEvent<TEvent>() where TEvent : IEvent
        {
            var eventType = typeof(TEvent);
            _handlers.TryRemove(eventType, out _);
            _logger.LogDebug("イベント {EventType} に対するすべてのプロセッサの登録を解除しました", eventType.Name);
        }

        /// <summary>
        /// オブジェクトに関連するすべてのイベントプロセッサの登録解除
        /// </summary>
        /// <param name="subscriber">購読者オブジェクト</param>
        public void UnsubscribeAllForSubscriber(object subscriber)
        {
            ArgumentNullException.ThrowIfNull(subscriber);
            
            foreach (var (eventType, handlers) in _handlers)
            {
                bool removed = false;
                lock (handlers)
                {
                    // オブジェクトを含むインスタンスを削除
                    var toRemove = handlers.Where(h => ReferenceEquals(h, subscriber) || 
                                                   (h is IEventProcessor<IEvent> processor && ReferenceEquals(processor, subscriber)))
                                         .ToList();
                    
                    foreach (var handler in toRemove)
                    {
                        handlers.Remove(handler);
                        removed = true;
                    }
                }
                
                if (removed)
                {
                    _logger.LogDebug("イベント {EventType} に対する購読者 {SubscriberType} の登録を解除しました",
                        eventType.Name, subscriber.GetType().Name);
                }
            }
        }
    }
