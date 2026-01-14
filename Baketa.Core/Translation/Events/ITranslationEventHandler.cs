using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Translation.Events;

/// <summary>
/// 翻訳イベントハンドラーインターフェース
/// </summary>
/// <typeparam name="TEvent">イベントの型</typeparam>
public interface ITranslationEventHandler<in TEvent> where TEvent : IEvent
{
    /// <summary>
    /// イベントを処理します
    /// </summary>
    /// <param name="eventData">イベント</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>完了タスク</returns>
    Task HandleAsync(TEvent eventData, CancellationToken cancellationToken = default);
}

/// <summary>
/// イベントハンドラーをイベントプロセッサーに変換するアダプター
/// </summary>
/// <typeparam name="TEvent">イベントの型</typeparam>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="handler">イベントハンドラー</param>
public class EventHandlerAdapter<TEvent>(ITranslationEventHandler<TEvent> handler) : IEventProcessor<TEvent> where TEvent : IEvent
{
    private readonly ITranslationEventHandler<TEvent> _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    /// <summary>
    /// 優先度
    /// </summary>
    public int Priority => 0;

    /// <summary>
    /// 同期実行
    /// </summary>
    public bool SynchronousExecution => false;

    /// <summary>
    /// イベント処理
    /// </summary>
    /// <param name="eventData">イベントデータ</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>完了タスク</returns>
    public Task HandleAsync(TEvent eventData, CancellationToken cancellationToken = default)
    {
        return _handler.HandleAsync(eventData, cancellationToken);
    }
}
