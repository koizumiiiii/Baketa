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
        
        // 🎯 高機能版: キャッシュ対応OCRエンジンを最終IOcrEngine実装として登録
        // PooledOcrServiceをベースエンジンとして使用
        services.AddSingleton<CachedOcrEngine>(provider =>
        {
            // 高機能版のPooledOcrServiceを取得
            var baseEngine = provider.GetRequiredService<PooledOcrService>();
            var cacheService = provider.GetRequiredService<IAdvancedOcrCacheService>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedOcrEngine>>();
            
            Console.WriteLine($"🔄 CachedOcrEngine作成中... ベースエンジン: {baseEngine.GetType().Name}");
            return new CachedOcrEngine(baseEngine, cacheService, logger);
        });
        
        // 最終的にCachedOcrEngineをメインのIOcrEngineとして登録
        services.AddSingleton<IOcrEngine>(provider => 
            provider.GetRequiredService<CachedOcrEngine>());
        
        Console.WriteLine("✅ Step3: 高度キャッシング戦略登録完了");
        Console.WriteLine("🎯 期待効果: キャッシュヒット時 数ミリ秒応答");
    }
    
    /// <summary>
    /// このモジュールが依存する他のモジュールの型を取得します
    /// </summary>
    /// <returns>依存モジュールの型のコレクション</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        // Step2の段階的OCR戦略に依存
        yield return typeof(StagedOcrStrategyModule);
        
        // インフラストラクチャモジュールに依存
        yield return typeof(Baketa.Infrastructure.DI.Modules.InfrastructureModule);
    }
}
