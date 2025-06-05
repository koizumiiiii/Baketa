using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.Platform;

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
        
        /// <summary>
        /// キャプチャのオプションを設定
        /// </summary>
        /// <param name="options">キャプチャオプション</param>
        void SetCaptureOptions(CaptureOptions options);
    }
    
    /// <summary>
    /// キャプチャオプション
    /// </summary>
    public class CaptureOptions
    {
        /// <summary>
        /// キャプチャの品質（1-100）
        /// </summary>
        public int Quality { get; set; } = 100;
        
        /// <summary>
        /// カーソルを含むかどうか
        /// </summary>
        public bool IncludeCursor { get; set; }
        
        /// <summary>
        /// キャプチャの間隔（ミリ秒）
        /// </summary>
        public int CaptureInterval { get; set; }
    }
