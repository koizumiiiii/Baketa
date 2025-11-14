using System;
using Baketa.Infrastructure.Platform.Windows.NativeMethods;

namespace Baketa.Infrastructure.Platform.Windows.Services;

/// <summary>
/// Windows Native API への統一アクセスサービス
/// P/Invoke定義の一元化とアセンブリ境界を越えたアクセス提供
/// </summary>
public static class WindowsNativeService
{
    /// <summary>
    /// デスクトップウィンドウハンドルを取得
    /// </summary>
    /// <returns>デスクトップウィンドウのハンドル</returns>
    public static IntPtr GetDesktopWindowHandle()
    {
        return User32Methods.GetDesktopWindow();
    }

    /// <summary>
    /// フォアグラウンドウィンドウハンドルを取得
    /// </summary>
    /// <returns>フォアグラウンドウィンドウのハンドル</returns>
    public static IntPtr GetForegroundWindowHandle()
    {
        return User32Methods.GetForegroundWindow();
    }

    /// <summary>
    /// ウィンドウが有効かどうかを判定
    /// </summary>
    /// <param name="hWnd">ウィンドウハンドル</param>
    /// <returns>有効時true</returns>
    public static bool IsWindow(IntPtr hWnd)
    {
        return User32Methods.IsWindow(hWnd);
    }

    /// <summary>
    /// ウィンドウが表示状態かどうかを判定
    /// </summary>
    /// <param name="hWnd">ウィンドウハンドル</param>
    /// <returns>表示状態時true</returns>
    public static bool IsWindowVisible(IntPtr hWnd)
    {
        return User32Methods.IsWindowVisible(hWnd);
    }
}
