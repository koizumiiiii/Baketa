using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Views;

    /// <summary>
    /// アクセシビリティ設定ビューのコードビハインド
    /// </summary>
    public partial class AccessibilitySettingsView : UserControl
    {
        /// <summary>
        /// 新しいアクセシビリティ設定ビューを初期化します。
        /// </summary>
        public AccessibilitySettingsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
