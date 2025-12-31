using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Views;

/// <summary>
/// ローディング画面ウィンドウ
/// アプリケーション起動時の初期化中に表示されます
/// </summary>
public partial class LoadingWindow : Window
{
    public LoadingWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Issue #239: 閉じるボタンクリック時にアプリケーションを終了
    /// </summary>
    private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        // アプリケーション全体を終了
        Environment.Exit(0);
    }

    /// <summary>
    /// ウィンドウ表示後にフェードインアニメーションを開始
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // フェードインアニメーション開始
        Dispatcher.UIThread.Post(() =>
        {
            Classes.Add("loaded");
        }, DispatcherPriority.Render);
    }

    /// <summary>
    /// フェードアウトアニメーション後にウィンドウを閉じる
    /// </summary>
    public async Task CloseWithFadeOutAsync()
    {
        // UIスレッドで確実に実行
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // フェードアウトアニメーション
            Classes.Remove("loaded");
            await Task.Delay(300); // アニメーション時間と同期
            Close();
        });
    }
}
