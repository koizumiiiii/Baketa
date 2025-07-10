using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Views;

public partial class MainOverlayView : Window
{
    public MainOverlayView()
    {
        InitializeComponent();
        
        // 画面左端から16px、縦中央に配置
        ConfigurePosition();
    }
    
    private void ConfigurePosition()
    {
        // 画面サイズを取得
        var screen = Screens.Primary;
        if (screen != null)
        {
            var bounds = screen.WorkingArea;
            var windowHeight = 280; // XAMLで設定したHeight値を使用
            
            // X座標: 画面左端から16px
            var x = 16;
            
            // Y座標: 画面縦中央（オーバーレイ中央が画面中央に来るよう配置）
            var y = (bounds.Height - windowHeight) / 2;
            
            Position = new Avalonia.PixelPoint(x, (int)y);
        }
    }
    
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        
        // 位置を再設定（画面解像度が変わった可能性があるため）
        ConfigurePosition();
    }
}