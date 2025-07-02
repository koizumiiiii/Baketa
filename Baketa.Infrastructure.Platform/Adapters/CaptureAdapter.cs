using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Common;

namespace Baketa.Infrastructure.Platform.Adapters;

    /// <summary>
    /// Windows固有のキャプチャサービスと抽象化レイヤーの間のアダプターインターフェース実装
    /// 差分検出およびキャプチャ最適化機能を提供します
    /// </summary>
    public class CaptureAdapter : DisposableBase, ICaptureAdapter, IScreenCapturer
    {
        private readonly IWindowsImageAdapter _imageAdapter;
        private readonly IWindowsCapturer _windowsCapturer;
        private readonly IDifferenceDetector? _differenceDetector;
        private CaptureOptions _captureOptions = new();
        private IImage? _lastCapturedImage;

        /// <summary>
        /// CaptureAdapterのコンストラクタ
        /// </summary>
        /// <param name="imageAdapter">WindowsImageAdapter</param>
        /// <param name="windowsCapturer">WindowsCapturer</param>
        public CaptureAdapter(
            IWindowsImageAdapter imageAdapter, 
            IWindowsCapturer windowsCapturer)
        {
            _imageAdapter = imageAdapter ?? throw new ArgumentNullException(nameof(imageAdapter));
            _windowsCapturer = windowsCapturer ?? throw new ArgumentNullException(nameof(windowsCapturer));
            
            // デフォルトのキャプチャオプションをWindowsキャプチャーに設定
            var windowsOptions = ConvertToWindowsOptions(_captureOptions);
            _windowsCapturer.SetCaptureOptions(windowsOptions);
        }

        /// <summary>
        /// 差分検出機能付きのコンストラクタ
        /// </summary>
        /// <param name="imageAdapter">WindowsImageAdapter</param>
        /// <param name="windowsCapturer">WindowsCapturer</param>
        /// <param name="differenceDetector">DifferenceDetector</param>
        public CaptureAdapter(
            IWindowsImageAdapter imageAdapter, 
            IWindowsCapturer windowsCapturer,
            IDifferenceDetector differenceDetector)
            : this(imageAdapter, windowsCapturer)
        {
            _differenceDetector = differenceDetector ?? throw new ArgumentNullException(nameof(differenceDetector));
        }

        /// <summary>
        /// Windows固有のキャプチャサービスを使用して画面全体をキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <returns>キャプチャした画像</returns>
        public async Task<IImage> CaptureScreenAsync()
        {
            var windowsImage = await _windowsCapturer.CaptureScreenAsync().ConfigureAwait(false);
            var currentImage = _imageAdapter.ToImage(windowsImage);
            
            // 差分検出による最適化（前回のキャプチャと比較）
            if (_lastCapturedImage != null && _differenceDetector != null && HasDifferenceDetection())
            {
                try
                {
                    var hasDifference = await CheckForDifference(_lastCapturedImage, currentImage).ConfigureAwait(false);
                    
                    if (!hasDifference)
                    {
                        // 有意な差がない場合は前回のイメージを再利用（メモリ最適化）
                        currentImage.Dispose(); // 新しいキャプチャは破棄
                        return _lastCapturedImage;
                    }
                }
                catch (InvalidOperationException)
                {
                    // 差分検出に失敗した場合は続行
                }
                catch (ArgumentException)
                {
                    // 引数エラーの場合も続行
                }
            }
            
            // 新しいキャプチャを保存して返す
            DisposePreviousImage();
            _lastCapturedImage = currentImage;
            return currentImage;
        }

        /// <summary>
        /// Windows固有のキャプチャサービスを使用して指定した領域をキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <param name="region">キャプチャする領域</param>
        /// <returns>キャプチャした画像</returns>
        public async Task<IImage> CaptureRegionAsync(Rectangle region)
        {
            // 領域が有効かどうかをチェック
            if (region.Width <= 0 || region.Height <= 0)
            {
                throw new ArgumentException("キャプチャ領域のサイズが無効です", nameof(region));
            }
            
            var windowsImage = await _windowsCapturer.CaptureRegionAsync(region).ConfigureAwait(false);
            var currentImage = _imageAdapter.ToImage(windowsImage);
            
            // 差分検出（前回と同じ領域をキャプチャした場合のみ）
            if (_lastCapturedImage != null && _differenceDetector != null && 
                HasDifferenceDetection() && 
                _lastCapturedImage.Width == region.Width && 
                _lastCapturedImage.Height == region.Height)
            {
                try
                {
                    var hasDifference = await CheckForDifference(_lastCapturedImage, currentImage).ConfigureAwait(false);
                    
                    if (!hasDifference)
                    {
                        // 有意な差がない場合は前回のイメージを再利用
                        currentImage.Dispose(); // 新しいキャプチャは破棄
                        return _lastCapturedImage;
                    }
                }
                catch (InvalidOperationException)
                {
                    // 差分検出に失敗した場合は続行
                }
                catch (ArgumentException)
                {
                    // 引数エラーの場合も続行
                }
            }
            
            // 新しいキャプチャを保存して返す
            DisposePreviousImage();
            _lastCapturedImage = currentImage;
            return currentImage;
        }

        /// <summary>
        /// Windows固有のキャプチャサービスを使用して指定したウィンドウをキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        public async Task<IImage> CaptureWindowAsync(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                throw new ArgumentException("無効なウィンドウハンドルです", nameof(windowHandle));
            }
            
            var windowsImage = await _windowsCapturer.CaptureWindowAsync(windowHandle).ConfigureAwait(false);
            var currentImage = _imageAdapter.ToImage(windowsImage);
            
            // 差分検出（サイズが変わる可能性があるため、同一サイズの場合のみ）
            if (_lastCapturedImage != null && _differenceDetector != null && 
                HasDifferenceDetection() &&
                _lastCapturedImage.Width == currentImage.Width && 
                _lastCapturedImage.Height == currentImage.Height)
            {
                try
                {
                    var hasDifference = await CheckForDifference(_lastCapturedImage, currentImage).ConfigureAwait(false);
                    
                    if (!hasDifference)
                    {
                        // 有意な差がない場合は前回のイメージを再利用
                        currentImage.Dispose(); // 新しいキャプチャは破棄
                        return _lastCapturedImage;
                    }
                }
                catch (InvalidOperationException)
                {
                    // 差分検出に失敗した場合は続行
                }
                catch (ArgumentException)
                {
                    // 引数エラーの場合も続行
                }
            }
            
            // 新しいキャプチャを保存して返す
            DisposePreviousImage();
            _lastCapturedImage = currentImage;
            return currentImage;
        }

        /// <summary>
        /// Windows固有のキャプチャサービスを使用して指定したウィンドウのクライアント領域をキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        public async Task<IImage> CaptureClientAreaAsync(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                throw new ArgumentException("無効なウィンドウハンドルです", nameof(windowHandle));
            }
            
            var windowsImage = await _windowsCapturer.CaptureClientAreaAsync(windowHandle).ConfigureAwait(false);
            var currentImage = _imageAdapter.ToImage(windowsImage);
            
            // 差分検出（サイズが変わる可能性があるため、同一サイズの場合のみ）
            if (_lastCapturedImage != null && _differenceDetector != null && 
                HasDifferenceDetection() &&
                _lastCapturedImage.Width == currentImage.Width && 
                _lastCapturedImage.Height == currentImage.Height)
            {
                try
                {
                    var hasDifference = await CheckForDifference(_lastCapturedImage, currentImage).ConfigureAwait(false);
                    
                    if (!hasDifference)
                    {
                        // 有意な差がない場合は前回のイメージを再利用
                        currentImage.Dispose(); // 新しいキャプチャは破棄
                        return _lastCapturedImage;
                    }
                }
                catch (InvalidOperationException)
                {
                    // 差分検出に失敗した場合は続行
                }
                catch (ArgumentException)
                {
                    // 引数エラーの場合も続行
                }
            }
            
            // 新しいキャプチャを保存して返す
            DisposePreviousImage();
            _lastCapturedImage = currentImage;
            return currentImage;
        }

        /// <summary>
        /// コアのCaptureOptionsをWindows固有のキャプチャオプションに変換して設定します
        /// </summary>
        /// <param name="options">プラットフォーム非依存のキャプチャオプション</param>
        public void SetCaptureOptions(CaptureOptions options)
        {
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            
            _captureOptions = options;
            
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
            return _captureOptions;
        }

        /// <summary>
        /// IWindowsCapturerからIScreenCapturerへの適応を行います
        /// </summary>
        /// <param name="windowsCapturer">Windows固有のキャプチャサービス</param>
        /// <returns>プラットフォーム非依存のキャプチャサービス</returns>
        public IScreenCapturer AdaptCapturer(IWindowsCapturer windowsCapturer)
        {
            ArgumentNullException.ThrowIfNull(windowsCapturer, nameof(windowsCapturer));
            return new CaptureAdapter(_imageAdapter, windowsCapturer);
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
                CaptureInterval = _captureOptions.CaptureInterval // 維持
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
            
            // _windowsCapturerがnullでないことを確認
            if (_windowsCapturer == null)
            {
                // Windowsキャプチャーが設定されていない場合はデフォルト値を返す
                return new WindowsCaptureOptions
                {
                    Quality = coreOptions.Quality,
                    IncludeCursor = coreOptions.IncludeCursor
                };
            }
            
            // 現在のWindowsオプションを取得（既存の設定を維持するため）
            var currentWindowsOptions = _windowsCapturer.GetCaptureOptions();
            
            // コアのオプションで上書き
            if (currentWindowsOptions != null)
            {
                currentWindowsOptions.Quality = coreOptions.Quality;
                currentWindowsOptions.IncludeCursor = coreOptions.IncludeCursor;
                return currentWindowsOptions;
            }
            else
            {
                // オプションがnullの場合は新規作成
                return new WindowsCaptureOptions
                {
                    Quality = coreOptions.Quality,
                    IncludeCursor = coreOptions.IncludeCursor
                };
            }
        }
        
        /// <summary>
        /// 前回のキャプチャイメージを破棄します
        /// </summary>
        private void DisposePreviousImage()
        {
            if (_lastCapturedImage != null)
            {
                _lastCapturedImage.Dispose();
                _lastCapturedImage = null;
            }
        }
        
        /// <summary>
        /// 差分検出が有効かどうかを確認します
        /// </summary>
        private static bool HasDifferenceDetection()
        {
            // 以下の条件で差分検出を有効にする
            // 1. 差分検出機能が利用可能であること
            // 2. キャプチャオプションで差分検出が有効になっていること（実際のオプション名に合わせて修正）
            return true; // 実際の実装では条件をチェック
        }
        
        /// <summary>
        /// 2つの画像間の差分を検出します
        /// </summary>
        private async Task<bool> CheckForDifference(IImage image1, IImage image2)
        {
            try
            {
                // 差分検出が利用可能な場合のみ実行
                if (_differenceDetector != null)
                {
                    var hasDifference = await _differenceDetector.HasSignificantDifferenceAsync(
                        image1, image2, 0.05f).ConfigureAwait(false);
                    return hasDifference;
                }
                
                // 差分検出が利用できない場合は差分ありとして扱う
                return true;
            }
            catch (InvalidOperationException)
            {
                // 差分検出処理中に例外が発生した場合は差分ありとみなす
                return true;
            }
            catch (ArgumentException)
            {
                // 引数エラーの場合も差分ありとみなす
                return true;
            }
        }

        /// <summary>
        /// マネージドリソースの破棄
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // CA2213対応：_lastCapturedImage を明示的にDisposeする
                if (_lastCapturedImage != null)
                {
                    _lastCapturedImage.Dispose();
                    _lastCapturedImage = null;
                }
                
                // CA2213対応：その他のIDisposableフィールドの処理
                if (_imageAdapter is IDisposable imageAdapterDisposable)
                {
                    imageAdapterDisposable.Dispose();
                }
                
                if (_windowsCapturer is IDisposable windowsCapturerDisposable)
                {
                    windowsCapturerDisposable.Dispose();
                }
                
                if (_differenceDetector is IDisposable differenceDetectorDisposable)
                {
                    differenceDetectorDisposable.Dispose();
                }
            }
            
            base.Dispose(disposing);
        }
    }
    
    /// <summary>
    /// 差分検出インターフェース（プロジェクトの実装に合わせて調整）
    /// </summary>
    public interface IDifferenceDetector
    {
        /// <summary>
        /// 2つの画像に有意な差分があるかどうかを判定します
        /// </summary>
        Task<bool> HasSignificantDifferenceAsync(IImage image1, IImage image2, float threshold = 0.05f);
    }
