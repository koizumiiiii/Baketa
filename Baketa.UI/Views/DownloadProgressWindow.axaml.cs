using System;
using Avalonia.Controls;
using Avalonia.Threading;
using Baketa.UI.ViewModels;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.Interfaces;

namespace Baketa.UI.Views;

/// <summary>
/// [Issue #249] カスタムダウンロード進行ダイアログ
/// i18n対応のためNetSparkleデフォルトUIを置き換え
/// </summary>
public partial class DownloadProgressWindow : Window, IDownloadProgress
{
    private readonly DownloadProgressViewModel _viewModel;

    /// <summary>
    /// デザイナー/XAMLローダー用パラメータなしコンストラクタ
    /// </summary>
    public DownloadProgressWindow() : this(null!)
    {
    }

    public DownloadProgressWindow(DownloadProgressViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // ViewModelのイベントをNetSparkleイベントにバインド
        _viewModel.ActionRequested += OnActionRequested;
    }

    private void OnActionRequested(object? sender, bool install)
    {
        // NetSparkleに通知
        DownloadProcessCompleted?.Invoke(this, new DownloadInstallEventArgs(install));

        if (!install)
        {
            // キャンセル時はウィンドウを閉じる
            Close();
        }
        // インストール時はNetSparkleがウィンドウを閉じる
    }

    /// <summary>
    /// ウィンドウが閉じられたときにリソースをクリーンアップ
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _viewModel.ActionRequested -= OnActionRequested;
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    #region IDownloadProgress Implementation

    /// <summary>
    /// ダウンロード処理完了イベント
    /// </summary>
    public event DownloadInstallEventHandler? DownloadProcessCompleted;

    /// <summary>
    /// ダウンロード/インストールボタンの有効状態を設定
    /// </summary>
    public void SetDownloadAndInstallButtonEnabled(bool shouldBeEnabled)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _viewModel.IsActionButtonEnabled = shouldBeEnabled;
        });
    }

    /// <summary>
    /// ウィンドウを表示
    /// </summary>
    void IDownloadProgress.Show()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Show();
            Activate();
        });
    }

    /// <summary>
    /// ダウンロード進行状況を更新
    /// </summary>
    public void OnDownloadProgressChanged(object sender, ItemDownloadProgressEventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _viewModel.UpdateProgress(args.BytesReceived, args.TotalBytesToReceive, args.ProgressPercentage);
        });
    }

    /// <summary>
    /// ウィンドウを閉じる
    /// </summary>
    void IDownloadProgress.Close()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Close();
        });
    }

    /// <summary>
    /// ダウンロード完了を通知
    /// </summary>
    public void FinishedDownloadingFile(bool isDownloadedFileValid)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _viewModel.SetDownloadComplete(isDownloadedFileValid);
        });
    }

    /// <summary>
    /// エラーメッセージを表示
    /// </summary>
    public bool DisplayErrorMessage(string errorMessage)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _viewModel.ShowError(errorMessage);
        });
        return true; // メッセージが表示されたことを示す
    }

    #endregion
}
