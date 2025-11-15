using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;

namespace Baketa.Infrastructure.OCR.Ensemble;

/// <summary>
/// アンサンブルエンジンの重みとバランスを動的に最適化するインターフェース
/// </summary>
public interface IEnsembleEngineBalancer
{
    /// <summary>
    /// 画像特性に基づいてエンジン重みを最適化
    /// </summary>
    Task<EngineWeightOptimizationResult> OptimizeEngineWeightsAsync(
        IAdvancedImage image,
        IReadOnlyList<EnsembleEngineInfo> engines,
        BalancingParameters parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 履歴データに基づいてエンジン重みを学習・更新
    /// </summary>
    Task<EngineWeightLearningResult> LearnFromHistoryAsync(
        IReadOnlyList<EnsembleExecutionHistory> executionHistory,
        LearningParameters parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 特定のシナリオに最適化されたエンジン構成を提案
    /// </summary>
    Task<EngineConfigurationRecommendation> RecommendConfigurationAsync(
        ImageCharacteristics imageCharacteristics,
        PerformanceRequirements performanceReqs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// リアルタイムでエンジンパフォーマンスを監視し調整を提案
    /// </summary>
    Task<PerformanceAdjustmentSuggestion> MonitorAndSuggestAdjustmentsAsync(
        IReadOnlyList<IndividualEngineResult> recentResults,
        MonitoringParameters parameters,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// バランシングパラメータ
/// </summary>
public record BalancingParameters(
    double AccuracyWeight = 0.4,
    double SpeedWeight = 0.3,
    double ReliabilityWeight = 0.2,
    double ResourceUsageWeight = 0.1,
    bool AdaptToImageQuality = true,
    bool ConsiderEngineSpecialization = true,
    double MinimumEngineWeight = 0.1,
    double MaximumEngineWeight = 2.0,
    bool EnableDynamicWeighting = true);

/// <summary>
/// 学習パラメータ
/// </summary>
public record LearningParameters(
    int MinimumHistorySize = 50,
    double LearningRate = 0.01,
    double DecayFactor = 0.95,
    bool UseReinforcementLearning = true,
    bool AdaptToImageTypes = true,
    TimeSpan HistoryTimeWindow = default,
    double SuccessRewardMultiplier = 1.2,
    double FailurePenaltyMultiplier = 0.8);

/// <summary>
/// 画像特性
/// </summary>
public record ImageCharacteristics(
    ImageQualityLevel QualityLevel,
    TextDensityLevel TextDensity,
    ImageComplexityLevel Complexity,
    ImageType Type,
    double EstimatedTextSize,
    double ContrastRatio,
    double NoiseLevel,
    bool HasSmallText,
    bool HasComplexLayout);

/// <summary>
/// パフォーマンス要件
/// </summary>
public record PerformanceRequirements(
    double MaxAcceptableProcessingTime,
    double MinimumRequiredAccuracy,
    double PreferredSpeedAccuracyBalance,
    bool PrioritizeSpeed,
    bool PrioritizeAccuracy,
    ResourceConstraints ResourceLimits);

/// <summary>
/// リソース制約
/// </summary>
public record ResourceConstraints(
    double MaxCpuUsage = 0.8,
    long MaxMemoryUsage = 2L * 1024 * 1024 * 1024, // 2GB
    int MaxConcurrentEngines = 4,
    bool AllowGpuUsage = true);

/// <summary>
/// 監視パラメータ
/// </summary>
public record MonitoringParameters(
    TimeSpan MonitoringWindow = default,
    double PerformanceDegradationThreshold = 0.15,
    double AccuracyVariationThreshold = 0.1,
    int MinimumSamplesForAdjustment = 10,
    bool EnableAutoAdjustment = false,
    double AdjustmentSensitivity = 0.5);

/// <summary>
/// エンジン重み最適化結果
/// </summary>
public record EngineWeightOptimizationResult(
    Dictionary<string, double> OptimizedWeights,
    double ExpectedAccuracyImprovement,
    double ExpectedSpeedImprovement,
    OptimizationReason PrimaryReason,
    IReadOnlyList<string> OptimizationDetails,
    double ConfidenceScore,
    TimeSpan OptimizationTime);

/// <summary>
/// エンジン重み学習結果
/// </summary>
public record EngineWeightLearningResult(
    Dictionary<string, double> LearnedWeights,
    double LearningProgress,
    int ProcessedSamples,
    double ModelConfidence,
    IReadOnlyDictionary<string, double> EnginePerformanceScores,
    LearningInsights Insights);

/// <summary>
/// エンジン構成推奨
/// </summary>
public record EngineConfigurationRecommendation(
    IReadOnlyList<RecommendedEngineConfig> RecommendedEngines,
    string RecommendationReason,
    double ExpectedPerformance,
    PerformanceTradeoffs Tradeoffs,
    IReadOnlyList<string> AlternativeConfigurations);

/// <summary>
/// 推奨エンジン構成
/// </summary>
public record RecommendedEngineConfig(
    string EngineName,
    double RecommendedWeight,
    EnsembleEngineRole RecommendedRole,
    bool IsEnabled,
    string Justification);

/// <summary>
/// パフォーマンス調整提案
/// </summary>
public record PerformanceAdjustmentSuggestion(
    AdjustmentType AdjustmentType,
    Dictionary<string, double> SuggestedWeightChanges,
    string Reason,
    double ExpectedImprovement,
    AdjustmentUrgency Urgency,
    IReadOnlyList<string> ActionItems);

/// <summary>
/// 学習洞察
/// </summary>
public record LearningInsights(
    IReadOnlyDictionary<string, double> EngineStrengths,
    IReadOnlyDictionary<string, double> EngineWeaknesses,
    IReadOnlyList<string> OptimalScenarios,
    IReadOnlyList<string> ProblematicScenarios,
    double OverallLearningQuality);

/// <summary>
/// パフォーマンストレードオフ
/// </summary>
public record PerformanceTradeoffs(
    double AccuracyVsSpeedRatio,
    double ResourceUsageImplication,
    double ReliabilityImplication,
    IReadOnlyList<string> TradeoffDetails);

/// <summary>
/// アンサンブル実行履歴
/// </summary>
public record EnsembleExecutionHistory(
    DateTime ExecutionTime,
    ImageCharacteristics ImageCharacteristics,
    Dictionary<string, double> UsedWeights,
    EnsembleOcrResults Results,
    double ActualAccuracy,
    TimeSpan ActualProcessingTime,
    bool WasSuccessful,
    string? ErrorDetails = null);

/// <summary>
/// 最適化理由
/// </summary>
public enum OptimizationReason
{
    ImageQualityAdaptation,
    SpeedOptimization,
    AccuracyOptimization,
    ResourceConstraints,
    HistoricalPerformance,
    EngineSpecialization,
    UserPreferences
}

/// <summary>
/// 調整タイプ
/// </summary>
public enum AdjustmentType
{
    WeightAdjustment,
    EngineToggle,
    StrategyChange,
    ParameterTuning,
    ConfigurationOverhaul
}

/// <summary>
/// 調整緊急度
/// </summary>
public enum AdjustmentUrgency
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// 画像品質レベル
/// </summary>
public enum ImageQualityLevel
{
    VeryLow,
    Low,
    Medium,
    High,
    VeryHigh
}

/// <summary>
/// テキスト密度レベル
/// </summary>
public enum TextDensityLevel
{
    Sparse,
    Low,
    Medium,
    High,
    Dense
}

/// <summary>
/// 画像複雑度レベル
/// </summary>
public enum ImageComplexityLevel
{
    Simple,
    Moderate,
    Complex,
    VeryComplex
}

/// <summary>
/// 画像タイプ
/// </summary>
public enum ImageType
{
    Document,
    Screenshot,
    Photo,
    Game,
    Technical,
    Other
}
