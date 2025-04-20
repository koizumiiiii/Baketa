using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.Services
{
    /// <summary>
    /// キャプチャサービスインターフェース
    /// </summary>
    public interface ICaptureService
    {
        /// <summary>
        /// 画面全体をキャプチャします
        /// </summary>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureScreenAsync();
        
        /// <summary>
        /// 指定した領域をキャプチャします
        /// </summary>
        /// <param name="region">キャプチャする領域</param>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureRegionAsync(Rectangle region);
        
        /// <summary>
        /// 指定したウィンドウをキャプチャします
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureWindowAsync(IntPtr windowHandle);
        
        /// <summary>
        /// 指定したウィンドウのクライアント領域をキャプチャします
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureClientAreaAsync(IntPtr windowHandle);
        
        /// <summary>
        /// キャプチャした画像の差分を検出します
        /// </summary>
        /// <param name="previousImage">前回のキャプチャ画像</param>
        /// <param name="currentImage">現在のキャプチャ画像</param>
        /// <param name="threshold">差分判定の閾値 (0.0-1.0)</param>
        /// <returns>差分が検出された場合はtrue</returns>
        Task<bool> DetectChangesAsync(IImage previousImage, IImage currentImage, float threshold = 0.05f);
        
        /// <summary>
        /// キャプチャオプションを設定します
        /// </summary>
        /// <param name="options">キャプチャオプション</param>
        void SetCaptureOptions(CaptureOptions options);
        
        /// <summary>
        /// 現在のキャプチャオプションを取得します
        /// </summary>
        /// <returns>キャプチャオプション</returns>
        CaptureOptions GetCaptureOptions();
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
        public bool IncludeCursor { get; set; } = false;
        
        /// <summary>
        /// キャプチャの間隔（ミリ秒）
        /// </summary>
        public int CaptureInterval { get; set; } = 100;
        
        /// <summary>
        /// キャプチャのフレームレート（秒間フレーム数）
        /// </summary>
        public int FrameRate => 1000 / Math.Max(1, CaptureInterval);
        
        /// <summary>
        /// 最適化レベル（0: なし、1: 低、2: 中、3: 高）
        /// </summary>
        public int OptimizationLevel { get; set; } = 1;
    }
}