using System;
using System.Reactive.Linq;
using Avalonia.Controls;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Views;

/// <summary>
/// [Issue #495] 初回セットアップウィザードウィンドウ
/// </summary>
public partial class FirstRunWizardWindow : Window
{
    /// <summary>
    /// デザイナー用コンストラクタ
    /// </summary>
    public FirstRunWizardWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ViewModelを指定してウィンドウを作成
    /// </summary>
    public FirstRunWizardWindow(FirstRunWizardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // ConfirmCommand完了後にウィンドウを閉じる（Command bindingと競合しない）
        viewModel.ConfirmCommand
            .Where(_ => viewModel.IsCompleted)
            .Take(1)
            .Subscribe(_ => Close(true));
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
    }
}
