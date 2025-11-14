using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.Imaging;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Capture.DifferenceDetection;

/// <summary>
/// ハイブリッド差分検出アルゴリズム（複数アルゴリズムの組み合わせ）
/// </summary>
public class HybridDifferenceAlgorithm : IDetectionAlgorithm
{
    private readonly ILogger<HybridDifferenceAlgorithm>? _logger;
    private readonly HistogramDifferenceAlgorithm _histogramAlgorithm;
    private readonly SamplingDifferenceAlgorithm _samplingAlgorithm;
    private readonly EdgeDifferenceAlgorithm _edgeAlgorithm;

    /// <summary>
    /// アルゴリズムの種類
    /// </summary>
    public DifferenceDetectionAlgorithm AlgorithmType => DifferenceDetectionAlgorithm.Hybrid;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="histogramAlgorithm">ヒストグラムアルゴリズム</param>
    /// <param name="samplingAlgorithm">サンプリングアルゴリズム</param>
    /// <param name="edgeAlgorithm">エッジアルゴリズム</param>
    /// <param name="logger">ロガー</param>
    public HybridDifferenceAlgorithm(
        HistogramDifferenceAlgorithm histogramAlgorithm,
        SamplingDifferenceAlgorithm samplingAlgorithm,
        EdgeDifferenceAlgorithm edgeAlgorithm,
        ILogger<HybridDifferenceAlgorithm>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(histogramAlgorithm, nameof(histogramAlgorithm));
        ArgumentNullException.ThrowIfNull(samplingAlgorithm, nameof(samplingAlgorithm));
        ArgumentNullException.ThrowIfNull(edgeAlgorithm, nameof(edgeAlgorithm));

        _histogramAlgorithm = histogramAlgorithm;
        _samplingAlgorithm = samplingAlgorithm;
        _edgeAlgorithm = edgeAlgorithm;
        _logger = logger;
    }

    /// <summary>
    /// 差分を検出します
    /// </summary>
    public async Task<DetectionResult> DetectAsync(
        IImage previousImage,
        IImage currentImage,
        DifferenceDetectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previousImage, nameof(previousImage));
        ArgumentNullException.ThrowIfNull(currentImage, nameof(currentImage));
        ArgumentNullException.ThrowIfNull(settings, nameof(settings));

        try
        {
            _logger?.LogDebug("ハイブリッド差分検出を開始");

            // ステップ1: 高速なヒストグラム比較から開始
            var histogramSettings = settings.Clone();
            histogramSettings.Threshold = settings.Threshold * 0.8; // 少し感度を上げる

            var histogramResult = await _histogramAlgorithm.DetectAsync(
                previousImage, currentImage, histogramSettings, cancellationToken).ConfigureAwait(false);

            if (!histogramResult.HasSignificantChange)
            {
                _logger?.LogDebug("ヒストグラム検出: 変化なし (変化率: {ChangeRatio:P2})", histogramResult.ChangeRatio);
                return histogramResult; // 変化なしの場合はここで終了
            }

            // ステップ2: サンプリングベースで詳細に確認
            var samplingSettings = settings.Clone();

            var samplingResult = await _samplingAlgorithm.DetectAsync(
                previousImage, currentImage, samplingSettings, cancellationToken).ConfigureAwait(false);

            if (!samplingResult.HasSignificantChange)
            {
                _logger?.LogDebug("サンプリング検出: 変化なし (変化率: {ChangeRatio:P2})", samplingResult.ChangeRatio);
                return samplingResult; // 変化なしの場合はここで終了
            }

            // ステップ3: テキスト領域に特化したエッジベース検出（テキスト重視の場合のみ）
            if (settings.FocusOnTextRegions)
            {
                var edgeSettings = settings.Clone();
                edgeSettings.EdgeChangeWeight = settings.EdgeChangeWeight * 1.2; // エッジ検出の感度を上げる

                var edgeResult = await _edgeAlgorithm.DetectAsync(
                    previousImage, currentImage, edgeSettings, cancellationToken).ConfigureAwait(false);

                // 結果の統合
                var hybridResult = new DetectionResult
                {
                    HasSignificantChange = samplingResult.HasSignificantChange,
                    ChangeRatio = (samplingResult.ChangeRatio + edgeResult.ChangeRatio) / 2, // 平均
                    ChangedRegions = MergeRegions(samplingResult.ChangedRegions, edgeResult.ChangedRegions),
                    DisappearedTextRegions = edgeResult.DisappearedTextRegions
                };

                _logger?.LogDebug("ハイブリッド検出: 変化あり (変化率: {ChangeRatio:P2}, 領域数: {RegionCount})",
                    hybridResult.ChangeRatio, hybridResult.ChangedRegions.Count);

                return hybridResult;
            }
            else
            {
                _logger?.LogDebug("サンプリング検出: 変化あり (変化率: {ChangeRatio:P2}, 領域数: {RegionCount})",
                    samplingResult.ChangeRatio, samplingResult.ChangedRegions.Count);

                return samplingResult;
            }
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "ハイブリッド差分検出中に引数エラーが発生しました");

            // エラー時は変化ありとして画面全体を返す
            var regions = new List<Rectangle> { new(0, 0, currentImage.Width, currentImage.Height) };
            return new DetectionResult
            {
                HasSignificantChange = true,
                ChangeRatio = 1.0,
                ChangedRegions = regions.AsReadOnly()
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "ハイブリッド差分検出中に操作エラーが発生しました");

            var regions = new List<Rectangle> { new(0, 0, currentImage.Width, currentImage.Height) };
            return new DetectionResult
            {
                HasSignificantChange = true,
                ChangeRatio = 1.0,
                ChangedRegions = regions.AsReadOnly()
            };
        }
        catch (IOException ex)
        {
            _logger?.LogError(ex, "ハイブリッド差分検出中にIOエラーが発生しました");

            var regions = new List<Rectangle> { new(0, 0, currentImage.Width, currentImage.Height) };
            return new DetectionResult
            {
                HasSignificantChange = true,
                ChangeRatio = 1.0,
                ChangedRegions = regions.AsReadOnly()
            };
        }
        catch (OutOfMemoryException ex)
        {
            _logger?.LogError(ex, "ハイブリッド差分検出中にメモリ不足が発生しました");

            var regions = new List<Rectangle> { new(0, 0, currentImage.Width, currentImage.Height) };
            return new DetectionResult
            {
                HasSignificantChange = true,
                ChangeRatio = 1.0,
                ChangedRegions = regions.AsReadOnly()
            };
        }
    }

    /// <summary>
    /// 領域をマージします
    /// </summary>
    private IReadOnlyList<Rectangle> MergeRegions(IReadOnlyList<Rectangle> regions1, IReadOnlyList<Rectangle> regions2)
    {
        var allRegions = new List<Rectangle>();
        allRegions.AddRange(regions1);
        allRegions.AddRange(regions2);

        // 領域の重複削除・統合処理
        if (allRegions.Count <= 1)
            return allRegions;

        var mergedRegions = new List<Rectangle>();
        var processedIndices = new HashSet<int>();

        for (int i = 0; i < allRegions.Count; i++)
        {
            if (processedIndices.Contains(i))
                continue;

            var currentRegion = allRegions[i];
            processedIndices.Add(i);

            // 重なる領域をマージ
            for (int j = i + 1; j < allRegions.Count; j++)
            {
                if (processedIndices.Contains(j))
                    continue;

                var otherRegion = allRegions[j];

                // 重なりまたは近接しているか確認
                if (RegionsOverlapOrAdjacent(currentRegion, otherRegion, 20)) // 20ピクセル以内なら近接と判断
                {
                    // 領域をマージ
                    currentRegion = MergeTwoRegions(currentRegion, otherRegion);
                    processedIndices.Add(j);
                }
            }

            mergedRegions.Add(currentRegion);
        }

        return mergedRegions.AsReadOnly();
    }

    /// <summary>
    /// 2つの領域が重なるまたは近接しているかを確認します
    /// </summary>
    private bool RegionsOverlapOrAdjacent(Rectangle r1, Rectangle r2, int proximityThreshold)
    {
        // 拡張した領域を作成
        var expandedR1 = new Rectangle(
            r1.X - proximityThreshold,
            r1.Y - proximityThreshold,
            r1.Width + proximityThreshold * 2,
            r1.Height + proximityThreshold * 2);

        return expandedR1.IntersectsWith(r2);
    }

    /// <summary>
    /// 2つの領域をマージします
    /// </summary>
    private Rectangle MergeTwoRegions(Rectangle r1, Rectangle r2)
    {
        int x = Math.Min(r1.X, r2.X);
        int y = Math.Min(r1.Y, r2.Y);
        int right = Math.Max(r1.Right, r2.Right);
        int bottom = Math.Max(r1.Bottom, r2.Bottom);

        return new Rectangle(x, y, right - x, bottom - y);
    }
}
