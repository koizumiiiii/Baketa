using System.Drawing;
using System.IO;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;

namespace Baketa.Infrastructure.OCR.Strategies;

/// <summary>
/// テキスト検出ベース適応的分割戦略
/// PaddleOCR検出APIを活用したテキスト境界保護分割
/// </summary>
public sealed class AdaptiveTileStrategy : ITileStrategy
{
    private readonly IOcrEngine _textDetector;
    private readonly ILogger<AdaptiveTileStrategy> _logger;

    public string StrategyName => "AdaptiveTile";
    public TileStrategyParameters Parameters { get; set; } = new();

    public AdaptiveTileStrategy(
        IOcrEngine textDetector,
        ILogger<AdaptiveTileStrategy> logger)
    {
        _textDetector = textDetector ?? throw new ArgumentNullException(nameof(textDetector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 適応的テキスト境界保護分割
    /// </summary>
    public async Task<List<TileRegion>> GenerateRegionsAsync(
        IAdvancedImage image, 
        TileGenerationOptions options, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogDebug("🎯 AdaptiveTileStrategy開始 - 画像: {Width}x{Height}", 
                image.Width, image.Height);

            // Phase 1: 高速テキスト検出
            var detectionResult = await DetectTextRegionsAsync(image, cancellationToken)
                .ConfigureAwait(false);

            if (detectionResult?.TextRegions == null || detectionResult.TextRegions.Count == 0)
            {
                _logger?.LogWarning("テキスト検出結果が空、GridTileStrategyにフォールバック");
                return CreateFallbackRegions(image, options);
            }

            _logger?.LogDebug("🔍 テキスト検出完了 - 検出領域数: {Count}", detectionResult.TextRegions.Count);

            // Phase 2: バウンディングボックス統合
            var mergedRegions = MergeBoundingBoxes(
                detectionResult.TextRegions.ToList(), Parameters);

            _logger?.LogDebug("🔄 バウンディングボックス統合完了 - 統合領域数: {Count}", mergedRegions.Count);

            // Phase 3: ROI品質検証・調整
            var validatedRegions = ValidateAndAdjustRegions(
                mergedRegions, image, options);

            _logger?.LogInformation("✅ AdaptiveTileStrategy完了 - 最終領域数: {Count}", validatedRegions.Count);

            // デバッグキャプチャ
            if (options.EnableDebugCapture)
            {
                await SaveDebugCaptureAsync(image, validatedRegions, "adaptive", options.DebugCapturePath)
                    .ConfigureAwait(false);
            }

            return validatedRegions;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ 適応的分割処理でエラー発生、GridTileStrategyにフォールバック");
            
            // フォールバック: 固定グリッド分割
            return CreateFallbackRegions(image, options);
        }
    }

    /// <summary>
    /// テキスト検出実行（PaddleOCR検出モード）
    /// </summary>
    private async Task<OcrResults?> DetectTextRegionsAsync(
        IAdvancedImage image, 
        CancellationToken cancellationToken)
    {
        try
        {
            // 高速テキスト検出専用メソッドを使用（パフォーマンス最適化）
            var ocrResult = await _textDetector.DetectTextRegionsAsync(image, cancellationToken)
                .ConfigureAwait(false);

            return ocrResult;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "テキスト検出エラー");
            return null;
        }
    }

    /// <summary>
    /// バウンディングボックス統合処理
    /// テキスト検出結果を意味のある領域に統合
    /// </summary>
    private List<TileRegion> MergeBoundingBoxes(
        List<OcrTextRegion> textRegions, 
        TileStrategyParameters parameters)
    {

        // Step 1: ノイズ除去
        var filteredRegions = FilterNoiseBoundingBoxes(textRegions, parameters);

        _logger?.LogDebug("🧹 ノイズ除去完了 - {Original} → {Filtered}個", 
            textRegions.Count, filteredRegions.Count);

        // Step 2: 行グループ化
        var lineGroups = GroupBoundingBoxesByLines(filteredRegions, parameters);

        _logger?.LogDebug("📝 行グループ化完了 - {Groups}グループ", lineGroups.Count);

        // Step 3: 水平方向統合
        var mergedRegions = new List<TileRegion>();
        var regionIdCounter = 0;

        foreach (var lineGroup in lineGroups)
        {
            var horizontalMerged = MergeHorizontalBoundingBoxes(lineGroup, parameters);
            
            foreach (var mergedBounds in horizontalMerged)
            {
                mergedRegions.Add(new TileRegion
                {
                    Bounds = mergedBounds,
                    RegionType = TileRegionType.TextAdaptive,
                    RegionId = $"adaptive-{regionIdCounter++}",
                    ConfidenceScore = CalculateRegionConfidence(mergedBounds, lineGroup),
                    Metadata = 
                    {
                        ["SourceBoundingBoxCount"] = lineGroup.Count,
                        ["LineGroupId"] = lineGroups.IndexOf(lineGroup),
                        ["MergedFromTexts"] = string.Join(", ", lineGroup.Select(r => r.Text.Length > 10 ? r.Text[..10] + "..." : r.Text))
                    }
                });
            }
        }

        _logger?.LogDebug("🔗 水平統合完了 - 最終領域数: {Count}", mergedRegions.Count);

        return mergedRegions;
    }

    /// <summary>
    /// ノイズ除去: 小さすぎる・信頼度低いボックスを除去
    /// </summary>
    private List<OcrTextRegion> FilterNoiseBoundingBoxes(
        List<OcrTextRegion> regions, 
        TileStrategyParameters parameters)
    {
        return regions.Where(region =>
        {
            var area = region.Bounds.Width * region.Bounds.Height;
            var hasMinArea = area >= parameters.MinBoundingBoxArea;
            var hasMinConfidence = region.Confidence >= parameters.MinConfidenceThreshold;
            
            return hasMinArea && hasMinConfidence;
        }).ToList();
    }

    /// <summary>
    /// 行グループ化: Y座標による行判定
    /// </summary>
    private List<List<OcrTextRegion>> GroupBoundingBoxesByLines(
        List<OcrTextRegion> regions,
        TileStrategyParameters parameters)
    {
        var lineGroups = new List<List<OcrTextRegion>>();
        var processed = new HashSet<OcrTextRegion>();

        foreach (var region in regions.OrderBy(r => r.Bounds.Y))
        {
            if (processed.Contains(region)) continue;

            var currentLine = new List<OcrTextRegion> { region };
            processed.Add(region);

            var baseY = region.Bounds.Y + region.Bounds.Height / 2f;

            // 同じ行に属する他の領域を検索
            foreach (var other in regions)
            {
                if (processed.Contains(other)) continue;

                var otherY = other.Bounds.Y + other.Bounds.Height / 2f;
                
                // Y座標の差が閾値以内なら同じ行
                if (Math.Abs(baseY - otherY) <= parameters.LineGroupingYTolerance)
                {
                    currentLine.Add(other);
                    processed.Add(other);
                }
            }

            // X座標でソート
            currentLine.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));
            lineGroups.Add(currentLine);
        }

        return lineGroups;
    }

    /// <summary>
    /// 水平統合: 近接するボックスを結合
    /// </summary>
    private List<Rectangle> MergeHorizontalBoundingBoxes(
        List<OcrTextRegion> lineGroup,
        TileStrategyParameters parameters)
    {
        if (lineGroup.Count == 0) return [];

        var merged = new List<Rectangle>();
        var currentBounds = lineGroup[0].Bounds;

        for (int i = 1; i < lineGroup.Count; i++)
        {
            var nextBounds = lineGroup[i].Bounds;
            var horizontalDistance = nextBounds.X - (currentBounds.X + currentBounds.Width);

            // 距離が閾値以内なら統合
            if (horizontalDistance <= parameters.HorizontalMergingMaxDistance)
            {
                currentBounds = Rectangle.Union(currentBounds, nextBounds);
            }
            else
            {
                merged.Add(currentBounds);
                currentBounds = nextBounds;
            }
        }

        merged.Add(currentBounds);
        return merged;
    }

    /// <summary>
    /// 領域信頼度計算
    /// </summary>
    private double CalculateRegionConfidence(Rectangle bounds, List<OcrTextRegion> sourceRegions)
    {
        if (sourceRegions.Count == 0) return 0.5;

        var avgConfidence = sourceRegions.Average(r => r.Confidence);
        var area = bounds.Width * bounds.Height;
        
        // 面積が大きく、平均信頼度が高い程、信頼度を上げる
        var areaBonus = Math.Min(0.2, area / 100000.0); // 最大20%のボーナス
        
        return Math.Min(1.0, avgConfidence + areaBonus);
    }

    /// <summary>
    /// ROI品質検証・調整
    /// </summary>
    private List<TileRegion> ValidateAndAdjustRegions(
        List<TileRegion> regions,
        IAdvancedImage image,
        TileGenerationOptions options)
    {

        var validatedRegions = new List<TileRegion>();

        foreach (var region in regions)
        {
            var adjustedRegions = ValidateRegionSize(region, image, Parameters);
            if (adjustedRegions != null)
            {
                validatedRegions.AddRange(adjustedRegions);
            }
        }

        // 最大領域数制限
        if (validatedRegions.Count > options.MaxRegionCount)
        {
            _logger?.LogWarning("領域数が制限を超過、信頼度順でトリミング: {Count} → {Max}",
                validatedRegions.Count, options.MaxRegionCount);

            validatedRegions = validatedRegions
                .OrderByDescending(r => r.ConfidenceScore)
                .Take(options.MaxRegionCount)
                .ToList();
        }

        return validatedRegions;
    }

    /// <summary>
    /// 領域サイズ検証・調整（巨大領域は複数領域に分割）
    /// </summary>
    private List<TileRegion>? ValidateRegionSize(
        TileRegion region, 
        IAdvancedImage image, 
        TileStrategyParameters parameters)
    {
        var bounds = region.Bounds;
        
        // 画像境界内にクリップ
        bounds = Rectangle.Intersect(bounds, new Rectangle(0, 0, image.Width, image.Height));
        
        if (bounds.Width < parameters.MinRegionSize.Width || 
            bounds.Height < parameters.MinRegionSize.Height)
        {
            return null; // 小さすぎる領域は除外
        }

        // 巨大すぎる領域は分割（オーバーフロー防止でlong計算→int変換）
        var maxArea = (int)Math.Min(int.MaxValue, (long)image.Width * image.Height * parameters.MaxRegionSizeRatio);
        if (bounds.Width * bounds.Height > maxArea)
        {
            _logger?.LogDebug("巨大領域検出、分割実行: {Width}x{Height} → 最大面積制限: {MaxArea}", 
                bounds.Width, bounds.Height, maxArea);
            
            // 巨大領域を適切なサイズに分割
            return SplitLargeRegion(region, image, parameters);
        }

        return [region with { Bounds = bounds }];
    }

    /// <summary>
    /// 巨大領域を適切なサイズに分割
    /// </summary>
    private List<TileRegion> SplitLargeRegion(
        TileRegion largeRegion,
        IAdvancedImage image,
        TileStrategyParameters parameters)
    {
        var bounds = largeRegion.Bounds;
        var splitRegions = new List<TileRegion>();
        
        // 最適な分割サイズを計算（オーバーフロー防止でlong計算→int変換）
        var targetArea = (int)Math.Min(int.MaxValue, (long)image.Width * image.Height * parameters.MaxRegionSizeRatio * 0.7); // 余裕をもたせる
        var targetSize = (int)Math.Sqrt(targetArea);
        
        // 水平・垂直分割数を計算
        var horizontalSplits = Math.Max(1, (int)Math.Ceiling((double)bounds.Width / targetSize));
        var verticalSplits = Math.Max(1, (int)Math.Ceiling((double)bounds.Height / targetSize));
        
        _logger?.LogDebug("巨大領域分割設計: {Width}x{Height} → {HSplits}x{VSplits} = {TotalSplits}個の領域", 
            bounds.Width, bounds.Height, horizontalSplits, verticalSplits, horizontalSplits * verticalSplits);
        
        var regionIdCounter = 0;
        
        for (int y = 0; y < verticalSplits; y++)
        {
            for (int x = 0; x < horizontalSplits; x++)
            {
                var splitX = bounds.X + (x * bounds.Width / horizontalSplits);
                var splitY = bounds.Y + (y * bounds.Height / verticalSplits);
                var splitWidth = (x == horizontalSplits - 1) 
                    ? bounds.X + bounds.Width - splitX 
                    : bounds.Width / horizontalSplits;
                var splitHeight = (y == verticalSplits - 1) 
                    ? bounds.Y + bounds.Height - splitY 
                    : bounds.Height / verticalSplits;
                
                var splitBounds = new Rectangle(splitX, splitY, splitWidth, splitHeight);
                
                // 画像境界内にクリップ
                splitBounds = Rectangle.Intersect(splitBounds, new Rectangle(0, 0, image.Width, image.Height));
                
                // 最小サイズチェック
                if (splitBounds.Width >= parameters.MinRegionSize.Width && 
                    splitBounds.Height >= parameters.MinRegionSize.Height)
                {
                    var splitRegion = new TileRegion
                    {
                        Bounds = splitBounds,
                        RegionType = TileRegionType.TextAdaptive, // 分割された巨大領域
                        RegionId = $"{largeRegion.RegionId}-split-{regionIdCounter++}",
                        ConfidenceScore = largeRegion.ConfidenceScore * 0.8, // 分割による信頼度低下
                        Metadata = 
                        {
                            ["ParentRegionId"] = largeRegion.RegionId,
                            ["SplitIndex"] = $"{x}-{y}",
                            ["TotalSplits"] = horizontalSplits * verticalSplits,
                            ["SplitReason"] = "LargeRegionSubdivision"
                        }
                    };
                    
                    splitRegions.Add(splitRegion);
                }
            }
        }
        
        _logger?.LogDebug("巨大領域分割完了: {OriginalSize} → {SplitCount}個の分割領域", 
            $"{bounds.Width}x{bounds.Height}", splitRegions.Count);
        
        return splitRegions;
    }

    /// <summary>
    /// フォールバック: 固定グリッド分割
    /// </summary>
    private List<TileRegion> CreateFallbackRegions(
        IAdvancedImage image, 
        TileGenerationOptions options)
    {
        // GridTileStrategyと同等の固定分割
        var tileSize = Parameters.TileSize ?? options.DefaultTileSize;
        var regions = new List<TileRegion>();

        if (image.Width <= tileSize && image.Height <= tileSize)
        {
            regions.Add(new TileRegion
            {
                Bounds = new Rectangle(0, 0, image.Width, image.Height),
                RegionType = TileRegionType.Fallback,
                RegionId = "fallback-single",
                ConfidenceScore = 1.0,
                Metadata = { ["IsFallback"] = true }
            });
        }
        else
        {
            var tilesX = (int)Math.Ceiling((double)image.Width / tileSize);
            var tilesY = (int)Math.Ceiling((double)image.Height / tileSize);

            for (var y = 0; y < tilesY; y++)
            {
                for (var x = 0; x < tilesX; x++)
                {
                    var startX = x * tileSize;
                    var startY = y * tileSize;
                    var width = Math.Min(tileSize, image.Width - startX);
                    var height = Math.Min(tileSize, image.Height - startY);

                    regions.Add(new TileRegion
                    {
                        Bounds = new Rectangle(startX, startY, width, height),
                        RegionType = TileRegionType.Fallback,
                        RegionId = $"fallback-{x}-{y}",
                        ConfidenceScore = 1.0,
                        Metadata = { ["IsFallback"] = true, ["GridX"] = x, ["GridY"] = y }
                    });
                }
            }
        }

        _logger?.LogInformation("📋 フォールバック分割実行 - 領域数: {Count}", regions.Count);
        
        return regions;
    }

    /// <summary>
    /// デバッグキャプチャ保存（AdaptiveTileStrategy用）
    /// </summary>
    private async Task SaveDebugCaptureAsync(
        IAdvancedImage image, 
        List<TileRegion> regions, 
        string suffix, 
        string? debugPath)
    {
        try
        {
            // 環境依存しないデバッグキャプチャパスの設定
            var capturePath = debugPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                "BaketaDebugCaptures"
            );
            
            if (!Directory.Exists(capturePath))
            {
                Directory.CreateDirectory(capturePath);
            }

            var imageBytes = await image.ToByteArrayAsync().ConfigureAwait(false);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");

            // 元画像保存
            var originalFilename = $"adaptive-debug-original_{timestamp}_{suffix}_{image.Width}x{image.Height}.png";
            var originalPath = Path.Combine(capturePath, originalFilename);
            await File.WriteAllBytesAsync(originalPath, imageBytes).ConfigureAwait(false);

            // 注釈付き画像生成
            if (regions.Count > 0)
            {
                var annotatedFilename = $"adaptive-debug-annotated_{timestamp}_{suffix}_{image.Width}x{image.Height}.png";
                var annotatedPath = Path.Combine(capturePath, annotatedFilename);
                
                await CreateAnnotatedImageAsync(imageBytes, regions, image.Width, image.Height, annotatedPath)
                    .ConfigureAwait(false);
                
                _logger?.LogDebug("🎯 AdaptiveTile デバッグ画像保存完了: {AnnotatedFile}", annotatedFilename);
            }

            _logger?.LogDebug("🎯 AdaptiveTile デバッグキャプチャ完了: {OriginalFile}, 領域数: {Count}", 
                originalFilename, regions.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AdaptiveTile デバッグキャプチャ保存エラー");
        }
    }

    /// <summary>
    /// 注釈付き画像作成（AdaptiveTileStrategy用）
    /// </summary>
    private async Task CreateAnnotatedImageAsync(
        byte[] imageBytes, 
        List<TileRegion> regions, 
        int width, 
        int height, 
        string outputPath)
    {
        try
        {
            using var memoryStream = new MemoryStream(imageBytes);
            using var originalBitmap = new System.Drawing.Bitmap(memoryStream);
            using var annotatedBitmap = new System.Drawing.Bitmap(originalBitmap);
            using var graphics = System.Drawing.Graphics.FromImage(annotatedBitmap);
            
            // 高品質描画設定
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            
            // 適応的領域境界線描画（緑色、太い線）
            using var adaptivePen = new System.Drawing.Pen(System.Drawing.Color.LimeGreen, 4.0f);
            using var fallbackPen = new System.Drawing.Pen(System.Drawing.Color.Orange, 3.0f)
            {
                DashStyle = System.Drawing.Drawing2D.DashStyle.DashDot
            };
            using var borderPen = new System.Drawing.Pen(System.Drawing.Color.Red, 2.0f) 
            { 
                DashStyle = System.Drawing.Drawing2D.DashStyle.Dash 
            };
            
            for (int i = 0; i < regions.Count; i++)
            {
                var region = regions[i];
                var rect = region.Bounds;
                var pen = region.RegionType == TileRegionType.Fallback ? fallbackPen : adaptivePen;
                
                // 適応的境界を緑色で描画
                graphics.DrawRectangle(pen, rect);
                
                // 領域情報を描画
                var regionInfo = $"A-{i} ({region.ConfidenceScore:F2})";
                if (region.RegionType == TileRegionType.Fallback)
                {
                    regionInfo = $"F-{i}";
                }

                using var font = new System.Drawing.Font("Arial", 11, System.Drawing.FontStyle.Bold);
                using var brush = new System.Drawing.SolidBrush(region.RegionType == TileRegionType.Fallback ? 
                    System.Drawing.Color.Orange : System.Drawing.Color.LimeGreen);
                using var backgroundBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(220, 0, 0, 0));
                
                var textSize = graphics.MeasureString(regionInfo, font);
                var textRect = new System.Drawing.RectangleF(rect.X + 3, rect.Y + 3, textSize.Width + 4, textSize.Height + 2);
                
                // 背景描画
                graphics.FillRectangle(backgroundBrush, textRect);
                
                // テキスト描画
                graphics.DrawString(regionInfo, font, brush, rect.X + 5, rect.Y + 5);
            }
            
            // 全体境界を赤色破線で描画
            graphics.DrawRectangle(borderPen, 0, 0, width - 1, height - 1);
            
            // 注釈付き画像保存
            annotatedBitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            
            _logger?.LogTrace("🎯 AdaptiveTile 注釈描画完了 - {Count}個の適応領域", regions.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AdaptiveTile 注釈描画エラー");
        }
    }
}