using System;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.ResourceManagement;

/// <summary>
/// A/Bテスト統計的有意性検定システム
/// Chi-square検定・t-test・効果量計算を提供
/// </summary>
internal sealed class StatisticalAnalyzer
{
    private readonly ILogger<StatisticalAnalyzer> _logger;

    public StatisticalAnalyzer(ILogger<StatisticalAnalyzer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 2つのバリアント間の統計的有意性検定
    /// </summary>
    public StatisticalTestResult CompareVariants(VariantResult variant1, VariantResult variant2)
    {
        try
        {
            // サンプルサイズ検証
            if (variant1.TotalMeasurements < ResourceManagementConstants.Statistics.MinimumSampleSize || 
                variant2.TotalMeasurements < ResourceManagementConstants.Statistics.MinimumSampleSize)
            {
                return new StatisticalTestResult(
                    TestType: "Insufficient Sample Size",
                    PValue: ResourceManagementConstants.Fallback.DefaultPValue,
                    IsSignificant: false,
                    EffectSize: ResourceManagementConstants.Fallback.DefaultEffectSize,
                    EffectSizeCategory: EffectSizeCategory.None,
                    Recommendation: $"最小サンプル数（{ResourceManagementConstants.Statistics.MinimumSampleSize}）に到達していません",
                    Confidence: ResourceManagementConstants.Fallback.DefaultConfidence
                );
            }

            // 成功率のChi-square検定
            var chisquareResult = PerformChiSquareTest(variant1, variant2);
            
            // パフォーマンス指標のt-test（成功率が有意差ありの場合）
            TestResult? performanceResult = null;
            if (chisquareResult.IsSignificant)
            {
                performanceResult = PerformWelchTTest(variant1, variant2);
            }

            // 総合的な効果量計算
            var overallEffectSize = CalculateOverallEffectSize(variant1, variant2);
            var effectCategory = ClassifyEffectSize(overallEffectSize);

            // 推奨事項生成
            var recommendation = GenerateRecommendation(chisquareResult, performanceResult, effectCategory);
            var confidence = CalculateConfidence(chisquareResult, performanceResult, 
                variant1.TotalMeasurements, variant2.TotalMeasurements);

            return new StatisticalTestResult(
                TestType: "Chi-square + Welch's t-test",
                PValue: chisquareResult.PValue,
                IsSignificant: chisquareResult.IsSignificant || (performanceResult?.IsSignificant ?? false),
                EffectSize: overallEffectSize,
                EffectSizeCategory: effectCategory,
                Recommendation: recommendation,
                Confidence: confidence
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STATS] 統計検定エラー: {V1} vs {V2}", 
                variant1.VariantName, variant2.VariantName);
            
            return new StatisticalTestResult(
                TestType: "Error",
                PValue: ResourceManagementConstants.Fallback.DefaultPValue,
                IsSignificant: false,
                EffectSize: ResourceManagementConstants.Fallback.DefaultEffectSize,
                EffectSizeCategory: EffectSizeCategory.None,
                Recommendation: "統計検定でエラーが発生しました",
                Confidence: ResourceManagementConstants.Fallback.DefaultConfidence
            );
        }
    }

    /// <summary>
    /// Chi-square検定による成功率比較
    /// </summary>
    private TestResult PerformChiSquareTest(VariantResult variant1, VariantResult variant2)
    {
        var n1 = variant1.TotalMeasurements;
        var n2 = variant2.TotalMeasurements;
        var success1 = (int)(variant1.SuccessRate * n1);
        var success2 = (int)(variant2.SuccessRate * n2);
        var failure1 = n1 - success1;
        var failure2 = n2 - success2;

        // 期待値計算
        var total = n1 + n2;
        var totalSuccess = success1 + success2;
        var totalFailure = failure1 + failure2;
        
        var expected11 = (double)(n1 * totalSuccess) / total;  // variant1 success
        var expected12 = (double)(n1 * totalFailure) / total;  // variant1 failure
        var expected21 = (double)(n2 * totalSuccess) / total;  // variant2 success
        var expected22 = (double)(n2 * totalFailure) / total;  // variant2 failure

        // Yatesの連続性補正を適用したChi-square統計量計算
        var yatesCorrection = ResourceManagementConstants.Statistics.YatesCorrectionValue;
        var chiSquare = 
            Math.Pow(Math.Abs(success1 - expected11) - yatesCorrection, 2) / expected11 +
            Math.Pow(Math.Abs(failure1 - expected12) - yatesCorrection, 2) / expected12 +
            Math.Pow(Math.Abs(success2 - expected21) - yatesCorrection, 2) / expected21 +
            Math.Pow(Math.Abs(failure2 - expected22) - yatesCorrection, 2) / expected22;

        // 自由度1のChi-square分布からp値を近似計算
        var pValue = CalculateChiSquarePValue(chiSquare, 1);
        var isSignificant = pValue < ResourceManagementConstants.Statistics.SignificanceThreshold;

        _logger.LogDebug("📊 [STATS] Chi-square: χ²={ChiSquare:F4}, p={PValue:F6}, significant={IsSignificant}",
            chiSquare, pValue, isSignificant);

        return new TestResult(chiSquare, pValue, isSignificant);
    }

    /// <summary>
    /// Welch's t-test（不等分散t検定）によるパフォーマンス指標比較
    /// </summary>
    private TestResult PerformWelchTTest(VariantResult variant1, VariantResult variant2)
    {
        // 冷却時間を秒単位で比較（主要パフォーマンス指標）
        var mean1 = variant1.AverageCooldownTime.TotalSeconds;
        var mean2 = variant2.AverageCooldownTime.TotalSeconds;
        var n1 = variant1.TotalMeasurements;
        var n2 = variant2.TotalMeasurements;

        // 分散推定（簡易的に標準偏差を平均の30%と仮定）
        var std1 = mean1 * ResourceManagementConstants.Statistics.StandardDeviationCoefficient;
        var std2 = mean2 * ResourceManagementConstants.Statistics.StandardDeviationCoefficient;
        var var1 = std1 * std1;
        var var2 = std2 * std2;

        // Welch's t統計量計算
        var tStatistic = (mean1 - mean2) / Math.Sqrt((var1 / n1) + (var2 / n2));
        
        // Welch-Satterthwaiteの自由度近似
        var numerator = Math.Pow((var1 / n1) + (var2 / n2), 2);
        var denominator = (Math.Pow(var1 / n1, 2) / (n1 - 1)) + (Math.Pow(var2 / n2, 2) / (n2 - 1));
        var degreesOfFreedom = numerator / denominator;

        // t分布からp値を近似計算（両側検定）
        var pValue = CalculateTTestPValue(Math.Abs(tStatistic), degreesOfFreedom) * 2;
        var isSignificant = pValue < ResourceManagementConstants.Statistics.SignificanceThreshold;

        _logger.LogDebug("📊 [STATS] Welch's t-test: t={TStatistic:F4}, df={DF:F1}, p={PValue:F6}, significant={IsSignificant}",
            tStatistic, degreesOfFreedom, pValue, isSignificant);

        return new TestResult(tStatistic, pValue, isSignificant);
    }

    /// <summary>
    /// 総合的効果量計算（Cohen's dの多次元拡張）
    /// </summary>
    private double CalculateOverallEffectSize(VariantResult variant1, VariantResult variant2)
    {
        // 成功率の効果量
        var successRateDiff = Math.Abs(variant1.SuccessRate - variant2.SuccessRate);
        var successRateEffectSize = successRateDiff / Math.Sqrt(0.25); // 二項分布の最大分散は0.25

        // パフォーマンス効果量（冷却時間）
        var cooldownDiff = Math.Abs(variant1.AverageCooldownTime.TotalSeconds - variant2.AverageCooldownTime.TotalSeconds);
        var avgCooldown = (variant1.AverageCooldownTime.TotalSeconds + variant2.AverageCooldownTime.TotalSeconds) / 2;
        var cooldownEffectSize = avgCooldown > 0 ? cooldownDiff / (avgCooldown * 0.3) : 0; // 標準偏差を30%と仮定

        // VRAM使用率の効果量
        var vramDiff = Math.Abs(variant1.AverageVramUsage - variant2.AverageVramUsage);
        var vramEffectSize = vramDiff / 20.0; // VRAM使用率の典型的な標準偏差を20%と仮定

        // 重み付き平均による総合効果量
        var totalEffectSize = (successRateEffectSize * 0.4) + (cooldownEffectSize * 0.4) + (vramEffectSize * 0.2);
        
        return Math.Min(totalEffectSize, 2.0); // 効果量を2.0でキャップ
    }

    /// <summary>
    /// Chi-square分布のp値近似計算
    /// </summary>
    private static double CalculateChiSquarePValue(double chiSquare, int degreesOfFreedom)
    {
        // 自由度1の場合の近似式（Wilson-Hilferty変換）
        if (degreesOfFreedom == 1)
        {
            if (chiSquare > 10.83) return 0.001; // p < 0.001
            if (chiSquare > 6.63) return 0.01;   // p < 0.01
            if (chiSquare > 3.84) return 0.05;   // p < 0.05
            if (chiSquare > 2.71) return 0.10;   // p < 0.10
            return 0.5; // p ≥ 0.10
        }
        
        return 0.5; // フォールバック
    }

    /// <summary>
    /// t分布のp値近似計算
    /// </summary>
    private static double CalculateTTestPValue(double tValue, double degreesOfFreedom)
    {
        // 大サンプル近似（t分布→標準正規分布）
        if (degreesOfFreedom > 30)
        {
            // 標準正規分布による近似
            if (tValue > 2.58) return 0.005; // p < 0.01
            if (tValue > 1.96) return 0.025; // p < 0.05  
            if (tValue > 1.645) return 0.05; // p < 0.10
            return 0.5;
        }
        
        // 小サンプルの場合（簡易的な臨界値テーブル）
        if (tValue > 2.75) return 0.01;
        if (tValue > 2.06) return 0.05;
        if (tValue > 1.65) return 0.10;
        return 0.5;
    }

    /// <summary>
    /// 効果量分類
    /// </summary>
    private static EffectSizeCategory ClassifyEffectSize(double effectSize)
    {
        var absEffectSize = Math.Abs(effectSize);
        return absEffectSize switch
        {
            < ResourceManagementConstants.Statistics.SmallEffectSize => EffectSizeCategory.None,
            < ResourceManagementConstants.Statistics.MediumEffectSize => EffectSizeCategory.Small,
            < ResourceManagementConstants.Statistics.LargeEffectSize => EffectSizeCategory.Medium,
            _ => EffectSizeCategory.Large
        };
    }

    /// <summary>
    /// 推奨事項生成
    /// </summary>
    private static string GenerateRecommendation(TestResult chisquareResult, TestResult? performanceResult, EffectSizeCategory effectCategory)
    {
        if (chisquareResult.IsSignificant && effectCategory >= EffectSizeCategory.Medium)
        {
            return performanceResult?.IsSignificant == true 
                ? "統計的有意差あり：より良いバリアントに切り替え推奨" 
                : "成功率に有意差あり：パフォーマンス監視継続推奨";
        }

        if (effectCategory >= EffectSizeCategory.Small)
        {
            return "効果量は検出されるがサンプル数不足：継続測定推奨";
        }

        return "有意差検出されず：現在設定維持推奨";
    }

    /// <summary>
    /// 信頼度計算
    /// </summary>
    private static double CalculateConfidence(TestResult chisquareResult, TestResult? performanceResult, int n1, int n2)
    {
        var baseConfidence = Math.Max(0, 1 - chisquareResult.PValue);
        
        // サンプルサイズ補正
        var sampleSizeBonus = Math.Min(0.2, (n1 + n2 - 60) / 500.0);
        
        // パフォーマンステスト一致度ボーナス
        var consistencyBonus = performanceResult?.IsSignificant == true ? 0.1 : 0.0;
        
        return Math.Min(0.99, baseConfidence + sampleSizeBonus + consistencyBonus);
    }

    /// <summary>
    /// 内部テスト結果
    /// </summary>
    private sealed record TestResult(double Statistic, double PValue, bool IsSignificant);
}

/// <summary>
/// 統計検定結果
/// </summary>
public sealed record StatisticalTestResult(
    string TestType,
    double PValue,
    bool IsSignificant,
    double EffectSize,
    EffectSizeCategory EffectSizeCategory,
    string Recommendation,
    double Confidence
);

/// <summary>
/// 効果量カテゴリ
/// </summary>
public enum EffectSizeCategory
{
    None,    // < 0.2
    Small,   // 0.2-0.5
    Medium,  // 0.5-0.8
    Large    // >= 0.8
}