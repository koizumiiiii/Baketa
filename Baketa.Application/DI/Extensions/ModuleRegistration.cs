using Baketa.Application.DI.Modules;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI.Modules;
using Baketa.Infrastructure.Platform.DI.Modules;
//using Baketa.UI.DI.Modules; // UIモジュールは現在実装中
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Baketa.Application.DI.Extensions
{
    /// <summary>
    /// Baketaの標準モジュールを登録するための拡張メソッドを提供します。
    /// </summary>
    public static class ModuleRegistration
    {
        /// <summary>
        /// Baketaの標準モジュールを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="scanForModules">オプショナルなモジュールを自動スキャンするかどうか</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddBaketaModules(
            this IServiceCollection services,
            bool scanForModules = false)
        {
            // 標準モジュールのインスタンスを作成
            var standardModules = new IServiceModule[]
            {
                new CoreModule(),              // コアレイヤー
                // new InfrastructureModule(),    // インフラストラクチャレイヤー (現時点では実装済みのモジュールのみ含める)
                new PlatformModule(),          // プラットフォーム依存レイヤー
                new ApplicationModule(),       // アプリケーションレイヤー
                // new UIModule()                 // UIレイヤー (現時点では実装済みのモジュールのみ含める)
            };
            
            // ServiceCollectionExtensions.AddBaketaServicesを使用して登録
            return services.AddBaketaServices(scanForModules, standardModules);
        }
        
        /// <summary>
        /// Baketa.Coreのモジュールのみを登録します。
        /// テスト用やヘッドレス実行に便利です。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddBaketaCoreModules(this IServiceCollection services)
        {
            return services.AddBaketaServices(false, new CoreModule());
        }
        
        /// <summary>
        /// Baketa.Infrastructureとそれまでのモジュールのみを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddBaketaInfrastructureModules(this IServiceCollection services)
        {
            return services.AddBaketaServices(
                false, 
                new CoreModule());
                // Infrastructureモジュールは現在実装中
        }
        
        /// <summary>
        /// Baketa.Applicationとそれまでのモジュールのみを登録します。
        /// UI要素を含まないバージョンに便利です。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddBaketaApplicationModules(this IServiceCollection services)
        {
            return services.AddBaketaServices(
                false, 
                new CoreModule(),
                // new InfrastructureModule(), 現在実装中
                new PlatformModule(),
                new ApplicationModule());
        }
    }
}