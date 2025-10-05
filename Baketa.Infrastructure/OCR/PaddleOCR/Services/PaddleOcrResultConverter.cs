using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;
using Microsoft.Extensions.Logging;
using Sdcb.PaddleOCR;
using System.Drawing;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Services;

/// <summary>
/// PaddleOCR結果の変換、座標復元、テキスト結合を担当するサービス
/// Phase 2.8: PaddleOcrEngineから抽出された結果変換実装
///
/// 🔧 [SKELETON_IMPL] 現在はスケルトン実装
/// 将来の完全実装時に追加予定（Phase 2.9）:
/// - CharacterSimilarityCorrector統合（文字形状類似性補正）
/// - CoordinateRestorer統合（座標復元）
/// - ITextMerger統合（テキスト結合）
/// - IOcrPostProcessor統合（OCR後処理）
/// - リフレクション処理の完全実装
/// - ROI座標調整の詳細ロジック
/// </summary>
public sealed class PaddleOcrResultConverter : IPaddleOcrResultConverter
{
    private readonly ILogger<PaddleOcrResultConverter>? _logger;

    public PaddleOcrResultConverter(ILogger<PaddleOcrResultConverter>? logger = null)
    {
        _logger = logger;
        _logger?.LogInformation("🚀 PaddleOcrResultConverter初期化完了");
    }

    #region IPaddleOcrResultConverter実装

    /// <summary>
    /// PaddleOCR結果をOcrTextRegionに変換
    /// </summary>
    public IReadOnlyList<OcrTextRegion> ConvertToTextRegions(
        PaddleOcrResult[] paddleResults,
        double scaleFactor,
        Rectangle? roi)
    {
        _logger?.LogDebug("🔄 ConvertToTextRegions開始: 結果数={Count}, ScaleFactor={ScaleFactor}, ROI={Roi}",
            paddleResults.Length, scaleFactor, roi);

        var textRegions = new List<OcrTextRegion>();

        try
        {
            // 🎯 [PHASE2.8_SKELETON] 基本的な変換ロジック
            foreach (var paddleResult in paddleResults)
            {
                if (paddleResult?.Regions == null || paddleResult.Regions.Length == 0)
                {
                    continue;
                }

                foreach (var region in paddleResult.Regions)
                {
                    // 🔧 [TODO_PHASE2.9] ProcessPaddleRegion完全実装
                    var textRegion = ConvertRegionSimplified(region, scaleFactor, roi);
                    if (textRegion != null)
                    {
                        textRegions.Add(textRegion);
                    }
                }
            }

            _logger?.LogDebug("✅ ConvertToTextRegions完了: 変換領域数={Count}", textRegions.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ ConvertToTextRegions失敗");
            throw;
        }

        return textRegions;
    }

    /// <summary>
    /// 検出専用結果の変換
    /// </summary>
    public IReadOnlyList<OcrTextRegion> ConvertDetectionOnlyResult(PaddleOcrResult[] paddleResults)
    {
        _logger?.LogDebug("⚡ ConvertDetectionOnlyResult開始: 結果数={Count}", paddleResults.Length);

        var textRegions = new List<OcrTextRegion>();

        try
        {
            // 🎯 [PHASE2.8_SKELETON] 検出専用変換（テキストなし）
            foreach (var paddleResult in paddleResults)
            {
                if (paddleResult?.Regions == null || paddleResult.Regions.Length == 0)
                {
                    continue;
                }

                foreach (var region in paddleResult.Regions)
                {
                    // 🔧 [TODO_PHASE2.9] ProcessSinglePaddleResultForDetectionOnly完全実装
                    var textRegion = ConvertRegionDetectionOnly(region);
                    if (textRegion != null)
                    {
                        textRegions.Add(textRegion);
                    }
                }
            }

            _logger?.LogDebug("✅ ConvertDetectionOnlyResult完了: 検出領域数={Count}", textRegions.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ ConvertDetectionOnlyResult失敗");
            throw;
        }

        return textRegions;
    }

    /// <summary>
    /// 空結果の作成
    /// </summary>
    public OcrResults CreateEmptyResult(IImage image, Rectangle? roi, TimeSpan processingTime)
    {
        _logger?.LogDebug("📝 CreateEmptyResult: Image={Width}x{Height}, ROI={Roi}, ProcessingTime={Time}ms",
            image.Width, image.Height, roi, processingTime.TotalMilliseconds);

        return new OcrResults(
            textRegions: Array.Empty<OcrTextRegion>(),
            sourceImage: image,
            processingTime: processingTime,
            languageCode: "Unknown",
            regionOfInterest: roi
        );
    }

    #endregion

    #region Privateヘルパーメソッド（簡略版）

    /// <summary>
    /// 単一領域を変換（簡略版）
    /// 🔧 [TODO_PHASE2.9] 完全実装：CharacterSimilarityCorrector, スケーリング, ROI座標調整
    /// </summary>
    private OcrTextRegion? ConvertRegionSimplified(
        PaddleOcrResultRegion region,
        double scaleFactor,
        Rectangle? roi)
    {
        try
        {
            // 基本的なバウンディングボックス計算
            var bounds = CalculateBoundingBoxFromRegion(region.Rect.Points());

            // スケーリング適用
            // 🔧 [GEMINI_REVIEW] Math.Roundで丸め処理を明確化（1ピクセル誤差回避）
            if (Math.Abs(scaleFactor - 1.0) > 0.001)
            {
                bounds = new Rectangle(
                    (int)Math.Round(bounds.X / scaleFactor),
                    (int)Math.Round(bounds.Y / scaleFactor),
                    (int)Math.Round(bounds.Width / scaleFactor),
                    (int)Math.Round(bounds.Height / scaleFactor)
                );
            }

            // ROI座標調整
            if (roi.HasValue)
            {
                bounds = new Rectangle(
                    bounds.X + roi.Value.X,
                    bounds.Y + roi.Value.Y,
                    bounds.Width,
                    bounds.Height
                );
            }

            return new OcrTextRegion(
                text: region.Text ?? string.Empty,
                bounds: bounds,
                confidence: 0.0, // 🔧 [TODO_PHASE2.9] Confidenceプロパティ実装予定
                contour: null
            );
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "⚠️ ConvertRegionSimplified失敗: Text={Text}", region.Text);
            return null;
        }
    }

    /// <summary>
    /// 検出専用領域変換（簡略版）
    /// 🔧 [TODO_PHASE2.9] 完全実装
    /// </summary>
    private OcrTextRegion? ConvertRegionDetectionOnly(PaddleOcrResultRegion region)
    {
        try
        {
            var bounds = CalculateBoundingBoxFromRegion(region.Rect.Points());

            return new OcrTextRegion(
                text: string.Empty, // 検出専用なのでテキストは空
                bounds: bounds,
                confidence: 0.0,
                contour: null
            );
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "⚠️ ConvertRegionDetectionOnly失敗");
            return null;
        }
    }

    /// <summary>
    /// バウンディングボックス計算（OpenCvSharp.Point2f[]版）
    /// 🔧 [COPIED_FROM_ENGINE] PaddleOcrEngine.CalculateBoundingBoxFromRegion
    /// </summary>
    private static Rectangle CalculateBoundingBoxFromRegion(OpenCvSharp.Point2f[] region)
    {
        if (region == null || region.Length == 0)
        {
            return Rectangle.Empty;
        }

        float minX = region.Min(p => p.X);
        float minY = region.Min(p => p.Y);
        float maxX = region.Max(p => p.X);
        float maxY = region.Max(p => p.Y);

        return new Rectangle(
            (int)Math.Floor(minX),
            (int)Math.Floor(minY),
            (int)Math.Ceiling(maxX - minX),
            (int)Math.Ceiling(maxY - minY)
        );
    }

    #endregion
}
