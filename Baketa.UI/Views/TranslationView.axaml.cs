using Avalonia.Controls;
using Avalonia.ReactiveUI;
using Baketa.UI.ViewModels;
using ReactiveUI;

namespace Baketa.UI.Views;

    /// <summary>
    /// 翻訳設定画面のビュー
    /// </summary>
    internal sealed partial class TranslationView : ReactiveUserControl<TranslationViewModel>
    {
        public TranslationView()
        {
            InitializeComponent();
            
            this.WhenActivated(disposables => 
            {
                // ビューモデルとのバインディングを設定
            });
        }
    }
