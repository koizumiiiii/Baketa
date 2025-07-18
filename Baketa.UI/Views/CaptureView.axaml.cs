using Avalonia.Controls;
using Avalonia.ReactiveUI;
using Baketa.UI.ViewModels;
using ReactiveUI;

namespace Baketa.UI.Views;

    /// <summary>
    /// キャプチャ設定画面のビュー
    /// </summary>
    internal sealed partial class CaptureView : ReactiveUserControl<CaptureViewModel>
    {
        public CaptureView()
        {
            InitializeComponent();
            
            this.WhenActivated(disposables => 
            {
                // ビューモデルとのバインディングを設定
            });
        }
    }
