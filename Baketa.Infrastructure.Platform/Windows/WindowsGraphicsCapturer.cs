using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Platform.Windows.Capture;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows;

/// <summary>
/// Windows Graphics Capture APIを使用した高性能ウィンドウキャプチャ実装
/// DirectFullScreenCaptureStrategy用に最適化されたキャプチャー
/// </summary>
public class WindowsGraphicsCapturer : IWindowsCapturer, IDisposable
{
    private readonly NativeWindowsCaptureWrapper _nativeCapture;
    private readonly ILogger<WindowsGraphicsCapturer>? _logger;
    private readonly LoggingSettings _loggingSettings;
    private WindowsCaptureOptions _options = new();
    private bool _disposed;
    private bool _isInitialized;

    /// <summary>
    /// Windows Graphics Capture APIがサポートされているかどうか
    /// </summary>
    public bool IsSupported => _nativeCapture.IsSupported();

    /// <summary>
    /// 現在初期化されているかどうか  
    /// </summary>
    public bool IsInitialized => _isInitialized && _nativeCapture.IsInitialized;

    /// <summary>
    /// WindowsGraphicsCapturerのコンストラクタ
    /// </summary>
    /// <param name="nativeCapture">ネイティブキャプチャラッパー</param>
    /// <param name="logger">ロガー</param>
    /// <param name="loggingSettings">ログ設定</param>
    public WindowsGraphicsCapturer(
        NativeWindowsCaptureWrapper nativeCapture,
        ILogger<WindowsGraphicsCapturer>? logger = null,
        LoggingSettings? loggingSettings = null)
    {
        _nativeCapture = nativeCapture ?? throw new ArgumentNullException(nameof(nativeCapture));
        _logger = logger;
        _loggingSettings = loggingSettings ?? new LoggingSettings();
    }

    /// <summary>
    /// キャプチャラーを初期化
    /// </summary>
    /// <returns>初期化成功時はtrue</returns>
    public async Task<bool> InitializeAsync()
    {
        if (_isInitialized)
            return true;

        return await Task.Run(() =>
        {
            try
            {
                _logger?.LogDebug("Windows Graphics Captureの初期化開始");

                // 🔍🔍🔍 デバッグ: サポート状況チェック
                try
                {
                    var debugPath = _loggingSettings.GetFullDebugLogPath();
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 WindowsGraphicsCapturer: サポート状況チェック開始{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }

                if (!_nativeCapture.IsSupported())
                {
                    _logger?.LogWarning("Windows Graphics Capture APIがサポートされていません");

                    try
                    {
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ WindowsGraphicsCapturer: APIサポートされていません{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }
                    return false;
                }

                try
                {
                    var debugPath = _loggingSettings.GetFullDebugLogPath();
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ WindowsGraphicsCapturer: APIサポート確認完了{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }

                // 🔍🔍🔍 デバッグ: ネイティブキャプチャ初期化
                try
                {
                    var debugPath = _loggingSettings.GetFullDebugLogPath();
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 WindowsGraphicsCapturer: ネイティブキャプチャ初期化開始{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }

                if (!_nativeCapture.Initialize())
                {
                    _logger?.LogError("Windows Graphics Captureの初期化に失敗");

                    try
                    {
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ WindowsGraphicsCapturer: ネイティブキャプチャ初期化失敗{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }
                    return false;
                }

                try
                {
                    var debugPath = _loggingSettings.GetFullDebugLogPath();
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ WindowsGraphicsCapturer: ネイティブキャプチャ初期化成功{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }

                _isInitialized = true;
                _logger?.LogInformation("Windows Graphics Captureが正常に初期化されました");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Windows Graphics Capture初期化中にエラーが発生");

                try
                {
                    var debugPath = _loggingSettings.GetFullDebugLogPath();
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 WindowsGraphicsCapturer: 初期化中に例外発生: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }
                return false;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 画面全体をキャプチャ（高性能版）
    /// </summary>
    /// <returns>キャプチャした画像</returns>
    public async Task<IWindowsImage> CaptureScreenAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_isInitialized)
        {
            var initSuccess = await InitializeAsync().ConfigureAwait(false);
            if (!initSuccess)
            {
                throw new InvalidOperationException("Windows Graphics Captureの初期化に失敗しました");
            }
        }

        _logger?.LogDebug("画面全体キャプチャを開始（Windows Graphics Capture）");

        try
        {
            // デスクトップウィンドウハンドルを取得
            var desktopWindow = GetDesktopWindow();
            return await CaptureWindowAsync(desktopWindow).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "画面全体キャプチャでエラーが発生");
            throw;
        }
    }

    /// <summary>
    /// 指定した領域をキャプチャ
    /// </summary>
    /// <param name="region">キャプチャする領域</param>
    /// <returns>キャプチャした画像</returns>
    public async Task<IWindowsImage> CaptureRegionAsync(Rectangle region)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger?.LogDebug("領域キャプチャを開始: {Region}（Windows Graphics Capture）", region);

        try
        {
            // 全画面キャプチャしてから領域を切り出す方式
            // TODO: 将来的にはネイティブレベルで領域指定キャプチャを実装
            var fullScreenImage = await CaptureScreenAsync().ConfigureAwait(false);

            // 領域切り出し処理
            var croppedImage = await CropImageAsync(fullScreenImage, region).ConfigureAwait(false);

            _logger?.LogDebug("領域キャプチャが完了");
            return croppedImage;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "領域キャプチャでエラーが発生: {Region}", region);
            throw;
        }
    }

    /// <summary>
    /// 指定したウィンドウをキャプチャ（最適化版）
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    public async Task<IWindowsImage> CaptureWindowAsync(IntPtr windowHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 🔍🔍🔍 デバッグ: 詳細ログ
        try
        {
            var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎥 WindowsGraphicsCapturer.CaptureWindowAsync: HWND=0x{windowHandle.ToInt64():X8} 開始, IsInitialized={_isInitialized}, IsDisposed={_disposed}{Environment.NewLine}");
        }
        catch { /* デバッグログ失敗は無視 */ }

        if (!_isInitialized)
        {
            try
            {
                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 WindowsGraphicsCapturer: 初期化開始{Environment.NewLine}");
            }
            catch { /* デバッグログ失敗は無視 */ }

            var initSuccess = await InitializeAsync().ConfigureAwait(false);
            if (!initSuccess)
            {
                try
                {
                    var debugPath = _loggingSettings.GetFullDebugLogPath();
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ WindowsGraphicsCapturer: 初期化失敗{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }
                throw new InvalidOperationException("Windows Graphics Captureの初期化に失敗しました");
            }

            try
            {
                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ WindowsGraphicsCapturer: 初期化成功{Environment.NewLine}");
            }
            catch { /* デバッグログ失敗は無視 */ }
        }

        _logger?.LogDebug("ウィンドウキャプチャを開始: 0x{WindowHandle:X8}（Windows Graphics Capture）", windowHandle.ToInt64());

        // 🚀 P3: Windows Graphics Capture試行とフォールバック機構
        var wgcFailureReason = "";

        try
        {
            // 🎯 P3: Primary Method - Windows Graphics Capture API試行
            var capturedImage = await TryWindowsGraphicsCaptureAsync(windowHandle).ConfigureAwait(false);
            if (capturedImage != null && IsImageValidForWGC(capturedImage))
            {
                // 🔍🔍🔍 デバッグ: WGC成功
                try
                {
                    var debugPath = _loggingSettings.GetFullDebugLogPath();
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ [P3_WGC_SUCCESS] Windows Graphics Capture成功 HWND=0x{windowHandle.ToInt64():X8}, サイズ={capturedImage.Width}x{capturedImage.Height}{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }

                return capturedImage;
            }
            else
            {
                wgcFailureReason = capturedImage == null ? "Null image" : "Invalid image quality";

                // 🔍🔍🔍 デバッグ: WGC品質不良
                try
                {
                    var debugPath = _loggingSettings.GetFullDebugLogPath();
                    var imageInfo = capturedImage != null ? $"Size={capturedImage.Width}x{capturedImage.Height}" : "null";
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⚠️ [P3_WGC_QUALITY] Windows Graphics Capture品質不良 HWND=0x{windowHandle.ToInt64():X8}, Image={imageInfo}, Reason={wgcFailureReason}{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }
            }
        }
        catch (Exception wgcEx)
        {
            wgcFailureReason = $"Exception: {wgcEx.GetType().Name}: {wgcEx.Message}";

            // 🔍🔍🔍 デバッグ: WGC例外
            try
            {
                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 [P3_WGC_EXCEPTION] Windows Graphics Capture例外 HWND=0x{windowHandle.ToInt64():X8}, Exception={wgcEx.GetType().Name}, Message={wgcEx.Message}{Environment.NewLine}");
            }
            catch { /* デバッグログ失敗は無視 */ }

            _logger?.LogWarning(wgcEx, "Windows Graphics Capture失敗、フォールバック検討中: 0x{WindowHandle:X8}", windowHandle.ToInt64());
        }

        // 🎯 P3: Fallback Method - GDI/PrintWindow試行
        try
        {
            var fallbackImage = await TryGdiFallbackCaptureAsync(windowHandle).ConfigureAwait(false);
            if (fallbackImage != null && IsImageValidForFallback(fallbackImage))
            {
                // 🔍🔍🔍 デバッグ: フォールバック成功
                try
                {
                    var debugPath = _loggingSettings.GetFullDebugLogPath();
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ [P3_FALLBACK_SUCCESS] GDI/PrintWindowフォールバック成功 HWND=0x{windowHandle.ToInt64():X8}, サイズ={fallbackImage.Width}x{fallbackImage.Height}, WGCFailureReason={wgcFailureReason}{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }

                _logger?.LogInformation("フォールバックキャプチャ成功: WGC失敗 ({FailureReason}) → GDI成功 ({Width}x{Height})",
                    wgcFailureReason, fallbackImage.Width, fallbackImage.Height);

                return fallbackImage;
            }
            else
            {
                // 🔍🔍🔍 デバッグ: フォールバック品質不良
                try
                {
                    var debugPath = _loggingSettings.GetFullDebugLogPath();
                    var imageInfo = fallbackImage != null ? $"Size={fallbackImage.Width}x{fallbackImage.Height}" : "null";
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⚠️ [P3_FALLBACK_QUALITY] GDIフォールバック品質不良 HWND=0x{windowHandle.ToInt64():X8}, Image={imageInfo}{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }

                _logger?.LogWarning("フォールバックキャプチャ品質不良: WGC失敗 ({WgcReason}) → GDI品質不良", wgcFailureReason);
            }
        }
        catch (Exception fallbackEx)
        {
            // 🔍🔍🔍 デバッグ: フォールバック例外
            try
            {
                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 [P3_FALLBACK_EXCEPTION] GDIフォールバック例外 HWND=0x{windowHandle.ToInt64():X8}, Exception={fallbackEx.GetType().Name}, Message={fallbackEx.Message}{Environment.NewLine}");
            }
            catch { /* デバッグログ失敗は無視 */ }

            _logger?.LogError(fallbackEx, "フォールバックキャプチャも失敗: WGC失敗 ({WgcReason}) → GDI例外", wgcFailureReason);
        }

        // 🔍🔍🔍 デバッグ: 完全失敗
        try
        {
            var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ [P3_COMPLETE_FAILURE] 全キャプチャ方式失敗 HWND=0x{windowHandle.ToInt64():X8}, WGCReason={wgcFailureReason}{Environment.NewLine}");
        }
        catch { /* デバッグログ失敗は無視 */ }

        // 全ての方式が失敗
        var finalErrorMessage = $"ウィンドウキャプチャが全て失敗: WGC失敗 ({wgcFailureReason}) → GDI失敗";
        _logger?.LogError(finalErrorMessage + ": 0x{WindowHandle:X8}", windowHandle.ToInt64());
        throw new InvalidOperationException(finalErrorMessage);
    }

    /// <summary>
    /// 🚀 [Issue #193] 指定したウィンドウをGPU上でリサイズしてキャプチャ
    /// GPU→CPU転送量を削減し、パフォーマンスを向上（4K→HD: 75%削減）
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <param name="targetWidth">ターゲット幅</param>
    /// <param name="targetHeight">ターゲット高さ</param>
    /// <returns>リサイズされたキャプチャ画像</returns>
    public async Task<IWindowsImage> CaptureWindowResizedAsync(IntPtr windowHandle, int targetWidth, int targetHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger?.LogDebug("🚀 [Issue #193] GPUリサイズキャプチャ開始: HWND=0x{WindowHandle:X8}, Target={Width}x{Height}",
            windowHandle.ToInt64(), targetWidth, targetHeight);

        if (!_isInitialized)
        {
            var initSuccess = await InitializeAsync().ConfigureAwait(false);
            if (!initSuccess)
            {
                throw new InvalidOperationException("Windows Graphics Captureの初期化に失敗しました");
            }
        }

        try
        {
            // キャプチャセッションを作成
            var sessionCreated = _nativeCapture.CreateCaptureSession(windowHandle);
            if (!sessionCreated)
            {
                _logger?.LogWarning("🚀 [Issue #193] セッション作成失敗、通常キャプチャにフォールバック");
                return await CaptureWindowAsync(windowHandle).ConfigureAwait(false);
            }

            // 🚀 GPUシェーダーリサイズを使用したフレームキャプチャ
            var timeoutMs = 5000;
            var capturedImage = await _nativeCapture.CaptureFrameResizedAsync(targetWidth, targetHeight, timeoutMs).ConfigureAwait(false);

            if (capturedImage != null && capturedImage.Width > 0 && capturedImage.Height > 0)
            {
                _logger?.LogInformation("✅ [Issue #193] GPUリサイズキャプチャ成功: {Width}x{Height} (Target: {TargetWidth}x{TargetHeight})",
                    capturedImage.Width, capturedImage.Height, targetWidth, targetHeight);
                return capturedImage;
            }

            // リサイズ失敗時は通常キャプチャにフォールバック
            _logger?.LogWarning("🚀 [Issue #193] GPUリサイズ失敗、通常キャプチャにフォールバック");
            return await CaptureWindowAsync(windowHandle).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "🚀 [Issue #193] GPUリサイズキャプチャ例外、通常キャプチャにフォールバック");
            return await CaptureWindowAsync(windowHandle).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 指定したウィンドウのクライアント領域をキャプチャ
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像</returns>
    public async Task<IWindowsImage> CaptureClientAreaAsync(IntPtr windowHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger?.LogDebug("ウィンドウクライアント領域キャプチャを開始: 0x{WindowHandle:X8}（Windows Graphics Capture）",
            windowHandle.ToInt64());

        try
        {
            // Windows Graphics Capture APIではウィンドウ全体がキャプチャされる
            // クライアント領域のみを抽出するための処理
            var fullWindowImage = await CaptureWindowAsync(windowHandle).ConfigureAwait(false);

            // クライアント領域の座標を取得
            var clientRect = GetClientAreaBounds(windowHandle);
            if (clientRect.IsEmpty)
            {
                _logger?.LogWarning("クライアント領域の取得に失敗、ウィンドウ全体を返却");
                return fullWindowImage;
            }

            // クライアント領域のみを切り出し
            var clientAreaImage = await CropImageAsync(fullWindowImage, clientRect).ConfigureAwait(false);

            _logger?.LogDebug("ウィンドウクライアント領域キャプチャが完了");
            return clientAreaImage;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ウィンドウクライアント領域キャプチャでエラーが発生: 0x{WindowHandle:X8}",
                windowHandle.ToInt64());
            throw;
        }
    }

    /// <summary>
    /// キャプチャオプションを設定
    /// </summary>
    /// <param name="options">キャプチャオプション</param>
    public void SetCaptureOptions(WindowsCaptureOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger?.LogDebug("キャプチャオプションを設定: Quality={Quality}, IncludeCursor={IncludeCursor}, UseDwm={UseDwm}",
            _options.Quality, _options.IncludeCursor, _options.UseDwmCapture);
    }

    /// <summary>
    /// 現在のキャプチャオプションを取得
    /// </summary>
    /// <returns>キャプチャオプション</returns>
    public WindowsCaptureOptions GetCaptureOptions()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _options;
    }

    /// <summary>
    /// 画像を指定領域で切り出し
    /// </summary>
    /// <param name="sourceImage">元画像</param>
    /// <param name="cropRegion">切り出し領域</param>
    /// <returns>切り出された画像</returns>
    private async Task<IWindowsImage> CropImageAsync(IWindowsImage sourceImage, Rectangle cropRegion)
    {
        return await Task.Run(() =>
        {
            try
            {
                // 元画像の境界内に切り出し領域を制限
                var imageBounds = new Rectangle(0, 0, sourceImage.Width, sourceImage.Height);
                var validCropRegion = Rectangle.Intersect(cropRegion, imageBounds);

                if (validCropRegion.IsEmpty)
                {
                    throw new ArgumentException("切り出し領域が画像範囲外です");
                }

                // 実際の切り出し処理（WindowsImageの実装に依存）
                // TODO: IWindowsImageインターフェースにCrop機能を追加することを検討
                _logger?.LogDebug("画像切り出し: 元サイズ={Width}x{Height}, 切り出し領域={CropRegion}",
                    sourceImage.Width, sourceImage.Height, validCropRegion);

                // 暫定的に元画像をそのまま返す（実際の切り出し処理は要実装）
                return sourceImage;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "画像切り出し中にエラーが発生");
                throw;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// ウィンドウのクライアント領域の境界を取得
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>クライアント領域の境界</returns>
    private Rectangle GetClientAreaBounds(IntPtr windowHandle)
    {
        try
        {
            // Windows APIを使用してクライアント領域を取得
            if (GetClientRect(windowHandle, out var clientRect))
            {
                return new Rectangle(0, 0, clientRect.Right - clientRect.Left, clientRect.Bottom - clientRect.Top);
            }

            return Rectangle.Empty;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "クライアント領域境界取得中にエラー");
            return Rectangle.Empty;
        }
    }

    /// <summary>
    /// 現在のキャプチャセッションを停止
    /// </summary>
    public void StopCurrentSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            _logger?.LogDebug("WindowsGraphicsCapturer セッション停止");
            _nativeCapture?.StopCurrentSession();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "WindowsGraphicsCapturer セッション停止中にエラー");
        }
    }

    /// <summary>
    /// 🎯 P3: Windows Graphics Capture API試行（元のロジック）
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像またはnull</returns>
    private async Task<IWindowsImage?> TryWindowsGraphicsCaptureAsync(IntPtr windowHandle)
    {
        try
        {
            // キャプチャセッションを作成
            var sessionCreated = _nativeCapture.CreateCaptureSession(windowHandle);

            // 🔍🔍🔍 デバッグ: セッション作成結果
            try
            {
                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                if (!sessionCreated)
                {
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📝 [P3_WGC_TRY] CreateCaptureSession結果: False, HWND=0x{windowHandle.ToInt64():X8}{Environment.NewLine}");
                }
                else
                {
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎥 [P3_WGC_TRY] CreateCaptureSession結果: True, SessionId={_nativeCapture.SessionId}{Environment.NewLine}");
                }
            }
            catch { /* デバッグログ失敗は無視 */ }

            if (!sessionCreated)
            {
                return null; // セッション作成失敗
            }

            // キャプチャオプションに基づいてタイムアウトを設定（デフォルトは5秒）
            var timeoutMs = 5000;

            // フレームキャプチャを実行
            var capturedImage = await _nativeCapture.CaptureFrameAsync(timeoutMs).ConfigureAwait(false);

            if (capturedImage == null)
            {
                return null; // フレームキャプチャ失敗
            }

            return capturedImage;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[P3_WGC_TRY] Windows Graphics Capture試行中に例外: 0x{WindowHandle:X8}", windowHandle.ToInt64());
            return null;
        }
    }

    /// <summary>
    /// 🎯 P3: GDI/PrintWindow フォールバック試行
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>キャプチャした画像またはnull</returns>
    private async Task<IWindowsImage?> TryGdiFallbackCaptureAsync(IntPtr windowHandle)
    {
        try
        {
            // 🔍🔍🔍 デバッグ: GDI試行開始
            try
            {
                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🖨️ [P3_GDI_TRY] GDI/PrintWindowフォールバック開始 HWND=0x{windowHandle.ToInt64():X8}{Environment.NewLine}");
            }
            catch { /* デバッグログ失敗は無視 */ }

            return await Task.Run(() =>
            {
                try
                {
                    // ウィンドウ情報を取得
                    if (!GetWindowRect(windowHandle, out var windowRect))
                    {
                        // 🔍🔍🔍 デバッグ: ウィンドウ矩形取得失敗
                        try
                        {
                            var debugPath = _loggingSettings.GetFullDebugLogPath();
                            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ [P3_GDI_TRY] GetWindowRect失敗 HWND=0x{windowHandle.ToInt64():X8}{Environment.NewLine}");
                        }
                        catch { /* デバッグログ失敗は無視 */ }
                        return null;
                    }

                    var width = windowRect.Right - windowRect.Left;
                    var height = windowRect.Bottom - windowRect.Top;

                    if (width <= 0 || height <= 0)
                    {
                        // 🔍🔍🔍 デバッグ: 無効なサイズ
                        try
                        {
                            var debugPath = _loggingSettings.GetFullDebugLogPath();
                            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ [P3_GDI_TRY] 無効なウィンドウサイズ HWND=0x{windowHandle.ToInt64():X8}, Size={width}x{height}{Environment.NewLine}");
                        }
                        catch { /* デバッグログ失敗は無視 */ }
                        return null;
                    }

                    // PrintWindow APIを使用してキャプチャ
                    using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using var graphics = System.Drawing.Graphics.FromImage(bitmap);

                    var hdc = graphics.GetHdc();
                    try
                    {
                        // PrintWindow APIでウィンドウをキャプチャ
                        var printResult = PrintWindow(windowHandle, hdc, 0);

                        // 🔍🔍🔍 デバッグ: PrintWindow結果
                        try
                        {
                            var debugPath = _loggingSettings.GetFullDebugLogPath();
                            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🖨️ [P3_GDI_TRY] PrintWindow結果: {printResult}, Size={width}x{height}{Environment.NewLine}");
                        }
                        catch { /* デバッグログ失敗は無視 */ }

                        if (!printResult)
                        {
                            return null; // PrintWindow失敗
                        }
                    }
                    finally
                    {
                        graphics.ReleaseHdc(hdc);
                    }

                    // BitmapをIWindowsImageに変換
                    var windowsImage = ConvertBitmapToWindowsImage(bitmap);

                    // 🔍🔍🔍 デバッグ: 変換結果
                    try
                    {
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
                        var imageInfo = windowsImage != null ? $"Size={windowsImage.Width}x{windowsImage.Height}, Type={windowsImage.GetType().Name}" : "null";
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔄 [P3_GDI_TRY] Bitmap変換結果: {imageInfo}{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }

                    return windowsImage;
                }
                catch (Exception ex)
                {
                    // 🔍🔍🔍 デバッグ: GDI例外
                    try
                    {
                        var debugPath = _loggingSettings.GetFullDebugLogPath();
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 [P3_GDI_TRY] GDI処理中例外: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }

                    _logger?.LogWarning(ex, "[P3_GDI_TRY] GDIフォールバック処理中に例外: 0x{WindowHandle:X8}", windowHandle.ToInt64());
                    return null;
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[P3_GDI_TRY] GDIフォールバック試行中に例外: 0x{WindowHandle:X8}", windowHandle.ToInt64());
            return null;
        }
    }

    /// <summary>
    /// 🎯 P3: Windows Graphics Capture画像の品質検証
    /// </summary>
    /// <param name="image">検証対象画像</param>
    /// <returns>WGCとして有効な品質の場合true</returns>
    private bool IsImageValidForWGC(IWindowsImage image)
    {
        try
        {
            if (image == null || image.Width <= 0 || image.Height <= 0)
                return false;

            // WGCは通常高品質なので基本的な検証のみ
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[P3_WGC_VALIDATION] WGC画像品質検証中に例外");
            return false;
        }
    }

    /// <summary>
    /// 🎯 P3: フォールバック画像の品質検証
    /// </summary>
    /// <param name="image">検証対象画像</param>
    /// <returns>フォールバックとして有効な品質の場合true</returns>
    private bool IsImageValidForFallback(IWindowsImage image)
    {
        try
        {
            if (image == null || image.Width <= 0 || image.Height <= 0)
                return false;

            // フォールバック画像はより厳しい検証
            // 最小サイズチェック（50x50未満は無効とする）
            if (image.Width < 50 || image.Height < 50)
                return false;

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[P3_FALLBACK_VALIDATION] フォールバック画像品質検証中に例外");
            return false;
        }
    }

    /// <summary>
    /// 🎯 P3: System.Drawing.BitmapをIWindowsImageに変換
    /// </summary>
    /// <param name="bitmap">変換対象Bitmap</param>
    /// <returns>変換されたIWindowsImage</returns>
    private IWindowsImage? ConvertBitmapToWindowsImage(System.Drawing.Bitmap bitmap)
    {
        try
        {
            if (bitmap == null)
            {
                return null;
            }

            // Bitmapのクローンを作成して所有権を分離
            var clonedBitmap = new System.Drawing.Bitmap(bitmap);

            // WindowsImageクラスでラップ
            return new WindowsImage(clonedBitmap);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[P3_CONVERSION] Bitmap変換中に例外");
            return null;
        }
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _nativeCapture?.Dispose();
            _isInitialized = false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "WindowsGraphicsCapturer破棄中にエラーが発生");
        }

        _disposed = true;
        GC.SuppressFinalize(this);
        _logger?.LogDebug("WindowsGraphicsCapturerが破棄されました");
    }

    // Windows API P/Invoke declarations
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// 🎯 P3: ウィンドウの矩形を取得するWindows API
    /// </summary>
    /// <param name="hWnd">ウィンドウハンドル</param>
    /// <param name="lpRect">矩形情報（出力）</param>
    /// <returns>成功時はtrue</returns>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// 🎯 P3: ウィンドウ内容をデバイスコンテキストにプリントするWindows API
    /// </summary>
    /// <param name="hWnd">ウィンドウハンドル</param>
    /// <param name="hDC">デバイスコンテキスト</param>
    /// <param name="nFlags">プリントフラグ（0=標準、1=クライアント領域のみ、2=非クライアント領域のみ）</param>
    /// <returns>成功時はtrue</returns>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hDC, uint nFlags);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
