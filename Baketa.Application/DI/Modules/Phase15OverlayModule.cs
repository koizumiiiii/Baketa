using Baketa.Application.Services.UI.Overlay;
using Baketa.Core.Abstractions.UI.Overlay;
using Baketa.Core.DI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.DI.Modules;

/// <summary>
/// Phase 15 新オーバーレイシステム DI登録モジュール
/// Clean Architecture準拠のオーバーレイシステムを登録
/// </summary>
public class Phase15OverlayModule : ServiceModuleBase
{
    /// <inheritdoc />
    public override void RegisterServices(IServiceCollection services)
    {
        var logger = services.BuildServiceProvider().GetService<ILogger<Phase15OverlayModule>>() 
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<Phase15OverlayModule>.Instance;

        // コンソール出力で確実に表示
        Console.WriteLine("🚀 [PHASE15_DI] Phase 15 新オーバーレイシステム登録開始");
        logger.LogInformation("🚀 [PHASE15_DI] Phase 15 新オーバーレイシステム登録開始");

        try
        {
            // Application層のサービス実装を登録
            RegisterApplicationServices(services, logger);
            
            // 設定オプションの登録
            RegisterConfigurationOptions(services, logger);

            Console.WriteLine("✅ [PHASE15_DI] Phase 15 新オーバーレイシステム登録完了");
            logger.LogInformation("✅ [PHASE15_DI] Phase 15 新オーバーレイシステム登録完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [PHASE15_DI] Phase 15 オーバーレイシステム登録中にエラー発生: {ex.Message}");
            logger.LogError(ex, "❌ [PHASE15_DI] Phase 15 オーバーレイシステム登録中にエラー発生");
            throw;
        }
    }

    /// <summary>
    /// Application層サービスの登録
    /// </summary>
    /// <summary>
    /// Application層サービスの登録
    /// </summary>
    private static void RegisterApplicationServices(IServiceCollection services, ILogger logger)
    {
        logger.LogDebug("📦 [PHASE15_DI] Application層サービス登録開始");

        // 重複検出・衝突検出サービス
        services.AddSingleton<IOverlayCollisionDetector>(serviceProvider =>
        {
            var collisionLogger = serviceProvider.GetRequiredService<ILogger<OverlayCollisionDetector>>();
            
            // デフォルト設定で重複検出器を作成
            var settings = new CollisionDetectionSettings
            {
                DuplicationPreventionWindow = TimeSpan.FromSeconds(2), // Phase 13互換
                AutoCleanupThreshold = 100, // Phase 13互換
                MaxEntryLifetime = TimeSpan.FromMinutes(5),
                EnablePositionCollisionDetection = true,
                PositionOverlapThreshold = 0.7
            };

            return new OverlayCollisionDetector(collisionLogger, settings);
        });

        // ライフサイクル管理サービス
        services.AddSingleton<IOverlayLifecycleManager, OverlayLifecycleManager>();

        // 中央調整オーケストレーター
        services.AddSingleton<IOverlayOrchestrator, OverlayOrchestrator>();

        // ✅ Phase 16統一: UI層依存サービスはPhase16UIOverlayModuleで実装
        // Stub実装の登録を除去し、Phase16UIOverlayModuleの統一実装に委任
        logger.LogInformation("🔄 [PHASE15_DI] UI層サービス登録はPhase16UIOverlayModuleに委任");

        logger.LogDebug("✅ [PHASE15_DI] Application層サービス登録完了");
    }

    /// <summary>
    /// 設定オプションの登録
    /// </summary>
    private static void RegisterConfigurationOptions(IServiceCollection services, ILogger logger)
    {
        logger.LogDebug("⚙️ [PHASE15_DI] 設定オプション登録開始");

        // 設定クラスの登録（appsettings.jsonから読み込み）
        // 将来的にIOptionsPattern対応
        
        logger.LogDebug("✅ [PHASE15_DI] 設定オプション登録完了");
    }

    /// <summary>
    /// このモジュールが依存する他のモジュールの型を取得します。
    /// </summary>
    /// <returns>依存モジュールの型のコレクション</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        // Phase 15新システムは Core モジュールに依存
        yield return typeof(Baketa.Core.DI.Modules.CoreModule);
    }
}