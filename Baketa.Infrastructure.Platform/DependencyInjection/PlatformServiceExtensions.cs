using Baketa.Core.Interfaces.Image;
using Baketa.Infrastructure.Platform.Adapters;
using Baketa.Infrastructure.Platform.Abstractions;
using Baketa.Infrastructure.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.Platform.DependencyInjection
{
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
            services.AddSingleton<IWindowsImageFactory, WindowsImageFactory>();
            services.AddSingleton<IImageFactory, WindowsImageAdapterFactory>();

            // その他のプラットフォームサービスを登録
            // services.AddSingleton<IWindowManager, WindowsManager>();
            // services.AddSingleton<IScreenCapturer, WindowsCapturer>();
            // ...
            
            return services;
        }
    }
}