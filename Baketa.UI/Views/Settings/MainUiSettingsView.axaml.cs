using Avalonia.Controls;
using Baketa.UI.ViewModels.Settings;

namespace Baketa.UI.Views.Settings;

/// <summary>
/// メイン操作UI設定画面のView
/// プログレッシブディスクロージャーによる基本/詳細設定の階層表示をサポート
/// </summary>
public partial class MainUiSettingsView : UserControl
{
    /// <summary>
    /// MainUiSettingsViewを初期化します
    /// </summary>
    public MainUiSettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ViewModelを指定してMainUiSettingsViewを初期化します
    /// </summary>
    /// <param name="viewModel">メインUI設定ViewModel</param>
    public MainUiSettingsView(MainUiSettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
