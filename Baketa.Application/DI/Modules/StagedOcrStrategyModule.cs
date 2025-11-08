using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Services;
using Baketa.Application.Services;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Baketa.Infrastructure.OCR.PaddleOCR.Services;
using Baketa.Infrastructure.OCR.PaddleOCR.Factory;
using System;
using System.Collections.Generic;

namespace Baketa.Application.DI.Modules;

/// <summary>
/// Gemini推奨の段階的OCR戦略のDI登録モジュール
/// Step 2: シングルトン化 + 非同期バックグラウンド初期化
/// </summary>
[ModulePriority(ModulePriority.Core)] // 最高優先度 - Step2段階的戦略優先
public sealed class StagedOcrStrategyModule : ServiceModuleBase
{
    /// <summary>
    /// 段階的OCR戦略のサービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public override void RegisterServices(IServiceCollection services)
    {
        // 🎯 高機能版OCRスタック構成
        Console.WriteLine("🚀 HighPerformanceOcrModule.RegisterServices 実行中！");
        
        // [ROI_DELETION] ROI並列処理統合削除 - シンプルなPaddleOcrEngine登録に変更
        services.AddTransient<IOcrEngine>(provider =>
        {
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PaddleOcrEngine>>();
            var factory = provider.GetRequiredService<IPaddleOcrEngineFactory>();

            // 標準PaddleOcrEngineを作成
            var baseOcrEngine = factory.CreateAsync().GetAwaiter().GetResult();

            logger.LogInformation("✅ PaddleOcrEngine作成完了 - ROI機能除去済み");

            return baseOcrEngine;
        });
        
        // 🏊 PooledOcrService（並列処理対応）をシングルトン登録
        services.AddSingleton<PooledOcrService>(provider =>
        {
            var enginePool = provider.GetRequiredService<ObjectPool<IOcrEngine>>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PooledOcrService>>();
            
            var ocrSettings = provider.GetRequiredService<IOptionsMonitor<Baketa.Core.Settings.OcrSettings>>();
            return new PooledOcrService(enginePool, logger, ocrSettings);
        });
        
        // 💾 CachedOcrEngine（最上位キャッシュ層）をシングルトン登録
        services.AddSingleton<CachedOcrEngine>(provider =>
        {
            var pooledService = provider.GetRequiredService<PooledOcrService>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedOcrEngine>>();
            var cacheService = provider.GetRequiredService<Baketa.Core.Abstractions.Services.IAdvancedOcrCacheService>();
            
            return new CachedOcrEngine(pooledService, cacheService, logger);
        });
        
        // 🎯 メインのIOcrEngineとしてCachedOcrEngineを登録
        services.AddSingleton<IOcrEngine>(provider => 
            provider.GetRequiredService<CachedOcrEngine>());
    }
    
    /// <summary>
    /// このモジュールが依存する他のモジュールの型を取得します
    /// </summary>
    /// <returns>依存モジュールの型のコレクション</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        // インフラストラクチャモジュールに依存（PaddleOCRエンジンファクトリー等）
        yield return typeof(Baketa.Infrastructure.DI.Modules.InfrastructureModule);
    }
}