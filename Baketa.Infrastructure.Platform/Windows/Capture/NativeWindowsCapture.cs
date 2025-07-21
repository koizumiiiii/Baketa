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
    public static extern int BaketaCapture_Initialize();

    /// <summary>
    /// ライブラリの終了処理
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void BaketaCapture_Shutdown();

    /// <summary>
    /// ウィンドウキャプチャセッションを作成
    /// </summary>
    /// <param name="hwnd">対象ウィンドウハンドル</param>
    /// <param name="sessionId">作成されたセッションID（出力）</param>
    /// <returns>成功時は ErrorCodes.Success</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int BaketaCapture_CreateSession([In] IntPtr hwnd, [Out] out int sessionId);

    /// <summary>
    /// フレームをキャプチャ
    /// </summary>
    /// <param name="sessionId">セッションID</param>
    /// <param name="frame">キャプチャフレーム（出力）</param>
    /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
    /// <returns>成功時は ErrorCodes.Success</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int BaketaCapture_CaptureFrame(int sessionId, [Out] out BaketaCaptureFrame frame, int timeoutMs);

    /// <summary>
    /// フレームデータを解放
    /// </summary>
    /// <param name="frame">解放するフレーム</param>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void BaketaCapture_ReleaseFrame([In, Out] ref BaketaCaptureFrame frame);

    /// <summary>
    /// キャプチャセッションを削除
    /// </summary>
    /// <param name="sessionId">セッションID</param>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void BaketaCapture_ReleaseSession(int sessionId);

    /// <summary>
    /// Windows Graphics Capture API がサポートされているかチェック
    /// </summary>
    /// <returns>サポートされている場合は 1、それ以外は 0</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int BaketaCapture_IsSupported();

    /// <summary>
    /// 最後のエラーメッセージを取得
    /// </summary>
    /// <param name="buffer">メッセージバッファ</param>
    /// <param name="bufferSize">バッファサイズ</param>
    /// <returns>実際のメッセージ長</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, 
              CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern int BaketaCapture_GetLastError([Out] IntPtr buffer, int bufferSize);

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
}