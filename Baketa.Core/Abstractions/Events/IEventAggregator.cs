using System;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Events;

/// <summary>
/// イベント集約インターフェース
/// </summary>
public interface IEventAggregator
{
    /// <summary>
    /// イベントの発行
    /// </summary>
    /// <typeparam name="TEvent">イベント型</typeparam>
    /// <param name="eventData">イベントデータ</param>
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

    /// <summary>
    /// すべてのイベントプロセッサの登録解除
    /// </summary>
    void UnsubscribeAll();

    /// <summary>
    /// 特定のイベント型に対するすべてのイベントプロセッサの登録解除
    /// </summary>
    /// <typeparam name="TEvent">イベント型</typeparam>
    void UnsubscribeAllForEvent<TEvent>() where TEvent : IEvent;

    /// <summary>
    /// オブジェクトに関連するすべてのイベントプロセッサの登録解除
    /// </summary>
    /// <param name="subscriber">購読者オブジェクト</param>
    void UnsubscribeAllForSubscriber(object subscriber);

    /// <summary>
    /// イベント処理の実行時に発生するエラーイベント
    /// </summary>
    event EventHandler<EventProcessorErrorEventArgs> EventProcessorError;
}

/// <summary>
/// イベントプロセッサのエラーイベント引数
/// </summary>
/// <remarks>
/// コンストラクター
/// </remarks>
/// <param name="exception">発生した例外</param>
/// <param name="eventData">処理しようとしていたイベント</param>
/// <param name="processor">例外を発生させたイベントプロセッサ</param>
public class EventProcessorErrorEventArgs(Exception exception, IEvent eventData, object processor) : EventArgs
{
    /// <summary>
    /// 発生した例外
    /// </summary>
    public Exception Exception { get; } = exception;

    /// <summary>
    /// 処理しようとしていたイベント
    /// </summary>
    public IEvent EventData { get; } = eventData;

    /// <summary>
    /// 例外を発生させたイベントプロセッサ
    /// </summary>
    public object Processor { get; } = processor;
}
