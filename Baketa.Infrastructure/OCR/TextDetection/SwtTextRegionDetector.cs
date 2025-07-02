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
    /// SWT (Stroke Width Transform) アルゴリズムによるテキスト領域検出器
    /// </summary>
    public class SwtTextRegionDetector : TextRegionDetectorBase
    {
        private readonly ImagingOpenCvWrapper _openCvWrapper;
        
        /// <summary>
        /// 検出器の名前
        /// </summary>
        public override string Name => "SWTテキスト検出器";
        
        /// <summary>
        /// 検出器の説明
        /// </summary>
        public override string Description => "ストローク幅変換を使用したテキスト領域検出";
        
        /// <summary>
        /// 検出に使用するアルゴリズム
        /// </summary>
        public override DetectionMethodEnum Method => DetectionMethodEnum.Swt;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="openCvWrapper">OpenCVラッパー</param>
        /// <param name="logger">ロガー</param>
        public SwtTextRegionDetector(ImagingOpenCvWrapper openCvWrapper, ILogger<SwtTextRegionDetector>? logger = null)
            : base(logger)
        {
            _openCvWrapper = openCvWrapper ?? throw new ArgumentNullException(nameof(openCvWrapper));
            
            // デフォルトパラメータを設定
            SetParameter("CannyThreshold1", 50);
            SetParameter("CannyThreshold2", 150);
            SetParameter("MinStrokeWidth", 2.0f);
            SetParameter("MaxStrokeWidth", 15.0f);
            SetParameter("StrokeWidthVarianceRatio", 0.5f);
            SetParameter("MinComponentSize", 8);
            SetParameter("MaxComponentSize", 1000);
            SetParameter("MinAspectRatio", 0.1f);
            SetParameter("MaxAspectRatio", 10.0f);
            SetParameter("GroupingEnabled", true);
            SetParameter("GroupingDistance", 3.0f);
            SetParameter("GroupingStrokeWidthRatio", 0.3f);
            SetParameter("MergeThreshold", 0.3f);
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
                
            Logger?.LogDebug("SWTテキスト領域検出を開始 (画像サイズ: {Width}x{Height})",
                image.Width, image.Height);
                
            try
            {
                // SWT検出パラメータを取得
                int cannyThreshold1 = GetParameter<int>("CannyThreshold1");
                int cannyThreshold2 = GetParameter<int>("CannyThreshold2");
                float minStrokeWidth = GetParameter<float>("MinStrokeWidth");
                float maxStrokeWidth = GetParameter<float>("MaxStrokeWidth");
                float strokeWidthVarianceRatio = GetParameter<float>("StrokeWidthVarianceRatio");
                int minComponentSize = GetParameter<int>("MinComponentSize");
                int maxComponentSize = GetParameter<int>("MaxComponentSize");
                float minAspectRatio = GetParameter<float>("MinAspectRatio");
                float maxAspectRatio = GetParameter<float>("MaxAspectRatio");
                bool groupingEnabled = GetParameter<bool>("GroupingEnabled");
                float groupingDistance = GetParameter<float>("GroupingDistance");
                float groupingStrokeWidthRatio = GetParameter<float>("GroupingStrokeWidthRatio");
                float mergeThreshold = GetParameter<float>("MergeThreshold");
                
                // 非同期処理を実行
                var detectedRegions = await Task.Run(() => 
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // グレースケール変換
                    var grayImage = image.IsGrayscale ? image : image.ToGrayscale();
                    
                    // エッジ検出（Canny）
                    var edgeImage = _openCvWrapper.CannyEdgeDetection(
                        grayImage, cannyThreshold1, cannyThreshold2);
                    
                    // ストローク幅変換（SWT）を適用
                    var swtImage = _openCvWrapper.StrokeWidthTransform(
                        grayImage, edgeImage, minStrokeWidth, maxStrokeWidth);
                    
                    // 連結成分の抽出
                    var components = _openCvWrapper.ExtractConnectedComponents(
                        swtImage, minComponentSize, maxComponentSize);
                    
                    // テキスト領域として抽出
                    List<OCRTextRegion> textRegions = [];
                    
                    foreach (var component in components)
                    {
                        // ストローク幅の分散を計算
                        float strokeWidthVariance = _openCvWrapper.CalculateStrokeWidthVariance(
                            swtImage, component);
                        
                        // ストローク幅の一貫性チェック
                        float meanStrokeWidth = _openCvWrapper.CalculateMeanStrokeWidth(
                            swtImage, component);
                            
                        if (strokeWidthVariance / meanStrokeWidth > strokeWidthVarianceRatio)
                        {
                            continue; // ストローク幅が一貫していない
                        }
                        
                        // バウンディングボックスを取得
                        var bounds = _openCvWrapper.GetBoundingRect(component);
                        
                        // サイズと縦横比によるフィルタリング
                        float aspectRatio = bounds.Width / (float)bounds.Height;
                        if (aspectRatio < minAspectRatio || aspectRatio > maxAspectRatio)
                        {
                            continue;
                        }
                        
                        // 信頼度スコアの計算
                        float confidenceScore = CalculateConfidenceScore(
                            bounds, component, meanStrokeWidth, strokeWidthVariance, grayImage);
                        
                        // テキスト領域として追加
                        var textRegion = new OCRTextRegion(bounds, confidenceScore)
                        {
                            RegionType = ClassifyRegionType(bounds, aspectRatio, meanStrokeWidth),
                            Contour = [.. component]
                        };
                        
                        // メタデータに追加情報を保存
                        textRegion.Metadata["MeanStrokeWidth"] = meanStrokeWidth;
                        textRegion.Metadata["StrokeWidthVariance"] = strokeWidthVariance;
                        
                        textRegions.Add(textRegion);
                    }
                    
                    // 領域のグループ化（オプション）
                    if (groupingEnabled && textRegions.Count > 1)
                    {
                        textRegions = GroupRegions(
                            textRegions, groupingDistance, groupingStrokeWidthRatio);
                    }
                    
                    // 重複領域のマージ
                    var mergedRegions = MergeOverlappingRegions(textRegions, mergeThreshold);
                    
                    return mergedRegions;
                    
                }, cancellationToken).ConfigureAwait(false);
                
                Logger?.LogDebug("SWTテキスト領域検出が完了 (検出数: {Count})", 
                    detectedRegions.Count);
                    
                return detectedRegions;
            }
            catch (OperationCanceledException)
            {
                Logger?.LogInformation("SWTテキスト領域検出がキャンセルされました");
                throw;
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "SWTテキスト領域検出中にエラーが発生しました: {Message}", ex.Message);
                throw;
            }
        }
        
        /// <summary>
        /// 信頼度スコアを計算します
        /// </summary>
        /// <param name="bounds">バウンディングボックス</param>
        /// <param name="contour">輪郭</param>
        /// <param name="meanStrokeWidth">平均ストローク幅</param>
        /// <param name="strokeWidthVariance">ストローク幅の分散</param>
        /// <param name="image">画像</param>
        /// <returns>信頼度スコア（0.0～1.0）</returns>
        private float CalculateConfidenceScore(
            Rectangle bounds, 
            Point[] contour, 
            float meanStrokeWidth,
            float strokeWidthVariance,
            IAdvancedImage image)
        {
            // ストローク幅の一貫性スコア
            float strokeWidthConsistencyScore = Math.Max(
                0.0f, 
                1.0f - strokeWidthVariance / (meanStrokeWidth * meanStrokeWidth));
            
            // サイズ適合性スコア
            float sizeScore = Math.Min(
                1.0f, 
                bounds.Width * bounds.Height / 10000.0f);
            
            // アスペクト比スコア（文字に適した縦横比ほど高スコア）
            float aspectRatio = bounds.Width / (float)bounds.Height;
            float aspectRatioScore = Math.Max(
                0.0f,
                1.0f - Math.Abs(aspectRatio - 1.5f) / 3.0f);
            
            // テキスト特性スコア（境界のシャープさなど）
            float textCharacteristicScore = 0.7f; // ここでは簡易実装
            
            // 総合スコア（重み付き平均）
            float totalScore = 
                strokeWidthConsistencyScore * 0.4f + 
                sizeScore * 0.2f + 
                aspectRatioScore * 0.2f + 
                textCharacteristicScore * 0.2f;
            
            return Math.Min(Math.Max(totalScore, 0.0f), 1.0f);
        }
        
        /// <summary>
        /// 領域のタイプを分類します
        /// </summary>
        /// <param name="bounds">領域のバウンディングボックス</param>
        /// <param name="aspectRatio">領域のアスペクト比</param>
        /// <param name="strokeWidth">ストローク幅</param>
        /// <returns>分類されたテキスト領域タイプ</returns>
        private static TextRegionType ClassifyRegionType(
            Rectangle bounds, 
            float aspectRatio, 
            float strokeWidth)
        {
            // ストローク幅から文字の大きさを推測
            if (strokeWidth > 8.0f && bounds.Width > 200)
                return TextRegionType.Title;
            if (strokeWidth > 5.0f && bounds.Width > 100)
                return TextRegionType.Heading;
            if (bounds.Width > 300 && bounds.Height > 50)
                return TextRegionType.Paragraph;
            if (bounds.Width < 150 && aspectRatio > 3.0f)
                return TextRegionType.Button;
            if (bounds.Width < 100 && bounds.Height < 40)
                return TextRegionType.Label;
                
            return TextRegionType.Unknown;
        }
        
        /// <summary>
        /// テキスト領域をグループ化します
        /// </summary>
        /// <param name="regions">グループ化する領域</param>
        /// <param name="distanceThreshold">グループ化する距離閾値</param>
        /// <param name="strokeWidthRatioThreshold">ストローク幅比率閾値</param>
        /// <returns>グループ化された領域</returns>
        private static List<OCRTextRegion> GroupRegions(
            List<OCRTextRegion> regions,
            float distanceThreshold,
            float strokeWidthRatioThreshold)
        {
            // グループを保持するリスト
            var groups = new List<List<OCRTextRegion>>();
            var assigned = new bool[regions.Count];
            
            // 各領域を調査
            for (int i = 0; i < regions.Count; i++)
            {
                if (assigned[i])
                    continue;
                
                var currentRegion = regions[i];
                var currentGroup = new List<OCRTextRegion> { currentRegion };
                assigned[i] = true;
                
                // 他の未割り当て領域と比較
                for (int j = 0; j < regions.Count; j++)
                {
                    if (i == j || assigned[j])
                        continue;
                    
                    var compareRegion = regions[j];
                    
                    // 距離を計算
                    float distance = CalculateDistance(currentRegion, compareRegion);
                    
                    // ストローク幅比を計算
                    float strokeWidthRatio = CalculateStrokeWidthRatio(currentRegion, compareRegion);
                    
                    // グループ化条件をチェック
                    if (distance <= distanceThreshold && 
                        strokeWidthRatio <= strokeWidthRatioThreshold)
                    {
                        currentGroup.Add(compareRegion);
                        assigned[j] = true;
                    }
                }
                
                groups.Add(currentGroup);
            }
            
            // グループ化結果を新しいTextRegionに変換
            var groupedRegions = new List<OCRTextRegion>();
            
            foreach (var group in groups)
            {
                if (group.Count == 1)
                {
                    // 単一領域はそのまま追加
                    groupedRegions.Add(group[0]);
                }
                else
                {
                    // 複数領域を統合
                    var bounds = CombineBounds(group.Select(r => r.Bounds));
                    var maxScore = group.Max(r => r.ConfidenceScore);
                    var regionType = DetermineGroupRegionType(group);
                    
                    var groupRegion = new OCRTextRegion(bounds, maxScore, regionType);
                    
                    // メタデータに情報を追加
                    groupRegion.Metadata["GroupSize"] = group.Count;
                    groupRegion.Metadata["OriginalRegions"] = group.ToArray();
                    
                    groupedRegions.Add(groupRegion);
                }
            }
            
            return groupedRegions;
        }
        
        /// <summary>
        /// 2つの領域間の距離を計算します
        /// </summary>
        /// <param name="region1">領域1</param>
        /// <param name="region2">領域2</param>
        /// <returns>領域間の距離</returns>
        private static float CalculateDistance(OCRTextRegion region1, OCRTextRegion region2)
        {
            // 領域が重なっている場合は距離0
            if (region1.Bounds.IntersectsWith(region2.Bounds))
            {
                return 0.0f;
            }
            
            // 領域間の距離を計算
            int horizontalDistance = 0;
            if (region1.Bounds.Right < region2.Bounds.Left)
            {
                horizontalDistance = region2.Bounds.Left - region1.Bounds.Right;
            }
            else if (region2.Bounds.Right < region1.Bounds.Left)
            {
                horizontalDistance = region1.Bounds.Left - region2.Bounds.Right;
            }
            
            int verticalDistance = 0;
            if (region1.Bounds.Bottom < region2.Bounds.Top)
            {
                verticalDistance = region2.Bounds.Top - region1.Bounds.Bottom;
            }
            else if (region2.Bounds.Bottom < region1.Bounds.Top)
            {
                verticalDistance = region1.Bounds.Top - region2.Bounds.Bottom;
            }
            
            return (float)Math.Sqrt(horizontalDistance * horizontalDistance + 
                               verticalDistance * verticalDistance);
        }
        
        /// <summary>
        /// 2つの領域のストローク幅比率を計算します
        /// </summary>
        /// <param name="region1">領域1</param>
        /// <param name="region2">領域2</param>
        /// <returns>ストローク幅比率</returns>
        private static float CalculateStrokeWidthRatio(OCRTextRegion region1, OCRTextRegion region2)
        {
            // メタデータからストローク幅を取得
            if (region1.Metadata.TryGetValue("MeanStrokeWidth", out var width1) &&
                region2.Metadata.TryGetValue("MeanStrokeWidth", out var width2))
            {
                float strokeWidth1 = Convert.ToSingle(width1, System.Globalization.CultureInfo.InvariantCulture);
                float strokeWidth2 = Convert.ToSingle(width2, System.Globalization.CultureInfo.InvariantCulture);
                
                // 比率を計算（小さい値/大きい値）
                return strokeWidth1 < strokeWidth2 ? 
                    strokeWidth1 / strokeWidth2 : 
                    strokeWidth2 / strokeWidth1;
            }
            
            return 1.0f; // デフォルト値（差なし）
        }
        
        /// <summary>
        /// 複数の矩形を結合した境界矩形を計算します
        /// </summary>
        /// <param name="rectangles">結合する矩形のコレクション</param>
        /// <returns>結合された境界矩形</returns>
        private static Rectangle CombineBounds(IEnumerable<Rectangle> rectangles)
        {
            int left = int.MaxValue;
            int top = int.MaxValue;
            int right = int.MinValue;
            int bottom = int.MinValue;
            
            foreach (var rect in rectangles)
            {
                left = Math.Min(left, rect.Left);
                top = Math.Min(top, rect.Top);
                right = Math.Max(right, rect.Right);
                bottom = Math.Max(bottom, rect.Bottom);
            }
            
            return new Rectangle(left, top, right - left, bottom - top);
        }
        
        /// <summary>
        /// グループの領域タイプを決定します
        /// </summary>
        /// <param name="regions">グループ内の領域</param>
        /// <returns>決定された領域タイプ</returns>
        private static TextRegionType DetermineGroupRegionType(List<OCRTextRegion> regions)
        {
            // 最も多い領域タイプを選択
            Dictionary<TextRegionType, int> typeCounts = [];
            
            foreach (var region in regions)
            {
                // Dictionaryのインデクサ表記を使用
                typeCounts[region.RegionType] = typeCounts.TryGetValue(region.RegionType, out int count) ? count + 1 : 1;
            }
            
            // 最も多いタイプを取得
            var mostCommonType = typeCounts
                .OrderByDescending(kv => kv.Value)
                .First().Key;
                
            // グループ全体のサイズから再判定
            var combinedBounds = CombineBounds(regions.Select(r => r.Bounds));
            
            // 大きなグループは段落または本文と判定
            return (combinedBounds.Width > 300 && combinedBounds.Height > 100) ? TextRegionType.Paragraph : mostCommonType;
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
