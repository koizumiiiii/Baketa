using System.Drawing;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.PaddleOCR.Diagnostics;

namespace Baketa.Infrastructure.OCR.Strategies;

/// <summary>
/// 固定グリッド分割戦略（既存ロジック互換）
/// 従来のBatchOcrProcessorロジックを抽出・実装
/// </summary>
public sealed class GridTileStrategy(
    ILogger<GridTileStrategy>? logger = null,
    IOptions<AdvancedSettings>? advancedOptions = null,
    ImageDiagnosticsSaver? diagnosticsSaver = null) : ITileStrategy
{
    private readonly AdvancedSettings _advancedSettings = advancedOptions?.Value ?? new();
    
    public string StrategyName => "GridTile";
    public TileStrategyParameters Parameters { get; set; } = new();

    /// <summary>
    /// 固定グリッドによるタイル分割
    /// BatchOcrProcessor.SplitImageIntoOptimalTilesAsync の移植版
    /// </summary>
    public async Task<List<TileRegion>> GenerateRegionsAsync(
        IAdvancedImage image, 
        TileGenerationOptions options, 
        CancellationToken cancellationToken = default)
    {
        var tileSize = Parameters.TileSize ?? options.DefaultTileSize;
        var regions = new List<TileRegion>();

        logger?.LogDebug("🔍 GridTileStrategy開始 - 画像: {Width}x{Height}, タイルサイズ: {TileSize}", 
            image.Width, image.Height, tileSize);

        // 画像サイズがタイルサイズより小さい場合はそのまま使用
        if (image.Width <= tileSize && image.Height <= tileSize)
        {
            var singleRegion = new TileRegion
            {
                Bounds = new Rectangle(0, 0, image.Width, image.Height),
                RegionType = TileRegionType.Grid,
                RegionId = "grid-single",
                ConfidenceScore = 1.0,
                Metadata = { ["IsSingleTile"] = true }
            };

            regions.Add(singleRegion);

            logger?.LogDebug("🔍 単一タイル使用 - サイズ: {Width}x{Height}", image.Width, image.Height);
            
            // デバッグキャプチャ
            if (options.EnableDebugCapture)
            {
                await SaveDebugCaptureAsync(image, regions, "no-split", options.DebugCapturePath).ConfigureAwait(false);
            }

            return regions;
        }

        // X方向とY方向のタイル数を計算
        var tilesX = (int)Math.Ceiling((double)image.Width / tileSize);
        var tilesY = (int)Math.Ceiling((double)image.Height / tileSize);

        logger?.LogInformation("🔥 GridTile分割開始 - 元画像: {Width}x{Height}, タイル: {TilesX}x{TilesY} = {Total}個", 
            image.Width, image.Height, tilesX, tilesY, tilesX * tilesY);

        // グリッド分割実行
        for (var y = 0; y < tilesY; y++)
        {
            for (var x = 0; x < tilesX; x++)
            {
                var startX = x * tileSize;
                var startY = y * tileSize;
                var width = Math.Min(tileSize, image.Width - startX);
                var height = Math.Min(tileSize, image.Height - startY);

                var bounds = new Rectangle(startX, startY, width, height);
                var regionId = $"grid-{x}-{y}";

                var region = new TileRegion
                {
                    Bounds = bounds,
                    RegionType = TileRegionType.Grid,
                    RegionId = regionId,
                    ConfidenceScore = 1.0,
                    Metadata = 
                    {
                        ["GridX"] = x,
                        ["GridY"] = y,
                        ["TileSize"] = tileSize,
                        ["TilesX"] = tilesX,
                        ["TilesY"] = tilesY
                    }
                };

                regions.Add(region);

                logger?.LogTrace("🔍 グリッドタイル生成: {RegionId}, 位置: ({X},{Y}), サイズ: {Width}x{Height}", 
                    regionId, startX, startY, width, height);
                
                // ROI画像出力（設定が有効な場合）
                if (_advancedSettings.EnableRoiImageOutput && diagnosticsSaver != null)
                {
                    await SaveRoiImageAsync(image, region, regionId).ConfigureAwait(false);
                }
            }
        }

        logger?.LogInformation("✅ GridTile分割完了 - 生成領域数: {Count}", regions.Count);

        // デバッグキャプチャ
        if (options.EnableDebugCapture)
        {
            await SaveDebugCaptureAsync(image, regions, $"split-{tilesX}x{tilesY}", options.DebugCapturePath).ConfigureAwait(false);
        }

        return regions;
    }

    /// <summary>
    /// デバッグキャプチャ保存（GridTileStrategy用）
    /// </summary>
    private async Task SaveDebugCaptureAsync(
        IAdvancedImage image, 
        List<TileRegion> regions, 
        string suffix, 
        string? debugPath)
    {
        try
        {
            var capturePath = debugPath ?? "E:\\dev\\Baketa\\debug_captures";
            if (!Directory.Exists(capturePath))
            {
                Directory.CreateDirectory(capturePath);
            }

            var imageBytes = await image.ToByteArrayAsync().ConfigureAwait(false);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");

            // 元画像保存
            var originalFilename = $"grid-debug-original_{timestamp}_{suffix}_{image.Width}x{image.Height}.png";
            var originalPath = Path.Combine(capturePath, originalFilename);
            await File.WriteAllBytesAsync(originalPath, imageBytes).ConfigureAwait(false);

            // 注釈付き画像生成
            if (regions.Count > 1)
            {
                var annotatedFilename = $"grid-debug-annotated_{timestamp}_{suffix}_{image.Width}x{image.Height}.png";
                var annotatedPath = Path.Combine(capturePath, annotatedFilename);
                
                await CreateAnnotatedImageAsync(imageBytes, regions, image.Width, image.Height, annotatedPath).ConfigureAwait(false);
                
                logger?.LogDebug("🎯 GridTile デバッグ画像保存完了: {AnnotatedFile}", annotatedFilename);
            }

            logger?.LogDebug("🎯 GridTile デバッグキャプチャ完了: {OriginalFile}, 領域数: {Count}", 
                originalFilename, regions.Count);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "GridTile デバッグキャプチャ保存エラー");
        }
    }

    /// <summary>
    /// 注釈付き画像作成（GridTileStrategy用）
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
            
            // グリッドタイル境界線描画（青色、太い線）
            using var gridPen = new System.Drawing.Pen(System.Drawing.Color.Blue, 3.0f);
            using var borderPen = new System.Drawing.Pen(System.Drawing.Color.Yellow, 2.0f) 
            { 
                DashStyle = System.Drawing.Drawing2D.DashStyle.Dash 
            };
            
            for (int i = 0; i < regions.Count; i++)
            {
                var region = regions[i];
                var rect = region.Bounds;
                
                // グリッド境界を青い実線で描画
                graphics.DrawRectangle(gridPen, rect);
                
                // グリッド位置情報を描画
                var gridInfo = $"Grid-{region.Metadata.GetValueOrDefault("GridX", "?")}-{region.Metadata.GetValueOrDefault("GridY", "?")}";
                using var font = new System.Drawing.Font("Arial", 12, System.Drawing.FontStyle.Bold);
                using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Blue);
                using var backgroundBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(200, 255, 255, 255));
                
                var textSize = graphics.MeasureString(gridInfo, font);
                var textRect = new System.Drawing.RectangleF(rect.X + 3, rect.Y + 3, textSize.Width + 2, textSize.Height + 1);
                
                // 背景描画
                graphics.FillRectangle(backgroundBrush, textRect);
                
                // テキスト描画
                graphics.DrawString(gridInfo, font, brush, rect.X + 4, rect.Y + 4);
            }
            
            // 全体境界を黄色破線で描画
            graphics.DrawRectangle(borderPen, 0, 0, width - 1, height - 1);
            
            // 注釈付き画像保存
            annotatedBitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            
            logger?.LogTrace("🎯 GridTile 注釈描画完了 - {Count}個のグリッド", regions.Count);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "GridTile 注釈描画エラー");
        }
    }
    
    /// <summary>
    /// ROI画像保存（GridTileStrategy用）
    /// </summary>
    private async Task SaveRoiImageAsync(IAdvancedImage sourceImage, TileRegion region, string regionId)
    {
        try
        {
            // 🎯 改善されたROI画像保存処理
            if (diagnosticsSaver == null)
            {
                System.Diagnostics.Debug.WriteLine($"GridTile: diagnosticsSaver is null, ROI保存スキップ: {regionId}");
                return;
            }
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
            var fileName = $"{timestamp}_grid_roi_{regionId}.png";
            
            // 🎯 実際の画像保存をImageDiagnosticsSaverに委任
            var imageBytes = await ExtractRoiImageAsync(sourceImage, region).ConfigureAwait(false);
            if (imageBytes != null && imageBytes.Length > 0)
            {
                var outputPath = Path.Combine(GetDiagnosticOutputPath(), fileName);
                
                // 🎯 ROI画像を直接保存（byte[]からファイルへ）
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllBytesAsync(outputPath, imageBytes).ConfigureAwait(false);
                
                // 🎯 ROI画像情報をBatchOcrProcessorに通知（イベント使用）
                await NotifyRoiImageSavedAsync(regionId, outputPath, region, imageBytes.Length)
                    .ConfigureAwait(false);
                
                System.Diagnostics.Debug.WriteLine($"GridTile ROI画像保存完了: {fileName} ({imageBytes.Length} bytes)");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"GridTile ROI画像抽出失敗: {regionId}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GridTile ROI保存エラー: {regionId} - {ex.Message}");
        }
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
            logger?.LogWarning(ex, "GridTile ROI画像抽出エラー");
            return null;
        }
    }
    
    /// <summary>
    /// ROI画像保存完了通知
    /// </summary>
    private async Task NotifyRoiImageSavedAsync(string regionId, string filePath, TileRegion region, long imageSizeBytes)
    {
        try
        {
            // 🎯 静的アクセスによるROI情報蓄積（BatchOcrProcessor統合用）
            var roiInfo = new TileRoiImageInfo
            {
                RegionId = regionId,
                Strategy = StrategyName,
                FilePath = filePath,
                Bounds = region.Bounds,
                ImageSizeBytes = imageSizeBytes,
                SavedAt = DateTime.UtcNow,
                ConfidenceScore = region.ConfidenceScore,
                Metadata = new Dictionary<string, object>(region.Metadata)
            };
            
            // 🎯 グローバルROI情報コレクションに追加
            GlobalRoiImageCollection.AddRoiImage(roiInfo);
            
            logger?.LogDebug("🎯 GridTile ROI保存通知完了: {RegionId}", regionId);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "GridTile ROI保存通知エラー");
        }
    }
    
    /// <summary>
    /// 診断出力パスを取得
    /// </summary>
    private string GetDiagnosticOutputPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Baketa", "ROI", "GridTile");
    }
}
