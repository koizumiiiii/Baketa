using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Baketa.UI.Services;

namespace Baketa.UI.Views;

/// <summary>
/// トースト通知ウィンドウ
/// </summary>
public partial class ToastNotificationWindow : Window
{
    private readonly DispatcherTimer? _autoCloseTimer;

    public ToastNotificationWindow()
    {
        InitializeComponent();
    }

    public ToastNotificationWindow(NotificationType type, string title, string message, int durationMs)
        : this()
    {
        // タイトルとメッセージを設定
        var titleText = this.FindControl<TextBlock>("TitleText");
        var messageText = this.FindControl<TextBlock>("MessageText");
        var iconBorder = this.FindControl<Border>("IconBorder");
        var iconText = this.FindControl<TextBlock>("IconText");

        if (titleText != null) titleText.Text = title;
        if (messageText != null) messageText.Text = message;

        // タイプに応じたアイコンと色を設定
        var (icon, color) = type switch
        {
            NotificationType.Success => ("✓", "#28A745"),
            NotificationType.Warning => ("⚠", "#FFC107"),
            NotificationType.Error => ("✕", "#DC3545"),
            _ => ("ℹ", "#17A2B8")
        };

        if (iconText != null) iconText.Text = icon;
        if (iconBorder != null)
        {
            iconBorder.Background = new SolidColorBrush(Color.Parse(color));
            if (iconText != null) iconText.Foreground = Brushes.White;
        }

        // 画面左側に配置（MainOverlay近く）
        ConfigurePosition();

        // 自動クローズタイマー
        if (durationMs > 0)
        {
            _autoCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs)
            };
            _autoCloseTimer.Tick += (_, _) =>
            {
                _autoCloseTimer.Stop();
                CloseWithFadeOut();
            };
            _autoCloseTimer.Start();
        }
    }

    private void ConfigurePosition()
    {
        // 画面左端、MainOverlayの下（縦方向で60%の位置）に配置
        var screen = Screens.Primary;
        if (screen != null)
        {
            var bounds = screen.WorkingArea;
            var x = 16; // MainOverlayと同じ左マージン
            var y = (int)(bounds.Height * 0.6); // 画面の60%位置

            Position = new PixelPoint(x, y);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _autoCloseTimer?.Stop();
        CloseWithFadeOut();
    }

    private void CloseWithFadeOut()
    {
        // シンプルに即閉じ（アニメーションは省略）
        try
        {
            Close();
        }
        catch
        {
            // 既に閉じている場合は無視
        }
    }
}
