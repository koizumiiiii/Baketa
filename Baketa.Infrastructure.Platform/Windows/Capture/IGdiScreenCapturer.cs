using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Windows.Capture
{
    /// <summary>
    /// GDIベースの画面キャプチャ機能を提供するインターフェース
    /// </summary>
    public interface IGdiScreenCapturer : IDisposable
    {
        /// <summary>
        /// プライマリスクリーン全体をキャプチャします
        /// </summary>
        /// <returns>キャプチャした画像</returns>
        Task<IWindowsImage> CaptureScreenAsync();
        
        /// <summary>
        /// 指定したウィンドウをキャプチャします
        /// </summary>
        /// <param name="hWnd">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        Task<IWindowsImage> CaptureWindowAsync(IntPtr hWnd);
        
        /// <summary>
        /// 指定した領域をキャプチャします
        /// </summary>
        /// <param name="region">キャプチャする領域</param>
        /// <returns>キャプチャした画像</returns>
        Task<IWindowsImage> CaptureRegionAsync(Rectangle region);
    }
}
