using Baketa.Core.Abstractions.DI;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Infrastructure.Platform.DI.Modules;
using Baketa.Application.DI.Modules;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using Baketa.Core.Abstractions.Services;
using Baketa.Application.Services.Capture;
using Baketa.Core.Events.Implementation;
using EventAggregatorImpl = Baketa.Core.Events.Implementation.EventAggregator;

namespace Baketa.Application.DI.Modules;

    /// <summary>
    /// アプリケーションレイヤーのサービスを登録するモジュール。
    /// ビジネスロジックやユースケースの実装が含まれます。
    /// </summary>
    [ModulePriority(ModulePriority.Application)]
    public class ApplicationModule : ServiceModuleBase
    {
        /// <summary>
        /// アプリケーションサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        public override void RegisterServices(IServiceCollection services)
        {
            // 環境設定は、BuildServiceProviderが存在しないか必要なパッケージがないため
            // コメントアウトし、デフォルト値を使用
            //var environment = services.BuildServiceProvider().GetService<Core.DI.BaketaEnvironment>() 
            //    ?? Core.DI.BaketaEnvironment.Production;
            var environment = Core.DI.BaketaEnvironment.Production;
            
            // OCRアプリケーションサービス
            RegisterOcrApplicationServices(services);
            
            // 翻訳アプリケーションサービス
            RegisterTranslationApplicationServices(services);
            
            // その他のアプリケーションサービス
            RegisterOtherApplicationServices(services, environment);
            
            // イベントハンドラー
            RegisterEventHandlers(services);
        }

        /// <summary>
        /// OCRアプリケーションサービスを登録します。
        /// </summary>
        /// <param name="_">サービスコレクション</param>
        private static void RegisterOcrApplicationServices(IServiceCollection _)
        {
            // OCR関連のアプリケーションサービス
            // 例: services.AddSingleton<IOcrService, OcrService>();
            // 例: services.AddSingleton<IOcrProfileService, OcrProfileService>();
            // 例: services.AddSingleton<IOcrConfigurationService, OcrConfigurationService>();
            
            // OCR結果処理サービス
            // 例: services.AddSingleton<IOcrResultProcessor, OcrResultProcessor>();
            // 例: services.AddSingleton<IOcrTextFormatter, OcrTextFormatter>();
        }
        
        /// <summary>
        /// 翻訳アプリケーションサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterTranslationApplicationServices(IServiceCollection services)
        {
            // 翻訳統合サービス（IEventAggregatorの依存を削除）
            services.AddSingleton<Baketa.Application.Services.Translation.TranslationOrchestrationService>();
            services.AddSingleton<Baketa.Application.Services.Translation.ITranslationOrchestrationService>(
                provider => provider.GetRequiredService<Baketa.Application.Services.Translation.TranslationOrchestrationService>());
            
            // 翻訳関連のアプリケーションサービス（将来拡張）
            // 例: services.AddSingleton<ITranslationService, TranslationService>();
            // 例: services.AddSingleton<ITranslationProfileService, TranslationProfileService>();
            // 例: services.AddSingleton<ILanguageService, LanguageService>();
            
            // 翻訳カスタマイズ（将来拡張）
            // 例: services.AddSingleton<IDictionaryService, DictionaryService>();
            // 例: services.AddSingleton<ITextReplacementService, TextReplacementService>();
        }
        
        /// <summary>
        /// その他のアプリケーションサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="environment">アプリケーション実行環境</param>
        private static void RegisterOtherApplicationServices(IServiceCollection services, Core.DI.BaketaEnvironment environment)
        {
            // イベント集約機構の登録
            RegisterEventAggregator(services);
            
            // キャプチャサービスの登録
            RegisterCaptureServices(services);
            
            // フルスクリーン管理サービス
            services.AddFullscreenManagement();
            
            // 統合サービス
            // 例: services.AddSingleton<ITranslationIntegrationService, TranslationIntegrationService>();
            
            // テキスト処理
            // 例: services.AddSingleton<ITextAnalysisService, TextAnalysisService>();
            
            // デバッグサービス（開発環境のみ）
            if (environment == Core.DI.BaketaEnvironment.Development)
            {
                // 例: services.AddSingleton<IDevelopmentService, DevelopmentService>();
                // 例: services.AddSingleton<IDebugConsoleService, DebugConsoleService>();
            }
        }
        
        /// <summary>
        /// イベント集約機構を登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterEventAggregator(IServiceCollection services)
        {
            // メインのイベント集約機構を登録
            services.AddSingleton<Baketa.Core.Abstractions.Events.IEventAggregator, EventAggregatorImpl>();
                
            // イベントプロセッサー自動登録サービス
            services.AddSingleton<Baketa.Application.Services.Events.EventProcessorRegistrationService>();
            services.AddHostedService<Baketa.Application.Services.Events.EventProcessorRegistrationService>();
        }
        
        /// <summary>
        /// キャプチャサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterCaptureServices(IServiceCollection services)
        {
            // キャプチャサービスの実装を登録
            services.AddSingleton<AdvancedCaptureService>();
            
            // 両方のインターフェースが同じインスタンスを参照するように設定
            services.AddSingleton<ICaptureService>(provider => provider.GetRequiredService<AdvancedCaptureService>());
            services.AddSingleton<IAdvancedCaptureService>(provider => provider.GetRequiredService<AdvancedCaptureService>());
            
            // ゲームプロファイル管理サービスの登録
            services.AddSingleton<IGameProfileManager, GameProfileManager>();
            
            // ゲーム自動検出サービスの登録
            services.AddSingleton<IGameDetectionService, GameDetectionService>();
        }
        
        /// <summary>
        /// イベントハンドラーを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterEventHandlers(IServiceCollection services)
        {
            // 翻訳モード変更イベントプロセッサー
            services.AddSingleton<Baketa.Application.Events.Processors.TranslationModeChangedEventProcessor>();
            
            // 他のイベントハンドラーの登録
            // 例: services.AddSingleton<CaptureCompletedEventHandler>();
            
            // 自動登録が必要な場合は必要に応じて実装
            // RegisterEventHandlersAutomatically(services);
        }
        
        /// <summary>
        /// イベントハンドラーを反射を使用して自動的に登録します。
        /// </summary>
        /// <param name="_">サービスコレクション</param>
        private static void RegisterEventHandlersAutomatically(IServiceCollection _)
        {
            // 現在は実装が必要なインターフェースが存在しないため、
            // 必要に応じて実装を追加してください。
            // 
            // 例: アセンブリからイベントハンドラーを探して登録するコード
            // var handlerTypes = typeof(ApplicationModule).Assembly
            //     .GetTypes()
            //     .Where(t => t.Namespace?.StartsWith("Baketa.Application.Handlers") == true
            //             && !t.IsInterface
            //             && !t.IsAbstract
            //             && t.GetInterfaces().Any(i => i.IsGenericType 
            //                 && i.GetGenericTypeDefinition() == typeof(IEventHandler<>)));
        }
        
        /// <summary>
        /// このモジュールが依存する他のモジュールの型を取得します。
        /// </summary>
        /// <returns>依存モジュールの型のコレクション</returns>
        public override IEnumerable<Type> GetDependentModules()
        {
            yield return typeof(CoreModule);
            yield return typeof(PlatformModule);
            // 現時点ではInfrastructureModuleは参照できない
        }
    }
