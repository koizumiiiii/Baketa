using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Views;

/// <summary>
/// [Issue #261] 同意ダイアログウィンドウ
/// </summary>
public partial class ConsentDialogWindow : Window
{
    private readonly ConsentDialogViewModel? _viewModel;

    /// <summary>
    /// デザイナー用コンストラクタ（実行時は使用しない）
    /// </summary>
    public ConsentDialogWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ViewModelを指定してウィンドウを作成
    /// </summary>
    public ConsentDialogWindow(ConsentDialogViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        // ボタンクリック時にウィンドウを閉じる
        var acceptButton = this.FindControl<Button>("AcceptButton");
        var declineButton = this.FindControl<Button>("DeclineButton");

        if (acceptButton != null)
        {
            acceptButton.Click += OnAcceptClick;
        }

        if (declineButton != null)
        {
            declineButton.Click += OnDeclineClick;
        }
    }

    private void OnAcceptClick(object? sender, RoutedEventArgs e)
    {
        Close(_viewModel?.Result ?? ConsentDialogResult.Declined);
    }

    private void OnDeclineClick(object? sender, RoutedEventArgs e)
    {
        Close(ConsentDialogResult.Declined);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _viewModel?.Dispose();
    }
}
