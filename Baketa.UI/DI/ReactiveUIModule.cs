using System;
using Baketa.UI.Framework.Debugging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Splat;
using Splat.Microsoft.Extensions.DependencyInjection;

namespace Baketa.UI.DI;

    /// <summary>
    /// ReactiveUIサービスを登録するモジュール
    /// </summary>
    internal static class ReactiveUIModule
    {
        /// <summary>
        /// ReactiveUIサービスを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="enableDebugMode">デバッグモードを有効にするかどうか</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddReactiveUIServices(
            this IServiceCollection services,
            bool enableDebugMode = false)
        {
            // SplatアダプターでReactiveUIのサービスをDIコンテナに接続
            services.UseMicrosoftDependencyResolver();
            
            var resolver = Splat.Locator.CurrentMutable;
            resolver.InitializeSplat();
            resolver.InitializeReactiveUI();
            
            // カスタム例外ハンドラー設定
            if (enableDebugMode)
            {
                // デバッグモードでは詳細な例外情報をログに出力
                services.AddSingleton<ReactiveUiDebuggingExceptionHandler>();
                services.AddSingleton<IObserver<Exception>>(provider => 
                    provider.GetRequiredService<ReactiveUiDebuggingExceptionHandler>());
            }
            else
            {
                // 本番モードではシンプルなログ出力
                services.AddSingleton<IObserver<Exception>>(provider => 
                    new ReactiveUiDebuggingExceptionHandler(
                        provider.GetService<ILogger<ReactiveUiDebuggingExceptionHandler>>()));
            }
            
            return services;
        }
    }
