using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Baketa.Core.Abstractions.Roi;
using Baketa.Core.Models.Roi;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Roi.Services;

/// <summary>
/// ROI学習エンジンの実装
/// </summary>
/// <remarks>
/// テキスト検出パターンを学習し、ヒートマップと高信頼度ROI領域を生成します。
/// スレッドセーフな実装で、並行アクセスをサポートします。
/// </remarks>
public sealed class RoiLearningEngine : IRoiLearningEngine
{
    private readonly ILogger<RoiLearningEngine> _logger;
    private readonly RoiManagerSettings _settings;
    private readonly object _lock = new();

    private RoiHeatmapData _heatmap;
    private long _positiveSamples;
    private long _negativeSamples;
    private DateTime? _learningStartedAt;
    private DateTime? _lastLearningAt;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public RoiLearningEngine(
        ILogger<RoiLearningEngine> logger,
        IOptions<RoiManagerSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? RoiManagerSettings.CreateDefault();

        // 初期ヒートマップを作成
        _heatmap = RoiHeatmapData.Create(_settings.HeatmapRows, _settings.HeatmapColumns);

        _logger.LogDebug(
            "RoiLearningEngine initialized: Rows={Rows}, Columns={Columns}, LearningRate={LearningRate}",
            _settings.HeatmapRows, _settings.HeatmapColumns, _settings.LearningRate);
    }

    /// <inheritdoc />
    public bool IsLearningEnabled { get; set; } = true;

    /// <inheritdoc />
    public RoiHeatmapData? CurrentHeatmap
    {
        get
        {
            lock (_lock)
            {
                return _heatmap;
            }
        }
    }

    /// <inheritdoc />
    public void RecordDetection(NormalizedRect normalizedBounds, float confidence, int weight = 1)
    {
        if (!normalizedBounds.IsValid())
        {
            _logger.LogWarning("Invalid normalized bounds received: {Bounds}", normalizedBounds);
            return;
        }

        lock (_lock)
        {
            // スレッドセーフティ: ロック内でフラグをチェック
            if (!IsLearningEnabled)
            {
                return;
            }

            _learningStartedAt ??= DateTime.UtcNow;
            _lastLearningAt = DateTime.UtcNow;

            // 領域内の全セルを更新（重み付き）
            var detectedCells = GetCellsInRegion(normalizedBounds);
            if (weight > 1)
            {
                // [Issue #354] 重み付き学習
                var weightedCells = detectedCells.Select(c => (c.row, c.column, weight)).ToArray();
                _heatmap = _heatmap.WithUpdatedCellsWeighted(weightedCells, _settings.LearningRate);
            }
            else
            {
                _heatmap = _heatmap.WithUpdatedCells(detectedCells, _settings.LearningRate);
            }

            // [Issue #379] P2-2: ヒットカウント記録（Miss比率計算用）
            _heatmap = _heatmap.WithRecordedHit(detectedCells);
            _positiveSamples++;

            _logger.LogTrace(
                "[Issue #354] Recorded detection: Bounds={Bounds}, Confidence={Confidence}, Weight={Weight}, Cells={CellCount}",
                normalizedBounds, confidence, weight, detectedCells.Length);
        }
    }

    /// <inheritdoc />
    public void RecordDetections(IEnumerable<(NormalizedRect bounds, float confidence)> detections)
    {
        var detectionList = detections.ToList();
        if (detectionList.Count == 0)
        {
            return;
        }

        lock (_lock)
        {
            // スレッドセーフティ: ロック内でフラグをチェック
            if (!IsLearningEnabled)
            {
                return;
            }

            _learningStartedAt ??= DateTime.UtcNow;
            _lastLearningAt = DateTime.UtcNow;

            // 全検出領域のセルを収集
            var allCells = new HashSet<(int row, int column)>();
            foreach (var (bounds, _) in detectionList)
            {
                if (bounds.IsValid())
                {
                    foreach (var cell in GetCellsInRegion(bounds))
                    {
                        allCells.Add(cell);
                    }
                }
            }

            _heatmap = _heatmap.WithUpdatedCells([.. allCells], _settings.LearningRate);

            // [Issue #379] P2-2: ヒットカウント記録（Miss比率計算用）
            _heatmap = _heatmap.WithRecordedHit([.. allCells]);
            _positiveSamples += detectionList.Count;

            _logger.LogTrace(
                "Recorded {Count} detections, {CellCount} unique cells updated",
                detectionList.Count, allCells.Count);
        }
    }

    /// <inheritdoc />
    public void RecordDetectionsWithWeight(IEnumerable<(NormalizedRect bounds, float confidence, int weight)> detections)
    {
        var detectionList = detections.ToList();
        if (detectionList.Count == 0)
        {
            return;
        }

        lock (_lock)
        {
            // スレッドセーフティ: ロック内でフラグをチェック
            if (!IsLearningEnabled)
            {
                return;
            }

            _learningStartedAt ??= DateTime.UtcNow;
            _lastLearningAt = DateTime.UtcNow;

            // [Issue #354] 全検出領域のセルを重み付きで収集
            var allCellsWithWeight = new List<(int row, int column, int weight)>();
            foreach (var (bounds, _, weight) in detectionList)
            {
                if (bounds.IsValid())
                {
                    foreach (var cell in GetCellsInRegion(bounds))
                    {
                        allCellsWithWeight.Add((cell.row, cell.column, weight));
                    }
                }
            }

            _heatmap = _heatmap.WithUpdatedCellsWeighted([.. allCellsWithWeight], _settings.LearningRate);

            // [Issue #379] P2-2: ヒットカウント記録（Miss比率計算用）
            var uniqueHitCells = allCellsWithWeight.Select(c => (c.row, c.column)).Distinct().ToArray();
            _heatmap = _heatmap.WithRecordedHit(uniqueHitCells);
            _positiveSamples += detectionList.Count;

            _logger.LogTrace(
                "[Issue #354] Recorded {Count} weighted detections, {CellCount} cells updated",
                detectionList.Count, allCellsWithWeight.Count);
        }
    }

    /// <inheritdoc />
    public void RecordNoDetection(NormalizedRect normalizedBounds)
    {
        if (!normalizedBounds.IsValid())
        {
            return;
        }

        lock (_lock)
        {
            // スレッドセーフティ: ロック内でフラグをチェック
            if (!IsLearningEnabled)
            {
                return;
            }

            _learningStartedAt ??= DateTime.UtcNow;
            _lastLearningAt = DateTime.UtcNow;

            // 空の検出セット（全セルが減衰）
            _heatmap = _heatmap.WithUpdatedCells([], _settings.LearningRate);
            _negativeSamples++;

            _logger.LogTrace("Recorded no detection for bounds: {Bounds}", normalizedBounds);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<NormalizedRect> RecordMiss(NormalizedRect normalizedBounds)
    {
        var exclusionCandidates = new List<NormalizedRect>();

        if (!normalizedBounds.IsValid())
        {
            _logger.LogWarning("[Issue #354] Invalid normalized bounds for miss: {Bounds}", normalizedBounds);
            return exclusionCandidates;
        }

        lock (_lock)
        {
            if (!IsLearningEnabled)
            {
                return exclusionCandidates;
            }

            _learningStartedAt ??= DateTime.UtcNow;
            _lastLearningAt = DateTime.UtcNow;

            // 領域内のセルを取得
            var missCells = GetCellsInRegion(normalizedBounds);

            // miss記録を適用（スコアリセット閾値を使用）
            _heatmap = _heatmap.WithRecordedMiss(
                missCells,
                _settings.ConsecutiveMissThresholdForReset);
            _negativeSamples++;

            // [Issue #379] P2-2: Miss比率ベースの除外候補生成
            var highMissRatioCells = _heatmap.GetHighMissRatioCells(
                _settings.MissRatioThresholdForExclusion,
                _settings.MinSamplesForMissRatio);
            if (highMissRatioCells.Length > 0)
            {
                var groups = GroupConnectedCells(highMissRatioCells.Select(c => (c.row, c.column, c.missRatio)).ToArray());
                foreach (var group in groups)
                {
                    if (group.Count > 0)
                    {
                        var region = CreateRegionFromCells(group);
                        exclusionCandidates.Add(region.NormalizedBounds);

                        _logger.LogInformation(
                            "[Issue #379] 比率ベース自動除外候補: Bounds={Bounds}, MissRatioCells={Count}",
                            region.NormalizedBounds, group.Count);
                    }
                }
            }

            // [Issue #354] 連続Miss数ベースの除外候補生成（フォールバック）
            var highMissCells = _heatmap.GetHighMissCells(_settings.ConsecutiveMissThresholdForExclusion);
            if (highMissCells.Length > 0)
            {
                var groups = GroupConnectedCells(highMissCells.Select(c => (c.row, c.column, 1.0f)).ToArray());
                foreach (var group in groups)
                {
                    if (group.Count > 0)
                    {
                        var region = CreateRegionFromCells(group);
                        // 既に比率ベースで追加済みの場合はスキップ
                        if (!exclusionCandidates.Any(c => c.CalculateIoU(region.NormalizedBounds) >= 0.5f))
                        {
                            exclusionCandidates.Add(region.NormalizedBounds);

                            _logger.LogInformation(
                                "[Issue #354] 連続Missベース自動除外候補: Bounds={Bounds}, MissCells={Count}",
                                region.NormalizedBounds, group.Count);
                        }
                    }
                }
            }

            // [Issue #379] P2-1: セーフゾーン保護
            // 高い検出回数を持つ学習済み領域は除外候補から除去
            if (exclusionCandidates.Count > 0)
            {
                var safeRegions = GenerateRegions(_settings.MinConfidenceForRegion, _settings.MinConfidenceForRegion);
                exclusionCandidates.RemoveAll(candidate =>
                {
                    var isProtected = safeRegions.Any(r =>
                        r.DetectionCount >= _settings.SafeZoneMinDetectionCount &&
                        r.NormalizedBounds.CalculateIoU(candidate) >= _settings.SafeZoneOverlapIoUThreshold);

                    if (isProtected)
                    {
                        _logger.LogInformation(
                            "[Issue #379] セーフゾーン保護により除外候補を除去: {Bounds}",
                            candidate);
                    }

                    return isProtected;
                });
            }

            _logger.LogTrace(
                "[Issue #354] Recorded miss: Bounds={Bounds}, Cells={CellCount}, ExclusionCandidates={ExclusionCount}",
                normalizedBounds, missCells.Length, exclusionCandidates.Count);
        }

        return exclusionCandidates;
    }

    /// <inheritdoc />
    public IReadOnlyList<RoiRegion> GenerateRegions(float minConfidence = 0.5f, float minHeatmapValue = 0.3f)
    {
        lock (_lock)
        {
            var regions = new List<RoiRegion>();
            var highValueCells = _heatmap.GetHighValueCells(minHeatmapValue);

            if (highValueCells.Length == 0)
            {
                _logger.LogDebug("No high value cells found for region generation");
                return regions;
            }

            // 連結セルをグループ化してROI領域を生成
            var groups = GroupConnectedCells(highValueCells);

            foreach (var group in groups)
            {
                if (group.Count == 0)
                {
                    continue;
                }

                var region = CreateRegionFromCells(group);
                if (region.NormalizedBounds.Area >= _settings.MinRegionSize)
                {
                    regions.Add(region);
                }
            }

            // 最大数を超える場合は信頼度順にソートして上位を返す
            if (regions.Count > _settings.MaxRegionsPerProfile)
            {
                regions = [.. regions.OrderByDescending(r => r.ConfidenceScore).Take(_settings.MaxRegionsPerProfile)];
            }

            _logger.LogInformation(
                "Generated {Count} ROI regions from {CellCount} high value cells",
                regions.Count, highValueCells.Length);

            return regions;
        }
    }

    /// <inheritdoc />
    public void ApplyDecay()
    {
        lock (_lock)
        {
            _heatmap = _heatmap.WithDecay(_settings.DecayRate);
            _logger.LogTrace("Applied decay with rate {DecayRate}", _settings.DecayRate);
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        lock (_lock)
        {
            _heatmap = RoiHeatmapData.Create(_settings.HeatmapRows, _settings.HeatmapColumns);
            _positiveSamples = 0;
            _negativeSamples = 0;
            _learningStartedAt = null;
            _lastLearningAt = null;

            _logger.LogInformation("Learning engine reset");
        }
    }

    /// <inheritdoc />
    public float GetHeatmapValueAt(float normalizedX, float normalizedY)
    {
        lock (_lock)
        {
            return _heatmap.GetValueAt(normalizedX, normalizedY);
        }
    }

    /// <inheritdoc />
    public RoiHeatmapData ExportHeatmap()
    {
        lock (_lock)
        {
            return _heatmap;
        }
    }

    /// <inheritdoc />
    public void ImportHeatmap(RoiHeatmapData heatmapData)
    {
        ArgumentNullException.ThrowIfNull(heatmapData);

        if (!heatmapData.IsValid())
        {
            throw new ArgumentException("Invalid heatmap data", nameof(heatmapData));
        }

        lock (_lock)
        {
            _heatmap = heatmapData;
            _learningStartedAt = DateTime.UtcNow;
            _lastLearningAt = heatmapData.LastUpdatedAt;

            _logger.LogInformation(
                "Imported heatmap: Rows={Rows}, Columns={Columns}, TotalSamples={TotalSamples}",
                heatmapData.Rows, heatmapData.Columns, heatmapData.TotalSamples);
        }
    }

    /// <inheritdoc />
    public RoiLearningStatistics GetStatistics()
    {
        lock (_lock)
        {
            var highValueCells = _heatmap.GetHighValueCells(_settings.MinConfidenceForRegion);
            var allValues = _heatmap.Values;
            var averageValue = allValues.Length > 0 ? allValues.Average() : 0.0f;
            var maxValue = allValues.Length > 0 ? allValues.Max() : 0.0f;

            return new RoiLearningStatistics
            {
                TotalSamples = _positiveSamples + _negativeSamples,
                PositiveSamples = _positiveSamples,
                NegativeSamples = _negativeSamples,
                HighValueCellCount = highValueCells.Length,
                AverageHeatmapValue = averageValue,
                MaxHeatmapValue = maxValue,
                LastLearningAt = _lastLearningAt,
                LearningStartedAt = _learningStartedAt
            };
        }
    }

    /// <summary>
    /// 指定した正規化矩形に含まれるセルのインデックスを取得
    /// </summary>
    private (int row, int column)[] GetCellsInRegion(NormalizedRect bounds)
    {
        var (startRow, startColumn) = _heatmap.GetCellIndex(bounds.X, bounds.Y);
        var (endRow, endColumn) = _heatmap.GetCellIndex(
            Math.Min(bounds.Right, 0.9999f),
            Math.Min(bounds.Bottom, 0.9999f));

        var cells = new List<(int, int)>();
        for (var row = startRow; row <= endRow; row++)
        {
            for (var column = startColumn; column <= endColumn; column++)
            {
                cells.Add((row, column));
            }
        }

        return [.. cells];
    }

    /// <summary>
    /// 連結セルをグループ化
    /// </summary>
    private static List<List<(int row, int column, float value)>> GroupConnectedCells(
        (int row, int column, float value)[] cells)
    {
        var groups = new List<List<(int row, int column, float value)>>();
        var visited = new HashSet<(int, int)>();
        var cellSet = cells.ToDictionary(c => (c.row, c.column), c => c.value);

        foreach (var cell in cells)
        {
            if (visited.Contains((cell.row, cell.column)))
            {
                continue;
            }

            var group = new List<(int row, int column, float value)>();
            var queue = new Queue<(int row, int column)>();
            queue.Enqueue((cell.row, cell.column));

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (visited.Contains(current))
                {
                    continue;
                }

                visited.Add(current);
                if (cellSet.TryGetValue(current, out var value))
                {
                    group.Add((current.row, current.column, value));

                    // 8方向の隣接セルをチェック
                    for (var dr = -1; dr <= 1; dr++)
                    {
                        for (var dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc == 0)
                            {
                                continue;
                            }

                            var neighbor = (current.row + dr, current.column + dc);
                            if (!visited.Contains(neighbor) && cellSet.ContainsKey(neighbor))
                            {
                                queue.Enqueue(neighbor);
                            }
                        }
                    }
                }
            }

            if (group.Count > 0)
            {
                groups.Add(group);
            }
        }

        return groups;
    }

    /// <summary>
    /// セルグループからROI領域を作成
    /// </summary>
    private RoiRegion CreateRegionFromCells(List<(int row, int column, float value)> cells)
    {
        var minRow = cells.Min(c => c.row);
        var maxRow = cells.Max(c => c.row);
        var minColumn = cells.Min(c => c.column);
        var maxColumn = cells.Max(c => c.column);
        var avgValue = cells.Average(c => c.value);

        var cellWidth = 1.0f / _heatmap.Columns;
        var cellHeight = 1.0f / _heatmap.Rows;

        var bounds = new NormalizedRect(
            minColumn * cellWidth,
            minRow * cellHeight,
            (maxColumn - minColumn + 1) * cellWidth,
            (maxRow - minRow + 1) * cellHeight
        );

        var confidenceLevel = avgValue >= _settings.HighConfidenceThreshold
            ? RoiConfidenceLevel.High
            : avgValue >= _settings.MinConfidenceForRegion
                ? RoiConfidenceLevel.Medium
                : RoiConfidenceLevel.Low;

        return new RoiRegion
        {
            Id = $"roi_{Guid.NewGuid():N}"[..16],
            NormalizedBounds = bounds,
            RegionType = RoiRegionType.Text,
            ConfidenceLevel = confidenceLevel,
            ConfidenceScore = avgValue,
            HeatmapValue = avgValue,
            DetectionCount = cells.Sum(c => _heatmap.GetSampleCount(c.row, c.column)),
            CreatedAt = DateTime.UtcNow,
            LastDetectedAt = DateTime.UtcNow
        };
    }
}
