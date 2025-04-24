using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Baketa.Core.DI
{
    /// <summary>
    /// サービス登録モジュールの基本実装。
    /// 共通の依存関係解決ロジックを提供します。
    /// </summary>
    public abstract class ServiceModuleBase : IServiceModule
    {
        /// <summary>
        /// このモジュールが提供するサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        public abstract void RegisterServices(IServiceCollection services);

        /// <summary>
        /// このモジュールが依存する他のモジュールの型を取得します。
        /// デフォルトでは依存モジュールはありません。
        /// </summary>
        /// <returns>依存モジュールの型のコレクション</returns>
        public virtual IEnumerable<Type> GetDependentModules() => [];

        /// <summary>
        /// 依存するモジュールを含めて全てのモジュールを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="registeredModules">登録済みモジュールの集合</param>
        /// <param name="moduleStack">現在の依存関係解決スタック（循環依存検出用）</param>
        public void RegisterWithDependencies(
            IServiceCollection services,
            HashSet<Type> registeredModules,
            Stack<Type> moduleStack)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));
            ArgumentNullException.ThrowIfNull(registeredModules, nameof(registeredModules));
            ArgumentNullException.ThrowIfNull(moduleStack, nameof(moduleStack));
            
            var moduleType = GetType();
            
            // 循環依存の検出
            if (moduleStack.Contains(moduleType))
            {
                // スタックのコピーを作成し、循環経路を正しい順序で取得
                var tempStack = new Stack<Type>(new Stack<Type>(moduleStack));
                var cycle = new List<Type>();
                
                // スタックをポップして循環経路を構築
                while (tempStack.Count > 0)
                {
                    var item = tempStack.Pop();
                    cycle.Add(item);
                    if (item == moduleType)
                        break;
                }
                
                // リストを反転して依存関係の順序を正しくする
                cycle.Reverse();
                // 完全な循環を表示するために追加
                cycle.Add(moduleType);
                
                var cycleInfo = string.Join(" -> ", cycle.Select(t => t.Name));
                throw new CircularDependencyException(
                    $"モジュール間の循環依存が検出されました: {cycleInfo}", cycle);
            }
            
            // このモジュールが既に登録済みの場合は何もしない
            if (registeredModules.Contains(moduleType))
            {
                return;
            }
            
            moduleStack.Push(moduleType);
            
            try
            {
                // 依存するモジュールを先に登録
                foreach (var dependentModuleType in GetDependentModules())
                {
                    if (registeredModules.Contains(dependentModuleType))
                    {
                        continue; // このモジュールは既に登録済み
                    }
                    
                    if (Activator.CreateInstance(dependentModuleType) is IServiceModule dependentModule)
                    {
                        if (dependentModule is ServiceModuleBase moduleBase)
                        {
                            // 依存モジュールも依存性を持つ場合は再帰的に登録
                            moduleBase.RegisterWithDependencies(services, registeredModules, moduleStack);
                        }
                        else
                        {
                            // 通常のモジュールの場合は直接登録
                            dependentModule.RegisterServices(services);
                            registeredModules.Add(dependentModuleType);
                        }
                    }
                }
                
                // このモジュールを登録
                RegisterServices(services);
                registeredModules.Add(moduleType);
            }
            finally
            {
                moduleStack.Pop();
            }
        }
    }
}