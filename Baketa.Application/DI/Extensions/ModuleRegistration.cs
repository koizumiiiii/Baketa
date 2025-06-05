using Baketa.Application.DI.Modules;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Modules;
using Baketa.Infrastructure.DI.Modules;
using Baketa.Infrastructure.Platform.DI.Modules;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Baketa.Application.DI.Extensions;

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
        /// <param name="environment">アプリケーション実行環境</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddBaketaModules(
            this IServiceCollection services,
            bool scanForModules = false,
            BaketaEnvironment environment = BaketaEnvironment.Production)
        {
            // 標準モジュールのインスタンスを作成
            var standardModules = new IServiceModule[]
            {
                new CoreModule(),              // コアレイヤー
                new InfrastructureModule(),    // インフラストラクチャレイヤー
                new PlatformModule(),          // プラットフォーム依存レイヤー
                new ApplicationModule(),       // アプリケーションレイヤー
                // UIModuleは別途登録する
                // new UIModule()                 // UIレイヤー
            };
            
            // ServiceCollectionExtensions.AddBaketaServicesを使用して登録
            return services.AddBaketaServices(scanForModules, environment, standardModules);
        }
        
        /// <summary>
        /// Baketa.Coreのモジュールのみを登録します。
        /// テスト用やヘッドレス実行に便利です。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="environment">アプリケーション実行環境</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddBaketaCoreModules(
            this IServiceCollection services,
            BaketaEnvironment environment = BaketaEnvironment.Production)
        {
            return services.AddBaketaServices(false, environment, new CoreModule());
        }
        
        /// <summary>
        /// Baketa.Infrastructureとそれまでのモジュールのみを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="environment">アプリケーション実行環境</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddBaketaInfrastructureModules(
            this IServiceCollection services,
            BaketaEnvironment environment = BaketaEnvironment.Production)
        {
            return services.AddBaketaServices(
                false, 
                environment,
                new CoreModule(),
                new InfrastructureModule());
        }
        
        /// <summary>
        /// Baketa.Applicationとそれまでのモジュールのみを登録します。
        /// UI要素を含まないバージョンに便利です。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="environment">アプリケーション実行環境</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddBaketaApplicationModules(
            this IServiceCollection services,
            BaketaEnvironment environment = BaketaEnvironment.Production)
        {
            return services.AddBaketaServices(
                false, 
                environment,
                new CoreModule(),
                new InfrastructureModule(),
                new PlatformModule(),
                new ApplicationModule());
        }
        
        /// <summary>
        /// 開発環境用の標準モジュールを登録します。
        /// デバッグ機能や詳細なログ出力が有効になります。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="scanForModules">オプショナルなモジュールを自動スキャンするかどうか</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddBaketaDevelopmentModules(
            this IServiceCollection services,
            bool scanForModules = false)
        {
            return services.AddBaketaModules(scanForModules, BaketaEnvironment.Development);
        }
        
        /// <summary>
        /// テスト環境用の標準モジュールを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddBaketaTestModules(this IServiceCollection services)
        {
            return services.AddBaketaModules(false, BaketaEnvironment.Test);
        }
    }
