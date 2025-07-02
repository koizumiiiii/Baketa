using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using ImagingOpenCvWrapper = Baketa.Core.Abstractions.Imaging.IOpenCvWrapper;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;
using Baketa.Core.Abstractions.OCR.TextDetection;
using Microsoft.Extensions.Logging;
using DetectionMethodEnum = Baketa.Core.Abstractions.OCR.TextDetection.TextDetectionMethod;

namespace Baketa.Infrastructure.OCR.TextDetection;

    /// <summary>
    /// MSER (Maximally Stable Extremal Regions) アルゴリズムによるテキスト領域検出器
    /// </summary>
    public class MserTextRegionDetector : TextRegionDetectorBase
    {
        private readonly ImagingOpenCvWrapper _openCvWrapper;
        
        /// <summary>
        /// 検出器の名前
        /// </summary>
        public override string Name => "MSERテキスト検出器";
        
        /// <summary>
        /// 検出器の説明
        /// </summary>
        public override string Description => "MSERアルゴリズムを使用したテキスト領域検出";
        
        /// <summary>
        /// 検出に使用するアルゴリズム
        /// </summary>
        public override TextDetectionMethod Method => TextDetectionMethod.Mser;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="openCvWrapper">OpenCVラッパー</param>
        /// <param name="logger">ロガー</param>
        public MserTextRegionDetector(ImagingOpenCvWrapper openCvWrapper, ILogger<MserTextRegionDetector>? logger = null)
            : base(logger)
        {
            _openCvWrapper = openCvWrapper ?? throw new ArgumentNullException(nameof(openCvWrapper));
        }
        
        /// <summary>
        /// 画像からテキスト領域を検出します
        /// </summary>
        /// <param name="image">検出対象の画像</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>検出されたテキスト領域のリスト</returns>
        public override async Task<IReadOnlyList<OCRTextRegion>> DetectRegionsAsync(
            IAdvancedImage image, 
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(image, nameof(image));
                
            Logger?.LogDebug("MSERテキスト領域検出を開始 (画像サイズ: {Width}x{Height})",
                image.Width, image.Height);
                
            try
            {
                // MSER検出パラメータを取得
                int delta = GetParameter<int>("MserDelta");
                int minArea = GetParameter<int>("MserMinArea");
                int maxArea = GetParameter<int>("MserMaxArea");
                int minWidth = GetParameter<int>("MinWidth");
                int minHeight = GetParameter<int>("MinHeight");
                float minAspectRatio = GetParameter<float>("MinAspectRatio");
                float maxAspectRatio = GetParameter<float>("MaxAspectRatio");
                float mergeThreshold = GetParameter<float>("MergeThreshold");
                
                // OpenCVを使用してMSER検出を実行
                var detectedRegions = await Task.Run(() => 
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // グレースケール変換（必要な場合）
                    var grayImage = image.IsGrayscale ? image : image.ToGrayscale();
                    
                    // MSER検出実行
                    Dictionary<string, object> mserParams = [];
                    mserParams["delta"] = delta;
                    mserParams["minArea"] = minArea;
                    mserParams["maxArea"] = maxArea;
                    var mserRegions = _openCvWrapper.DetectMSERRegions(grayImage, mserParams);
                    
                    // 検出結果を処理
                    List<OCRTextRegion> textRegions = [];
                    
                    foreach (var region in mserRegions)
                    {
                        // バウンディングボックスを取得
                        var bounds = _openCvWrapper.GetBoundingRect(region);
                        
                        // サイズと縦横比によるフィルタリング
                        if (bounds.Width < minWidth || bounds.Height < minHeight)
                            continue;
                            
                        float aspectRatio = bounds.Width / (float)bounds.Height;
                        if (aspectRatio < minAspectRatio || aspectRatio > maxAspectRatio)
                            continue;
                        
                        // 信頼度スコアの計算（簡易的な実装）
                        float confidenceScore = CalculateConfidenceScore(bounds, region, grayImage);
                        
                        // テキスト領域として追加
                        var textRegion = new OCRTextRegion(bounds, confidenceScore)
                        {
                            RegionType = ClassifyRegionType(bounds, aspectRatio),
                            Contour = [.. region]
                        };
                        
                        textRegions.Add(textRegion);
                    }
                    
                    // 重複領域のマージ
                    var mergedRegions = MergeOverlappingRegions(textRegions, mergeThreshold);
                    
                    return mergedRegions;
                    
                }, cancellationToken).ConfigureAwait(false);
                
                Logger?.LogDebug("MSERテキスト領域検出が完了 (検出数: {Count})", 
                    detectedRegions.Count);
                    
                return detectedRegions;
            }
            catch (OperationCanceledException)
            {
                Logger?.LogInformation("MSERテキスト領域検出がキャンセルされました");
                throw;
            }
            catch (Exception ex)
            {
                // EventIdの代わりに直接メッセージを渡す
                Logger?.LogError(ex, "MSERテキスト領域検出中にエラーが発生しました");
                throw;
            }
        }
        
        /// <summary>
        /// 信頼度スコアを計算します（簡易的な実装）
        /// </summary>
        /// <param name="bounds">バウンディングボックス</param>
        /// <param name="contour">輪郭</param>
        /// <param name="image">画像</param>
        /// <returns>信頼度スコア（0.0～1.0）</returns>
        private static float CalculateConfidenceScore(Rectangle bounds, Point[] contour, IAdvancedImage image)
        {
            // この実装は簡易的なもので、実際にはより複雑なスコアリングが必要
            
            // 面積の要素
            float areaScore = Math.Min(1.0f, bounds.Width * bounds.Height / 10000.0f);
            
            // アスペクト比の要素
            float aspectRatio = bounds.Width / (float)bounds.Height;
            float aspectScore = aspectRatio > 0.2f && aspectRatio < 5.0f ? 
                1.0f - Math.Min(Math.Abs(aspectRatio - 1.0f) / 4.0f, 1.0f) : 0.0f;
            
            // 輪郭の複雑さの要素
            float complexityScore = Math.Min(contour.Length / 100.0f, 1.0f);
            
            // コントラストの要素（理想的にはここでテキスト領域内のコントラストを計算）
            float contrastScore = 0.7f; // 簡易実装のため固定値
            
            // 総合スコア
            float totalScore = (areaScore * 0.2f + aspectScore * 0.3f + complexityScore * 0.2f + contrastScore * 0.3f);
            
            return Math.Min(Math.Max(totalScore, 0.0f), 1.0f);
        }
        
        /// <summary>
        /// 領域のタイプを分類します
        /// </summary>
        /// <param name="bounds">領域のバウンディングボックス</param>
        /// <param name="aspectRatio">領域のアスペクト比</param>
        /// <returns>分類されたテキスト領域タイプ</returns>
        private static TextRegionType ClassifyRegionType(Rectangle bounds, float aspectRatio)
        {
            // 簡易的な分類ロジック
            if (bounds.Width > 300 && bounds.Height < 50 && aspectRatio > 6.0f)
            {
                return TextRegionType.Title;
            }
            else if (bounds.Width > 200 && bounds.Height > 100)
            {
                return TextRegionType.Paragraph;
            }
            else if (bounds.Width < 100 && bounds.Height < 50)
            {
                return aspectRatio > 3.0f ? TextRegionType.Button : TextRegionType.Label;
            }
            else
            {
                return TextRegionType.Unknown;
            }
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
            var mergedRegions = new List<OCRTextRegion>();
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
