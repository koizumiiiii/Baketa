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
using Baketa.Core.Abstractions.Memory;
using Microsoft.Extensions.Logging;
using System.Drawing;
using Baketa.Core.Extensions;

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
        System.Drawing.Rectangle? regionOfInterest,
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
            
            // 🔧 [PHASE3.5_FIX] ReferencedSafeImage防御的処理 - ObjectDisposedException完全対応
            if (image is ReferencedSafeImage referencedSafeImage)
            {
                // 🛡️ 段階的防御的アクセス - オーバーレイ表示修正の決定的対応
                try
                {
                    // Step 1: 参照カウント検証
                    var refCount = referencedSafeImage.ReferenceCount;
                    if (refCount <= 0)
                    {
                        _logger.LogWarning("🔧 [PHASE3.5_FIX] ReferencedSafeImage参照カウントが無効: {RefCount} - 処理スキップ", refCount);
                        throw new InvalidOperationException($"ReferencedSafeImage has invalid reference count: {refCount}");
                    }

                    // Step 2: SafeImage本体の有効性確認
                    var safeImage = referencedSafeImage.GetUnderlyingSafeImage();
                    if (safeImage == null || safeImage.IsDisposed)
                    {
                        _logger.LogWarning("🔧 [PHASE3.5_FIX] SafeImage本体が無効または破棄済み - 処理スキップ");
                        throw new InvalidOperationException("Underlying SafeImage is null or disposed");
                    }

                    // Step 3: 画像データの安全な取得
                    try
                    {
                        imageData = safeImage.GetImageData().ToArray();
                        _logger.LogDebug("🎯 [PHASE3.5_FIX] ReferencedSafeImage処理成功 - データ取得: {Size}bytes, RefCount: {RefCount}",
                            imageData.Length, refCount);
                    }
                    catch (ObjectDisposedException imageDataEx)
                    {
                        _logger.LogError(imageDataEx, "🔧 [PHASE3.5_FIX] 画像データ取得中にObjectDisposedException - SafeImage内部で破棄済み");
                        throw new InvalidOperationException("SafeImage data access failed due to disposal", imageDataEx);
                    }
                }
                catch (ObjectDisposedException disposedEx)
                {
                    // 🔥 [PHASE3.5_FIX] ObjectDisposedException統一ハンドリング - オーバーレイ非表示問題の根本修正
                    _logger.LogError(disposedEx, "💀 [SAFE_IMAGE] IImage変換でObjectDisposedException発生 - 画像が破棄済み");
                    throw new InvalidOperationException("ReferencedSafeImage has been disposed and cannot be used for OCR caching", disposedEx);
                }
                catch (Exception unexpectedEx)
                {
                    _logger.LogError(unexpectedEx, "🔧 [PHASE3.5_FIX] ReferencedSafeImage処理で予期しないエラー: {ErrorType}", unexpectedEx.GetType().Name);
                    throw new InvalidOperationException($"Unexpected error in ReferencedSafeImage processing: {unexpectedEx.Message}", unexpectedEx);
                }
            }
            // 🧠 [ULTRATHINK_TYPE_FIX] IAdvancedImage対応 - WindowsImageAdapter型不一致解決
            else if (regionOfInterest.HasValue && image is IAdvancedImage advancedImage)
            {
                // ROIが指定されている場合は切り取り処理
                using var croppedImage = await advancedImage.ExtractRegionAsync(regionOfInterest.Value.ToMemoryRectangle()).ConfigureAwait(false);
                imageData = await croppedImage.ToByteArrayAsync().ConfigureAwait(false);
            }
            else if (image is IAdvancedImage advancedImageFull)
            {
                // 🎯 [TYPE_COMPATIBILITY] IAdvancedImage.ToByteArrayAsync()使用でWindowsImageAdapter対応
                imageData = await advancedImageFull.ToByteArrayAsync().ConfigureAwait(false);
            }
            else if (image is IWindowsImage windowsImage)
            {
                // 🔄 [FALLBACK_COMPATIBILITY] 従来のIWindowsImage対応維持
                using var memoryStream = new MemoryStream();
                using var bitmap = windowsImage.GetBitmap();
                
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
                
                imageData = memoryStream.ToArray();
            }
            else
            {
                // エラーメッセージ改善 - ReferencedSafeImage対応を明記
                throw new NotSupportedException($"IImage type {image.GetType().Name} is not supported for caching. Supported types: ReferencedSafeImage, IAdvancedImage, IWindowsImage");
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
    /// 連続失敗回数を取得（診断・フォールバック判定用）
    /// </summary>
    /// <returns>連続失敗回数</returns>
    public int GetConsecutiveFailureCount()
    {
        // ベースエンジンに委譲
        return _baseEngine.GetConsecutiveFailureCount();
    }

    /// <summary>
    /// 失敗カウンタをリセット（緊急時復旧用）
    /// </summary>
    public void ResetFailureCounter()
    {
        // ベースエンジンに委譲
        _baseEngine.ResetFailureCounter();
    }

    /// <summary>
    /// テキスト検出のみを実行（認識処理をスキップ）
    /// キャッシュ対応版
    /// </summary>
    public async Task<OcrResults> DetectTextRegionsAsync(IImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        
        _logger.LogDebug("🔍 CachedOcrEngine: DetectTextRegionsAsync - ベースエンジンに委譲");
        
        // キャッシングは複雑になるため、現在は直接ベースエンジンに委譲
        // TODO: 将来的には検出専用キャッシュの実装を検討
        return await _baseEngine.DetectTextRegionsAsync(image, cancellationToken).ConfigureAwait(false);
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