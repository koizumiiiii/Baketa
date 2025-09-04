using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Models.Capture;
using Baketa.Core.Exceptions.Capture;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Models.ImageProcessing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using System.Drawing;
using Baketa.Core.Settings;
using CaptureOptions = Baketa.Core.Models.Capture.CaptureOptions;

namespace Baketa.Application.Services.Capture;

/// <summary>
/// 適応的キャプチャサービスの実装
/// Phase 1: OCR処理最適化システム統合済み
/// </summary>
public class AdaptiveCaptureService(
    ICaptureEnvironmentDetector gpuDetector,
    ICaptureStrategyFactory strategyFactory,
    ILogger<AdaptiveCaptureService> logger,
    IEventAggregator eventAggregator,
    IOptions<LoggingSettings> loggingOptions,
    IImageChangeDetectionService? changeDetectionService = null,
    IWindowsImageAdapter? imageAdapter = null) : IAdaptiveCaptureService, IDisposable
{
    private readonly ICaptureEnvironmentDetector _gpuDetector = gpuDetector ?? throw new ArgumentNullException(nameof(gpuDetector));
    private readonly ICaptureStrategyFactory _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
    private readonly ILogger<AdaptiveCaptureService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    private readonly LoggingSettings _loggingSettings = loggingOptions?.Value ?? throw new ArgumentNullException(nameof(loggingOptions));
    private readonly IImageChangeDetectionService? _changeDetectionService = changeDetectionService;
    // ImageChangeDetectionSettings は新しい実装では不要
    private readonly IWindowsImageAdapter? _imageAdapter = imageAdapter;
    
    // GpuEnvironmentInfoのキャッシュ（起動時に1回だけ検出）
    private GpuEnvironmentInfo? _cachedEnvironment;
    private readonly object _cacheLock = new();
    
    // 画像変化検知用キャッシュ（ハッシュ値のみ保存でメモリ効率化）
    private IImage? _previousImage;
    private Rectangle _previousCaptureRegion;
    private readonly object _imageChangeLock = new();
    
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
            result.GpuEnvironment = await GetOrDetectGpuEnvironmentAsync().ConfigureAwait(false);
            
            
            // 2. 戦略選択
            var strategy = await SelectOptimalStrategyAsync(result.GpuEnvironment).ConfigureAwait(false);
            
            
            // 3. キャプチャ実行（フォールバック付き）
            var captureResult = await ExecuteWithFallbackAsync(
                hwnd, options, strategy, result.FallbacksAttempted).ConfigureAwait(false);
            
            // 🔄 Phase 1: 画像変化検知システム統合
            var imageChangeSkipped = false;
            if (captureResult.Success && captureResult.Images?.Count > 0 && 
                _changeDetectionService != null && _imageAdapter != null)
            {
                // 新しい実装では常に有効
                if (true)
                {
                    // WindowsImageをIImageに変換
                    var windowsImage = captureResult.Images[0];
                    var coreImage = await _imageAdapter.AdaptToImageAsync(windowsImage).ConfigureAwait(false);
                    var captureRegion = new Rectangle(0, 0, windowsImage.Width, windowsImage.Height);
                    
                    imageChangeSkipped = await ProcessImageChangeDetectionAsync(
                        coreImage, captureRegion).ConfigureAwait(false);
                }
            }
            
            // 4. 結果構築
            result.Success = captureResult.Success;
            result.CapturedImages = captureResult.Images;
            result.StrategyUsed = ParseStrategyUsed(captureResult.StrategyName);
            result.DetectedTextRegions = captureResult.TextRegions;
            result.ProcessingTime = stopwatch.Elapsed;
            result.Metrics = captureResult.Metrics;
            result.ErrorDetails = captureResult.ErrorMessage;
            result.ImageChangeSkipped = imageChangeSkipped; // 新機能: 変化検知結果
            
            // キャプチャ結果をログ出力
            if (_loggingSettings.EnableDebugFileLogging)
            {
                try 
                {
                    var logPath = _loggingSettings.GetFullDebugLogPath();
                    var imageCount = result.CapturedImages?.Count ?? 0;
                    var firstImage = result.CapturedImages?.FirstOrDefault();
                    File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📸 キャプチャ結果: 成功={result.Success}, 戦略={result.StrategyUsed}, 画像数={imageCount}, サイズ={firstImage?.Width}x{firstImage?.Height}, エラー={result.ErrorDetails ?? "None"}{Environment.NewLine}");
                }
                catch { /* ログファイル書き込み失敗は無視 */ }
            }
            
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

    public async Task<GpuEnvironmentInfo> DetectGpuEnvironmentAsync()
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
                environment.GpuName, environment.IsIntegratedGpu, environment.IsDedicatedGpu);
            
            return environment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPU環境検出中にエラー");
            throw;
        }
    }

    public async Task<ICaptureStrategy> SelectOptimalStrategyAsync(GpuEnvironmentInfo environment)
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

    public GpuEnvironmentInfo? GetCachedEnvironmentInfo()
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

    private async Task<GpuEnvironmentInfo> GetOrDetectGpuEnvironmentAsync()
    {
        lock (_cacheLock)
        {
            if (_cachedEnvironment != null)
            {
                _logger.LogDebug("キャッシュされたGPU環境情報を使用");
                return _cachedEnvironment;
            }
        }

        return await DetectGpuEnvironmentAsync().ConfigureAwait(false);
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
                if (_loggingSettings.EnableDebugFileLogging)
                {
                    try 
                    {
                        var logPath = _loggingSettings.GetFullDebugLogPath();
                        File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 戦略実行: {strategy.StrategyName}, HWND=0x{hwnd.ToInt64():X}{Environment.NewLine}");
                    }
                    catch { /* ログファイル書き込み失敗は無視 */ }
                }
                
                var result = await strategy.ExecuteCaptureAsync(hwnd, options).ConfigureAwait(false);
                
                // 戦略実行結果のログ
                if (_loggingSettings.EnableDebugFileLogging)
                {
                    try 
                    {
                        var logPath = _loggingSettings.GetFullDebugLogPath();
                        File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📊 戦略結果: {strategy.StrategyName}, 成功={result.Success}, 画像数={result.Images?.Count ?? 0}, エラー={result.ErrorMessage ?? "None"}{Environment.NewLine}");
                    }
                    catch { /* ログファイル書き込み失敗は無視 */ }
                }
                
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

    /// <summary>
    /// 画像変化検知処理（Phase 1: OCR処理最適化システム）
    /// </summary>
    private async Task<bool> ProcessImageChangeDetectionAsync(
        IImage currentImage, 
        Rectangle captureRegion)
    {
        try
        {
            string contextId = "adaptive_capture";
            
            lock (_imageChangeLock)
            {
                // ROI変更時はキャッシュをリセット（Gemini提案）
                if (_previousCaptureRegion != captureRegion)
                {
                    _logger.LogDebug("🔄 キャプチャ領域変更検出 - キャッシュリセット");
                    _changeDetectionService!.ClearCache(contextId);
                    
                    // 🔥 Critical Fix: 古いIImageを適切に破棄
                    if (_previousImage is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                    _previousImage = null;
                    _previousCaptureRegion = captureRegion;
                }
            }

            // P0: 3段階フィルタリング画像変化検知を実行
            var changeResult = await _changeDetectionService!.DetectChangeAsync(
                _previousImage, 
                currentImage, 
                contextId, 
                CancellationToken.None).ConfigureAwait(false);

            // ログ出力
            _logger.LogDebug("🎯 P0画像変化検知: {HasChanged}, Stage: {DetectionStage}, 変化率: {ChangePercentage:F3}%, 処理時間: {ProcessingTimeMs}ms",
                changeResult.HasChanged, 
                changeResult.DetectionStage, 
                changeResult.ChangePercentage * 100, 
                changeResult.ProcessingTime.TotalMilliseconds);

            // 🔥 Critical Fix: 古い前回画像を適切に破棄してから更新
            lock (_imageChangeLock)
            {
                if (_previousImage is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                _previousImage = currentImage;
            }

            // 変化なし = OCRスキップ
            return !changeResult.HasChanged;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 P0画像変化検知エラー - フォールバック実行");
            return false; // エラー時はOCRを実行
        }
    }

    /// <summary>
    /// 画像変化検知キャッシュのリセット（ROI変更等で使用）
    /// </summary>
    public void ClearImageChangeCache()
    {
        lock (_imageChangeLock)
        {
            // 🔥 Critical Fix: IImageを適切に破棄
            if (_previousImage is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _previousImage = null;
            _previousCaptureRegion = Rectangle.Empty;
            _changeDetectionService?.ClearCache("adaptive_capture");
            _logger.LogDebug("🔄 P0画像変化検知キャッシュをクリア（リソース破棄完了）");
        }
    }

    /// <summary>
    /// IImageをbyte配列に変換
    /// </summary>

    /// <summary>
    /// ハッシュ間の変化率を計算
    /// </summary>

    private async Task PublishCaptureCompletedEventAsync(AdaptiveCaptureResult result)
    {
        try
        {
            // イベント発行（既存のイベント集約器を使用）
            // 具体的なイベントクラスは既存のアーキテクチャに合わせて実装
            _logger.LogDebug("キャプチャ完了イベント発行準備");
            
            // CaptureCompletedEventを発行して、OCR・翻訳パイプラインをトリガー
            if (result.Success && result.CapturedImages.Count > 0)
            {
                var primaryImage = result.CapturedImages[0];
                
                // 🔧 [CAPTURE_FIX] IImage変換処理
                IImage? imageInterface = null;
                
                if (primaryImage is IImage directImage)
                {
                    // 直接IImageの場合
                    imageInterface = directImage;
                    _logger.LogDebug("🔧 [CAPTURE_FIX] 直接IImage変換成功");
                }
                else if (primaryImage is IWindowsImage windowsImage)
                {
                    // WindowsImageの場合はアダプターを直接作成して変換
                    var adapter = new Baketa.Infrastructure.Platform.Adapters.DefaultWindowsImageAdapter();
                    imageInterface = adapter.ToImage(windowsImage);
                    _logger.LogDebug("🔧 [CAPTURE_FIX] WindowsImageAdapter変換成功 - Type: {Type}", imageInterface?.GetType()?.Name ?? "null");
                    // 🔧 [DISPOSE_FIX] adapter.Dispose()を削除 - WindowsImageの早期破棄を防ぐ
                }
                
                if (imageInterface == null)
                {
                    _logger.LogWarning("🔧 [CAPTURE_FIX] IImage変換失敗 - Type: {Type}", 
                        primaryImage?.GetType()?.Name ?? "null");
                    return;
                }
                
                var captureRegion = new Rectangle(0, 0, primaryImage.Width, primaryImage.Height);
                var captureCompletedEvent = new CaptureCompletedEvent(
                    imageInterface, 
                    captureRegion, 
                    result.ProcessingTime)
                {
                    ImageChangeSkipped = result.ImageChangeSkipped
                };
                
                await _eventAggregator.PublishAsync(captureCompletedEvent).ConfigureAwait(false);
                
                _logger.LogInformation("🎯 CaptureCompletedEvent発行完了: {Width}x{Height}", 
                    primaryImage.Width, primaryImage.Height);
            }
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
