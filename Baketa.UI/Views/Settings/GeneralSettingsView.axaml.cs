using Avalonia.Controls;
using Baketa.UI.ViewModels.Settings;

namespace Baketa.UI.Views.Settings;

/// <summary>
/// 一般設定画面のView
/// </summary>
public partial class GeneralSettingsView : UserControl
{
    /// <summary>
    /// GeneralSettingsViewを初期化します
    /// </summary>
    public GeneralSettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// GeneralSettingsViewを初期化します
    /// </summary>
    /// <param name="viewModel">一般設定ViewModel</param>
    public GeneralSettingsView(GeneralSettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
