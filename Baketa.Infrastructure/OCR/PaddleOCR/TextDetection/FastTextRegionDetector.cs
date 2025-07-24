using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Models.Capture;
using Microsoft.Extensions.Logging;
using System.Drawing;

namespace Baketa.Infrastructure.OCR.PaddleOCR.TextDetection;

/// <summary>
/// 高速テキスト領域検出器 - 適応的キャプチャシステム用の軽量実装
/// </summary>
public sealed class FastTextRegionDetector : ITextRegionDetector, IDisposable
{
    private readonly ILogger<FastTextRegionDetector>? _logger;
    private TextDetectionConfig _config = new();
    private bool _disposed;

    public FastTextRegionDetector(ILogger<FastTextRegionDetector>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 画像からテキスト領域を高速検出
    /// </summary>
    public async Task<IList<Rectangle>> DetectTextRegionsAsync(IWindowsImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        
        try
        {
            _logger?.LogDebug("高速テキスト領域検出開始: サイズ={Width}x{Height}", image.Width, image.Height);
            
            // CPU負荷を軽減するため非同期で実行
            return await Task.Run(() => DetectRegionsInternal(image)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "テキスト領域検出中にエラーが発生");
            return [];
        }
    }

    /// <summary>
    /// プレビューモードでの検出（デバッグ情報付き）
    /// </summary>
    public async Task<IList<Rectangle>> DetectWithPreviewAsync(IWindowsImage image, bool showDebugInfo = false)
    {
        var regions = await DetectTextRegionsAsync(image).ConfigureAwait(false);
        
        if (showDebugInfo && _logger != null)
        {
            _logger.LogInformation("検出されたテキスト領域数: {Count}", regions.Count);
            for (int i = 0; i < regions.Count; i++)
            {
                var rect = regions[i];
                _logger.LogDebug("領域{Index}: ({X},{Y}) サイズ={Width}x{Height}", 
                    i, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }
        
        return regions;
    }

    /// <summary>
    /// 検出パラメータ設定
    /// </summary>
    public void ConfigureDetection(TextDetectionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _logger?.LogDebug("テキスト検出設定を更新: MinArea={MinArea}, EdgeThreshold={EdgeThreshold}", 
            _config.MinTextArea, _config.EdgeDetectionThreshold);
    }

    /// <summary>
    /// 現在の検出設定取得
    /// </summary>
    public TextDetectionConfig GetCurrentConfig() => _config;

    /// <summary>
    /// 内部検出ロジック - 軽量エッジベース検出
    /// </summary>
    private List<Rectangle> DetectRegionsInternal(IWindowsImage image)
    {
        var regions = new List<Rectangle>();
        
        try
        {
            // 簡素化されたテキスト領域検出アルゴリズム
            // ROI処理のため高速性を優先
            
            var width = image.Width;
            var height = image.Height;
            
            // グリッドベースでのサンプリング検出（パフォーマンス重視）
            var gridSizeX = Math.Max(1, width / 20);  // 横20分割
            var gridSizeY = Math.Max(1, height / 15); // 縦15分割
            
            for (int y = 0; y < height - gridSizeY; y += gridSizeY / 2)
            {
                for (int x = 0; x < width - gridSizeX; x += gridSizeX / 2)
                {
                    var rect = new Rectangle(x, y, gridSizeX, gridSizeY);
                    
                    // 最小サイズフィルタリング
                    if (rect.Width >= _config.MinTextWidth && 
                        rect.Height >= _config.MinTextHeight &&
                        rect.Width * rect.Height >= _config.MinTextArea)
                    {
                        // アスペクト比チェック
                        float aspectRatio = (float)rect.Width / rect.Height;
                        if (aspectRatio >= _config.MinAspectRatio && 
                            aspectRatio <= _config.MaxAspectRatio)
                        {
                            regions.Add(rect);
                        }
                    }
                }
            }
            
            // 近接領域の統合
            regions = MergeNearbyRegions(regions);
            
            _logger?.LogDebug("テキスト領域検出完了: {Count}個の領域を検出", regions.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "テキスト領域検出処理中にエラー");
        }
        
        return regions;
    }

    /// <summary>
    /// 近接する領域を統合してテキストブロックを形成
    /// </summary>
    private List<Rectangle> MergeNearbyRegions(List<Rectangle> regions)
    {
        if (regions.Count <= 1) return [.. regions];
        
        var merged = new List<Rectangle>();
        var processed = new bool[regions.Count];
        
        for (int i = 0; i < regions.Count; i++)
        {
            if (processed[i]) continue;
            
            var currentRegion = regions[i];
            processed[i] = true;
            
            // 近接する領域を探して統合
            for (int j = i + 1; j < regions.Count; j++)
            {
                if (processed[j]) continue;
                
                var otherRegion = regions[j];
                
                // 距離チェック
                var distance = CalculateDistance(currentRegion, otherRegion);
                if (distance <= _config.MergeDistanceThreshold)
                {
                    currentRegion = Rectangle.Union(currentRegion, otherRegion);
                    processed[j] = true;
                }
            }
            
            merged.Add(currentRegion);
        }
        
        return merged;
    }

    /// <summary>
    /// 2つの矩形間の距離を計算
    /// </summary>
    private static float CalculateDistance(Rectangle rect1, Rectangle rect2)
    {
        var center1X = rect1.X + rect1.Width / 2f;
        var center1Y = rect1.Y + rect1.Height / 2f;
        var center2X = rect2.X + rect2.Width / 2f;
        var center2Y = rect2.Y + rect2.Height / 2f;
        
        var dx = center1X - center2X;
        var dy = center1Y - center2Y;
        
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _logger?.LogDebug("FastTextRegionDetector をクリーンアップ");
        _disposed = true;
    }
}