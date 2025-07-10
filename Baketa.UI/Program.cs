using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.ReactiveUI;
using Baketa.Application.DI.Modules;
using Baketa.Core.DI;
using Baketa.Core.DI.Modules;
using Baketa.Infrastructure.DI.Modules;
using Baketa.Infrastructure.Platform.DI;
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
            // ファイル出力で確実にログを残す
            try
            {
                File.WriteAllText("debug_startup.txt", $"🚀 Baketa.UI.exe 起動開始 - {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n");
            }
            catch { /* ファイル出力失敗は無視 */ }
            
            Console.WriteLine("🚀 Baketa.UI.exe 起動開始");
            System.Diagnostics.Debug.WriteLine("🚀 Baketa.UI.exe 起動開始");
            
            // 未処理例外の強制ログ出力
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => 
            {
                Console.WriteLine($"💥 FATAL: 未処理例外: {e.ExceptionObject}");
                System.Diagnostics.Debug.WriteLine($"💥 FATAL: 未処理例外: {e.ExceptionObject}");
                if (e.ExceptionObject is Exception ex)
                {
                    Console.WriteLine($"💥 FATAL: Exception Type: {ex.GetType().Name}");
                    Console.WriteLine($"💥 FATAL: Message: {ex.Message}");
                    Console.WriteLine($"💥 FATAL: StackTrace: {ex.StackTrace}");
                    System.Diagnostics.Debug.WriteLine($"💥 FATAL: Exception Type: {ex.GetType().Name}");
                    System.Diagnostics.Debug.WriteLine($"💥 FATAL: Message: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"💥 FATAL: StackTrace: {ex.StackTrace}");
                }
            };
            
            try
            {
                Console.WriteLine("🔧 DIコンテナの初期化開始");
                System.Diagnostics.Debug.WriteLine("🔧 DIコンテナの初期化開始");
                
                // DIコンテナの初期化
                ConfigureServices();
                
                Console.WriteLine("🎯 Avalonia アプリケーション開始");
                System.Diagnostics.Debug.WriteLine("🎯 Avalonia アプリケーション開始");
                
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 MAIN EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"💥 MAIN STACK: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"💥 MAIN EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"💥 MAIN STACK: {ex.StackTrace}");
                throw;
            }
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
            Console.WriteLine("🔍 ConfigureServices開始");
            System.Diagnostics.Debug.WriteLine("🔍 ConfigureServices開始");
            
            // 環境の検出
            var environment = Debugger.IsAttached 
                ? BaketaEnvironment.Development 
                : BaketaEnvironment.Production;
            
            Console.WriteLine($"🌍 環境: {environment}");
            System.Diagnostics.Debug.WriteLine($"🌍 環境: {environment}");
            
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
            
            // 設定システムを登録（ISettingsServiceを提供）
            services.AddSettingsSystem();
            
            // InfrastructureModuleの登録
            var infrastructureModule = new InfrastructureModule();
            infrastructureModule.RegisterWithDependencies(services, registeredModules, moduleStack);
            
            // PlatformModuleの登録
            var platformModule = new Baketa.Infrastructure.Platform.DI.Modules.PlatformModule();
            platformModule.RegisterWithDependencies(services, registeredModules, moduleStack);
            
            // AuthModuleの登録（InfrastructureレイヤーのAuthサービス）
            var authModule = new AuthModule();
            authModule.RegisterWithDependencies(services, registeredModules, moduleStack);
            
            // ApplicationModuleの明示的登録
            var applicationModule = new Baketa.Application.DI.Modules.ApplicationModule();
            applicationModule.RegisterWithDependencies(services, registeredModules, moduleStack);
            
            // UIモジュールの登録
            var uiModule = new UIModule();
            uiModule.RegisterWithDependencies(services, registeredModules, moduleStack);
            
            // アダプターサービスの登録
            services.AddAdapterServices();
            
            // DI登録デバッグ
            DebugServiceRegistration(services);
            
            // さらに詳細なDI診断
            DebugViewModelRegistration(services);
            
            // サービスプロバイダーの構築
            Console.WriteLine("🏗️ ServiceProvider構築開始");
            System.Diagnostics.Debug.WriteLine("🏗️ ServiceProvider構築開始");
            ServiceProvider = services.BuildServiceProvider();
            Console.WriteLine("✅ ServiceProvider構築完了");
            System.Diagnostics.Debug.WriteLine("✅ ServiceProvider構築完了");
            
            // アプリケーション起動完了後にサービスを開始（App.axaml.csで実行）
        }
        
        /// <summary>
        /// DI登録状況をデバッグします
        /// </summary>
        private static void DebugServiceRegistration(IServiceCollection services)
        {
            System.Console.WriteLine("=== DI Service Registration Debug ===");
            
            // ISettingsServiceの登録確認
            var settingsServices = services.Where(s => s.ServiceType == typeof(Baketa.Core.Services.ISettingsService));
            System.Console.WriteLine($"ISettingsService registrations count: {settingsServices.Count()}");
            
            foreach (var service in settingsServices)
            {
                System.Console.WriteLine($"  - ServiceType: {service.ServiceType.Name}");
                System.Console.WriteLine($"  - ImplementationType: {service.ImplementationType?.Name ?? "N/A"}");
                System.Console.WriteLine($"  - Lifetime: {service.Lifetime}");
                System.Console.WriteLine($"  - ImplementationFactory: {(service.ImplementationFactory != null ? "Yes" : "No")}");
            }
            
            // AccessibilitySettingsViewModelの登録確認
            var accessibilityVM = services.Where(s => s.ServiceType == typeof(Baketa.UI.ViewModels.AccessibilitySettingsViewModel));
            System.Console.WriteLine($"AccessibilitySettingsViewModel registrations count: {accessibilityVM.Count()}");
        }
        
        /// <summary>
        /// ViewModelのDI登録詳細を確認します
        /// </summary>
        private static void DebugViewModelRegistration(IServiceCollection services)
        {
            System.Console.WriteLine("=== ViewModel Registration Debug ===");
            
            var viewModelTypes = new[]
            {
                typeof(Baketa.UI.ViewModels.AccessibilitySettingsViewModel),
                typeof(Baketa.UI.ViewModels.SettingsViewModel),
                typeof(Baketa.UI.ViewModels.LanguagePairsViewModel),
                typeof(Baketa.UI.ViewModels.MainWindowViewModel)
            };
            
            foreach (var vmType in viewModelTypes)
            {
                var registrations = services.Where(s => s.ServiceType == vmType);
                System.Console.WriteLine($"{vmType.Name}: {registrations.Count()} registration(s)");
                
                foreach (var reg in registrations)
                {
                    System.Console.WriteLine($"  - Lifetime: {reg.Lifetime}");
                    System.Console.WriteLine($"  - ImplementationType: {reg.ImplementationType?.Name ?? "Factory"}");
                }
            }
        }
    }
