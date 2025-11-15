using Avalonia.Controls;
using Baketa.UI.ViewModels.Auth;

namespace Baketa.UI.Views.Auth;

/// <summary>
/// サインアップ画面のView
/// </summary>
public partial class SignupView : Window
{
    /// <summary>
    /// SignupViewを初期化します（デザイナー用）
    /// </summary>
    public SignupView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// SignupViewを初期化します
    /// </summary>
    /// <param name="viewModel">SignupViewModel</param>
    public SignupView(SignupViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
