using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Interfaces.Image;

namespace Baketa.Core.Interfaces.Platform
{
    /// <summary>
    /// 画面キャプチャインターフェース
    /// </summary>
    public interface IScreenCapturer
    {
        /// <summary>
        /// 画面全体をキャプチャ
        /// </summary>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureScreenAsync();
        
        /// <summary>
        /// 指定した領域をキャプチャ
        /// </summary>
        /// <param name="region">キャプチャする領域</param>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureRegionAsync(Rectangle region);
        
        /// <summary>
        /// 指定したウィンドウをキャプチャ
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureWindowAsync(IntPtr windowHandle);
        
        /// <summary>
        /// 指定したウィンドウのクライアント領域をキャプチャ
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureClientAreaAsync(IntPtr windowHandle);
    }
}