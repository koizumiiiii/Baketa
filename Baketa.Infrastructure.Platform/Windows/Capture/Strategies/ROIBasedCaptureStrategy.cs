using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Models.Capture;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Exceptions.Capture;
using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.GPU;
using System.Drawing;

namespace Baketa.Infrastructure.Platform.Windows.Capture.Strategies;

/// <summary>
/// 専用GPU向けROIベースキャプチャ戦略
/// </summary>
public class ROIBasedCaptureStrategy : ICaptureStrategy
{
    private readonly ILogger<ROIBasedCaptureStrategy> _logger;
    private readonly ITextRegionDetector _textDetector;
    private readonly NativeWindowsCaptureWrapper _nativeWrapper;
    private readonly Baketa.Core.Abstractions.Factories.IWindowsImageFactory _imageFactory;

    public string StrategyName => "ROIBased";
    public int Priority => 50; // 中優先度（専用GPU環境で効率的）

    public ROIBasedCaptureStrategy(
        ILogger<ROIBasedCaptureStrategy> logger,
        ITextRegionDetector textDetector,
        NativeWindowsCaptureWrapper nativeWrapper,
        Baketa.Core.Abstractions.Factories.IWindowsImageFactory imageFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _textDetector = textDetector ?? throw new ArgumentNullException(nameof(textDetector));
        _nativeWrapper = nativeWrapper ?? throw new ArgumentNullException(nameof(nativeWrapper));
        _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
    }

    public bool CanApply(GpuEnvironmentInfo environment, IntPtr hwnd)
    {
        try
        {
            // 専用GPUまたは大画面での制約回避が必要な場合
            var canApply = environment.IsDedicatedGpu || 
                          environment.MaximumTexture2DDimension < 8192;

            _logger.LogInformation("ROIBased戦略適用判定: {CanApply} (専用GPU: {IsDedicated}, MaxTexture: {MaxTexture})", 
                canApply, environment.IsDedicatedGpu, environment.MaximumTexture2DDimension);

            return canApply;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ROIBased戦略適用可能性チェック中にエラー");
            return false;
        }
    }

    public async Task<bool> ValidatePrerequisitesAsync(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero)
                return false;

            // 非同期的にウィンドウ検証を実行
            return await Task.Run(() =>
            {
                var windowExists = IsWindow(hwnd);
                var isVisible = IsWindowVisible(hwnd);

                return windowExists && isVisible;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ROIBased前提条件チェック中にエラー");
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
            _logger.LogInformation("ROIBasedキャプチャ開始: ウィンドウ=0x{Hwnd:X}", hwnd.ToInt64());

            // Phase 1: 低解像度スキャン
            var lowResImage = await CaptureLowResolutionAsync(hwnd, options.ROIScaleFactor).ConfigureAwait(false);
            if (lowResImage == null)
            {
                result.Success = false;
                result.ErrorMessage = "低解像度スキャンに失敗";
                return result;
            }

            // Phase 2: テキスト領域検出
            var textRegions = await _textDetector.DetectTextRegionsAsync(lowResImage).ConfigureAwait(false);
            result.TextRegions = textRegions;

            // Phase 3: 高解像度部分キャプチャ
            var highResImages = await CaptureHighResRegionsAsync(hwnd, textRegions).ConfigureAwait(false);
            
            result.Success = highResImages.Count > 0;
            result.Images = highResImages;
            result.Metrics.ActualCaptureTime = stopwatch.Elapsed;
            result.Metrics.FrameCount = highResImages.Count;
            result.Metrics.PerformanceCategory = "Balanced";

            _logger.LogInformation("ROIBasedキャプチャ完了: {RegionCount}個の領域, 処理時間={ProcessingTime}ms", 
                textRegions.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ROIBasedキャプチャ中にエラー");
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

    private async Task<IWindowsImage?> CaptureLowResolutionAsync(IntPtr hwnd, float scaleFactor)
    {
        try
        {
            _logger.LogInformation("低解像度スキャン実行: スケール={ScaleFactor}, 対象ウィンドウ=0x{Hwnd:X}", 
                scaleFactor, hwnd.ToInt64());

            // ネイティブラッパーでキャプチャセッション作成
            if (!_nativeWrapper.CreateCaptureSession(hwnd))
            {
                _logger.LogError("ネイティブキャプチャセッション作成失敗");
                return null;
            }

            try
            {
                // フル解像度キャプチャ
                var fullImage = await _nativeWrapper.CaptureFrameAsync(5000).ConfigureAwait(false);
                if (fullImage == null)
                {
                    _logger.LogWarning("フル解像度キャプチャに失敗");
                    return null;
                }

                // スケールダウン（リサイズ）
                var targetWidth = Math.Max(1, (int)(fullImage.Width * scaleFactor));
                var targetHeight = Math.Max(1, (int)(fullImage.Height * scaleFactor));

                var lowResImage = _imageFactory.ResizeImage(fullImage, targetWidth, targetHeight);

                _logger.LogInformation("低解像度キャプチャ完了: {OriginalSize} → {ScaledSize} (スケール: {ScaleFactor})",
                    $"{fullImage.Width}x{fullImage.Height}", $"{targetWidth}x{targetHeight}", scaleFactor);

                // フル解像度画像はリソース解放
                fullImage.Dispose();

                return lowResImage;
            }
            finally
            {
                _nativeWrapper.StopCurrentSession();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "低解像度キャプチャ中にエラー");
            throw new CaptureStrategyException(StrategyName, "低解像度キャプチャに失敗", ex);
        }
    }

    private async Task<IList<IWindowsImage>> CaptureHighResRegionsAsync(IntPtr hwnd, IList<Rectangle> textRegions)
    {
        var results = new List<IWindowsImage>();

        try
        {
            _logger.LogInformation("高解像度部分キャプチャ実行: {RegionCount}個の領域, 対象ウィンドウ=0x{Hwnd:X}", 
                textRegions.Count, hwnd.ToInt64());

            if (textRegions.Count == 0)
            {
                _logger.LogDebug("テキスト領域が指定されていません");
                return results;
            }

            // ネイティブラッパーでキャプチャセッション作成
            if (!_nativeWrapper.CreateCaptureSession(hwnd))
            {
                _logger.LogError("高解像度キャプチャセッション作成失敗");
                return results;
            }

            try
            {
                // 高解像度全体キャプチャ
                var fullImage = await _nativeWrapper.CaptureFrameAsync(5000).ConfigureAwait(false);
                if (fullImage == null)
                {
                    _logger.LogWarning("高解像度フル画像キャプチャに失敗");
                    return results;
                }

                try
                {
                    // 並列処理でROI領域を切り出し（スレッドセーフなConcurrentBag使用）
                    var cropTasks = textRegions.Select(async region =>
                    {
                        try
                        {
                            // 境界チェック
                            if (region.X < 0 || region.Y < 0 ||
                                region.Right > fullImage.Width || region.Bottom > fullImage.Height ||
                                region.Width <= 0 || region.Height <= 0)
                            {
                                _logger.LogWarning("無効な領域をスキップ: {Region}, 画像サイズ: {ImageSize}",
                                    region, $"{fullImage.Width}x{fullImage.Height}");
                                return null;
                            }

                            // 並列実行での画像切り出し（CPU集約的処理）
                            return await Task.Run(() =>
                            {
                                var croppedImage = _imageFactory.CropImage(fullImage, region);
                                if (croppedImage != null)
                                {
                                    _logger.LogDebug("領域キャプチャ完了: {Region} → {Size}",
                                        region, $"{croppedImage.Width}x{croppedImage.Height}");
                                }
                                return croppedImage;
                            }).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "領域 {Region} のキャプチャに失敗", region);
                            return null;
                        }
                    });

                    // 全ての並列タスクを待機
                    var croppedImages = await Task.WhenAll(cropTasks).ConfigureAwait(false);
                    
                    // 成功した画像のみをresultsに追加
                    foreach (var image in croppedImages)
                    {
                        if (image != null)
                        {
                            results.Add(image);
                        }
                    }

                    _logger.LogInformation("高解像度部分キャプチャ完了: {SuccessCount}/{TotalCount}個の領域を並列処理",
                        results.Count, textRegions.Count);
                }
                finally
                {
                    fullImage.Dispose();
                }
            }
            finally
            {
                _nativeWrapper.StopCurrentSession();
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "高解像度部分キャプチャ中にエラー");
            
            // エラー時はリソースクリーンアップ
            foreach (var image in results)
            {
                try { image.Dispose(); } catch { /* ignore cleanup errors */ }
            }
            results.Clear();
            
            throw new CaptureStrategyException(StrategyName, "部分キャプチャに失敗", ex);
        }
    }

    // Windows API P/Invoke
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}