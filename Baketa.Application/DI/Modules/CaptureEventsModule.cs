using Baketa.Application.EventHandlers.Capture;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.Capture;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Application.DI.Modules
{
    /// <summary>
    /// キャプチャイベント関連のDIモジュール
    /// </summary>
    public class CaptureEventsModule : IServiceModule
    {
        /// <summary>
        /// キャプチャイベント関連サービスを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        public void RegisterServices(IServiceCollection services)
        {
            // イベントハンドラーの登録
            services.AddSingleton<TextDisappearanceEventHandler>();
            services.AddSingleton<IEventProcessor<IEvent>>(sp => sp.GetRequiredService<TextDisappearanceEventHandler>());
            
            // イベントハンドラーの自動登録
            services.AddSingleton<IEventRegistrar>(sp => 
            {
                var registrar = new EventRegistrar(sp.GetRequiredService<IEventAggregator>());
                
                // テキスト消失イベントハンドラーの登録
                registrar.Register<IEvent>(sp.GetRequiredService<TextDisappearanceEventHandler>());
                
                return registrar;
            });
        }
    }
    
    /// <summary>
    /// イベント登録ヘルパークラス
    /// </summary>
    internal class EventRegistrar : IEventRegistrar
    {
        private readonly IEventAggregator _eventAggregator;
        
        public EventRegistrar(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }
        
        public void Register<TEvent>(IEventProcessor<TEvent> processor) where TEvent : IEvent
        {
            _eventAggregator.Subscribe(processor);
        }
    }
    
    /// <summary>
    /// イベント登録インターフェース
    /// </summary>
    public interface IEventRegistrar
    {
        /// <summary>
        /// イベントプロセッサーを登録します
        /// </summary>
        void Register<TEvent>(IEventProcessor<TEvent> processor) where TEvent : IEvent;
    }
}