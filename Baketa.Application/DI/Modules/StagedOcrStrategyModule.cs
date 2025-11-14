using System;
using System.Collections.Generic;
using Baketa.Application.Services;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using Baketa.Infrastructure.OCR.PaddleOCR.Factory;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Baketa.Infrastructure.OCR.PaddleOCR.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;

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
        // 🎯 このモジュールでの登録はAdvancedCachingModuleに移行されました。
        // 競合を避けるため、ここでは何も登録しません。
        Console.WriteLine("ℹ️ StagedOcrStrategyModule: 登録処理はAdvancedCachingModuleに移行済み。");
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
