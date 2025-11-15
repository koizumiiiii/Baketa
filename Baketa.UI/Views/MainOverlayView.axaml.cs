using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Baketa.UI.Utils;
using Baketa.UI.ViewModels;

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

        // 🔥 [PHASE6.1_DIAGNOSTIC_DEEP] StartStopボタンのCommand/DataContext確認
        try
        {
            var startStopButton = this.FindControl<Button>("StartStopButton");
            if (startStopButton != null)
            {
                Console.WriteLine($"🔧🔧🔧 [BUTTON_BINDING] StartStopButton発見 - Command: {startStopButton.Command != null}, IsEnabled: {startStopButton.IsEnabled}, DataContext: {startStopButton.DataContext != null}");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔧🔧🔧 [BUTTON_BINDING] StartStopButton - Command: {startStopButton.Command != null}, IsEnabled: {startStopButton.IsEnabled}, DataContext: {startStopButton.DataContext != null}");

                if (DataContext is MainOverlayViewModel viewModel)
                {
                    Console.WriteLine($"🔧🔧🔧 [BUTTON_BINDING] ViewModel確認 - IsStartStopEnabled: {viewModel.IsStartStopEnabled}, IsTranslationActive: {viewModel.IsTranslationActive}");
                    SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔧🔧🔧 [BUTTON_BINDING] ViewModel - IsStartStopEnabled: {viewModel.IsStartStopEnabled}, IsTranslationActive: {viewModel.IsTranslationActive}");
                }
            }
            else
            {
                Console.WriteLine("❌ [BUTTON_BINDING] StartStopButton が見つかりません！");
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "❌ [BUTTON_BINDING] StartStopButton が見つかりません！");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [BUTTON_BINDING] ボタン検証エラー: {ex.Message}");
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [BUTTON_BINDING] ボタン検証エラー: {ex.Message}");
        }

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

    /// <summary>
    /// 🔧 [PHASE6.1_DIAGNOSTIC] StartStopボタンの物理的クリック検出
    /// 目的: ボタンがクリックされているかを100%確実に検証
    /// </summary>
    private void StartStopButton_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var button = sender as Button;
        var viewModel = DataContext as MainOverlayViewModel;

        Console.WriteLine($"🖱️ [DIAGNOSTIC] StartStopButton物理的クリック検出！");
        Console.WriteLine($"🖱️ [DIAGNOSTIC] Button.IsEnabled: {button?.IsEnabled}");
        Console.WriteLine($"🖱️ [DIAGNOSTIC] Button.Command: {button?.Command != null}");
        Console.WriteLine($"🖱️ [DIAGNOSTIC] ViewModel.IsTranslationActive: {viewModel?.IsTranslationActive}");
        Console.WriteLine($"🖱️ [DIAGNOSTIC] ViewModel.IsStartStopEnabled: {viewModel?.IsStartStopEnabled}");

        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖱️ [DIAGNOSTIC] StartStopButton物理的クリック検出！");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖱️ [DIAGNOSTIC] Button.IsEnabled: {button?.IsEnabled}");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖱️ [DIAGNOSTIC] Button.Command: {button?.Command != null}");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖱️ [DIAGNOSTIC] ViewModel.IsTranslationActive: {viewModel?.IsTranslationActive}");
        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🖱️ [DIAGNOSTIC] ViewModel.IsStartStopEnabled: {viewModel?.IsStartStopEnabled}");
    }
}
