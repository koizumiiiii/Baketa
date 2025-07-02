using Avalonia.Controls;
using Baketa.UI.ViewModels.Settings;

namespace Baketa.UI.Views.Settings;

/// <summary>
/// OCR設定画面のView
/// </summary>
public partial class OcrSettingsView : UserControl
{
    /// <summary>
    /// OcrSettingsViewを初期化します
    /// </summary>
    public OcrSettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// OcrSettingsViewを初期化します
    /// </summary>
    /// <param name="viewModel">OCR設定ViewModel</param>
    public OcrSettingsView(OcrSettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
