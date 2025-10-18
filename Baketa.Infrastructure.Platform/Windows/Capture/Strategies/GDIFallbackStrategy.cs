using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Models.Capture;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Exceptions.Capture;
using Baketa.Core.Abstractions.GPU;
// 🔥 [PHASE_K-29-G] CaptureOptions統合: Baketa.Core.Abstractions.Servicesから取得
using CaptureOptions = Baketa.Core.Abstractions.Services.CaptureOptions;

namespace Baketa.Infrastructure.Platform.Windows.Capture.Strategies;

/// <summary>
/// GDI を使用した最終フォールバック戦略（最も確実だが低性能）
/// </summary>
public class GDIFallbackStrategy : ICaptureStrategy
{
    private readonly ILogger<GDIFallbackStrategy> _logger;

    public string StrategyName => "GDIFallback";
    public int Priority => 5; // 最低優先度（最終手段）

    public GDIFallbackStrategy(ILogger<GDIFallbackStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanApply(GpuEnvironmentInfo environment, IntPtr hwnd)
    {
        // GDI APIは常に利用可能
        return hwnd != IntPtr.Zero;
    }

    public async Task<bool> ValidatePrerequisitesAsync(IntPtr hwnd)
    {
        return await Task.Run(() => hwnd != IntPtr.Zero && IsWindow(hwnd)).ConfigureAwait(false);
    }

    public async Task<CaptureStrategyResult> ExecuteCaptureAsync(IntPtr hwnd, CaptureOptions options)
    {
        var result = new CaptureStrategyResult
        {
            StrategyName = StrategyName,
            Metrics = new CaptureMetrics()
        };

        try
        {
            _logger.LogDebug("GDIFallbackキャプチャ開始（簡易実装）");
            
            // TODO: 実際のGDI実装
            // 現時点ではスタブ実装
            await Task.Delay(100).ConfigureAwait(false); // GDIキャプチャのシミュレート
            
            result.Success = false; // 現在は未実装
            result.ErrorMessage = "GDI戦略は未実装";
            result.Metrics.PerformanceCategory = "LowPerformance";

            _logger.LogWarning("GDIFallback戦略は現在未実装");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GDIFallbackキャプチャ中にエラー");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);
}