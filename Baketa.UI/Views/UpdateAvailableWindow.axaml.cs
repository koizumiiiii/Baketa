using Avalonia.Controls;
using Baketa.UI.ViewModels;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.Interfaces;

namespace Baketa.UI.Views;

/// <summary>
/// [Issue #249] カスタム更新ダイアログウィンドウ
/// i18n対応のためNetSparkleデフォルトUIを置き換え
/// </summary>
public partial class UpdateAvailableWindow : Window, IUpdateAvailable
{
    private readonly UpdateAvailableViewModel _viewModel;

    public UpdateAvailableWindow(UpdateAvailableViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // ViewModelのイベントをウィンドウアクションにバインド
        _viewModel.CloseRequested += (_, result) =>
        {
            UserResponded?.Invoke(this, new UpdateResponseEventArgs(result, _viewModel.CurrentItem));
            Close();
        };
    }

    /// <summary>
    /// ユーザー応答イベント
    /// </summary>
    public event UserRespondedToUpdate? UserResponded;

    /// <summary>
    /// 結果（互換性のため）
    /// </summary>
    public UpdateAvailableResult Result => _viewModel.Result;

    /// <summary>
    /// 現在のアイテム（互換性のため）
    /// </summary>
    public AppCastItem CurrentItem => _viewModel.CurrentItem!;

    /// <summary>
    /// ウィンドウを表示してユーザー応答を待つ
    /// </summary>
    public void BringToFront()
    {
        Show();
        Activate();
    }

    /// <summary>
    /// ウィンドウを閉じる
    /// </summary>
    public void HideWindow()
    {
        Close();
    }

    /// <summary>
    /// ウィンドウを表示
    /// </summary>
    void IUpdateAvailable.Show()
    {
        Show();
    }

    /// <summary>
    /// リリースノートを非表示（このダイアログでは使用しない）
    /// </summary>
    public void HideReleaseNotes()
    {
        // カスタムダイアログではリリースノートセクションを持たない
    }

    /// <summary>
    /// 後で通知ボタンを非表示
    /// </summary>
    public void HideRemindMeLaterButton()
    {
        // TODO: 必要に応じてボタンを非表示にする実装を追加
    }

    /// <summary>
    /// スキップボタンを非表示
    /// </summary>
    public void HideSkipButton()
    {
        // TODO: 必要に応じてボタンを非表示にする実装を追加
    }
}
