using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Extensions;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Capture.DifferenceDetection;

/// <summary>
/// ブロックベースの差分検出アルゴリズム
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="logger">ロガー</param>
public class BlockDifferenceAlgorithm(ILogger<BlockDifferenceAlgorithm>? logger = null) : IDetectionAlgorithm
{
    private readonly ILogger<BlockDifferenceAlgorithm>? _logger = logger;

    /// <summary>
    /// アルゴリズムの種類
    /// </summary>
    public DifferenceDetectionAlgorithm AlgorithmType => DifferenceDetectionAlgorithm.BlockBased;

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
            int width = previousImage.Width;
            int height = previousImage.Height;
            int blockSize = settings.BlockSize;

            // ブロック数の計算
            int blocksX = (width + blockSize - 1) / blockSize;
            int blocksY = (height + blockSize - 1) / blockSize;
            int totalBlocks = blocksX * blocksY;

            // 前回と現在の画像からピクセルデータを取得
            byte[] prevPixels = await previousImage.GetPixelsAsync(0, 0, width, height).ConfigureAwait(false);
            byte[] currPixels = await currentImage.GetPixelsAsync(0, 0, width, height).ConfigureAwait(false);

            int bytesPerPixel = 3; // RGB想定
            int stride = width * bytesPerPixel;

            // 差分のあるブロックを追跡
            List<Rectangle> changedBlocks = [];
            int changedBlockCount = 0;

            // 各ブロックを処理
            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // ブロックの範囲を計算
                    int startX = bx * blockSize;
                    int startY = by * blockSize;
                    int endX = Math.Min(startX + blockSize, width);
                    int endY = Math.Min(startY + blockSize, height);

                    double blockDifference = CalculateBlockDifference(
                        prevPixels, currPixels, startX, startY, endX, endY, width, bytesPerPixel);

                    // 閾値を超える差分があればブロックに変化ありと判定
                    if (blockDifference > settings.Threshold)
                    {
                        changedBlockCount++;
                        changedBlocks.Add(new Rectangle(startX, startY, endX - startX, endY - startY));

                        // 早期終了条件：十分な数のブロックに変化がある場合
                        if ((double)changedBlockCount / totalBlocks > 0.3) // 全体の30%
                        {
                            _logger?.LogDebug("早期終了: ブロックの {PercentChanged:P2} が変更されています",
                                (double)changedBlockCount / totalBlocks);
                            break;
                        }
                    }
                }
            }

            // 変化率と結果の計算
            double changeRatio = (double)changedBlockCount / totalBlocks;
            bool hasSignificantChange = changeRatio > settings.Threshold;

            _logger?.LogDebug("ブロックベース差分検出: 変化率 {ChangeRatio:P2} ({ChangedCount}/{TotalBlocks}), 閾値 {Threshold:P2}",
                changeRatio, changedBlockCount, totalBlocks, settings.Threshold);

            // 変化領域のマージと最小サイズフィルタリング
            var mergedRegions = MergeAdjacentRegions(changedBlocks);
            var filteredRegions = mergedRegions
                .Where(r => r.Width * r.Height >= settings.MinimumChangedArea)
                .ToList();

            // テキスト消失の検出（必要に応じて実装）
            List<Rectangle> disappearedTextRegions = [];

            return new DetectionResult
            {
                HasSignificantChange = hasSignificantChange,
                ChangeRatio = changeRatio,
                ChangedRegions = filteredRegions,
                DisappearedTextRegions = disappearedTextRegions
            };
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "ブロックベース差分検出中に引数エラーが発生しました: {Message}", ex.Message);
            return CreateErrorResult(currentImage);
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "ブロックベース差分検出中に操作エラーが発生しました: {Message}", ex.Message);
            return CreateErrorResult(currentImage);
        }
        catch (IOException ex)
        {
            _logger?.LogError(ex, "ブロックベース差分検出中にIO例外が発生しました: {Message}", ex.Message);
            return CreateErrorResult(currentImage);
        }
        catch (Exception ex) when (ex is not ApplicationException)
        {
            _logger?.LogError(ex, "ブロックベース差分検出中に予期しないエラーが発生しました: {Message}", ex.Message);
            return CreateErrorResult(currentImage);
        }
    }

    /// <summary>
    /// ブロック内の差分を計算します
    /// </summary>
    private double CalculateBlockDifference(
        byte[] prevPixels,
        byte[] currPixels,
        int startX,
        int startY,
        int endX,
        int endY,
        int width,
        int bytesPerPixel)
    {
        ArgumentNullException.ThrowIfNull(prevPixels, nameof(prevPixels));
        ArgumentNullException.ThrowIfNull(currPixels, nameof(currPixels));

        // ピクセル数は計算するが使用されていないためディスカード
        int _ = (endX - startX) * (endY - startY);
        int differentPixels = 0;
        int stride = width * bytesPerPixel;

        // サンプリングでブロック内のピクセルをチェック（処理を高速化）
        int samplingRate = Math.Max(1, (endX - startX) / 4); // ブロックサイズの1/4でサンプリング

        for (int y = startY; y < endY; y += samplingRate)
        {
            for (int x = startX; x < endX; x += samplingRate)
            {
                int pos = (y * stride) + (x * bytesPerPixel);

                // 範囲チェック
                if (pos + 2 >= prevPixels.Length || pos + 2 >= currPixels.Length)
                    continue;

                // RGB値の差分を計算
                int diffR = Math.Abs(prevPixels[pos] - currPixels[pos]);
                int diffG = Math.Abs(prevPixels[pos + 1] - currPixels[pos + 1]);
                int diffB = Math.Abs(prevPixels[pos + 2] - currPixels[pos + 2]);

                // RGB差分の総和
                int totalDiff = diffR + diffG + diffB;

                // 閾値を超える差分があるかチェック
                if (totalDiff > 30) // 単純な閾値
                {
                    differentPixels++;
                }
            }
        }

        // サンプリングを考慮した差分率を計算
        int sampledPixels = ((endX - startX) / samplingRate) * ((endY - startY) / samplingRate);
        return sampledPixels > 0 ? (double)differentPixels / sampledPixels : 0.0;
    }

    /// <summary>
    /// エラー発生時の結果を作成します
    /// </summary>
    private DetectionResult CreateErrorResult(IImage currentImage)
    {
        // エラー時は変化ありとして画面全体を返す
        return new DetectionResult
        {
            HasSignificantChange = true,
            ChangeRatio = 1.0,
            ChangedRegions = [new Rectangle(0, 0, currentImage.Width, currentImage.Height)]
        };
    }

    /// <summary>
    /// 隣接する領域をマージします
    /// </summary>
    private List<Rectangle> MergeAdjacentRegions(List<Rectangle> regions)
    {
        ArgumentNullException.ThrowIfNull(regions, nameof(regions));

        if (regions.Count <= 1)
            return regions;

        var mergedRegions = new List<Rectangle>(regions);
        bool merged;

        do
        {
            merged = false;

            for (int i = 0; i < mergedRegions.Count; i++)
            {
                for (int j = i + 1; j < mergedRegions.Count; j++)
                {
                    if (AreRegionsAdjacent(mergedRegions[i], mergedRegions[j]))
                    {
                        // 領域をマージ
                        mergedRegions[i] = MergeTwoRegions(mergedRegions[i], mergedRegions[j]);
                        mergedRegions.RemoveAt(j);

                        merged = true;
                        break;
                    }
                }

                if (merged) break;
            }
        }
        while (merged);

        return mergedRegions;
    }

    /// <summary>
    /// 2つの領域が隣接しているかチェックします
    /// </summary>
    private bool AreRegionsAdjacent(Rectangle r1, Rectangle r2)
    {
        // 領域が重なっているか、隣接している（5ピクセル以内の距離）場合はtrue
        Rectangle expandedR1 = new(
            r1.X - 5,
            r1.Y - 5,
            r1.Width + 10,
            r1.Height + 10
        );

        return expandedR1.IntersectsWith(r2);
    }

    /// <summary>
    /// 2つの領域をマージします
    /// </summary>
    private Rectangle MergeTwoRegions(Rectangle r1, Rectangle r2)
    {
        int left = Math.Min(r1.Left, r2.Left);
        int top = Math.Min(r1.Top, r2.Top);
        int right = Math.Max(r1.Right, r2.Right);
        int bottom = Math.Max(r1.Bottom, r2.Bottom);

        return new Rectangle(left, top, right - left, bottom - top);
    }
}
