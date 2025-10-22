using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Baketa.Infrastructure.Platform.Windows.NativeMethods;

// 構造体とデリゲートの定義はCommonStructures.csに統一されました

/// <summary>
/// User32.dll P/Invoke定義
/// </summary>
[SupportedOSPlatform("windows")]
internal static class User32Methods
{
    // 🎯 Gemini Expert推奨: 標準DLL検索メカニズムを使用（移植性・堅牢性向上）
    // クライアントアプリケーションでは標準検索順序で十分安全
    private const string USER32_DLL = "user32.dll";
    
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PrintWindow(IntPtr hwnd, IntPtr hDC, PrintWindowFlags flags);
    
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    internal static extern IntPtr GetDC(IntPtr hWnd);
    
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    
    [DllImport(USER32_DLL, SetLastError = false, ExactSpelling = true)]
    internal static extern int GetSystemMetrics(SystemMetric smIndex);
    
    [DllImport(USER32_DLL, SetLastError = false, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessDPIAware();
    
    [DllImport(USER32_DLL, SetLastError = false, ExactSpelling = true)]
    internal static extern IntPtr GetForegroundWindow();
    
    [DllImport(USER32_DLL, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "FindWindowW")]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
    
    // 別のパターンによる最適化バージョン（CA1838警告対策）
    [DllImport(USER32_DLL, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = false)]
    private static extern int GetWindowTextW(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);
    
    // StringBuilderの代わりに固定バッファを使用するラッパー
    internal static int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount)
    {
        if (nMaxCount <= 0) return 0;
        
        char[] buffer = new char[nMaxCount];
        int length = GetWindowTextW(hWnd, buffer, nMaxCount);
        
        if (length > 0)
        {
            lpString.Clear();
            lpString.Append(buffer, 0, length);
        }
        
        return length;
    }
    
    [DllImport(USER32_DLL, EntryPoint = "GetWindowTextLengthW", SetLastError = true, ExactSpelling = true)]
    internal static extern int GetWindowTextLength(IntPtr hWnd);
    
    // フルスクリーン検出のための追加API
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    internal static extern IntPtr GetDesktopWindow();
    
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    internal static extern long GetWindowLong(IntPtr hWnd, GetWindowLongIndex nIndex);
    
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, MonitorFlags dwFlags);
    
    [DllImport(USER32_DLL, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsDelegate lpfnEnum, IntPtr dwData);
    
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hWnd);
    
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);
    
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(IntPtr hWnd);
    
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);
    
    // マルチモニターサポート用の追加API
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    internal static extern IntPtr MonitorFromPoint(POINT pt, MonitorFlags dwFlags);
    
    [DllImport(USER32_DLL, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
    
    // DPI取得用API（SHCore.dllから）
    [DllImport("SHCore.dll", SetLastError = true, ExactSpelling = true)]
    internal static extern int GetDpiForMonitor(IntPtr hmonitor, DpiType dpiType, out uint dpiX, out uint dpiY);
    
    // ウィンドウ作成・管理API（WindowsDisplayChangeListener用）
    [DllImport(USER32_DLL, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "CreateWindowExW")]
    internal static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        ushort lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x, int y,
        int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);
    
    [DllImport(USER32_DLL, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "RegisterClassW")]
    internal static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    // 🔥 [P0_GC_FIX] ウィンドウクラス登録解除（静的リソースクリーンアップ用）
    [DllImport(USER32_DLL, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "UnregisterClassW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr hWnd);
    
    [DllImport(USER32_DLL, SetLastError = false, CharSet = CharSet.Unicode, ExactSpelling = false)]
    internal static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "GetModuleHandleW")]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);
    
    // ウィンドウイベントフック（GameWindowTracker用）
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    internal static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);
    
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    
    // モニター情報フラグ
    internal const uint MONITORINFOF_PRIMARY = 0x00000001;
    
    // 特殊ウィンドウハンドル
    internal static readonly IntPtr HWND_MESSAGE = new(-3);
    
    [DllImport(USER32_DLL, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = false)]
    internal static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);
    
    // 🚀 UltraThink + Gemini推奨: EnumWindows軽量実装によるProcess.GetProcesses()代替
    // ウィンドウ列挙用デリゲート
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    
    [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    
    // GetWindowTextLength と IsIconic は既に定義済み（68行目、105行目）
}

[Flags]
internal enum PrintWindowFlags : uint
{
    PW_DEFAULT = 0,
    PW_CLIENTONLY = 1
}

internal enum SystemMetric
{
    SM_CXSCREEN = 0,
    SM_CYSCREEN = 1
}
