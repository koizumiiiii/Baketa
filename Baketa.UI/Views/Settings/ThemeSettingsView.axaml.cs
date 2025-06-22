using Avalonia.Controls;
using Baketa.UI.ViewModels.Settings;

namespace Baketa.UI.Views.Settings;

/// <summary>
/// テーマ設定画面のView
/// </summary>
public partial class ThemeSettingsView : UserControl
{
    /// <summary>
    /// ThemeSettingsViewを初期化します
    /// </summary>
    public ThemeSettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ThemeSettingsViewを初期化します
    /// </summary>
    /// <param name="viewModel">テーマ設定ViewModel</param>
    public ThemeSettingsView(ThemeSettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
