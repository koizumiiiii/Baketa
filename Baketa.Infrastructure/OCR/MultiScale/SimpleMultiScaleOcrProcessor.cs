using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Services.Imaging;
using OpenCvSharp;

namespace Baketa.Infrastructure.OCR.MultiScale;

/// <summary>
/// 簡単なマルチスケールOCR処理実装
/// </summary>
public class SimpleMultiScaleOcrProcessor(ILogger<SimpleMultiScaleOcrProcessor> logger) : IMultiScaleOcrProcessor
{
    private readonly ILogger<SimpleMultiScaleOcrProcessor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly List<float> _scaleFactors = [1.0f, 1.5f, 2.0f];
    
    public IReadOnlyList<float> ScaleFactors => _scaleFactors;
    public bool UseDynamicScaling { get; set; } = true;

    public async Task<OcrResults> ProcessAsync(IAdvancedImage image, IOcrEngine ocrEngine)
    {
        _logger.LogInformation("マルチスケールOCR処理開始");
        
        var detailedResult = await ProcessWithDetailsAsync(image, ocrEngine).ConfigureAwait(false);
        return detailedResult.MergedResult;
    }
    
    public async Task<MultiScaleOcrResult> ProcessWithDetailsAsync(IAdvancedImage image, IOcrEngine ocrEngine)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("マルチスケールOCR処理開始: {Scales}個のスケール", _scaleFactors.Count);
        
        try
        {
            // 各スケールで並列処理
            var tasks = _scaleFactors.Select(scale => 
                ProcessAtScaleAsync(image, ocrEngine, scale, sw.ElapsedMilliseconds)
            ).ToList();
            
            var scaleResults = await Task.WhenAll(tasks).ConfigureAwait(false);
            
            // 結果を統合
            var mergedResult = MergeResults([.. scaleResults]);
            
            // 統計情報を計算
            var stats = CalculateStats([.. scaleResults], mergedResult, sw.ElapsedMilliseconds);
            
            _logger.LogInformation("マルチスケール処理完了: {ResultCount}→{MergedCount}リージョン, 改善スコア: {Score:F2}", 
                stats.TotalRegionsBeforeMerge, stats.TotalRegionsAfterMerge, stats.ImprovementScore);
            
            return new MultiScaleOcrResult
            {
                ScaleResults = [.. scaleResults],
                MergedResult = mergedResult,
                Stats = stats
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "マルチスケール処理中にエラーが発生しました");
            
            // フォールバック: ベースライン処理
            var fallbackResult = await ocrEngine.RecognizeAsync(image).ConfigureAwait(false);
            return CreateFallbackResult(fallbackResult, sw.ElapsedMilliseconds);
        }
    }
    
    public async Task<IReadOnlyList<float>> DetermineOptimalScalesAsync(IAdvancedImage image)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return _scaleFactors;
    }
    
    /// <summary>
    /// 特定のスケールでOCR処理を実行
    /// </summary>
    private async Task<ScaleProcessingResult> ProcessAtScaleAsync(
        IAdvancedImage originalImage, 
        IOcrEngine ocrEngine, 
        float scaleFactor,
        long _)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogDebug("スケール {Scale}x で処理開始", scaleFactor);
        
        try
        {
            IAdvancedImage processImage = originalImage;
            
            // スケーリングが必要な場合
            if (Math.Abs(scaleFactor - 1.0f) > 0.001f)
            {
                processImage = await ScaleImageAsync(originalImage, scaleFactor).ConfigureAwait(false);
            }
            
            // OCR処理
            var ocrResult = await ocrEngine.RecognizeAsync(processImage).ConfigureAwait(false);
            
            // 座標を元のスケールに戻す
            if (Math.Abs(scaleFactor - 1.0f) > 0.001f && ocrResult.HasText)
            {
                AdjustCoordinatesForScale(ocrResult, scaleFactor);
                
                // スケール済み画像を破棄
                if (processImage != originalImage && processImage is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            
            var confidence = ocrResult.TextRegions.Any() ? 
                (float)ocrResult.TextRegions.Average(r => r.Confidence) : 0f;
            
            _logger.LogDebug("スケール {Scale}x 完了: {Regions}リージョン, 信頼度: {Confidence:F2}", 
                scaleFactor, ocrResult.TextRegions.Count, confidence);
            
            return new ScaleProcessingResult
            {
                ScaleFactor = scaleFactor,
                OcrResult = ocrResult,
                ProcessingTimeMs = sw.ElapsedMilliseconds,
                DetectedRegions = ocrResult.TextRegions.Count,
                AverageConfidence = confidence
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "スケール {Scale}x 処理中にエラーが発生", scaleFactor);
            
            // エラー時は空の結果を返す
            var emptyResult = CreateEmptyOcrResult(originalImage);
            return new ScaleProcessingResult
            {
                ScaleFactor = scaleFactor,
                OcrResult = emptyResult,
                ProcessingTimeMs = sw.ElapsedMilliseconds,
                DetectedRegions = 0,
                AverageConfidence = 0f
            };
        }
    }
    
    /// <summary>
    /// 画像をスケーリング
    /// </summary>
    private async Task<IAdvancedImage> ScaleImageAsync(IAdvancedImage image, float scaleFactor)
    {
        return await Task.Run(async () =>
        {
            try
            {
                // 新しいサイズを計算
                var newWidth = Math.Max(1, (int)(image.Width * scaleFactor));
                var newHeight = Math.Max(1, (int)(image.Height * scaleFactor));
                
                _logger.LogDebug("画像スケーリング: {Width}x{Height} → {NewWidth}x{NewHeight} (スケール: {Scale}x)",
                    image.Width, image.Height, newWidth, newHeight, scaleFactor);
                
                // 元の画像データを取得
                var originalBytes = await image.ToByteArrayAsync().ConfigureAwait(false);

                // OpenCVを使用してスケーリング処理を実行
                using var mat = CreateMatFromBytes(originalBytes, image.Width, image.Height, image.Format);
                using var scaledMat = new OpenCvSharp.Mat();
                // バイキュービック補間でスケーリング（高品質）
                var interpolation = scaleFactor > 1.0f ?
                    OpenCvSharp.InterpolationFlags.Cubic :  // 拡大時はキュービック
                    OpenCvSharp.InterpolationFlags.Area;   // 縮小時はエリア補間

                OpenCvSharp.Cv2.Resize(mat, scaledMat, new OpenCvSharp.Size(newWidth, newHeight),
                    0, 0, interpolation);

                // スケーリングされた画像をバイト配列に変換
                var scaledBytes = ConvertMatToBytes(scaledMat, image.Format);

                return new AdvancedImage(scaledBytes, newWidth, newHeight, image.Format);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "画像スケーリングに失敗、元の画像を返します。スケール: {Scale}x", scaleFactor);
                
                // エラー時は元の画像を返す
                var originalBytes = await image.ToByteArrayAsync().ConfigureAwait(false);
                return new AdvancedImage(originalBytes, image.Width, image.Height, image.Format);
            }
        }).ConfigureAwait(false);
    }
    
    /// <summary>
    /// バイト配列からMatを作成
    /// </summary>
    private OpenCvSharp.Mat CreateMatFromBytes(byte[] imageBytes, int width, int height, ImageFormat format)
    {
        var matType = format switch
        {
            ImageFormat.Rgb24 => OpenCvSharp.MatType.CV_8UC3,
            ImageFormat.Png => OpenCvSharp.MatType.CV_8UC3,
            ImageFormat.Jpeg => OpenCvSharp.MatType.CV_8UC3,
            _ => OpenCvSharp.MatType.CV_8UC3
        };
        
        var mat = new OpenCvSharp.Mat(height, width, matType);
        
        // バイト配列をMatにコピー
        var expectedSize = width * height * (matType == OpenCvSharp.MatType.CV_8UC1 ? 1 : 3);
        if (imageBytes.Length >= expectedSize)
        {
            System.Runtime.InteropServices.Marshal.Copy(imageBytes, 0, mat.Data, expectedSize);
        }
        else
        {
            _logger.LogWarning("画像バイト配列サイズが不足: 期待 {Expected}, 実際 {Actual}", 
                expectedSize, imageBytes.Length);
        }
        
        return mat;
    }
    
    /// <summary>
    /// Matをバイト配列に変換
    /// </summary>
    private byte[] ConvertMatToBytes(OpenCvSharp.Mat mat, ImageFormat _)
    {
        var channels = mat.Channels();
        var totalSize = mat.Width * mat.Height * channels;
        var bytes = new byte[totalSize];
        
        System.Runtime.InteropServices.Marshal.Copy(mat.Data, bytes, 0, totalSize);
        
        return bytes;
    }
    
    /// <summary>
    /// スケーリングされた座標を元のスケールに調整
    /// </summary>
    private void AdjustCoordinatesForScale(OcrResults ocrResult, float scaleFactor)
    {
        if (Math.Abs(scaleFactor - 1.0f) < 0.001f)
        {
            return; // スケーリングなしの場合は調整不要
        }
        
        var inverseScale = 1.0f / scaleFactor;
        List<OcrTextRegion> adjustedRegions = [];
        
        foreach (var region in ocrResult.TextRegions)
        {
            try
            {
                // 座標を元のスケールに戻す
                var adjustedBounds = new System.Drawing.Rectangle(
                    (int)(region.Bounds.X * inverseScale),
                    (int)(region.Bounds.Y * inverseScale),
                    (int)(region.Bounds.Width * inverseScale),
                    (int)(region.Bounds.Height * inverseScale)
                );
                
                // 角座標を調整（OcrTextRegionにCornersプロパティがない場合はnull）
                // System.Drawing.Point[]? adjustedCorners = null; // 未使用のため削除
                
                // 調整されたリージョンを作成
                var adjustedRegion = new OcrTextRegion(
                    region.Text,
                    adjustedBounds,
                    region.Confidence
                );
                
                adjustedRegions.Add(adjustedRegion);
                
                _logger.LogDebug("座標調整: {Text} - {OriginalBounds} → {AdjustedBounds} (スケール逆数: {InverseScale:F2})",
                    region.Text, region.Bounds, adjustedBounds, inverseScale);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "座標調整エラー、元の座標を使用: {Text}", region.Text);
                adjustedRegions.Add(region);
            }
        }
        
        // OcrResultsの元のTextRegionsを置き換える
        // OcrResultsが不変の場合は、リフレクションまたは新しいインスタンスを作成
        try
        {
            var textRegionsField = typeof(OcrResults).GetField("_textRegions", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (textRegionsField != null)
            {
                textRegionsField.SetValue(ocrResult, adjustedRegions);
            }
            else
            {
                _logger.LogWarning("TextRegionsフィールドが見つかりません。座標調整をスキップします。");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "座標調整の適用に失敗しました");
        }
    }
    
    /// <summary>
    /// 複数スケールの結果を統合
    /// </summary>
    private OcrResults MergeResults(List<ScaleProcessingResult> scaleResults)
    {
        if (scaleResults.Count == 0 || scaleResults.All(r => !r.OcrResult.HasText))
        {
            _logger.LogWarning("統合可能な結果がありません");
            return CreateEmptyOcrResult(null);
        }
        
        _logger.LogDebug("結果統合開始: {ScaleCount}個のスケール結果", scaleResults.Count);
        
        // 全スケールのテキストリージョンを収集
        List<(OcrTextRegion region, float scale, float confidence)> allRegions = [];
        
        foreach (var scaleResult in scaleResults.Where(r => r.OcrResult.HasText))
        {
            foreach (var region in scaleResult.OcrResult.TextRegions)
            {
                allRegions.Add((region, scaleResult.ScaleFactor, (float)region.Confidence));
            }
        }
        
        if (allRegions.Count == 0)
        {
            return CreateEmptyOcrResult(null);
        }
        
        // IoUベースの重複除去と統合
        var mergedRegions = MergeOverlappingRegions(allRegions);
        
        // 最高品質のベース画像を取得
        var bestBaseResult = scaleResults
            .OrderByDescending(r => r.AverageConfidence)
            .ThenByDescending(r => r.DetectedRegions)
            .First();
        
        var finalResult = new OcrResults(
            mergedRegions,
            bestBaseResult.OcrResult.SourceImage,
            TimeSpan.FromMilliseconds(scaleResults.Sum(r => r.ProcessingTimeMs)),
            "ja"
        );
        
        _logger.LogInformation("結果統合完了: {OriginalCount}→{MergedCount}リージョン", 
            allRegions.Count, mergedRegions.Count);
        
        return finalResult;
    }
    
    /// <summary>
    /// IoUベースの重複リージョンを統合
    /// </summary>
    private List<OcrTextRegion> MergeOverlappingRegions(List<(OcrTextRegion region, float scale, float confidence)> regions)
    {
        var mergedRegions = new List<OcrTextRegion>();
        var processed = new HashSet<int>();
        
        for (int i = 0; i < regions.Count; i++)
        {
            if (processed.Contains(i)) continue;
            
            var currentGroup = new List<(OcrTextRegion region, float scale, float confidence)> { regions[i] };
            processed.Add(i);
            
            // 重複するリージョンを検索
            for (int j = i + 1; j < regions.Count; j++)
            {
                if (processed.Contains(j)) continue;
                
                var iou = CalculateIoU(regions[i].region.Bounds, regions[j].region.Bounds);
                
                // IoU閾値（0.3以上で重複とみなす）
                if (iou >= 0.3f)
                {
                    currentGroup.Add(regions[j]);
                    processed.Add(j);
                }
            }
            
            // グループ内で最高信頼度のリージョンを選択、または統合
            var mergedRegion = CreateMergedRegion(currentGroup);
            if (mergedRegion != null)
            {
                mergedRegions.Add(mergedRegion);
            }
        }
        
        return mergedRegions;
    }
    
    /// <summary>
    /// IoU（Intersection over Union）を計算
    /// </summary>
    private float CalculateIoU(System.Drawing.Rectangle rect1, System.Drawing.Rectangle rect2)
    {
        // 交差する矩形を計算
        var intersection = System.Drawing.Rectangle.Intersect(rect1, rect2);
        
        if (intersection.IsEmpty)
        {
            return 0f;
        }
        
        var intersectionArea = intersection.Width * intersection.Height;
        var unionArea = (rect1.Width * rect1.Height) + (rect2.Width * rect2.Height) - intersectionArea;
        
        return unionArea > 0 ? (float)intersectionArea / unionArea : 0f;
    }
    
    /// <summary>
    /// 重複リージョングループから最適なリージョンを作成
    /// </summary>
    private OcrTextRegion? CreateMergedRegion(List<(OcrTextRegion region, float scale, float confidence)> group)
    {
        if (group.Count == 0) return null;
        
        // 最高信頼度のリージョンを基準とする
        var bestRegion = group.OrderByDescending(g => g.confidence).First();
        
        // より良いテキストが見つかった場合は置き換え
        var (region, scale, confidence) = group
            .Where(g => !string.IsNullOrWhiteSpace(g.region.Text))
            .OrderByDescending(g => g.region.Text.Length)  // より長いテキストを優先
            .ThenByDescending(g => g.confidence)
            .FirstOrDefault();
        
        var finalText = region?.Text ?? bestRegion.region.Text;
        var finalConfidence = group.Average(g => g.confidence);
        
        _logger.LogDebug("リージョン統合: {GroupCount}個 → \"{Text}\" (信頼度: {Confidence:F2})", 
            group.Count, finalText, finalConfidence);
        
        return new OcrTextRegion(
            finalText,
            bestRegion.region.Bounds,
            (float)finalConfidence
        );
    }
    
    /// <summary>
    /// 統計情報を計算
    /// </summary>
    private MultiScaleProcessingStats CalculateStats(
        List<ScaleProcessingResult> scaleResults, 
        OcrResults mergedResult, 
        long totalTime)
    {
        var totalRegionsBefore = scaleResults.Sum(r => r.DetectedRegions);
        var totalRegionsAfter = mergedResult.TextRegions.Count;
        
        // ベースライン（スケール1.0）との比較
        var baselineResult = scaleResults.FirstOrDefault(r => Math.Abs(r.ScaleFactor - 1.0f) < 0.001f);
        var baselineRegions = baselineResult?.DetectedRegions ?? 0;
        
        var improvementScore = baselineRegions > 0 ? 
            (float)(totalRegionsAfter - baselineRegions) / baselineRegions : 
            totalRegionsAfter > 0 ? 1.0f : 0f;
        
        return new MultiScaleProcessingStats
        {
            TotalProcessingTimeMs = totalTime,
            ScalesUsed = scaleResults.Count,
            TotalRegionsBeforeMerge = totalRegionsBefore,
            TotalRegionsAfterMerge = totalRegionsAfter,
            SmallTextRegions = scaleResults.Where(r => r.ScaleFactor > 1.5f).Sum(r => r.DetectedRegions),
            ImprovementScore = Math.Max(0f, improvementScore)
        };
    }
    
    /// <summary>
    /// フォールバック結果を作成
    /// </summary>
    private MultiScaleOcrResult CreateFallbackResult(OcrResults fallbackResult, long processingTime)
    {
        var scaleResult = new ScaleProcessingResult
        {
            ScaleFactor = 1.0f,
            OcrResult = fallbackResult,
            ProcessingTimeMs = processingTime,
            DetectedRegions = fallbackResult.TextRegions.Count,
            AverageConfidence = fallbackResult.TextRegions.Any() ? 
                (float)fallbackResult.TextRegions.Average(r => r.Confidence) : 0f
        };
        
        var stats = new MultiScaleProcessingStats
        {
            TotalProcessingTimeMs = processingTime,
            ScalesUsed = 1,
            TotalRegionsBeforeMerge = fallbackResult.TextRegions.Count,
            TotalRegionsAfterMerge = fallbackResult.TextRegions.Count,
            SmallTextRegions = 0,
            ImprovementScore = 0f
        };
        
        return new MultiScaleOcrResult
        {
            ScaleResults = [scaleResult],
            MergedResult = fallbackResult,
            Stats = stats
        };
    }
    
    /// <summary>
    /// 空のOCR結果を作成
    /// </summary>
    private OcrResults CreateEmptyOcrResult(IAdvancedImage? sourceImage)
    {
        var dummyImage = sourceImage ?? new AdvancedImage(
            [0], 1, 1, ImageFormat.Rgb24);
        
        return new OcrResults(
            [], 
            dummyImage, 
            TimeSpan.Zero, 
            "ja");
    }
}
