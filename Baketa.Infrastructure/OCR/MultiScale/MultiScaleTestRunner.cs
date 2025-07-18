using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Services.Imaging;

namespace Baketa.Infrastructure.OCR.MultiScale;

/// <summary>
/// マルチスケールOCR処理のテストランナー
/// </summary>
public class MultiScaleTestRunner
{
    private readonly IMultiScaleOcrProcessor _multiScaleProcessor;
    private readonly IOcrEngine _ocrEngine;
    private readonly ILogger<MultiScaleTestRunner> _logger;

    public MultiScaleTestRunner(
        IMultiScaleOcrProcessor multiScaleProcessor,
        IOcrEngine ocrEngine,
        ILogger<MultiScaleTestRunner> logger)
    {
        _multiScaleProcessor = multiScaleProcessor ?? throw new ArgumentNullException(nameof(multiScaleProcessor));
        _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 小文字テキスト認識のマルチスケール効果をテスト
    /// </summary>
    public async Task TestSmallTextRecognitionAsync()
    {
        _logger.LogInformation("🔍 マルチスケールOCR小文字認識テスト開始");

        try
        {
            // テスト用の小さい画像を作成
            var testImage = CreateTestImage();
            
            _logger.LogInformation("📷 テスト画像作成完了: {Width}x{Height}", testImage.Width, testImage.Height);

            // 1. 通常のOCR処理
            _logger.LogInformation("⚪ 通常OCR処理開始");
            var normalStart = DateTime.Now;
            var normalResult = await _ocrEngine.RecognizeAsync(testImage).ConfigureAwait(false);
            var normalTime = DateTime.Now - normalStart;
            
            _logger.LogInformation("⚪ 通常OCR結果: リージョン数={RegionCount}, 処理時間={Time}ms", 
                normalResult.TextRegions.Count, normalTime.TotalMilliseconds);

            // 2. マルチスケールOCR処理
            _logger.LogInformation("🔍 マルチスケールOCR処理開始");
            var multiScaleStart = DateTime.Now;
            var multiScaleDetailResult = await _multiScaleProcessor.ProcessWithDetailsAsync(testImage, _ocrEngine).ConfigureAwait(false);
            var multiScaleTime = DateTime.Now - multiScaleStart;

            _logger.LogInformation("🔍 マルチスケールOCR結果: 統合後リージョン数={MergedRegions}, 処理時間={Time}ms", 
                multiScaleDetailResult.MergedResult.TextRegions.Count, multiScaleTime.TotalMilliseconds);

            // 3. 詳細結果の分析
            LogDetailedResults(normalResult, multiScaleDetailResult);

            // 4. 効果測定
            AnalyzeImprovements(normalResult, multiScaleDetailResult.MergedResult, normalTime, multiScaleTime);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ マルチスケールOCRテスト中にエラーが発生");
        }
    }

    /// <summary>
    /// 詳細結果をログ出力
    /// </summary>
    private void LogDetailedResults(OcrResults normalResult, MultiScaleOcrResult multiScaleResult)
    {
        _logger.LogInformation("📊 詳細結果分析:");
        
        // 各スケールの結果
        foreach (var scaleResult in multiScaleResult.ScaleResults)
        {
            _logger.LogInformation("   スケール {Scale}x: {Regions}リージョン, 信頼度平均: {Confidence:F2}", 
                scaleResult.ScaleFactor, scaleResult.DetectedRegions, scaleResult.AverageConfidence);
        }

        // 統計情報
        var stats = multiScaleResult.Stats;
        _logger.LogInformation("📈 統計情報:");
        _logger.LogInformation("   使用スケール数: {ScaleCount}", stats.ScalesUsed);
        _logger.LogInformation("   統合前リージョン数: {Before}", stats.TotalRegionsBeforeMerge);
        _logger.LogInformation("   統合後リージョン数: {After}", stats.TotalRegionsAfterMerge);
        _logger.LogInformation("   小文字リージョン数: {SmallText}", stats.SmallTextRegions);
        _logger.LogInformation("   改善スコア: {Score:F2}", stats.ImprovementScore);
    }

    /// <summary>
    /// 改善効果を分析
    /// </summary>
    private void AnalyzeImprovements(OcrResults normalResult, OcrResults multiScaleResult, TimeSpan normalTime, TimeSpan multiScaleTime)
    {
        var regionCountImprovement = multiScaleResult.TextRegions.Count - normalResult.TextRegions.Count;
        var processingTimeRatio = multiScaleTime.TotalMilliseconds / normalTime.TotalMilliseconds;

        _logger.LogInformation("🎯 改善効果分析:");
        _logger.LogInformation("   検出リージョン数の変化: {Normal} → {MultiScale} (差分: {Diff})", 
            normalResult.TextRegions.Count, multiScaleResult.TextRegions.Count, regionCountImprovement);
        _logger.LogInformation("   処理時間比: {Ratio:F2}x (通常: {Normal}ms, マルチスケール: {Multi}ms)", 
            processingTimeRatio, normalTime.TotalMilliseconds, multiScaleTime.TotalMilliseconds);

        if (regionCountImprovement > 0)
        {
            _logger.LogInformation("✅ マルチスケール処理により{Count}個のリージョンが追加検出されました", regionCountImprovement);
        }
        else if (regionCountImprovement < 0)
        {
            _logger.LogInformation("⚠️ マルチスケール処理により{Count}個のリージョンが減少しました", Math.Abs(regionCountImprovement));
        }
        else
        {
            _logger.LogInformation("📊 検出リージョン数に変化はありませんでした");
        }

        // 特定テキストの検出確認
        CheckSpecificTextDetection(normalResult, multiScaleResult);
    }

    /// <summary>
    /// 特定テキストの検出を確認
    /// </summary>
    private void CheckSpecificTextDetection(OcrResults normalResult, OcrResults multiScaleResult)
    {
        var targetTexts = new[] { "単体テスト", "E2E", "設計", "データ", "分析" };
        
        _logger.LogInformation("🎯 特定テキスト検出確認:");

        foreach (var targetText in targetTexts)
        {
            var normalContains = normalResult.TextRegions.Any(r => r.Text.Contains(targetText));
            var multiScaleContains = multiScaleResult.TextRegions.Any(r => r.Text.Contains(targetText));

            var status = (normalContains, multiScaleContains) switch
            {
                (true, true) => "✅ 両方で検出",
                (false, true) => "🎯 マルチスケールで新規検出",
                (true, false) => "⚠️ マルチスケールで未検出",
                (false, false) => "❌ 両方で未検出"
            };

            _logger.LogInformation("   '{Text}': {Status}", targetText, status);
        }
    }

    /// <summary>
    /// テスト用画像を作成
    /// </summary>
    private IAdvancedImage CreateTestImage()
    {
        // 実際の画像データの代わりに、テストメタデータを使用
        var testData = System.Text.Encoding.UTF8.GetBytes("MultiScaleTest:SmallText:12px");
        return new AdvancedImage(testData, 800, 600, ImageFormat.Rgb24);
    }
}