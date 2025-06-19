using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.ReactiveUI;
using Baketa.Application.DI.Modules;
using Baketa.Core.DI;
using Baketa.Core.DI.Modules;
using Baketa.UI.DI.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Baketa.UI;

    internal sealed class Program
    {
        /// <summary>
        /// DIコンテナとサービスプロバイダー
        /// </summary>
        public static ServiceProvider? ServiceProvider { get; private set; }
        
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // DIコンテナの初期化
            ConfigureServices();
            
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .UseReactiveUI();
                
        /// <summary>
        /// DIコンテナを構成します。
        /// </summary>
        private static void ConfigureServices()
        {
            // 環境の検出
            var environment = Debugger.IsAttached 
                ? BaketaEnvironment.Development 
                : BaketaEnvironment.Production;
            
            // 設定ファイルの読み込み
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{(environment == BaketaEnvironment.Development ? "Development" : "Production")}.json", optional: true, reloadOnChange: true)
                .Build();
            
            // DIコンテナの構成
            var services = new ServiceCollection();
            
            // Configurationを登録
            services.AddSingleton<IConfiguration>(configuration);
            
            // appsettings.jsonから設定を読み込み
            services.Configure<Baketa.UI.Services.TranslationEngineStatusOptions>(
                configuration.GetSection("TranslationEngineStatus"));
            
            // ロギングの設定
            services.AddLogging(builder => 
            {
                builder.AddConsole();
                
                // 環境に応じたログレベル設定
                if (environment == BaketaEnvironment.Development)
                {
                    // 開発環境では詳細なログを有効化
                    builder.SetMinimumLevel(LogLevel.Debug);
                }
                else
                {
                    // 本番環境では必要最低限のログのみ
                    builder.SetMinimumLevel(LogLevel.Information);
                }
            });
            
            // Baketaの標準モジュールを登録
            // Coreモジュールの登録
            var coreModule = new CoreModule();
            var registeredModules = new HashSet<Type>();
            var moduleStack = new Stack<Type>();
            coreModule.RegisterWithDependencies(services, registeredModules, moduleStack);
            
            // ApplicationModuleの明示的登録
            var applicationModule = new Baketa.Application.DI.Modules.ApplicationModule();
            applicationModule.RegisterWithDependencies(services, registeredModules, moduleStack);
            
            // UIモジュールの登録
            var uiModule = new UIModule();
            uiModule.RegisterWithDependencies(services, registeredModules, moduleStack);
            
            // サービスプロバイダーの構築
            ServiceProvider = services.BuildServiceProvider();
            
            // アプリケーション起動完了後にサービスを開始（App.axaml.csで実行）
        }
    }
