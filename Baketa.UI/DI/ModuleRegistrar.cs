using Baketa.Application.DI.Extensions;
using Baketa.Application.DI.Modules;
using Baketa.Core.DI;
using Baketa.Core.DI.Modules;
using Baketa.Infrastructure.DI;
using Baketa.Infrastructure.DI.Modules;
using Baketa.UI.DI.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.UI.DI;

/// <summary>
/// UIモジュール登録クラス
/// </summary>
internal static class ModuleRegistrar
{
    /// <summary>
    /// UIモジュールを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="environment">環境設定</param>
    /// <param name="configuration">設定</param>
    /// <returns>登録後のサービスコレクション</returns>
    public static IServiceCollection AddUIModule(
        this IServiceCollection services,
        BaketaEnvironment environment = BaketaEnvironment.Production,
        IConfiguration? configuration = null)
    {
        // CoreModuleを直接登録（IEventAggregatorなどの基本サービス）
        var coreModule = new CoreModule();
        coreModule.RegisterServices(services);

        // SettingsSystemを登録（ISettingsServiceを提供）
        services.AddSettingsSystem();

        // InfrastructureModuleを直接登録（OCR、翻訳、永続化サービス）
        var infrastructureModule = new InfrastructureModule();
        infrastructureModule.RegisterServices(services);

        // 🚀 Gemini推奨Step2: 段階的OCR戦略モジュール直接登録
        Console.WriteLine("🔍 [DEBUG] StagedOcrStrategyModule登録開始...");
        var stagedOcrModule = new StagedOcrStrategyModule();
        stagedOcrModule.RegisterServices(services);
        Console.WriteLine("✅ [DEBUG] StagedOcrStrategyModule登録完了！");

        // Baketaのその他のサービスモジュールを追加
        services.AddBaketaServices(environment: environment);

        // ReactiveUIサービスをモジュール経由で登録
        var enableDebugMode = environment == BaketaEnvironment.Development;
        services.AddReactiveUIServices(enableDebugMode);

        // UIサービスとビューモデルを登録
        services.RegisterUIServices(configuration);

        return services;
    }
}
