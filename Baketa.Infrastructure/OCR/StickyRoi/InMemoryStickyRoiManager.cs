using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Settings;

namespace Baketa.Infrastructure.OCR.StickyRoi;

/// <summary>
/// インメモリスティッキーROI管理実装
/// 高速な領域記憶・検索・優先度管理システム
/// Issue #143 Week 3 Phase 1: 処理効率向上の中核実装
/// </summary>
public sealed class InMemoryStickyRoiManager : IStickyRoiManager, IDisposable
{
    private readonly ILogger<InMemoryStickyRoiManager> _logger;
    private readonly ConcurrentDictionary<string, Core.Abstractions.OCR.StickyRoi> _rois = new();
    private readonly System.Threading.Timer _cleanupTimer;
    private readonly object _statsLock = new();
    private StickyRoiSettings _settings;
    private bool _disposed = false;
    
    // 統計情報
    private long _totalDetections = 0;
    private long _successfulDetections = 0;
    private double _totalProcessingTimeMs = 0;
    private int _cleanupCount = 0;

    public InMemoryStickyRoiManager(
        ILogger<InMemoryStickyRoiManager> logger,
        IOptions<OcrSettings> ocrSettings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // デフォルト設定（OCR設定から拡張可能）
        _settings = new StickyRoiSettings();
        
        // 自動クリーンアップタイマー
        _cleanupTimer = new System.Threading.Timer(
            AutoCleanupCallback, 
            null, 
            _settings.AutoCleanupInterval, 
            _settings.AutoCleanupInterval);
        
        _logger.LogInformation("🎯 InMemoryStickyRoiManager初期化完了 - スティッキーROIシステム開始");
    }

    public async Task<RoiRecordResult> RecordDetectedRegionsAsync(
        IReadOnlyList<TextRegion> regions, 
        DateTime captureTimestamp, 
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("📝 ROI記録開始 - 領域数: {Count}", regions.Count);
            
            var newRoisAdded = 0;
            var existingRoisUpdated = 0;
            var roisMerged = 0;
            
            foreach (var region in regions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // 既存ROIとの重複・近接チェック
                var existingRoi = FindOverlappingRoi(region.Bounds);
                
                if (existingRoi != null)
                {
                    // 既存ROIを更新
                    var updated = UpdateExistingRoi(existingRoi, region, captureTimestamp);
                    if (updated)
                    {
                        existingRoisUpdated++;
                    }
                }
                else
                {
                    // 近接ROIとのマージチェック
                    var nearbyRois = FindNearbyRois(region.Bounds, _settings.MergeDistanceThreshold);
                    
                    if (nearbyRois.Any())
                    {
                        // 複数ROIをマージ
                        var mergedRoi = MergeRois(nearbyRois, region, captureTimestamp);
                        if (mergedRoi != null)
                        {
                            roisMerged++;
                        }
                    }
                    else
                    {
                        // 新規ROI作成
                        var newRoi = CreateNewRoi(region, captureTimestamp);
                        if (_rois.TryAdd(newRoi.RoiId, newRoi))
                        {
                            newRoisAdded++;
                        }
                    }
                }
            }
            
            // ROI数制限チェック
            await EnforceRoiLimitsAsync(cancellationToken);
            
            stopwatch.Stop();
            
            // 統計更新
            Interlocked.Add(ref _totalDetections, regions.Count);
            lock (_statsLock)
            {
                _totalProcessingTimeMs += stopwatch.Elapsed.TotalMilliseconds;
            }
            
            var result = new RoiRecordResult
            {
                IsSuccessful = true,
                NewRoisAdded = newRoisAdded,
                ExistingRoisUpdated = existingRoisUpdated,
                RoisMerged = roisMerged,
                ProcessingTime = stopwatch.Elapsed
            };
            
            _logger.LogInformation("✅ ROI記録完了 - 新規: {New}, 更新: {Updated}, マージ: {Merged}, 時間: {Time}ms",
                newRoisAdded, existingRoisUpdated, roisMerged, stopwatch.ElapsedMilliseconds);
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ ROI記録失敗");
            
            return new RoiRecordResult
            {
                IsSuccessful = false,
                ProcessingTime = stopwatch.Elapsed,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<IReadOnlyList<Core.Abstractions.OCR.StickyRoi>> GetPriorityRoisAsync(
        Rectangle currentScreenBounds, 
        int maxRegions = 10, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("🔍 優先ROI取得開始 - 最大数: {Max}", maxRegions);
            
            var currentTime = DateTime.UtcNow;
            var activeRois = _rois.Values
                .Where(roi => IsRoiActive(roi, currentTime) && 
                             IsRoiInBounds(roi, currentScreenBounds))
                .ToList();
            
            // 優先度とスコアリング
            var prioritizedRois = activeRois
                .Select(roi => new
                {
                    Roi = roi,
                    Score = CalculateRoiScore(roi, currentTime)
                })
                .OrderByDescending(x => x.Score)
                .Take(maxRegions)
                .Select(x => x.Roi)
                .ToList();
            
            _logger.LogDebug("✅ 優先ROI取得完了 - 取得数: {Count}/{Total}", 
                prioritizedRois.Count, activeRois.Count);
            
            return prioritizedRois.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 優先ROI取得失敗");
            return Array.Empty<Core.Abstractions.OCR.StickyRoi>();
        }
    }

    public async Task<bool> UpdateRoiConfidenceAsync(
        string roiId, 
        RoiDetectionResult detectionResult, 
        double confidence, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_rois.TryGetValue(roiId, out var roi))
            {
                _logger.LogWarning("ROI更新対象が見つかりません: {RoiId}", roiId);
                return false;
            }
            
            var currentTime = DateTime.UtcNow;
            
            switch (detectionResult)
            {
                case RoiDetectionResult.Success:
                    roi.ConfidenceScore = Math.Min(1.0, roi.ConfidenceScore * 0.8 + confidence * 0.2);
                    roi.LastDetectedAt = currentTime;
                    roi.DetectionCount++;
                    roi.ConsecutiveFailures = 0;
                    Interlocked.Increment(ref _successfulDetections);
                    break;
                    
                case RoiDetectionResult.Failed:
                    roi.ConfidenceScore *= _settings.ConfidenceDecayRate;
                    roi.ConsecutiveFailures++;
                    break;
                    
                case RoiDetectionResult.PartialSuccess:
                    roi.ConfidenceScore = Math.Min(1.0, roi.ConfidenceScore * 0.9 + confidence * 0.1);
                    roi.LastDetectedAt = currentTime;
                    roi.DetectionCount++;
                    break;
                    
                case RoiDetectionResult.RegionChanged:
                case RoiDetectionResult.TextChanged:
                    roi.ConfidenceScore = confidence;
                    roi.LastDetectedAt = currentTime;
                    roi.DetectionCount++;
                    break;
            }
            
            // 優先度自動調整
            if (_settings.EnablePriorityAdjustment)
            {
                AdjustRoiPriority(roi);
            }
            
            _logger.LogDebug("🔄 ROI信頼度更新: {RoiId} - 結果: {Result}, 信頼度: {Confidence:F3}",
                roiId, detectionResult, roi.ConfidenceScore);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ROI信頼度更新失敗: {RoiId}", roiId);
            return false;
        }
    }

    public async Task<int> CleanupExpiredRoisAsync(
        TimeSpan expirationTime, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var currentTime = DateTime.UtcNow;
            var expiredRois = _rois.Values
                .Where(roi => IsRoiExpired(roi, currentTime, expirationTime) ||
                             roi.ConsecutiveFailures >= _settings.MaxConsecutiveFailures ||
                             roi.ConfidenceScore < _settings.MinConfidenceThreshold)
                .ToList();
            
            var removedCount = 0;
            foreach (var roi in expiredRois)
            {
                if (_rois.TryRemove(roi.RoiId, out _))
                {
                    removedCount++;
                }
            }
            
            if (removedCount > 0)
            {
                Interlocked.Increment(ref _cleanupCount);
                _logger.LogInformation("🧹 期限切れROIクリーンアップ完了 - 削除数: {Count}", removedCount);
            }
            
            return removedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ROIクリーンアップ失敗");
            return 0;
        }
    }

    public async Task<RoiStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var currentTime = DateTime.UtcNow;
            var allRois = _rois.Values.ToList();
            var activeRois = allRois.Where(roi => IsRoiActive(roi, currentTime)).ToList();
            var highPriorityRois = activeRois.Where(roi => roi.Priority >= RoiPriority.High).ToList();
            
            var avgConfidence = activeRois.Any() ? activeRois.Average(r => r.ConfidenceScore) : 0.0;
            var successRate = _totalDetections > 0 ? (double)_successfulDetections / _totalDetections : 0.0;
            
            // 効率性計算（仮定: ROI使用により30%高速化）
            var efficiencyGain = activeRois.Any() ? 0.3 : 0.0;
            
            lock (_statsLock)
            {
                return new RoiStatistics
                {
                    TotalRois = allRois.Count,
                    ActiveRois = activeRois.Count,
                    HighPriorityRois = highPriorityRois.Count,
                    AverageConfidence = avgConfidence,
                    TotalDetections = _totalDetections,
                    SuccessRate = successRate,
                    EfficiencyGain = efficiencyGain,
                    LastUpdated = currentTime
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ROI統計取得失敗");
            return new RoiStatistics();
        }
    }

    public async Task<bool> UpdateSettingsAsync(
        StickyRoiSettings settings, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            
            // タイマー間隔更新
            _cleanupTimer.Change(_settings.AutoCleanupInterval, _settings.AutoCleanupInterval);
            
            _logger.LogInformation("⚙️ ROI設定更新完了 - 最大ROI数: {Max}, 有効期限: {Expiration}",
                _settings.MaxRoiCount, _settings.RoiExpirationTime);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ROI設定更新失敗");
            return false;
        }
    }

    public async Task<Core.Abstractions.OCR.StickyRoi?> AddManualRoiAsync(
        Rectangle region, 
        RoiPriority priority = RoiPriority.Normal, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var manualRoi = new Core.Abstractions.OCR.StickyRoi
            {
                Region = region,
                Priority = priority,
                Type = RoiType.Manual,
                ConfidenceScore = 1.0,
                CreatedAt = DateTime.UtcNow,
                LastDetectedAt = DateTime.UtcNow
            };
            
            if (_rois.TryAdd(manualRoi.RoiId, manualRoi))
            {
                _logger.LogInformation("➕ 手動ROI追加: {RoiId} - 領域: {Region}, 優先度: {Priority}",
                    manualRoi.RoiId, region, priority);
                
                return manualRoi;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 手動ROI追加失敗");
            return null;
        }
    }

    public async Task<bool> RemoveRoiAsync(string roiId, CancellationToken cancellationToken = default)
    {
        try
        {
            var removed = _rois.TryRemove(roiId, out var roi);
            
            if (removed && roi != null)
            {
                _logger.LogInformation("➖ ROI削除: {RoiId} - タイプ: {Type}", roiId, roi.Type);
            }
            
            return removed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ROI削除失敗: {RoiId}", roiId);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _cleanupTimer?.Dispose();
        _rois.Clear();
        _disposed = true;
        
        _logger.LogInformation("🧹 InMemoryStickyRoiManager リソース解放完了");
    }

    private Core.Abstractions.OCR.StickyRoi? FindOverlappingRoi(Rectangle bounds)
    {
        return _rois.Values
            .FirstOrDefault(roi => roi.Region.IntersectsWith(bounds) && 
                                   CalculateOverlapRatio(roi.Region, bounds) > 0.7);
    }

    private IEnumerable<Core.Abstractions.OCR.StickyRoi> FindNearbyRois(Rectangle bounds, int threshold)
    {
        return _rois.Values
            .Where(roi => CalculateDistance(roi.Region, bounds) <= threshold);
    }

    private bool UpdateExistingRoi(Core.Abstractions.OCR.StickyRoi roi, TextRegion region, DateTime timestamp)
    {
        try
        {
            roi.LastDetectedText = region.Text;
            roi.LastDetectedAt = timestamp;
            roi.DetectionCount++;
            roi.ConsecutiveFailures = 0;
            
            // 領域調整（学習的拡張）
            roi.Region = AdjustRegionBounds(roi.Region, region.Bounds);
            
            // 信頼度更新
            roi.ConfidenceScore = Math.Min(1.0, roi.ConfidenceScore * 0.8 + region.Confidence * 0.2);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ROI更新エラー: {RoiId}", roi.RoiId);
            return false;
        }
    }

    private Core.Abstractions.OCR.StickyRoi? MergeRois(IEnumerable<Core.Abstractions.OCR.StickyRoi> rois, TextRegion newRegion, DateTime timestamp)
    {
        try
        {
            var roiList = rois.ToList();
            if (!roiList.Any()) return null;
            
            // 統合領域計算
            var mergedBounds = roiList.Select(r => r.Region).Aggregate(newRegion.Bounds, Rectangle.Union);
            
            // 最高優先度を採用
            var maxPriority = roiList.Max(r => r.Priority);
            
            // 平均信頼度
            var avgConfidence = roiList.Average(r => r.ConfidenceScore);
            
            var mergedRoi = new Core.Abstractions.OCR.StickyRoi
            {
                Region = mergedBounds,
                LastDetectedText = newRegion.Text,
                ConfidenceScore = Math.Min(1.0, avgConfidence * 0.7 + newRegion.Confidence * 0.3),
                Priority = maxPriority,
                CreatedAt = roiList.Min(r => r.CreatedAt),
                LastDetectedAt = timestamp,
                DetectionCount = roiList.Sum(r => r.DetectionCount) + 1,
                Type = RoiType.Learned
            };
            
            // 古いROIを削除
            foreach (var oldRoi in roiList)
            {
                _rois.TryRemove(oldRoi.RoiId, out _);
            }
            
            // 新しいマージされたROIを追加
            _rois.TryAdd(mergedRoi.RoiId, mergedRoi);
            
            return mergedRoi;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ROIマージエラー");
            return null;
        }
    }

    private Core.Abstractions.OCR.StickyRoi CreateNewRoi(TextRegion region, DateTime timestamp)
    {
        return new Core.Abstractions.OCR.StickyRoi
        {
            Region = ExpandRegion(region.Bounds, _settings.RegionExpansionMargin),
            LastDetectedText = region.Text,
            ConfidenceScore = region.Confidence,
            Priority = RoiPriority.Normal,
            CreatedAt = timestamp,
            LastDetectedAt = timestamp,
            DetectionCount = 1,
            Type = RoiType.Automatic
        };
    }

    private async Task EnforceRoiLimitsAsync(CancellationToken cancellationToken)
    {
        if (_rois.Count <= _settings.MaxRoiCount) return;
        
        var excessCount = _rois.Count - _settings.MaxRoiCount;
        var currentTime = DateTime.UtcNow;
        
        // 最も価値の低いROIを削除
        var leastValuableRois = _rois.Values
            .OrderBy(roi => CalculateRoiScore(roi, currentTime))
            .Take(excessCount)
            .ToList();
        
        foreach (var roi in leastValuableRois)
        {
            _rois.TryRemove(roi.RoiId, out _);
        }
        
        _logger.LogDebug("🧹 ROI数制限適用 - 削除数: {Count}", excessCount);
    }

    private double CalculateRoiScore(Core.Abstractions.OCR.StickyRoi roi, DateTime currentTime)
    {
        var ageInHours = (currentTime - roi.LastDetectedAt).TotalHours;
        var recencyScore = Math.Max(0, 1.0 - (ageInHours / 24.0)); // 24時間で減衰
        
        var frequencyScore = Math.Min(1.0, roi.DetectionCount / 10.0); // 10回で最大
        var priorityScore = (int)roi.Priority / 4.0;
        var confidenceScore = roi.ConfidenceScore;
        
        // 総合スコア計算
        return recencyScore * 0.3 + frequencyScore * 0.2 + priorityScore * 0.3 + confidenceScore * 0.2;
    }

    private bool IsRoiActive(Core.Abstractions.OCR.StickyRoi roi, DateTime currentTime)
    {
        return roi.ConsecutiveFailures < _settings.MaxConsecutiveFailures &&
               roi.ConfidenceScore >= _settings.MinConfidenceThreshold &&
               !IsRoiExpired(roi, currentTime, _settings.RoiExpirationTime);
    }

    private bool IsRoiExpired(Core.Abstractions.OCR.StickyRoi roi, DateTime currentTime, TimeSpan expirationTime)
    {
        return (currentTime - roi.LastDetectedAt) > expirationTime;
    }

    private bool IsRoiInBounds(Core.Abstractions.OCR.StickyRoi roi, Rectangle bounds)
    {
        return bounds.IntersectsWith(roi.Region);
    }

    private void AdjustRoiPriority(Core.Abstractions.OCR.StickyRoi roi)
    {
        // 高頻度・高信頼度ROIの優先度向上
        if (roi.DetectionCount >= 10 && roi.ConfidenceScore >= 0.8)
        {
            roi.Priority = RoiPriority.High;
        }
        else if (roi.ConsecutiveFailures >= 2)
        {
            roi.Priority = RoiPriority.Low;
        }
    }

    private double CalculateOverlapRatio(Rectangle rect1, Rectangle rect2)
    {
        var intersection = Rectangle.Intersect(rect1, rect2);
        if (intersection.IsEmpty) return 0.0;
        
        var union = Rectangle.Union(rect1, rect2);
        return (double)(intersection.Width * intersection.Height) / (union.Width * union.Height);
    }

    private int CalculateDistance(Rectangle rect1, Rectangle rect2)
    {
        var center1 = new Point(rect1.X + rect1.Width / 2, rect1.Y + rect1.Height / 2);
        var center2 = new Point(rect2.X + rect2.Width / 2, rect2.Y + rect2.Height / 2);
        
        var dx = center1.X - center2.X;
        var dy = center1.Y - center2.Y;
        
        return (int)Math.Sqrt(dx * dx + dy * dy);
    }

    private Rectangle AdjustRegionBounds(Rectangle current, Rectangle detected)
    {
        // 学習的な領域調整（現在80%, 検出20%）
        var newX = (int)(current.X * 0.8 + detected.X * 0.2);
        var newY = (int)(current.Y * 0.8 + detected.Y * 0.2);
        var newWidth = (int)(current.Width * 0.8 + detected.Width * 0.2);
        var newHeight = (int)(current.Height * 0.8 + detected.Height * 0.2);
        
        return new Rectangle(newX, newY, newWidth, newHeight);
    }

    private Rectangle ExpandRegion(Rectangle region, int margin)
    {
        return new Rectangle(
            region.X - margin,
            region.Y - margin,
            region.Width + margin * 2,
            region.Height + margin * 2);
    }

    private void AutoCleanupCallback(object? state)
    {
        try
        {
            if (_disposed) return;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                    await CleanupExpiredRoisAsync(_settings.RoiExpirationTime, cts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "自動クリーンアップ中に警告が発生");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "自動クリーンアップタイマーでエラーが発生");
        }
    }
}