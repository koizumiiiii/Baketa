using System;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.DI;
using Baketa.UI.Framework.Events;
using Baketa.UI.Services;
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
            logger.LogDebug("🔄 TranslationFlowModuleの初期化を開始");
            
            // TranslationFlowEventProcessorを取得
            Console.WriteLine("📡 TranslationFlowEventProcessorを取得中");
            logger.LogDebug("📡 TranslationFlowEventProcessorを取得中");
            var processor = serviceProvider.GetRequiredService<TranslationFlowEventProcessor>();
            Console.WriteLine($"✅ TranslationFlowEventProcessor取得成功: ハッシュ={processor.GetHashCode()}");
            logger.LogDebug("✅ TranslationFlowEventProcessor取得成功");

            // 各イベントタイプに対してプロセッサーを購読
            Console.WriteLine("📢 イベント購読を開始");
            logger.LogDebug("📢 イベント購読を開始");
            eventAggregator.Subscribe<StartTranslationRequestEvent>(processor);
            Console.WriteLine("✅ StartTranslationRequestEvent購読完了");
            logger.LogDebug("✅ StartTranslationRequestEvent購読完了");
            
            eventAggregator.Subscribe<StopTranslationRequestEvent>(processor);
            Console.WriteLine("✅ StopTranslationRequestEvent購読完了");
            logger.LogDebug("✅ StopTranslationRequestEvent購読完了");
            
            eventAggregator.Subscribe<ToggleTranslationDisplayRequestEvent>(processor);
            Console.WriteLine("✅ ToggleTranslationDisplayRequestEvent購読完了");
            logger.LogDebug("✅ ToggleTranslationDisplayRequestEvent購読完了");
            
            eventAggregator.Subscribe<SettingsChangedEvent>(processor);
            Console.WriteLine("✅ SettingsChangedEvent購読完了");
            logger.LogDebug("✅ SettingsChangedEvent購読完了");

            Console.WriteLine("🎉 翻訳フローイベントプロセッサーを正常に登録しました");
            logger.LogInformation("🎉 翻訳フローイベントプロセッサーを正常に登録しました");
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "💥 翻訳フローイベントプロセッサーの登録に失敗しました: {ErrorMessage}", ex.Message);
            throw;
        }
    }
}