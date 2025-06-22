using Avalonia.Controls;
using Baketa.UI.ViewModels.Settings;

namespace Baketa.UI.Views.Settings;

/// <summary>
/// キャプチャ設定画面のView
/// </summary>
public partial class CaptureSettingsView : UserControl
{
    /// <summary>
    /// CaptureSettingsViewを初期化します
    /// </summary>
    public CaptureSettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ViewModelを指定してCaptureSettingsViewを初期化します
    /// </summary>
    /// <param name="viewModel">キャプチャ設定ViewModel</param>
    public CaptureSettingsView(CaptureSettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
