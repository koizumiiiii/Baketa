# 実装: イベント集約管理機能の実装

## 概要
イベント集約機構の中核となる`EventAggregator`クラスと、イベントハンドラーの登録・管理機能を実装します。

## 目的・理由
イベントの発行と購読を効率的に管理するための集約機構を実装することで、アプリケーション全体でのイベントベースのコミュニケーションを実現します。また、依存性注入と連携させることで、コンポーネント間の疎結合を促進します。

## 詳細
- `EventAggregator`クラスの実装
- イベントハンドラーの登録・解除メカニズムの実装
- 非同期イベント配信とエラーハンドリングの実装
- 依存性注入との統合

## タスク分解
- [ ] `EventAggregator`クラスのコア機能実装
  - [ ] イベントハンドラー格納コレクションの設計
  - [ ] イベント発行メソッド`PublishAsync<TEvent>`の実装
  - [ ] ハンドラー登録メソッド`Subscribe<TEvent>`の実装
  - [ ] ハンドラー登録解除メソッド`Unsubscribe<TEvent>`の実装
- [ ] ハンドラー呼び出しロジックの実装
  - [ ] 並列実行のサポート
  - [ ] キャンセレーション対応
  - [ ] ハンドラーでの例外処理
- [ ] パフォーマンス最適化
  - [ ] キャッシュの活用
  - [ ] 並列処理の最適化
  - [ ] メモリ使用量の最適化
- [ ] 依存性注入での登録機能
  - [ ] DIコンテナでの登録拡張メソッド
  - [ ] シングルトンライフタイムの適用
- [ ] ログ出力とデバッグサポート
  - [ ] イベントフロー追跡機能
  - [ ] パフォーマンスメトリクス出力
- [ ] 単体テストの作成
  - [ ] 基本機能のテスト
  - [ ] エッジケースの検証
  - [ ] パフォーマンステスト

## 実装例
```csharp
/// <summary>
/// イベント集約機構の実装
/// </summary>
public class EventAggregator : IEventAggregator
{
    private readonly ILogger<EventAggregator>? _logger;
    private readonly Dictionary<Type, List<object>> _handlers = new();
    private readonly object _syncRoot = new();
    
    public EventAggregator(ILogger<EventAggregator>? logger = null)
    {
        _logger = logger;
    }
    
    public async Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent
    {
        if (@event == null)
            throw new ArgumentNullException(nameof(@event));
            
        _logger?.LogDebug("イベント発行: {EventType} (ID: {@EventId})", typeof(TEvent).Name, @event.Id);
        
        var eventType = typeof(TEvent);
        List<object>? eventHandlers = null;
        
        lock (_syncRoot)
        {
            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                eventHandlers = handlers.ToList(); // スレッドセーフにするため複製
            }
        }
        
        if (eventHandlers == null || eventHandlers.Count == 0)
        {
            _logger?.LogDebug("イベント {EventType} のハンドラーが登録されていません", eventType.Name);
            return;
        }
        
        var tasks = new List<Task>();
        
        foreach (var handler in eventHandlers.OfType<IEventHandler<TEvent>>())
        {
            try
            {
                tasks.Add(handler.HandleAsync(@event));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ハンドラー {HandlerType} でイベント処理中にエラーが発生しました", 
                    handler.GetType().Name);
            }
        }
        
        await Task.WhenAll(tasks);
        _logger?.LogDebug("イベント {EventType} の処理が完了しました (ハンドラー数: {HandlerCount})", 
            eventType.Name, eventHandlers.Count);
    }
    
    public void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
            
        var eventType = typeof(TEvent);
        
        lock (_syncRoot)
        {
            if (!_handlers.TryGetValue(eventType, out var handlers))
            {
                handlers = new List<object>();
                _handlers[eventType] = handlers;
            }
            
            if (!handlers.Contains(handler))
            {
                handlers.Add(handler);
                _logger?.LogDebug("ハンドラー {HandlerType} をイベント {EventType} に登録しました", 
                    handler.GetType().Name, eventType.Name);
            }
        }
    }
    
    public void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
            
        var eventType = typeof(TEvent);
        
        lock (_syncRoot)
        {
            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                if (handlers.Remove(handler))
                {
                    _logger?.LogDebug("ハンドラー {HandlerType} のイベント {EventType} 登録を解除しました", 
                        handler.GetType().Name, eventType.Name);
                }
            }
        }
    }
}
```

## DIでの登録
```csharp
public static class EventAggregatorServiceExtensions
{
    public static IServiceCollection AddEventAggregator(this IServiceCollection services)
    {
        services.AddSingleton<IEventAggregator, EventAggregator>();
        return services;
    }
}
```

## 関連Issue/参考
- 親Issue: #4 実装: イベント集約機構の構築
- 依存: #4.1 実装: イベント関連インターフェースの設計と実装
- 依存: #4.2 実装: イベント型とハンドラーの実装
- 関連: #2 改善: 依存性注入モジュール構造の実装
- 参照: E:\dev\Baketa\docs\3-architecture\improved-architecture.md (7. イベント集約機構の設計)
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (3.4 キャンセレーション対応)
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (6. エラーとパフォーマンスのログ記録)

## マイルストーン
マイルストーン1: アーキテクチャ基盤の改善

## ラベル
- `type: feature`
- `priority: high`
- `component: core`
