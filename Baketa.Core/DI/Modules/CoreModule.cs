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
            
            // Note: 現時点ではAddLogging()拡張メソッドが存在しないか、
            // Microsoft.Extensions.Loggingパッケージが必要なためコメントアウト
            // services.AddLogging();
        }

        /// <summary>
        /// コアの抽象化（インターフェース）を登録します。
        /// </summary>
        /// <param name="_">サービスコレクション</param>
        private static void RegisterAbstractions(IServiceCollection _)
        {
            // ファクトリーやインターフェースの登録
            // 実際の実装はプロジェクトの状況に応じて追加
            // 例: services.AddSingleton<IImageFactory, DefaultImageFactory>();
            
            // ヘルパーと共通実装
            // 例: services.AddTransient<IJsonSerializer, SystemTextJsonSerializer>();
        }
        
        /// <summary>
        /// イベント集約システムを登録します。
        /// </summary>
        /// <param name="_">サービスコレクション</param>
        private static void RegisterEventAggregator(IServiceCollection _)
        {
            // イベント集約システムの登録
            // 例: services.AddSingleton<IEventAggregator, EventAggregator>();
            // 例: services.AddSingleton<IEventDispatcher, EventDispatcher>();
            
            // 基本的なイベント処理用サービス
            // 例: services.AddTransient<IEventExceptionHandler, LoggingEventExceptionHandler>();
        }
        
        /// <summary>
        /// その他のコアサービスを登録します。
        /// </summary>
        /// <param name="_">サービスコレクション</param>
        private static void RegisterCoreServices(IServiceCollection _)
        {
            // ロギングサービスやその他の基本サービス
            // 例: services.AddSingleton<ISettingsManager, JsonSettingsManager>();
            // 例: services.AddSingleton<IPathProvider, AppDataPathProvider>();
            
            // 基本的なライフサイクル管理
            // 例: services.AddSingleton<IApplicationLifecycle, ApplicationLifecycle>();
            
            // 各種プロファイル管理
            // 例: services.AddSingleton<IProfileManager, ProfileManager>();
        }
    }
}