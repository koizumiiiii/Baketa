using System;
using System.Collections.Generic;
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
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _errorMessage, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でErrorMessage設定失敗 - 直接設定で続行");
                _errorMessage = value;
            }
        }
    }
    
    /// <summary>
    /// 読み込み中フラグ
    /// </summary>
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _isLoading, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でIsLoading設定失敗 - 直接設定で続行");
                _isLoading = value;
            }
        }
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
    /// 新しいビューモデルを初期化します（イベント集約器のみ）
    /// </summary>
    /// <param name="eventAggregator">イベント集約器</param>
    protected ViewModelBase(IEventAggregator eventAggregator)
    {
        EventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        Logger = null;
        
        // アクティベーション処理の設定
        this.WhenActivated(disposables =>
        {
            HandleActivation();
            Disposable.Create(HandleDeactivation).DisposeWith(disposables);
        });
    }
    
    /// <summary>
    /// 新しいビューモデルを初期化します（フルパラメータ）
    /// </summary>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー</param>
    protected ViewModelBase(IEventAggregator eventAggregator, ILogger? logger)
    {
        EventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
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
        Console.WriteLine($"🚀 ViewModelBase.PublishEventAsync開始: {typeof(TEvent).Name} (ID: {eventData.Id})");
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🚀 ViewModelBase.PublishEventAsync開始: {typeof(TEvent).Name} (ID: {eventData.Id})");
        var task = EventAggregator.PublishAsync(eventData);
        Console.WriteLine($"✅ ViewModelBase.PublishEventAsync呼び出し完了: {typeof(TEvent).Name}");
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ ViewModelBase.PublishEventAsync呼び出し完了: {typeof(TEvent).Name}");
        return task;
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
        
        try
        {
            // インラインプロセッサーを作成
            var processor = new InlineEventProcessor<TEvent>(handler);
            EventAggregator.Subscribe<TEvent>(processor);
            
            // 購読解除用のDisposableを返す
            var subscription = Disposable.Create(() => 
            {
                try
                {
                    EventAggregator.Unsubscribe<TEvent>(processor);
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "イベント購読解除中にエラーが発生: {EventType}", typeof(TEvent).Name);
                }
            });
            Disposables.Add(subscription);
            return subscription;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "イベント購読中にエラーが発生: {EventType}", typeof(TEvent).Name);
            throw;
        }
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
            try
            {
                return _handler(eventData);
            }
            catch (InvalidOperationException ex)
            {
                // UIスレッド違反が発生した場合はログ出力して続行
                Console.WriteLine($"🚨 InlineEventProcessorでUIスレッド違反: {ex.Message}");
                return Task.CompletedTask;
            }
        }
    }
}
