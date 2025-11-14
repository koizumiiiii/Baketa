using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Baketa.Infrastructure.Platform.Windows.Overlay;

/// <summary>
/// オーバーレイウィンドウ関連のWin32 API定義
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class OverlayInterop
{
    // === 定数定義 ===

    // 拡張ウィンドウスタイル
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    // ウィンドウスタイル
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_VISIBLE = 0x10000000;

    // ShowWindow コマンド
    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
    public const int SW_SHOWNOACTIVATE = 4;

    // SetWindowPos フラグ
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;

    // GetWindowLong/SetWindowLong インデックス
    public const int GWL_EXSTYLE = -20;
    public const int GWL_STYLE = -16;

    // LayeredWindow属性
    public const uint LWA_ALPHA = 0x00000002;
    public const uint LWA_COLORKEY = 0x00000001;

    // UpdateLayeredWindow フラグ
    public const uint ULW_ALPHA = 0x00000002;
    public const uint ULW_COLORKEY = 0x00000001;
    public const uint ULW_OPAQUE = 0x00000004;

    // ヒットテスト結果
    public const int HTTRANSPARENT = -1;
    public const int HTCLIENT = 1;

    // ウィンドウメッセージ
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_DESTROY = 0x0002;

    // 特別なウィンドウハンドル
    public static readonly nint HWND_TOPMOST = new(-1);

    // === P/Invoke メソッド定義 ===

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint CreateWindowExW(
        int dwExStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string? lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(
        nint hwnd,
        uint crKey,
        byte bAlpha,
        uint dwFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateLayeredWindow(
        nint hwnd,
        nint hdcDst,
        ref POINT pptDst,
        ref SIZE psize,
        nint hdcSrc,
        ref POINT pprSrc,
        uint crKey,
        ref BLENDFUNCTION pblend,
        uint dwFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetWindowLongW(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint SetWindowLongW(nint hWnd, int nIndex, uint dwNewLong);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GetModuleHandleW([MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName);

    [LibraryImport("user32.dll")]
    public static partial nint LoadCursorW(nint hInstance, nint lpCursorName);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleBitmap(nint hdc, int width, int height);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint hdc, nint hgdiobj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint hObject);

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint hWnd, nint hDC);

    // GetLastErrorもLibraryImportに統一
    [LibraryImport("kernel32.dll")]
    public static partial uint GetLastError();
}

/// <summary>
/// RECT構造体
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RECT : IEquatable<RECT>
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;

    public readonly bool Equals(RECT other) =>
        Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

    public override readonly bool Equals(object? obj) => obj is RECT rect && Equals(rect);

    public override readonly int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    public static bool operator ==(RECT left, RECT right) => left.Equals(right);

    public static bool operator !=(RECT left, RECT right) => !left.Equals(right);
}

/// <summary>
/// POINT構造体
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct POINT(int x, int y) : IEquatable<POINT>
{
    public int X = x;
    public int Y = y;

    public readonly bool Equals(POINT other) => X == other.X && Y == other.Y;

    public override readonly bool Equals(object? obj) => obj is POINT point && Equals(point);

    public override readonly int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(POINT left, POINT right) => left.Equals(right);

    public static bool operator !=(POINT left, POINT right) => !left.Equals(right);
}

/// <summary>
/// SIZE構造体
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SIZE(int cx, int cy) : IEquatable<SIZE>
{
    public int cx = cx;
    public int cy = cy;

    public readonly bool Equals(SIZE other) => cx == other.cx && cy == other.cy;

    public override readonly bool Equals(object? obj) => obj is SIZE size && Equals(size);

    public override readonly int GetHashCode() => HashCode.Combine(cx, cy);

    public static bool operator ==(SIZE left, SIZE right) => left.Equals(right);

    public static bool operator !=(SIZE left, SIZE right) => !left.Equals(right);
}

/// <summary>
/// BLENDFUNCTION構造体
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BLENDFUNCTION : IEquatable<BLENDFUNCTION>
{
    public byte BlendOp;
    public byte BlendFlags;
    public byte SourceConstantAlpha;
    public byte AlphaFormat;

    public readonly bool Equals(BLENDFUNCTION other) =>
        BlendOp == other.BlendOp && BlendFlags == other.BlendFlags &&
        SourceConstantAlpha == other.SourceConstantAlpha && AlphaFormat == other.AlphaFormat;

    public override readonly bool Equals(object? obj) => obj is BLENDFUNCTION blend && Equals(blend);

    public override readonly int GetHashCode() =>
        HashCode.Combine(BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat);

    public static bool operator ==(BLENDFUNCTION left, BLENDFUNCTION right) => left.Equals(right);

    public static bool operator !=(BLENDFUNCTION left, BLENDFUNCTION right) => !left.Equals(right);
}

/// <summary>
/// WNDCLASSEXW構造体
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct WNDCLASSEXW : IEquatable<WNDCLASSEXW>
{
    public uint cbSize;
    public uint style;
    public nint lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string? lpszMenuName;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string lpszClassName;
    public nint hIconSm;

    public readonly bool Equals(WNDCLASSEXW other) =>
        cbSize == other.cbSize && style == other.style && lpfnWndProc == other.lpfnWndProc &&
        cbClsExtra == other.cbClsExtra && cbWndExtra == other.cbWndExtra &&
        hInstance == other.hInstance && hIcon == other.hIcon && hCursor == other.hCursor &&
        hbrBackground == other.hbrBackground && lpszMenuName == other.lpszMenuName &&
        lpszClassName == other.lpszClassName && hIconSm == other.hIconSm;

    public override readonly bool Equals(object? obj) => obj is WNDCLASSEXW wnd && Equals(wnd);

    public override readonly int GetHashCode() =>
        HashCode.Combine(cbSize, style, lpfnWndProc, cbClsExtra, cbWndExtra, hInstance, hIcon, hCursor);

    public static bool operator ==(WNDCLASSEXW left, WNDCLASSEXW right) => left.Equals(right);

    public static bool operator !=(WNDCLASSEXW left, WNDCLASSEXW right) => !left.Equals(right);
}
