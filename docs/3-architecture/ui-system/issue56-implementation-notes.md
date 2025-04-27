# Issue56 実装ノート: ReactiveUIベースのMVVMフレームワーク

## 概要

このドキュメントはIssue56「ReactiveUIベースのMVVMフレームワーク実装」の対応過程で得られた知見と実装上の重要なポイントをまとめたものです。将来の開発者が参照できるよう、特に注意が必要な点や最適化手法について記録しています。

## 1. 警告対応のポイント

### 1.1 Null参照安全性（CS8604）

ReactiveObjectExtensionsクラスにおける`RaisePropertyChanged`メソッドでは、引数として渡される`propertyNames`配列のnull参照警告が発生していました。配列自体のnullチェックだけでなく、配列内の各要素のnullチェックも必要でした。

```csharp
public static void RaisePropertyChanged(this ReactiveObject This, params string[] propertyNames)
{
    ArgumentNullException.ThrowIfNull(This);
    ArgumentNullException.ThrowIfNull(propertyNames);
    foreach (var propertyName in propertyNames)
    {
        // propertyNameがnullでないことを確認してから処理
        if (propertyName is not null)
        {
            This.RaisePropertyChanged(propertyName);
        }
    }
}
```

### 1.2 未使用パラメーター（IDE0060）

未使用パラメーターがある場合は、アンダースコア(`_`)を使用して明示的に破棄パラメーターであることを示すことが推奨されています。

```csharp
// 修正前
private static void RegisterUIServices(IServiceCollection services)
{
    // 実装なし、パラメーターが未使用
}

// 修正後
private static void RegisterUIServices(IServiceCollection _)
{
    // 実装なし、明示的に破棄パラメーターとして宣言
}

// または、警告を抑制する方法
private static void RegisterUIServices(IServiceCollection services)
{
    // 未使用の警告を抑制するためのコード
    _ = services;
}
```

### 1.3 プライマリコンストラクター（IDE0290）

C# 12以降でサポートされているプライマリコンストラクターを使用することで、コードの簡潔化が可能です。

```csharp
// 修正前
internal sealed class ReactiveUiDebuggingExceptionHandler : IObserver<Exception>
{
    private readonly ILogger? _logger;

    public ReactiveUiDebuggingExceptionHandler(ILogger? logger = null)
    {
        _logger = logger;
    }
    
    // メソッド実装...
}

// 修正後
internal sealed class ReactiveUiDebuggingExceptionHandler(ILogger? logger = null) : IObserver<Exception>
{
    private readonly ILogger? _logger = logger;
    
    // メソッド実装...
}
```

### 1.4 コレクション初期化の最適化（IDE0028/IDE0300/IDE0301）

コレクション初期化時には、適切な初期容量を指定することでパフォーマンスを向上させることができます。

```csharp
// 修正前
var tasks = new List<Task>();

// 修正後
var tasks = new List<Task>(handlers.Count);
```

`GetOrAdd`メソッドのようなファクトリー関数では、`static`修飾子を使用することで不要なキャプチャを避けられます。

```csharp
// 修正前
var handlers = _handlers.GetOrAdd(eventType, _ => new List<object>());

// 修正後
var handlers = _handlers.GetOrAdd(eventType, static _ => new List<object>(5));
```

## 2. テスト時の注意点

### 2.1 Exceptionのデフォルトメッセージ

カスタム例外クラスを実装する際、引数なしコンストラクターでは基底クラスの`Exception()`が呼び出されますが、このコンストラクターは空文字列ではなく、規定のメッセージ（"Exception of type [型名] was thrown."）を設定します。

テストでメッセージが空文字列であることを期待する場合は、明示的に空文字列を渡す必要があります。

```csharp
// 修正前（テストが失敗する可能性あり）
public OcrProcessingException() : base() { }

// 修正後（空の文字列を明示的に指定）
public OcrProcessingException() : base("") { }
```

## 3. ReactiveUIのベストプラクティス

### 3.1 ViewModelBaseの実装

```csharp
internal abstract class ViewModelBase : ReactiveObject, IActivatableViewModel, IDisposable
{
    // アクティベーター
    public ViewModelActivator Activator { get; } = new ViewModelActivator();
    
    // イベント集約器
    protected readonly IEventAggregator _eventAggregator;
    
    // ロガー（オプション）
    protected readonly ILogger? _logger;
    
    // エラーメッセージ用プロパティ
    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }
    
    // リソース破棄用コレクション
    protected readonly CompositeDisposable _disposables = new();
    
    // コンストラクタ
    protected ViewModelBase(IEventAggregator eventAggregator, ILogger? logger = null)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger;
        
        // アクティベーション設定
        this.WhenActivated(disposables =>
        {
            HandleActivation();
            Disposable.Create(HandleDeactivation).DisposeWith(disposables);
        });
    }
    
    // アクティベーションハンドラー
    protected virtual void HandleActivation() { }
    
    // 非アクティベーションハンドラー
    protected virtual void HandleDeactivation() { }
    
    // IDisposable実装
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposables.Dispose();
        }
    }
}
```

### 3.2 イベント集約機構の実装

```csharp
internal sealed class EventAggregator(ILogger<EventAggregator>? logger = null) : IEventAggregator
{
    private readonly ILogger<EventAggregator>? _logger = logger;
    private readonly ConcurrentDictionary<Type, List<object>> _handlers = new();
    
    // イベント発行
    public async Task PublishAsync<TEvent>(TEvent eventData) where TEvent : IEvent
    {
        var eventType = typeof(TEvent);
        
        if (!_handlers.TryGetValue(eventType, out var handlers))
        {
            return;
        }
        
        var tasks = new List<Task>(handlers.Count);
        
        foreach (var handler in handlers.ToList())
        {
            if (handler is not Func<TEvent, Task> typedHandler)
            {
                continue;
            }
            
            tasks.Add(typedHandler(eventData));
        }
        
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
    
    // イベント購読
    public IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : IEvent
    {
        var eventType = typeof(TEvent);
        
        var handlers = _handlers.GetOrAdd(eventType, static _ => new List<object>(5));
        lock (handlers)
        {
            handlers.Add(handler);
        }
        
        return new SubscriptionToken(() => Unsubscribe(eventType, handler));
    }
    
    // 購読解除
    private void Unsubscribe(Type eventType, object handler)
    {
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
    
    // 購読トークン
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
```

## 4. 将来の検討事項

1. **ReactiveUI.Fodyの最適化**:
   - プロパティ定義の簡素化
   - 自動プロパティ変更通知の活用

2. **エラー処理の改善**:
   - ReactiveCommandに対する統合エラー処理
   - ビューモデルレベルでのエラー管理

3. **パフォーマンス最適化**:
   - イベント処理の並列化
   - メモリ使用量の最適化

## 5. 参考リソース

- [Baketaプロジェクト - ReactiveUI実装ガイド](./reactiveui-guide.md)
- [ReactiveUIバージョン互換性ガイド](./reactiveui-version-compatibility.md)
- [C# 12の新機能](../../2-development/language-features/csharp-12-support.md)
- [Baketaコーディング規約](../../2-development/coding-standards/csharp-standards.md)

---

作成日: 2025-04-28  
最終更新日: 2025-04-28  
担当者: Baketa開発チーム
