using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace Baketa.Application.DI.Modules;

    /// <summary>
    /// サンプルのUIモジュール実装。
    /// </summary>
    [ModulePriority(ModulePriority.UI)]
    [AutoRegister] // 自動検出の対象としてマーク
    public class SampleUIModule : ServiceModuleBase
    {
        /// <summary>
        /// UIサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters",
            Justification = "サンプルコードに静的リソースを使用")]
        public override void RegisterServices(IServiceCollection services)
        {
            // ここで実際のサービス登録を行います
            // 例：services.AddTransient<MainViewModel>();
            
            // サンプル用なのでログメッセージのみ出力
            Console.WriteLine(Resources.ModuleResources.SampleUIModuleRegistered);
        }
        
        /// <summary>
        /// このモジュールが依存するモジュールを取得します。
        /// </summary>
        /// <returns>依存モジュールの型</returns>
        public override IEnumerable<Type> GetDependentModules()
        {
            yield return typeof(SampleApplicationModule);
            // 間接的に他のすべてのモジュールにも依存しています
        }
    }
