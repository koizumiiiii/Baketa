using System;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.DI;
using Baketa.Core.Events.EventTypes;
using Baketa.UI.Framework.Events;
using Baketa.UI.Services;
using Baketa.UI.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.DI.Modules;

/// <summary>
/// 翻訳フロー統合のDIモジュール
/// </summary>
public class TranslationFlowModule : ServiceModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        // TranslationFlowEventProcessorは既にUIServiceCollectionExtensionsで登録済み
        // ここではイベント購読の設定のみ行う
    }

    public void ConfigureEventAggregator(IEventAggregator eventAggregator, IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<TranslationFlowModule>>();
        
        try
        {
            Console.WriteLine("🔄 TranslationFlowModuleの初期化を開始");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 TranslationFlowModuleの初期化を開始");
            logger.LogDebug("🔄 TranslationFlowModuleの初期化を開始");
            
            // TranslationFlowEventProcessorを取得
            Console.WriteLine("📡 TranslationFlowEventProcessorを取得中");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "📡 TranslationFlowEventProcessorを取得中");
            logger.LogDebug("📡 TranslationFlowEventProcessorを取得中");
            
            try 
            {
                var processor = serviceProvider.GetRequiredService<TranslationFlowEventProcessor>();
                Console.WriteLine($"✅ TranslationFlowEventProcessor取得成功: ハッシュ={processor.GetHashCode()}");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ TranslationFlowEventProcessor取得成功: ハッシュ={processor.GetHashCode()}");
                logger.LogDebug("✅ TranslationFlowEventProcessor取得成功");

                // 各イベントタイプに対してプロセッサーを購読
                Console.WriteLine("📢 イベント購読を開始");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "📢 イベント購読を開始");
                logger.LogDebug("📢 イベント購読を開始");
                
                eventAggregator.Subscribe<StartTranslationRequestEvent>(processor);
                Console.WriteLine("✅ StartTranslationRequestEvent購読完了");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ StartTranslationRequestEvent購読完了");
                logger.LogDebug("✅ StartTranslationRequestEvent購読完了");
                
                eventAggregator.Subscribe<StopTranslationRequestEvent>(processor);
                Console.WriteLine("✅ StopTranslationRequestEvent購読完了");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ StopTranslationRequestEvent購読完了");
                logger.LogDebug("✅ StopTranslationRequestEvent購読完了");
                
                eventAggregator.Subscribe<ToggleTranslationDisplayRequestEvent>(processor);
                Console.WriteLine("✅ ToggleTranslationDisplayRequestEvent購読完了");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ ToggleTranslationDisplayRequestEvent購読完了");
                logger.LogDebug("✅ ToggleTranslationDisplayRequestEvent購読完了");
                
                eventAggregator.Subscribe<SettingsChangedEvent>(processor);
                Console.WriteLine("✅ SettingsChangedEvent購読完了");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ SettingsChangedEvent購読完了");
                logger.LogDebug("✅ SettingsChangedEvent購読完了");
                
                // 🎯 UltraThink Phase 23 修正: StartCaptureRequestedEvent購読追加
                eventAggregator.Subscribe<StartCaptureRequestedEvent>(processor);
                Console.WriteLine("✅ StartCaptureRequestedEvent購読完了");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ StartCaptureRequestedEvent購読完了");
                logger.LogDebug("✅ StartCaptureRequestedEvent購読完了");
                
                eventAggregator.Subscribe<StopCaptureRequestedEvent>(processor);
                Console.WriteLine("✅ StopCaptureRequestedEvent購読完了");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ StopCaptureRequestedEvent購読完了");
                logger.LogDebug("✅ StopCaptureRequestedEvent購読完了");

                // OverlayUpdateEventの購読を追加
                try
                {
                    var overlayManager = serviceProvider.GetRequiredService<InPlaceTranslationOverlayManager>();
                    eventAggregator.Subscribe<OverlayUpdateEvent>(overlayManager);
                    Console.WriteLine("✅ OverlayUpdateEvent購読完了");
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ OverlayUpdateEvent購読完了");
                    logger.LogDebug("✅ OverlayUpdateEvent購読完了");
                }
                catch (Exception overlayEx)
                {
                    Console.WriteLine($"⚠️ OverlayUpdateEvent購読失敗: {overlayEx.Message}");
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⚠️ OverlayUpdateEvent購読失敗: {overlayEx.Message}");
                    logger.LogWarning(overlayEx, "OverlayUpdateEvent購読失敗");
                }

                Console.WriteLine("🎉 翻訳フローイベントプロセッサーを正常に登録しました");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🎉 翻訳フローイベントプロセッサーを正常に登録しました");
                logger.LogInformation("🎉 翻訳フローイベントプロセッサーを正常に登録しました");
            }
            catch (Exception processorEx)
            {
                Console.WriteLine($"💥 TranslationFlowEventProcessor取得失敗: {processorEx.GetType().Name}: {processorEx.Message}");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 TranslationFlowEventProcessor取得失敗: {processorEx.GetType().Name}: {processorEx.Message}");
                logger.LogError(processorEx, "💥 TranslationFlowEventProcessor取得失敗");
                
                // 内部例外も出力
                if (processorEx.InnerException != null)
                {
                    Console.WriteLine($"💥 内部例外: {processorEx.InnerException.GetType().Name}: {processorEx.InnerException.Message}");
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 内部例外: {processorEx.InnerException.GetType().Name}: {processorEx.InnerException.Message}");
                }
                
                throw; // 再スロー
            }
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "💥 翻訳フローイベントプロセッサーの登録に失敗しました: {ErrorMessage}", ex.Message);
            throw;
        }
    }
}