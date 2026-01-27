using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace Baketa.UI.Utils;

/// <summary>
/// Avaloniaウィンドウでクリックスルー（マウスイベント透過）を実現するヘルパー
/// WndProcをフックしてWM_NCHITTESTにHTTRANSPARENTを返すことで、
/// WS_EX_TRANSPARENTだけでは解決できないAvaloniaとの競合問題を回避する
/// </summary>
/// <remarks>
/// Issue #340: 翻訳オーバーレイのクリックスルーが効かない問題の修正
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ClickThroughHelper : IDisposable
{
    // Win32 Constants
    private const int GWL_WNDPROC = -4;
    private const uint WM_NCHITTEST = 0x0084;
    private const nint HTTRANSPARENT = -1;

    // P/Invoke declarations
    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

    // Instance fields
    private readonly nint _hwnd;
    private readonly nint _oldWndProc;
    private readonly WndProcDelegate _wndProcDelegate;
    private bool _disposed;

    /// <summary>
    /// 指定されたウィンドウハンドルに対してクリックスルーを有効化
    /// </summary>
    /// <param name="hwnd">ウィンドウハンドル</param>
    private ClickThroughHelper(nint hwnd)
    {
        _hwnd = hwnd;

        // デリゲートをフィールドに保持してGC回収を防止
        _wndProcDelegate = HookWndProc;

        // WndProcをフック
        nint newWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        _oldWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC, newWndProc);

        // 🔧 [Geminiレビュー対応] SetWindowLongPtr失敗時は例外をスローして不正状態を防止
        if (_oldWndProc == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt",
                $"❌ [ClickThroughHelper] SetWindowLongPtr failed - Error: {error}");
            throw new Win32Exception(error, "Failed to set new window procedure for click-through.");
        }

        SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt",
            $"✅ [ClickThroughHelper] WndProc hooked successfully - HWND: 0x{_hwnd:X}");
    }

    /// <summary>
    /// カスタムWndProc - WM_NCHITTESTをインターセプトしてHTTRANSPARENTを返す
    /// </summary>
    private nint HookWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_NCHITTEST)
        {
            // マウスイベントを背後のウィンドウに透過させる
            return HTTRANSPARENT;
        }

        // その他のメッセージは元のWndProcに転送
        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Avaloniaウィンドウにクリックスルーを適用
    /// </summary>
    /// <param name="window">対象のAvaloniaウィンドウ</param>
    /// <returns>ClickThroughHelperインスタンス（Disposeで元に戻す）</returns>
    public static ClickThroughHelper? Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
            {
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt",
                    "⚠️ [ClickThroughHelper] Could not get window handle");
                return null;
            }

            return new ClickThroughHelper(hwnd);
        }
        catch (Exception ex)
        {
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt",
                $"❌ [ClickThroughHelper] Apply failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 元のWndProcを復元
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_oldWndProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_hwnd, GWL_WNDPROC, _oldWndProc);
                SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt",
                    $"✅ [ClickThroughHelper] WndProc restored - HWND: 0x{_hwnd:X}");
            }
        }
        catch (Exception ex)
        {
            SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt",
                $"⚠️ [ClickThroughHelper] Dispose error: {ex.Message}");
        }

        GC.SuppressFinalize(this);
    }

    ~ClickThroughHelper()
    {
        Dispose();
    }
}
