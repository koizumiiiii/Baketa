using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Windows.NativeMethods;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Capture;

    /// <summary>
    /// GDIベースの画面キャプチャ実装
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class GdiScreenCapturer : IGdiScreenCapturer
    {
        private readonly Baketa.Core.Abstractions.Factories.IWindowsImageFactory _imageFactory;
        private readonly ILogger<GdiScreenCapturer>? _logger;
        
        // LoggerMessageデリゲートの定義
        private static class Log
        {
            private static readonly Action<ILogger, string, Exception?> _captureStart =
                LoggerMessage.Define<string>(
                    LogLevel.Debug,
                    new EventId(1, nameof(CaptureStart)),
                    "{Message}");
                    
            private static readonly Action<ILogger, IntPtr, Exception?> _windowCaptureStart =
                LoggerMessage.Define<IntPtr>(
                    LogLevel.Debug,
                    new EventId(2, nameof(WindowCaptureStart)),
                    "ウィンドウ (HWND: {Hwnd}) のキャプチャを開始");
                    
            private static readonly Action<ILogger, string, Exception?> _printWindowFallback =
                LoggerMessage.Define<string>(
                    LogLevel.Warning,
                    new EventId(3, nameof(PrintWindowFallback)),
                    "{Message}");
                    
            private static readonly Action<ILogger, int, int, Exception?> _captureCompleted =
                LoggerMessage.Define<int, int>(
                    LogLevel.Debug,
                    new EventId(4, nameof(CaptureCompleted)),
                    "キャプチャ完了: {Width}x{Height}");
                    
            public static void CaptureStart(ILogger logger, string message)
                => _captureStart(logger, message, null);
                
            public static void WindowCaptureStart(ILogger logger, IntPtr hwnd)
                => _windowCaptureStart(logger, hwnd, null);
                
            public static void PrintWindowFallback(ILogger logger, string message)
                => _printWindowFallback(logger, message, null);
                
            public static void CaptureCompleted(ILogger logger, int width, int height)
                => _captureCompleted(logger, width, height, null);
        }
        
        // DIBセクションの再利用による最適化のためのフィールド
        private IntPtr _hdcMemory;
        private IntPtr _hBitmap;
        private int _lastWidth;
        private int _lastHeight;
        private bool _disposed;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="imageFactory">WindowsImageファクトリー</param>
        /// <param name="logger">ロガー（オプション）</param>
        public GdiScreenCapturer(
            Baketa.Core.Abstractions.Factories.IWindowsImageFactory imageFactory,
            ILogger<GdiScreenCapturer>? logger = null)
        {
            _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
            _logger = logger;
            
            _hdcMemory = IntPtr.Zero;
            _hBitmap = IntPtr.Zero;
            _lastWidth = 0;
            _lastHeight = 0;
        }
        
        /// <summary>
        /// プライマリスクリーン全体をキャプチャします
        /// </summary>
        /// <returns>キャプチャした画像</returns>
        public async Task<IWindowsImage> CaptureScreenAsync()
        {
            if (_logger != null)
                Log.CaptureStart(_logger, "プライマリスクリーンのキャプチャを開始");
            
            // プライマリスクリーンのサイズ取得
            int screenWidth = User32Methods.GetSystemMetrics(SystemMetric.SM_CXSCREEN);
            int screenHeight = User32Methods.GetSystemMetrics(SystemMetric.SM_CYSCREEN);
            
            return await CaptureRegionAsync(new Rectangle(0, 0, screenWidth, screenHeight)).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 指定したウィンドウをキャプチャします
        /// </summary>
        /// <param name="hWnd">ウィンドウハンドル</param>
        /// <returns>キャプチャした画像</returns>
        public async Task<IWindowsImage> CaptureWindowAsync(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                throw new ArgumentException("ウィンドウハンドルが無効です", nameof(hWnd));
                
            if (_logger != null)
                Log.WindowCaptureStart(_logger, hWnd);
            
            // ウィンドウの領域を取得
            if (!User32Methods.GetWindowRect(hWnd, out RECT rect))
            {
                throw new InvalidOperationException($"ウィンドウの領域取得に失敗: {hWnd}");
            }
            
            // クライアント領域のキャプチャ
            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;
            
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException($"無効なウィンドウサイズ: {width}x{height}");
            }
            
            // DPI対応
            User32Methods.SetProcessDPIAware();
            
            return await Task.Run(() =>
            {
                // メモリDCの準備
                using var screenDC = new DeviceContextHandle(User32Methods.GetDC(IntPtr.Zero));
                using var memoryDC = new DeviceContextHandle(Gdi32Methods.CreateCompatibleDC(screenDC.DangerousGetHandle()));
                using var bitmapHandle = new BitmapHandle(Gdi32Methods.CreateCompatibleBitmap(screenDC.DangerousGetHandle(), width, height));
                
                if (memoryDC.IsInvalid || bitmapHandle.IsInvalid)
                {
                    throw new InvalidOperationException("デバイスコンテキストまたはビットマップの作成に失敗しました");
                }
                
                Gdi32Methods.SelectObject(memoryDC.DangerousGetHandle(), bitmapHandle.DangerousGetHandle());
                
                // PrintWindowを使用してウィンドウをキャプチャ
                if (!User32Methods.PrintWindow(hWnd, memoryDC.DangerousGetHandle(), PrintWindowFlags.PW_CLIENTONLY))
                {
                    if (_logger != null)
                        Log.PrintWindowFallback(_logger, "PrintWindow失敗 - BitBltにフォールバック");
                    
                    // PrintWindowが失敗した場合、BitBltにフォールバック
                    // ただしこれはウィンドウが表示されている場合のみ機能する
                    Gdi32Methods.BitBlt(
                        memoryDC.DangerousGetHandle(),
                        0, 0, width, height,
                        screenDC.DangerousGetHandle(),
                        rect.left, rect.top,
                        BitBltFlags.SRCCOPY);
                }
                
                // ビットマップからイメージを作成
                var bitmap = System.Drawing.Image.FromHbitmap(bitmapHandle.DangerousGetHandle());
                var windowsImage = _imageFactory.CreateFromBitmap((Bitmap)bitmap);
                
                if (_logger != null)
                    Log.CaptureCompleted(_logger, width, height);
                
                return windowsImage;
            }).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 指定した領域をキャプチャします
        /// </summary>
        /// <param name="region">キャプチャする領域</param>
        /// <returns>キャプチャした画像</returns>
        public async Task<IWindowsImage> CaptureRegionAsync(Rectangle region)
        {
            if (_logger != null)
                Log.CaptureStart(_logger, $"画面領域 {region} のキャプチャを開始");
            
            int width = region.Width;
            int height = region.Height;
            
            if (width <= 0 || height <= 0)
                throw new ArgumentException("キャプチャ領域のサイズが無効です", nameof(region));
                
            // DPI対応
            User32Methods.SetProcessDPIAware();
            
            return await Task.Run(() =>
            {
                // デバイスコンテキスト取得
                using var screenDC = new DeviceContextHandle(User32Methods.GetDC(IntPtr.Zero));
                
                // メモリDCの準備または再利用
                EnsureMemoryDC(screenDC.DangerousGetHandle(), width, height);
                
                // BitBltでキャプチャ実行
                if (!Gdi32Methods.BitBlt(
                    _hdcMemory,
                    0, 0, width, height,
                    screenDC.DangerousGetHandle(),
                    region.X, region.Y,
                    BitBltFlags.SRCCOPY))
                {
                    throw new InvalidOperationException("BitBlt操作に失敗しました");
                }
                
                // ビットマップからイメージを作成
                var bitmap = System.Drawing.Image.FromHbitmap(_hBitmap);
                var windowsImage = _imageFactory.CreateFromBitmap((Bitmap)bitmap);
                
                // 元のBitmapオブジェクトは解放
                bitmap.Dispose();
                
                if (_logger != null)
                    Log.CaptureCompleted(_logger, width, height);
                
                return windowsImage;
            }).ConfigureAwait(false);
        }
        
        /// <summary>
        /// メモリDCの準備または再利用を行います
        /// </summary>
        private void EnsureMemoryDC(IntPtr hdcScreen, int width, int height)
        {
            // 既存のメモリDCが再利用可能か確認
            if (_hdcMemory != IntPtr.Zero && _lastWidth == width && _lastHeight == height)
            {
                return;
            }
            
            // 既存のリソースを解放
            CleanupResources();
            
            // 新しいメモリDC作成
            _hdcMemory = Gdi32Methods.CreateCompatibleDC(hdcScreen);
            _hBitmap = Gdi32Methods.CreateCompatibleBitmap(hdcScreen, width, height);
            
            if (_hdcMemory == IntPtr.Zero || _hBitmap == IntPtr.Zero)
            {
                CleanupResources();
                throw new InvalidOperationException("メモリDCの作成に失敗しました");
            }
            
            // メモリDCにビットマップを選択
            Gdi32Methods.SelectObject(_hdcMemory, _hBitmap);
            
            _lastWidth = width;
            _lastHeight = height;
        }
        
        /// <summary>
        /// GDIリソースを解放します
        /// </summary>
        private void CleanupResources()
        {
            if (_hBitmap != IntPtr.Zero)
            {
                Gdi32Methods.DeleteObject(_hBitmap);
                _hBitmap = IntPtr.Zero;
            }
            
            if (_hdcMemory != IntPtr.Zero)
            {
                Gdi32Methods.DeleteDC(_hdcMemory);
                _hdcMemory = IntPtr.Zero;
            }
            
            _lastWidth = 0;
            _lastHeight = 0;
        }
        
        /// <summary>
        /// ファイナライザー
        /// </summary>
        ~GdiScreenCapturer()
        {
            Dispose(false);
        }
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースも解放する場合はtrue</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;
                
            CleanupResources();
            _disposed = true;
        }
    }
