using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Models.Capture;
using Baketa.Core.Exceptions.Capture;
using Baketa.Core.Abstractions.Events;

namespace Baketa.Application.Services.Capture;

/// <summary>
/// 適応的キャプチャサービスの実装
/// </summary>
public class AdaptiveCaptureService(
    IGPUEnvironmentDetector gpuDetector,
    ICaptureStrategyFactory strategyFactory,
    ILogger<AdaptiveCaptureService> logger,
    IEventAggregator eventAggregator) : IAdaptiveCaptureService, IDisposable
{
    private readonly IGPUEnvironmentDetector _gpuDetector = gpuDetector ?? throw new ArgumentNullException(nameof(gpuDetector));
    private readonly ICaptureStrategyFactory _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
    private readonly ILogger<AdaptiveCaptureService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    
    // GPUEnvironmentInfoのキャッシュ（起動時に1回だけ検出）
    private GPUEnvironmentInfo? _cachedEnvironment;
    private readonly object _cacheLock = new();
    
    // キャンセレーションとリソース管理
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<IDisposable> _activeResources = [];
    private bool _disposed;

    public async Task<AdaptiveCaptureResult> CaptureAsync(IntPtr hwnd, CaptureOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new AdaptiveCaptureResult
        {
            FallbacksAttempted = [],
            CaptureTime = DateTime.Now
        };
        
        try
        {
            _logger.LogInformation("適応的キャプチャ開始: HWND=0x{Hwnd:X}", hwnd.ToInt64());

            // 1. GPU環境取得（キャッシュ利用）
            result.GPUEnvironment = await GetOrDetectGPUEnvironmentAsync().ConfigureAwait(false);
            
            
            // 2. 戦略選択
            var strategy = await SelectOptimalStrategyAsync(result.GPUEnvironment).ConfigureAwait(false);
            
            
            // 3. キャプチャ実行（フォールバック付き）
            var captureResult = await ExecuteWithFallbackAsync(
                hwnd, options, strategy, result.FallbacksAttempted).ConfigureAwait(false);
            
            // 4. 結果構築
            result.Success = captureResult.Success;
            result.CapturedImages = captureResult.Images;
            result.StrategyUsed = ParseStrategyUsed(captureResult.StrategyName);
            result.DetectedTextRegions = captureResult.TextRegions;
            result.ProcessingTime = stopwatch.Elapsed;
            result.Metrics = captureResult.Metrics;
            result.ErrorDetails = captureResult.ErrorMessage;
            
            // キャプチャ結果をログ出力
            try 
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                var imageCount = result.CapturedImages?.Count ?? 0;
                var firstImage = result.CapturedImages?.FirstOrDefault();
                File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📸 キャプチャ結果: 成功={result.Success}, 戦略={result.StrategyUsed}, 画像数={imageCount}, サイズ={firstImage?.Width}x{firstImage?.Height}, エラー={result.ErrorDetails ?? "None"}{Environment.NewLine}");
            }
            catch { /* ログファイル書き込み失敗は無視 */ }
            
            // 5. メトリクス記録
            RecordMetrics(result);
            
            // 6. イベント発行（UIスレッドコンテキストを維持）
            await PublishCaptureCompletedEventAsync(result).ConfigureAwait(false);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "適応的キャプチャ失敗");
            result.Success = false;
            result.ProcessingTime = stopwatch.Elapsed;
            result.ErrorDetails = ex.Message;
            return result;
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    public async Task<GPUEnvironmentInfo> DetectGPUEnvironmentAsync()
    {
        try
        {
            _logger.LogDebug("GPU環境検出開始");
            var environment = await _gpuDetector.DetectEnvironmentAsync().ConfigureAwait(false);
            
            // キャッシュに保存
            lock (_cacheLock)
            {
                _cachedEnvironment = environment;
            }
            
            _logger.LogInformation("GPU環境検出完了: {GpuName} (統合={IsIntegrated}, 専用={IsDedicated})", 
                environment.GPUName, environment.IsIntegratedGPU, environment.IsDedicatedGPU);
            
            return environment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPU環境検出中にエラー");
            throw;
        }
    }

    public async Task<ICaptureStrategy> SelectOptimalStrategyAsync(GPUEnvironmentInfo environment)
    {
        try
        {
            _logger.LogDebug("最適戦略選択開始");
            
            var strategy = _strategyFactory.GetOptimalStrategy(environment, IntPtr.Zero);
            
            _logger.LogInformation("選択された戦略: {StrategyName}", strategy.StrategyName);
            return await Task.FromResult(strategy).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "戦略選択中にエラー");
            throw;
        }
    }

    public GPUEnvironmentInfo? GetCachedEnvironmentInfo()
    {
        lock (_cacheLock)
        {
            return _cachedEnvironment;
        }
    }

    public void ClearEnvironmentCache()
    {
        lock (_cacheLock)
        {
            _cachedEnvironment = null;
        }
        _gpuDetector.ClearCache();
        _logger.LogDebug("GPU環境キャッシュをクリア");
    }

    private async Task<GPUEnvironmentInfo> GetOrDetectGPUEnvironmentAsync()
    {
        lock (_cacheLock)
        {
            if (_cachedEnvironment != null)
            {
                _logger.LogDebug("キャッシュされたGPU環境情報を使用");
                return _cachedEnvironment;
            }
        }

        return await DetectGPUEnvironmentAsync().ConfigureAwait(false);
    }

    private async Task<CaptureStrategyResult> ExecuteWithFallbackAsync(
        IntPtr hwnd, 
        CaptureOptions options, 
        ICaptureStrategy primaryStrategy,
        IList<string> fallbacksAttempted)
    {
        var strategies = _strategyFactory.GetStrategiesInOrder(primaryStrategy);
        
        foreach (var strategy in strategies)
        {
            if (!ShouldTryStrategy(strategy, options))
                continue;
                
            try
            {
                _logger.LogDebug("戦略実行中: {StrategyName}", strategy.StrategyName);
                fallbacksAttempted.Add(strategy.StrategyName);
                
                // 戦略実行前のログ
                try 
                {
                    var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                    File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 戦略実行: {strategy.StrategyName}, HWND=0x{hwnd.ToInt64():X}{Environment.NewLine}");
                }
                catch { /* ログファイル書き込み失敗は無視 */ }
                
                var result = await strategy.ExecuteCaptureAsync(hwnd, options).ConfigureAwait(false);
                
                // 戦略実行結果のログ
                try 
                {
                    var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                    File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📊 戦略結果: {strategy.StrategyName}, 成功={result.Success}, 画像数={result.Images?.Count ?? 0}, エラー={result.ErrorMessage ?? "None"}{Environment.NewLine}");
                }
                catch { /* ログファイル書き込み失敗は無視 */ }
                
                if (result.Success)
                {
                    _logger.LogInformation("戦略成功: {StrategyName}", strategy.StrategyName);
                    return result;
                }
                else
                {
                    _logger.LogDebug("戦略失敗: {StrategyName} - {ErrorMessage}", 
                        strategy.StrategyName, result.ErrorMessage);
                }
            }
            catch (TDRException ex)
            {
                _logger.LogWarning(ex, "戦略でTDR検出: {StrategyName}", strategy.StrategyName);
                
                // TDR検出時の処理
                await HandleTDRAsync().ConfigureAwait(false);
                
                // TDRが発生した戦略は継続試行しない
                continue;
            }
            catch (GPUConstraintException ex)
            {
                _logger.LogWarning(ex, "戦略でGPU制約検出: {StrategyName}", strategy.StrategyName);
                
                // GPU制約が発生した戦略は継続試行しない
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "戦略実行中にエラー: {StrategyName}", strategy.StrategyName);
                
                // その他のエラーは次の戦略を試行
                continue;
            }
        }
        
        throw new InvalidOperationException("すべての戦略が失敗しました");
    }

    private bool ShouldTryStrategy(ICaptureStrategy strategy, CaptureOptions options)
    {
        return strategy.StrategyName switch
        {
            "DirectFullScreen" => options.AllowDirectFullScreen,
            "ROIBased" => options.AllowROIProcessing,
            "PrintWindowFallback" or "GDIFallback" => options.AllowSoftwareFallback,
            _ => true
        };
    }

    private async Task HandleTDRAsync()
    {
        _logger.LogWarning("TDR検出 - GPU回復待機中");
        await Task.Delay(3000).ConfigureAwait(false); // GPU回復待機
        
        // 環境情報リセット（再検出を促す）
        ClearEnvironmentCache();
    }

    private CaptureStrategyUsed ParseStrategyUsed(string strategyName)
    {
        return strategyName switch
        {
            "DirectFullScreen" => CaptureStrategyUsed.DirectFullScreen,
            "ROIBased" => CaptureStrategyUsed.ROIBased,
            "PrintWindowFallback" => CaptureStrategyUsed.PrintWindowFallback,
            "GDIFallback" => CaptureStrategyUsed.GDIFallback,
            _ => CaptureStrategyUsed.DirectFullScreen
        };
    }

    private void RecordMetrics(AdaptiveCaptureResult result)
    {
        try
        {
            if (result.Metrics != null)
            {
                result.Metrics.TotalProcessingTime = result.ProcessingTime;
                result.Metrics.RetryAttempts = result.FallbacksAttempted.Count - 1;
                
                _logger.LogDebug("メトリクス記録: 戦略={Strategy}, 処理時間={ProcessingTime}ms, リトライ={Retries}", 
                    result.StrategyUsed, result.ProcessingTime.TotalMilliseconds, result.Metrics.RetryAttempts);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "メトリクス記録中にエラー");
        }
    }

    private async Task PublishCaptureCompletedEventAsync(AdaptiveCaptureResult result)
    {
        try
        {
            // イベント発行（既存のイベント集約器を使用）
            // 具体的なイベントクラスは既存のアーキテクチャに合わせて実装
            _logger.LogDebug("キャプチャ完了イベント発行準備");
            
            // TODO: 適切なCaptureCompletedEventを実装して発行
            await Task.CompletedTask.ConfigureAwait(false);
            
            // Note: result は将来のイベント発行で使用予定
            _ = result; // 未使用警告を抑制
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "キャプチャ完了イベント発行中にエラー");
        }
    }
    
    /// <summary>
    /// キャプチャサービスを停止し、リソースをクリーンアップ
    /// </summary>
    public async Task StopAsync()
    {
        if (_disposed)
            return;
            
        try
        {
            _logger.LogInformation("AdaptiveCaptureService停止処理開始");
            
            // 実行中のキャプチャをキャンセル
            _cancellationTokenSource.Cancel();
            
            // アクティブなリソースをクリーンアップ
            lock (_activeResources)
            {
                foreach (var resource in _activeResources)
                {
                    try
                    {
                        resource.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "リソース解放中にエラー");
                    }
                }
                _activeResources.Clear();
            }
            
            // 環境キャッシュをクリア
            ClearEnvironmentCache();
            
            _logger.LogInformation("AdaptiveCaptureService停止処理完了");
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "キャプチャサービス停止中にエラー");
        }
    }
    
    /// <summary>
    /// 現在実行中のキャプチャ操作をキャンセル
    /// </summary>
    public async Task CancelCurrentCaptureAsync()
    {
        try
        {
            _logger.LogDebug("現在のキャプチャ操作をキャンセル");
            _cancellationTokenSource.Cancel();
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "キャプチャキャンセル中にエラー");
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
            _cancellationTokenSource.Dispose();
            _disposed = true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "AdaptiveCaptureService破棄中にエラー");
        }
        
        GC.SuppressFinalize(this);
    }
}
