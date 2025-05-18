using System;
using Baketa.UI.Framework.Debugging;
using Baketa.UI.Framework.Events;
using Baketa.UI.Framework.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Splat;
using Splat.Microsoft.Extensions.DependencyInjection;

namespace Baketa.UI.Framework;

    /// <summary>
    /// ReactiveUI依存性注入設定
    /// </summary>
    internal static class ReactiveUIDependencyInjection
    {
        /// <summary>
        /// ReactiveUIサービスを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="enableDebugMode">デバッグモードを有効化するかどうか</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddReactiveUI(
            this IServiceCollection services, 
            bool enableDebugMode = false)
        {
            // SplatをMicrosoft.Extensions.DependencyInjectionと接続
            services.UseMicrosoftDependencyResolver();
            
            // ReactiveUIサービスを登録
            Splat.Locator.CurrentMutable.InitializeReactiveUI();

            // イベント集約機構を登録
            services.AddSingleton<IEventAggregator, EventAggregator>();
            
            // ナビゲーション機構を登録
            services.AddSingleton<INavigationHost, NavigationManager>();
            
            // デバッグモードの設定
            if (enableDebugMode)
            {
                services.AddSingleton<Action<System.IServiceProvider>>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<object>>();
                    ReactiveUiDebugging.EnableReactiveUiDebugMode(logger);
                });
            }

            return services;
        }
        
        /// <summary>
        /// ビューモデルをナビゲーション対応で登録します
        /// </summary>
        /// <typeparam name="TViewModel">ビューモデルの型</typeparam>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddNavigableViewModel<TViewModel>(
            this IServiceCollection services)
            where TViewModel : class, IRoutableViewModel
        {
            services.AddTransient<TViewModel>();
            return services;
        }
        
        /// <summary>
        /// パラメータ付きビューモデルファクトリを登録します
        /// </summary>
        /// <typeparam name="TViewModel">ビューモデルの型</typeparam>
        /// <typeparam name="TParam">パラメータの型</typeparam>
        /// <param name="services">サービスコレクション</param>
        /// <param name="factory">ファクトリ関数</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddViewModelFactory<TViewModel, TParam>(
            this IServiceCollection services,
            Func<System.IServiceProvider, TParam, TViewModel> factory)
            where TViewModel : class, IRoutableViewModel
        {
            services.AddTransient<Func<TParam, TViewModel>>(sp => 
                param => factory(sp, param));
            return services;
        }
    }
