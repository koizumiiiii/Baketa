using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Models.Capture;
using Baketa.Core.Exceptions.Capture;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Abstractions.GPU;
// 🔥 [PHASE_K-29-G] CaptureOptions統合: Baketa.Core.Abstractions.Servicesから取得
using CaptureOptions = Baketa.Core.Abstractions.Services.CaptureOptions;

namespace Baketa.Infrastructure.Platform.Windows.Capture.Strategies;

/// <summary>
/// PrintWindow API を使用した確実動作フォールバック戦略
/// </summary>
public class PrintWindowFallbackStrategy : ICaptureStrategy
{
    private readonly ILogger<PrintWindowFallbackStrategy> _logger;
    private readonly IWindowsCapturer _windowsCapturer;

    public string StrategyName => "PrintWindowFallback";
    public int Priority => 75; // 🔧 Phase 0 WGC修復: WGC問題対応で高優先度（確実な代替手段）

    public PrintWindowFallbackStrategy(
        ILogger<PrintWindowFallbackStrategy> logger,
        IWindowsCapturer windowsCapturer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _windowsCapturer = windowsCapturer ?? throw new ArgumentNullException(nameof(windowsCapturer));
    }

    public bool CanApply(GpuEnvironmentInfo environment, IntPtr hwnd)
    {
        try
        {
            // PrintWindow API は常に利用可能（最終手段）
            var canApply = hwnd != IntPtr.Zero;

            _logger.LogDebug("PrintWindowFallback戦略適用可能性: {CanApply} (HWND: 0x{Hwnd:X})", 
                canApply, hwnd.ToInt64());

            return canApply;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PrintWindowFallback戦略適用可能性チェック中にエラー");
            return true; // フォールバック戦略は常に適用可能とする
        }
    }

    public async Task<bool> ValidatePrerequisitesAsync(IntPtr hwnd)
    {
        try
        {
            // 基本的なウィンドウ検証
            if (hwnd == IntPtr.Zero)
            {
                _logger.LogDebug("無効なウィンドウハンドル");
                return false;
            }

            // 非同期的にウィンドウ検証を実行
            return await Task.Run(() =>
            {
                var windowExists = IsWindow(hwnd);
                
                _logger.LogDebug("PrintWindowFallback前提条件: Window存在={WindowExists}", windowExists);

                return windowExists;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PrintWindowFallback前提条件チェック中にエラー");
            return true; // フォールバック戦略は寛容に動作
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
            _logger.LogDebug("PrintWindowFallbackキャプチャ開始");

            // PrintWindow API を使用した確実なキャプチャ
            var capturedImage = await CaptureWithPrintWindowAsync(hwnd, options).ConfigureAwait(false);
            
            if (capturedImage != null)
            {
                result.Success = true;
                result.Images = [capturedImage];
                result.Metrics.ActualCaptureTime = stopwatch.Elapsed;
                result.Metrics.FrameCount = 1;
                result.Metrics.PerformanceCategory = "Reliable";

                _logger.LogInformation("PrintWindowFallbackキャプチャ成功: サイズ={Width}x{Height}, 処理時間={ProcessingTime}ms", 
                    capturedImage.Width, capturedImage.Height, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = "PrintWindowキャプチャの取得に失敗";
                _logger.LogWarning("PrintWindowFallbackキャプチャ失敗: イメージが null");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PrintWindowFallbackキャプチャ中にエラー");
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

    private async Task<IWindowsImage?> CaptureWithPrintWindowAsync(IntPtr hwnd, CaptureOptions options)
    {
        try
        {
            _logger.LogDebug("PrintWindow API でキャプチャ実行中");

            // 既存のIWindowsCapturerを使用
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(options.TDRTimeoutMs));
            var capturedImage = await _windowsCapturer.CaptureWindowAsync(hwnd).ConfigureAwait(false);

            if (capturedImage != null)
            {
                _logger.LogDebug("PrintWindowキャプチャ成功: {Width}x{Height}",
                    capturedImage.Width, capturedImage.Height);
                return capturedImage;
            }
            else
            {
                _logger.LogWarning("PrintWindowキャプチャ失敗: 結果がnull");
                return null;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("PrintWindowキャプチャがタイムアウト: {TimeoutMs}ms", options.TDRTimeoutMs);
            throw new CaptureStrategyException(StrategyName, $"キャプチャがタイムアウトしました ({options.TDRTimeoutMs}ms)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PrintWindowキャプチャ中にエラー");
            throw new CaptureStrategyException(StrategyName, "PrintWindowキャプチャに失敗しました", ex);
        }
    }

    // Windows API P/Invoke
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);
}