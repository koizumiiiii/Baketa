using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Baketa.UI.Views;

public partial class FinalReleaseNoticeWindow : Window
{
    public FinalReleaseNoticeWindow() => InitializeComponent();

    private void OnOpenSettingsClick(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close(false);
}
