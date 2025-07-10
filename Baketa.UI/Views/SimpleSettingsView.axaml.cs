using Avalonia.Controls;
using Avalonia.Interactivity;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Views;

public partial class SimpleSettingsView : Window
{
    public SimpleSettingsView()
    {
        InitializeComponent();
        
        // ウィンドウの設定
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is SimpleSettingsViewModel viewModel)
        {
            // ViewModelの初期化
            _ = viewModel.LoadSettingsAsync();
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        
        // 初期フォーカス設定などがあれば追加
    }
}