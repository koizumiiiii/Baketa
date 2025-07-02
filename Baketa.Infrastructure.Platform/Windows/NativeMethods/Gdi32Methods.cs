using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Baketa.Infrastructure.Platform.Windows.NativeMethods;

    /// <summary>
    /// Gdi32.dll P/Invoke定義
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class Gdi32Methods
    {
        // CA5392警告対策：LoadLibraryのパスを明示的に指定
        private const string GDI32_DLL = @"C:\Windows\System32\gdi32.dll";
        
        [DllImport(GDI32_DLL, SetLastError = true, ExactSpelling = true)]
        internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        
        [DllImport(GDI32_DLL, SetLastError = true, ExactSpelling = true)]
        internal static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
        
        [DllImport(GDI32_DLL, SetLastError = true, ExactSpelling = true)]
        internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        
        [DllImport(GDI32_DLL, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, BitBltFlags dwRop);
        
        [DllImport(GDI32_DLL, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteDC(IntPtr hdc);
        
        [DllImport(GDI32_DLL, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteObject(IntPtr hObject);
    }
    
    [Flags]
    internal enum BitBltFlags : uint
    {
        SRCCOPY = 0x00CC0020
    }
