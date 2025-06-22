using Avalonia.Controls;
using Baketa.UI.ViewModels.Settings;

namespace Baketa.UI.Views.Settings;

/// <summary>
/// 拡張設定画面のView
/// </summary>
public partial class AdvancedSettingsView : UserControl
{
    /// <summary>
    /// AdvancedSettingsViewを初期化します
    /// </summary>
    public AdvancedSettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ViewModelを指定してAdvancedSettingsViewを初期化します
    /// </summary>
    /// <param name="viewModel">拡張設定ViewModel</param>
    public AdvancedSettingsView(AdvancedSettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
