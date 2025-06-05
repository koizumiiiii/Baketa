using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace Baketa.Application.DI.Modules;

    /// <summary>
    /// サンプルのコアモジュール実装。
    /// </summary>
    [ModulePriority(ModulePriority.Core)]
    public class SampleCoreModule : ServiceModuleBase
    {
        /// <summary>
        /// コアサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters",
            Justification = "サンプルコードに静的リソースを使用")]
        public override void RegisterServices(IServiceCollection services)
        {
            // ここで実際のサービス登録を行います
            // 例：services.AddSingleton<IEventAggregator, EventAggregator>();
            
            // サンプル用なのでログメッセージのみ出力
            Console.WriteLine(Resources.ModuleResources.SampleCoreModuleRegistered);
        }
    }
