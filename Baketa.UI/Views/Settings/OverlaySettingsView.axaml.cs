using Avalonia.Controls;
using Baketa.UI.ViewModels.Settings;

namespace Baketa.UI.Views.Settings;

/// <summary>
/// オーバーレイ設定画面のView
/// </summary>
public partial class OverlaySettingsView : UserControl
{
    /// <summary>
    /// OverlaySettingsViewを初期化します
    /// </summary>
    public OverlaySettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ViewModelを指定してOverlaySettingsViewを初期化します
    /// </summary>
    /// <param name="viewModel">オーバーレイ設定ViewModel</param>
    public OverlaySettingsView(OverlaySettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
