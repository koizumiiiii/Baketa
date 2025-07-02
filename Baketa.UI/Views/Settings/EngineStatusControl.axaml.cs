using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Baketa.UI.Views.Settings;

public partial class EngineStatusControl : UserControl
{
    public EngineStatusControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
