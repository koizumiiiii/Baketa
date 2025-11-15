using Avalonia.Controls;
using Avalonia.ReactiveUI;
using Baketa.UI.ViewModels;
using ReactiveUI;

namespace Baketa.UI.Views;

/// <summary>
/// オーバーレイ設定画面のビュー
/// </summary>
internal sealed partial class OverlayView : ReactiveUserControl<OverlayViewModel>
{
    public OverlayView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            // ビューモデルとのバインディングを設定
        });
    }
}
