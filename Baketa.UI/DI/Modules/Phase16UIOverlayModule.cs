using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.UI.Overlay;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.DI;
using Baketa.UI.Services.Overlay;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.DI.Modules;

/// <summary>
/// UltraThink Phase 16 UI層オーバーレイシステム統合モジュール
/// Phase 15 Clean Architecture インターフェースを実際の Avalonia UI 実装に接続
/// </summary>
public class Phase16UIOverlayModule : ServiceModuleBase
{
    /// <inheritdoc />
    public override void RegisterServices(IServiceCollection services)
    {
        // コンソール出力で確実に表示 (ロガーはファクトリ内で解決)
        Console.WriteLine("🚀 [PHASE16_UI] UltraThink Phase 16 UI層統合開始 - Avalonia UI 実装");

        try
        {
            // Phase 15 スタブ実装を Avalonia UI 実装に置き換え
            RegisterAvaloniaUIServices(services);
            
            // Phase 16 統合設定
            RegisterPhase16Configuration(services);

            Console.WriteLine("✅ [PHASE16_UI] Phase 16 UI層統合完了 - Clean Architecture と Avalonia UI 統合");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [PHASE16_UI] Phase 16 UI層統合中にエラー発生: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Avalonia UI サービスの登録
    /// Phase 15 インターフェースを実装する実際の UI クラスを登録
    /// </summary>
    private static void RegisterAvaloniaUIServices(IServiceCollection services)
    {
        // 🔄 Phase 15 スタブ実装を Avalonia UI 実装に置き換え
        
        // ✅ Interface Implementation Replacement戦略: 統一インスタンス生成
        // AvaloniaOverlayRenderer が複数インターフェースを実装する統一システム
        
        // 共通ファクトリーメソッドで統一インスタンス生成
        services.AddSingleton<AvaloniaOverlayRenderer>(serviceProvider =>
        {
            var moduleLogger = serviceProvider.GetRequiredService<ILogger<Phase16UIOverlayModule>>();
            var rendererLogger = serviceProvider.GetRequiredService<ILogger<AvaloniaOverlayRenderer>>();
            var overlayManager = serviceProvider.GetRequiredService<Baketa.UI.Services.InPlaceTranslationOverlayManager>();
            
            moduleLogger.LogDebug("🔗 [PHASE16_UI] AvaloniaOverlayRenderer 作成 - Interface Implementation Replacement");
            return new AvaloniaOverlayRenderer(overlayManager, rendererLogger);
        });
        
        // IOverlayRenderer インターフェースの実装登録
        services.AddSingleton<IOverlayRenderer>(serviceProvider =>
            serviceProvider.GetRequiredService<AvaloniaOverlayRenderer>());
        
        // IInPlaceTranslationOverlayManager インターフェースの実装登録
        services.AddSingleton<IInPlaceTranslationOverlayManager>(serviceProvider =>
            serviceProvider.GetRequiredService<AvaloniaOverlayRenderer>());
        
        // IEventProcessor<OverlayUpdateEvent> インターフェースの実装登録
        services.AddSingleton<IEventProcessor<OverlayUpdateEvent>>(serviceProvider =>
            serviceProvider.GetRequiredService<AvaloniaOverlayRenderer>());

        // IOverlayPositionCalculator の実装を AvaloniaOverlayPositionCalculator に置き換え
        services.AddSingleton<IOverlayPositionCalculator, AvaloniaOverlayPositionCalculator>(serviceProvider =>
        {
            var moduleLogger = serviceProvider.GetRequiredService<ILogger<Phase16UIOverlayModule>>();
            var calculatorLogger = serviceProvider.GetRequiredService<ILogger<AvaloniaOverlayPositionCalculator>>();
            
            moduleLogger.LogDebug("🔗 [PHASE16_UI] AvaloniaOverlayPositionCalculator 作成 - モニター統合");
            return new AvaloniaOverlayPositionCalculator(calculatorLogger, serviceProvider);
        });

        Console.WriteLine("✅ [PHASE16_UI] Interface Implementation Replacement完全実装 - 統一システム完成");
    }

    /// <summary>
    /// Phase 16 統合設定の登録
    /// </summary>
    private static void RegisterPhase16Configuration(IServiceCollection services)
    {
        // Phase 16 統合設定オプション（将来的な拡張用）
        // 現時点では基本的な設定のみ
        
        Console.WriteLine("✅ [PHASE16_UI] Phase 16 統合設定登録完了");
    }

    /// <summary>
    /// このモジュールが依存する他のモジュールの型を取得します。
    /// </summary>
    /// <returns>依存モジュールの型のコレクション</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        // Phase 16 は以下に依存:
        // 1. Phase 15 システム (Application 層)
        // 2. UI 層の基本モジュール
        yield return typeof(Baketa.Application.DI.Modules.Phase15OverlayModule);
        yield return typeof(UIModule);
    }

    // Priority プロパティは ServiceModuleBase に存在しないため削除
}

/// <summary>
/// Phase 16 統合設定クラス
/// 将来的な拡張用設定オプション
/// </summary>
public class Phase16IntegrationSettings
{
    /// <summary>
    /// Phase 15 システムとの統合を有効にするか
    /// </summary>
    public bool EnablePhase15Integration { get; set; } = true;
    
    /// <summary>
    /// 既存システムとの互換性モード
    /// </summary>
    public bool LegacyCompatibilityMode { get; set; } = true;
    
    /// <summary>
    /// 高度な位置計算を有効にするか
    /// </summary>
    public bool EnableAdvancedPositioning { get; set; } = true;
    
    /// <summary>
    /// デバッグログレベル
    /// </summary>
    public string DebugLogLevel { get; set; } = "Information";
}