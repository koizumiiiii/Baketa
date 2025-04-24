using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform;
using Baketa.Core.Abstractions.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Adapters
{
    /// <summary>
    /// Windows固有の画面キャプチャ実装と抽象化レイヤーの間のアダプターインターフェース
    /// </summary>
    public interface ICaptureAdapter
    {
        /// <summary>
        /// Windows固有のキャプチャサービスを使用して画面全体をキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureScreenAsync();
        
        /// <summary>
        /// Windows固有のキャプチャサービスを使用して指定した領域をキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <param name="region">キャプチャする領域</param>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureRegionAsync(Rectangle region);
        
        /// <summary>
        /// Windows固有のキャプチャサービスを使用して指定したウィンドウをキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureWindowAsync(IntPtr windowHandle);
        
        /// <summary>
        /// Windows固有のキャプチャサービスを使用して指定したウィンドウのクライアント領域をキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        Task<IImage> CaptureClientAreaAsync(IntPtr windowHandle);
        
        /// <summary>
        /// コアのCaptureOptionsをWindows固有のキャプチャオプションに変換して設定します
        /// </summary>
        /// <param name="options">プラットフォーム非依存のキャプチャオプション</param>
        void SetCaptureOptions(CaptureOptions options);
        
        /// <summary>
        /// 現在のWindows固有のキャプチャオプションをコアのCaptureOptionsに変換して返します
        /// </summary>
        /// <returns>プラットフォーム非依存のキャプチャオプション</returns>
        CaptureOptions GetCaptureOptions();
        
        /// <summary>
        /// IWindowsCapturerからIScreenCapturerへの適応を行います
        /// </summary>
        /// <param name="windowsCapturer">Windows固有のキャプチャサービス</param>
        /// <returns>プラットフォーム非依存のキャプチャサービス</returns>
        IScreenCapturer AdaptCapturer(IWindowsCapturer windowsCapturer);
        
        /// <summary>
        /// Windows固有のキャプチャオプションをコアのCaptureOptionsに変換します
        /// </summary>
        /// <param name="windowsOptions">Windows固有のキャプチャオプション</param>
        /// <returns>プラットフォーム非依存のキャプチャオプション</returns>
        CaptureOptions ConvertToCoreOptions(WindowsCaptureOptions windowsOptions);
        
        /// <summary>
        /// コアのCaptureOptionsをWindows固有のキャプチャオプションに変換します
        /// </summary>
        /// <param name="coreOptions">プラットフォーム非依存のキャプチャオプション</param>
        /// <returns>Windows固有のキャプチャオプション</returns>
        WindowsCaptureOptions ConvertToWindowsOptions(CaptureOptions coreOptions);
    }
}