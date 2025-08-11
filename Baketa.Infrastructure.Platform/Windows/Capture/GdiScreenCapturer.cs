using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Windows.NativeMethods;
using Microsoft.Extensions.Logging;
using Baketa.Core.Utilities;

namespace Baketa.Infrastructure.Platform.Windows.Capture;

    /// <summary>
    /// GDIベースの画面キャプチャ実装
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class GdiScreenCapturer : IGdiScreenCapturer
    {
        private readonly Baketa.Core.Abstractions.Factories.IWindowsImageFactory _imageFactory;
        private readonly ILogger<GdiScreenCapturer>? _logger;
        private readonly WinRTWindowCapture _winRTCapture;
        
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
            _winRTCapture = new WinRTWindowCapture(imageFactory, logger as ILogger<WinRTWindowCapture>);
            
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
            
            // ウィンドウが最小化されている場合はエラー
            if (User32Methods.IsIconic(hWnd))
            {
                DebugLogUtility.WriteLog($"❌ ウィンドウが最小化されています: Handle={hWnd}");
                throw new InvalidOperationException("最小化されたウィンドウはキャプチャできません");
            }
            
            // ウィンドウが表示されているか確認
            if (!User32Methods.IsWindowVisible(hWnd))
            {
                DebugLogUtility.WriteLog($"⚠️ ウィンドウが非表示です: Handle={hWnd}");
            }
            
            DebugLogUtility.WriteLog($"🚀 Windows Graphics Capture API を使用してキャプチャ開始: Handle={hWnd}");
            Console.WriteLine($"🚀 GdiScreenCapturer: Windows Graphics Capture API を使用してキャプチャ開始: Handle={hWnd.ToInt64():X8}");

            try
            {
                // Windows Graphics Capture API を使用
                Console.WriteLine($"📞 GdiScreenCapturer: _winRTCapture.CaptureWindowAsync呼び出し中...");
                var result = await _winRTCapture.CaptureWindowAsync(hWnd).ConfigureAwait(false);
                
                DebugLogUtility.WriteLog($"✅ Windows Graphics Capture API キャプチャ成功: {result.Width}x{result.Height}");
                Console.WriteLine($"✅ GdiScreenCapturer: Windows Graphics Capture API キャプチャ成功: {result.Width}x{result.Height}");
                
                if (_logger != null)
                    Log.CaptureCompleted(_logger, result.Width, result.Height);
                
                return result;
            }
            catch (Exception ex)
            {
                DebugLogUtility.WriteLog($"❌ Windows Graphics Capture API 失敗: {ex.Message}");
                Console.WriteLine($"❌ GdiScreenCapturer: Windows Graphics Capture API 失敗: {ex.Message}");
                DebugLogUtility.WriteLog($"❌ Windows Graphics Capture API 失敗: {ex.Message}");
                _logger?.LogWarning(ex, "Windows Graphics Capture API failed, falling back to BitBlt");
                
                // フォールバック: BitBlt を使用
                return await CaptureWindowWithBitBltFallback(hWnd).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// BitBltフォールバック処理
        /// </summary>
        private async Task<IWindowsImage> CaptureWindowWithBitBltFallback(IntPtr hWnd)
        {
            DebugLogUtility.WriteLog($"🔄 BitBltフォールバック開始: Handle={hWnd}");
            
            // ウィンドウの領域を取得
            if (!User32Methods.GetWindowRect(hWnd, out RECT rect))
            {
                throw new InvalidOperationException($"ウィンドウの領域取得に失敗: {hWnd}");
            }
            
            int width = rect.right - rect.left;
            int height = rect.bottom - rect.top;
            
            DebugLogUtility.WriteLog($"📏 ウィンドウ座標: {rect.left}, {rect.top}, {rect.right}, {rect.bottom}");
            DebugLogUtility.WriteLog($"📐 ウィンドウサイズ: {width}x{height}");
            
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
                
                var oldBitmap = Gdi32Methods.SelectObject(memoryDC.DangerousGetHandle(), bitmapHandle.DangerousGetHandle());
                
                try
                {
                    DebugLogUtility.WriteLog($"🔸 BitBltフォールバック実行: 領域({rect.left}, {rect.top}, {width}, {height})");
                    
                    bool bitBltSuccess = Gdi32Methods.BitBlt(
                        memoryDC.DangerousGetHandle(),
                        0, 0, width, height,
                        screenDC.DangerousGetHandle(),
                        rect.left, rect.top,
                        BitBltFlags.SRCCOPY);
                    
                    DebugLogUtility.WriteLog($"🔸 BitBlt結果: {(bitBltSuccess ? "成功" : "失敗")}");
                    
                    if (!bitBltSuccess)
                    {
                        throw new InvalidOperationException("BitBltキャプチャに失敗しました");
                    }
                    
                    DebugLogUtility.WriteLog($"📋 キャプチャ方式: BitBlt API（画面領域キャプチャ）");
                
                    // ビットマップからイメージを作成
                    var bitmap = System.Drawing.Image.FromHbitmap(bitmapHandle.DangerousGetHandle());
                    var windowsImage = _imageFactory.CreateFromBitmap((Bitmap)bitmap);
                    
                    if (_logger != null)
                        Log.CaptureCompleted(_logger, width, height);
                    
                    return windowsImage;
                }
                finally
                {
                    // 旧ビットマップを復元
                    Gdi32Methods.SelectObject(memoryDC.DangerousGetHandle(), oldBitmap);
                }
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
                
                // WindowsImageが内部的にBitmapを参照するため、元のBitmapは破棄しない
                // bitmap.Dispose(); // WindowsImageのDispose時に適切に処理される
                
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
        /// キャプチャした画像の詳細分析
        /// </summary>
        private void AnalyzeCapturedImage(IntPtr hdc, int width, int height, string method)
        {
            try
            {
                DebugLogUtility.WriteLog($"🔍 画像分析開始: {method}");
                DebugLogUtility.WriteLog($"📐 画像サイズ: {width}x{height}");
                
                // 簡易的な画像内容チェック（ピクセル値のサンプリング）
                var samplePoints = new[]
                {
                    new Point(width / 4, height / 4),
                    new Point(width / 2, height / 2), 
                    new Point(3 * width / 4, 3 * height / 4)
                };
                
                bool hasNonBlackPixels = false;
                foreach (var point in samplePoints)
                {
                    var pixel = Gdi32Methods.GetPixel(hdc, point.X, point.Y);
                    if (pixel != 0) // 0 = 黒色
                    {
                        hasNonBlackPixels = true;
                        break;
                    }
                }
                
                DebugLogUtility.WriteLog($"🎨 画像内容: {(hasNonBlackPixels ? "有効なコンテンツあり" : "黒画像または空")}");
                
                if (!hasNonBlackPixels)
                {
                    DebugLogUtility.WriteLog($"⚠️ 警告: {method}で取得した画像が黒画像の可能性");
                }
            }
            catch (Exception ex)
            {
                DebugLogUtility.WriteLog($"❌ 画像分析エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ウィンドウ状態の詳細分析
        /// </summary>
        private void AnalyzeWindowState(IntPtr hWnd)
        {
            try
            {
                DebugLogUtility.WriteLog($"🔍 ウィンドウ状態分析開始: {hWnd}");
                
                var isVisible = User32Methods.IsWindowVisible(hWnd);
                var isIconic = User32Methods.IsIconic(hWnd);
                var isZoomed = User32Methods.IsZoomed(hWnd);
                
                DebugLogUtility.WriteLog($"👁️ 表示状態: {(isVisible ? "表示" : "非表示")}");
                DebugLogUtility.WriteLog($"📉 最小化: {(isIconic ? "はい" : "いいえ")}");
                DebugLogUtility.WriteLog($"📈 最大化: {(isZoomed ? "はい" : "いいえ")}");
                
                // ウィンドウクラス名を取得
                var className = GetWindowClassName(hWnd);
                DebugLogUtility.WriteLog($"🏷️ ウィンドウクラス: {className}");
                
                // ウィンドウのスタイル情報
                var style = User32Methods.GetWindowLong(hWnd, GetWindowLongIndex.GWL_STYLE);
                var exStyle = User32Methods.GetWindowLong(hWnd, GetWindowLongIndex.GWL_EXSTYLE);
                
                DebugLogUtility.WriteLog($"🎨 ウィンドウスタイル: 0x{style:X8}");
                DebugLogUtility.WriteLog($"🎨 拡張スタイル: 0x{exStyle:X8}");
                
                // LayeredWindow かどうかチェック
                const int WS_EX_LAYERED = 0x80000;
                if ((exStyle & WS_EX_LAYERED) != 0)
                {
                    DebugLogUtility.WriteLog($"⚠️ LayeredWindow検出: PrintWindowが動作しない可能性");
                }
            }
            catch (Exception ex)
            {
                DebugLogUtility.WriteLog($"❌ ウィンドウ状態分析エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ウィンドウクラス名を取得
        /// </summary>
        private string GetWindowClassName(IntPtr hWnd)
        {
            try
            {
                var className = new char[256];
                var length = User32Methods.GetClassName(hWnd, className, className.Length);
                return length > 0 ? new string(className, 0, length) : "Unknown";
            }
            catch
            {
                return "Error";
            }
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
