using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Platform.Windows;
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
    public WindowsGraphicsCapturer(
        NativeWindowsCaptureWrapper nativeCapture, 
        ILogger<WindowsGraphicsCapturer>? logger = null)
    {
        _nativeCapture = nativeCapture ?? throw new ArgumentNullException(nameof(nativeCapture));
        _logger = logger;
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
                    var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 WindowsGraphicsCapturer: サポート状況チェック開始{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }

                if (!_nativeCapture.IsSupported())
                {
                    _logger?.LogWarning("Windows Graphics Capture APIがサポートされていません");
                    
                    try
                    {
                        var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ WindowsGraphicsCapturer: APIサポートされていません{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }
                    return false;
                }

                try
                {
                    var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ WindowsGraphicsCapturer: APIサポート確認完了{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }

                // 🔍🔍🔍 デバッグ: ネイティブキャプチャ初期化
                try
                {
                    var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 WindowsGraphicsCapturer: ネイティブキャプチャ初期化開始{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }

                if (!_nativeCapture.Initialize())
                {
                    _logger?.LogError("Windows Graphics Captureの初期化に失敗");
                    
                    try
                    {
                        var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ WindowsGraphicsCapturer: ネイティブキャプチャ初期化失敗{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }
                    return false;
                }

                try
                {
                    var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
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
                    var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
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
                    var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
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

        try
        {
            // キャプチャセッションを作成
            var sessionCreated = _nativeCapture.CreateCaptureSession(windowHandle);
            
            // 🔍🔍🔍 デバッグ: セッション作成結果
            try
            {
                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎥 CreateCaptureSession結果: {sessionCreated}, SessionId={_nativeCapture.SessionId}{Environment.NewLine}");
            }
            catch { /* デバッグログ失敗は無視 */ }
            
            if (!sessionCreated)
            {
                throw new InvalidOperationException($"ウィンドウ 0x{windowHandle.ToInt64():X8} のキャプチャセッション作成に失敗");
            }

            // キャプチャオプションに基づいてタイムアウトを設定（デフォルトは5秒）
            var timeoutMs = 5000;
            
            // フレームキャプチャを実行
            var capturedImage = await _nativeCapture.CaptureFrameAsync(timeoutMs).ConfigureAwait(false);

            if (capturedImage == null)
            {
                throw new InvalidOperationException($"ウィンドウ 0x{windowHandle.ToInt64():X8} のフレームキャプチャに失敗");
            }

            _logger?.LogDebug("ウィンドウキャプチャが完了: {Width}x{Height}", 
                capturedImage.Width, capturedImage.Height);
            
            // 🔍🔍🔍 デバッグ: キャプチャした画像の内容を検証
            try
            {
                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🖼️ WindowsGraphicsCapturer: キャプチャ完了 HWND=0x{windowHandle.ToInt64():X8}, サイズ={capturedImage.Width}x{capturedImage.Height}, Type={capturedImage.GetType().Name}{Environment.NewLine}");
            }
            catch { /* デバッグログ失敗は無視 */ }
            
            return capturedImage;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ウィンドウキャプチャでエラーが発生: 0x{WindowHandle:X8}", windowHandle.ToInt64());
            throw;
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

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}