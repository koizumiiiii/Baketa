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
    }

    /// <summary>
    /// SettingsWindowを初期化します
    /// </summary>
    /// <param name="viewModel">設定ウィンドウのViewModel</param>
    public SettingsWindow(SettingsWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
