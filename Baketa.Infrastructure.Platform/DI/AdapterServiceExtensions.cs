using System;
using Baketa.Infrastructure.Platform.Adapters;
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
            
            // Windows画像ファクトリを登録
            // 注: 実際の実装では、プロジェクトのサービス構成に合わせて適切なファクトリを登録
            services.AddSingleton<Baketa.Core.Abstractions.Platform.Windows.IWindowsImageFactory, Baketa.Infrastructure.Platform.Windows.WindowsImageFactory>();

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
    }
}