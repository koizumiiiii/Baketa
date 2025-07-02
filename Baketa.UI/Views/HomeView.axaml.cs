using Avalonia.Controls;
using Avalonia.ReactiveUI;
using Baketa.UI.ViewModels;
using ReactiveUI;

namespace Baketa.UI.Views;

    /// <summary>
    /// ホーム画面のビュー
    /// </summary>
    internal sealed partial class HomeView : ReactiveUserControl<HomeViewModel>
    {
        public HomeView()
        {
            InitializeComponent();
            
            this.WhenActivated(disposables => 
            {
                // ビューモデルとのバインディングを設定
            });
        }
    }
