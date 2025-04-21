using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.ReactiveUI;
using Baketa.Application.DI.Extensions;
using Baketa.Core.DI;
using Baketa.UI.DI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Baketa.UI
{
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
            
            // DIコンテナの構成
            var services = new ServiceCollection();
            
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
            // UIモジュールを含む全モジュールを登録
            services.AddUIModule(environment);
            
            // サービスプロバイダーの構築
            ServiceProvider = services.BuildServiceProvider();
            
            // アプリケーション起動完了後にサービスを開始（App.axaml.csで実行）
        }
    }
}
