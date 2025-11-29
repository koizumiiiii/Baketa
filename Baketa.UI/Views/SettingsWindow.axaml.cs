using System;
using Avalonia.Controls;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Views;

/// <summary>
/// 設定ウィンドウのView
/// プログレッシブディスクロージャーによる基本/詳細設定の階層表示をサポート
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// SettingsWindowを初期化します
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();

        // ウィンドウが閉じられる際にカテゴリContentをクリア
        // これによりAvaloniaのビジュアルツリー問題を回避
        Closed += OnWindowClosed;
    }

    /// <summary>
    /// SettingsWindowを初期化します
    /// </summary>
    /// <param name="viewModel">設定ウィンドウのViewModel</param>
    public SettingsWindow(SettingsWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    /// <summary>
    /// ウィンドウが閉じられた際のイベントハンドラ
    /// </summary>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // ViewModelのカテゴリContentをクリアして、次回表示時に新しいViewを作成できるようにする
        if (DataContext is SettingsWindowViewModel viewModel)
        {
            viewModel.ClearCategoryContents();
        }
    }
}
