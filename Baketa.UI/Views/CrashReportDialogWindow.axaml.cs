using System;
using Avalonia.Controls;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Views;

/// <summary>
/// [Issue #252] クラッシュレポートダイアログウィンドウ
/// 前回クラッシュ時のレポート送信を促すダイアログ
/// </summary>
public partial class CrashReportDialogWindow : Window
{
    private readonly CrashReportDialogViewModel _viewModel;

    /// <summary>
    /// デザイナー/XAMLローダー用パラメータなしコンストラクタ
    /// </summary>
    public CrashReportDialogWindow() : this(null!)
    {
    }

    public CrashReportDialogWindow(CrashReportDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // ViewModelのイベントをウィンドウアクションにバインド
        _viewModel.CloseRequested += OnViewModelCloseRequested;
    }

    private void OnViewModelCloseRequested(object? sender, CrashReportDialogResult result)
    {
        Close(result);
    }

    /// <summary>
    /// ウィンドウが閉じられたときにリソースをクリーンアップ
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _viewModel.CloseRequested -= OnViewModelCloseRequested;
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    /// <summary>
    /// 結果を取得
    /// </summary>
    public CrashReportDialogResult Result => _viewModel.Result;

    /// <summary>
    /// システム情報を含めるかどうか
    /// </summary>
    public bool IncludeSystemInfo => _viewModel.IncludeSystemInfo;

    /// <summary>
    /// ログを含めるかどうか
    /// </summary>
    public bool IncludeLogs => _viewModel.IncludeLogs;
}
