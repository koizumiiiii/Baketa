using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        /// <param name="customModules">追加の手動登録モジュール</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddBaketaServices(
            this IServiceCollection services,
            bool scanForModules = false,
            params IServiceModule[] customModules)
        {
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
        /// モジュールを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="modules">登録するモジュール</param>
        private static void RegisterModules(IServiceCollection services, IEnumerable<IServiceModule> modules)
        {
            var registeredModules = new HashSet<Type>();
            
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
            try {
                var serviceProvider = services.BuildServiceProvider();
                var logger = serviceProvider.GetService<ILogger<IServiceModule>>();
                logger?.LogDebug("登録されたモジュール: {RegisteredModules}", 
                    string.Join(", ", registeredModules.Select(t => t.Name)));
            } catch (Exception) {
                // ロギングに失敗しても処理を継続
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