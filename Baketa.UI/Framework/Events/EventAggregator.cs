using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Framework.Events
{
    /// <summary>
    /// イベント集約器の実装
    /// </summary>
    internal sealed class EventAggregator(ILogger<EventAggregator>? logger = null) : IEventAggregator
    {
        private readonly ILogger<EventAggregator>? _logger = logger;
        private readonly ConcurrentDictionary<Type, List<object>> _handlers = new();
        
        // LoggerMessage デリゲートを定義
        private static readonly Action<ILogger, string, Exception?> _logEventPublished =
            LoggerMessage.Define<string>(
                LogLevel.Trace,
                new EventId(1, "EventPublished"),
                "イベント発行: {EventType}");
                
        private static readonly Action<ILogger, string, Exception?> _logNoHandlersFound =
            LoggerMessage.Define<string>(
                LogLevel.Trace,
                new EventId(2, "NoHandlersFound"),
                "イベント {EventType} に対するハンドラがありません");
                
        private static readonly Action<ILogger, string, Exception?> _logInvalidHandler =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(3, "InvalidHandler"),
                "イベント {EventType} に対する無効なハンドラがあります");
                
        private static readonly Action<ILogger, string, Exception> _logHandlerExecutionError =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(4, "HandlerExecutionError"),
                "イベント {EventType} のハンドラ実行中にエラーが発生しました");
                
        private static readonly Action<ILogger, string, Exception?> _logSubscriptionAdded =
            LoggerMessage.Define<string>(
                LogLevel.Trace,
                new EventId(5, "SubscriptionAdded"),
                "イベント {EventType} への購読を追加します");
                
        private static readonly Action<ILogger, string, Exception?> _logSubscriptionRemoved =
            LoggerMessage.Define<string>(
                LogLevel.Trace,
                new EventId(6, "SubscriptionRemoved"),
                "イベント {EventType} の購読を解除します");
        
        /// <inheritdoc/>
        public async Task PublishAsync<TEvent>(TEvent eventData) where TEvent : Baketa.Core.Events.IEvent
        {
            var eventType = typeof(TEvent);
            if (_logger != null)
                _logEventPublished(_logger, eventType.Name, null);
            
            if (!_handlers.TryGetValue(eventType, out var handlers))
            {
                if (_logger != null)
                    _logNoHandlersFound(_logger, eventType.Name, null);
                return;
            }
            
            var tasks = new List<Task>(handlers.Count);
            
            foreach (var handler in handlers.ToList())
            {
                if (handler is not Func<TEvent, Task> typedHandler)
                {
                    if (_logger != null)
                        _logInvalidHandler(_logger, eventType.Name, null);
                    continue;
                }
                
                try
                {
                    tasks.Add(typedHandler(eventData));
                }
                catch (ArgumentException ex)
                {
                    if (_logger != null)
                        _logHandlerExecutionError(_logger, eventType.Name, ex);
                }
                catch (ObjectDisposedException ex)
                {
                    if (_logger != null)
                        _logHandlerExecutionError(_logger, eventType.Name, ex);
                }
                catch (OperationCanceledException ex)
                {
                    if (_logger != null)
                        _logHandlerExecutionError(_logger, eventType.Name, ex);
                }
            }
            
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        
        /// <inheritdoc/>
        public IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : Baketa.Core.Events.IEvent
        {
            var eventType = typeof(TEvent);
            if (_logger != null)
                _logSubscriptionAdded(_logger, eventType.Name, null);
            
            var handlers = _handlers.GetOrAdd(eventType, static _ => new List<object>(5));
            lock (handlers)
            {
                handlers.Add(handler);
            }
            
            return new SubscriptionToken(() => Unsubscribe(eventType, handler));
        }
        
        private void Unsubscribe(Type eventType, object handler)
        {
            if (_logger != null)
                _logSubscriptionRemoved(_logger, eventType.Name, null);
            
            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                lock (handlers)
                {
                    handlers.Remove(handler);
                    
                    if (handlers.Count == 0)
                    {
                        _handlers.TryRemove(eventType, out _);
                    }
                }
            }
        }
        
        private sealed class SubscriptionToken(Action unsubscribeAction) : IDisposable
        {
            private readonly Action _unsubscribeAction = unsubscribeAction;
            private bool _disposed;
            
            public void Dispose()
            {
                if (_disposed)
                    return;
                
                _unsubscribeAction();
                _disposed = true;
            }
        }
    }
}