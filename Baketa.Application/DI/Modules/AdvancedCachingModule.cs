using System;
using System.Collections.Generic;
using Baketa.Application.Services.Cache;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Application.DI.Modules;

/// <summary>
/// Step3: Gemini推奨高度キャッシング戦略のDI登録モジュール
/// 数ミリ秒OCR応答の実現のためのキャッシング機能統合
/// PP-OCRv5削除後: SuryaOcrModuleがIOcrEngineを登録するため、キャッシュサービスのみ登録
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

        // [Issue #415] Cloud翻訳キャッシュサービス（Fork-Join段階でのAPIコール抑制）
        services.AddSingleton<ICloudTranslationCache, CloudTranslationCache>();
        Console.WriteLine("✅ ICloudTranslationCache登録完了");

        // NOTE: [PP-OCRv5削除] IOcrEngineの登録はSuryaOcrModuleに移行
        // SuryaOcrModuleがIOcrEngine→SuryaOcrEngineを直接登録するため、
        // ここでのCachedOcrEngineデコレーターパターンは削除
        // キャッシュ機能が必要な場合は、SuryaOcrEngineをラップする新しい実装を検討

        Console.WriteLine("✅ Step3: 高度キャッシング戦略登録完了");
        Console.WriteLine("ℹ️ IOcrEngine登録はSuryaOcrModuleで実施");
    }

    /// <summary>
    /// このモジュールが依存する他のモジュールの型を取得します
    /// </summary>
    /// <returns>依存モジュールの型のコレクション</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        // インフラストラクチャモジュールに依存
        yield return typeof(Baketa.Infrastructure.DI.Modules.InfrastructureModule);

        // Surya OCRモジュールに依存（IOcrEngineの登録）
        yield return typeof(Baketa.Infrastructure.DI.SuryaOcrModule);
    }
}
