using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.Platform.Windows.Adapters;

/// <summary>
/// WindowsCapturerをコアキャプチャサービスに変換するアダプターインターフェース
/// </summary>
public interface IWindowsCapturerAdapter : IWindowsAdapter
{
    /// <summary>
    /// 画面全体をキャプチャしてコア画像として返す
    /// </summary>
    /// <returns>キャプチャした画像</returns>
    Task<IImage> CaptureScreenAsync();

    /// <summary>
    /// 指定した領域をキャプチャしてコア画像として返す
    /// </summary>
    /// <param name="region">キャプチャする領域</param>
    /// <returns>キャプチャした画像</returns>
    Task<IImage> CaptureRegionAsync(Rectangle region);

    /// <summary>
    /// 指定したウィンドウをキャプチャしてコア画像として返す
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    Task<IImage> CaptureWindowAsync(IntPtr windowHandle);

    /// <summary>
    /// 指定したウィンドウのクライアント領域をキャプチャしてコア画像として返す
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    Task<IImage> CaptureClientAreaAsync(IntPtr windowHandle);

    /// <summary>
    /// キャプチャオプションを設定（内部でWindowsCaptureOptionsに変換）
    /// </summary>
    /// <param name="quality">キャプチャ品質 (1-100)</param>
    /// <param name="includeCursor">カーソルを含むかどうか</param>
    void SetCaptureOptions(int quality, bool includeCursor);
}
