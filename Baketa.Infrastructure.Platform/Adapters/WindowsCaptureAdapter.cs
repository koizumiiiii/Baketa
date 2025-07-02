using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Common;
using Baketa.Infrastructure.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Adapters;

    /// <summary>
    /// Windows固有の画面キャプチャ実装と抽象化レイヤーの間のアダプター
    /// パフォーマンスと差分検出機能を強化した実装
    /// </summary>
    public class WindowsCaptureAdapter : DisposableBase, ICaptureAdapter, IScreenCapturer
    {
        private readonly IWindowsImageAdapter _imageAdapter;
        private readonly IWindowsCapturer _windowsCapturer;
        private CaptureOptions _captureOptions = new();
        
        // 差分検出用の前回キャプチャイメージ
        private IImage? _previousImage;
        private readonly SemaphoreSlim _captureSync = new(1, 1);
        
        // 差分検出の設定
        private bool _differenceDetectionEnabled = true;
        private float _differenceThreshold = 0.05f; // 5%以上の変化で差分ありと判定
        
        // パフォーマンス測定用
        private readonly Queue<long> _captureTimes = new(10);
        private readonly object _captureTimesLock = new();
        
        /// <summary>
        /// WindowsCaptureAdapterのコンストラクタ
        /// </summary>
        /// <param name="imageAdapter">Windows画像アダプター</param>
        /// <param name="windowsCapturer">Windowsキャプチャサービス</param>
        public WindowsCaptureAdapter(IWindowsImageAdapter imageAdapter, IWindowsCapturer windowsCapturer)
        {
            _imageAdapter = imageAdapter ?? throw new ArgumentNullException(nameof(imageAdapter));
            _windowsCapturer = windowsCapturer ?? throw new ArgumentNullException(nameof(windowsCapturer));
        }
        
        /// <summary>
        /// 差分検出の有効/無効を設定します
        /// </summary>
        /// <param name="enabled">有効にする場合はtrue</param>
        public void EnableDifferenceDetection(bool enabled)
        {
            _differenceDetectionEnabled = enabled;
            
            // 無効にする場合は参照を解放
            if (!enabled)
            {
                _previousImage?.Dispose();
                _previousImage = null;
            }
        }
        
        /// <summary>
        /// 差分検出の閾値を設定します（0.0〜1.0）
        /// </summary>
        /// <param name="threshold">閾値（0.0〜1.0）</param>
        /// <exception cref="ArgumentOutOfRangeException">閾値が範囲外の場合</exception>
        public void SetDifferenceThreshold(float threshold)
        {
            if (threshold < 0.0f || threshold > 1.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(threshold), "閾値は0.0〜1.0の範囲で指定してください");
            }
            
            _differenceThreshold = threshold;
        }

        /// <summary>
        /// Windows固有のキャプチャサービスを使用して画面全体をキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <returns>キャプチャした画像</returns>
        public async Task<IImage> CaptureScreenAsync()
        {
            try
            {
                await _captureSync.WaitAsync().ConfigureAwait(false);
                
                var startTime = DateTime.UtcNow.Ticks;
                
                // Windowsのキャプチャ機能を使用
                var windowsImage = await _windowsCapturer.CaptureScreenAsync().ConfigureAwait(false);
                
                // コアのImage型に変換
                IImage currentImage = _imageAdapter.ToImage(windowsImage);
                
                // 差分検出が有効で、前回のキャプチャがある場合は差分を検出
                if (_differenceDetectionEnabled && _previousImage != null)
                {
                    var hasDifference = await HasSignificantDifferenceAsync(currentImage, _previousImage).ConfigureAwait(false);
                    if (!hasDifference)
                    {
                        // 有意な差分がない場合は前回のイメージを使用
                        currentImage.Dispose();
                        return _previousImage.Clone();
                    }
                }
                
                // 差分検出のために現在のイメージを保存
                if (_differenceDetectionEnabled)
                {
                    _previousImage?.Dispose();
                    _previousImage = currentImage.Clone();
                }
                
                // パフォーマンス測定
                RecordCaptureTime(DateTime.UtcNow.Ticks - startTime);
                
                return currentImage;
            }
            finally
            {
                _captureSync.Release();
            }
        }

        /// <summary>
        /// Windows固有のキャプチャサービスを使用して指定した領域をキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <param name="region">キャプチャする領域</param>
        /// <returns>キャプチャした画像</returns>
        public async Task<IImage> CaptureRegionAsync(Rectangle region)
        {
            try
            {
                await _captureSync.WaitAsync().ConfigureAwait(false);
                
                var startTime = DateTime.UtcNow.Ticks;
                
                // 領域の妥当性チェック
                ValidateRegion(region);
                
                // Windowsのキャプチャ機能を使用
                var windowsImage = await _windowsCapturer.CaptureRegionAsync(region).ConfigureAwait(false);
                
                // コアのImage型に変換
                IImage currentImage = _imageAdapter.ToImage(windowsImage);
                
                // 差分検出は領域キャプチャでは行わない（前回と領域が異なる可能性があるため）
                
                // パフォーマンス測定
                RecordCaptureTime(DateTime.UtcNow.Ticks - startTime);
                
                return currentImage;
            }
            finally
            {
                _captureSync.Release();
            }
        }

        /// <summary>
        /// Windows固有のキャプチャサービスを使用して指定したウィンドウをキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        public async Task<IImage> CaptureWindowAsync(IntPtr windowHandle)
        {
            try
            {
                await _captureSync.WaitAsync().ConfigureAwait(false);
                
                // ウィンドウハンドルの妥当性チェック
                ValidateWindowHandle(windowHandle);
                
                // Windowsのキャプチャ機能を使用
                var windowsImage = await _windowsCapturer.CaptureWindowAsync(windowHandle).ConfigureAwait(false);
                
                // コアのImage型に変換
                IImage currentImage = _imageAdapter.ToImage(windowsImage);
                
                // 差分検出は特定のウィンドウキャプチャモードでは行わない（最適化の余地あり）
                
                return currentImage;
            }
            finally
            {
                _captureSync.Release();
            }
        }

        /// <summary>
        /// Windows固有のキャプチャサービスを使用して指定したウィンドウのクライアント領域をキャプチャし、プラットフォーム非依存のIImageを返します
        /// </summary>
        /// <param name="windowHandle">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        public async Task<IImage> CaptureClientAreaAsync(IntPtr windowHandle)
        {
            try
            {
                await _captureSync.WaitAsync().ConfigureAwait(false);
                
                // ウィンドウハンドルの妥当性チェック
                ValidateWindowHandle(windowHandle);
                
                // Windowsのキャプチャ機能を使用
                var windowsImage = await _windowsCapturer.CaptureClientAreaAsync(windowHandle).ConfigureAwait(false);
                
                // コアのImage型に変換
                IImage currentImage = _imageAdapter.ToImage(windowsImage);
                
                // 差分検出はクライアント領域キャプチャモードでは行わない（最適化の余地あり）
                
                return currentImage;
            }
            finally
            {
                _captureSync.Release();
            }
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
            // 現在のWindowsオプションを取得して変換
            var windowsOptions = _windowsCapturer.GetCaptureOptions();
            return ConvertToCoreOptions(windowsOptions);
        }

        /// <summary>
        /// Windows固有のキャプチャサービスからプラットフォーム非依存のキャプチャサービスを作成します
        /// </summary>
        /// <param name="windowsCapturer">Windows固有のキャプチャサービス</param>
        /// <returns>プラットフォーム非依存のキャプチャサービス</returns>
        public IScreenCapturer AdaptCapturer(IWindowsCapturer windowsCapturer)
        {
            ArgumentNullException.ThrowIfNull(windowsCapturer, nameof(windowsCapturer));
            
            // 自分自身がIScreenCapturerを実装しているので、新たなアダプターインスタンスを作成
            return new WindowsCaptureAdapter(_imageAdapter, windowsCapturer);
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
                // Windowsオプションには対応するものがないためデフォルト値または既存の値を設定
                CaptureInterval = _captureOptions.CaptureInterval
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
            
            // 現在のWindowsオプションを取得
            var currentWindowsOptions = _windowsCapturer.GetCaptureOptions();
            
            // 共通項目を更新
            currentWindowsOptions.Quality = coreOptions.Quality;
            currentWindowsOptions.IncludeCursor = coreOptions.IncludeCursor;
            
            // Windows固有の項目はデフォルト値を維持
            
            return currentWindowsOptions;
        }
        
        /// <summary>
        /// 2つの画像間に有意な差分があるかどうかを判定します
        /// </summary>
        /// <param name="current">現在の画像</param>
        /// <param name="previous">前回の画像</param>
        /// <returns>有意な差分がある場合はtrue</returns>
        private async Task<bool> HasSignificantDifferenceAsync(IImage current, IImage previous)
        {
            // サイズが異なる場合は差分ありと判定
            if (current.Width != previous.Width || current.Height != previous.Height)
            {
                return true;
            }
            
            // IAdvancedImageのCalculateSimilarityAsync機能を使用（可能な場合）
            if (current is IAdvancedImage advancedCurrent && previous is IAdvancedImage advancedPrevious)
            {
                var similarity = await advancedCurrent.CalculateSimilarityAsync(advancedPrevious).ConfigureAwait(false);
                return similarity < (1.0f - _differenceThreshold);
            }
            
            // バイト配列の簡易比較（AdvancedImageでない場合のフォールバック）
            var currentBytes = await current.ToByteArrayAsync().ConfigureAwait(false);
            var previousBytes = await previous.ToByteArrayAsync().ConfigureAwait(false);
            
            if (currentBytes.Length != previousBytes.Length)
            {
                return true;
            }
            
            // サンプリングベースの比較（全ピクセルではなく一部をサンプリングして比較）
            int diffCount = 0;
            int sampleCount = 0;
            int samplingStep = Math.Max(1, currentBytes.Length / 10000); // 最大1万ポイントをサンプリング
            
            for (int i = 0; i < currentBytes.Length; i += samplingStep)
            {
                if (currentBytes[i] != previousBytes[i])
                {
                    diffCount++;
                }
                sampleCount++;
            }
            
            float diffRatio = (float)diffCount / sampleCount;
            return diffRatio > _differenceThreshold;
        }
        
        /// <summary>
        /// パフォーマンス測定のためのキャプチャ時間記録
        /// </summary>
        /// <param name="elapsedTicks">経過時間（Ticks）</param>
        private void RecordCaptureTime(long elapsedTicks)
        {
            lock (_captureTimesLock)
            {
                _captureTimes.Enqueue(elapsedTicks);
                while (_captureTimes.Count > 10)
                {
                    _captureTimes.Dequeue();
                }
            }
        }
        
        /// <summary>
        /// キャプチャのパフォーマンス情報を取得します
        /// </summary>
        /// <returns>キャプチャ時間の平均（ミリ秒）</returns>
        public double AverageCaptureTimeMs
        {
            get
            {
                lock (_captureTimesLock)
                {
                    if (_captureTimes.Count == 0)
                    {
                        return 0;
                    }
                    
                    long sum = 0;
                    foreach (var time in _captureTimes)
                    {
                        sum += time;
                    }
                    
                    // Ticks → ミリ秒に変換（10000 Ticks = 1ms）
                    return (double)sum / _captureTimes.Count / 10000;
                }
            }
        }
        
        /// <summary>
        /// 指定された領域が有効かどうかを検証します
        /// </summary>
        /// <param name="region">検証する領域</param>
        /// <exception cref="ArgumentException">領域が無効な場合</exception>
        private static void ValidateRegion(Rectangle region)
        {
            if (region.Width <= 0 || region.Height <= 0)
            {
                throw new ArgumentException("キャプチャ領域の幅と高さは正の値である必要があります", nameof(region));
            }
        }
        
        /// <summary>
        /// 指定されたウィンドウハンドルが有効かどうかを検証します
        /// </summary>
        /// <param name="windowHandle">検証するウィンドウハンドル</param>
        /// <exception cref="ArgumentException">ウィンドウハンドルが無効な場合</exception>
        private static void ValidateWindowHandle(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                throw new ArgumentException("有効なウィンドウハンドルを指定してください", nameof(windowHandle));
            }
        }
        
        /// <summary>
        /// Disposeパターンの実装 - アンマネージリソースの開放
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // CA2213対応：_previousImageの明示的なDispose
                if (_previousImage != null)
                {
                    _previousImage.Dispose();
                    _previousImage = null;
                }
                
                // CA2213対応：_captureSyncの明示的なDispose
                _captureSync?.Dispose();
                
                // CA2213対応：その他のIDisposableフィールドのDispose
                if (_imageAdapter is IDisposable imageAdapterDisposable)
                {
                    imageAdapterDisposable.Dispose();
                }
                
                if (_windowsCapturer is IDisposable windowsCapturerDisposable)
                {
                    windowsCapturerDisposable.Dispose();
                }
            }
            
            base.Dispose(disposing);
        }
    }
