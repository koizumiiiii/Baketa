using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;

namespace Baketa.Infrastructure.OCR.Ensemble;

/// <summary>
/// 複数OCRエンジンによるアンサンブル処理のインターフェース
/// </summary>
public interface IEnsembleOcrEngine : IOcrEngine
{
    /// <summary>
    /// アンサンブルに参加するOCRエンジンを追加
    /// </summary>
    void AddEngine(IOcrEngine engine, double weight = 1.0, EnsembleEngineRole role = EnsembleEngineRole.Primary);
    
    /// <summary>
    /// アンサンブルに参加するOCRエンジンを削除
    /// </summary>
    bool RemoveEngine(IOcrEngine engine);
    
    /// <summary>
    /// 現在のアンサンブル構成を取得
    /// </summary>
    IReadOnlyList<EnsembleEngineInfo> GetEnsembleConfiguration();
    
    /// <summary>
    /// 結果融合戦略を設定
    /// </summary>
    void SetFusionStrategy(IResultFusionStrategy strategy);
    
    /// <summary>
    /// アンサンブル処理の詳細結果を含む認識を実行
    /// </summary>
    Task<EnsembleOcrResults> RecognizeWithDetailsAsync(IImage image, IProgress<OcrProgress>? progress = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// アンサンブル統計情報を取得
    /// </summary>
    EnsemblePerformanceStats GetEnsembleStats();
}

/// <summary>
/// アンサンブルにおけるエンジンの役割
/// </summary>
public enum EnsembleEngineRole
{
    /// <summary>メインエンジン（高精度重視）</summary>
    Primary,
    
    /// <summary>サポートエンジン（速度重視）</summary>
    Secondary,
    
    /// <summary>特殊処理エンジン（特定ケース対応）</summary>
    Specialized,
    
    /// <summary>フォールバックエンジン（信頼性重視）</summary>
    Fallback
}

/// <summary>
/// アンサンブルエンジン情報
/// </summary>
public record EnsembleEngineInfo(
    IOcrEngine Engine,
    string EngineName,
    double Weight,
    EnsembleEngineRole Role,
    bool IsEnabled,
    EnsembleEngineStats Stats);

/// <summary>
/// アンサンブルエンジン統計
/// </summary>
public record EnsembleEngineStats(
    int TotalExecutions,
    double AverageProcessingTime,
    double AverageConfidence,
    double SuccessRate,
    DateTime LastExecution);

/// <summary>
/// アンサンブル処理結果
/// </summary>
public class EnsembleOcrResults(
    IReadOnlyList<OcrTextRegion> textRegions,
    IImage sourceImage,
    TimeSpan processingTime,
    string languageCode,
    Rectangle? regionOfInterest = null,
    string? mergedText = null) : OcrResults(textRegions, sourceImage, processingTime, languageCode, regionOfInterest, mergedText)
{
    /// <summary>各エンジンの個別結果</summary>
    public IReadOnlyList<IndividualEngineResult> IndividualResults { get; init; } = [];
    
    /// <summary>結果融合の詳細情報</summary>
    public ResultFusionDetails FusionDetails { get; init; } = new(0, 0, 0, 0, 0, []);
    
    /// <summary>使用されたアンサンブル戦略</summary>
    public string FusionStrategy { get; init; } = "";
    
    /// <summary>総合信頼度スコア</summary>
    public double EnsembleConfidence { get; init; }
    
    /// <summary>アンサンブル処理時間</summary>
    public TimeSpan EnsembleProcessingTime { get; init; }
}

/// <summary>
/// 個別エンジンの結果
/// </summary>
public record IndividualEngineResult(
    string EngineName,
    EnsembleEngineRole Role,
    OcrResults Results,
    TimeSpan ProcessingTime,
    double Weight,
    bool IsSuccessful,
    string? ErrorMessage = null);

/// <summary>
/// 結果融合詳細情報
/// </summary>
public record ResultFusionDetails(
    int TotalCandidateRegions,
    int FinalRegions,
    int AgreedRegions,
    int ConflictedRegions,
    double AgreementRate,
    IReadOnlyList<RegionFusionDetail> RegionDetails);

/// <summary>
/// 領域融合詳細
/// </summary>
public record RegionFusionDetail(
    int RegionIndex,
    IReadOnlyList<string> SourceEngines,
    double FinalConfidence,
    string FinalText,
    FusionDecisionType DecisionType,
    string DecisionReason);

/// <summary>
/// 融合決定タイプ
/// </summary>
public enum FusionDecisionType
{
    /// <summary>全エンジンで一致</summary>
    Unanimous,
    
    /// <summary>多数決</summary>
    Majority,
    
    /// <summary>重み付き選択</summary>
    WeightedSelection,
    
    /// <summary>信頼度ベース選択</summary>
    ConfidenceBased,
    
    /// <summary>単一エンジン（他エンジンは失敗）</summary>
    SingleEngine,
    
    /// <summary>競合解決</summary>
    ConflictResolution
}

/// <summary>
/// アンサンブル パフォーマンス統計
/// </summary>
public record EnsemblePerformanceStats(
    int TotalEnsembleExecutions,
    double AverageEnsembleTime,
    double AverageImprovementRate,
    double BestEngineAgreementRate,
    IReadOnlyDictionary<string, EnsembleEngineStats> EngineStats,
    IReadOnlyDictionary<string, int> FusionStrategyUsage);
