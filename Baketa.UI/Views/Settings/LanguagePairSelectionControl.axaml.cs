using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Baketa.UI.Views.Settings;

public partial class LanguagePairSelectionControl : UserControl
{
    public LanguagePairSelectionControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
