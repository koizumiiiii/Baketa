using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Baketa.UI.Views.Settings;

public partial class TranslationStrategyControl : UserControl
{
    public TranslationStrategyControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
