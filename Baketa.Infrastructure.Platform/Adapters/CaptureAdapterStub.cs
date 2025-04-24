using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform;
using Baketa.Core.Abstractions.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Adapters
{
    /// <summary>
    /// ICaptureAdapterインターフェースの基本スタブ実装
    /// 注：実際の機能実装は後の段階で行います
    /// </summary>
    public class CaptureAdapterStub : ICaptureAdapter
    {
        private readonly IWindowsImageAdapter _imageAdapter;
        private readonly IWindowsCapturer _windowsCapturer;
        private CaptureOptions _captureOptions = new();

        /// <summary>
        /// CaptureAdapterのコンストラクタ
        /// </summary>
        /// <param name="imageAdapter">画像アダプター</param>
        /// <param name="windowsCapturer">Windows用キャプチャーサービス</param>
        public CaptureAdapterStub(IWindowsImageAdapter imageAdapter, IWindowsCapturer windowsCapturer)
        {
            _imageAdapter = imageAdapter ?? throw new ArgumentNullException(nameof(imageAdapter));
            _windowsCapturer = windowsCapturer ?? throw new ArgumentNullException(nameof(windowsCapturer));
        }

        /// <summary>
        /// Windows固有のキャプチャサービスを使用して画面全体をキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <returns>キャプチャした画像</returns>
        public async Task<IImage> CaptureScreenAsync()
        {
            // Windowsのキャプチャ機能を使用
            var windowsImage = await _windowsCapturer.CaptureScreenAsync().ConfigureAwait(false);
            
            // コアのImage型に変換
            return _imageAdapter.ToImage(windowsImage);
        }

        /// <summary>
        /// Windows固有のキャプチャサービスを使用して指定した領域をキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <param name="region">キャプチャする領域</param>
        /// <returns>キャプチャした画像</returns>
        public async Task<IImage> CaptureRegionAsync(Rectangle region)
        {
            var windowsImage = await _windowsCapturer.CaptureRegionAsync(region).ConfigureAwait(false);
            return _imageAdapter.ToImage(windowsImage);
        }

        /// <summary>
        /// Windows固有のキャプチャサービスを使用して指定したウィンドウをキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        public async Task<IImage> CaptureWindowAsync(IntPtr windowHandle)
        {
            var windowsImage = await _windowsCapturer.CaptureWindowAsync(windowHandle).ConfigureAwait(false);
            return _imageAdapter.ToImage(windowsImage);
        }

        /// <summary>
        /// Windows固有のキャプチャサービスを使用して指定したウィンドウのクライアント領域をキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        public async Task<IImage> CaptureClientAreaAsync(IntPtr windowHandle)
        {
            var windowsImage = await _windowsCapturer.CaptureClientAreaAsync(windowHandle).ConfigureAwait(false);
            return _imageAdapter.ToImage(windowsImage);
        }

        /// <summary>
        /// コアのCaptureOptionsをWindows固有のキャプチャオプションに変換して設定します
        /// </summary>
        /// <param name="options">プラットフォーム非依存のキャプチャオプション</param>
        public void SetCaptureOptions(CaptureOptions options)
        {
            _captureOptions = options ?? throw new ArgumentNullException(nameof(options));
            
            // Windows固有オプションに変換して設定
            var windowsOptions = ConvertToWindowsOptions(_captureOptions);
            _windowsCapturer.SetCaptureOptions(windowsOptions);
        }

        /// <summary>
        /// 現在のWindows固有のキャプチャオプションをコアのCaptureOptionsに変換して返します
        /// </summary>
        /// <returns>プラットフォーム非依存のキャプチャオプション</returns>
        public CaptureOptions GetCaptureOptions()
        {
            // 現在のWindowsオプションを取得して変換
            var windowsOptions = _windowsCapturer.GetCaptureOptions();
            return ConvertToCoreOptions(windowsOptions);
        }

        /// <summary>
        /// IWindowsCapturerからIScreenCapturerへの適応を行います
        /// </summary>
        /// <param name="windowsCapturer">Windows固有のキャプチャサービス</param>
        /// <returns>プラットフォーム非依存のキャプチャサービス</returns>
        public IScreenCapturer AdaptCapturer(IWindowsCapturer windowsCapturer)
        {
            // スタブ実装では自身を返す（実際の実装では専用のアダプターを返す）
            // ここでは必要なインターフェースが揃っていないためコンパイルエラーを避けるためのスタブ
            throw new NotImplementedException("実際の実装ではない場所で呼び出されました");
        }

        /// <summary>
        /// Windows固有のキャプチャオプションをコアのCaptureOptionsに変換します
        /// </summary>
        /// <param name="windowsOptions">Windows固有のキャプチャオプション</param>
        /// <returns>プラットフォーム非依存のキャプチャオプション</returns>
        public CaptureOptions ConvertToCoreOptions(WindowsCaptureOptions windowsOptions)
        {
            ArgumentNullException.ThrowIfNull(windowsOptions, nameof(windowsOptions));
            
            return new CaptureOptions
            {
                Quality = windowsOptions.Quality,
                IncludeCursor = windowsOptions.IncludeCursor,
                // Windowsオプションには対応するものがないためデフォルト値を設定
                CaptureInterval = 100
            };
        }

        /// <summary>
        /// コアのCaptureOptionsをWindows固有のキャプチャオプションに変換します
        /// </summary>
        /// <param name="coreOptions">プラットフォーム非依存のキャプチャオプション</param>
        /// <returns>Windows固有のキャプチャオプション</returns>
        public WindowsCaptureOptions ConvertToWindowsOptions(CaptureOptions coreOptions)
        {
            ArgumentNullException.ThrowIfNull(coreOptions, nameof(coreOptions));
            
            return new WindowsCaptureOptions
            {
                Quality = coreOptions.Quality,
                IncludeCursor = coreOptions.IncludeCursor,
                // コアオプションでは対応するものがないためデフォルト値を設定
                IncludeWindowDecorations = true,
                PreserveTransparency = true,
                UseDwmCapture = true
            };
        }
    }
}