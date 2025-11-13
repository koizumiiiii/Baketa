using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Services;
using Baketa.Application.Services;
using Baketa.Application.Services.Cache;
using Baketa.Infrastructure.OCR.PaddleOCR.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Baketa.Application.DI.Modules;

/// <summary>
/// Step3: Gemini推奨高度キャッシング戦略のDI登録モジュール
/// 数ミリ秒OCR応答の実現のためのキャッシング機能統合
/// </summary>
[ModulePriority(ModulePriority.Core)] // 最高優先度 - Step3キャッシング戦略
public sealed class AdvancedCachingModule : ServiceModuleBase
{
    /// <summary>
    /// 高度キャッシング戦略のサービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public override void RegisterServices(IServiceCollection services)
    {
        // 🚨 DEBUG: モジュール実行確認
        Console.WriteLine("🚀 AdvancedCachingModule.RegisterServices 実行中！");
        
        // ⚡ Step3: 高度キャッシングサービス登録
        services.AddSingleton<IAdvancedOcrCacheService, AdvancedOcrCacheService>();
        Console.WriteLine("✅ IAdvancedOcrCacheService登録完了");
        
        /*
        // 🏭 ファクトリパターン対応のため、古い登録は無効化
        services.AddSingleton<CachedOcrEngine>(provider =>
        {
            var baseEngine = provider.GetRequiredService<IOcrEngine>(); // これは循環参照を引き起こす
            var cacheService = provider.GetRequiredService<IAdvancedOcrCacheService>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedOcrEngine>>();
            
            Console.WriteLine($"🏭 CachedOcrEngine作成中... （旧実装）");
            return new CachedOcrEngine(baseEngine, cacheService, logger);
        });
        */

        // 🚀 Gemini推奨: デコレーターパターンによる正しいキャッシング実装
        // 1. ベースとなるプール化サービスを具体的な型で登録
        services.AddSingleton<PooledOcrService>();
        Console.WriteLine("✅ PooledOcrServiceを具体的な型で登録完了");

        // 2. キャッシュエンジン（デコレーター）を具体的な型で登録
        //    ベースとなるPooledOcrServiceをコンストラクタで受け取る
        services.AddSingleton<CachedOcrEngine>(provider =>
        {
            var pooledService = provider.GetRequiredService<PooledOcrService>();
            var cacheService = provider.GetRequiredService<IAdvancedOcrCacheService>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedOcrEngine>>();
            
            Console.WriteLine($"✅ CachedOcrEngine（デコレーター）作成 - ベースサービス: {pooledService.GetType().Name}");
            return new CachedOcrEngine(pooledService, cacheService, logger);
        });
        Console.WriteLine("✅ CachedOcrEngineを具体的な型で登録完了");

        // 3. IOcrEngineインターフェースへの要求を、最終的なキャッシュエンジン実装に解決
        //    これにより、IOcrEngineを要求する全てのサービスがキャッシュ機能の恩恵を受ける
        services.AddSingleton<IOcrEngine>(provider => provider.GetRequiredService<CachedOcrEngine>());
        Console.WriteLine("✅ IOcrEngineをCachedOcrEngineに解決するよう最終登録完了");
        
        Console.WriteLine("✅ Step3: 高度キャッシング戦略登録完了");
        Console.WriteLine("🎯 期待効果: キャッシュヒット時 数ミリ秒応答");
    }
    
    /// <summary>
    /// このモジュールが依存する他のモジュールの型を取得します
    /// </summary>
    /// <returns>依存モジュールの型のコレクション</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        // ❌ 旧プール化システム依存を除去
        // yield return typeof(StagedOcrStrategyModule);
        
        // インフラストラクチャモジュールに依存（新ファクトリシステム）
        yield return typeof(Baketa.Infrastructure.DI.Modules.InfrastructureModule);
        
        // 🏭 新しいPaddleOcrModuleに依存（ファクトリシステム）
        yield return typeof(Baketa.Infrastructure.DI.PaddleOcrModule);
    }
}
