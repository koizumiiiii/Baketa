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

        // [Issue #344] 画面左下に配置（Openedイベントで実際の高さ取得後に位置設定）
        this.Opened += OnWindowOpened;

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

    /// <summary>
    /// [Issue #344] ウィンドウが開いた後に画面左下に配置
    /// Openedイベントで実際の高さを取得してから位置を計算
    /// </summary>
    private void OnWindowOpened(object? sender, EventArgs e)
    {
        var screen = Screens.Primary;
        if (screen != null)
        {
            var bounds = screen.WorkingArea;
            const int margin = 16;
            var x = margin; // 左マージン
            var y = bounds.Height - (int)Bounds.Height - margin; // 画面左下

            Position = new PixelPoint(x, (int)y);
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
