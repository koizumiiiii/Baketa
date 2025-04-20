using System.Runtime.Versioning;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.Platform
{
    public static class PlatformServiceExtensions
    {
        /// <summary>
        /// プラットフォームサービスの登録
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static IServiceCollection AddPlatformServices(this IServiceCollection services)
        {
            // Windows専用アプリケーションのため、直接Windows実装を登録
            // 実際のクラスが実装されたら、コメントアウトを外してください
            /*
            services.AddSingleton<IWindowManager, WindowsManager>();
            services.AddSingleton<IScreenCapturer, WindowsCapturer>();
            services.AddSingleton<IKeyboardHook, WindowsKeyboardHook>();
            
            // イメージファクトリ登録
            services.AddSingleton<IWindowsImageFactory, WindowsImageFactory>();
            
            // アダプター登録
            services.AddSingleton<IImageFactory, WindowsImageAdapterFactory>();
            */
            
            return services;
        }
    }
}