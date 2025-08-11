using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.Abstractions.OCR;
using Baketa.Application.Services;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
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
        // 🚨 DEBUG: モジュール実行確認
        Console.WriteLine("🚀 StagedOcrStrategyModule.RegisterServices 実行中！");
        // 🔥 Geminiの推奨アプローチ: IHostedServiceによるバックグラウンド初期化
        services.AddSingleton<OcrEngineInitializerService>();
        services.AddHostedService<OcrEngineInitializerService>(provider => 
            provider.GetRequiredService<OcrEngineInitializerService>());
        
        // 🚀 高速エンジン（即座に利用可能）をファクトリー登録
        services.AddTransient<IOcrEngine>(provider =>
        {
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PaddleOcrEngine>>();
            var modelPathResolver = provider.GetRequiredService<IModelPathResolver>();
            
            // SafePaddleOcrEngineは5ms初期化で即座に利用可能
            return new SafePaddleOcrEngine(modelPathResolver, logger, skipRealInitialization: false);
        });
        
        // ⚡ CompositeOcrEngine（段階的戦略の中核）をシングルトン登録
        services.AddSingleton<CompositeOcrEngine>(provider =>
        {
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CompositeOcrEngine>>();
            var fastEngine = provider.GetRequiredService<IOcrEngine>();
            var heavyEngineService = provider.GetRequiredService<OcrEngineInitializerService>();
            
            return new CompositeOcrEngine(logger, fastEngine, heavyEngineService);
        });
        
        // 🎯 メインのIOcrEngineとしてCompositeOcrEngineを登録
        services.AddSingleton<IOcrEngine>(provider => 
            provider.GetRequiredService<CompositeOcrEngine>());
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