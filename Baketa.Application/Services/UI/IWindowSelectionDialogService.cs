using Baketa.Core.Abstractions.Platform.Windows.Adapters;

namespace Baketa.Application.Services.UI;

/// <summary>
/// ウィンドウ選択ダイアログサービス抽象化
/// UIレイヤーでの実装を前提とした責務分離インターフェース
/// Clean Architecture原則に従い、ApplicationレイヤーからUIレイヤーへの依存を排除
/// </summary>
public interface IWindowSelectionDialogService
{
    /// <summary>
    /// ウィンドウ選択ダイアログを表示します
    /// </summary>
    /// <returns>選択されたウィンドウ情報（キャンセル時はnull）</returns>
    Task<WindowInfo?> ShowWindowSelectionDialogAsync();
}