using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.Framework;

    /// <summary>
    /// Baketaアプリケーション用のビューモデル基底クラス
    /// </summary>
    public abstract class ViewModelBase : ReactiveObject, IActivatableViewModel, IDisposable
    {
        /// <summary>
        /// アクティベーション処理
        /// </summary>
        public ViewModelActivator Activator { get; } = new ViewModelActivator();
        
        /// <summary>
        /// イベント集約器
        /// </summary>
        protected IEventAggregator EventAggregator { get; }
        
        /// <summary>
        /// ロガー
        /// </summary>
        protected ILogger? Logger { get; }
        

        
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        private string? _errorMessage;
        public string? ErrorMessage
        {
            get => _errorMessage;
            set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
        }
        
        /// <summary>
        /// 読み込み中フラグ
        /// </summary>
        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }
        
        /// <summary>
        /// リソース破棄用コレクション
        /// </summary>
        protected CompositeDisposable Disposables { get; } = [];
        
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
            ArgumentNullException.ThrowIfNull(eventAggregator);
            
            EventAggregator = eventAggregator;
            Logger = logger;
            
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
                Disposables.Dispose();
            }
            
            _disposed = true;
        }
        
        /// <summary>
        /// イベントを発行します
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="eventData">イベントインスタンス</param>
        /// <returns>発行タスク</returns>
        protected Task PublishEventAsync<TEvent>(TEvent eventData) where TEvent : IEvent
        {
            ArgumentNullException.ThrowIfNull(eventData);
            return EventAggregator.PublishAsync(eventData);
        }
        
        /// <summary>
        /// イベントをサブスクライブします（プロセッサー版）
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="processor">イベントプロセッサー</param>
        /// <returns>購読解除可能なDisposable</returns>
        protected void SubscribeToEvent<TEvent>(IEventProcessor<TEvent> processor) where TEvent : IEvent
        {
            ArgumentNullException.ThrowIfNull(processor);
            EventAggregator.Subscribe<TEvent>(processor);
        }
        
        /// <summary>
        /// イベントをサブスクライブします（ハンドラ版）
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="handler">イベントハンドラ</param>
        /// <returns>購読解除可能なDisposable</returns>
        protected IDisposable SubscribeToEvent<TEvent>(Func<TEvent, Task> handler) where TEvent : IEvent
        {
            ArgumentNullException.ThrowIfNull(handler);
            
            // インラインプロセッサーを作成
            var processor = new InlineEventProcessor<TEvent>(handler);
            EventAggregator.Subscribe<TEvent>(processor);
            
            // 購読解除用のDisposableを返す
            var subscription = Disposable.Create(() => EventAggregator.Unsubscribe<TEvent>(processor));
            Disposables.Add(subscription);
            return subscription;
        }
        
        /// <summary>
        /// インラインイベントプロセッサー
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        private sealed class InlineEventProcessor<TEvent> : IEventProcessor<TEvent>
            where TEvent : IEvent
        {
            private readonly Func<TEvent, Task> _handler;
            
            public InlineEventProcessor(Func<TEvent, Task> handler)
            {
                ArgumentNullException.ThrowIfNull(handler);
                _handler = handler;
            }
            
            public int Priority => 100;
            public bool SynchronousExecution => false;
            
            public Task HandleAsync(TEvent eventData)
            {
                return _handler(eventData);
            }
        }
    }
