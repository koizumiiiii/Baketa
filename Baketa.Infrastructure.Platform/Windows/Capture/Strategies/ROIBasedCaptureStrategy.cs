using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Models.Capture;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Exceptions.Capture;
using System.Drawing;

namespace Baketa.Infrastructure.Platform.Windows.Capture.Strategies;

/// <summary>
/// 専用GPU向けROIベースキャプチャ戦略
/// </summary>
public class ROIBasedCaptureStrategy : ICaptureStrategy
{
    private readonly ILogger<ROIBasedCaptureStrategy> _logger;
    private readonly ITextRegionDetector _textDetector;

    public string StrategyName => "ROIBased";
    public int Priority => 50; // 中優先度（専用GPU環境で効率的）

    public ROIBasedCaptureStrategy(
        ILogger<ROIBasedCaptureStrategy> logger,
        ITextRegionDetector textDetector)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _textDetector = textDetector ?? throw new ArgumentNullException(nameof(textDetector));
    }

    public bool CanApply(GPUEnvironmentInfo environment, IntPtr hwnd)
    {
        try
        {
            // 専用GPUまたは大画面での制約回避が必要な場合
            var canApply = environment.IsDedicatedGPU || 
                          environment.MaximumTexture2DDimension < 8192;

            _logger.LogDebug("ROIBased戦略適用可能性: {CanApply} (専用GPU: {IsDedicated}, MaxTexture: {MaxTexture})", 
                canApply, environment.IsDedicatedGPU, environment.MaximumTexture2DDimension);

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
            _logger.LogDebug("ROIBasedキャプチャ開始");

            // Phase 1: 低解像度スキャン
            var lowResImage = await CaptureLowResolutionAsync(hwnd, options.ROIScaleFactor);
            if (lowResImage == null)
            {
                result.Success = false;
                result.ErrorMessage = "低解像度スキャンに失敗";
                return result;
            }

            // Phase 2: テキスト領域検出
            var textRegions = await _textDetector.DetectTextRegionsAsync(lowResImage);
            result.TextRegions = textRegions;

            // Phase 3: 高解像度部分キャプチャ
            var highResImages = await CaptureHighResRegionsAsync(hwnd, textRegions);
            
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
            _logger.LogDebug("低解像度スキャン実行: スケール={ScaleFactor}", scaleFactor);
            
            // TODO: 実際の低解像度キャプチャ実装
            // 現在はスタブ実装
            await Task.Delay(50);
            
            _logger.LogWarning("低解像度キャプチャは現在未実装");
            return null;
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
            _logger.LogDebug("高解像度部分キャプチャ実行: {RegionCount}個の領域", textRegions.Count);

            foreach (var region in textRegions)
            {
                // TODO: 実際の部分キャプチャ実装
                await Task.Delay(10); // 部分キャプチャのシミュレート
                
                _logger.LogDebug("領域キャプチャ (未実装): {X},{Y} {Width}x{Height}", 
                    region.X, region.Y, region.Width, region.Height);
            }

            _logger.LogWarning("高解像度部分キャプチャは現在未実装");
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "高解像度部分キャプチャ中にエラー");
            throw new CaptureStrategyException(StrategyName, "部分キャプチャに失敗", ex);
        }
    }

    // Windows API P/Invoke
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}