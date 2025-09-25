using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.Events.Implementation;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace Baketa.Core.DI.Modules;

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
        /// <param name="services">サービスコレクション</param>
        private static void RegisterEventAggregator(IServiceCollection services)
        {
            // イベント集約システムの登録
            services.AddEventAggregator();
            
            // 基本的なイベント処理用サービス
            // 例: services.AddTransient<IEventExceptionHandler, LoggingEventExceptionHandler>();
        }
        
        /// <summary>
        /// その他のコアサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterCoreServices(IServiceCollection services)
        {
            // ロギングサービスやその他の基本サービス
            // 例: services.AddSingleton<ISettingsManager, JsonSettingsManager>();
            // 例: services.AddSingleton<IPathProvider, AppDataPathProvider>();

            // 基本的なライフサイクル管理
            // 例: services.AddSingleton<IApplicationLifecycle, ApplicationLifecycle>();

            // 各種プロファイル管理
            // 例: services.AddSingleton<IProfileManager, ProfileManager>();

            // 🎯 [PHASE5] 優先度付きOCR完了ハンドラー - 画面中央優先度翻訳システム
            // 🚀 [DUPLICATE_FIX] TimedChunkAggregatorサービス統合対応 - DI登録を依存関係注入対応に更新
            services.AddSingleton<Baketa.Core.Events.Handlers.PriorityAwareOcrCompletedHandler>(provider =>
                new Baketa.Core.Events.Handlers.PriorityAwareOcrCompletedHandler(
                    provider.GetRequiredService<Baketa.Core.Abstractions.Events.IEventAggregator>(),
                    provider.GetRequiredService<Baketa.Core.Abstractions.Settings.IUnifiedSettingsService>(),
                    provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Baketa.Core.Events.Handlers.PriorityAwareOcrCompletedHandler>>(),
                    provider.GetRequiredService<Baketa.Core.Abstractions.Translation.ILanguageConfigurationService>(),
                    provider.GetRequiredService<Baketa.Core.Abstractions.Translation.ITextChunkAggregatorService>()));
            services.AddSingleton<Baketa.Core.Abstractions.Events.IEventProcessor<Baketa.Core.Events.EventTypes.OcrCompletedEvent>>(
                provider => provider.GetRequiredService<Baketa.Core.Events.Handlers.PriorityAwareOcrCompletedHandler>());
        }
    }
