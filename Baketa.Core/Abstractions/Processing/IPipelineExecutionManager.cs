namespace Baketa.Core.Abstractions.Processing;

/// <summary>
/// パイプライン実行の排他制御を管理するインターフェース
/// Strategy A: 並行パイプライン実行を防ぎ、SafeImage競合を根絶
/// </summary>
public interface IPipelineExecutionManager
{
    /// <summary>
    /// パイプライン処理を排他的に実行
    /// 並行実行を防ぎ、SafeImage早期破棄競合を完全に回避
    /// </summary>
    /// <typeparam name="T">戻り値の型</typeparam>
    /// <param name="pipelineFunc">実行するパイプライン処理（CancellationTokenを受け取る）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>パイプライン処理の結果</returns>
    Task<T> ExecuteExclusivelyAsync<T>(Func<CancellationToken, Task<T>> pipelineFunc, CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在パイプラインが実行中かどうかを判定
    /// </summary>
    bool IsExecuting { get; }
}
