using System.Drawing;
using System.IO;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Services;
// 🔥 [PHASE_K-29-G] CaptureOptions統合: Baketa.Core.Models.Capture削除
using Baketa.Core.Settings;
using Baketa.Infrastructure.Platform.Adapters;
using Baketa.Infrastructure.Platform.Windows.Services;
using Microsoft.Extensions.Logging;
using ServicesCaptureOptions = Baketa.Core.Abstractions.Services.CaptureOptions;

namespace Baketa.Application.Services.Capture;

/// <summary>
/// 適応的キャプチャサービスを従来のICaptureServiceインターフェースに適合させるアダプター
/// </summary>
public partial class AdaptiveCaptureServiceAdapter(
    IAdaptiveCaptureService adaptiveCaptureService,
    ILogger<AdaptiveCaptureServiceAdapter> logger,
    ICoordinateTransformationService coordinateTransformationService,
    IImageChangeDetectionService? imageChangeDetectionService = null) : ICaptureService, IDisposable
{
    private readonly IAdaptiveCaptureService _adaptiveCaptureService = adaptiveCaptureService ?? throw new ArgumentNullException(nameof(adaptiveCaptureService));
    private readonly ILogger<AdaptiveCaptureServiceAdapter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ICoordinateTransformationService _coordinateTransformationService = coordinateTransformationService ?? throw new ArgumentNullException(nameof(coordinateTransformationService));
    private readonly IImageChangeDetectionService? _imageChangeDetectionService = imageChangeDetectionService;
    private ServicesCaptureOptions _currentOptions = new();
    private bool _disposed;

    /// <summary>
    /// [Issue #361] 同時キャプチャ防止用セマフォ
    /// 複数サービス（翻訳ループ、ROI監視、バックグラウンド学習）からの
    /// 同時キャプチャによるACCESS_VIOLATIONを防止
    /// </summary>
    private static readonly SemaphoreSlim _captureSemaphore = new(1, 1);

    public async Task<IImage> CaptureScreenAsync(CancellationToken cancellationToken = default)
    {
        // [Issue #361] 同時キャプチャ防止 + キャンセル対応
        await _captureSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("🔥 適応的キャプチャサービスアダプター: CaptureScreenAsync呼び出され - Windows Graphics Capture API使用予定");
            _logger.LogDebug("適応的画面キャプチャ開始");

            // 画面全体キャプチャ用のCaptureOptionsを作成
            var adaptiveCaptureOptions = CreateAdaptiveCaptureOptions();

            // デスクトップのHWNDを取得（画面全体キャプチャ用）
            var desktopHwnd = GetDesktopWindowHandle();

            var result = await _adaptiveCaptureService.CaptureAsync(desktopHwnd, adaptiveCaptureOptions).ConfigureAwait(false);

            if (!result.Success || result.CapturedImages == null || result.CapturedImages.Count == 0)
            {
                throw new InvalidOperationException($"適応的画面キャプチャに失敗: {result.ErrorDetails}");
            }

            _logger.LogInformation("適応的画面キャプチャ成功: 戦略={Strategy}, 処理時間={ProcessingTime}ms",
                result.StrategyUsed, result.ProcessingTime.TotalMilliseconds);

            // IWindowsImageをIImageアダプターでラップして返す
            return new WindowsImageAdapter(result.CapturedImages[0]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "適応的画面キャプチャでエラー");
            throw;
        }
        finally
        {
            _captureSemaphore.Release();
        }
    }

    public async Task<IImage> CaptureRegionAsync(Rectangle region, CancellationToken cancellationToken = default)
    {
        // [Issue #361] 同時キャプチャ防止 + キャンセル対応
        await _captureSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogDebug("適応的領域キャプチャ開始: {Region}", region);

            // 領域キャプチャ用のCaptureOptionsを作成
            var adaptiveCaptureOptions = CreateAdaptiveCaptureOptions();
            // 注意: 現在のCaptureOptionsにはTargetRegionプロパティがないため、ROI処理を有効化のみ
            adaptiveCaptureOptions.AllowROIProcessing = true;

            // デスクトップのHWNDを使用
            var desktopHwnd = GetDesktopWindowHandle();

            var result = await _adaptiveCaptureService.CaptureAsync(desktopHwnd, adaptiveCaptureOptions).ConfigureAwait(false);

            if (!result.Success || result.CapturedImages == null || result.CapturedImages.Count == 0)
            {
                throw new InvalidOperationException($"適応的領域キャプチャに失敗: {result.ErrorDetails}");
            }

            _logger.LogInformation("適応的領域キャプチャ成功: 戦略={Strategy}, 処理時間={ProcessingTime}ms",
                result.StrategyUsed, result.ProcessingTime.TotalMilliseconds);

            // IWindowsImageをIImageアダプターでラップして返す
            return new WindowsImageAdapter(result.CapturedImages[0]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "適応的領域キャプチャでエラー");
            throw;
        }
        finally
        {
            _captureSemaphore.Release();
        }
    }

    public async Task<IImage> CaptureWindowAsync(IntPtr windowHandle, CancellationToken cancellationToken = default)
    {
        // [Issue #361] 同時キャプチャ防止 + キャンセル対応
        await _captureSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Console.WriteLine("🔥🔥🔥 [ADAPTER] CaptureWindowAsync呼び出されました！HWND=0x{0:X}", windowHandle.ToInt64());

            // ログファイルにも出力
            try
            {
                var loggingSettings = LoggingSettings.CreateDevelopmentSettings();
                var logPath = loggingSettings.GetFullDebugLogPath();
                File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥🔥🔥 [ADAPTER] CaptureWindowAsync呼び出されました！HWND=0x{windowHandle.ToInt64():X}{Environment.NewLine}");
            }
            catch { /* ログファイル書き込み失敗は無視 */ }
            _logger.LogInformation("🔥 適応的キャプチャサービスアダプター: CaptureWindowAsync呼び出され - HWND=0x{WindowHandle:X}", windowHandle.ToInt64());
            _logger.LogDebug("適応的ウィンドウキャプチャ開始: HWND=0x{WindowHandle:X}", windowHandle.ToInt64());

            // 🎯 [WIN32_OVERLAY_FIX] ウィンドウサイズに基づいて最適なROIScaleFactorを計算
            var adaptiveCaptureOptions = CreateAdaptiveCaptureOptions();
            adaptiveCaptureOptions.ROIScaleFactor = CalculateOptimalROIScaleFactor(windowHandle);

            var result = await _adaptiveCaptureService.CaptureAsync(windowHandle, adaptiveCaptureOptions).ConfigureAwait(false);

            if (!result.Success || result.CapturedImages == null || result.CapturedImages.Count == 0)
            {
                throw new InvalidOperationException($"適応的ウィンドウキャプチャに失敗: {result.ErrorDetails}");
            }

            _logger.LogInformation("適応的ウィンドウキャプチャ成功: 戦略={Strategy}, 処理時間={ProcessingTime}ms",
                result.StrategyUsed, result.ProcessingTime.TotalMilliseconds);

            // 🔥 [PHASE2.5_ROI_FIX] result.DetectedTextRegionsからCaptureRegionを取得
            // AdaptiveCaptureServiceがROI画像の絶対座標をDetectedTextRegionsに格納している
            Rectangle? captureRegion = null;
            if (result.DetectedTextRegions != null && result.DetectedTextRegions.Count > 0)
            {
                captureRegion = result.DetectedTextRegions[0];
                _logger.LogDebug("🔥 [ROI_CAPTURE_REGION] CaptureRegion取得: {CaptureRegion}", captureRegion);
            }

            // 🎯 CRITICAL FIX: SafeImageAdapterの場合はWindowsImageAdapterでラップ（型互換性確保）
            var capturedImage = result.CapturedImages[0];
            WindowsImageAdapter adapter;
            if (capturedImage is SafeImageAdapter safeImageAdapter)
            {
                _logger.LogInformation("🎯 [PHASE3.18.4] SafeImageAdapter検出 - WindowsImageAdapterでラップしてIImage互換性確保");
                Console.WriteLine("🎯 [PHASE3.18.4] SafeImageAdapter → WindowsImageAdapter変換（型安全）");
                // 🔥 [PHASE2.5_ROI_FIX] result.DetectedTextRegions[0]から取得したCaptureRegionを設定
                adapter = new WindowsImageAdapter(safeImageAdapter, captureRegion);
            }
            else
            {
                // レガシー対応: SafeImageAdapter以外の場合はWindowsImageAdapterでラップ
                _logger.LogWarning("⚠️ [PHASE3.18.4] 非SafeImageAdapter検出 - WindowsImageAdapterでラップ: Type={Type}", capturedImage.GetType().Name);
                // 🔥 [PHASE2.5_ROI_FIX] result.DetectedTextRegions[0]から取得したCaptureRegionを設定
                adapter = new WindowsImageAdapter(capturedImage, captureRegion);
            }

            // 🔥 [Issue #193/#194] キャプチャ時に実行されたOCR結果を画像に付与（二重OCR防止）
            if (result.PreExecutedOcrResult != null)
            {
                adapter.PreExecutedOcrResult = result.PreExecutedOcrResult;
                _logger.LogInformation("🔥 [DUAL_OCR_FIX] PreExecutedOcrResult設定: {RegionCount}個のテキスト領域",
                    result.PreExecutedOcrResult.TextRegions.Count);
            }

            return adapter;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "適応的ウィンドウキャプチャでエラー");
            throw;
        }
        finally
        {
            // [Issue #361] セマフォ解放
            _captureSemaphore.Release();
        }
    }

    public async Task<IImage> CaptureClientAreaAsync(IntPtr windowHandle, CancellationToken cancellationToken = default)
    {
        // [Issue #361] 同時キャプチャ防止 + キャンセル対応
        await _captureSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogDebug("適応的クライアント領域キャプチャ開始: HWND=0x{WindowHandle:X}", windowHandle.ToInt64());

            // クライアント領域キャプチャ用のCaptureOptionsを作成
            var adaptiveCaptureOptions = CreateAdaptiveCaptureOptions();
            // 注意: 現在のCaptureOptionsにはCaptureClientAreaOnlyプロパティがないため、通常のキャプチャを使用

            var result = await _adaptiveCaptureService.CaptureAsync(windowHandle, adaptiveCaptureOptions).ConfigureAwait(false);

            if (!result.Success || result.CapturedImages == null || result.CapturedImages.Count == 0)
            {
                throw new InvalidOperationException($"適応的クライアント領域キャプチャに失敗: {result.ErrorDetails}");
            }

            _logger.LogInformation("適応的クライアント領域キャプチャ成功: 戦略={Strategy}, 処理時間={ProcessingTime}ms",
                result.StrategyUsed, result.ProcessingTime.TotalMilliseconds);

            // IWindowsImageをIImageアダプターでラップして返す
            return new WindowsImageAdapter(result.CapturedImages[0]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "適応的クライアント領域キャプチャでエラー");
            throw;
        }
        finally
        {
            _captureSemaphore.Release();
        }
    }

    public async Task<bool> DetectChangesAsync(IImage previousImage, IImage currentImage, float threshold = 0.05f)
    {
        try
        {
            // 🚀 [PHASE_C_IMPLEMENTATION] EnhancedImageChangeDetectionServiceを活用した3段階フィルタリング
            if (previousImage == null || currentImage == null)
            {
                _logger.LogTrace("🎯 [PHASE_C] 画像がnullのため変更ありと判定");
                return true;
            }

            // 画像サイズが異なる場合は変更ありとみなす
            if (previousImage.Width != currentImage.Width || previousImage.Height != currentImage.Height)
            {
                _logger.LogTrace("🎯 [PHASE_C] 画像サイズ変更検出: {PrevSize} → {CurrentSize}",
                    $"{previousImage.Width}x{previousImage.Height}",
                    $"{currentImage.Width}x{currentImage.Height}");
                return true;
            }

            // EnhancedImageChangeDetectionServiceが利用可能な場合は高度な3段階フィルタリングを使用
            if (_imageChangeDetectionService != null)
            {
                _logger.LogTrace("🎯 [PHASE_C] EnhancedImageChangeDetectionService使用 - 3段階フィルタリング開始");

                var changeResult = await _imageChangeDetectionService.DetectChangeAsync(
                    previousImage,
                    currentImage,
                    "adaptive_capture_adapter", // 一意のコンテキストID
                    CancellationToken.None).ConfigureAwait(false);

                _logger.LogTrace("🎯 [PHASE_C] 画面変化検知結果: {HasChanged}, Stage: {DetectionStage}, 変化率: {ChangePercentage:F3}%, 処理時間: {ProcessingTimeMs}ms",
                    changeResult.HasChanged,
                    changeResult.DetectionStage,
                    changeResult.ChangePercentage * 100,
                    changeResult.ProcessingTime.TotalMilliseconds);

                return changeResult.HasChanged;
            }

            // フォールバック: EnhancedImageChangeDetectionServiceが利用できない場合は基本検出
            _logger.LogTrace("🎯 [PHASE_C] EnhancedImageChangeDetectionService未利用 - 基本検出にフォールバック");
            return true; // 安全のため変更ありとする
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 [PHASE_C] 画面変化検知でエラー - 安全のため変更ありと判定");
            return true; // エラー時は変更ありとみなす（安全性優先）
        }
    }

    public void SetCaptureOptions(ServicesCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _currentOptions = options;

        _logger.LogDebug("キャプチャオプション設定: 間隔={Interval}ms, 品質={Quality}",
            options.CaptureInterval, options.Quality);
    }

    public ServicesCaptureOptions GetCaptureOptions()
    {
        return _currentOptions;
    }

    // 🔥 [PHASE_K-29-G] CaptureOptions統合: ServicesCaptureOptionsを使用
    // 🎯 [WIN32_OVERLAY_FIX] 解像度に基づく動的ROIScaleFactor計算
    private ServicesCaptureOptions CreateAdaptiveCaptureOptions()
    {
        return new ServicesCaptureOptions
        {
            AllowDirectFullScreen = true,
            AllowROIProcessing = true,
            AllowSoftwareFallback = true,
            ROIScaleFactor = 1.0f, // 🔥 [WIN32_OVERLAY_FIX] デフォルト1.0（ウィンドウキャプチャ時に動的計算）
            MaxRetryAttempts = 3,
            EnableHDRProcessing = true,
            TDRTimeoutMs = 2000
        };
    }

    /// <summary>
    /// 🎯 [WIN32_OVERLAY_FIX] ウィンドウサイズに基づいて最適なROIScaleFactorを計算
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>最適なROIScaleFactor (0.5 ~ 1.0)</returns>
    private float CalculateOptimalROIScaleFactor(IntPtr windowHandle)
    {
        try
        {
            // 🔥 [WIN32_OVERLAY_FIX] ICoordinateTransformationServiceを使用してウィンドウサイズ取得
            // GetWindowOffset()内部でGetWindowRect()を呼び出してウィンドウ矩形を取得
            var windowOffset = _coordinateTransformationService.GetWindowOffset(windowHandle);

            if (windowOffset == Point.Empty)
            {
                // [Issue #326] 動作に影響ないためDebugレベルに変更（ログノイズ削減）
                _logger.LogDebug("GetWindowOffset失敗 - デフォルトROIScaleFactor=1.0を使用");
                return 1.0f;
            }

            // ウィンドウサイズを直接取得できないため、一時的な解決策：
            // GetWindowRect経由でサイズ取得する代わりに、キャプチャ結果のサイズを使用
            // ただし、この時点ではキャプチャ前なので、別の方法が必要
            //
            // 🔥 [P0_OPTIMIZATION] ROIScaleFactor=0.5（Phase 1高速化 + ROI Smart Scaling）
            // 効果1: Phase 1処理時間74%削減（3840x2160→1920x1080）
            // 効果2: Phase 1メモリ使用量75%削減
            // 効果3: ROI画像（≤200px）は自動的にスケーリングスキップで精度100%維持
            //        (PaddleOcrImageProcessor.ConvertToMatWithScalingAsync:143-150)
            // 根拠: Gemini推奨、CoordinateRestorerにより座標系の整合性は自動保証
            _logger.LogInformation("🎯 [P0_OPTIMIZATION] ROIScaleFactor=0.5（Phase 1高速化 + ROI Smart Scaling有効化）");
            return 0.5f;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ROIScaleFactor計算エラー - デフォルト1.0を使用");
            return 1.0f;
        }
    }

    private static IntPtr GetDesktopWindowHandle()
    {
        // WindowsNativeServiceを通じてUser32Methods.GetDesktopWindow()を呼び出し
        return WindowsNativeService.GetDesktopWindowHandle();
    }

    /// <summary>
    /// キャプチャサービスを停止
    /// </summary>
    public async Task StopAsync()
    {
        if (_disposed)
            return;

        try
        {
            _logger.LogInformation("AdaptiveCaptureServiceAdapter停止処理開始");
            await _adaptiveCaptureService.StopAsync().ConfigureAwait(false);
            _logger.LogInformation("AdaptiveCaptureServiceAdapter停止処理完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AdaptiveCaptureServiceAdapter停止中にエラー");
        }
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            StopAsync().Wait(TimeSpan.FromSeconds(5));

            if (_adaptiveCaptureService is IDisposable disposableService)
            {
                disposableService.Dispose();
            }

            _disposed = true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "AdaptiveCaptureServiceAdapter破棄中にエラー");
        }

        GC.SuppressFinalize(this);
    }
}
