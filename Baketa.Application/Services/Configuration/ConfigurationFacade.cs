using Baketa.Core.Abstractions.Configuration;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Logging;
using Baketa.Core.Abstractions.Settings;

namespace Baketa.Application.Services.Configuration;

/// <summary>
/// 設定ファサード実装
/// 設定、イベント、ログに関連するサービス群を統合管理し、横断的関心事の依存関係を簡素化
/// </summary>
public sealed class ConfigurationFacade : IConfigurationFacade
{
    /// <inheritdoc />
    public IUnifiedSettingsService SettingsService { get; }
    
    /// <inheritdoc />
    public IEventAggregator EventAggregator { get; }
    
    /// <inheritdoc />
    public IBaketaLogger Logger { get; }

    public ConfigurationFacade(
        IUnifiedSettingsService settingsService,
        IEventAggregator eventAggregator,
        IBaketaLogger logger)
    {
        SettingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        EventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}