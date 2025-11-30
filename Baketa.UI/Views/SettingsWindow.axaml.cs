using System;
using Avalonia.Controls;
using Avalonia.Threading;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Views;

/// <summary>
/// 設定ウィンドウのView
/// プログレッシブディスクロージャーによる基本/詳細設定の階層表示をサポート
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// SettingsWindowを初期化します
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();

        // ウィンドウが閉じられる際にカテゴリContentをクリア
        // これによりAvaloniaのビジュアルツリー問題を回避
        Closed += OnWindowClosed;

        // ウィンドウが開かれる際に選択中のカテゴリのContentを再作成
        Opened += OnWindowOpened;

        // DataContext変更時にViewModelのイベントをサブスクライブ
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// SettingsWindowを初期化します
    /// </summary>
    /// <param name="viewModel">設定ウィンドウのViewModel</param>
    public SettingsWindow(SettingsWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    /// <summary>
    /// DataContext変更時のイベントハンドラ
    /// </summary>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // ViewModelのCloseRequestedイベントをサブスクライブ
        if (DataContext is SettingsWindowViewModel viewModel)
        {
            viewModel.CloseRequested += OnCloseRequested;
        }
    }

    /// <summary>
    /// ウィンドウを閉じる要求を受け取った際のイベントハンドラ
    /// </summary>
    private void OnCloseRequested(object? sender, EventArgs e)
    {
        // UIスレッドでウィンドウを閉じる
        Dispatcher.UIThread.Post(() => Close());
    }

    /// <summary>
    /// ウィンドウが開かれた際のイベントハンドラ
    /// 選択中のカテゴリのContentが未作成の場合は再作成する
    /// </summary>
    private void OnWindowOpened(object? sender, EventArgs e)
    {
        // 選択中のカテゴリのContentを確実に再作成
        // ClearCategoryContentsでContentがnullになった後、同じViewModelが再利用される場合に対応
        if (DataContext is SettingsWindowViewModel viewModel)
        {
            viewModel.EnsureSelectedCategoryContent();
        }
    }

    /// <summary>
    /// ウィンドウが閉じられた際のイベントハンドラ
    /// </summary>
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // ViewModelのカテゴリContentをクリアして、次回表示時に新しいViewを作成できるようにする
        if (DataContext is SettingsWindowViewModel viewModel)
        {
            // イベントハンドラーの解除
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.ClearCategoryContents();
        }
    }
}
