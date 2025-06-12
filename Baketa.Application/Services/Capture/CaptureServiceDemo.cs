using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Hosting; // 一時的にコメントアウト
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Settings;
using Baketa.Application.Services.Capture;

namespace Baketa.Application.Services.Capture;

/// <summary>
/// AdvancedCaptureServiceのデモンストレーションプログラム
/// </summary>
public class CaptureServiceDemo
{
    private readonly IAdvancedCaptureService _captureService;
    private readonly ILogger<CaptureServiceDemo> _logger;
    
    public CaptureServiceDemo(
        IAdvancedCaptureService captureService,
        ILogger<CaptureServiceDemo> logger)
    {
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <summary>
    /// デモプログラムを実行します
    /// </summary>
    public async Task RunDemoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("=== Baketa Advanced Capture Service Demo ===");
        
        try
        {
            // 1. 基本的なキャプチャ設定の作成
            await DemoBasicCapture(cancellationToken).ConfigureAwait(false);
            
            // 2. パフォーマンス最適化のデモ
            await DemoPerformanceOptimization(cancellationToken).ConfigureAwait(false);
            
            _logger.LogInformation("デモプログラムが正常に完了しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "デモプログラム実行中にエラーが発生しました");
            throw;
        }
    }
    
    /// <summary>
    /// 基本的なキャプチャ機能のデモ
    /// </summary>
    private async Task DemoBasicCapture(CancellationToken cancellationToken)
    {
        _logger.LogInformation("--- 基本キャプチャ機能のデモ ---");
        
        // 全画面キャプチャ
        _logger.LogInformation("全画面キャプチャを実行...");
        var screenImage = await _captureService.CaptureScreenAsync().ConfigureAwait(false);
        _logger.LogInformation("全画面キャプチャ完了: {Width}x{Height}", screenImage.Width, screenImage.Height);
        screenImage.Dispose();
        
        // 領域キャプチャ
        _logger.LogInformation("指定領域キャプチャを実行...");
        var region = new System.Drawing.Rectangle(100, 100, 400, 300);
        var regionImage = await _captureService.CaptureRegionAsync(region).ConfigureAwait(false);
        _logger.LogInformation("領域キャプチャ完了: {Width}x{Height}", regionImage.Width, regionImage.Height);
        regionImage.Dispose();
        
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
    }
    
    /// <summary>
    /// パフォーマンス最適化機能のデモ
    /// </summary>
    private async Task DemoPerformanceOptimization(CancellationToken cancellationToken)
    {
        _logger.LogInformation("--- パフォーマンス最適化機能のデモ ---");
        
        // 現在のパフォーマンス情報を表示
        var perfInfo = _captureService.PerformanceInfo;
        _logger.LogInformation("現在のパフォーマンス統計:");
        _logger.LogInformation("  総キャプチャ回数: {TotalCount}", perfInfo.TotalCaptureCount);
        _logger.LogInformation("  成功回数: {SuccessCount}", perfInfo.SuccessfulCaptureCount);
        _logger.LogInformation("  失敗回数: {FailedCount}", perfInfo.FailedCaptureCount);
        _logger.LogInformation("  スキップ回数: {SkippedCount}", perfInfo.SkippedCaptureCount);
        _logger.LogInformation("  成功率: {SuccessRate:F1}%", perfInfo.SuccessRate);
        _logger.LogInformation("  平均キャプチャ時間: {AvgTime:F1}ms", perfInfo.AverageCaptureTimeMs);
        _logger.LogInformation("  現在のキャプチャレート: {Rate:F1}/秒", perfInfo.CurrentCaptureRate);
        
        // 最適化の実行
        _logger.LogInformation("キャプチャ最適化を実行...");
        await _captureService.OptimizeCaptureAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("最適化完了");
        
        // 最適化後の設定を確認
        var currentSettings = _captureService.GetCurrentSettings();
        _logger.LogInformation("最適化後のキャプチャ間隔: {Interval}ms", currentSettings.CaptureIntervalMs);
        
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
    }
}

/* TODO: IGameProfileManagerとMicrosoft.Extensions.Hostingが必要なため一時的にコメントアウト
/// <summary>
/// デモプログラムのエントリーポイント
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        // TODO: 実装予定
        Console.WriteLine("Demo implementation pending");
        await Task.CompletedTask;
    }
}
*/
