using Baketa.Core.DI;
using Baketa.Application.DI.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.UI.DI;

    /// <summary>
    /// UIモジュール登録クラス
    /// </summary>
    internal static class ModuleRegistrar
    {
        /// <summary>
        /// UIモジュールを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="environment">環境設定</param>
        /// <returns>登録後のサービスコレクション</returns>
        public static IServiceCollection AddUIModule(
            this IServiceCollection services,
            BaketaEnvironment environment = BaketaEnvironment.Production)
        {
            // アプリケーションモジュールを追加
            services.AddBaketaApplicationModules(environment);
            
            // ReactiveUIサービスをモジュール経由で登録
            var enableDebugMode = environment == BaketaEnvironment.Development;
            services.AddReactiveUIServices(enableDebugMode);
            
            // UIサービスとビューモデルを登録
            services.RegisterUIServices();
            
            return services;
        }
    }
