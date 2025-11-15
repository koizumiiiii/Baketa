using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.OCR.TextDetection;
using Microsoft.Extensions.Logging;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;

namespace Baketa.Infrastructure.OCR.TextDetection;

/// <summary>
/// テキスト領域集約実装クラス
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="logger">ロガー</param>
public class TextRegionAggregator(ILogger<TextRegionAggregator>? logger = null) : ITextRegionAggregator
{
    private readonly ILogger<TextRegionAggregator>? _logger = logger;
    private readonly List<OCRTextRegion> _previousRegions = [];

    /// <summary>
    /// 複数の検出結果を統合します
    /// </summary>
    /// <param name="detectionResults">各検出器からの結果</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>統合された検出結果</returns>
    public async Task<IReadOnlyList<OCRTextRegion>> AggregateResultsAsync(
            IEnumerable<IReadOnlyList<OCRTextRegion>> detectionResults,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(detectionResults, nameof(detectionResults));

        List<OCRTextRegion> allRegions = [.. detectionResults
                .Where(result => result != null)
                .SelectMany(result => result)];

        if (allRegions.Count == 0)
        {
            _logger?.LogInformation("集約する検出結果がありません");
            return [];
        }

        _logger?.LogDebug("テキスト領域集約を開始 (合計: {TotalCount}個の領域)", allRegions.Count);

        try
        {
            // 重複領域のマージを実行
            var mergedRegions = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 重複領域をマージ
                return MergeOverlappingRegions(allRegions, 0.5f);

            }, cancellationToken).ConfigureAwait(false);

            // スコアリングを実行
            var scoredRegions = await ScoreRegionsAsync(mergedRegions, cancellationToken).ConfigureAwait(false);

            // 前フレームとの時間的追跡を実行
            var trackedRegions = await TrackRegionsAsync(scoredRegions, _previousRegions, cancellationToken).ConfigureAwait(false);

            // 結果をキャッシュ（次回フレーム用）
            _previousRegions.Clear();
            _previousRegions.AddRange(trackedRegions);

            _logger?.LogDebug("テキスト領域集約が完了 (統合後: {MergedCount}個の領域)", trackedRegions.Count);

            return trackedRegions;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("テキスト領域集約がキャンセルされました");
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "テキスト領域集約中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// 時間的な追跡を適用します
    /// </summary>
    /// <param name="currentRegions">現在のフレームの検出結果</param>
    /// <param name="previousRegions">前のフレームの検出結果</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>追跡情報が更新された検出結果</returns>
    public async Task<IReadOnlyList<OCRTextRegion>> TrackRegionsAsync(
        IReadOnlyList<OCRTextRegion> currentRegions,
        IReadOnlyList<OCRTextRegion> previousRegions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentRegions, nameof(currentRegions));

        if (previousRegions == null || previousRegions.Count == 0)
        {
            return currentRegions;
        }

        _logger?.LogDebug("テキスト領域追跡を開始 (現在: {CurrentCount}, 前フレーム: {PreviousCount})",
            currentRegions.Count, previousRegions.Count);

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                return currentRegions.Select(currentRegion =>
                {
                    // 前フレームの対応する領域を検索
                    OCRTextRegion? matchedPrevRegion = null;
                    float maxIoU = 0.3f; // 閾値

                    foreach (var prevRegion in previousRegions)
                    {
                        // IoU (Intersection over Union) を計算
                        var intersection = Rectangle.Intersect(currentRegion.Bounds, prevRegion.Bounds);
                        if (intersection.IsEmpty)
                            continue;

                        float intersectionArea = intersection.Width * intersection.Height;
                        float currentArea = currentRegion.Bounds.Width * currentRegion.Bounds.Height;
                        float prevArea = prevRegion.Bounds.Width * prevRegion.Bounds.Height;
                        float unionArea = currentArea + prevArea - intersectionArea;

                        float iou = intersectionArea / unionArea;

                        if (iou > maxIoU)
                        {
                            maxIoU = iou;
                            matchedPrevRegion = prevRegion;
                        }
                    }

                    // 対応する領域が見つかった場合、情報を継承
                    if (matchedPrevRegion != null)
                    {
                        // 新しい領域を作成し、元の領域の信頼度を補正
                        float newConfidence = currentRegion.ConfidenceScore * 0.7f + matchedPrevRegion.ConfidenceScore * 0.3f;

                        var trackedRegion = new OCRTextRegion(
                            currentRegion.Bounds,
                            newConfidence,
                            currentRegion.RegionType)
                        {
                            Contour = currentRegion.Contour,
                            ProcessedImage = currentRegion.ProcessedImage
                        };

                        // 一時的な消失を許容するためのメタデータ
                        trackedRegion.Metadata["TrackingFrameCount"] =
                            matchedPrevRegion.Metadata.TryGetValue("TrackingFrameCount", out var frameCount) ?
                            (int)frameCount + 1 : 1;

                        return trackedRegion;
                    }
                    else
                    {
                        // 新規領域
                        currentRegion.Metadata["TrackingFrameCount"] = 1;
                        return currentRegion;
                    }
                }).ToList();

            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("テキスト領域追跡がキャンセルされました");
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "テキスト領域追跡中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// 検出されたテキスト領域をスコアリングします
    /// </summary>
    /// <param name="regions">評価する領域</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>スコアリング済みの領域</returns>
    public async Task<IReadOnlyList<OCRTextRegion>> ScoreRegionsAsync(
        IReadOnlyList<OCRTextRegion> regions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(regions, nameof(regions));

        if (regions.Count == 0)
        {
            return [];
        }

        _logger?.LogDebug("テキスト領域スコアリングを開始 (対象: {RegionCount}個の領域)", regions.Count);

        try
        {
            return await Task.Run<IReadOnlyList<OCRTextRegion>>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Selectの結果を一時変数に格納
                var regionResults = regions.Select(region =>
                {
                    // 基本スコア（すでに設定されたもの）
                    float baseScore = region.ConfidenceScore;

                    // テキスト特性スコア（テキストらしさ）
                    float textFeatureScore = CalculateTextFeatureScore(region);

                    // 空間配置スコア（整列など）
                    float spatialScore = CalculateSpatialScore(region, regions);

                    // 最終スコアの計算（重み付け）
                    float finalScore = baseScore * 0.5f + textFeatureScore * 0.3f + spatialScore * 0.2f;

                    // 新しいスコアリング済み領域を作成
                    var scoredRegion = new OCRTextRegion(
                        region.Bounds,
                        finalScore,
                        region.RegionType)
                    {
                        Contour = region.Contour,
                        ProcessedImage = region.ProcessedImage
                    };

                    // メタデータの継承
                    foreach (var item in region.Metadata)
                    {
                        scoredRegion.Metadata[item.Key] = item.Value;
                    }

                    // スコアリング詳細の追加
                    scoredRegion.Metadata["BaseScore"] = baseScore;
                    scoredRegion.Metadata["TextFeatureScore"] = textFeatureScore;
                    scoredRegion.Metadata["SpatialScore"] = spatialScore;

                    return scoredRegion;
                });

                // コレクション式を使用して結果を返す
                return [.. regionResults];

            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("テキスト領域スコアリングがキャンセルされました");
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "テキスト領域スコアリング中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// テキスト特性スコアを計算します
    /// </summary>
    /// <param name="region">評価する領域</param>
    /// <returns>テキスト特性スコア（0.0～1.0）</returns>
    private static float CalculateTextFeatureScore(OCRTextRegion region)
    {
        // アスペクト比の評価
        float aspectRatio = region.Bounds.Width / (float)region.Bounds.Height;
        float aspectScore = 0.0f;

        if (aspectRatio >= 0.2f && aspectRatio <= 20.0f)
        {
            // 横長のテキストに高いスコアの計算を三項演算子で実装
            aspectScore = aspectRatio > 1.0f && aspectRatio < 10.0f ?
                1.0f - (Math.Min(aspectRatio - 1.0f, 9.0f) / 9.0f) * 0.5f :
                aspectRatio <= 1.0f && aspectRatio >= 0.2f ?
                    0.5f + (aspectRatio - 0.2f) / (1.0f - 0.2f) * 0.5f :
                    0.5f;
        }

        // サイズ評価
        float area = region.Bounds.Width * region.Bounds.Height;
        float sizeScore = Math.Min(area / 50000.0f, 1.0f) * 0.8f;

        // 輪郭の複雑さ評価
        float complexityScore = 0.6f; // デフォルト値

        if (region.Contour != null)
        {
            // 輪郭の長さと面積の比率から複雑さを評価
            // ディスカード変数を使用してパフォーマンスを維持
            _ = region.Contour.Count; // 輪郭の長さ
            float perimeterAreaRatio = region.Contour.Count / Math.Max(area, 1.0f);

            complexityScore = Math.Max(0.0f, 1.0f - Math.Min(perimeterAreaRatio * 0.1f, 1.0f));
        }

        // 総合スコア
        return aspectScore * 0.4f + sizeScore * 0.3f + complexityScore * 0.3f;
    }

    /// <summary>
    /// 空間配置スコアを計算します
    /// </summary>
    /// <param name="region">評価する領域</param>
    /// <param name="allRegions">すべての領域</param>
    /// <returns>空間配置スコア（0.0～1.0）</returns>
    private static float CalculateSpatialScore(OCRTextRegion region, IReadOnlyList<OCRTextRegion> allRegions)
    {
        return allRegions.Count <= 1 ? 0.5f : CalculateSpatialScoreInternal(region, allRegions); // デフォルト値または計算値

    }

    /// <summary>
    /// 空間配置スコアの内部計算メソッド
    /// </summary>
    private static float CalculateSpatialScoreInternal(OCRTextRegion region, IReadOnlyList<OCRTextRegion> allRegions)
    {
        // 水平方向の整列をカウントする変数
        int alignedCount = 0;
        int closeCount = 0;

        foreach (var other in allRegions)
        {
            if (other == region)
                continue;

            // 水平方向の整列をチェック
            int thisMiddleY = region.Bounds.Y + region.Bounds.Height / 2;
            int otherMiddleY = other.Bounds.Y + other.Bounds.Height / 2;
            int yDiff = Math.Abs(thisMiddleY - otherMiddleY);

            // 高さの10%以内ならば整列していると判断
            if (yDiff <= Math.Max(region.Bounds.Height, other.Bounds.Height) * 0.1f)
            {
                alignedCount++;
            }

            // 近接性をチェック
            int xDist = Math.Min(
                Math.Abs(region.Bounds.Right - other.Bounds.X),
                Math.Abs(other.Bounds.Right - region.Bounds.X));

            // 幅の50%以内ならば近接していると判断
            if (xDist <= Math.Max(region.Bounds.Width, other.Bounds.Width) * 0.5f)
            {
                closeCount++;
            }
        }

        // 各スコアを計算して総合スコアを返す
        float alignmentScore = Math.Min(alignedCount / (float)Math.Max(allRegions.Count - 1, 1), 1.0f);
        float proximityScore = Math.Min(closeCount / (float)Math.Max(allRegions.Count - 1, 1), 1.0f);
        return alignmentScore * 0.6f + proximityScore * 0.4f;
    }

    /// <summary>
    /// 重複する領域をマージします
    /// </summary>
    /// <param name="regions">マージ前の領域リスト</param>
    /// <param name="overlapThreshold">重複と判定する閾値（0.0～1.0）</param>
    /// <returns>マージ後の領域リスト</returns>
    private static List<OCRTextRegion> MergeOverlappingRegions(List<OCRTextRegion> regions, float overlapThreshold)
    {
        if (regions.Count <= 1)
            return regions;

        // 結果用リスト
        List<OCRTextRegion> mergedRegions = [];
        // 処理済みフラグ
        var processed = new bool[regions.Count];

        for (int i = 0; i < regions.Count; i++)
        {
            // 既に処理済みならスキップ
            if (processed[i])
                continue;

            var currentRegion = regions[i];
            var currentBounds = currentRegion.Bounds;
            List<Point>? mergedContour = currentRegion.Contour?.ToList();
            float maxScore = currentRegion.ConfidenceScore;

            bool merged = false;

            // 他の未処理領域と比較
            for (int j = i + 1; j < regions.Count; j++)
            {
                if (processed[j])
                    continue;

                var compareRegion = regions[j];

                // 重複判定
                if (currentRegion.Overlaps(compareRegion, overlapThreshold))
                {
                    // 新しいバウンディングボックスを計算
                    var x = Math.Min(currentBounds.X, compareRegion.Bounds.X);
                    var y = Math.Min(currentBounds.Y, compareRegion.Bounds.Y);
                    var right = Math.Max(currentBounds.Right, compareRegion.Bounds.Right);
                    var bottom = Math.Max(currentBounds.Bottom, compareRegion.Bounds.Bottom);

                    currentBounds = new Rectangle(x, y, right - x, bottom - y);

                    // 輪郭の統合
                    if (mergedContour != null && compareRegion.Contour != null)
                    {
                        mergedContour.AddRange(compareRegion.Contour);
                    }

                    // 最大スコアを更新
                    maxScore = Math.Max(maxScore, compareRegion.ConfidenceScore);

                    // 処理済みとしてマーク
                    processed[j] = true;
                    merged = true;
                }
            }

            // マージされた新しい領域またはオリジナルの領域を追加
            if (merged)
            {
                var mergedRegion = new OCRTextRegion(currentBounds, maxScore, currentRegion.RegionType);
                if (mergedContour != null)
                {
                    mergedRegion.Contour = [.. mergedContour];
                }
                mergedRegions.Add(mergedRegion);
            }
            else
            {
                mergedRegions.Add(currentRegion);
            }

            // 現在の領域を処理済みとしてマーク
            processed[i] = true;
        }

        return mergedRegions;
    }
}
