using Baketa.Core.Models.Processing;

namespace Baketa.Core.Abstractions.Processing;

/// <summary>
/// 段階的処理パイプラインサービス
/// OCR→翻訳の効率的な段階的フィルタリングを提供
/// </summary>
public interface ISmartProcessingPipelineService
{
    /// <summary>
    /// 段階的処理パイプラインを実行
    /// </summary>
    /// <param name="input">処理入力データ</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>処理結果</returns>
    Task<ProcessingPipelineResult> ExecuteAsync(ProcessingPipelineInput input, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 特定段階のみを実行
    /// </summary>
    /// <param name="stage">実行対象段階</param>
    /// <param name="context">処理コンテキスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>段階別処理結果</returns>
    Task<ProcessingStageResult> ExecuteStageAsync(ProcessingStageType stage, ProcessingContext context, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 入力データに基づく推奨実行段階を取得
    /// </summary>
    /// <param name="input">処理入力データ</param>
    /// <returns>推奨実行段階リスト</returns>
    IReadOnlyList<ProcessingStageType> GetExecutableStageSuggestion(ProcessingPipelineInput input);
}

/// <summary>
/// 段階的処理の各ステージ実行戦略
/// Strategy Patternによる段階別処理の抽象化
/// </summary>
public interface IProcessingStageStrategy
{
    /// <summary>
    /// 段階種別
    /// </summary>
    ProcessingStageType StageType { get; }
    
    /// <summary>
    /// 段階処理を実行
    /// </summary>
    /// <param name="context">処理コンテキスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>段階別処理結果</returns>
    Task<ProcessingStageResult> ExecuteAsync(ProcessingContext context, CancellationToken cancellationToken);
    
    /// <summary>
    /// 段階実行の必要性を判定
    /// </summary>
    /// <param name="context">処理コンテキスト</param>
    /// <returns>実行すべきかどうか</returns>
    bool ShouldExecute(ProcessingContext context);
    
    /// <summary>
    /// 推定処理時間
    /// </summary>
    TimeSpan EstimatedProcessingTime { get; }
}