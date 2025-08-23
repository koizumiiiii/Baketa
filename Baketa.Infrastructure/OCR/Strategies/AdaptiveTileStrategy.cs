using System.Drawing;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.PaddleOCR.Diagnostics;

namespace Baketa.Infrastructure.OCR.Strategies;

/// <summary>
/// テキスト検出ベース適応的分割戦略
/// PaddleOCR検出APIを活用したテキスト境界保護分割
/// </summary>
public sealed class AdaptiveTileStrategy(
    IOcrEngine textDetector,
    ILogger<AdaptiveTileStrategy> logger,
    IOptions<AdvancedSettings>? advancedOptions = null,
    ImageDiagnosticsSaver? diagnosticsSaver = null) : ITileStrategy
{
    private readonly IOcrEngine _textDetector = textDetector ?? throw new ArgumentNullException(nameof(textDetector));
    private readonly ILogger<AdaptiveTileStrategy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly AdvancedSettings _advancedSettings = advancedOptions?.Value ?? new();
    private readonly ImageDiagnosticsSaver? _diagnosticsSaver = diagnosticsSaver;

    public string StrategyName => "AdaptiveTile";
    public TileStrategyParameters Parameters { get; set; } = new();

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
                _logger?.LogWarning("⚠️ テキスト検出結果が空 - 文字分割回避のため、時間はかかるが全画面OCR処理を継続");
                
                // 🎯 [PROPER_APPROACH] テキスト分割回避のため、全画面を一つの領域として処理
                // グリッド分割は文字を分断するため使用しない
                return GenerateFullScreenRegion(image);
            }

            _logger?.LogDebug("🔍 テキスト検出完了 - 検出領域数: {Count}", detectionResult.TextRegions.Count);

            // Phase 2: バウンディングボックス統合
            var mergedRegions = MergeBoundingBoxes(
[..detectionResult.TextRegions], Parameters);

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
            _logger?.LogError(ex, "❌ 適応的分割処理でエラー発生、空の領域リストを返却");
            return [];
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
        
        // 🔍 [DEBUG] 除去されたテキスト内容の確認
        var removedRegions = textRegions.Where(r => !filteredRegions.Contains(r)).ToList();
        foreach (var removed in removedRegions.Take(5)) // 最初の5個だけログ出力
        {
            _logger?.LogDebug("❌ [NOISE_FILTER] 除去されたテキスト: '{Text}' (信頼度: {Confidence}, 領域: {Width}×{Height})", 
                removed.Text, removed.Confidence, removed.Bounds.Width, removed.Bounds.Height);
        }

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
                var region = new TileRegion
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
                };
                
                // 🔍 [DEBUG] 作成されたTileRegionの詳細ログ
                var sourceTexts = string.Join(" | ", lineGroup.Select(r => r.Text.Length > 20 ? r.Text[..20] + "..." : r.Text));
                _logger?.LogDebug("✅ [TILE_REGION] 作成: ID={RegionId}, 範囲={X},{Y} ({Width}×{Height}), 信頼度={Confidence:F3}, 含有テキスト=[{SourceTexts}]", 
                    region.RegionId, mergedBounds.X, mergedBounds.Y, mergedBounds.Width, mergedBounds.Height, region.ConfidenceScore, sourceTexts);
                
                mergedRegions.Add(region);
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
        return [..regions.Where(region =>
        {
            var area = region.Bounds.Width * region.Bounds.Height;
            var hasMinArea = area >= parameters.MinBoundingBoxArea;
            var hasMinConfidence = region.Confidence >= parameters.MinConfidenceThreshold;
            
            return hasMinArea && hasMinConfidence;
        })];
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
                
                // ROI画像保存（設定が有効な場合）
                if (_advancedSettings.EnableRoiImageOutput && _diagnosticsSaver != null)
                {
                    foreach (var adjustedRegion in adjustedRegions)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await SaveRoiImageAsync(image, adjustedRegion, adjustedRegion.RegionId).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogWarning(ex, "AdaptiveTile ROI画像保存エラー - 領域: {RegionId}", adjustedRegion.RegionId);
                            }
                        });
                    }
                }
            }
        }

        // 最大領域数制限
        if (validatedRegions.Count > options.MaxRegionCount)
        {
            _logger?.LogWarning("領域数が制限を超過、信頼度順でトリミング: {Count} → {Max}",
                validatedRegions.Count, options.MaxRegionCount);

            validatedRegions = [..validatedRegions
                .OrderByDescending(r => r.ConfidenceScore)
                .Take(options.MaxRegionCount)];
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

        // 巨大すぎる領域は分割（オーバーフロー防止でlong計算→int変換、浮動小数点精度保持）
        var imageArea = (long)image.Width * image.Height;
        var scaledMaxArea = (long)(imageArea * parameters.MaxRegionSizeRatio); // 浮動小数点計算を分離
        var maxArea = (int)Math.Min(int.MaxValue, scaledMaxArea);
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
        
        // 最適な分割サイズを計算（オーバーフロー防止でlong計算→int変換、浮動小数点精度保持）
        var baseArea = (long)image.Width * image.Height;
        var scaledArea = (long)(baseArea * parameters.MaxRegionSizeRatio * 0.7); // 浮動小数点計算を分離
        var targetArea = (int)Math.Min(int.MaxValue, scaledArea); // 余裕をもたせる
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
            // フォールバック処理削除により不要
            using var borderPen = new System.Drawing.Pen(System.Drawing.Color.Red, 2.0f) 
            { 
                DashStyle = System.Drawing.Drawing2D.DashStyle.Dash 
            };
            
            for (int i = 0; i < regions.Count; i++)
            {
                var region = regions[i];
                var rect = region.Bounds;
                var pen = adaptivePen;
                
                // 適応的境界を緑色で描画
                graphics.DrawRectangle(pen, rect);
                
                // 領域情報を描画
                var regionInfo = $"A-{i} ({region.ConfidenceScore:F2})";

                using var font = new System.Drawing.Font("Arial", 11, System.Drawing.FontStyle.Bold);
                using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.LimeGreen);
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
    
    /// <summary>
    /// ROI画像保存（AdaptiveTileStrategy用）
    /// </summary>
    private async Task SaveRoiImageAsync(IAdvancedImage sourceImage, TileRegion region, string regionId)
    {
        try
        {
            // ROI画像保存機能（診断設定で有効な場合のみ）
            // 注意：現在の実装では画像保存を簡略化
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
            var fileName = $"{timestamp}_adaptive_roi_{regionId}.txt";
            
            // 基本的なメタデータのみテキストファイルとして保存
            var metadata = new Dictionary<string, object>
            {
                ["RegionId"] = regionId,
                ["Strategy"] = "AdaptiveTile",
                ["Bounds"] = $"{region.Bounds.X},{region.Bounds.Y},{region.Bounds.Width},{region.Bounds.Height}",
                ["Timestamp"] = DateTime.UtcNow.ToString("O")
            };

            var metadataContent = string.Join("\n", metadata.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
            var outputPath = Path.Combine(GetDiagnosticOutputPath(), fileName);
            
            // ディレクトリ作成と保存を並列実行
            await Task.Run(async () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllTextAsync(outputPath, metadataContent).ConfigureAwait(false);
            }).ConfigureAwait(false);
            
            // ログは基本的なもののみ出力
            System.Diagnostics.Debug.WriteLine($"AdaptiveTile ROI情報保存完了: {regionId}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AdaptiveTile ROI保存エラー: {regionId} - {ex.Message}");
        }
    }
    
    /// <summary>
    /// 診断出力パスを取得
    /// </summary>
    private string GetDiagnosticOutputPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Baketa", "ROI", "AdaptiveTile");
    }
    
    /// <summary>
    /// ROI画像抽出（指定領域のみを切り出し）
    /// </summary>
    private async Task<byte[]?> ExtractRoiImageAsync(IAdvancedImage sourceImage, TileRegion region)
    {
        try
        {
            // 元画像をバイト配列に変換
            var sourceBytes = await sourceImage.ToByteArrayAsync().ConfigureAwait(false);
            if (sourceBytes == null || sourceBytes.Length == 0) return null;
            
            // 元画像からROI領域を切り出し
            using var memoryStream = new MemoryStream(sourceBytes);
            using var sourceBitmap = new System.Drawing.Bitmap(memoryStream);
            using var roiBitmap = new System.Drawing.Bitmap(region.Bounds.Width, region.Bounds.Height);
            using var graphics = System.Drawing.Graphics.FromImage(roiBitmap);
            
            // 高品質描画設定
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            
            // ROI領域を切り出し
            var destRect = new Rectangle(0, 0, region.Bounds.Width, region.Bounds.Height);
            graphics.DrawImage(sourceBitmap, destRect, region.Bounds, GraphicsUnit.Pixel);
            
            // ROI画像をバイト配列に変換
            using var outputStream = new MemoryStream();
            var imageFormat = _advancedSettings.RoiImageFormat switch
            {
                RoiImageFormat.Jpeg => System.Drawing.Imaging.ImageFormat.Jpeg,
                RoiImageFormat.Bmp => System.Drawing.Imaging.ImageFormat.Bmp,
                _ => System.Drawing.Imaging.ImageFormat.Png
            };
            
            roiBitmap.Save(outputStream, imageFormat);
            return outputStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AdaptiveTile ROI画像抽出エラー");
            return null;
        }
    }

    /// <summary>
    /// 全画面OCR処理戦略
    /// テキスト検出失敗時に文字分割を回避して全画面を一つの領域として処理
    /// </summary>
    private List<TileRegion> GenerateFullScreenRegion(IAdvancedImage image)
    {
        _logger?.LogInformation("🎯 [PROPER_APPROACH] 全画面OCR戦略を開始 - 画像: {Width}x{Height} (文字分割回避)", 
            image.Width, image.Height);

        // 全画面を一つの領域として処理
        var fullScreenBounds = new Rectangle(0, 0, image.Width, image.Height);
        
        var region = new TileRegion
        {
            Bounds = fullScreenBounds,
            RegionType = TileRegionType.Composite, // 全画面複合領域
            RegionId = $"fullscreen-{DateTime.UtcNow.Ticks}",
            ConfidenceScore = 0.8, // 高い信頼度（文字分割リスクなし）
            Metadata = 
            {
                ["Strategy"] = "FullScreenOCR",
                ["Reason"] = "TextDetectionFailed_AvoidCharacterSplitting",
                ["ProcessingMode"] = "SingleRegionComplete",
                ["ExpectedBehavior"] = "SlowerButAccurate"
            }
        };
        
        _logger?.LogInformation("✅ [PROPER_APPROACH] 全画面OCR領域生成完了 - 1つの完全な領域 (時間はかかるが正確)");
        
        return [region];
    }
}
