using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Infrastructure.Platform.DI.Modules;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace Baketa.Application.DI.Modules
{
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
            // OCRアプリケーションサービス
            RegisterOcrApplicationServices(services);
            
            // 翻訳アプリケーションサービス
            RegisterTranslationApplicationServices(services);
            
            // その他のアプリケーションサービス
            RegisterOtherApplicationServices(services);
            
            // イベントハンドラー
            RegisterEventHandlers(services);
        }

        /// <summary>
        /// OCRアプリケーションサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private void RegisterOcrApplicationServices(IServiceCollection services)
        {
            // OCR関連のアプリケーションサービス
            // 例: services.AddSingleton<IOcrService, OcrService>();
            
            // 現時点では実際の実装はプレースホルダー
        }
        
        /// <summary>
        /// 翻訳アプリケーションサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private void RegisterTranslationApplicationServices(IServiceCollection services)
        {
            // 翻訳関連のアプリケーションサービス
            // 例: services.AddSingleton<ITranslationService, TranslationService>();
            
            // 現時点では実際の実装はプレースホルダー
        }
        
        /// <summary>
        /// その他のアプリケーションサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private void RegisterOtherApplicationServices(IServiceCollection services)
        {
            // キャプチャサービスなど他のアプリケーションサービス
            // 例: services.AddSingleton<ICaptureService, CaptureService>();
            
            // 現時点では実際の実装はプレースホルダー
        }
        
        /// <summary>
        /// イベントハンドラーを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private void RegisterEventHandlers(IServiceCollection services)
        {
            // 各種イベントハンドラーの登録
            // 例: services.AddSingleton<ICaptureCompletedEventHandler, CaptureCompletedEventHandler>();
            
            // 現時点では実際の実装はプレースホルダー
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
}