using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI.Attributes;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace Baketa.Core.DI.Modules
{
    /// <summary>
    /// コアレイヤーのサービスを登録するモジュール。
    /// 最も基本的なサービスとインターフェースが含まれます。
    /// </summary>
    [ModulePriority(ModulePriority.Core)]
    public class CoreModule : ServiceModuleBase
    {
        /// <summary>
        /// コアサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        public override void RegisterServices(IServiceCollection services)
        {
            // コアインターフェースと抽象化の登録
            RegisterAbstractions(services);
            
            // イベント集約システム
            RegisterEventAggregator(services);
            
            // その他のコアサービス
            RegisterCoreServices(services);
        }

        /// <summary>
        /// コアの抽象化（インターフェース）を登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private void RegisterAbstractions(IServiceCollection services)
        {
            // ファクトリーやインターフェースの登録
            // 例: services.AddSingleton<IImageFactory, DefaultImageFactory>();
            
            // 現時点では実際の実装はプレースホルダー
        }
        
        /// <summary>
        /// イベント集約システムを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private void RegisterEventAggregator(IServiceCollection services)
        {
            // イベント集約システムの登録
            // 例: services.AddSingleton<IEventAggregator, EventAggregator>();
            
            // 現時点では実際の実装はプレースホルダー
        }
        
        /// <summary>
        /// その他のコアサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private void RegisterCoreServices(IServiceCollection services)
        {
            // ロギングサービスやその他の基本サービス
            // 例: services.AddSingleton<ISettingsManager, SettingsManager>();
            
            // 現時点では実際の実装はプレースホルダー
        }
    }
}