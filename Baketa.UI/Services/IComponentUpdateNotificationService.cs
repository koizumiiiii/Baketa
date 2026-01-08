using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Services;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Services;

/// <summary>
/// [Issue #256] コンポーネント更新通知サービスインターフェース
/// Clean Architecture: Core層の抽象（IComponentUpdateCheckResult）に依存
/// </summary>
public interface IComponentUpdateNotificationService
{
    /// <summary>
    /// 起動時の更新チェック（バックグラウンド）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>更新が利用可能な場合はtrue</returns>
    Task<bool> CheckForUpdatesOnStartupAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 手動更新チェック（設定画面から）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>更新チェック結果のリスト</returns>
    Task<IReadOnlyList<IComponentUpdateCheckResult>> CheckForUpdatesManuallyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新ダイアログを表示
    /// </summary>
    /// <param name="updates">更新可能なコンポーネントリスト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>ダイアログの結果</returns>
    Task<ComponentUpdateDialogResult> ShowUpdateDialogAsync(
        IReadOnlyList<IComponentUpdateCheckResult> updates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 選択されたコンポーネントを更新
    /// </summary>
    /// <param name="selectedItems">選択されたコンポーネント</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>すべて成功した場合はtrue</returns>
    Task<bool> UpdateSelectedComponentsAsync(
        IEnumerable<ComponentUpdateItem> selectedItems,
        CancellationToken cancellationToken = default);
}
