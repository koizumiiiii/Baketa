using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace Baketa.Infrastructure.DI.Modules
{
    /// <summary>
    /// インフラストラクチャレイヤーのサービスを登録するモジュール。
    /// 外部サービス連携やプラットフォーム非依存の実装が含まれます。
    /// </summary>
    [ModulePriority(ModulePriority.Infrastructure)]
    public class InfrastructureModule : ServiceModuleBase
    {
        /// <summary>
        /// インフラストラクチャサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        public override void RegisterServices(IServiceCollection services)
        {
            // OCR関連サービス
            RegisterOcrServices(services);
            
            // 翻訳サービス
            RegisterTranslationServices(services);
            
            // データ永続化
            RegisterPersistenceServices(services);
        }

        /// <summary>
        /// OCR関連サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private void RegisterOcrServices(IServiceCollection services)
        {
            // OCRエンジンやプロセッサーの登録
            // 例: services.AddSingleton<IOcrEngine, PaddleOcrEngine>();
            
            // 現時点では実際の実装はプレースホルダー
        }
        
        /// <summary>
        /// 翻訳サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private void RegisterTranslationServices(IServiceCollection services)
        {
            // 翻訳エンジンやサービスの登録
            // 例: services.AddSingleton<ITranslationEngine, OnnxTranslationEngine>();
            
            // 現時点では実際の実装はプレースホルダー
        }
        
        /// <summary>
        /// データ永続化サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private void RegisterPersistenceServices(IServiceCollection services)
        {
            // 設定保存やキャッシュサービスの登録
            // 例: services.AddSingleton<ISettingsStorage, JsonSettingsStorage>();
            
            // 現時点では実際の実装はプレースホルダー
        }
        
        /// <summary>
        /// このモジュールが依存する他のモジュールの型を取得します。
        /// </summary>
        /// <returns>依存モジュールの型のコレクション</returns>
        public override IEnumerable<Type> GetDependentModules()
        {
            yield return typeof(CoreModule);
        }
    }
}