using System;
using Baketa.UI.Framework.Debugging;
using Baketa.UI.Framework.Navigation;
using Baketa.UI.ViewModels.Examples;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.DI
{
    /// <summary>
    /// ReactiveUI関連サービスを登録するDIモジュール
    /// </summary>
    internal static class ReactiveUIModule
    {
        /// <summary>
        /// ReactiveUI関連サービスを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="enableDebugMode">デバッグモードを有効化するかどうか</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddReactiveUIServices(
            this IServiceCollection services,
            bool enableDebugMode = false)
        {
            // ReactiveUIの基本設定をサービスに登録
            // AddReactiveUIメソッドが定義されていない場合は直接サービスを登録
            services.AddSingleton<IViewLocator, DefaultViewLocator>();
            // RoutingStateのインスタンスを先に作成
            services.AddSingleton<RoutingState>();
            
            // IScreenを実装するクラスを登録
            services.AddSingleton<IScreen>(sp => {
                var routingState = sp.GetRequiredService<RoutingState>();
                // IScreenインターフェースを実装するクラスを返す
                return new ScreenAdapter(routingState);
            });
            
            // IScreenのアダプタークラスを追加
            services.AddTransient<ScreenAdapter>();
            
            if (enableDebugMode)
            {
                // デバッグ機能を有効化
                // カスタム例外ハンドラを使用
                RxApp.DefaultExceptionHandler = new ReactiveUiDebuggingExceptionHandler();
            }
            
            // サンプルビューモデルを登録
            services.AddTransient<ReactiveViewModelExample>();
            
            // 必要に応じて、ホストでサービスプロバイダーが作成された後にデバッグモードを有効化
            if (enableDebugMode)
            {
                services.AddSingleton<Action<System.IServiceProvider>>(sp =>
                {
                    // RxApp初期化後にデバッグモードを有効化
                    var logger = sp.GetRequiredService<ILogger<object>>();
                    var debugLogger = sp.GetRequiredService<ILogger<object>>();
                    ReactiveUiDebugging.EnableReactiveUiDebugMode(debugLogger);
                });
                
                services.AddSingleton<HostBuilderContext>();
            }

            return services;
        }
    }

    /// <summary>
    /// ホストビルダーコンテキスト（アプリケーション起動時の初期化用）
    /// </summary>
    /// <param name="serviceProvider">サービスプロバイダー</param>
    internal class HostBuilderContext(System.IServiceProvider serviceProvider)
    {
        /// <summary>
        /// サービスプロバイダー
        /// </summary>
        public System.IServiceProvider ServiceProvider { get; } = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }
}