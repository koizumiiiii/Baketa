using System.Drawing;
using System.IO;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Infrastructure.OCR.Strategies;

/// <summary>
/// 固定グリッド分割戦略（既存ロジック互換）
/// 従来のBatchOcrProcessorロジックを抽出・実装
/// </summary>
public sealed class GridTileStrategy : ITileStrategy
{
    private readonly ILogger<GridTileStrategy>? _logger;

    public string StrategyName => "GridTile";
    public TileStrategyParameters Parameters { get; set; } = new();

    public GridTileStrategy(ILogger<GridTileStrategy>? logger = null)
    {
        _logger = logger;
    }

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

        _logger?.LogDebug("🔍 GridTileStrategy開始 - 画像: {Width}x{Height}, タイルサイズ: {TileSize}", 
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

            _logger?.LogDebug("🔍 単一タイル使用 - サイズ: {Width}x{Height}", image.Width, image.Height);
            
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

        _logger?.LogInformation("🔥 GridTile分割開始 - 元画像: {Width}x{Height}, タイル: {TilesX}x{TilesY} = {Total}個", 
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

                _logger?.LogTrace("🔍 グリッドタイル生成: {RegionId}, 位置: ({X},{Y}), サイズ: {Width}x{Height}", 
                    regionId, startX, startY, width, height);
            }
        }

        _logger?.LogInformation("✅ GridTile分割完了 - 生成領域数: {Count}", regions.Count);

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
                
                _logger?.LogDebug("🎯 GridTile デバッグ画像保存完了: {AnnotatedFile}", annotatedFilename);
            }

            _logger?.LogDebug("🎯 GridTile デバッグキャプチャ完了: {OriginalFile}, 領域数: {Count}", 
                originalFilename, regions.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "GridTile デバッグキャプチャ保存エラー");
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
            
            _logger?.LogTrace("🎯 GridTile 注釈描画完了 - {Count}個のグリッド", regions.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "GridTile 注釈描画エラー");
        }
    }
}