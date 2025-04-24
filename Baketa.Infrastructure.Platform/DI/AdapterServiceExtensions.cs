using System;
using Baketa.Infrastructure.Platform.Adapters;
using Baketa.Infrastructure.Platform.Adapters.Factory;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.Platform.DI
{
    /// <summary>
    /// アダプターサービスの登録を行う拡張メソッド群
    /// </summary>
    public static class AdapterServiceExtensions
    {
        /// <summary>
        /// アダプターサービスを依存性注入コンテナに登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション（チェーン呼び出し用）</returns>
        public static IServiceCollection AddAdapterServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));
            
            // Windowsサービス実装の登録
            services.AddSingleton<Baketa.Core.Abstractions.Factories.IWindowsImageFactory, Baketa.Infrastructure.Platform.Windows.WindowsImageFactory>();
            services.AddSingleton<Baketa.Core.Abstractions.Platform.Windows.IWindowsCapturer, Baketa.Infrastructure.Platform.Windows.WindowsCapturerStub>();
            services.AddSingleton<Baketa.Core.Abstractions.Platform.Windows.IWindowManager, Baketa.Infrastructure.Platform.Windows.WindowsManagerStub>();

            // アダプターインターフェースとスタブ実装を登録
            services.AddSingleton<IWindowsImageAdapter, WindowsImageAdapterStub>();
            services.AddSingleton<ICaptureAdapter>(sp => {
                var imageAdapter = sp.GetRequiredService<IWindowsImageAdapter>();
                var capturer = sp.GetRequiredService<Baketa.Core.Abstractions.Platform.Windows.IWindowsCapturer>();
                return new CaptureAdapterStub(imageAdapter, capturer);
            });
            services.AddSingleton<IWindowManagerAdapter>(sp => {
                var windowManager = sp.GetRequiredService<Baketa.Core.Abstractions.Platform.Windows.IWindowManager>();
                return new WindowManagerAdapterStub(windowManager);
            });
            
            return services;
        }
        
        /// <summary>
        /// テスト用のモックアダプターサービスを依存性注入コンテナに登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション（チェーン呼び出し用）</returns>
        public static IServiceCollection AddMockAdapterServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));
            
            // モックアダプター実装の登録（実際の実装では別のモッククラスを使用）
            services.AddSingleton<IWindowsImageAdapter, WindowsImageAdapterStub>();
            services.AddSingleton<ICaptureAdapter, CaptureAdapterStub>();
            services.AddSingleton<IWindowManagerAdapter, WindowManagerAdapterStub>();
            
            return services;
        }

        /// <summary>
        /// すべてのアダプターサービスを依存性注入コンテナに登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション（チェーン呼び出し用）</returns>
        public static IServiceCollection AddAllAdapterServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));
            
            // Windowsサービス実装の登録
            services.AddWindowsPlatformServices();
            
            // アダプターサービスの登録
            services.AddAdapterServices();
            
            // アダプターファクトリーの登録
            services.AddAdapterFactoryServices();
            
            return services;
        }
        
        /// <summary>
        /// 開発環境向けのアダプターサービスを依存性注入コンテナに登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション（チェーン呼び出し用）</returns>
        public static IServiceCollection AddDevelopmentAdapterServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));
            
            // Windowsサービス実装の登録
            services.AddWindowsPlatformServices();
            
            // スタブアダプターサービスの登録
            services.AddStubAdapterFactoryServices();
            
            return services;
        }
        
        /// <summary>
        /// テスト環境向けのアダプターサービスを依存性注入コンテナに登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション（チェーン呼び出し用）</returns>
        public static IServiceCollection AddTestAdapterServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));
            
            // モックアダプターサービスの登録
            services.AddMockAdapterFactoryServices();
            
            return services;
        }
    }
}