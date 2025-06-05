using IImageFactoryInterface = Baketa.Core.Abstractions.Factories.IImageFactory;
using IWindowsImageFactoryInterface = Baketa.Core.Abstractions.Factories.IWindowsImageFactory;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Adapters;
using Baketa.Infrastructure.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.Platform.DependencyInjection;

    /// <summary>
    /// プラットフォームサービス拡張メソッド
    /// </summary>
    public static class PlatformServiceExtensions
    {
        /// <summary>
        /// プラットフォームサービスを依存性注入コンテナに登録
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddPlatformServices(this IServiceCollection services)
        {
            // Windows画像関連
            services.AddSingleton<IWindowsImageFactoryInterface, WindowsImageFactory>();
            services.AddSingleton<IImageFactoryInterface, WindowsImageAdapterFactory>();

            // その他のプラットフォームサービスを登録
            // services.AddSingleton<IWindowManager, WindowsManager>();
            // services.AddSingleton<IScreenCapturer, WindowsCapturer>();
            // ...
            
            return services;
        }
    }
