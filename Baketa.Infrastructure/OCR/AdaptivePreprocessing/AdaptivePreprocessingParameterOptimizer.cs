using System.Diagnostics;
using Baketa.Core.Abstractions.Imaging;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.AdaptivePreprocessing;

/// <summary>
/// 適応的前処理パラメータ最適化の実装クラス
/// </summary>
public class AdaptivePreprocessingParameterOptimizer(
    IImageQualityAnalyzer imageQualityAnalyzer,
    ILogger<AdaptivePreprocessingParameterOptimizer> logger) : IAdaptivePreprocessingParameterOptimizer
{

    /// <summary>
    /// 画像特性に基づいて最適な前処理パラメータを決定します
    /// </summary>
    public async Task<AdaptivePreprocessingParameters> OptimizeParametersAsync(IAdvancedImage image)
    {
        var result = await OptimizeWithDetailsAsync(image).ConfigureAwait(false);
        return result.Parameters;
    }

    /// <summary>
    /// 画像品質指標に基づいて前処理パラメータを調整します
    /// </summary>
    public async Task<AdaptivePreprocessingParameters> AdjustParametersAsync(
        ImageQualityMetrics qualityMetrics,
        TextDensityMetrics textDensityMetrics)
    {
        return await Task.Run(() =>
        {
            logger.LogDebug("パラメータ調整開始: 品質={Quality:F3}, エッジ密度={EdgeDensity:F3}",
                qualityMetrics.OverallQuality, textDensityMetrics.EdgeDensity);

            var parameters = new AdaptivePreprocessingParameters();

            // 画像品質に基づくパラメータ調整
            parameters = AdjustForImageQuality(parameters, qualityMetrics);

            // テキスト特性に基づくパラメータ調整
            parameters = AdjustForTextCharacteristics(parameters, textDensityMetrics);

            // 総合的な最適化
            parameters = ApplyFinalOptimization(parameters, qualityMetrics, textDensityMetrics);

            logger.LogInformation(
                "パラメータ調整完了: γ={Gamma:F2}, C={Contrast:F2}, B={Brightness:F2}, NR={NoiseReduction:F2}",
                parameters.Gamma, parameters.Contrast, parameters.Brightness, parameters.NoiseReduction);

            return parameters;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// パラメータ最適化の詳細結果を取得します
    /// </summary>
    public async Task<AdaptivePreprocessingResult> OptimizeWithDetailsAsync(IAdvancedImage image)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            logger.LogInformation("適応的前処理最適化開始: {Width}x{Height}", image.Width, image.Height);

            // 画像品質とテキスト密度を並列分析
            var qualityTask = imageQualityAnalyzer.AnalyzeAsync(image);
            var textDensityTask = imageQualityAnalyzer.AnalyzeTextDensityAsync(image);

            await Task.WhenAll(qualityTask, textDensityTask).ConfigureAwait(false);

            var qualityMetrics = await qualityTask.ConfigureAwait(false);
            var textDensityMetrics = await textDensityTask.ConfigureAwait(false);

            // パラメータを最適化
            var parameters = await AdjustParametersAsync(qualityMetrics, textDensityMetrics).ConfigureAwait(false);

            // 最適化戦略の決定
            var strategy = DetermineOptimizationStrategy(qualityMetrics, textDensityMetrics);
            var reason = GenerateOptimizationReason(qualityMetrics, textDensityMetrics, parameters);
            var expectedImprovement = EstimateExpectedImprovement(qualityMetrics, textDensityMetrics, parameters);
            var confidence = CalculateParameterConfidence(qualityMetrics, textDensityMetrics);

            var result = new AdaptivePreprocessingResult
            {
                Parameters = parameters,
                QualityMetrics = qualityMetrics,
                TextDensityMetrics = textDensityMetrics,
                OptimizationReason = reason,
                OptimizationStrategy = strategy,
                OptimizationTimeMs = sw.ElapsedMilliseconds,
                ExpectedImprovement = expectedImprovement,
                ParameterConfidence = confidence
            };

            logger.LogInformation(
                "適応的前処理最適化完了: 戦略={Strategy}, 改善予想={Improvement:F2}, 信頼度={Confidence:F2} ({ElapsedMs}ms)",
                strategy, expectedImprovement, confidence, sw.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "適応的前処理最適化中にエラーが発生しました");
            return CreateFallbackResult(image, sw.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// 画像品質に基づくパラメータ調整
    /// </summary>
    private AdaptivePreprocessingParameters AdjustForImageQuality(
        AdaptivePreprocessingParameters baseParameters,
        ImageQualityMetrics qualityMetrics)
    {
        var brightness = qualityMetrics.Brightness;
        var contrast = qualityMetrics.Contrast;
        var noise = qualityMetrics.NoiseLevel;
        var sharpness = qualityMetrics.Sharpness;

        // 明度調整
        var brightnessAdjustment = CalculateBrightnessAdjustment(brightness);

        // コントラスト調整
        var contrastAdjustment = CalculateContrastAdjustment(contrast);

        // ガンマ補正
        var gammaAdjustment = CalculateGammaAdjustment(brightness, contrast);

        // ノイズ除去
        var noiseReductionLevel = CalculateNoiseReductionLevel(noise);

        // シャープニング
        var sharpeningLevel = CalculateSharpeningLevel(sharpness, noise);

        return baseParameters with
        {
            Brightness = brightnessAdjustment,
            Contrast = contrastAdjustment,
            Gamma = gammaAdjustment,
            NoiseReduction = noiseReductionLevel,
            Sharpening = sharpeningLevel
        };
    }

    /// <summary>
    /// テキスト特性に基づくパラメータ調整
    /// </summary>
    private AdaptivePreprocessingParameters AdjustForTextCharacteristics(
        AdaptivePreprocessingParameters baseParameters,
        TextDensityMetrics textMetrics)
    {
        var textSize = textMetrics.EstimatedTextSize;
        var edgeDensity = textMetrics.EdgeDensity;
        var textAreaRatio = textMetrics.TextAreaRatio;

        // 小さなテキスト対応
        var morphologyKernel = CalculateMorphologyKernel(textSize);
        var binarizationThreshold = CalculateBinarizationThreshold(edgeDensity, textAreaRatio);
        var detectionThreshold = CalculateDetectionThreshold(textSize, edgeDensity);
        var recognitionThreshold = CalculateRecognitionThreshold(textSize, textAreaRatio);
        var priority = DeterminePriority(textSize, edgeDensity);

        return baseParameters with
        {
            MorphologyKernelSize = morphologyKernel,
            BinarizationThreshold = binarizationThreshold,
            DetectionThreshold = detectionThreshold,
            RecognitionThreshold = recognitionThreshold,
            Priority = priority
        };
    }

    /// <summary>
    /// 最終的な総合最適化
    /// </summary>
    private AdaptivePreprocessingParameters ApplyFinalOptimization(
        AdaptivePreprocessingParameters parameters,
        ImageQualityMetrics qualityMetrics,
        TextDensityMetrics textMetrics)
    {
        // 低品質画像の特別処理
        if (qualityMetrics.OverallQuality < 0.3)
        {
            return parameters with
            {
                NoiseReduction = Math.Max(parameters.NoiseReduction, 0.4),
                EdgePreservingSmoothing = 0.3,
                Priority = PreprocessingPriority.LowQuality,
                OptimizationConfidence = Math.Max(0.7, parameters.OptimizationConfidence)
            };
        }

        // 小さなテキストの特別処理
        if (textMetrics.EstimatedTextSize < 12)
        {
            return parameters with
            {
                Sharpening = Math.Max(parameters.Sharpening, 0.3),
                DetectionThreshold = Math.Min(parameters.DetectionThreshold, 0.2),
                Priority = PreprocessingPriority.SmallText,
                OptimizationConfidence = Math.Max(0.8, parameters.OptimizationConfidence)
            };
        }

        // 高品質画像の軽量処理
        if (qualityMetrics.OverallQuality > 0.8)
        {
            return parameters with
            {
                NoiseReduction = Math.Min(parameters.NoiseReduction, 0.1),
                Priority = PreprocessingPriority.Speed,
                OptimizationConfidence = Math.Max(0.9, parameters.OptimizationConfidence)
            };
        }

        return parameters with
        {
            OptimizationConfidence = Math.Max(0.6, parameters.OptimizationConfidence)
        };
    }

    #region Parameter Calculation Methods

    private double CalculateBrightnessAdjustment(double brightness)
    {
        // 最適明度を0.5として調整
        if (brightness < 0.3) return 0.3 - brightness; // 暗すぎる場合は明るく
        if (brightness > 0.7) return 0.7 - brightness; // 明るすぎる場合は暗く
        return 0.0;
    }

    private double CalculateContrastAdjustment(double contrast)
    {
        // 低コントラストの場合は強化
        if (contrast < 0.3) return 1.0 + (0.3 - contrast) * 2.0;
        if (contrast > 0.8) return 1.0 - (contrast - 0.8) * 0.5;
        return 1.0;
    }

    private double CalculateGammaAdjustment(double brightness, double contrast)
    {
        if (brightness < 0.4 && contrast < 0.4) return 0.8; // 暗く低コントラスト
        if (brightness > 0.6 && contrast < 0.4) return 1.2; // 明るく低コントラスト
        return 1.0;
    }

    private double CalculateNoiseReductionLevel(double noise)
    {
        return Math.Min(noise * 1.5, 0.8); // ノイズレベルに比例、最大0.8
    }

    private double CalculateSharpeningLevel(double sharpness, double noise)
    {
        if (noise > 0.5) return 0.0; // ノイズが多い場合はシャープニングしない
        return Math.Max(0.0, 0.5 - sharpness); // シャープネスが低い場合に強化
    }

    private int CalculateMorphologyKernel(double textSize)
    {
        if (textSize < 10) return 1;
        if (textSize < 16) return 2;
        return 3;
    }

    private int CalculateBinarizationThreshold(double edgeDensity, double textAreaRatio)
    {
        if (edgeDensity > 0.1 && textAreaRatio > 0.3) return 140; // 高密度テキスト
        if (edgeDensity < 0.05) return 180; // 低密度テキスト
        return -1; // 自動閾値
    }

    private double CalculateDetectionThreshold(double textSize, double edgeDensity)
    {
        if (textSize < 12) return 0.2; // 小さなテキストは低閾値
        if (edgeDensity > 0.1) return 0.25; // 高密度は少し高め
        return 0.3;
    }

    private double CalculateRecognitionThreshold(double textSize, double textAreaRatio)
    {
        if (textSize < 12 && textAreaRatio < 0.2) return 0.2; // 小さく少ないテキスト
        if (textAreaRatio > 0.5) return 0.35; // 多くのテキスト
        return 0.3;
    }

    private PreprocessingPriority DeterminePriority(double textSize, double edgeDensity)
    {
        if (textSize < 12) return PreprocessingPriority.SmallText;
        if (edgeDensity < 0.03) return PreprocessingPriority.LowQuality;
        if (edgeDensity > 0.15) return PreprocessingPriority.Speed;
        return PreprocessingPriority.Balanced;
    }

    #endregion

    #region Result Generation Methods

    private string DetermineOptimizationStrategy(ImageQualityMetrics qualityMetrics, TextDensityMetrics textMetrics)
    {
        if (qualityMetrics.OverallQuality < 0.3)
            return "低品質画像対応戦略";
        if (textMetrics.EstimatedTextSize < 12)
            return "小文字テキスト最適化戦略";
        if (qualityMetrics.NoiseLevel > 0.5)
            return "ノイズ除去重視戦略";
        if (qualityMetrics.Contrast < 0.3)
            return "コントラスト強化戦略";
        return "バランス最適化戦略";
    }

    private string GenerateOptimizationReason(
        ImageQualityMetrics _,
        TextDensityMetrics _2,
        AdaptivePreprocessingParameters parameters)
    {
        var reasons = new List<string>();

        if (parameters.Brightness != 0.0)
            reasons.Add($"明度調整({parameters.Brightness:+0.0;-0.0})");
        if (parameters.Contrast != 1.0)
            reasons.Add($"コントラスト調整({parameters.Contrast:F1}x)");
        if (parameters.Gamma != 1.0)
            reasons.Add($"ガンマ補正({parameters.Gamma:F1})");
        if (parameters.NoiseReduction > 0.1)
            reasons.Add($"ノイズ除去({parameters.NoiseReduction:F1})");
        if (parameters.Sharpening > 0.0)
            reasons.Add($"シャープニング({parameters.Sharpening:F1})");

        return reasons.Count > 0 ? string.Join(", ", reasons) : "調整不要";
    }

    private double EstimateExpectedImprovement(
        ImageQualityMetrics qualityMetrics,
        TextDensityMetrics textMetrics,
        AdaptivePreprocessingParameters parameters)
    {
        var baseImprovement = 0.1; // 基本改善

        // 低品質画像での大きな改善期待
        if (qualityMetrics.OverallQuality < 0.4)
            baseImprovement += 0.3;

        // 小さなテキストでの改善期待
        if (textMetrics.EstimatedTextSize < 12)
            baseImprovement += 0.2;

        // パラメータ調整強度に応じた改善期待
        var adjustmentIntensity =
            Math.Abs(parameters.Brightness) +
            Math.Abs(parameters.Contrast - 1.0) +
            Math.Abs(parameters.Gamma - 1.0) +
            parameters.NoiseReduction +
            parameters.Sharpening;

        baseImprovement += adjustmentIntensity * 0.1;

        return Math.Min(baseImprovement, 0.8);
    }

    private double CalculateParameterConfidence(ImageQualityMetrics qualityMetrics, TextDensityMetrics textMetrics)
    {
        var confidence = 0.5; // 基本信頼度

        // 明確な品質問題がある場合は信頼度向上
        if (qualityMetrics.OverallQuality < 0.4) confidence += 0.3;
        if (qualityMetrics.Contrast < 0.3) confidence += 0.2;
        if (qualityMetrics.NoiseLevel > 0.5) confidence += 0.2;
        if (textMetrics.EstimatedTextSize < 12) confidence += 0.2;

        // 高品質画像では調整の必要性が低く、信頼度も控えめ
        if (qualityMetrics.OverallQuality > 0.8) confidence = Math.Min(confidence, 0.6);

        return Math.Min(confidence, 0.95);
    }

    private AdaptivePreprocessingResult CreateFallbackResult(IAdvancedImage image, long elapsedMs)
    {
        return new AdaptivePreprocessingResult
        {
            Parameters = new AdaptivePreprocessingParameters(),
            QualityMetrics = new ImageQualityMetrics
            {
                Width = image.Width,
                Height = image.Height,
                Contrast = 0.5,
                Brightness = 0.5,
                NoiseLevel = 0.1,
                Sharpness = 0.5,
                OverallQuality = 0.5
            },
            TextDensityMetrics = new TextDensityMetrics
            {
                EdgeDensity = 0.05,
                EstimatedTextSize = 16.0,
                TextAreaRatio = 0.3,
                EstimatedCharacterSpacing = 1.6,
                EstimatedLineSpacing = 19.2,
                TextOrientation = 0.0
            },
            OptimizationReason = "エラーによりデフォルトパラメータを使用",
            OptimizationStrategy = "フォールバック戦略",
            OptimizationTimeMs = elapsedMs,
            ExpectedImprovement = 0.0,
            ParameterConfidence = 0.1
        };
    }

    #endregion
}
