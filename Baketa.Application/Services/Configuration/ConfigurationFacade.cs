using Baketa.Core.Abstractions.Configuration;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Logging;
using Baketa.Core.Abstractions.Settings;

namespace Baketa.Application.Services.Configuration;

/// <summary>
/// 設定ファサード実装
/// 設定、イベント、ログに関連するサービス群を統合管理し、横断的関心事の依存関係を簡素化
/// </summary>
public sealed class ConfigurationFacade(
    IUnifiedSettingsService settingsService,
    IEventAggregator eventAggregator,
    IBaketaLogger logger) : IConfigurationFacade
{
    /// <inheritdoc />
    public IUnifiedSettingsService SettingsService { get; } = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

    /// <inheritdoc />
    public IEventAggregator EventAggregator { get; } = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));

    /// <inheritdoc />
    public IBaketaLogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));
}
