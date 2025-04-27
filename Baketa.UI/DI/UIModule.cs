using System;
using Baketa.Core.DI;
using Baketa.UI.Framework.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.DI
{
    /// <summary>
    /// UI関連サービスを登録するDIモジュール
    /// </summary>
    internal static class UIModule
    {
        /// <summary>
        /// ビューモデルを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection RegisterViewModels(this IServiceCollection services)
        {
            // ViewModelsディレクトリ内のビューモデルを登録
            // 必要に応じて、各ビューモデルを個別に登録することも可能
            
            return services;
        }
        
        /// <summary>
        /// UIサービスを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="environment">環境設定</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection RegisterServices(
            this IServiceCollection services,
            BaketaEnvironment _ = BaketaEnvironment.Production)
        {
            // ナビゲーション関連サービスの登録
            services.AddSingleton<INavigationHost, NavigationManager>();
            
            // その他のUIサービスを登録
            
            return services;
        }
    }
}