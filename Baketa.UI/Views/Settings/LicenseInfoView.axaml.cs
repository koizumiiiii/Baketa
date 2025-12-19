using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Baketa.UI.Views.Settings;

/// <summary>
/// ライセンス情報設定画面のView
/// </summary>
public partial class LicenseInfoView : UserControl
{
    public LicenseInfoView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
