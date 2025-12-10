using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Baketa.Infrastructure.Platform.Windows.Capture;

/// <summary>
/// BaketaCaptureNative.dll の P/Invoke インターフェース
/// </summary>
public static partial class NativeWindowsCapture
{
    private const string DllName = "BaketaCaptureNative.dll";

    /// <summary>
    /// エラーコード定義
    /// </summary>
    public static class ErrorCodes
    {
        public const int Success = 0;
        public const int InvalidWindow = -1;
        public const int Unsupported = -2;
        public const int AlreadyExists = -3;
        public const int NotFound = -4;
        public const int Memory = -5;
        public const int Device = -6;
    }

    /// <summary>
    /// フレームデータ構造体
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct BaketaCaptureFrame
    {
        public IntPtr bgraData;         // BGRA ピクセルデータ
        public int width;               // 幅
        public int height;              // 高さ
        public int stride;              // 行バイト数
        [MarshalAs(UnmanagedType.I8)]
        public long timestamp;          // キャプチャ時刻 (100ns 単位)
    }

    /// <summary>
    /// ライブラリの初期化
    /// </summary>
    /// <returns>成功時は ErrorCodes.Success</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1707:Identifiers should not contain underscores", Justification = "Native API naming convention")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Public API for platform integration")]
    public static extern int BaketaCapture_Initialize();

    /// <summary>
    /// ライブラリの終了処理
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1707:Identifiers should not contain underscores", Justification = "Native API naming convention")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Public API for platform integration")]
    public static extern void BaketaCapture_Shutdown();

    /// <summary>
    /// ウィンドウキャプチャセッションを作成
    /// </summary>
    /// <param name="hwnd">対象ウィンドウハンドル</param>
    /// <param name="sessionId">作成されたセッションID（出力）</param>
    /// <returns>成功時は ErrorCodes.Success</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1707:Identifiers should not contain underscores", Justification = "Native API naming convention")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Public API for platform integration")]
    public static extern int BaketaCapture_CreateSession([In] IntPtr hwnd, [Out] out int sessionId);

    /// <summary>
    /// フレームをキャプチャ
    /// </summary>
    /// <param name="sessionId">セッションID</param>
    /// <param name="frame">キャプチャフレーム（出力）</param>
    /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
    /// <returns>成功時は ErrorCodes.Success</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1707:Identifiers should not contain underscores", Justification = "Native API naming convention")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Public API for platform integration")]
    public static extern int BaketaCapture_CaptureFrame(int sessionId, [Out] out BaketaCaptureFrame frame, int timeoutMs);

    /// <summary>
    /// 🚀 [Issue #193] フレームをキャプチャしてGPU側でリサイズ
    /// </summary>
    /// <param name="sessionId">セッションID</param>
    /// <param name="frame">キャプチャフレーム（出力）</param>
    /// <param name="targetWidth">ターゲット幅（0の場合はリサイズなし）</param>
    /// <param name="targetHeight">ターゲット高さ（0の場合はリサイズなし）</param>
    /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
    /// <returns>成功時は ErrorCodes.Success</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1707:Identifiers should not contain underscores", Justification = "Native API naming convention")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Public API for platform integration")]
    public static extern int BaketaCapture_CaptureFrameResized(int sessionId, [Out] out BaketaCaptureFrame frame, int targetWidth, int targetHeight, int timeoutMs);

    /// <summary>
    /// フレームデータを解放
    /// </summary>
    /// <param name="frame">解放するフレーム</param>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1707:Identifiers should not contain underscores", Justification = "Native API naming convention")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Public API for platform integration")]
    public static extern void BaketaCapture_ReleaseFrame([In, Out] ref BaketaCaptureFrame frame);

    /// <summary>
    /// キャプチャセッションを削除
    /// </summary>
    /// <param name="sessionId">セッションID</param>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1707:Identifiers should not contain underscores", Justification = "Native API naming convention")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Public API for platform integration")]
    public static extern void BaketaCapture_ReleaseSession(int sessionId);

    /// <summary>
    /// Windows Graphics Capture API がサポートされているかチェック
    /// </summary>
    /// <returns>サポートされている場合は 1、それ以外は 0</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1707:Identifiers should not contain underscores", Justification = "Native API naming convention")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Public API for platform integration")]
    public static extern int BaketaCapture_IsSupported();

    /// <summary>
    /// 最後のエラーメッセージを取得
    /// </summary>
    /// <param name="buffer">メッセージバッファ</param>
    /// <param name="bufferSize">バッファサイズ</param>
    /// <returns>実際のメッセージ長</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl,
              CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1707:Identifiers should not contain underscores", Justification = "Native API naming convention")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Public API for platform integration")]
    public static extern int BaketaCapture_GetLastError([Out] IntPtr buffer, int bufferSize);

    /// <summary>
    /// セッションのウィンドウデバッグ情報を取得
    /// </summary>
    /// <param name="sessionId">セッションID</param>
    /// <param name="windowInfoBuffer">ウィンドウ情報バッファ</param>
    /// <param name="windowInfoSize">ウィンドウ情報バッファサイズ</param>
    /// <param name="screenRectBuffer">スクリーン座標バッファ</param>
    /// <param name="screenRectSize">スクリーン座標バッファサイズ</param>
    /// <returns>成功時は 1、失敗時は 0</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl,
              CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1707:Identifiers should not contain underscores", Justification = "Native API naming convention")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Public API for platform integration")]
    public static extern int BaketaCapture_GetWindowDebugInfo(int sessionId, [Out] IntPtr windowInfoBuffer, int windowInfoSize, [Out] IntPtr screenRectBuffer, int screenRectSize);

    /// <summary>
    /// 最後のエラーメッセージを取得（文字列版）
    /// </summary>
    /// <returns>エラーメッセージ</returns>
    public static string GetLastErrorMessage()
    {
        const int bufferSize = 1024;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            int length = BaketaCapture_GetLastError(buffer, bufferSize);

            if (length <= 0)
                return string.Empty;

            return Marshal.PtrToStringAnsi(buffer, Math.Min(length, bufferSize - 1)) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// セッションのデバッグ情報を取得（文字列版）
    /// </summary>
    /// <param name="sessionId">セッションID</param>
    /// <returns>デバッグ情報（ウィンドウ情報, スクリーン座標）</returns>
    public static (string windowInfo, string screenRect) GetSessionDebugInfo(int sessionId)
    {
        const int bufferSize = 1024;
        IntPtr windowBuffer = Marshal.AllocHGlobal(bufferSize);
        IntPtr rectBuffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            int result = BaketaCapture_GetWindowDebugInfo(sessionId, windowBuffer, bufferSize, rectBuffer, bufferSize);

            if (result == 0)
                return ("Debug info unavailable", "Debug info unavailable");

            string windowInfo = Marshal.PtrToStringAnsi(windowBuffer) ?? "N/A";
            string screenRect = Marshal.PtrToStringAnsi(rectBuffer) ?? "N/A";

            return (windowInfo, screenRect);
        }
        finally
        {
            Marshal.FreeHGlobal(windowBuffer);
            Marshal.FreeHGlobal(rectBuffer);
        }
    }
}
