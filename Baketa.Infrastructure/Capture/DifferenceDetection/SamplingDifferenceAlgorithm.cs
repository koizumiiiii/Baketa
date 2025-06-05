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
    /// サンプリングベースの差分検出アルゴリズム（最も高速）
    /// </summary>
    public class SamplingDifferenceAlgorithm : IDetectionAlgorithm
    {
        private readonly ILogger<SamplingDifferenceAlgorithm>? _logger;
        
        /// <summary>
        /// アルゴリズムの種類
        /// </summary>
        public DifferenceDetectionAlgorithm AlgorithmType => DifferenceDetectionAlgorithm.SamplingBased;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="logger">ロガー</param>
        public SamplingDifferenceAlgorithm(ILogger<SamplingDifferenceAlgorithm>? logger = null)
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
                
                // サンプリングポイントの生成
                List<Point> samplingPoints = GenerateSamplingPoints(
                    width, height, settings.SamplingDensity, settings.FocusOnTextRegions);
                
                // 前回と現在の画像からピクセルデータを取得
                // 注意：実際の大規模実装では一度に全ピクセルを取得するのではなく、
                // 必要な部分だけを効率的に取得する実装が望ましい
                byte[] prevPixels = await previousImage.GetPixelsAsync(0, 0, width, height).ConfigureAwait(false);
                byte[] currPixels = await currentImage.GetPixelsAsync(0, 0, width, height).ConfigureAwait(false);
                
                int bytesPerPixel = 3; // RGB想定
                int stride = width * bytesPerPixel;
                
                int differentPixels = 0;
                var changedRegions = new List<Point>();
                
                // サンプリングポイントでの差分検出
                foreach (var point in samplingPoints)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                        
                    // ピクセル位置の計算
                    int position = (point.Y * stride) + (point.X * bytesPerPixel);
                    
                    // バウンダリチェックを強化
                    if (position < 0 || position + 2 >= prevPixels.Length || position + 2 >= currPixels.Length || 
                        point.X < 0 || point.Y < 0 || point.X >= width || point.Y >= height)
                    {
                        continue;
                    }
                        
                    // RGB差分の合計を計算
                    double diff = 
                        Math.Abs(prevPixels[position] - currPixels[position]) +
                        Math.Abs(prevPixels[position + 1] - currPixels[position + 1]) +
                        Math.Abs(prevPixels[position + 2] - currPixels[position + 2]);
                    
                    // 閾値を超える差分があるか判定
                    double colorThreshold = 30.0; // 色差の閾値
                    if (diff > colorThreshold)
                    {
                        differentPixels++;
                        changedRegions.Add(point);
                        
                        // 早期終了条件：十分な数の差分を検出した場合
                        double earlyTerminationThreshold = 0.2; // サンプル中の20%
                        if ((double)differentPixels / samplingPoints.Count > earlyTerminationThreshold)
                        {
                            _logger?.LogDebug("早期終了: サンプルの {PercentChanged:P2} が変更されています", 
                                (double)differentPixels / samplingPoints.Count);
                            break;
                        }
                    }
                }
                
                // 変化率と結果の計算
                double changeRatio = (double)differentPixels / samplingPoints.Count;
                bool hasSignificantChange = changeRatio > settings.Threshold;
                
                _logger?.LogDebug("サンプリング差分検出: 変化率 {ChangeRatio:P2} ({DiffCount}/{TotalSamples}), 閾値 {Threshold:P2}",
                    changeRatio, differentPixels, samplingPoints.Count, settings.Threshold);
                
                var result = new DetectionResult
                {
                    HasSignificantChange = hasSignificantChange,
                    ChangeRatio = changeRatio
                };
                
                // 変化領域のクラスタリング
                if (hasSignificantChange && changedRegions.Count > 0)
                {
                    result.ChangedRegions = ClusterChangedPoints(changedRegions, width, height, settings.BlockSize);
                }
                
                return result;
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "サンプリング差分検出中に引数エラーが発生しました: {Message}", ex.Message);
                return CreateErrorResult(currentImage);
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "サンプリング差分検出中に操作エラーが発生しました: {Message}", ex.Message);
                return CreateErrorResult(currentImage);
            }
            catch (IOException ex)
            {
                _logger?.LogError(ex, "サンプリング差分検出中にIO例外が発生しました: {Message}", ex.Message);
                return CreateErrorResult(currentImage);
            }
            catch (Exception ex) when (ex is not ApplicationException)
            {
                _logger?.LogError(ex, "サンプリング差分検出中に予期しないエラーが発生しました: {Message}", ex.Message);
                return CreateErrorResult(currentImage);
            }
        }
        
        /// <summary>
        /// サンプリングポイントを生成します
        /// </summary>
        private List<Point> GenerateSamplingPoints(int width, int height, int density, bool focusOnTextRegions)
        {
            var points = new List<Point>();
            
            // グリッドベースサンプリング
            int stepX = Math.Max(1, width / density);
            int stepY = Math.Max(1, height / density);
            
            for (int y = 0; y < height; y += stepY)
            {
                for (int x = 0; x < width; x += stepX)
                {
                    points.Add(new Point(x, y));
                }
            }
            
            // テキスト重視モードの場合、暗号学的に安全な乱数生成器を使用して別のポイントも追加
            if (focusOnTextRegions)
            {
                using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
                int additionalPoints = density * 5; // 追加サンプリングポイント
                
                for (int i = 0; i < additionalPoints; i++)
                {
                    byte[] bytes = new byte[8];
                    rng.GetBytes(bytes);
                    int x = Math.Abs(BitConverter.ToInt32(bytes, 0) % width);
                    int y = Math.Abs(BitConverter.ToInt32(bytes, 4) % height);
                    points.Add(new Point(x, y));
                }
            }
            
            return points;
        }
        
        /// <summary>
        /// エラー発生時の結果を作成します
        /// </summary>
        private DetectionResult CreateErrorResult(IImage currentImage)
        {
            IReadOnlyList<Rectangle> regions = [new Rectangle(0, 0, currentImage.Width, currentImage.Height)];
            return new DetectionResult
            {
                HasSignificantChange = true,
                ChangeRatio = 1.0,
                ChangedRegions = regions
            };
        }
        
        /// <summary>
        /// 変化ポイントをクラスタリングして領域に変換します
        /// </summary>
        private static List<Rectangle> ClusterChangedPoints(List<Point> changedPoints, int width, int height, int blockSize)
        {
            ArgumentNullException.ThrowIfNull(changedPoints, nameof(changedPoints));
                
            if (changedPoints.Count == 0)
                return [];
                
            // シンプルなグリッドベースのクラスタリング
            int gridWidth = (width + blockSize - 1) / blockSize;
            int gridHeight = (height + blockSize - 1) / blockSize;
            
            // グリッドセルの変化を記録 - ジャグ配列に変更
            var changedCells = new bool[gridHeight][];
            for (int i = 0; i < gridHeight; i++)
            {
                changedCells[i] = new bool[gridWidth];
            }
            
            foreach (var point in changedPoints)
            {
                int cellX = point.X / blockSize;
                int cellY = point.Y / blockSize;
                
                if (cellX < gridWidth && cellY < gridHeight)
                {
                    changedCells[cellY][cellX] = true;
                }
            }
            
            // 連結成分のラベリング（簡易版）
            List<Rectangle> regions = [];
            // 訪問済みセルを記録 - ジャグ配列に変更
            var visited = new bool[gridHeight][];
            for (int i = 0; i < gridHeight; i++)
            {
                visited[i] = new bool[gridWidth];
            }
            
            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    if (changedCells[y][x] && !visited[y][x])
                    {
                        // 新しい連結成分を見つけた
                        var region = ExploreComponent(changedCells, visited, x, y, gridWidth, gridHeight, blockSize);
                        regions.Add(region);
                    }
                }
            }
            
            // 小さすぎる領域を除外（5x5ブロック未満）
            var filteredRegions = regions
                .Where(r => r.Width * r.Height >= blockSize * blockSize * 5)
                .ToList();
                
            return filteredRegions;
        }
        
        /// <summary>
        /// 連結成分を探索して領域を決定します
        /// </summary>
        private static Rectangle ExploreComponent(
            bool[][] changedCells, 
            bool[][] visited, 
            int startX, 
            int startY, 
            int gridWidth, 
            int gridHeight,
            int blockSize)
        {
            int minX = startX;
            int minY = startY;
            int maxX = startX;
            int maxY = startY;
            
            // スタックベースの深さ優先探索
            var stack = new Stack<(int x, int y)>();
            stack.Push((startX, startY));
            
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                
                if (x < 0 || y < 0 || x >= gridWidth || y >= gridHeight || visited[y][x] || !changedCells[y][x])
                    continue;
                    
                visited[y][x] = true;
                
                // 領域の境界を更新
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                
                // 隣接セルをスタックに追加
                stack.Push((x + 1, y));
                stack.Push((x - 1, y));
                stack.Push((x, y + 1));
                stack.Push((x, y - 1));
            }
            
            // グリッドセルから実際のピクセル位置へ変換
            return new Rectangle(
                minX * blockSize,
                minY * blockSize,
                (maxX - minX + 1) * blockSize,
                (maxY - minY + 1) * blockSize);
        }
    }
