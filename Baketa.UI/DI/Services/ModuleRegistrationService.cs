using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.DI.Modules;
using Baketa.Core.DI;
using Baketa.Infrastructure.DI.Modules;
using Baketa.Infrastructure.DI;
using Baketa.Infrastructure.Platform.DI;
using Baketa.Application.DI.Modules;
using Baketa.UI.DI.Modules;
using System.Diagnostics;

namespace Baketa.UI.DI.Services;

/// <summary>
/// DIモジュール登録の簡素化と一元化を担当するサービス
/// Phase 2 DI簡素化の一環として作成
/// </summary>
public sealed class ModuleRegistrationService
{
    private readonly IServiceCollection _services;
    private readonly HashSet<Type> _registeredModules = [];
    private readonly Stack<Type> _moduleStack = [];

    public ModuleRegistrationService(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// すべての必要なモジュールを適切な順序で登録します
    /// </summary>
    public void RegisterAllModules()
    {
        LogRegistrationStart();
        
        // Phase 1: Core基盤モジュール
        RegisterCoreModules();
        
        // Phase 2: Infrastructure基盤
        RegisterInfrastructureModules();
        
        // Phase 3: Application業務ロジック
        RegisterApplicationModules();
        
        // Phase 4: UI/Presentation
        RegisterUIModules();
        
        // Phase 5: 特殊機能モジュール
        RegisterSpecializedModules();
        
        LogRegistrationComplete();
    }

    private void RegisterCoreModules()
    {
        Console.WriteLine("🏗️ Phase 1: Core基盤モジュール登録開始");
        
        // Core基盤
        var coreModule = new CoreModule();
        coreModule.RegisterWithDependencies(_services, _registeredModules, _moduleStack);
        
        // 設定システム
        _services.AddSettingsSystem();
        
        Console.WriteLine("✅ Core基盤モジュール登録完了");
    }

    private void RegisterInfrastructureModules()
    {
        Console.WriteLine("🔧 Phase 2: Infrastructure基盤登録開始");
        
        // Infrastructure基盤
        var infrastructureModule = new InfrastructureModule();
        infrastructureModule.RegisterWithDependencies(_services, _registeredModules, _moduleStack);
        
        // Platform基盤
        var platformModule = new Baketa.Infrastructure.Platform.DI.Modules.PlatformModule();
        platformModule.RegisterWithDependencies(_services, _registeredModules, _moduleStack);
        
        // Auth基盤
        var authModule = new AuthModule();
        authModule.RegisterWithDependencies(_services, _registeredModules, _moduleStack);
        
        // ログシステム（Phase 1完了項目）
        var loggingModule = new LoggingModule();
        loggingModule.RegisterServices(_services);
        
        Console.WriteLine("✅ Infrastructure基盤登録完了");
    }

    private void RegisterApplicationModules()
    {
        Console.WriteLine("🚀 Phase 3: Application業務ロジック登録開始");
        
        // AdaptiveCapture（ApplicationModule依存関係）
        var adaptiveCaptureModule = new Baketa.Infrastructure.Platform.DI.Modules.AdaptiveCaptureModule();
        adaptiveCaptureModule.RegisterServices(_services);
        
        // メインApplication
        var applicationModule = new ApplicationModule();
        applicationModule.RegisterWithDependencies(_services, _registeredModules, _moduleStack);
        
        Console.WriteLine("✅ Application業務ロジック登録完了");
    }

    private void RegisterUIModules()
    {
        Console.WriteLine("🎨 Phase 4: UI/Presentation登録開始");
        
        // UI基盤
        var uiModule = new UIModule();
        uiModule.RegisterWithDependencies(_services, _registeredModules, _moduleStack);
        
        // オーバーレイUI
        var overlayUIModule = new OverlayUIModule();
        overlayUIModule.RegisterServices(_services);
        
        // アダプターサービス
        _services.AddAdapterServices();
        
        Console.WriteLine("✅ UI/Presentation登録完了");
    }

    private void RegisterSpecializedModules()
    {
        Console.WriteLine("⚡ Phase 5: 特殊機能モジュール登録開始");
        
        // OCR最適化モジュール群
        RegisterOcrOptimizationModules();
        
        // Gemini推奨モジュール群
        RegisterGeminiRecommendedModules();
        
        Console.WriteLine("✅ 特殊機能モジュール登録完了");
    }

    private void RegisterOcrOptimizationModules()
    {
        // バッチOCR
        var batchOcrModule = new BatchOcrModule();
        batchOcrModule.RegisterServices(_services);
        
        // OCR前処理
        var ocrProcessingModule = new OcrProcessingModule();
        ocrProcessingModule.RegisterServices(_services);
        
        // OpenCV処理（IOcrPreprocessingService上書き）
        var openCvProcessingModule = new Baketa.Infrastructure.DI.Modules.OpenCvProcessingModule();
        openCvProcessingModule.RegisterServices(_services);
        
        // PaddleOCR統合
        var paddleOcrModule = new PaddleOcrModule();
        paddleOcrModule.RegisterServices(_services);
    }

    private void RegisterGeminiRecommendedModules()
    {
        // Gemini推奨Step2: 段階的OCR戦略
        Console.WriteLine("🔍 [GEMINI] StagedOcrStrategyModule登録開始...");
        var stagedOcrModule = new StagedOcrStrategyModule();
        stagedOcrModule.RegisterWithDependencies(_services, _registeredModules, _moduleStack);
        Console.WriteLine("✅ [GEMINI] StagedOcrStrategyModule登録完了！");
        
        // Gemini推奨Step3: 高度キャッシング戦略
        Console.WriteLine("🔍 [GEMINI] AdvancedCachingModule登録開始...");
        var advancedCachingModule = new AdvancedCachingModule();
        advancedCachingModule.RegisterWithDependencies(_services, _registeredModules, _moduleStack);
        Console.WriteLine("✅ [GEMINI] AdvancedCachingModule登録完了！");
    }

    private void LogRegistrationStart()
    {
        Console.WriteLine("🏁 ModuleRegistrationService: 統合モジュール登録開始");
        Console.WriteLine($"🏁 Phase 2 DI簡素化: 統一的モジュール管理による保守性向上");
    }

    private void LogRegistrationComplete()
    {
        Console.WriteLine($"🎉 ModuleRegistrationService: 全モジュール登録完了");
        Console.WriteLine($"📊 登録済みモジュール数: {_registeredModules.Count}");
        Console.WriteLine($"🚀 DI簡素化Phase 2完了: プログラム保守性大幅向上");
    }
}