using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Views;

/// <summary>
/// エラー通知ウィンドウ
/// 画面中央最下部に配置されるエラーメッセージ表示ウィンドウ
/// </summary>
public partial class ErrorNotificationView : Window
{
    public ErrorNotificationView()
    {
        InitializeComponent();

        // ウィンドウが開かれたときに位置を調整
        Opened += OnWindowOpened;

        // IsVisibleプロパティの変更を監視
        this.GetObservable(IsVisibleProperty).Subscribe(OnIsVisibleChanged);
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        PositionWindowAtBottomCenter();
    }

    private void OnIsVisibleChanged(bool isVisible)
    {
        if (isVisible)
        {
            // 表示時に位置を再調整
            PositionWindowAtBottomCenter();

            // ウィンドウを表示
            Show();
        }
        else
        {
            // 非表示時にウィンドウを隠す
            Hide();
        }
    }

    /// <summary>
    /// ウィンドウを画面中央最下部に配置
    /// </summary>
    private void PositionWindowAtBottomCenter()
    {
        try
        {
            // プライマリスクリーンを取得
            var screen = Screens.Primary;
            if (screen == null)
            {
                // フォールバック：すべてのスクリーンから最初のものを取得
                screen = Screens.All.FirstOrDefault();
            }

            if (screen == null)
            {
                return;
            }

            // 作業領域（タスクバーを除いた領域）を取得
            var workingArea = screen.WorkingArea;

            // ウィンドウサイズを取得（DPIスケーリング考慮）
            var windowWidth = Width;
            var windowHeight = Height;

            // 画面中央最下部の位置を計算
            var bottomMargin = 20; // 画面下端からのマージン（px）
            var x = workingArea.X + (workingArea.Width - windowWidth) / 2;
            var y = workingArea.Y + workingArea.Height - windowHeight - bottomMargin;

            // ウィンドウ位置を設定
            Position = new PixelPoint((int)x, (int)y);
        }
        catch (Exception ex)
        {
            // エラーが発生してもクラッシュしないようにログ出力のみ
            System.Diagnostics.Debug.WriteLine($"ErrorNotificationView position adjustment failed: {ex.Message}");
        }
    }
}
