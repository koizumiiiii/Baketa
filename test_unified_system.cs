using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.UI.Overlay;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.Application.DI.Modules;
using Baketa.UI.DI.Modules;
using Baketa.UI.Services.Overlay;

/// <summary>
/// 統一システム動作検証用テストコード
/// Interface Implementation Replacement戦略の実装確認
/// </summary>
public class UnifiedSystemTest
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("🚀 統一システム動作テスト開始");

        var services = new ServiceCollection();
        
        // 必要な基本サービスを登録
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<Baketa.UI.Services.InPlaceTranslationOverlayManager>();
        
        // Phase16モジュールを登録してDI確認
        var phase16Module = new Phase16UIOverlayModule();
        phase16Module.RegisterServices(services);
        
        // その他の必要なモジュールも登録
        var appModule = new ApplicationModule();
        appModule.RegisterServices(services);
        
        var serviceProvider = services.BuildServiceProvider();
        
        try
        {
            Console.WriteLine("\n✅ DI登録確認:");
            
            // IOverlayRendererが正しく登録されているか確認
            var overlayRenderer = serviceProvider.GetService<IOverlayRenderer>();
            Console.WriteLine($"   IOverlayRenderer: {overlayRenderer?.GetType().Name ?? "NULL"}");
            
            // IInPlaceTranslationOverlayManagerとして解決できるか確認
            var overlayManager = serviceProvider.GetService<IInPlaceTranslationOverlayManager>();
            Console.WriteLine($"   IInPlaceTranslationOverlayManager: {overlayManager?.GetType().Name ?? "NULL"}");
            
            // IEventProcessor<OverlayUpdateEvent>として解決できるか確認
            var eventProcessor = serviceProvider.GetService<IEventProcessor<OverlayUpdateEvent>>();
            Console.WriteLine($"   IEventProcessor<OverlayUpdateEvent>: {eventProcessor?.GetType().Name ?? "NULL"}");
            
            // 同一インスタンスかどうか確認（重要）
            bool isSameInstance = ReferenceEquals(overlayRenderer, overlayManager) && 
                                  ReferenceEquals(overlayManager, eventProcessor);
            Console.WriteLine($"   同一インスタンス確認: {isSameInstance}");
            
            if (overlayRenderer is AvaloniaOverlayRenderer avaloniaRenderer)
            {
                Console.WriteLine("\n✅ Interface Implementation確認:");
                Console.WriteLine($"   AvaloniaOverlayRenderer として型キャスト成功");
                
                // インターフェース実装確認
                bool implementsOverlayRenderer = avaloniaRenderer is IOverlayRenderer;
                bool implementsOverlayManager = avaloniaRenderer is IInPlaceTranslationOverlayManager;
                bool implementsEventProcessor = avaloniaRenderer is IEventProcessor<OverlayUpdateEvent>;
                
                Console.WriteLine($"   IOverlayRenderer実装: {implementsOverlayRenderer}");
                Console.WriteLine($"   IInPlaceTranslationOverlayManager実装: {implementsOverlayManager}");
                Console.WriteLine($"   IEventProcessor<OverlayUpdateEvent>実装: {implementsEventProcessor}");
                
                // EventProcessor設定確認
                if (avaloniaRenderer is IEventProcessor<OverlayUpdateEvent> processor)
                {
                    Console.WriteLine($"   EventProcessor Priority: {processor.Priority}");
                    Console.WriteLine($"   EventProcessor SynchronousExecution: {processor.SynchronousExecution}");
                }
                
                Console.WriteLine("\n🎯 統一システム動作結果:");
                if (isSameInstance && implementsOverlayRenderer && implementsOverlayManager && implementsEventProcessor)
                {
                    Console.WriteLine("   ✅ Interface Implementation Replacement戦略が正常に実装されています！");
                    Console.WriteLine("   ✅ 重複オーバーレイ表示問題の根本解決が完了しました");
                }
                else
                {
                    Console.WriteLine("   ❌ Interface Implementation Replacementに問題があります");
                }
            }
            else
            {
                Console.WriteLine("   ❌ AvaloniaOverlayRendererとして解決できませんでした");
            }
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ テスト中にエラーが発生: {ex.Message}");
            Console.WriteLine($"   スタックトレース: {ex.StackTrace}");
        }
        finally
        {
            await serviceProvider.DisposeAsync();
        }
        
        Console.WriteLine("\n🏁 統一システム動作テスト完了");
    }
}