using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
// パッケージ追加済みのため使用可能
using Microsoft.Extensions.Logging.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Baketa.Application.DI.Extensions
{
    /// <summary>
    /// サービスコレクション拡張メソッド。
    /// Baketaサービスモジュールの登録を簡略化します。
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Baketaの全サービスモジュールを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="scanForModules">アセンブリスキャンによるモジュール自動検出を有効にするかどうか</param>
        /// <param name="environment">アプリケーション実行環境</param>
        /// <param name="customModules">追加の手動登録モジュール</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddBaketaServices(
            this IServiceCollection services,
            bool scanForModules = false,
            BaketaEnvironment environment = BaketaEnvironment.Production,
            params IServiceModule[] customModules)
        {
            // 環境設定をサービスコンテナに登録
            // enum型はサービスとして登録できないので設定クラスを使用
            services.AddSingleton(new BaketaEnvironmentSettings { Environment = environment });
            
            // 環境に応じた設定
            ConfigureEnvironmentSpecificServices(services, environment);
            
            // カスタムモジュールと基本モジュールを統合
            var modules = customModules.ToList();
            
            // スキャンによるモジュール検出（オプション）
            if (scanForModules)
            {
                var scannedModules = DiscoverModules();
                modules.AddRange(scannedModules);
            }
            
            // 優先度でソート
            var sortedModules = modules
                .Select(m => new 
                {
                    Module = m,
                    Priority = m.GetType().GetCustomAttribute<ModulePriorityAttribute>()?.Priority 
                              ?? ModulePriority.Custom
                })
                .OrderByDescending(x => (int)x.Priority)
                .Select(x => x.Module)
                .ToArray();
            
            // 登録
            RegisterModules(services, sortedModules);
            
            return services;
        }
        
        /// <summary>
        /// 環境固有のサービス設定を行います。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="environment">アプリケーション実行環境</param>
        private static void ConfigureEnvironmentSpecificServices(
            IServiceCollection services, 
            BaketaEnvironment environment)
        {
            switch (environment)
            {
                case BaketaEnvironment.Development:
                    // 開発環境固有の設定
                    // ロギング設定は各レイヤーで個別に設定
                    ConfigureLoggingForDevelopment(services);
                    break;
                    
                case BaketaEnvironment.Test:
                    // テスト環境固有の設定
                    ConfigureLoggingForTest(services);
                    break;
                    
                case BaketaEnvironment.Production:
                default:
                    // 本番環境固有の設定
                    ConfigureLoggingForProduction(services);
                    break;
            }
        }
        
        /// <summary>
        /// 開発環境用のロギング設定
        /// </summary>
        private static void ConfigureLoggingForDevelopment(IServiceCollection services)
        {
            // 開発環境ではDebugレベルからログを出力
            services.AddLogging(builder => 
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddConsole();
            });
        }
        
        /// <summary>
        /// テスト環境用のロギング設定
        /// </summary>
        private static void ConfigureLoggingForTest(IServiceCollection services)
        {
            // テスト環境ではInformationレベルからログを出力
            services.AddLogging(builder => 
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddConsole();
            });
        }
        
        /// <summary>
        /// 本番環境用のロギング設定
        /// </summary>
        private static void ConfigureLoggingForProduction(IServiceCollection services)
        {
            // 本番環境ではWarningレベルからログを出力
            services.AddLogging(builder => 
            {
                builder.SetMinimumLevel(LogLevel.Warning);
                builder.AddConsole();
            });
        }
        
        /// <summary>
        /// モジュールを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="modules">登録するモジュール</param>
        private static void RegisterModules(IServiceCollection services, IEnumerable<IServiceModule> modules)
        {
            var registeredModules = new HashSet<Type>();
            // 未使用変数を削除
            
            foreach (var module in modules)
            {
                if (module is ServiceModuleBase moduleBase)
                {
                    // ServiceModuleBase を継承している場合は依存関係を考慮して登録
                    moduleBase.RegisterWithDependencies(services, registeredModules, new Stack<Type>());
                }
                else
                {
                    // 通常のモジュールは直接登録
                    if (!registeredModules.Contains(module.GetType()))
                    {
                        module.RegisterServices(services);
                        registeredModules.Add(module.GetType());
                    }
                }
            }
            
            // オプショナル: 登録されたモジュールをデバッグログに出力
            LogRegisteredModules(services, registeredModules);
        }
        
        /// <summary>
        /// 登録されたモジュールをログに出力します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="registeredModules">登録されたモジュールの型</param>
        private static void LogRegisteredModules(
            IServiceCollection services, 
            HashSet<Type> registeredModules)
        {
            try {
                var serviceProvider = services.BuildServiceProvider();
                var logger = serviceProvider.GetService<ILogger<IServiceModule>>();
                
                if (logger != null)
                {
                    logger.LogDebug("登録されたモジュール: {RegisteredModules}", 
                        string.Join(", ", registeredModules.Select(t => t.Name)));
                    
                    // 詳細なサービス登録情報（デバッグ用）
                    if (logger.IsEnabled(LogLevel.Trace))
                    {
                        // ディスカード変数を使用して警告を抑制
                        var _ = services
                            .Select(s => $"{s.ServiceType.Name} => {s.ImplementationType?.Name ?? "Factory"} ({s.Lifetime})")
                            .ToList();
                            
                        logger.LogTrace("登録されたサービス: {ServiceCount}個", services.Count);
                        
                        // 必要な場合はクエリを再実行
                        foreach (var service in services.Select(s => 
                            $"{s.ServiceType.Name} => {s.ImplementationType?.Name ?? "Factory"} ({s.Lifetime})"))
                        {
                            logger.LogTrace("  {Service}", service);
                        }
                    }
                }
            } catch (Exception ex) {
                // ロギングに失敗しても処理を継続
                Console.WriteLine($"モジュール登録のログ出力に失敗: {ex.Message}");
            }
        }
        
        /// <summary>
        /// アセンブリから自動登録対象のモジュールを検出します。
        /// </summary>
        /// <returns>検出されたモジュール</returns>
        private static IEnumerable<IServiceModule> DiscoverModules()
        {
            // アプリケーションドメイン内のアセンブリから IServiceModule 実装を検索
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(IServiceModule).IsAssignableFrom(t) &&
                           !t.IsAbstract &&
                           !t.IsInterface &&
                           t.GetCustomAttribute<AutoRegisterAttribute>() != null)
                .Select(t => Activator.CreateInstance(t) as IServiceModule)
                .Where(m => m != null)!;
        }
    }
}