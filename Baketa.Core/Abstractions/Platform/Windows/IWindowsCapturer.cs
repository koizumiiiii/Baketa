using System;
using System.Drawing;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Platform.Windows;

/// <summary>
/// Windows画面キャプチャインターフェース
/// </summary>
public interface IWindowsCapturer
{
    /// <summary>
    /// 画面全体をキャプチャ
    /// </summary>
    /// <returns>キャプチャした画像</returns>
    Task<IWindowsImage> CaptureScreenAsync();

    /// <summary>
    /// 指定した領域をキャプチャ
    /// </summary>
    /// <param name="region">キャプチャする領域</param>
    /// <returns>キャプチャした画像</returns>
    Task<IWindowsImage> CaptureRegionAsync(Rectangle region);

    /// <summary>
    /// 指定したウィンドウをキャプチャ
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    Task<IWindowsImage> CaptureWindowAsync(IntPtr windowHandle);

    /// <summary>
    /// 🚀 [Issue #193] 指定したウィンドウをGPU上でリサイズしてキャプチャ
    /// GPU→CPU転送量を削減し、パフォーマンスを向上
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <param name="targetWidth">ターゲット幅</param>
    /// <param name="targetHeight">ターゲット高さ</param>
    /// <returns>リサイズされたキャプチャ画像</returns>
    Task<IWindowsImage> CaptureWindowResizedAsync(IntPtr windowHandle, int targetWidth, int targetHeight);

    /// <summary>
    /// 指定したウィンドウのクライアント領域をキャプチャ
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    Task<IWindowsImage> CaptureClientAreaAsync(IntPtr windowHandle);

    /// <summary>
    /// キャプチャオプションを設定
    /// </summary>
    /// <param name="options">キャプチャオプション</param>
    void SetCaptureOptions(WindowsCaptureOptions options);

    /// <summary>
    /// 現在のキャプチャオプションを取得
    /// </summary>
    /// <returns>キャプチャオプション</returns>
    WindowsCaptureOptions GetCaptureOptions();
}

/// <summary>
/// Windowsキャプチャオプション
/// </summary>
public class WindowsCaptureOptions
{
    /// <summary>
    /// キャプチャのクオリティ（1-100）
    /// </summary>
    public int Quality { get; set; } = 100;

    /// <summary>
    /// ウィンドウキャプチャ時に装飾（タイトルバーなど）を含むかどうか
    /// </summary>
    public bool IncludeWindowDecorations { get; set; } = true;

    /// <summary>
    /// カーソルを含むかどうか
    /// </summary>
    public bool IncludeCursor { get; set; }

    /// <summary>
    /// 透過ウィンドウの透過部分を維持するかどうか
    /// </summary>
    public bool PreserveTransparency { get; set; } = true;

    /// <summary>
    /// DWM（Desktop Window Manager）レンダリングを使用するかどうか
    /// </summary>
    public bool UseDwmCapture { get; set; } = true;
}
