using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events.Implementation;

/// <summary>
/// イベント集約機構の実装
/// </summary>
/// <remarks>
/// イベント集約機構を初期化します
/// </remarks>
/// <param name="logger">ロガー（オプション）</param>
// プライマリコンストラクターの使用を拒否（IDE0290）
public class EventAggregator(ILogger<EventAggregator>? logger = null) : Baketa.Core.Abstractions.Events.IEventAggregator
    {
        private readonly ILogger<EventAggregator>? _logger = logger;
        // Dictionary<Type, List<object>> そのままの実装を使用（IDE0028/IDE0090を拒否）
        private readonly Dictionary<Type, List<object>> _processors = [];
        private readonly object _syncRoot = new();

    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(TEvent eventData) where TEvent : IEvent
        {
            ArgumentNullException.ThrowIfNull(eventData);
            
            _logger?.LogDebug("イベント発行: {EventType} (ID: {EventId})", typeof(TEvent).Name, eventData.Id);
            
            var eventType = typeof(TEvent);
            List<object>? eventProcessors = null;
            
            lock (_syncRoot)
            {
                if (_processors.TryGetValue(eventType, out var handlers))
                {
                    eventProcessors = [.. handlers]; // スレッドセーフにするため複製
                }
            }
            
            if (eventProcessors == null || eventProcessors.Count == 0)
            {
                _logger?.LogDebug("イベント {EventType} のプロセッサが登録されていません", eventType.Name);
                return;
            }
            
            // List<Task> そのままの実装を使用（IDE0305を拒否）
            // List<Task> そのままの実装を使用（IDE0305を拒否）
            var tasks = new List<Task>();
            
            foreach (var processor in eventProcessors.OfType<IEventProcessor<TEvent>>())
            {
                try
                {
                    var processorType = processor.GetType().Name;
                    _logger?.LogTrace("プロセッサ {ProcessorType} でイベント {EventType} の処理を開始", 
                        processorType, eventType.Name);
                    
                    tasks.Add(ExecuteProcessorAsync(processor, eventData, processorType));
                }
                catch (InvalidOperationException ex)
                {
                    _logger?.LogError(ex, "プロセッサ {ProcessorType} で操作エラーが発生しました", 
                        processor.GetType().Name);
                }
                catch (ArgumentException ex)
                {
                    _logger?.LogError(ex, "プロセッサ {ProcessorType} で引数エラーが発生しました", 
                        processor.GetType().Name);
                }
                // イベント処理は継続する必要があるため、一般的な例外を意図的にキャッチします
                // CA1031: 致命的でない例外はイベント処理の継続のために適切に処理されます
#pragma warning disable CA1031
                catch (Exception ex) when (ShouldCatchException(ex, processor.GetType().Name, eventData, processor))
                {
                // ロギングとイベント発行はShouldCatchExceptionメソッド内で行われます
                }
#pragma warning restore CA1031
            }
            
            await Task.WhenAll(tasks).ConfigureAwait(false);
            _logger?.LogDebug("イベント {EventType} の処理が完了しました (プロセッサ数: {ProcessorCount})", 
                eventType.Name, eventProcessors.Count);
        }
        
        /// <summary>
        /// キャンセレーション対応のイベント発行
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="eventData">イベント</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>イベント発行の完了を表すTask</returns>
        public async Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken) 
            where TEvent : IEvent
        {
            ArgumentNullException.ThrowIfNull(eventData);
            
            _logger?.LogDebug("イベント発行(キャンセル可能): {EventType} (ID: {EventId})", 
                typeof(TEvent).Name, eventData.Id);
            
            cancellationToken.ThrowIfCancellationRequested();
            
            var eventType = typeof(TEvent);
            List<object>? eventProcessors = null;
            
            lock (_syncRoot)
            {
                if (_processors.TryGetValue(eventType, out var handlers))
                {
                    eventProcessors = [.. handlers]; // スレッドセーフにするため複製
                }
            }
            
            if (eventProcessors == null || eventProcessors.Count == 0)
            {
                _logger?.LogDebug("イベント {EventType} のプロセッサが登録されていません", eventType.Name);
                return;
            }
            
            var tasks = new List<Task>();
            
            foreach (var processor in eventProcessors.OfType<IEventProcessor<TEvent>>())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogInformation("イベント {EventType} の処理がキャンセルされました", eventType.Name);
                    break;
                }
                
                try
                {
                    var processorType = processor.GetType().Name;
                    _logger?.LogTrace("プロセッサ {ProcessorType} でイベント {EventType} の処理を開始", 
                        processorType, eventType.Name);
                    
                    tasks.Add(ExecuteProcessorAsync(processor, eventData, processorType, cancellationToken));
                }
                catch (InvalidOperationException ex)
                {
                    _logger?.LogError(ex, "プロセッサ {ProcessorType} で操作エラーが発生しました", 
                        processor.GetType().Name);
                }
                catch (ArgumentException ex)
                {
                    _logger?.LogError(ex, "プロセッサ {ProcessorType} で引数エラーが発生しました", 
                        processor.GetType().Name);
                }
                // イベント処理は継続する必要があるため、一般的な例外を意図的にキャッチします
                // CA1031: 致命的でない例外はイベント処理の継続のために適切に処理されます
#pragma warning disable CA1031
                catch (Exception ex) when (ShouldCatchException(ex, processor.GetType().Name, eventData, processor) && 
                ex is not OperationCanceledException)
                {
                // ロギングとイベント発行はShouldCatchExceptionメソッド内で行われます
                }
#pragma warning restore CA1031
            }
            
            await Task.WhenAll(tasks).ConfigureAwait(false);
            
            if (!cancellationToken.IsCancellationRequested)
            {
                _logger?.LogDebug("イベント {EventType} の処理が完了しました (プロセッサ数: {ProcessorCount})", 
                    eventType.Name, eventProcessors.Count);
            }
        }
        
        /// <inheritdoc />
        public void Subscribe<TEvent>(IEventProcessor<TEvent> processor) where TEvent : IEvent
        {
            ArgumentNullException.ThrowIfNull(processor);
            
            var eventType = typeof(TEvent);
            
            lock (_syncRoot)
            {
                if (!_processors.TryGetValue(eventType, out var handlers))
                {
                    // List<object> そのままの実装を使用（IDE0028を拒否）
                    handlers = [];
                    _processors[eventType] = handlers;
                }
                
                if (!handlers.Contains(processor))
                {
                    handlers.Add(processor);
                    _logger?.LogDebug("プロセッサ {ProcessorType} をイベント {EventType} に登録しました", 
                        processor.GetType().Name, eventType.Name);
                }
                else
                {
                    _logger?.LogDebug("プロセッサ {ProcessorType} は既にイベント {EventType} に登録されています", 
                        processor.GetType().Name, eventType.Name);
                }
            }
        }
        
        /// <inheritdoc />
        public void Unsubscribe<TEvent>(IEventProcessor<TEvent> processor) where TEvent : IEvent
        {
            ArgumentNullException.ThrowIfNull(processor);
            
            var eventType = typeof(TEvent);
            
            lock (_syncRoot)
            {
                if (_processors.TryGetValue(eventType, out var handlers))
                {
                    if (handlers.Remove(processor))
                    {
                        _logger?.LogDebug("プロセッサ {ProcessorType} のイベント {EventType} 登録を解除しました", 
                            processor.GetType().Name, eventType.Name);
                    }
                    else
                    {
                        _logger?.LogDebug("プロセッサ {ProcessorType} はイベント {EventType} に登録されていませんでした", 
                            processor.GetType().Name, eventType.Name);
                    }
                }
            }
        }
        
        /// <inheritdoc />
        public void UnsubscribeAll()
        {
            lock (_syncRoot)
            {
                _processors.Clear();
                _logger?.LogDebug("すべてのイベントプロセッサの登録を解除しました");
            }
        }
        
        /// <inheritdoc />
        public void UnsubscribeAllForEvent<TEvent>() where TEvent : IEvent
        {
            var eventType = typeof(TEvent);
            lock (_syncRoot)
            {
                if (_processors.Remove(eventType))
                {
                    _logger?.LogDebug("イベント {EventType} のすべてのプロセッサ登録を解除しました", eventType.Name);
                }
            }
        }
        
        /// <inheritdoc />
        public void UnsubscribeAllForSubscriber(object subscriber)
        {
            ArgumentNullException.ThrowIfNull(subscriber);
            
            lock (_syncRoot)
            {
                foreach (var kvp in _processors.ToList())
                {
                    var eventType = kvp.Key;
                    var handlers = kvp.Value;
                    
                    var toRemove = handlers.Where(h => ReferenceEquals(h, subscriber)).ToList();
                    foreach (var handler in toRemove)
                    {
                        handlers.Remove(handler);
                    }
                    
                    if (toRemove.Count > 0)
                    {
                        _logger?.LogDebug("購読者 {SubscriberType} のイベント {EventType} 登録を {Count} 件解除しました",
                            subscriber.GetType().Name, eventType.Name, toRemove.Count);
                    }
                }
            }
        }
        
        /// <inheritdoc />
        public event EventHandler<EventProcessorErrorEventArgs>? EventProcessorError;
        
        /// <summary>
        /// 登録されたプロセッサを実行し、エラーハンドリングを行う
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="processor">イベントプロセッサ</param>
        /// <param name="eventData">イベント</param>
        /// <param name="processorType">プロセッサタイプ名（ログ用）</param>
        /// <returns>処理の完了を表すTask</returns>
        private async Task ExecuteProcessorAsync<TEvent>(
            IEventProcessor<TEvent> processor, 
            TEvent eventData, 
            string processorType) 
            where TEvent : IEvent
        {
            try
            {
                // パフォーマンス測定のための開始時間記録
                var startTime = DateTime.UtcNow;
                
                // プロセッサの実行
                await processor.HandleAsync(eventData).ConfigureAwait(false);
                
                // 処理時間の計算と記録
                var processingTime = DateTime.UtcNow - startTime;
                _logger?.LogTrace("プロセッサ {ProcessorType} がイベント {EventType} を処理しました (処理時間: {ProcessingTime}ms)",
                    processorType, typeof(TEvent).Name, processingTime.TotalMilliseconds);
                
                // 処理時間が長い場合は警告を出力
                if (processingTime.TotalMilliseconds > 100)
                {
                    _logger?.LogWarning("プロセッサ {ProcessorType} のイベント処理に {ProcessingTime}ms かかりました",
                        processorType, processingTime.TotalMilliseconds);
                }
            }
            catch (TaskCanceledException ex)
            {
                _logger?.LogInformation(ex, "プロセッサ {ProcessorType} のタスクがキャンセルされました", processorType);
            }
            catch (OperationCanceledException ex)
            {
                _logger?.LogInformation(ex, "プロセッサ {ProcessorType} の処理がキャンセルされました", processorType);
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "プロセッサ {ProcessorType} でイベント {EventType} の処理中に操作エラーが発生しました",
                    processorType, typeof(TEvent).Name);
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "プロセッサ {ProcessorType} でイベント {EventType} の処理中に引数エラーが発生しました",
                    processorType, typeof(TEvent).Name);
            }
            // イベント処理は継続する必要があるため、一般的な例外を意図的にキャッチします
            // CA1031: 致命的でない例外はイベント処理の継続のために適切に処理されます
#pragma warning disable CA1031
            catch (Exception ex) when (ShouldCatchException(ex, processorType, eventData, processor))
            {
                // ロギングとイベント発行はShouldCatchExceptionメソッド内で行われます
            }
#pragma warning restore CA1031
        }
        
        /// <summary>
        /// キャンセル可能なプロセッサ実行
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="processor">イベントプロセッサ</param>
        /// <param name="eventData">イベント</param>
        /// <param name="processorType">プロセッサタイプ名（ログ用）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>処理の完了を表すTask</returns>
        private async Task ExecuteProcessorAsync<TEvent>(
            IEventProcessor<TEvent> processor, 
            TEvent eventData, 
            string processorType,
            CancellationToken cancellationToken) 
            where TEvent : IEvent
        {
            try
            {
                // キャンセルされている場合は早期リターン
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogDebug("プロセッサ {ProcessorType} の処理はキャンセルされました", processorType);
                    return;
                }
                
                // パフォーマンス測定のための開始時間記録
                var startTime = DateTime.UtcNow;
                
                // 実際にはプロセッサに渡すトークンを作成するべきだが、
                // IEventProcessor<TEvent>インターフェースには対応するオーバーロードがないため、
                // 内部的にキャンセル状態をチェックする
                
                // プロセッサの実行
                await processor.HandleAsync(eventData).ConfigureAwait(false);
                
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                
                // 処理時間の計算と記録
                var processingTime = DateTime.UtcNow - startTime;
                _logger?.LogTrace("プロセッサ {ProcessorType} がイベント {EventType} を処理しました (処理時間: {ProcessingTime}ms)",
                    processorType, typeof(TEvent).Name, processingTime.TotalMilliseconds);
                
                // 処理時間が長い場合は警告を出力
                if (processingTime.TotalMilliseconds > 100)
                {
                    _logger?.LogWarning("プロセッサ {ProcessorType} のイベント処理に {ProcessingTime}ms かかりました",
                        processorType, processingTime.TotalMilliseconds);
                }
            }
            catch (TaskCanceledException ex)
            {
                _logger?.LogInformation(ex, "プロセッサ {ProcessorType} のタスクがキャンセルされました", processorType);
            }
            catch (OperationCanceledException ex)
            {
                _logger?.LogInformation(ex, "プロセッサ {ProcessorType} の処理がキャンセルされました", processorType);
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "プロセッサ {ProcessorType} でイベント {EventType} の処理中に操作エラーが発生しました",
                    processorType, typeof(TEvent).Name);
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "プロセッサ {ProcessorType} でイベント {EventType} の処理中に引数エラーが発生しました",
                    processorType, typeof(TEvent).Name);
            }
            // イベント処理は継続する必要があるため、一般的な例外を意図的にキャッチします
            // CA1031: 致命的でない例外はイベント処理の継続のために適切に処理されます
#pragma warning disable CA1031
            catch (Exception ex) when (ShouldCatchException(ex, processorType, eventData, processor))
            {
                // ロギングとイベント発行はShouldCatchExceptionメソッド内で行われます
            }
#pragma warning restore CA1031
        }
        /// <summary>
        /// 例外を捕捉すべきかを判断し、ログ出力とイベント発行も行います
        /// </summary>
        /// <param name="exception">発生した例外</param>
        /// <param name="processorType">プロセッサタイプ名</param>
        /// <param name="eventData">処理中のイベントデータ</param>
        /// <param name="processor">プロセッサインスタンス</param>
        /// <returns>例外を捕捉すべき場合はtrue</returns>
        private bool ShouldCatchException(Exception exception, string processorType, IEvent eventData, object processor)
        {
            // 既知の例外型をチェック
            if (exception is OutOfMemoryException or StackOverflowException or ThreadAbortException)
            {
                // これらの致命的な例外は再スローする（捕捉しない）
                return false;
            }
            
            // ログ記録
            _logger?.LogError(exception, "プロセッサ {ProcessorType} で予期しない例外が発生しました: {ExceptionType}", 
                processorType, exception.GetType().Name);
            
            // EventProcessorErrorイベントの発行
            try
            {
                var errorArgs = new EventProcessorErrorEventArgs(exception, eventData, processor);
                EventProcessorError?.Invoke(this, errorArgs);
            }
            // イベント発行中の例外はログのみで処理し、イベント処理を継続します
            // CA1031: イベント発行中の例外はメインのイベント処理を阻害しないように適切に処理されます
#pragma warning disable CA1031
            catch (Exception eventEx)
            {
                // イベント発行中の例外はログのみで処理
                _logger?.LogError(eventEx, "イベントプロセッサエラーイベントの発行中に例外が発生しました");
            }
#pragma warning restore CA1031
            
            return true;
        }
    }
