using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Baketa.UI.Views.Settings;

public partial class EngineSelectionControl : UserControl
{
    public EngineSelectionControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
