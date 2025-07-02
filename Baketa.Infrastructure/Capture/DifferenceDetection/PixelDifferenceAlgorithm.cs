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
    /// ピクセルベースの差分検出アルゴリズム（最も高精度だが処理負荷が高い）
    /// </summary>
    public class PixelDifferenceAlgorithm : IDetectionAlgorithm
    {
        private readonly ILogger<PixelDifferenceAlgorithm>? _logger;
        
        /// <summary>
        /// アルゴリズムの種類
        /// </summary>
        public DifferenceDetectionAlgorithm AlgorithmType => DifferenceDetectionAlgorithm.PixelBased;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="logger">ロガー</param>
        public PixelDifferenceAlgorithm(ILogger<PixelDifferenceAlgorithm>? logger = null)
        {
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
                int width = previousImage.Width;
                int height = previousImage.Height;
                int totalPixels = width * height;
                
                // 前回と現在の画像からピクセルデータを取得
                byte[] prevPixels = await previousImage.GetPixelsAsync(0, 0, width, height).ConfigureAwait(false);
                byte[] currPixels = await currentImage.GetPixelsAsync(0, 0, width, height).ConfigureAwait(false);
                
                int bytesPerPixel = 3; // RGB想定
                int stride = width * bytesPerPixel;
                
                // 差分マップ（ピクセル単位の変化を記録）- ジャグ配列に変更
                bool[][] diffMap = new bool[height][];
                for (int i = 0; i < height; i++)
                {
                    diffMap[i] = new bool[width];
                }
                int differentPixelCount = 0;
                
                // マルチスケール処理（オプション）
                if (settings.ScaleCount > 1)
                {
                    // 縮小版での高速チェック
                    bool hasChange = await CheckMultiScaleDifferenceAsync(
                        previousImage, currentImage, settings, cancellationToken).ConfigureAwait(false);
                        
                    if (!hasChange)
                    {
                        _logger?.LogDebug("マルチスケール検出: 変化なし");
                        return new DetectionResult
                        {
                            HasSignificantChange = false,
                            ChangeRatio = 0.0,
                            ChangedRegions = []
                        };
                    }
                }
                
                // 詳細な差分検出を実行
                for (int y = 0; y < height; y++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                        
                    for (int x = 0; x < width; x++)
                    {
                        int pos = (y * stride) + (x * bytesPerPixel);
                        
                        if (pos + 2 >= prevPixels.Length || pos + 2 >= currPixels.Length)
                            continue;
                            
                        // RGB値の差分を計算
                        int diffR = Math.Abs(prevPixels[pos] - currPixels[pos]);
                        int diffG = Math.Abs(prevPixels[pos + 1] - currPixels[pos + 1]);
                        int diffB = Math.Abs(prevPixels[pos + 2] - currPixels[pos + 2]);
                        
                        // 照明変化対応（必要に応じて）
                        if (settings.IgnoreLightingChanges)
                        {
                            // 照明変化と思われる変化を除外するロジック
                            // 例：RGB値が同じ比率で変化している場合は照明変化の可能性
                            
                            // 簡易実装例
                            double avgDiff = (diffR + diffG + diffB) / 3.0;
                            if (Math.Abs(diffR - avgDiff) < 10 && 
                                Math.Abs(diffG - avgDiff) < 10 && 
                                Math.Abs(diffB - avgDiff) < 10)
                            {
                                // 照明変化と思われる場合は閾値を引き上げる
                                diffR = (int)(diffR * 0.5);
                                diffG = (int)(diffG * 0.5);
                                diffB = (int)(diffB * 0.5);
                            }
                        }
                        
                        // RGB差分の総和
                        int totalDiff = diffR + diffG + diffB;
                        
                        // 閾値を超える差分があるかチェック
                        double pixelThreshold = 30.0; // 単純な閾値
                        if (totalDiff > pixelThreshold)
                        {
                            diffMap[y][x] = true;
                            differentPixelCount++;
                            
                            // 早期終了条件
                            if ((double)differentPixelCount / totalPixels > settings.Threshold * 2)
                            {
                                _logger?.LogDebug("早期終了: ピクセルの {PercentChanged:P2} が変更されています", 
                                    (double)differentPixelCount / totalPixels);
                                break;
                            }
                        }
                    }
                }
                
                // 変化率と結果の計算
                double changeRatio = (double)differentPixelCount / totalPixels;
                bool hasSignificantChange = changeRatio > settings.Threshold;
                
                _logger?.LogDebug("ピクセルベース差分検出: 変化率 {ChangeRatio:P2} ({ChangedPixels}/{TotalPixels}), 閾値 {Threshold:P2}",
                    changeRatio, differentPixelCount, totalPixels, settings.Threshold);
                
                // 変化領域の抽出
                List<Rectangle> changedRegions = ExtractChangedRegions(diffMap, width, height, settings.BlockSize);
                
                // 最小サイズフィルタリング
                var filteredRegions = changedRegions
                    .Where(r => r.Width * r.Height >= settings.MinimumChangedArea)
                    .ToList();
                
                // テキスト消失の検出
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
                _logger?.LogError(ex, "ピクセルベース差分検出中に引数エラーが発生しました: {Message}", ex.Message);
                
                // エラー時は変化ありとして画面全体を返す
                return new DetectionResult
                {
                    HasSignificantChange = true,
                    ChangeRatio = 1.0,
                    ChangedRegions = [new Rectangle(0, 0, currentImage.Width, currentImage.Height)]
                };
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "ピクセルベース差分検出中に操作エラーが発生しました: {Message}", ex.Message);
                
                return new DetectionResult
                {
                    HasSignificantChange = true,
                    ChangeRatio = 1.0,
                    ChangedRegions = [new Rectangle(0, 0, currentImage.Width, currentImage.Height)]
                };
            }
            catch (IOException ex)
            {
                _logger?.LogError(ex, "ピクセルベース差分検出中にIO例外が発生しました: {Message}", ex.Message);
                
                return new DetectionResult
                {
                    HasSignificantChange = true,
                    ChangeRatio = 1.0,
                    ChangedRegions = [new Rectangle(0, 0, currentImage.Width, currentImage.Height)]
                };
            }
            catch (Exception ex) when (ex is not ApplicationException)
            {
                _logger?.LogError(ex, "ピクセルベース差分検出中に予期しないエラーが発生しました: {Message}", ex.Message);
                
                return new DetectionResult
                {
                    HasSignificantChange = true,
                    ChangeRatio = 1.0,
                    ChangedRegions = [new Rectangle(0, 0, currentImage.Width, currentImage.Height)]
                };
            }
        }
        
        /// <summary>
        /// マルチスケールでの差分チェック（高速フィルタリング）
        /// </summary>
        private async Task<bool> CheckMultiScaleDifferenceAsync(
            IImage previousImage, 
            IImage currentImage, 
            DifferenceDetectionSettings settings,
            CancellationToken cancellationToken)
        {
            // 縮小率
            int scale = 8;
            
            // 縮小版の画像サイズ
            int scaledWidth = Math.Max(1, previousImage.Width / scale);
            int scaledHeight = Math.Max(1, previousImage.Height / scale);
            
            // 縮小版の作成（実際の実装ではリサイズ処理を使用）
            // ここではサンプリングによる簡易的な縮小処理
            
            byte[] prevPixels = await previousImage.GetPixelsAsync(0, 0, previousImage.Width, previousImage.Height).ConfigureAwait(false);
            byte[] currPixels = await currentImage.GetPixelsAsync(0, 0, currentImage.Width, currentImage.Height).ConfigureAwait(false);
            
            int bytesPerPixel = 3;
            int stride = previousImage.Width * bytesPerPixel;
            int scaledPixelCount = scaledWidth * scaledHeight;
            int differentPixelCount = 0;
            
            // 縮小画像をサンプリングで比較
            for (int y = 0; y < scaledHeight; y++)
            {
                for (int x = 0; x < scaledWidth; x++)
                {
                    // 元画像での位置
                    int srcX = x * scale;
                    int srcY = y * scale;
                    
                    // ピクセル位置の計算
                    int pos = (srcY * stride) + (srcX * bytesPerPixel);
                    
                    if (pos + 2 >= prevPixels.Length || pos + 2 >= currPixels.Length)
                        continue;
                        
                    // RGB値の差分を計算
                    int diffR = Math.Abs(prevPixels[pos] - currPixels[pos]);
                    int diffG = Math.Abs(prevPixels[pos + 1] - currPixels[pos + 1]);
                    int diffB = Math.Abs(prevPixels[pos + 2] - currPixels[pos + 2]);
                    
                    // RGB差分の総和
                    int totalDiff = diffR + diffG + diffB;
                    
                    // 閾値を超える差分があるかチェック
                    if (totalDiff > 40) // 縮小版では少し高めの閾値
                    {
                        differentPixelCount++;
                        
                        // 早期終了条件
                        if ((double)differentPixelCount / scaledPixelCount > settings.Threshold)
                        {
                            return true; // 十分な変化あり
                        }
                    }
                }
            }
            
            // 変化率の計算
            double changeRatio = (double)differentPixelCount / scaledPixelCount;
            return changeRatio > settings.Threshold;
        }
        
        /// <summary>
        /// 差分マップから変化領域を抽出します
        /// </summary>
        private List<Rectangle> ExtractChangedRegions(bool[][] diffMap, int width, int height, int blockSize)
        {
            // 連結成分のラベリングによる領域抽出 - ジャグ配列に変更
            int[][] labels = new int[height][];
            for (int i = 0; i < height; i++)
            {
                labels[i] = new int[width];
            }
            int nextLabel = 1;
            Dictionary<int, List<Point>> labelPoints = [];
            
            // 第1パス：ラベリング
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!diffMap[y][x])
                        continue;
                        
                    // 周囲のラベルをチェック
                    HashSet<int> neighborLabels = [];
                    
                    if (x > 0 && diffMap[y][x - 1] && labels[y][x - 1] > 0)
                        neighborLabels.Add(labels[y][x - 1]);
                        
                    if (y > 0 && diffMap[y - 1][x] && labels[y - 1][x] > 0)
                        neighborLabels.Add(labels[y - 1][x]);
                        
                    if (x > 0 && y > 0 && diffMap[y - 1][x - 1] && labels[y - 1][x - 1] > 0)
                        neighborLabels.Add(labels[y - 1][x - 1]);
                        
                    if (x < width - 1 && y > 0 && diffMap[y - 1][x + 1] && labels[y - 1][x + 1] > 0)
                        neighborLabels.Add(labels[y - 1][x + 1]);
                    
                    if (neighborLabels.Count == 0)
                    {
                        // 新しいラベルを割り当て
                        labels[y][x] = nextLabel;
                        labelPoints[nextLabel] = [new Point(x, y)];
                        nextLabel++;
                    }
                    else
                    {
                        // 既存のラベルを使用
                        int minLabel = neighborLabels.Min();
                        labels[y][x] = minLabel;
                        labelPoints[minLabel].Add(new Point(x, y));
                        
                        // ラベルの統合
                        if (neighborLabels.Count > 1)
                        {
                            foreach (int label in neighborLabels)
                            {
                                if (label != minLabel)
                                {
                                    // ポイントを統合
                                    labelPoints[minLabel].AddRange(labelPoints[label]);
                                    
                                    // 元のラベルのポイントを更新
                                    foreach (var pt in labelPoints[label])
                                    {
                                        labels[pt.Y][pt.X] = minLabel;
                                    }
                                    
                                    // 元のラベルを削除
                                    labelPoints.Remove(label);
                                }
                            }
                        }
                    }
                }
            }
            
            // 各ラベルの領域を計算
            List<Rectangle> regions = [];
            
            foreach (var labelGroup in labelPoints.Values)
            {
                if (labelGroup.Count < 5) // 小さすぎる領域は無視
                    continue;
                    
                // 領域の境界を計算
                int minX = width, minY = height, maxX = 0, maxY = 0;
                
                foreach (var pt in labelGroup)
                {
                    minX = Math.Min(minX, pt.X);
                    minY = Math.Min(minY, pt.Y);
                    maxX = Math.Max(maxX, pt.X);
                    maxY = Math.Max(maxY, pt.Y);
                }
                
                // 領域をブロックサイズに合わせて拡張
                int alignedMinX = (minX / blockSize) * blockSize;
                int alignedMinY = (minY / blockSize) * blockSize;
                int alignedMaxX = ((maxX / blockSize) + 1) * blockSize;
                int alignedMaxY = ((maxY / blockSize) + 1) * blockSize;
                
                // 境界チェック
                alignedMinX = Math.Max(0, alignedMinX);
                alignedMinY = Math.Max(0, alignedMinY);
                alignedMaxX = Math.Min(width, alignedMaxX);
                alignedMaxY = Math.Min(height, alignedMaxY);
                
                regions.Add(new Rectangle(
                    alignedMinX, 
                    alignedMinY, 
                    alignedMaxX - alignedMinX, 
                    alignedMaxY - alignedMinY));
            }
            
            return regions;
        }
    }
