using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Baketa.Infrastructure.Platform.Windows.NativeMethods
{
    /// <summary>
    /// User32.dll P/Invoke定義
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class User32Methods
    {
        // CA5392警告対策：LoadLibraryのパスを明示的に指定
        private const string USER32_DLL = @"C:\Windows\System32\user32.dll";
        
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
        
        [DllImport(USER32_DLL, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = false)]
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
        
        [DllImport(USER32_DLL, SetLastError = true, ExactSpelling = true)]
        internal static extern int GetWindowTextLength(IntPtr hWnd);
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
}