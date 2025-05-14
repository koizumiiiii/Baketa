using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Common;
using Baketa.Core.Translation.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Baketa.Core.Translation.DI
{
    /// <summary>
    /// 翻訳サービスのDI拡張メソッド
    /// </summary>
    public static class TranslationServiceCollectionExtensions
    {
        /// <summary>
        /// 翻訳サービスをDIコンテナに登録します
        /// </summary>
        /// <param name="services">DIサービスコレクション</param>
        /// <param name="options">翻訳オプション</param>
        /// <returns>DIサービスコレクション</returns>
        public static IServiceCollection AddTranslationServices(
            this IServiceCollection services, 
            TranslationOptions? options = null)
        {
            // デフォルトのオプション
            options ??= new TranslationOptions();
            
            // 基本サービス登録
            services.AddSingleton(options);
            
            // 各コンポーネントの登録
            services.AddTranslationEngines(options.WebApiOptions);
            services.AddTranslationManagement(options.ManagementOptions);
            services.AddTranslationCache(options.CacheOptions);
            services.AddTranslationEvents(options.EventOptions);
            
            return services;
        }
        
        /// <summary>
        /// 翻訳エンジンをDIコンテナに登録します
        /// </summary>
        /// <param name="services">DIサービスコレクション</param>
        /// <param name="options">WebAPI翻訳オプション</param>
        /// <returns>DIサービスコレクション</returns>
        public static IServiceCollection AddTranslationEngines(
            this IServiceCollection services,
            WebApiTranslationOptions options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(options);
            
            // HttpClientを登録
            services.AddHttpClient();
            
            // 各エンジンの登録はInfrastructureレイヤーで実装
            
            return services;
        }
        
        /// <summary>
        /// 翻訳結果管理をDIコンテナに登録します
        /// </summary>
        /// <param name="services">DIサービスコレクション</param>
        /// <param name="options">翻訳結果管理オプション</param>
        /// <returns>DIサービスコレクション</returns>
        public static IServiceCollection AddTranslationManagement(
            this IServiceCollection services,
            TranslationManagementOptions options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(options);
            
            // 翻訳結果管理の登録はInfrastructureレイヤーで実装
            
            return services;
        }
        
        /// <summary>
        /// 翻訳キャッシュをDIコンテナに登録します
        /// </summary>
        /// <param name="services">DIサービスコレクション</param>
        /// <param name="options">翻訳キャッシュオプション</param>
        /// <returns>DIサービスコレクション</returns>
        public static IServiceCollection AddTranslationCache(
            this IServiceCollection services,
            TranslationCacheOptions options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(options);
            
            // 翻訳キャッシュの登録はInfrastructureレイヤーで実装
            
            return services;
        }
        
        /// <summary>
        /// 翻訳イベントをDIコンテナに登録します
        /// </summary>
        /// <param name="services">DIサービスコレクション</param>
        /// <param name="options">翻訳イベントオプション</param>
        /// <returns>DIサービスコレクション</returns>
        public static IServiceCollection AddTranslationEvents(
            this IServiceCollection services,
            TranslationEventOptions options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(options);
            
            // 基本的なイベント型の登録
            if (options.EnableEvents)
            {
                // イベントハンドラーの登録はアプリケーションレイヤーで実装
            }
            
            return services;
        }
    }
}
