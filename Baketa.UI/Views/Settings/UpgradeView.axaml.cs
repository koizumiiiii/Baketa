using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Baketa.UI.Views.Settings;

/// <summary>
/// プランアップグレード画面のView
/// </summary>
public partial class UpgradeView : UserControl
{
    public UpgradeView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
