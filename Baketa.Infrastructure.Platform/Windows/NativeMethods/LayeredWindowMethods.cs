using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Baketa.Infrastructure.Platform.Windows.NativeMethods;

/// <summary>
/// Win32 Layered Window用のP/Invoke定義
/// WS_EX_LAYERED スタイルのウィンドウ作成・操作に必要なAPI
/// </summary>
/// <remarks>
/// 🎯 [WIN32_OVERLAY_MIGRATION] Phase 1: Layered Window基盤実装
/// - OS-native透過ウィンドウの作成
/// - UpdateLayeredWindow によるピクセル単位のアルファブレンディング
/// - SetLayeredWindowAttributes による透明度制御
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class LayeredWindowMethods
{
    private const string USER32_DLL = "user32.dll";
    private const string GDI32_DLL = "gdi32.dll";

    // ========================================
    // Window Creation & Management
    // ========================================

    /// <summary>
    /// メッセージ処理
    /// </summary>
    /// <remarks>
    /// 🔥 [WIN32_FIX] ExactSpelling削除 - GetMessageA/GetMessageWの自動選択を有効化
    /// ExactSpelling=trueでは「GetMessage」という名前の関数を探すが、実際にはGetMessageA/Wしか存在しない
    /// </remarks>
    [DllImport(USER32_DLL, SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport(USER32_DLL, SetLastError = false, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport(USER32_DLL, SetLastError = false, CharSet = CharSet.Auto)]
    internal static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport(USER32_DLL, SetLastError = true)]
    internal static extern void PostQuitMessage(int nExitCode);

    /// <summary>
    /// メッセージをウィンドウのメッセージキューに投稿
    /// </summary>
    /// <remarks>
    /// 🔥 [MESSAGE_QUEUE_FIX] カスタムメッセージキューの処理をトリガー
    /// GetMessage()のブロックを解除し、_messageQueueを処理させる
    /// </remarks>
    [DllImport(USER32_DLL, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// ウィンドウ位置・サイズ設定
    /// </summary>
    [DllImport(USER32_DLL, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x, int y,
        int cx, int cy,
        SetWindowPosFlags uFlags);

    /// <summary>
    /// ウィンドウ表示状態設定
    /// </summary>
    [DllImport(USER32_DLL, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommands nCmdShow);

    // ========================================
    // Layered Window Specific APIs
    // ========================================

    /// <summary>
    /// レイヤードウィンドウの透明度・カラーキー設定
    /// </summary>
    /// <remarks>
    /// 🔥 [GEMINI_RECOMMENDATION] 簡易的な透明度制御に使用
    /// UpdateLayeredWindow より軽量だが、アルファブレンディングは全体一律
    /// </remarks>
    [DllImport(USER32_DLL, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(
        IntPtr hwnd,
        uint crKey,
        byte bAlpha,
        LayeredWindowAttributes dwFlags);

    /// <summary>
    /// レイヤードウィンドウのビットマップ更新
    /// </summary>
    /// <remarks>
    /// 🔥 [GEMINI_RECOMMENDATION] ピクセル単位のアルファブレンディング
    /// GDI32で描画したビットマップを直接ウィンドウに転送
    /// </remarks>
    [DllImport(USER32_DLL, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref POINT pptDst,
        ref SIZE psize,
        IntPtr hdcSrc,
        ref POINT pptSrc,
        uint crKey,
        ref BLENDFUNCTION pblend,
        UpdateLayeredWindowFlags dwFlags);

    // ========================================
    // GDI32 APIs for Bitmap Rendering
    // ========================================

    /// <summary>
    /// メモリデバイスコンテキスト作成
    /// </summary>
    [DllImport(GDI32_DLL, SetLastError = true)]
    internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    /// <summary>
    /// ビットマップ作成（DIB: Device Independent Bitmap）
    /// </summary>
    /// <remarks>
    /// 🔥 [GEMINI_RECOMMENDATION] 32bit ARGB ビットマップ作成用
    /// ppvBits でピクセルデータへの直接アクセス可能
    /// </remarks>
    [DllImport(GDI32_DLL, SetLastError = true)]
    internal static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BITMAPINFO pbmi,
        uint usage,
        out IntPtr ppvBits,
        IntPtr hSection,
        uint offset);

    /// <summary>
    /// GDIオブジェクトをDCに選択
    /// </summary>
    [DllImport(GDI32_DLL, SetLastError = true)]
    internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    /// <summary>
    /// GDIオブジェクト削除
    /// </summary>
    [DllImport(GDI32_DLL, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// デバイスコンテキスト削除
    /// </summary>
    [DllImport(GDI32_DLL, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(IntPtr hdc);

    // ========================================
    // Window Constants
    // ========================================

    /// <summary>
    /// SetWindowPos用の特殊ウィンドウハンドル
    /// </summary>
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);

    /// <summary>
    /// WS_EX_LAYERED に未設定 - CommonStructures.cs ExtendedWindowStyles に存在
    /// </summary>
    internal const uint WS_EX_LAYERED = 0x00080000;

    /// <summary>
    /// WS_EX_NOACTIVATE - ウィンドウがアクティブ化されない（既存にないため追加）
    /// </summary>
    internal const uint WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>
    /// DIB_RGB_COLORS - CreateDIBSection用
    /// </summary>
    internal const uint DIB_RGB_COLORS = 0;

    /// <summary>
    /// BI_RGB - 非圧縮ビットマップ
    /// </summary>
    internal const uint BI_RGB = 0;
}

// ========================================
// Structures
// ========================================

/// <summary>
/// MSG構造体（Window Message）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public POINT pt;
}

/// <summary>
/// BLENDFUNCTION構造体（アルファブレンディング設定）
/// </summary>
/// <remarks>
/// UpdateLayeredWindow で使用
/// AC_SRC_ALPHA=1 かつ AlphaFormat=AC_SRC_ALPHA で per-pixel alpha 有効
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct BLENDFUNCTION
{
    public byte BlendOp;
    public byte BlendFlags;
    public byte SourceConstantAlpha;
    public byte AlphaFormat;

    /// <summary>
    /// 標準のper-pixel alpha blending用の設定を作成
    /// </summary>
    public static BLENDFUNCTION CreateDefault()
    {
        return new BLENDFUNCTION
        {
            BlendOp = 0, // AC_SRC_OVER
            BlendFlags = 0,
            SourceConstantAlpha = 255, // 不透明度100%
            AlphaFormat = 1 // AC_SRC_ALPHA - per-pixel alpha使用
        };
    }
}

/// <summary>
/// BITMAPINFO構造体（ビットマップ情報）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BITMAPINFO
{
    public BITMAPINFOHEADER bmiHeader;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
    public uint[] bmiColors;
}

/// <summary>
/// BITMAPINFOHEADER構造体
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;

    /// <summary>
    /// 32bit ARGB ビットマップ用のヘッダー作成
    /// </summary>
    public static BITMAPINFOHEADER Create32BitARGB(int width, int height)
    {
        return new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // 🔥 [CRITICAL] 負の値でトップダウンDIB（原点が左上）
            biPlanes = 1,
            biBitCount = 32, // 32bit ARGB
            biCompression = LayeredWindowMethods.BI_RGB,
            biSizeImage = 0, // BI_RGBの場合は0でOK
            biXPelsPerMeter = 0,
            biYPelsPerMeter = 0,
            biClrUsed = 0,
            biClrImportant = 0
        };
    }
}

// ========================================
// Enums & Flags
// ========================================

/// <summary>
/// SetLayeredWindowAttributes用のフラグ
/// </summary>
[Flags]
internal enum LayeredWindowAttributes : uint
{
    /// <summary>
    /// crKeyパラメータで指定した色を透明化
    /// </summary>
    LWA_COLORKEY = 0x00000001,

    /// <summary>
    /// bAlphaパラメータで全体の透明度を設定
    /// </summary>
    LWA_ALPHA = 0x00000002
}

/// <summary>
/// UpdateLayeredWindow用のフラグ
/// </summary>
[Flags]
internal enum UpdateLayeredWindowFlags : uint
{
    /// <summary>
    /// per-pixel alphaを使用（BLENDFUNCTIONで設定）
    /// </summary>
    ULW_ALPHA = 0x00000002,

    /// <summary>
    /// カラーキーを使用
    /// </summary>
    ULW_COLORKEY = 0x00000001,

    /// <summary>
    /// 不透明ウィンドウ
    /// </summary>
    ULW_OPAQUE = 0x00000004
}

/// <summary>
/// SetWindowPos用のフラグ
/// </summary>
[Flags]
internal enum SetWindowPosFlags : uint
{
    SWP_NOSIZE = 0x0001,
    SWP_NOMOVE = 0x0002,
    SWP_NOZORDER = 0x0004,
    SWP_NOREDRAW = 0x0008,
    SWP_NOACTIVATE = 0x0010,
    SWP_FRAMECHANGED = 0x0020,
    SWP_SHOWWINDOW = 0x0040,
    SWP_HIDEWINDOW = 0x0080,
    SWP_NOCOPYBITS = 0x0100,
    SWP_NOOWNERZORDER = 0x0200,
    SWP_NOSENDCHANGING = 0x0400,
    SWP_ASYNCWINDOWPOS = 0x4000
}

/// <summary>
/// ShowWindow用のコマンド
/// </summary>
internal enum ShowWindowCommands
{
    SW_HIDE = 0,
    SW_SHOWNORMAL = 1,
    SW_SHOWMINIMIZED = 2,
    SW_SHOWMAXIMIZED = 3,
    SW_MAXIMIZE = 3,
    SW_SHOWNOACTIVATE = 4,
    SW_SHOW = 5,
    SW_MINIMIZE = 6,
    SW_SHOWMINNOACTIVE = 7,
    SW_SHOWNA = 8,
    SW_RESTORE = 9,
    SW_SHOWDEFAULT = 10,
    SW_FORCEMINIMIZE = 11
}
