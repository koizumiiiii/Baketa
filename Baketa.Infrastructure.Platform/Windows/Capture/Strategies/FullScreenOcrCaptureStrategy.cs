using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Events.Diagnostics;
using Baketa.Core.Models.Capture;
using Microsoft.Extensions.Logging;
// 🔥 [PHASE2] CaptureOptions型エイリアス
using CaptureOptions = Baketa.Core.Abstractions.Services.CaptureOptions;

namespace Baketa.Infrastructure.Platform.Windows.Capture.Strategies;

/// <summary>
/// 🔥 [PHASE2] 全画面キャプチャ戦略
///
/// 🔧 [Issue #230] OCR実行はSmartProcessingPipelineServiceに移譲
/// 画面変化検知の後にOCRを実行することで、変化がない場合のOCR処理（7-8秒）を省略
///
/// 処理フロー:
/// 1. 全画面キャプチャ (1回のみ) - NativeWindowsCaptureWrapper使用
/// 2. 画像を返す（OCRはパイプラインで実行）
///
/// ⚡ 期待される効果:
/// - 画面変化なし時: OCRスキップで7-8秒の処理時間を節約
/// - 画面変化あり時: パイプラインでOCRを実行（従来通り）
/// </summary>
public class FullScreenOcrCaptureStrategy : ICaptureStrategy
{
    private readonly ILogger<FullScreenOcrCaptureStrategy> _logger;
    private readonly IOcrEngine _ocrEngine;
    private readonly IWindowsCapturer _windowsCapturer;
    private readonly IEventAggregator _eventAggregator;

    public string StrategyName => "FullScreenOcr";

    // 🔥 [PHASE2] 最優先戦略（ROI代替・60-80%高速化）
    // 全画面キャプチャ + PaddleOCR統合実行で二重OCR処理を廃止
    public int Priority => 30;

    public FullScreenOcrCaptureStrategy(
        ILogger<FullScreenOcrCaptureStrategy> logger,
        IOcrEngine ocrEngine,
        IWindowsCapturer windowsCapturer,
        IEventAggregator eventAggregator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
        _windowsCapturer = windowsCapturer ?? throw new ArgumentNullException(nameof(windowsCapturer));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));

        _logger.LogInformation("🔧 [Issue #230] FullScreenCaptureStrategy initialized - OCR deferred to pipeline");
    }

    public bool CanApply(GpuEnvironmentInfo environment, IntPtr hwnd)
    {
        try
        {
            // 🔥 [PHASE2] フォールバック戦略 - 常に適用可能
            // ROIBasedCaptureStrategyが適用不可の場合に使用
            _logger.LogInformation("🔥 [PHASE2] FullScreenOcr strategy - Always applicable (fallback strategy)");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FullScreenOcr strategy applicability check error");
            return false;
        }
    }

    public async Task<bool> ValidatePrerequisitesAsync(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero)
                return false;

            // OCRエンジン初期化状態確認
            if (!_ocrEngine.IsInitialized)
            {
                _logger.LogWarning("OCR engine not initialized");
                return false;
            }

            // 🔥 [PHASE2] IWindowsCapturerはDI登録済みのため、初期化状態確認は不要
            // NativeWindowsCaptureWrapperとは異なり、IWindowsCapturerインターフェースは
            // DIコンテナによって管理されており、常に使用可能な状態で提供される

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FullScreenOcr prerequisites validation error");
            return false;
        }
    }

    public async Task<CaptureStrategyResult> ExecuteCaptureAsync(IntPtr hwnd, CaptureOptions options)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var totalStopwatch = Stopwatch.StartNew();
        var result = new CaptureStrategyResult
        {
            StrategyName = StrategyName,
            Metrics = new CaptureMetrics()
        };

        try
        {
            _logger.LogInformation("🔥 [PHASE2] FullScreenOcr capture started - Window: 0x{Hwnd:X}, Session: {SessionId}",
                hwnd.ToInt64(), sessionId);

            // 📊 [DIAGNOSTIC] キャプチャ開始イベント
            await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "FullScreenOcr_Start",
                IsSuccess = true,
                ProcessingTimeMs = 0,
                SessionId = sessionId,
                Severity = DiagnosticSeverity.Information,
                Message = $"FullScreenOcr capture started - Window: 0x{hwnd.ToInt64():X}",
                Metrics = new Dictionary<string, object>
                {
                    { "Strategy", StrategyName },
                    { "OcrEngine", _ocrEngine.EngineName },
                    { "OcrEngineVersion", _ocrEngine.EngineVersion }
                }
            }).ConfigureAwait(false);

            // 🔥 [PHASE2_STEP1] 全画面キャプチャ (1回のみ)
            var phase1Stopwatch = Stopwatch.StartNew();
            var fullImage = await CaptureFullScreenAsync(hwnd).ConfigureAwait(false);
            phase1Stopwatch.Stop();

            if (fullImage == null)
            {
                result.Success = false;
                result.ErrorMessage = "Full-screen capture failed";
                _logger.LogError("🔥 [PHASE2_STEP1] Full-screen capture failed");
                return result;
            }

            // 🚀 [Issue #193] ネイティブAPIから取得した実際のキャプチャサイズを使用
            // DWMWA_EXTENDED_FRAME_BOUNDSではなく、WGCが実際にキャプチャした元サイズを使用
            var originalWindowSize = new Size(fullImage.OriginalWidth, fullImage.OriginalHeight);
            _logger.LogInformation("🚀 [Issue #193] 元キャプチャサイズ取得: {OriginalWidth}x{OriginalHeight} (リサイズ後: {Width}x{Height})",
                originalWindowSize.Width, originalWindowSize.Height, fullImage.Width, fullImage.Height);
            Console.WriteLine($"🚀 [Issue #193 DEBUG] 元キャプチャサイズ: {originalWindowSize.Width}x{originalWindowSize.Height} (リサイズ後: {fullImage.Width}x{fullImage.Height})");

            _logger.LogInformation("🔥 [PHASE2_STEP1] Full-screen capture completed - Size: {Width}x{Height}, Time: {ElapsedMs}ms",
                fullImage.Width, fullImage.Height, phase1Stopwatch.ElapsedMilliseconds);

            // 📊 [DIAGNOSTIC] キャプチャ完了イベント
            await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "FullScreenOcr_Capture",
                IsSuccess = true,
                ProcessingTimeMs = phase1Stopwatch.ElapsedMilliseconds,
                SessionId = sessionId,
                Severity = DiagnosticSeverity.Information,
                Metrics = new Dictionary<string, object>
                {
                    { "ImageWidth", fullImage.Width },
                    { "ImageHeight", fullImage.Height },
                    { "CaptureTimeMs", phase1Stopwatch.ElapsedMilliseconds }
                }
            }).ConfigureAwait(false);

            // 🔧 [Issue #230] OCR実行を削除 - 画面変化検知後に実行するように変更
            // OCRはSmartProcessingPipelineServiceのOcrExecutionStageStrategyで実行される
            // これにより、画面変化がない場合はOCRをスキップして7-8秒の処理時間を節約できる
            //
            // 変更前のフロー: Capture+OCR → ImageChangeDetection → 早期リターン（OCR無駄）
            // 変更後のフロー: Capture → ImageChangeDetection → 変化あり時のみOCR → Translation

            result.Success = true;
            result.Images = [fullImage]; // 全画面画像1つのみ

            // PreExecutedOcrResultは設定しない（パイプラインでOCRを実行させる）
            result.PreExecutedOcrResult = null;
            result.TextRegions = [];

            _logger.LogInformation("🔧 [Issue #230] キャプチャ完了 - OCRはパイプラインで実行 (Size: {Width}x{Height})",
                fullImage.Width, fullImage.Height);

            result.Metrics.ActualCaptureTime = totalStopwatch.Elapsed;
            result.Metrics.FrameCount = 1;
            result.Metrics.PerformanceCategory = "Fast";

            totalStopwatch.Stop();

            _logger.LogInformation("🔧 [Issue #230] FullScreenCapture完了 - Size: {Width}x{Height}, Total time: {TotalMs}ms (Capture: {CaptureMs}ms)",
                fullImage.Width, fullImage.Height,
                totalStopwatch.ElapsedMilliseconds,
                phase1Stopwatch.ElapsedMilliseconds);

            // 📊 [DIAGNOSTIC] 完了イベント
            await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "FullScreenCapture_Complete",
                IsSuccess = result.Success,
                ProcessingTimeMs = totalStopwatch.ElapsedMilliseconds,
                SessionId = sessionId,
                Severity = DiagnosticSeverity.Information,
                Message = $"FullScreenCapture completed - {fullImage.Width}x{fullImage.Height}, OCR deferred to pipeline",
                Metrics = new Dictionary<string, object>
                {
                    { "ImageWidth", fullImage.Width },
                    { "ImageHeight", fullImage.Height },
                    { "Phase1_CaptureMs", phase1Stopwatch.ElapsedMilliseconds },
                    { "TotalTimeMs", totalStopwatch.ElapsedMilliseconds },
                    { "OcrDeferred", true }
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [PHASE2] FullScreenOcr capture error");

            // 📊 [DIAGNOSTIC] エラーイベント
            await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "FullScreenOcr_Error",
                IsSuccess = false,
                ProcessingTimeMs = totalStopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message,
                SessionId = sessionId,
                Severity = DiagnosticSeverity.Error,
                Message = $"FullScreenOcr error: {ex.GetType().Name}: {ex.Message}",
                Metrics = new Dictionary<string, object>
                {
                    { "ErrorType", ex.GetType().Name },
                    { "StackTrace", ex.StackTrace ?? "N/A" }
                }
            }).ConfigureAwait(false);

            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            result.CompletionTime = DateTime.Now;
            result.Metrics.TotalProcessingTime = totalStopwatch.Elapsed;
            totalStopwatch.Stop();
        }

        return result;
    }

    // 🚀 [Issue #193] OCR処理に適したGPUリサイズターゲットサイズ
    // GPU→CPU転送量を削減するため、OCR処理に十分な解像度にリサイズ
    private const int OcrTargetWidth = 1280;
    private const int OcrTargetHeight = 720;

    private async Task<IWindowsImage?> CaptureFullScreenAsync(IntPtr hwnd)
    {
        try
        {
            // 🚀 [Issue #193] GPUリサイズキャプチャを優先使用
            // GPU上でリサイズしてからCPUに転送することで、転送量を削減
            _logger.LogDebug("🚀 [Issue #193] GPUリサイズキャプチャ試行: Target={Width}x{Height}",
                OcrTargetWidth, OcrTargetHeight);

            var fullImage = await _windowsCapturer.CaptureWindowResizedAsync(
                hwnd, OcrTargetWidth, OcrTargetHeight).ConfigureAwait(false);

            if (fullImage == null)
            {
                _logger.LogWarning("🚀 [Issue #193] GPUリサイズキャプチャ失敗 - null返却");
                return null;
            }

            _logger.LogInformation("🚀 [Issue #193] GPUリサイズキャプチャ成功 - Size: {Width}x{Height} (Target: {TargetWidth}x{TargetHeight})",
                fullImage.Width, fullImage.Height, OcrTargetWidth, OcrTargetHeight);

            return fullImage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚀 [Issue #193] GPUリサイズキャプチャエラー");
            throw new InvalidOperationException("Full-screen capture failed", ex);
        }
    }

    #region P/Invoke Win32 API - DPI Awareness対応

    // 🚀 [Issue #193] DWM API - 物理ピクセルサイズ取得用（DPI対応）
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT lpRect, int cbAttribute);

    // DWMWA_EXTENDED_FRAME_BOUNDS はウィンドウの物理的な境界を取得
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    // 🚀 [Issue #193] GetWindowRect - フォールバック用（SetLastError追加）
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // 🚀 [Issue #193] DPI取得用API (Windows 10 1607+)
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    private const uint USER_DEFAULT_SCREEN_DPI = 96;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    #endregion

    /// <summary>
    /// 🚀 [Issue #193] ウィンドウの物理ピクセルサイズを取得（DPI Awareness対応）
    /// DwmGetWindowAttributeを優先使用し、失敗時はGetWindowRect+DPIスケール補正
    /// </summary>
    private Size GetOriginalWindowSize(IntPtr hwnd)
    {
        // 方法1: DwmGetWindowAttribute を使用して物理サイズを取得 (推奨)
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT rect, Marshal.SizeOf(typeof(RECT))) == 0) // S_OK
        {
            var size = new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
            _logger.LogDebug("🚀 [Issue #193] DwmGetWindowAttribute成功: {Width}x{Height}", size.Width, size.Height);
            return size;
        }

        _logger.LogDebug("🚀 [Issue #193] DwmGetWindowAttribute失敗、GetWindowRect+DPIスケールにフォールバック");

        // 方法2: フォールバックとして GetWindowRect と DPI スケールで計算
        if (GetWindowRect(hwnd, out rect))
        {
            try
            {
                uint dpi = GetDpiForWindow(hwnd);
                double scaleFactor = dpi / (double)USER_DEFAULT_SCREEN_DPI;
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                var scaledSize = new Size((int)(width * scaleFactor), (int)(height * scaleFactor));
                _logger.LogDebug("🚀 [Issue #193] GetWindowRect+DPIスケール: {Width}x{Height} (DPI={Dpi}, Scale={Scale:F2})",
                    scaledSize.Width, scaledSize.Height, dpi, scaleFactor);
                return scaledSize;
            }
            catch (EntryPointNotFoundException)
            {
                // GetDpiForWindow が存在しない古いOSの場合
                var size = new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
                _logger.LogWarning("🚀 [Issue #193] GetDpiForWindow未サポート、DPIスケールなしで使用: {Width}x{Height}",
                    size.Width, size.Height);
                return size;
            }
        }

        // 最終的なフォールバック
        _logger.LogWarning("🚀 [Issue #193] ウィンドウサイズ取得失敗 (hwnd=0x{Hwnd:X})、OCRターゲットサイズを使用",
            hwnd.ToInt64());
        return new Size(OcrTargetWidth, OcrTargetHeight);
    }

    /// <summary>
    /// 🚀 [Issue #193] OCR座標を元ウィンドウサイズにスケーリング
    /// リサイズ画像での座標 → 元ウィンドウ座標に変換
    /// </summary>
    private Rectangle ScaleCoordinates(Rectangle bounds, Size originalSize, Size capturedSize)
    {
        // サイズが同じ場合は計算をスキップ（パフォーマンス最適化）
        if (originalSize == capturedSize)
            return bounds;

        if (capturedSize.Width <= 0 || capturedSize.Height <= 0)
            return bounds;

        double scaleX = (double)originalSize.Width / capturedSize.Width;
        double scaleY = (double)originalSize.Height / capturedSize.Height;

        return new Rectangle(
            (int)(bounds.X * scaleX),
            (int)(bounds.Y * scaleY),
            (int)(bounds.Width * scaleX),
            (int)(bounds.Height * scaleY)
        );
    }
}

/// <summary>
/// 🔥 [PHASE2] IWindowsImage → IImage 変換アダプター
/// PaddleOCR (IOcrEngine.RecognizeAsync) がIImageを期待するため、
/// IWindowsImageをIImageインターフェースでラップする
/// </summary>
internal sealed class WindowsImageToIImageAdapter : Baketa.Core.Abstractions.Imaging.IImage
{
    private readonly IWindowsImage _windowsImage;
    private bool _disposed;

    public WindowsImageToIImageAdapter(IWindowsImage windowsImage)
    {
        _windowsImage = windowsImage ?? throw new ArgumentNullException(nameof(windowsImage));
    }

    // IImageBase メンバー
    public int Width => _windowsImage.Width;
    public int Height => _windowsImage.Height;
    public Baketa.Core.Abstractions.Imaging.ImageFormat Format => Baketa.Core.Abstractions.Imaging.ImageFormat.Png;

    public Task<byte[]> ToByteArrayAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _windowsImage.ToByteArrayAsync(format: null, CancellationToken.None);
    }

    // IImage メンバー
    public Baketa.Core.Abstractions.Memory.ImagePixelFormat PixelFormat => Baketa.Core.Abstractions.Memory.ImagePixelFormat.Bgra32;

    public ReadOnlyMemory<byte> GetImageMemory()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // IWindowsImageにはGetImageMemoryメソッドがないため、ToByteArrayAsync()を同期的に実行
        return new ReadOnlyMemory<byte>(_windowsImage.ToByteArrayAsync().GetAwaiter().GetResult());
    }

    public Baketa.Core.Abstractions.Imaging.PixelDataLock LockPixelData()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // 🔥 [PHASE2.1.4] ref structであるPixelDataLockはラムダ式でキャプチャ不可
        // IWindowsImageのLockPixelDataを呼び出し、データとStrideのみを取得
        var windowsLock = _windowsImage.LockPixelData();
        // IImageのPixelDataLockに変換 - unlockアクションは空（Adapter Disposeで管理）
        return new Baketa.Core.Abstractions.Imaging.PixelDataLock(
            windowsLock.Data,
            windowsLock.Stride,
            () => { /* windowsLock解放は_windowsImage.Disposeで自動処理される */ }
        );
    }

    public Baketa.Core.Abstractions.Imaging.IImage Clone()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        throw new NotSupportedException("WindowsImageToIImageAdapter does not support Clone operation");
    }

    public async Task<Baketa.Core.Abstractions.Imaging.IImage> ResizeAsync(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var resizedWindowsImage = await _windowsImage.ResizeAsync(width, height).ConfigureAwait(false);
        return new WindowsImageToIImageAdapter(resizedWindowsImage);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // 🔥 [PHASE2] IWindowsImageの所有権はFullScreenOcrCaptureStrategyにあるため、
        // このアダプターではDisposeしない（二重Disposeを防止）
    }
}
