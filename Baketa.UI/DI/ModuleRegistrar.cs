using Baketa.Application.DI.Extensions;
using Baketa.Core.DI;
using Baketa.UI.DI.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.UI.DI
{
    /// <summary>
    /// UIモジュール登録クラス
    /// </summary>
    public static class ModuleRegistrar
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
            // 基本モジュールを追加
            services.AddBaketaApplicationModules(environment);
            
            // UIモジュールを追加
            var uiModule = new UIModule();
            uiModule.RegisterServices(services);
            
            return services;
        }
    }
}