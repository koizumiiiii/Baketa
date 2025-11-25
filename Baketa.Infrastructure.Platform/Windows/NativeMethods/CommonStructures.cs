using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Baketa.Infrastructure.Platform.Windows.NativeMethods;

/// <summary>
/// RECT構造体
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct RECT
{
    public readonly int left;
    public readonly int top;
    public readonly int right;
    public readonly int bottom;

    // コンストラクタを追加してreadonlyメンバーを初期化できるようにする
    public RECT(int left, int top, int right, int bottom)
    {
        this.left = left;
        this.top = top;
        this.right = right;
        this.bottom = bottom;
    }

    public int Width => right - left;
    public int Height => bottom - top;

    public override string ToString() => $"[{left}, {top}, {right}, {bottom}]";
}

/// <summary>
/// POINT構造体
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct POINT
{
    public readonly int X;
    public readonly int Y;

    public POINT(int x, int y)
    {
        X = x;
        Y = y;
    }

    public override string ToString() => $"({X}, {Y})";
}

/// <summary>
/// SIZE構造体
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct SIZE
{
    public readonly int Width;
    public readonly int Height;

    public SIZE(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>
/// MONITORINFO構造体
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal struct MONITORINFO
{
    public uint cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;

    public static MONITORINFO Create()
    {
        return new MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<MONITORINFO>()
        };
    }
}

/// <summary>
/// GetWindowLong用のインデックス
/// </summary>
internal enum GetWindowLongIndex
{
    GWL_STYLE = -16,
    GWL_EXSTYLE = -20
}

/// <summary>
/// ウィンドウスタイル
/// </summary>
[Flags]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1069:Enums values should not be duplicated", Justification = "Windows API constants have intentional duplicate values")]
internal enum WindowStyles : long
{
    WS_OVERLAPPED = 0x00000000L,
    WS_POPUP = 0x80000000L,
    WS_CHILD = 0x40000000L,
    WS_MINIMIZE = 0x20000000L,
    WS_VISIBLE = 0x10000000L,
    WS_DISABLED = 0x08000000L,
    WS_CLIPSIBLINGS = 0x04000000L,
    WS_CLIPCHILDREN = 0x02000000L,
    WS_MAXIMIZE = 0x01000000L,
    WS_CAPTION = 0x00C00000L,
    WS_BORDER = 0x00800000L,
    WS_DLGFRAME = 0x00400000L,
    WS_VSCROLL = 0x00200000L,
    WS_HSCROLL = 0x00100000L,
    WS_SYSMENU = 0x00080000L,
    WS_THICKFRAME = 0x00040000L,
    WS_GROUP = 0x00020000L,
    WS_TABSTOP = 0x00010000L,
    WS_MINIMIZEBOX = 0x00020000L,
    WS_MAXIMIZEBOX = 0x00010000L,
    WS_OVERLAPPEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX,
    WS_POPUPWINDOW = WS_POPUP | WS_BORDER | WS_SYSMENU
}

/// <summary>
/// 拡張ウィンドウスタイル
/// </summary>
[Flags]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1069:Enums values should not be duplicated", Justification = "Windows API constants have intentional duplicate values")]
internal enum ExtendedWindowStyles : long
{
    WS_EX_DLGMODALFRAME = 0x00000001L,
    WS_EX_NOPARENTNOTIFY = 0x00000004L,
    WS_EX_TOPMOST = 0x00000008L,
    WS_EX_ACCEPTFILES = 0x00000010L,
    WS_EX_TRANSPARENT = 0x00000020L,
    WS_EX_MDICHILD = 0x00000040L,
    WS_EX_TOOLWINDOW = 0x00000080L,
    WS_EX_WINDOWEDGE = 0x00000100L,
    WS_EX_CLIENTEDGE = 0x00000200L,
    WS_EX_CONTEXTHELP = 0x00000400L,
    WS_EX_RIGHT = 0x00001000L,
    WS_EX_LEFT = 0x00000000L,
    WS_EX_RTLREADING = 0x00002000L,
    WS_EX_LTRREADING = 0x00000000L,
    WS_EX_LEFTSCROLLBAR = 0x00004000L,
    WS_EX_RIGHTSCROLLBAR = 0x00000000L,
    WS_EX_CONTROLPARENT = 0x00010000L,
    WS_EX_STATICEDGE = 0x00020000L,
    WS_EX_APPWINDOW = 0x00040000L,
    WS_EX_OVERLAPPEDWINDOW = WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE,
    WS_EX_PALETTEWINDOW = WS_EX_WINDOWEDGE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
}

/// <summary>
/// MonitorFromWindow用のフラグ
/// </summary>
internal enum MonitorFlags : uint
{
    MONITOR_DEFAULTTONULL = 0x00000000,
    MONITOR_DEFAULTTOPRIMARY = 0x00000001,
    MONITOR_DEFAULTTONEAREST = 0x00000002
}

/// <summary>
/// MONITORINFOEX構造体（モニター名を含む拡張版）
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MONITORINFOEX
{
    public uint cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string szDevice;

    public static MONITORINFOEX Create()
    {
        return new MONITORINFOEX
        {
            cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>(),
            szDevice = string.Empty
        };
    }
}

/// <summary>
/// DPI取得タイプ
/// </summary>
internal enum DpiType : uint
{
    Effective = 0,
    Angular = 1,
    Raw = 2
}

/// <summary>
/// EnumDisplayMonitors用のデリゲート
/// </summary>
/// <param name="hMonitor">モニターハンドル</param>
/// <param name="hdcMonitor">モニターのデバイスコンテキスト</param>
/// <param name="lprcMonitor">モニターの矩形</param>
/// <param name="dwData">ユーザーデータ</param>
/// <returns>継続するかどうか</returns>
internal delegate bool EnumMonitorsDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

/// <summary>
/// ウィンドウイベントプロシージャデリゲート
/// </summary>
internal delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
    int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

/// <summary>
/// ウィンドウプロシージャデリゲート
/// </summary>
internal delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

/// <summary>
/// WNDCLASS構造体
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WNDCLASS
{
    public uint style;
    public IntPtr lpfnWndProc;  // デリゲートではなくIntPtrを使用
    public int cbClsExtra;
    public int cbWndExtra;
    public IntPtr hInstance;
    public IntPtr hIcon;
    public IntPtr hCursor;
    public IntPtr hbrBackground;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string? lpszMenuName;
    [MarshalAs(UnmanagedType.LPWStr)]
    public string lpszClassName;
}

/// <summary>
/// 🔥 [DWM_BLUR_IMPLEMENTATION] PAINTSTRUCT構造体
/// </summary>
/// <remarks>
/// WM_PAINT処理に使用される構造体。
/// BeginPaint/EndPaint APIで使用されます。
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct PAINTSTRUCT
{
    public IntPtr hdc;
    public bool fErase;
    public RECT rcPaint;
    public bool fRestore;
    public bool fIncUpdate;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] rgbReserved;

    public PAINTSTRUCT()
    {
        hdc = IntPtr.Zero;
        fErase = false;
        rcPaint = new RECT();
        fRestore = false;
        fIncUpdate = false;
        rgbReserved = new byte[32];
    }
}
