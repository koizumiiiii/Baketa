using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Settings;

namespace Baketa.Infrastructure.OCR.StickyRoi;

/// <summary>
/// スティッキーROI機能拡張OCRエンジン
/// 前回検出領域の優先処理によるパフォーマンス最適化
/// Issue #143 Week 3 Phase 1: 統合OCRシステム
/// </summary>
public sealed class StickyRoiEnhancedOcrEngine : ISimpleOcrEngine
{
    private readonly ILogger<StickyRoiEnhancedOcrEngine> _logger;
    private readonly ISimpleOcrEngine _baseOcrEngine;
    private readonly IStickyRoiManager _roiManager;
    private readonly IOptionsMonitor<OcrSettings> _ocrSettings;
    private readonly SemaphoreSlim _ocrSemaphore;
    private bool _disposed;
    
    // パフォーマンス統計
    private long _totalRequests;
    private long _roiHits;
    private double _totalProcessingTime;
    private double _roiProcessingTime;

    public StickyRoiEnhancedOcrEngine(
        ILogger<StickyRoiEnhancedOcrEngine> logger,
        ISimpleOcrEngine baseOcrEngine,
        IStickyRoiManager roiManager,
        IOptionsMonitor<OcrSettings> ocrSettings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _baseOcrEngine = baseOcrEngine ?? throw new ArgumentNullException(nameof(baseOcrEngine));
        _roiManager = roiManager ?? throw new ArgumentNullException(nameof(roiManager));
        _ocrSettings = ocrSettings ?? throw new ArgumentNullException(nameof(ocrSettings));
        
        // Improvement: 設定から読み込み（Gemini指摘事項対応 - ハードコード設定外部化完了）
        var maxConcurrentRequests = _ocrSettings.CurrentValue.MaxConcurrentOcrRequests;
        _ocrSemaphore = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
        
        _logger.LogInformation("🎯 StickyRoiEnhancedOcrEngine初期化完了 - ROI最適化OCR開始 (最大同時OCR: {MaxConcurrent}, 最大並列ROI: {MaxParallelRois})", 
            maxConcurrentRequests, _ocrSettings.CurrentValue.MaxParallelRois);
    }

    public async Task<Baketa.Core.Abstractions.OCR.OcrResult> RecognizeTextAsync(
        byte[] imageData, 
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        Interlocked.Increment(ref _totalRequests);
        
        try
        {
            _logger.LogDebug("🔍 ROI拡張OCR開始 - データサイズ: {Size}B", imageData.Length);
            
            // 画像情報の取得
            var imageBounds = await GetImageBoundsAsync(imageData, cancellationToken);
            
            // 優先ROI領域の取得
            var priorityRois = await _roiManager.GetPriorityRoisAsync(imageBounds, 10, cancellationToken);
            
            Baketa.Core.Abstractions.OCR.OcrResult? roiResult = null;
            var roiStopwatch = Stopwatch.StartNew();
            
            // ROI優先処理
            if (priorityRois.Any())
            {
                roiResult = await ProcessPriorityRoisAsync(imageData, priorityRois, cancellationToken);
                roiStopwatch.Stop();
                
                if (roiResult != null && roiResult.DetectedTexts.Any())
                {
                    Interlocked.Increment(ref _roiHits);
                    _roiProcessingTime += roiStopwatch.Elapsed.TotalMilliseconds;
                    
                    _logger.LogDebug("✅ ROI処理成功 - 検出数: {Count}, 時間: {Time}ms",
                        roiResult.DetectedTexts.Count, roiStopwatch.ElapsedMilliseconds);
                    
                    // ROI信頼度更新
                    await UpdateRoiConfidenceAsync(priorityRois, roiResult, cancellationToken);
                    
                    // 新しい領域記録
                    await RecordDetectedRegionsAsync(roiResult, cancellationToken);
                    
                    stopwatch.Stop();
                    _totalProcessingTime += stopwatch.Elapsed.TotalMilliseconds;
                    
                    return roiResult;
                }
            }
            
            // フルスクリーン処理（ROI失敗時）
            _logger.LogDebug("🔄 フルスクリーンOCR実行 - ROI結果: {HasRoi}", roiResult != null);
            
            var fullResult = await _baseOcrEngine.RecognizeTextAsync(imageData, cancellationToken);
            
            // 結果統合
            var finalResult = MergeResults(roiResult, fullResult);
            
            // 新しい領域記録
            await RecordDetectedRegionsAsync(finalResult, cancellationToken);
            
            stopwatch.Stop();
            _totalProcessingTime += stopwatch.Elapsed.TotalMilliseconds;
            
            _logger.LogInformation("✅ ROI拡張OCR完了 - 総検出数: {Count}, 時間: {Time}ms, ROI効率: {Efficiency:P1}",
                finalResult.DetectedTexts.Count, stopwatch.ElapsedMilliseconds, CalculateRoiEfficiency());
            
            return finalResult;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ ROI拡張OCR失敗");
            
            // フォールバック: ベースエンジンのみで処理
            return await _baseOcrEngine.RecognizeTextAsync(imageData, cancellationToken);
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return await _baseOcrEngine.IsAvailableAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _baseOcrEngine?.Dispose();
        _ocrSemaphore?.Dispose(); // Critical: SemaphoreSlim リソース解放
        // _roiManager?.Dispose(); // IStickyRoiManagerは IDisposable を実装していない
        _disposed = true;
        
        // 最終統計ログ
        var efficiency = CalculateRoiEfficiency();
        var hitRate = _totalRequests > 0 ? (double)_roiHits / _totalRequests : 0.0;
        
        _logger.LogInformation("📊 StickyRoiEnhancedOcrEngine統計 - " +
            "総リクエスト: {Total}, ROIヒット: {Hits}, ヒット率: {HitRate:P1}, 効率向上: {Efficiency:P1}",
            _totalRequests, _roiHits, hitRate, efficiency);
        
        _logger.LogInformation("🧹 StickyRoiEnhancedOcrEngine リソース解放完了");
    }

    private async Task<Rectangle> GetImageBoundsAsync(byte[] imageData, CancellationToken _)
    {
        try
        {
            // 簡易的な画像サイズ取得（実際の実装では画像ヘッダーを解析）
            using var stream = new System.IO.MemoryStream(imageData);
            using var image = Image.FromStream(stream);
            
            return new Rectangle(0, 0, image.Width, image.Height);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "画像境界取得失敗 - デフォルト値使用");
            return new Rectangle(0, 0, 1920, 1080); // デフォルトサイズ
        }
    }

    private async Task<Baketa.Core.Abstractions.OCR.OcrResult?> ProcessPriorityRoisAsync(
        byte[] imageData, 
        IReadOnlyList<Core.Abstractions.OCR.StickyRoi> rois, 
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("🚀 Sprint 3: ROI並列処理開始 - ROI数: {RoiCount}", rois.Count);
            
            // Improvement: 設定から読み込み（Gemini指摘事項対応 - ハードコード設定外部化完了）
            var maxParallelRois = _ocrSettings.CurrentValue.MaxParallelRois;
            
            var processingTasks = rois.Select(async roi =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var roiStopwatch = Stopwatch.StartNew();
                
                try
                {
                    // ROI領域の画像切り出し
                    var roiImageData = await ExtractRoiImageAsync(imageData, roi.Region, cancellationToken);
                    if (roiImageData == null) 
                    {
                        _logger.LogDebug("⚠️ ROI画像切り出し失敗 - ROI: {RoiId}", roi.RoiId);
                        return new RoiProcessingResult { RoiId = roi.RoiId, Success = false };
                    }
                    
                    // Critical: OCRエンジンスレッドセーフティ制御（Gemini指摘事項対応）
                    await _ocrSemaphore.WaitAsync(cancellationToken);
                    
                    Baketa.Core.Abstractions.OCR.OcrResult roiResult;
                    try
                    {
                        // ROI領域でOCR実行（同期化制御あり）
                        roiResult = await _baseOcrEngine.RecognizeTextAsync(roiImageData, cancellationToken);
                    }
                    finally
                    {
                        _ocrSemaphore.Release();
                    }
                    
                    // 座標をフルスクリーン座標に変換
                    var adjustedTexts = AdjustCoordinates(roiResult.DetectedTexts, roi.Region);
                    
                    // ROI信頼度更新（非同期並列）
                    var detectionResult = roiResult.DetectedTexts.Any() ? 
                        RoiDetectionResult.Success : RoiDetectionResult.Failed;
                    
                    var confidence = roiResult.DetectedTexts.Any() ? 
                        roiResult.DetectedTexts.Average(t => t.Confidence) : 0.0;
                    
                    // 信頼度更新を非同期で実行（パフォーマンス向上）
                    var confidenceUpdateTask = _roiManager.UpdateRoiConfidenceAsync(roi.RoiId, detectionResult, confidence, cancellationToken);
                    
                    roiStopwatch.Stop();
                    
                    _logger.LogDebug("✅ ROI並列処理完了 - ROI: {RoiId}, 検出数: {Count}, 時間: {Time}ms", 
                        roi.RoiId, adjustedTexts.Count, roiStopwatch.ElapsedMilliseconds);
                    
                    return new RoiProcessingResult 
                    { 
                        RoiId = roi.RoiId, 
                        Success = true, 
                        DetectedTexts = adjustedTexts,
                        ProcessingTime = roiStopwatch.Elapsed,
                        ConfidenceUpdateTask = confidenceUpdateTask
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ ROI個別処理失敗 - ROI: {RoiId}", roi.RoiId);
                    return new RoiProcessingResult { RoiId = roi.RoiId, Success = false };
                }
            });
            
            // 並列タスク実行・完了待機
            var results = await Task.WhenAll(processingTasks);
            
            // 信頼度更新タスクの完了待機（非ブロッキング統計更新）
            var confidenceUpdateTasks = results
                .Where(r => r.Success && r.ConfidenceUpdateTask != null)
                .Select(r => r.ConfidenceUpdateTask!)
                .ToArray();
            
            if (confidenceUpdateTasks.Any())
            {
                // 信頼度更新を非同期で完了させる（メイン処理をブロックしない）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.WhenAll(confidenceUpdateTasks);
                        _logger.LogDebug("📊 ROI信頼度更新完了 - 更新数: {Count}", confidenceUpdateTasks.Length);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ ROI信頼度更新一部失敗");
                    }
                }, cancellationToken);
            }
            
            // 成功したROI結果のマージ
            var allDetectedTexts = results
                .Where(r => r.Success && r.DetectedTexts?.Any() == true)
                .SelectMany(r => r.DetectedTexts!)
                .ToList();
            
            if (!allDetectedTexts.Any()) 
            {
                _logger.LogDebug("ℹ️ ROI並列処理完了 - 検出テキストなし");
                return null;
            }
            
            var totalProcessingTime = results.Where(r => r.Success).Sum(r => r.ProcessingTime.TotalMilliseconds);
            var successfulRois = results.Count(r => r.Success);
            
            _logger.LogInformation("🎯 Sprint 3並列ROI処理完了 - 成功: {Success}/{Total}, 検出数: {Count}, 平均時間: {AvgTime:F1}ms", 
                successfulRois, rois.Count, allDetectedTexts.Count, totalProcessingTime / Math.Max(1, successfulRois));
            
            return new Baketa.Core.Abstractions.OCR.OcrResult
            {
                DetectedTexts = allDetectedTexts.AsReadOnly(),
                ProcessingTime = TimeSpan.FromMilliseconds(totalProcessingTime), 
                IsSuccessful = true,
                Metadata = new Dictionary<string, object>
                {
                    ["ProcessingMode"] = "StickyROI_Parallel",
                    ["RoiCount"] = rois.Count,
                    ["SuccessfulRois"] = successfulRois,
                    ["DetectedRegions"] = allDetectedTexts.Count,
                    ["ParallelProcessingEnabled"] = true,
                    ["Sprint3Optimization"] = true
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ROI並列処理失敗");
            return null;
        }
    }
    
    /// <summary>
    /// Sprint 3: ROI並列処理結果
    /// </summary>
    private sealed class RoiProcessingResult
    {
        public string RoiId { get; init; } = string.Empty;
        public bool Success { get; init; }
        public List<Baketa.Core.Abstractions.OCR.DetectedText>? DetectedTexts { get; init; }
        public TimeSpan ProcessingTime { get; init; }
        public Task? ConfidenceUpdateTask { get; init; }
    }

    private async Task<byte[]?> ExtractRoiImageAsync(byte[] imageData, Rectangle roi, CancellationToken _)
    {
        try
        {
            using var stream = new System.IO.MemoryStream(imageData);
            using var sourceImage = Image.FromStream(stream);
            
            // ROI領域の境界チェック
            var clampedRoi = Rectangle.Intersect(roi, new Rectangle(0, 0, sourceImage.Width, sourceImage.Height));
            if (clampedRoi.IsEmpty || clampedRoi.Width < 10 || clampedRoi.Height < 10)
            {
                return null;
            }
            
            using var roiImage = new Bitmap(clampedRoi.Width, clampedRoi.Height);
            using var graphics = Graphics.FromImage(roiImage);
            
            graphics.DrawImage(sourceImage, 
                new Rectangle(0, 0, clampedRoi.Width, clampedRoi.Height),
                clampedRoi, 
                GraphicsUnit.Pixel);
            
            using var outputStream = new System.IO.MemoryStream();
            roiImage.Save(outputStream, System.Drawing.Imaging.ImageFormat.Png);
            
            return outputStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ROI画像切り出し失敗: {Roi}", roi);
            return null;
        }
    }

    private List<Baketa.Core.Abstractions.OCR.DetectedText> AdjustCoordinates(IReadOnlyList<Baketa.Core.Abstractions.OCR.DetectedText> texts, Rectangle roiRegion)
    {
        return [.. texts.Select(text => new Baketa.Core.Abstractions.OCR.DetectedText
        {
            Text = text.Text,
            Confidence = text.Confidence,
            BoundingBox = new Rectangle(
                text.BoundingBox.X + roiRegion.X,
                text.BoundingBox.Y + roiRegion.Y,
                text.BoundingBox.Width,
                text.BoundingBox.Height),
            Language = text.Language,
            Metadata = text.Metadata
        })];
    }

    private Baketa.Core.Abstractions.OCR.OcrResult MergeResults(Baketa.Core.Abstractions.OCR.OcrResult? roiResult, Baketa.Core.Abstractions.OCR.OcrResult fullResult)
    {
        if (roiResult == null) 
        {
            // ROIなしの場合でも、ProcessingModeを追加
            var enhancedResult = new Baketa.Core.Abstractions.OCR.OcrResult
            {
                DetectedTexts = fullResult.DetectedTexts,
                ProcessingTime = fullResult.ProcessingTime,
                IsSuccessful = fullResult.IsSuccessful,
                Metadata = fullResult.Metadata ?? []
            };
            
            if (!enhancedResult.Metadata.ContainsKey("ProcessingMode"))
            {
                enhancedResult.Metadata["ProcessingMode"] = "Full";
            }
            
            return enhancedResult;
        }
        
        // 重複除去と結果統合
        var allTexts = new List<Baketa.Core.Abstractions.OCR.DetectedText>(roiResult.DetectedTexts);
        
        foreach (var fullText in fullResult.DetectedTexts)
        {
            // 重複チェック（位置と内容）
            var isDuplicate = allTexts.Any(existing => 
                IsOverlapping(existing.BoundingBox, fullText.BoundingBox) &&
                string.Equals(existing.Text.Trim(), fullText.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            
            if (!isDuplicate)
            {
                allTexts.Add(fullText);
            }
        }
        
        return new Baketa.Core.Abstractions.OCR.OcrResult
        {
            DetectedTexts = allTexts.AsReadOnly(),
            ProcessingTime = fullResult.ProcessingTime,
            IsSuccessful = true,
            Metadata = new Dictionary<string, object>
            {
                ["ProcessingMode"] = "Hybrid",
                ["RoiDetections"] = roiResult.DetectedTexts.Count,
                ["FullDetections"] = fullResult.DetectedTexts.Count,
                ["MergedDetections"] = allTexts.Count
            }
        };
    }

    private bool IsOverlapping(Rectangle rect1, Rectangle rect2)
    {
        var intersection = Rectangle.Intersect(rect1, rect2);
        if (intersection.IsEmpty) return false;
        
        var area1 = rect1.Width * rect1.Height;
        var area2 = rect2.Width * rect2.Height;
        var intersectionArea = intersection.Width * intersection.Height;
        
        var overlapRatio1 = (double)intersectionArea / area1;
        var overlapRatio2 = (double)intersectionArea / area2;
        
        return overlapRatio1 > 0.5 || overlapRatio2 > 0.5;
    }

    private async Task UpdateRoiConfidenceAsync(
        IReadOnlyList<Core.Abstractions.OCR.StickyRoi> rois, 
        Baketa.Core.Abstractions.OCR.OcrResult result, 
        CancellationToken cancellationToken)
    {
        foreach (var roi in rois)
        {
            var roiTexts = result.DetectedTexts
                .Where(t => roi.Region.IntersectsWith(t.BoundingBox))
                .ToList();
            
            var detectionResult = roiTexts.Any() ? RoiDetectionResult.Success : RoiDetectionResult.Failed;
            var confidence = roiTexts.Any() ? roiTexts.Average(t => t.Confidence) : 0.0;
            
            await _roiManager.UpdateRoiConfidenceAsync(roi.RoiId, detectionResult, confidence, cancellationToken);
        }
    }

    private async Task RecordDetectedRegionsAsync(Baketa.Core.Abstractions.OCR.OcrResult result, CancellationToken cancellationToken)
    {
        try
        {
            var regions = result.DetectedTexts.Select(text => new TextRegion
            {
                Bounds = text.BoundingBox,
                Text = text.Text,
                Confidence = text.Confidence,
                Language = text.Language ?? "unknown"
            }).ToList();
            
            await _roiManager.RecordDetectedRegionsAsync(regions, DateTime.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ROI領域記録中に警告が発生");
        }
    }

    private double CalculateRoiEfficiency()
    {
        if (_totalRequests == 0) return 0.0;
        
        var roiAvgTime = _roiHits > 0 ? _roiProcessingTime / _roiHits : 0.0;
        var totalAvgTime = _totalProcessingTime / _totalRequests;
        
        if (totalAvgTime == 0) return 0.0;
        
        // ROI処理の効率化計算
        var hitRate = (double)_roiHits / _totalRequests;
        var speedup = roiAvgTime > 0 ? Math.Min(totalAvgTime / roiAvgTime, 10.0) : 1.0;
        
        return hitRate * (speedup - 1.0) / speedup;
    }

}
