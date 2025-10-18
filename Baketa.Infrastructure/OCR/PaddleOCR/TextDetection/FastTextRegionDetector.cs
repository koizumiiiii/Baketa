using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Models.Capture;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Infrastructure.OCR.Scaling;
using Microsoft.Extensions.Logging;
using System.Drawing;
// 🔥 [PHASE_K-29-G] CaptureOptions統合: TextDetectionConfigのみ使用（CaptureOptionsは不使用）

namespace Baketa.Infrastructure.OCR.PaddleOCR.TextDetection;

/// <summary>
/// 高速テキスト領域検出器 - PaddleOCRベースの実際の検出実装（ROI修正版）
/// </summary>
public sealed class FastTextRegionDetector(
    ILogger<FastTextRegionDetector>? logger = null, 
    Baketa.Core.Abstractions.OCR.IOcrEngine? ocrEngine = null,
    Baketa.Core.Abstractions.Factories.IImageFactory? imageFactory = null) : ITextRegionDetector, IDisposable
{
    private TextDetectionConfig _config = new();
    private bool _disposed;

    /// <summary>
    /// 画像からテキスト領域を高速検出
    /// </summary>
    public async Task<IList<Rectangle>> DetectTextRegionsAsync(IWindowsImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        
        try
        {
            logger?.LogDebug("🔍 PaddleOCRベーステキスト領域検出開始: サイズ={Width}x{Height}", image.Width, image.Height);
            
            // 実際のPaddleOCRエンジンを使用した検出（認識スキップで高速化）
            return await DetectRegionsWithPaddleOCRAsync(image).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "❌ PaddleOCRベーステキスト領域検出でエラーが発生");
            
            // フォールバック: エラー時のみ軽量検出を使用
            logger?.LogWarning("⚠️ フォールバック: 軽量グリッド検出を実行");
            return await Task.Run(() => DetectRegionsLightweightFallback(image)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// プレビューモードでの検出（デバッグ情報付き）
    /// </summary>
    public async Task<IList<Rectangle>> DetectWithPreviewAsync(IWindowsImage image, bool showDebugInfo = false)
    {
        var regions = await DetectTextRegionsAsync(image).ConfigureAwait(false);
        
        if (showDebugInfo && logger != null)
        {
            logger.LogInformation("✅ 検出されたテキスト領域数: {Count} (PaddleOCRベース)", regions.Count);
            for (int i = 0; i < regions.Count; i++)
            {
                var rect = regions[i];
                logger.LogDebug("🔍 領域{Index}: ({X},{Y}) サイズ={Width}x{Height}, アスペクト比={AspectRatio:F2}", 
                    i, rect.X, rect.Y, rect.Width, rect.Height, (double)rect.Width / rect.Height);
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
        logger?.LogDebug("テキスト検出設定を更新: MinArea={MinArea}, EdgeThreshold={EdgeThreshold}", 
            _config.MinTextArea, _config.EdgeDetectionThreshold);
    }

    /// <summary>
    /// 現在の検出設定取得
    /// </summary>
    public TextDetectionConfig GetCurrentConfig() => _config;

    /// <summary>
    /// 実際のPaddleOCRエンジンを使用したテキスト領域検出（根本修正）
    /// </summary>
    private async Task<IList<Rectangle>> DetectRegionsWithPaddleOCRAsync(IWindowsImage image)
    {
        if (ocrEngine == null)
        {
            logger?.LogWarning("⚠️ OCRエンジンが注入されていません - フォールバック検出を実行");
            return await Task.Run(() => DetectRegionsLightweightFallback(image)).ConfigureAwait(false);
        }

        Baketa.Core.Abstractions.Imaging.IImage? convertedImage = null;
        try
        {
            // 元画像サイズを記録（座標復元用）
            var originalWidth = image.Width;
            var originalHeight = image.Height;
            
            // IWindowsImage → IImage 変換（バイト配列経由）
            if (imageFactory != null)
            {
                var imageBytes = await image.ToByteArrayAsync().ConfigureAwait(false);
                convertedImage = await imageFactory.CreateFromBytesAsync(imageBytes).ConfigureAwait(false);
            }
            else
            {
                logger?.LogWarning("⚠️ IImageFactoryが注入されていません - フォールバックへ");
                return await Task.Run(() => DetectRegionsLightweightFallback(image)).ConfigureAwait(false);
            }
            
            // スケールファクター計算（座標復元用）
            var convertedWidth = convertedImage.Width;
            var convertedHeight = convertedImage.Height;
            var scaleFactorX = (double)convertedWidth / originalWidth;
            var scaleFactorY = (double)convertedHeight / originalHeight;
            var scaleFactor = Math.Min(scaleFactorX, scaleFactorY); // 縮小率を使用
            
            logger?.LogDebug("🎯 [COORDINATE_FIX] 座標復元情報: 元画像={OriginalWidth}x{OriginalHeight}, 変換後={ConvertedWidth}x{ConvertedHeight}, スケール={ScaleFactor:F3}", 
                originalWidth, originalHeight, convertedWidth, convertedHeight, scaleFactor);
            
            // PaddleOCRの検出専用機能を使用（認識処理をスキップして高速化）
            var ocrResults = await ocrEngine.DetectTextRegionsAsync(convertedImage).ConfigureAwait(false);
            
            if (ocrResults?.TextRegions == null || ocrResults.TextRegions.Count == 0)
            {
                logger?.LogDebug("🔍 PaddleOCR検出結果が空 - 軽量フォールバック実行");
                return await Task.Run(() => DetectRegionsLightweightFallback(image)).ConfigureAwait(false);
            }

            // 🎯 [COORDINATE_FIX] 座標復元処理を追加 - CoordinateRestorerでスケーリング後座標を元座標に復元
            var restoredRegions = ocrResults.TextRegions
                .Select(region => CoordinateRestorer.RestoreTextRegion(region, scaleFactor))
                .Where(region => IsRegionValid(region.Bounds))
                .Select(region => region.Bounds)
                .ToList();
                
            logger?.LogDebug("🎯 [COORDINATE_FIX] 座標復元完了: 検出={DetectionCount}個, 復元後有効={RestoredCount}個", 
                ocrResults.TextRegions.Count, restoredRegions.Count);

            // 近接領域の統合（既存ロジックを活用）
            var mergedRegions = MergeNearbyRegions(restoredRegions);

            logger?.LogInformation("✅ PaddleOCRベーステキスト領域検出完了: {OriginalCount}個 → 復元後{RestoredCount}個 → 統合後{MergedCount}個", 
                ocrResults.TextRegions.Count, restoredRegions.Count, mergedRegions.Count);

            return mergedRegions;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "❌ PaddleOCR検出処理中にエラー");
            throw;
        }
        finally
        {
            // 変換された画像のリソース解放
            convertedImage?.Dispose();
        }
    }

    /// <summary>
    /// 軽量フォールバック検出（改良版：テキスト分断回避）
    /// </summary>
    private List<Rectangle> DetectRegionsLightweightFallback(IWindowsImage image)
    {
        var regions = new List<Rectangle>();
        
        try
        {
            logger?.LogWarning("⚡ テキスト検出失敗 - フォールバック実行: テキスト分断回避のため画面全体を処理");
            
            var width = image.Width;
            var height = image.Height;
            
            // 🔧 修正: 固定グリッド分割でテキスト分断するのを回避
            // 代替案: 画面全体を1つの領域として処理（テキスト完全性を保持）
            var fullScreenRegion = new Rectangle(0, 0, width, height);
            
            if (IsRegionValid(fullScreenRegion))
            {
                regions.Add(fullScreenRegion);
                logger?.LogInformation("✅ フォールバック: 画面全体を単一領域として処理（テキスト分断回避）");
            }
            else
            {
                logger?.LogWarning("⚠️ 画面全体が処理対象外サイズ - テキスト検出をスキップ");
            }
            
            logger?.LogDebug("⚡ フォールバック完了: {Count}個の領域（テキスト分断回避版）", regions.Count);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "❌ フォールバック検出処理中にもエラー");
        }
        
        return regions;
    }

    /// <summary>
    /// 領域の妥当性チェック（設定ベース）
    /// </summary>
    private bool IsRegionValid(Rectangle rect)
    {
        // 最小サイズフィルタリング
        if (rect.Width < _config.MinTextWidth || 
            rect.Height < _config.MinTextHeight ||
            rect.Width * rect.Height < _config.MinTextArea)
        {
            return false;
        }

        // アスペクト比チェック
        float aspectRatio = (float)rect.Width / rect.Height;
        if (aspectRatio < _config.MinAspectRatio || aspectRatio > _config.MaxAspectRatio)
        {
            return false;
        }

        return true;
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
        
        logger?.LogDebug("🧹 FastTextRegionDetector をクリーンアップ（PaddleOCRベース版）");
        _disposed = true;
    }
}
