using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Views;

public partial class WindowSelectionDialogView : Window
{
    public WindowSelectionDialogView()
    {
        InitializeComponent();
        
        // ViewModelの変更を監視してダイアログを閉じる
        DataContextChanged += OnDataContextChanged;
        
        // ClosedイベントでViewModelに通知
        Closed += OnWindowClosed;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is WindowSelectionDialogViewModel viewModel)
        {
            // IsClosed プロパティの変更を監視
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(WindowSelectionDialogViewModel.IsClosed) && viewModel.IsClosed)
                {
                    Close(viewModel.DialogResult);
                }
            };
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        
        // ダイアログの位置を画面中央に設定
        if (VisualRoot is Window)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }

    /// <summary>
    /// ウィンドウアイテムのクリックイベント処理
    /// </summary>
    private void OnWindowItemClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is WindowInfo windowInfo)
        {
            if (DataContext is WindowSelectionDialogViewModel viewModel)
            {
                // クリック数に応じて処理を分ける
                if (e.ClickCount == 1)
                {
                    // シングルクリック: 選択状態を設定
                    viewModel.SelectedWindow = windowInfo;
                }
                else if (e.ClickCount >= 2)
                {
                    // ダブルクリック: 選択して即座に決定
                    viewModel.SelectedWindow = windowInfo;
                    
                    // ダブルクリックで即座に選択を実行
                    viewModel.SelectWindowCommand.Execute(windowInfo);
                }
            }
        }
    }
    
    /// <summary>
    /// ウィンドウが閉じられた時のViewModel清理
    /// </summary>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (DataContext is WindowSelectionDialogViewModel viewModel && !viewModel.IsClosed)
        {
            // ウィンドウが強制的に閉じられた場合はキャンセルとして扱う
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                viewModel.CancelCommand.Execute(System.Reactive.Unit.Default);
            });
        }
    }
    
}