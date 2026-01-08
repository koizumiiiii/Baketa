using System;
using Avalonia.Controls;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Views;

/// <summary>
/// [Issue #256] コンポーネント更新ダイアログウィンドウ
/// </summary>
public partial class ComponentUpdateDialogWindow : Window
{
    private readonly ComponentUpdateDialogViewModel? _viewModel;

    /// <summary>
    /// デザイナー/XAMLローダー用パラメータなしコンストラクタ
    /// </summary>
    public ComponentUpdateDialogWindow() : this(null!)
    {
    }

    public ComponentUpdateDialogWindow(ComponentUpdateDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // ViewModelのイベントをウィンドウアクションにバインド
        if (_viewModel != null)
        {
            _viewModel.CloseRequested += OnViewModelCloseRequested;
        }
    }

    private void OnViewModelCloseRequested(object? sender, ComponentUpdateDialogResult result)
    {
        Close(result);
    }

    /// <summary>
    /// ウィンドウが閉じられたときにリソースをクリーンアップ
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.CloseRequested -= OnViewModelCloseRequested;
            _viewModel.Dispose();
        }
        base.OnClosed(e);
    }

    /// <summary>
    /// ダイアログ結果
    /// </summary>
    public ComponentUpdateDialogResult Result => _viewModel?.Result ?? ComponentUpdateDialogResult.None;
}
