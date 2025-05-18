using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Baketa.UI.Framework.Events;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.Framework;

    /// <summary>
    /// Baketaアプリケーション用のビューモデル基底クラス
    /// </summary>
    internal abstract class ViewModelBase : ReactiveObject, IActivatableViewModel, IDisposable
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
        protected readonly CompositeDisposable _disposables = new();
        
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
                _disposables.Dispose();
            }
            
            _disposed = true;
        }
        
        /// <summary>
        /// イベントを発行します
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="eventData">イベントインスタンス</param>
        /// <returns>発行タスク</returns>
        protected Task PublishEventAsync<TEvent>(TEvent eventData) where TEvent : Baketa.Core.Events.IEvent
        {
            ArgumentNullException.ThrowIfNull(eventData);
            return _eventAggregator.PublishAsync(eventData);
        }
        
        /// <summary>
        /// イベントをサブスクライブします
        /// </summary>
        /// <typeparam name="TEvent">イベント型</typeparam>
        /// <param name="handler">ハンドラ</param>
        /// <returns>購読解除可能なDisposable</returns>
        protected IDisposable SubscribeToEvent<TEvent>(Func<TEvent, Task> handler) where TEvent : Baketa.Core.Events.IEvent
        {
            ArgumentNullException.ThrowIfNull(handler);
            
            var subscription = _eventAggregator.Subscribe<TEvent>(handler);
            _disposables.Add(subscription);
            return subscription;
        }
    }
