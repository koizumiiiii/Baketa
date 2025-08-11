using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Models.Capture;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Exceptions.Capture;
using Baketa.Infrastructure.Platform.Windows;
using System;

namespace Baketa.Infrastructure.Platform.Windows.Capture.Strategies;

/// <summary>
/// 統合GPU向け直接フルスクリーンキャプチャ戦略
/// </summary>
public class DirectFullScreenCaptureStrategy : ICaptureStrategy
{
    private readonly ILogger<DirectFullScreenCaptureStrategy> _logger;
    private readonly IWindowsCapturer _windowsCapturer;

    public string StrategyName => "DirectFullScreen";
    public int Priority => 100; // 最高優先度（統合GPUでは最も効率的）

    public DirectFullScreenCaptureStrategy(
        ILogger<DirectFullScreenCaptureStrategy> logger,
        IWindowsCapturer windowsCapturer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _windowsCapturer = windowsCapturer ?? throw new ArgumentNullException(nameof(windowsCapturer));
    }

    public bool CanApply(GPUEnvironmentInfo environment, IntPtr hwnd)
    {
        try
        {
            // 統合GPUかつ十分なテクスチャサイズサポートの場合に適用
            var canApply = environment.IsIntegratedGPU && 
                          environment.HasDirectX11Support &&
                          environment.MaximumTexture2DDimension >= 4096;

            _logger.LogDebug("DirectFullScreen戦略適用可能性: {CanApply} (統合GPU: {IsIntegrated}, DX11: {HasDx11}, MaxTexture: {MaxTexture})", 
                canApply, environment.IsIntegratedGPU, environment.HasDirectX11Support, environment.MaximumTexture2DDimension);

            return canApply;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DirectFullScreen戦略適用可能性チェック中にエラー");
            return false;
        }
    }

    public async Task<bool> ValidatePrerequisitesAsync(IntPtr hwnd)
    {
        try
        {
            // ウィンドウハンドルが有効かチェック
            if (hwnd == IntPtr.Zero)
            {
                _logger.LogDebug("無効なウィンドウハンドル");
                return false;
            }

            // 非同期的にウィンドウ検証を実行
            return await Task.Run(() =>
            {
                // ウィンドウが実際に存在し、キャプチャ可能かチェック
                var windowExists = IsWindow(hwnd);
                var isVisible = IsWindowVisible(hwnd);

                _logger.LogDebug("DirectFullScreen前提条件: Window存在={WindowExists}, 可視={IsVisible}", 
                    windowExists, isVisible);

                return windowExists && isVisible;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DirectFullScreen前提条件チェック中にエラー");
            return false;
        }
    }

    public async Task<CaptureStrategyResult> ExecuteCaptureAsync(IntPtr hwnd, CaptureOptions options)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = new CaptureStrategyResult
        {
            StrategyName = StrategyName,
            Metrics = new CaptureMetrics()
        };

        try
        {
            _logger.LogDebug("DirectFullScreenキャプチャ開始");

            // Windows Graphics Capture APIで直接キャプチャ
            var capturedImage = await CaptureDirectFullScreenAsync(hwnd, options).ConfigureAwait(false);
            
            if (capturedImage != null)
            {
                result.Success = true;
                result.Images = [capturedImage];
                result.Metrics.ActualCaptureTime = stopwatch.Elapsed;
                result.Metrics.FrameCount = 1;
                result.Metrics.PerformanceCategory = "HighPerformance";

                _logger.LogInformation("DirectFullScreenキャプチャ成功: サイズ={Width}x{Height}, 処理時間={ProcessingTime}ms", 
                    capturedImage.Width, capturedImage.Height, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = "キャプチャイメージの取得に失敗";
                _logger.LogWarning("DirectFullScreenキャプチャ失敗: イメージが null");
            }
        }
        catch (TDRException ex)
        {
            _logger.LogWarning(ex, "DirectFullScreen キャプチャでTDRを検出");
            result.Success = false;
            result.ErrorMessage = $"GPU タイムアウト: {ex.Message}";
            throw; // TDR例外は上位層で特別に処理する必要がある
        }
        catch (GPUConstraintException ex)
        {
            _logger.LogWarning(ex, "DirectFullScreen キャプチャでGPU制約を検出");
            result.Success = false;
            result.ErrorMessage = $"GPU制約: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DirectFullScreenキャプチャ中にエラー");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            result.CompletionTime = DateTime.Now;
            result.Metrics.TotalProcessingTime = stopwatch.Elapsed;
            stopwatch.Stop();
        }

        return result;
    }

    private async Task<IWindowsImage?> CaptureDirectFullScreenAsync(IntPtr hwnd, CaptureOptions options)
    {
        try
        {
            // 🔍🔍🔍 デバッグ: キャプチャータイプ確認
            try
            {
                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🌯 DirectFullScreen: _windowsCapturerタイプ={_windowsCapturer.GetType().FullName}{Environment.NewLine}");
            }
            catch { /* デバッグログ失敗は無視 */ }
            
            // Windows Graphics Capture APIに最適化されたキャプチャオプションを設定
            var captureOptions = new WindowsCaptureOptions
            {
                Quality = 100, // 高品質
                IncludeCursor = false, // カーソルは通常含まない
                UseDwmCapture = true // DWMキャプチャを有効化
            };

            if (_windowsCapturer is WindowsGraphicsCapturer graphicsCapturer)
            {
                // Windows Graphics Capturerの場合は専用設定を適用
                graphicsCapturer.SetCaptureOptions(captureOptions);
                
                // 初期化が必要な場合は実行
                if (!graphicsCapturer.IsInitialized)
                {
                    var initSuccess = await graphicsCapturer.InitializeAsync().ConfigureAwait(false);
                    if (!initSuccess)
                    {
                        _logger.LogError("Windows Graphics Captureの初期化に失敗");
                        return null;
                    }
                }
            }

            // DirectFullScreen戦略に最適化されたキャプチャ実行
            var capturedImage = await ExecuteOptimizedCaptureAsync(hwnd, options).ConfigureAwait(false);
            
            if (capturedImage != null)
            {
                _logger.LogDebug("DirectFullScreen最適化キャプチャ成功: {Width}x{Height}",
                    capturedImage.Width, capturedImage.Height);
                return capturedImage;
            }
            else
            {
                _logger.LogWarning("DirectFullScreen最適化キャプチャ失敗: 結果がnull");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "直接フルスクリーンキャプチャ中にエラー");
            
            // エラーメッセージからTDRを検出
            if (IsErrorIndicatingTDR(ex.Message))
            {
                throw new TDRException(unchecked((int)0x887A0005)); // DXGI_ERROR_DEVICE_REMOVED
            }
            
            // GPU制約エラーの検出
            if (IsErrorIndicatingGPUConstraint(ex.Message))
            {
                throw new GPUConstraintException(4096, 2048); // 仮の数値：要求サイズ vs 最大サイズ
            }
            
            throw new CaptureStrategyException(StrategyName, "直接キャプチャに失敗しました", ex);
        }
    }

    /// <summary>
    /// DirectFullScreen戦略に最適化されたキャプチャ実行
    /// </summary>
    private async Task<IWindowsImage?> ExecuteOptimizedCaptureAsync(IntPtr hwnd, CaptureOptions options)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            // ウィンドウがフルスクリーンかチェック
            // 🔍🔍🔍 デバッグ: ウィンドウ情報
            try
            {
                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                if (GetWindowRect(hwnd, out var rect))
                {
                    var width = rect.Right - rect.Left;
                    var height = rect.Bottom - rect.Top;
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🪟 ウィンドウ情報: HWND=0x{hwnd.ToInt64():X8}, サイズ={width}x{height}, 位置=({rect.Left},{rect.Top}){Environment.NewLine}");
                }
            }
            catch { /* デバッグログ失敗は無視 */ }
            
            if (IsFullScreenWindow(hwnd))
            {
                _logger.LogDebug("フルスクリーンウィンドウを検出、画面全体キャプチャを実行");
                
                // 🔍🔍🔍 デバッグ: フルスクリーン判定でもウィンドウキャプチャを使う
                try
                {
                    var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎬 DirectFullScreen: フルスクリーン検出だがウィンドウキャプチャを実行 HWND=0x{hwnd.ToInt64():X8}{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }
                
                // FIXME: 一時的にフルスクリーンでもウィンドウキャプチャを使用
                var windowCapture = await _windowsCapturer.CaptureWindowAsync(hwnd).ConfigureAwait(false);
                
                _logger.LogDebug("フルスクリーンウィンドウキャプチャ完了: 処理時間={ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
                return windowCapture;
            }
            else
            {
                _logger.LogDebug("通常ウィンドウを検出、ウィンドウキャプチャを実行");
                
                // 🔍🔍🔍 デバッグ: キャプチャ前ログ
                try
                {
                    var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎬 DirectFullScreen: ウィンドウキャプチャ開始 HWND=0x{hwnd.ToInt64():X8}, Capturer={_windowsCapturer.GetType().Name}{Environment.NewLine}");
                    
                    // WindowsGraphicsCapturerの詳細ステータス
                    if (_windowsCapturer is WindowsGraphicsCapturer wgc)
                    {
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 WindowsGraphicsCapturer: IsInitialized={wgc.IsInitialized}, IsSupported={wgc.IsSupported}{Environment.NewLine}");
                    }
                }
                catch { /* デバッグログ失敗は無視 */ }
                
                // 通常ウィンドウの場合はウィンドウキャプチャ
                IWindowsImage? windowCapture = null;
                try
                {
                    windowCapture = await _windowsCapturer.CaptureWindowAsync(hwnd).ConfigureAwait(false);
                }
                catch (Exception captureEx)
                {
                    // 🔍🔍🔍 デバッグ: キャプチャ例外
                    try
                    {
                        var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 DirectFullScreen: CaptureWindowAsync例外: {captureEx.GetType().Name}: {captureEx.Message}{Environment.NewLine}");
                    }
                    catch { /* デバッグログ失敗は無視 */ }
                    throw;
                }
                
                // 🔍🔍🔍 デバッグ: キャプチャ後ログ
                try
                {
                    var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                    System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎬 DirectFullScreen: キャプチャ完了 {(windowCapture != null ? $"{windowCapture.Width}x{windowCapture.Height}" : "null")}{Environment.NewLine}");
                }
                catch { /* デバッグログ失敗は無視 */ }
                
                _logger.LogDebug("ウィンドウキャプチャ完了: 処理時間={ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
                return windowCapture;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "最適化キャプチャ実行中にエラー: 処理時間={ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    /// <summary>
    /// ウィンドウがフルスクリーンモードかチェック
    /// </summary>
    private bool IsFullScreenWindow(IntPtr hwnd)
    {
        try
        {
            // ウィンドウの矩形を取得
            if (!GetWindowRect(hwnd, out var windowRect))
            {
                return false;
            }

            // プライマリモニタの解像度を取得
            var screenWidth = GetSystemMetrics(SM_CXSCREEN);
            var screenHeight = GetSystemMetrics(SM_CYSCREEN);

            // ウィンドウの大きさが画面全体と一致するかチェック
            var windowWidth = windowRect.Right - windowRect.Left;
            var windowHeight = windowRect.Bottom - windowRect.Top;

            var isFullScreen = windowWidth >= screenWidth && windowHeight >= screenHeight;
            
            _logger.LogDebug("フルスクリーン判定: Window={WindowW}x{WindowH}, Screen={ScreenW}x{ScreenH}, IsFullScreen={IsFullScreen}",
                windowWidth, windowHeight, screenWidth, screenHeight, isFullScreen);

            return isFullScreen;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "フルスクリーン判定中にエラー");
            return false;
        }
    }

    /// <summary>
    /// TDRエラーを示すメッセージかチェック
    /// </summary>
    private bool IsErrorIndicatingTDR(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return false;

        // TDRを示すエラーメッセージパターン
        var tdrPatterns = new[]
        {
            "0x887A0005", // DXGI_ERROR_DEVICE_REMOVED
            "0x887A0006", // DXGI_ERROR_DEVICE_HUNG
            "DXGI_ERROR_DEVICE_REMOVED",
            "DXGI_ERROR_DEVICE_HUNG",
            "GPU timeout",
            "device removed",
            "display driver stopped responding"
        };

        return tdrPatterns.Any(pattern => 
            errorMessage.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// GPU制約エラーを示すメッセージかチェック
    /// </summary>
    private bool IsErrorIndicatingGPUConstraint(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return false;

        // GPU制約を示すエラーメッセージパターン
        var constraintPatterns = new[]
        {
            "insufficient memory",
            "out of memory",
            "memory allocation failed",
            "texture too large",
            "resource limit",
            "integrated GPU constraint"
        };

        return constraintPatterns.Any(pattern => 
            errorMessage.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    // Windows API P/Invoke
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0; // プライマリモニタの幅
    private const int SM_CYSCREEN = 1; // プライマリモニタの高さ

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}