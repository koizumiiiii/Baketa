using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Baketa.Core.Abstractions.Services;

namespace Baketa.Infrastructure.Platform.Windows.Services;

/// <summary>
/// [Issue #497] Win32 APIによるカーソル状態プロバイダー
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class CursorStateProvider : ICursorStateProvider
{
    private const uint CURSOR_SHOWING = 0x00000001;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    public bool IsCursorHidden(out int screenX, out int screenY)
    {
        var state = GetCursorState();
        screenX = state.ScreenX;
        screenY = state.ScreenY;
        return state.IsHidden;
    }

    public CursorState GetCursorState()
    {
        var cursorInfo = new CURSORINFO { cbSize = (uint)Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref cursorInfo))
            return default;

        var isHidden = (cursorInfo.flags & CURSOR_SHOWING) == 0;
        return new CursorState(
            isHidden,
            cursorInfo.ptScreenPos.X,
            cursorInfo.ptScreenPos.Y,
            cursorInfo.flags,
            cursorInfo.hCursor);
    }

    public bool IsWindowForeground(nint windowHandle)
    {
        var foreground = GetForegroundWindow();

        // 直接比較
        if (foreground == windowHandle)
            return true;

        _ = GetWindowThreadProcessId(foreground, out var fgProcessId);
        _ = GetWindowThreadProcessId(windowHandle, out var targetProcessId);

        // ゲームの別ウィンドウ（親/子）がフォアグラウンドの場合
        if (fgProcessId != 0 && fgProcessId == targetProcessId)
            return true;

        // Baketaオーバーレイがフォアグラウンドの場合も許可
        // （通常の使用時はBaketaがゲームの上に表示されている）
        return fgProcessId == _ownProcessId;
    }

    public bool IsPointInClientArea(nint windowHandle, int screenX, int screenY)
    {
        if (!GetClientRect(windowHandle, out var clientRect))
            return false;

        var pt = new POINT(screenX, screenY);
        if (!ScreenToClient(windowHandle, ref pt))
            return false;

        return pt.X >= 0 && pt.X < clientRect.Right
            && pt.Y >= 0 && pt.Y < clientRect.Bottom;
    }

    #region P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public uint cbSize;
        public uint flags;
        public nint hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll", SetLastError = false, ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll", SetLastError = false, ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    #endregion
}
