using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform.Windows;
using Microsoft.Extensions.Logging;
using System.Drawing;

namespace Baketa.Application.Services;

/// <summary>
/// Step3: キャッシュ対応OCRエンジン
/// Gemini推奨戦略 - 画像ハッシュベースキャッシングで数ミリ秒応答
/// </summary>
public sealed class CachedOcrEngine : IOcrEngine
{
    private readonly IOcrEngine _baseEngine;
    private readonly IAdvancedOcrCacheService _cacheService;
    private readonly ILogger<CachedOcrEngine> _logger;
    
    // パフォーマンス統計
    private long _totalRequests = 0;
    private long _cacheHits = 0;
    private long _cacheMisses = 0;

    public CachedOcrEngine(
        IOcrEngine baseEngine,
        IAdvancedOcrCacheService cacheService,
        ILogger<CachedOcrEngine> logger)
    {
        _baseEngine = baseEngine ?? throw new ArgumentNullException(nameof(baseEngine));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _logger.LogInformation("🚀 CachedOcrEngine初期化完了 - Step3高度キャッシング戦略有効");
    }

    // IOcrEngine インターフェース実装
    public string EngineName => $"Cached({_baseEngine.EngineName})";
    public string EngineVersion => _baseEngine.EngineVersion;
    public bool IsInitialized => _baseEngine.IsInitialized;
    public string? CurrentLanguage => _baseEngine.CurrentLanguage;

    public async Task<bool> InitializeAsync(OcrEngineSettings? settings = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await _baseEngine.InitializeAsync(settings, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        
        _logger.LogInformation("⚡ CachedOcrEngine初期化完了 - 時間: {ElapsedMs}ms, 結果: {Result}", stopwatch.ElapsedMilliseconds, result);
        return result;
    }
    
    public async Task<bool> WarmupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔥 CachedOcrEngine: ウォームアップ処理を内部エンジンに委譲");
        var stopwatch = Stopwatch.StartNew();
        var result = await _baseEngine.WarmupAsync(cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        _logger.LogInformation("✅ CachedOcrEngineウォームアップ完了 - 時間: {ElapsedMs}ms, 結果: {Result}", stopwatch.ElapsedMilliseconds, result);
        return result;
    }

    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        return await RecognizeAsync(image, null, progressCallback, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        Rectangle? regionOfInterest,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        
        var totalStopwatch = Stopwatch.StartNew();
        var requestId = ++_totalRequests;
        
        try
        {
            // 🎯 Step 1: 画像データを取得してハッシュ化
            var hashStopwatch = Stopwatch.StartNew();
            byte[] imageData;
            
            using (var memoryStream = new MemoryStream())
            {
                // IImageから画像データを抽出
                if (image is IWindowsImage windowsImage)
                {
                    using var bitmap = windowsImage.GetBitmap();
                    
                    // ROIが指定されている場合は切り取り
                    if (regionOfInterest.HasValue)
                    {
                        var roi = regionOfInterest.Value;
                        using var croppedBitmap = new Bitmap(roi.Width, roi.Height);
                        using var graphics = Graphics.FromImage(croppedBitmap);
                        graphics.DrawImage(bitmap, 0, 0, roi, GraphicsUnit.Pixel);
                        croppedBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    else
                    {
                        bitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                else
                {
                    // フォールバック: 汎用IImage処理
                    throw new NotSupportedException($"IImage type {image.GetType().Name} is not supported for caching");
                }
                
                imageData = memoryStream.ToArray();
            }
            
            var imageHash = _cacheService.GenerateImageHash(imageData);
            hashStopwatch.Stop();
            
            _logger.LogDebug("🔍 [Req:{RequestId}] 画像ハッシュ生成: {Hash} - 時間: {ElapsedMs}ms, サイズ: {Size}bytes", 
                requestId, imageHash[..12], hashStopwatch.ElapsedMilliseconds, imageData.Length);

            // 🎯 Step 2: キャッシュチェック
            var cacheStopwatch = Stopwatch.StartNew();
            var cachedResult = _cacheService.GetCachedResult(imageHash);
            cacheStopwatch.Stop();
            
            if (cachedResult != null)
            {
                // ✅ キャッシュヒット
                Interlocked.Increment(ref _cacheHits);
                totalStopwatch.Stop();
                
                _logger.LogInformation("⚡ [Req:{RequestId}] キャッシュヒット成功 - 総時間: {TotalMs}ms (ハッシュ: {HashMs}ms, キャッシュ: {CacheMs}ms), 認識数: {TextCount}", 
                    requestId, totalStopwatch.ElapsedMilliseconds, hashStopwatch.ElapsedMilliseconds, cacheStopwatch.ElapsedMilliseconds, cachedResult.TextRegions.Count);
                
                // プログレスコールバック（即座に完了を通知）
                progressCallback?.Report(new OcrProgress(1.0, "キャッシュから取得済み"));
                
                return cachedResult;
            }
            
            // ❌ キャッシュミス - 実際のOCR処理を実行
            Interlocked.Increment(ref _cacheMisses);
            
            _logger.LogDebug("🔄 [Req:{RequestId}] キャッシュミス - OCR処理開始: {Hash}", requestId, imageHash[..12]);
            
            // 🎯 Step 3: 実際のOCR処理
            var ocrStopwatch = Stopwatch.StartNew();
            var ocrResult = await _baseEngine.RecognizeAsync(image, regionOfInterest, progressCallback, cancellationToken).ConfigureAwait(false);
            ocrStopwatch.Stop();
            
            // 🎯 Step 4: 結果をキャッシュに保存
            var saveCacheStopwatch = Stopwatch.StartNew();
            _cacheService.CacheResult(imageHash, ocrResult);
            saveCacheStopwatch.Stop();
            
            totalStopwatch.Stop();
            
            _logger.LogInformation("💾 [Req:{RequestId}] OCR処理+キャッシュ保存完了 - 総時間: {TotalMs}ms (ハッシュ: {HashMs}ms, OCR: {OcrMs}ms, 保存: {SaveMs}ms), 認識数: {TextCount}", 
                requestId, totalStopwatch.ElapsedMilliseconds, hashStopwatch.ElapsedMilliseconds, ocrStopwatch.ElapsedMilliseconds, saveCacheStopwatch.ElapsedMilliseconds, ocrResult.TextRegions.Count);
            
            return ocrResult;
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            _logger.LogError(ex, "❌ [Req:{RequestId}] CachedOcrEngine処理エラー - 総時間: {TotalMs}ms", requestId, totalStopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    public OcrEngineSettings GetSettings()
    {
        return _baseEngine.GetSettings();
    }

    public async Task ApplySettingsAsync(OcrEngineSettings settings, CancellationToken cancellationToken = default)
    {
        await _baseEngine.ApplySettingsAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<string> GetAvailableLanguages()
    {
        return _baseEngine.GetAvailableLanguages();
    }

    public IReadOnlyList<string> GetAvailableModels()
    {
        return _baseEngine.GetAvailableModels();
    }

    public async Task<bool> IsLanguageAvailableAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        return await _baseEngine.IsLanguageAvailableAsync(languageCode, cancellationToken).ConfigureAwait(false);
    }

    public OcrPerformanceStats GetPerformanceStats()
    {
        return _baseEngine.GetPerformanceStats();
    }

    public void CancelCurrentOcrTimeout()
    {
        _baseEngine.CancelCurrentOcrTimeout();
    }

    /// <summary>
    /// キャッシュ統計情報をログ出力
    /// </summary>
    public void LogCacheStatistics()
    {
        var hitRate = _totalRequests > 0 ? (double)_cacheHits / _totalRequests * 100 : 0;
        _logger.LogInformation("📊 キャッシュ統計 - 総リクエスト: {TotalRequests}, ヒット: {Hits}, ミス: {Misses}, ヒット率: {HitRate:F1}%", 
            _totalRequests, _cacheHits, _cacheMisses, hitRate);
    }

    public void Dispose()
    {
        LogCacheStatistics();
        _baseEngine?.Dispose();
        _cacheService?.Dispose();
        _logger.LogInformation("🗑️ CachedOcrEngine disposed");
    }
}