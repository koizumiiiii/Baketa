using Baketa.Application.Services.UI.Overlay;
using Baketa.Core.Abstractions.UI.Overlay;
using Baketa.Core.DI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.DI.Modules;

/// <summary>
/// オーバーレイオーケストレーションシステム DI登録モジュール
/// Clean Architecture準拠のオーバーレイ調整・管理システムを登録
/// - 重複検出・衝突検出サービス
/// - ライフサイクル管理サービス
/// - 中央調整オーケストレーター
/// </summary>
public class OverlayOrchestrationModule : ServiceModuleBase
{
    /// <inheritdoc />
    public override void RegisterServices(IServiceCollection services)
    {
        // 診断ログ用のロガー取得（DIコンテナ初期化時のみ一時的に使用）
        using var tempProvider = services.BuildServiceProvider();
        var logger = tempProvider.GetService<ILogger<OverlayOrchestrationModule>>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OverlayOrchestrationModule>.Instance;

        logger.LogInformation("🚀 [OVERLAY_ORCHESTRATION] オーバーレイオーケストレーションシステム登録開始");

        try
        {
            // Application層のサービス実装を登録
            RegisterApplicationServices(services, logger);
            
            // 設定オプションの登録
            RegisterConfigurationOptions(services, logger);

            logger.LogInformation("✅ [OVERLAY_ORCHESTRATION] オーバーレイオーケストレーションシステム登録完了");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ [OVERLAY_ORCHESTRATION] オーバーレイオーケストレーションシステム登録中にエラー発生");
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
        logger.LogDebug("📦 [OVERLAY_ORCHESTRATION] Application層サービス登録開始");

        // 重複検出・衝突検出サービス
        services.AddSingleton<IOverlayCollisionDetector>(serviceProvider =>
        {
            var collisionLogger = serviceProvider.GetRequiredService<ILogger<OverlayCollisionDetector>>();

            // デフォルト設定で重複検出器を作成
            var settings = new CollisionDetectionSettings
            {
                DuplicationPreventionWindow = TimeSpan.FromSeconds(2),
                AutoCleanupThreshold = 100,
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

        // ✅ UI層サービスはOverlayUIModuleで実装済み
        // オーバーレイ表示の具象実装はUI層に委任
        logger.LogInformation("🔄 [OVERLAY_ORCHESTRATION] UI層サービス登録はOverlayUIModuleに委任");

        logger.LogDebug("✅ [OVERLAY_ORCHESTRATION] Application層サービス登録完了");
    }

    /// <summary>
    /// 設定オプションの登録
    /// </summary>
    private static void RegisterConfigurationOptions(IServiceCollection services, ILogger logger)
    {
        logger.LogDebug("⚙️ [OVERLAY_ORCHESTRATION] 設定オプション登録開始");

        // 設定クラスの登録（appsettings.jsonから読み込み）
        // 将来的にIOptionsPattern対応

        logger.LogDebug("✅ [OVERLAY_ORCHESTRATION] 設定オプション登録完了");
    }

    /// <summary>
    /// このモジュールが依存する他のモジュールの型を取得します。
    /// </summary>
    /// <returns>依存モジュールの型のコレクション</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        // オーバーレイオーケストレーションシステムは Core モジュールに依存
        yield return typeof(Baketa.Core.DI.Modules.CoreModule);
    }
}