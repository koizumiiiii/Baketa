using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Baketa.UI.Views;

/// <summary>
/// [Issue #545] ウェルカムボーナス通知ダイアログ
/// アカウント作成時にEXモードのお試しボーナスが付与されたことを通知
/// </summary>
public partial class WelcomeBonusWindow : Window
{
    public WelcomeBonusWindow()
    {
        InitializeComponent();
    }

    public string DialogTitle { get; set; } = "";
    public string Message { get; set; } = "";
    public string OkButtonText { get; set; } = "OK";

    /// <summary>
    /// プロパティ設定後にDataContextをバインド（変更通知なしのため）
    /// </summary>
    private void ApplyBindings() => DataContext = this;

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// ウェルカムボーナスダイアログを表示
    /// </summary>
    public static async Task ShowAsync(long bonusAmount, Window? owner = null)
    {
        var normalizedAmount = (bonusAmount + 50) / 100; // 1/100正規化

        var title = Baketa.UI.Resources.Strings.WelcomeBonus_Title ?? "Thanks for signing up!";
        var message = string.Format(
            Baketa.UI.Resources.Strings.WelcomeBonus_Message ?? "You've received a bonus of {0} uses to try out EX Mode.",
            normalizedAmount);
        var okText = Baketa.UI.Resources.Strings.WelcomeBonus_OK ?? "OK";

        var dialog = new WelcomeBonusWindow
        {
            DialogTitle = title,
            Message = message,
            OkButtonText = okText
        };
        dialog.Title = title;
        dialog.ApplyBindings();

        if (owner != null)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
            dialog.Activate();
        }
    }
}
