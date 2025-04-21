# Issue 10-1: ReactiveUIベースのMVVMフレームワーク実装

## 概要
ReactiveUIパターンを活用したMVVM（Model-View-ViewModel）フレームワークをAvalonia UIプロジェクトに実装します。これにより、アプリケーション全体でのUI状態管理と反応性のあるユーザーインターフェース開発の基盤を確立します。

## 目的・理由
ReactiveUIベースのMVVMパターンを採用する理由は以下の通りです：

1. 宣言的なUI状態管理により、データバインディングのバグを減少させる
2. リアクティブプログラミングによる非同期操作の簡素化
3. ビジネスロジックとUIの明確な分離によるテスト容易性の向上
4. 継続的なUI状態更新によるリアルタイム性の確保
5. コードの再利用性と保守性の向上

## 詳細
- ReactiveUIベースのViewModelBase基底クラスの実装
- ObservableプロパティとReactiveCommandのユーティリティ実装
- ビューとビューモデルのバインディング機構の実装
- メッセージングとイベント処理の統合機能の実装

## タスク分解
- [ ] ReactiveUI基盤の構築
  - [ ] ReactiveUIパッケージの追加と設定
  - [ ] アプリケーションブートストラップの設定
  - [ ] ReactiveUI用のSplat DIコンテナ設定
- [ ] ViewModelBase実装
  - [ ] `ViewModelBase`抽象クラスの設計と実装
  - [ ] プロパティ変更通知の最適化実装
  - [ ] Observableプロパティ管理機能の実装
  - [ ] ReactiveCommandヘルパーの実装
- [ ] ビューとビューモデル連携機構
  - [ ] `IViewFor<T>`実装ヘルパーの構築
  - [ ] バインディングヘルパーエクステンションの実装
  - [ ] バインディングコンテキスト管理の実装
- [ ] 画面遷移・ナビゲーション機構
  - [ ] `IScreen`および`RoutingState`の設定
  - [ ] ビュー管理システムの実装
  - [ ] 画面遷移アニメーションの基盤実装
- [ ] イベント集約機構との統合
  - [ ] `IEventAggregator`とReactiveUIメッセージバスの連携
  - [ ] イベント購読ヘルパーの実装
- [ ] バリデーション機構
  - [ ] リアクティブなバリデーションの実装
  - [ ] バリデーションメッセージ管理システムの実装
- [ ] デバッグとロギング
  - [ ] ReactiveUIデバッグツールの統合
  - [ ] プロパティ変更とコマンド実行のロギング
- [ ] 単体テストの実装
  - [ ] ビューモデルのユニットテスト基盤構築
  - [ ] モック用インフラストラクチャの構築

## インターフェース設計案
```csharp
namespace Baketa.UI.Framework
{
    /// <summary>
    /// Baketaアプリケーション用のビューモデル基底クラス
    /// </summary>
    public abstract class ViewModelBase : ReactiveObject, IActivatableViewModel, IDeactivatable, IDisposable
    {
        /// <summary>
        /// アクティベーション処理
        /// </summary>
        public ViewModelActivator Activator { get; } = new ViewModelActivator();
        
        /// <summary>
        /// イベント集約器
        /// </summary>
        protected readonly IEventAggregator _eventAggregator;
        
        /// <summary>
        /// ロガー
        /// </summary>
        protected readonly ILogger? _logger;
        
        /// <summary>
        /// 廃棄フラグ
        /// </summary>
        private bool _disposed;
        
        /// <summary>
        /// 新しいビューモデルを初期化します
        /// </summary>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="logger">ロガー</param>
        protected ViewModelBase(IEventAggregator eventAggregator, ILogger? logger = null)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _logger = logger;
            
            // アクティベーション処理の設定
            this.WhenActivated(disposables =>
            {
                HandleActivation();
                Disposable.Create(HandleDeactivation).DisposeWith(disposables);
            });
        }
        
        /// <summary>
        /// ビューモデルがアクティブ化されたときの処理
        /// </summary>
        protected virtual void HandleActivation()
        {
            // 派生クラスでオーバーライド
        }
        
        /// <summary>
        /// ビューモデルが非アクティブ化されたときの処理
        /// </summary>
        protected virtual void HandleDeactivation()
        {
            // 派生クラスでオーバーライド
        }
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースを解放するかどうか</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;
                
            if (disposing)
            {
                // マネージドリソースの解放
            }
            
            _disposed = true;
        }
        
        /// <summary>
        /// イベントを発行します
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="event">イベントインスタンス</param>
        /// <returns>発行タスク</returns>
        protected Task PublishEventAsync<TEvent>(TEvent @event) where TEvent : IEvent
        {
            return _eventAggregator.PublishAsync(@event);
        }
        
        /// <summary>
        /// イベントをサブスクライブします
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="handler">ハンドラ</param>
        /// <returns>購読解除可能なDisposable</returns>
        protected IDisposable SubscribeToEvent<TEvent>(Func<TEvent, Task> handler) where TEvent : IEvent
        {
            return _eventAggregator.Subscribe<TEvent>(handler);
        }
    }
    
    /// <summary>
    /// プロパティ変更通知を最適化するエクステンション
    /// </summary>
    public static class ReactiveObjectExtensions
    {
        /// <summary>
        /// プロパティを設定し、変更があった場合のみ通知します
        /// </summary>
        /// <typeparam name="TObj">ReactiveObjectの型</typeparam>
        /// <typeparam name="TProperty">プロパティの型</typeparam>
        /// <param name="This">ReactiveObjectインスタンス</param>
        /// <param name="backingField">バッキングフィールドの参照</param>
        /// <param name="newValue">新しい値</param>
        /// <param name="propertyName">プロパティ名</param>
        /// <returns>値が変更されたかどうか</returns>
        public static bool RaiseAndSetIfChanged<TObj, TProperty>(
            this TObj This,
            ref TProperty backingField,
            TProperty newValue,
            [CallerMemberName] string? propertyName = null)
            where TObj : ReactiveObject
        {
            if (EqualityComparer<TProperty>.Default.Equals(backingField, newValue))
                return false;
                
            This.RaisePropertyChanging(propertyName);
            backingField = newValue;
            This.RaisePropertyChanged(propertyName);
            return true;
        }
        
        /// <summary>
        /// 複数のプロパティ変更を一度に通知します
        /// </summary>
        /// <param name="This">ReactiveObjectインスタンス</param>
        /// <param name="propertyNames">プロパティ名の配列</param>
        public static void RaisePropertyChanged(this ReactiveObject This, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                This.RaisePropertyChanged(propertyName);
            }
        }
    }
    
    /// <summary>
    /// 反応型コマンドファクトリ
    /// </summary>
    public static class ReactiveCommandFactory
    {
        /// <summary>
        /// パラメータなしのコマンドを作成します
        /// </summary>
        /// <param name="execute">実行関数</param>
        /// <param name="canExecute">実行可能条件</param>
        /// <returns>コマンド</returns>
        public static ReactiveCommand<Unit, Unit> Create(
            Func<Task> execute,
            IObservable<bool>? canExecute = null)
        {
            return ReactiveCommand.CreateFromTask(execute, canExecute);
        }
        
        /// <summary>
        /// パラメータ付きのコマンドを作成します
        /// </summary>
        /// <typeparam name="TParam">パラメータ型</typeparam>
        /// <param name="execute">実行関数</param>
        /// <param name="canExecute">実行可能条件</param>
        /// <returns>コマンド</returns>
        public static ReactiveCommand<TParam, Unit> Create<TParam>(
            Func<TParam, Task> execute,
            IObservable<bool>? canExecute = null)
        {
            return ReactiveCommand.CreateFromTask(execute, canExecute);
        }
        
        /// <summary>
        /// 戻り値のあるコマンドを作成します
        /// </summary>
        /// <typeparam name="TResult">戻り値の型</typeparam>
        /// <param name="execute">実行関数</param>
        /// <param name="canExecute">実行可能条件</param>
        /// <returns>コマンド</returns>
        public static ReactiveCommand<Unit, TResult> CreateWithResult<TResult>(
            Func<Task<TResult>> execute,
            IObservable<bool>? canExecute = null)
        {
            return ReactiveCommand.CreateFromTask(execute, canExecute);
        }
        
        /// <summary>
        /// パラメータと戻り値のあるコマンドを作成します
        /// </summary>
        /// <typeparam name="TParam">パラメータ型</typeparam>
        /// <typeparam name="TResult">戻り値の型</typeparam>
        /// <param name="execute">実行関数</param>
        /// <param name="canExecute">実行可能条件</param>
        /// <returns>コマンド</returns>
        public static ReactiveCommand<TParam, TResult> CreateWithResult<TParam, TResult>(
            Func<TParam, Task<TResult>> execute,
            IObservable<bool>? canExecute = null)
        {
            return ReactiveCommand.CreateFromTask(execute, canExecute);
        }
    }
    
    /// <summary>
    /// ビューとビューモデルをバインドするヘルパー
    /// </summary>
    public static class ViewBindingHelpers
    {
        /// <summary>
        /// ビューにビューモデルをバインドします
        /// </summary>
        /// <typeparam name="TView">ビューの型</typeparam>
        /// <typeparam name="TViewModel">ビューモデルの型</typeparam>
        /// <param name="view">ビューインスタンス</param>
        /// <param name="disposables">破棄可能オブジェクトコレクション</param>
        public static void BindViewModel<TView, TViewModel>(this TView view, CompositeDisposable disposables)
            where TView : IViewFor<TViewModel>
            where TViewModel : ReactiveObject
        {
            // ビューとビューモデルのバインディング実装
        }
        
        /// <summary>
        /// 一方向バインディングを設定します
        /// </summary>
        /// <typeparam name="TView">ビューの型</typeparam>
        /// <typeparam name="TViewModel">ビューモデルの型</typeparam>
        /// <typeparam name="TProperty">プロパティの型</typeparam>
        /// <param name="view">ビューインスタンス</param>
        /// <param name="viewProperty">ビュープロパティセレクタ</param>
        /// <param name="viewModelProperty">ビューモデルプロパティセレクタ</param>
        /// <returns>破棄可能なバインディング</returns>
        public static IDisposable OneWayBind<TView, TViewModel, TProperty>(
            this TView view,
            Expression<Func<TView, TProperty>> viewProperty,
            Expression<Func<TViewModel, TProperty>> viewModelProperty)
            where TView : IViewFor<TViewModel>
            where TViewModel : ReactiveObject
        {
            // 一方向バインディング実装
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// 双方向バインディングを設定します
        /// </summary>
        /// <typeparam name="TView">ビューの型</typeparam>
        /// <typeparam name="TViewModel">ビューモデルの型</typeparam>
        /// <typeparam name="TProperty">プロパティの型</typeparam>
        /// <param name="view">ビューインスタンス</param>
        /// <param name="viewProperty">ビュープロパティセレクタ</param>
        /// <param name="viewModelProperty">ビューモデルプロパティセレクタ</param>
        /// <returns>破棄可能なバインディング</returns>
        public static IDisposable TwoWayBind<TView, TViewModel, TProperty>(
            this TView view,
            Expression<Func<TView, TProperty>> viewProperty,
            Expression<Func<TViewModel, TProperty>> viewModelProperty)
            where TView : IViewFor<TViewModel>
            where TViewModel : ReactiveObject
        {
            // 双方向バインディング実装
            throw new NotImplementedException();
        }
        
        /// <summary>
        /// コマンドをバインドします
        /// </summary>
        /// <typeparam name="TView">ビューの型</typeparam>
        /// <typeparam name="TViewModel">ビューモデルの型</typeparam>
        /// <typeparam name="TControl">コントロールの型</typeparam>
        /// <param name="view">ビューインスタンス</param>
        /// <param name="controlSelector">コントロールセレクタ</param>
        /// <param name="command">コマンドセレクタ</param>
        /// <returns>破棄可能なバインディング</returns>
        public static IDisposable BindCommand<TView, TViewModel, TControl>(
            this TView view,
            Expression<Func<TView, TControl>> controlSelector,
            Expression<Func<TViewModel, ReactiveCommand<Unit, Unit>>> command)
            where TView : IViewFor<TViewModel>
            where TViewModel : ReactiveObject
            where TControl : ICommand
        {
            // コマンドバインディング実装
            throw new NotImplementedException();
        }
    }
}
```

## 実装上の注意点
- ReactiveUIパターンの使用において、過度な複雑さを避ける
- `Observable.FromEventPattern`などを使用して非フレームワークイベントも反応的に処理する
- メモリリークを防ぐため、イベント購読の解除を適切に行う
- パフォーマンスに影響を与えうる大量のObservableプロパティに注意する
- ReactiveUI固有のバグや問題に対する対処パターンを把握しておく
- ReactiveCommandのエラーハンドリングを適切に設計・実装する
- デバッグしやすい構造にするため、リアクティブチェーンを適切に分割する

## 関連Issue/参考
- 親Issue: #10 Avalonia UIフレームワーク完全実装
- 依存Issue: #4 イベント集約機構の構築
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\reactive-ui-patterns.md
- 参照: E:\dev\Baketa\docs\3-architecture\ui-system\avalonia-guidelines.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (5.2 イベント通知パターン)

## マイルストーン
マイルストーン3: 翻訳とUI

## ラベル
- `type: feature`
- `priority: high`
- `component: ui`
