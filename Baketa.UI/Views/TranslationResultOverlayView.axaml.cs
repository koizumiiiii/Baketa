using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Views;

public partial class TranslationResultOverlayView : Window
{
    public TranslationResultOverlayView()
    {
        InitializeComponent();
        
        // ウィンドウの設定
        DataContextChanged += OnDataContextChanged;
        
        // マウスイベントを無効化（オーバーレイがゲームプレイを邪魔しないように）
        this.IsHitTestVisible = false;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is TranslationResultOverlayViewModel viewModel)
        {
            // ViewModelの変更を監視
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TranslationResultOverlayViewModel.IsVisible))
                {
                    UpdateVisibility(viewModel.IsVisible);
                }
                else if (e.PropertyName == nameof(TranslationResultOverlayViewModel.PositionX) ||
                         e.PropertyName == nameof(TranslationResultOverlayViewModel.PositionY))
                {
                    UpdatePosition(viewModel.PositionX, viewModel.PositionY);
                }
            };
        }
    }

    private void UpdateVisibility(bool isVisible)
    {
        if (isVisible)
        {
            Show();
        }
        else
        {
            Hide();
        }
    }

    private void UpdatePosition(double x, double y)
    {
        // スクリーンサイズを考慮した位置調整
        var screen = Screens.Primary;
        if (screen != null)
        {
            var bounds = screen.WorkingArea;
            var adjustedX = Math.Max(0, Math.Min(x, bounds.Width - Width));
            var adjustedY = Math.Max(0, Math.Min(y, bounds.Height - Height));
            
            Position = new Avalonia.PixelPoint((int)adjustedX, (int)adjustedY);
        }
        else
        {
            Position = new Avalonia.PixelPoint((int)x, (int)y);
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        
        // 初期状態で非表示
        Hide();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        // オーバーレイはクリック不可
        e.Handled = false;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        // オーバーレイはマウスイベントを無視
        e.Handled = false;
    }
}