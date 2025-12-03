using System;
using System.Collections.Generic;
using System.Linq;
using Baketa.Application.Services;
using Baketa.Application.Services.Cache;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Infrastructure.OCR.ONNX;
using Baketa.Infrastructure.OCR.PaddleOCR.Services;
using Microsoft.Extensions.DependencyInjection;

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

        // 🚀 [Issue #181] OnnxOcrEngineをベースとしたデコレーターパターン実装
        // PP-OCRv5 ONNXモデル + 日本語対応辞書(ppocrv5_dict.txt)を使用
        //
        // 旧: PooledOcrService → ObjectPool<IOcrEngine> → PaddleOcrEngine (Sdcb.PaddleOCR/ChineseV5)
        //     問題: 辞書が中国語のみで日本語ひらがな・カタカナを認識不可
        //
        // 新: OnnxOcrEngine (ONNX Runtime + PP-OCRv5モデル + 日本語対応辞書)
        //     解決: ppocrv5_dict.txtは9,912文字（日本語ひらがな・カタカナ含む）

        // 2. キャッシュエンジン（デコレーター）を具体的な型で登録
        //    ベースとなるOnnxOcrEngineをコンストラクタで受け取る
        services.AddSingleton<CachedOcrEngine>(provider =>
        {
            var onnxEngine = provider.GetRequiredService<OnnxOcrEngine>();
            var cacheService = provider.GetRequiredService<IAdvancedOcrCacheService>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedOcrEngine>>();

            Console.WriteLine($"✅ [Issue #181] CachedOcrEngine（デコレーター）作成 - ベースサービス: {onnxEngine.GetType().Name}");
            Console.WriteLine($"   → OnnxOcrEngine使用 (PP-OCRv5 ONNX + 日本語対応辞書)");
            return new CachedOcrEngine(onnxEngine, cacheService, logger);
        });
        Console.WriteLine("✅ CachedOcrEngineを具体的な型で登録完了 (OnnxOcrEngineベース)");

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
        // インフラストラクチャモジュールに依存
        yield return typeof(Baketa.Infrastructure.DI.Modules.InfrastructureModule);

        // 🚀 [Issue #181] OnnxOcrModuleに依存（OnnxOcrEngineの登録）
        yield return typeof(Baketa.Infrastructure.DI.OnnxOcrModule);
    }
}
