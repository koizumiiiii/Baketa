using System;
using System.Threading.Tasks;

namespace Baketa.UI.Services;

/// <summary>
/// ナビゲーションサービスのインターフェース
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// ログイン画面を表示します
    /// </summary>
    /// <returns>ログインが成功した場合true</returns>
    Task<bool> ShowLoginAsync();

    /// <summary>
    /// サインアップ画面を表示します
    /// </summary>
    /// <returns>サインアップが成功した場合true</returns>
    Task<bool> ShowSignupAsync();

    /// <summary>
    /// メイン画面を表示します
    /// </summary>
    Task ShowMainWindowAsync();

    /// <summary>
    /// 設定画面を表示します
    /// </summary>
    Task ShowSettingsAsync();

    /// <summary>
    /// 現在のウィンドウを閉じます
    /// </summary>
    Task CloseCurrentWindowAsync();

    /// <summary>
    /// ログアウトして認証画面に戻ります
    /// </summary>
    Task LogoutAndShowLoginAsync();
}