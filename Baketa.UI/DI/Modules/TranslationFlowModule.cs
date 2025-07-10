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
            // TranslationFlowEventProcessorを取得
            var processor = serviceProvider.GetRequiredService<TranslationFlowEventProcessor>();

            // 各イベントタイプに対してプロセッサーを購読
            eventAggregator.Subscribe<StartTranslationRequestEvent>(processor);
            eventAggregator.Subscribe<StopTranslationRequestEvent>(processor);
            eventAggregator.Subscribe<ToggleTranslationDisplayRequestEvent>(processor);
            eventAggregator.Subscribe<SettingsChangedEvent>(processor);

            logger.LogInformation("翻訳フローイベントプロセッサーを登録しました");
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "翻訳フローイベントプロセッサーの登録に失敗しました");
            throw;
        }
    }
}