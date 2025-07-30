using Baketa.Core.Abstractions.DI;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Application.Services.OCR;
using Baketa.Infrastructure.OCR.PostProcessing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace Baketa.Application.DI.Modules;

/// <summary>
/// OCR処理関連のサービスを登録するモジュール
/// </summary>
[ModulePriority(ModulePriority.Application)]
public sealed class OcrProcessingModule : ServiceModuleBase
{
    /// <summary>
    /// OCR処理サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public override void RegisterServices(IServiceCollection services)
    {
        // OCR前処理サービス（Core.Abstractionsの抽象化を使用）
        services.AddSingleton<Baketa.Core.Abstractions.OCR.IOcrPreprocessingService, OcrPreprocessingService>();
        
        // OCR精度向上機能を追加（Phase 1実装）
        services.AddSingleton<ConfidenceBasedReprocessor>();
        services.AddSingleton<UniversalMisrecognitionCorrector>();
        
        // OCR精度向上機能のログを記録
        try
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 [DIRECT] OcrProcessingModule - OCR精度向上機能をDI登録: ConfidenceBasedReprocessor, UniversalMisrecognitionCorrector{Environment.NewLine}");
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"OcrProcessingModule ファイル書き込みエラー: {fileEx.Message}");
        }
    }
    
    /// <summary>
    /// このモジュールが依存する他のモジュールの型を取得します
    /// </summary>
    /// <returns>依存モジュールの型のコレクション</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        yield return typeof(CoreModule);
        // Infrastructure modulesは参照できないため、依存関係は最小限に抑える
    }
}