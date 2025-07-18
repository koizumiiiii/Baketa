using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;
using Baketa.Core.Abstractions.OCR.TextDetection;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Infrastructure.Imaging.Extensions;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Imaging.Filters;

/// <summary>
/// テキスト領域検出フィルター
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="detector">テキスト領域検出器</param>
/// <param name="logger">ロガー</param>
public sealed class TextRegionDetectionFilter(
        ITextRegionDetector detector,
        ILogger<TextRegionDetectionFilter>? logger = null) : BaseImageFilter(logger)
    {
        private readonly ITextRegionDetector _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        
        /// <summary>
        /// フィルターの名前
        /// </summary>
        public override string Name => "テキスト領域検出";
        
        /// <summary>
        /// フィルターの説明
        /// </summary>
        public override string Description => "画像からテキスト領域を検出し、メタデータとして添付します";
        
        /// <summary>
        /// フィルターのカテゴリ
        /// </summary>
        public override FilterCategory Category => FilterCategory.Effect;

    /// <summary>
    /// デフォルトパラメータを初期化します
    /// </summary>
    protected override void InitializeDefaultParameters()
        {
            base.InitializeDefaultParameters();
            
            // デフォルト値を設定
            SetParameter("MinConfidence", 0.5f);
            SetParameter("StoreRegionsInMetadata", true);
            SetParameter("StoreProcessedImages", false);
            SetParameter("MaxRegions", 50);
        }
        
        /// <summary>
        /// パラメータ定義を取得します
        /// </summary>
        /// <returns>パラメータ定義のコレクション</returns>
        protected override IReadOnlyCollection<PipelineStepParameter> GetParameterDefinitions()
        {
            return 
            [
                new PipelineStepParameter(
                    "MinConfidence",
                    "検出結果として採用する最小信頼度スコア（0.0～1.0）",
                    typeof(float),
                    0.5f,
                    0.0f,
                    1.0f),
                new PipelineStepParameter(
                    "StoreRegionsInMetadata",
                    "検出結果を画像のメタデータに保存するかどうか",
                    typeof(bool),
                    true),
                new PipelineStepParameter(
                    "StoreProcessedImages",
                    "検出領域ごとの処理済み画像を保存するかどうか",
                    typeof(bool),
                    false),
                new PipelineStepParameter(
                    "MaxRegions",
                    "保存する最大領域数",
                    typeof(int),
                    50,
                    1,
                    1000)
            ];
        }
        
        /// <summary>
        /// フィルターを適用します
        /// </summary>
        /// <param name="image">入力画像</param>
        /// <returns>処理結果画像</returns>
        public override async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);
                
            Log(LogLevel.Debug, "テキスト領域検出フィルターを適用中...");
            
            try
            {
                // パラメータを取得
                float minConfidence = GetParameter<float>("MinConfidence");
                bool storeRegionsInMetadata = GetParameter<bool>("StoreRegionsInMetadata");
                bool storeProcessedImages = GetParameter<bool>("StoreProcessedImages");
                int maxRegions = GetParameter<int>("MaxRegions");
                
                // テキスト領域検出を実行
                var regions = await _detector.DetectRegionsAsync(inputImage).ConfigureAwait(false);
                
                // 結果のフィルタリング
                var filteredRegions = FilterRegions(regions, minConfidence, maxRegions);
                
                // 処理済み画像の保存
                if (!storeProcessedImages)
                {
                    foreach (var region in filteredRegions)
                    {
                        region.ProcessedImage = null;
                    }
                }
                
                // 結果画像を作成
                var resultImage = inputImage.Clone() as IAdvancedImage
                    ?? throw new InvalidOperationException("画像のクローンに失敗しました");
                
                // メタデータに検出結果を保存
                if (storeRegionsInMetadata)
                {
                    resultImage.SetMetadata("TextRegions", filteredRegions);
                    resultImage.SetMetadata("TextRegionCount", filteredRegions.Count);
                    resultImage.SetMetadata("TextDetectionMethod", _detector.Method.ToString());
                }
                
                Log(LogLevel.Debug, "テキスト領域検出が完了しました (検出数: {RegionCount})", filteredRegions.Count);
                
                return resultImage;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "テキスト領域検出中にエラーが発生しました: {0}", ex.Message);
                throw;
            }
        }
        
        /// <summary>
        /// 検出結果をフィルタリングします
        /// </summary>
        /// <param name="regions">検出結果</param>
        /// <param name="minConfidence">最小信頼度</param>
        /// <param name="maxRegions">最大領域数</param>
        /// <returns>フィルタリングされた検出結果</returns>
        private static List<OCRTextRegion> FilterRegions(
            IReadOnlyList<OCRTextRegion> regions,
            float minConfidence,
            int maxRegions)
        {
            // LINQを使用してフィルタリング、ソート、制限を一度に実行
            return [.. regions
                .Where(region => region.ConfidenceScore >= minConfidence)
                .OrderByDescending(region => region.ConfidenceScore)
                .Take(maxRegions)];
        }
    }
