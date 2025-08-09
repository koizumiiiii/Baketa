using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Logging;
using Baketa.Core.Abstractions.Settings;

namespace Baketa.Core.Abstractions.Configuration;

/// <summary>
/// 設定とイベント処理に関連するサービス群のファサード
/// 横断的関心事の依存関係を統合管理
/// </summary>
public interface IConfigurationFacade
{
    /// <summary>
    /// 統一設定サービス
    /// </summary>
    IUnifiedSettingsService SettingsService { get; }
    
    /// <summary>
    /// イベントアグリゲーター
    /// </summary>
    IEventAggregator EventAggregator { get; }
    
    /// <summary>
    /// Baketaロガー
    /// </summary>
    IBaketaLogger Logger { get; }
}