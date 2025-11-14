namespace Baketa.Core.Abstractions.Imaging.Pipeline;

/// <summary>
/// ステップのエラーハンドリング戦略
/// </summary>
public enum StepErrorHandlingStrategy
{
    /// <summary>
    /// エラー発生時に実行を停止
    /// </summary>
    StopExecution,

    /// <summary>
    /// エラー発生時にステップをスキップ
    /// </summary>
    SkipStep,

    /// <summary>
    /// エラー発生時にフォールバック処理を使用
    /// </summary>
    UseFallback,

    /// <summary>
    /// エラーをログに記録して続行
    /// </summary>
    LogAndContinue,

    /// <summary>
    /// エラーにかかわらず処理を続行
    /// </summary>
    ContinueExecution
}
