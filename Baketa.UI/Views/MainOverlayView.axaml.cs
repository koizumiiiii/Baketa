using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Baketa.UI.ViewModels;
using Baketa.UI.Utils;
using System;
using System.IO;

namespace Baketa.UI.Views;

public partial class MainOverlayView : Window
{
    public MainOverlayView()
    {
        Console.WriteLine("🔧 MainOverlayView初期化開始");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔧 MainOverlayView初期化開始");
        
        InitializeComponent();
        
        Console.WriteLine("🔧 MainOverlayView - InitializeComponent完了");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔧 MainOverlayView - InitializeComponent完了");
        
        // 画面左端から16px、縦中央に配置
        ConfigurePosition();
        
        // 可視性確認
        Console.WriteLine($"🔧 MainOverlayView - IsVisible: {IsVisible}");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔧 MainOverlayView - IsVisible: {IsVisible}");
        Console.WriteLine($"🔧 MainOverlayView - WindowState: {WindowState}");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔧 MainOverlayView - WindowState: {WindowState}");
    }
    
    private void ConfigurePosition()
    {
        // 画面サイズを取得
        var screen = Screens.Primary;
        if (screen != null)
        {
            var bounds = screen.WorkingArea;
            var windowHeight = 380; // 展開時の高さ値を使用（Exitボタンを含む）
            
            // X座標: 画面左端から16px
            var x = 16;
            
            // Y座標: 画面縦中央（オーバーレイ中央が画面中央に来るよう配置）
            var y = (bounds.Height - windowHeight) / 2;
            
            Position = new Avalonia.PixelPoint(x, (int)y);
        }
    }
    
    protected override void OnLoaded(RoutedEventArgs e)
    {
        Console.WriteLine("🔧 MainOverlayView - OnLoaded呼び出し");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔧 MainOverlayView - OnLoaded呼び出し");
        
        base.OnLoaded(e);
        
        // 位置を再設定（画面解像度が変わった可能性があるため）
        ConfigurePosition();
        
        // ウィンドウの状態確認
        Console.WriteLine($"🔧 MainOverlayView - OnLoaded後: IsVisible={IsVisible}, IsEnabled={IsEnabled}");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔧 MainOverlayView - OnLoaded後: IsVisible={IsVisible}, IsEnabled={IsEnabled}");
        Console.WriteLine($"🔧 MainOverlayView - Position: {Position}, Width: {Width}, Height: {Height}");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔧 MainOverlayView - Position: {Position}, Width: {Width}, Height: {Height}");
        
        // ウィンドウを前面に表示
        try
        {
            Show();
            Activate();
            Console.WriteLine("🔧 MainOverlayView - Show()とActivate()を実行");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔧 MainOverlayView - Show()とActivate()を実行");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🔧 MainOverlayView - Show/Activate失敗: {ex.Message}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔧 MainOverlayView - Show/Activate失敗: {ex.Message}");
        }
    }
    
    
    private void OnExitButtonClick(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine("🔴 ExitButtonClick呼び出し");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔴 ExitButtonClick呼び出し");
        
        try
        {
            // アプリケーション終了
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                Console.WriteLine("🔴 アプリケーション終了を実行");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔴 アプリケーション終了を実行");
                desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 アプリケーション終了エラー: {ex.Message}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 アプリケーション終了エラー: {ex.Message}");
        }
    }
}