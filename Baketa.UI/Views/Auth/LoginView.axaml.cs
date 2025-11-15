using Avalonia.Controls;
using Baketa.UI.ViewModels.Auth;

namespace Baketa.UI.Views.Auth;

/// <summary>
/// ログイン画面のView
/// </summary>
public partial class LoginView : Window
{
    /// <summary>
    /// LoginViewを初期化します（デザイナー用）
    /// </summary>
    public LoginView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// LoginViewを初期化します
    /// </summary>
    /// <param name="viewModel">LoginViewModel</param>
    public LoginView(LoginViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
