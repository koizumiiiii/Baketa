using Baketa.Core.UI.Overlay.Positioning;

namespace Baketa.Core.UI.Overlay.Positioning;

/// <summary>
/// オーバーレイ位置管理システムのファクトリーインターフェース
/// </summary>
public interface IOverlayPositionManagerFactory
{
    /// <summary>
    /// 基本設定でオーバーレイ位置管理システムを作成します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>オーバーレイ位置管理システム</returns>
    Task<IOverlayPositionManager> CreateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定された設定でオーバーレイ位置管理システムを作成します
    /// </summary>
    /// <param name="settings">オーバーレイ位置設定</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>オーバーレイ位置管理システム</returns>
    Task<IOverlayPositionManager> CreateWithSettingsAsync(
        OverlayPositionSettings settings, 
        CancellationToken cancellationToken = default);
}
