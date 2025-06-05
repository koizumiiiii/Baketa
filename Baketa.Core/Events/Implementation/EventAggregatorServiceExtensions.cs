using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Core.Events.Implementation;

    /// <summary>
    /// イベント集約機構のサービス登録拡張メソッド
    /// </summary>
    public static class EventAggregatorServiceExtensions
    {
        /// <summary>
        /// イベント集約機構をサービスコレクションに登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>設定済みのサービスコレクション</returns>
        public static IServiceCollection AddEventAggregator(this IServiceCollection services)
        {
            // イベント集約機構をシングルトンとして登録
            services.AddSingleton<IEventAggregator, EventAggregator>();
            
            return services;
        }
    }
