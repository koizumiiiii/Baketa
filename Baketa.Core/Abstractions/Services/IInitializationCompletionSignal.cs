namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// [Issue #198] アプリケーション初期化完了を通知するシグナル
/// コンポーネントダウンロード・解凍が完了するまで翻訳サーバー起動を遅延させる
///
/// 背景:
/// - ServerManagerHostedServiceはIHostedServiceとしてアプリ起動時に自動実行される
/// - コンポーネントダウンロード・解凍中はディスクI/Oが高負荷になる
/// - このタイミングで翻訳サーバーが起動し、ヘルスチェックが走ると誤判定が発生する
/// - 本インターフェースで初期化完了を通知し、翻訳サーバー起動を適切なタイミングに遅延させる
/// </summary>
public interface IInitializationCompletionSignal
{
    /// <summary>
    /// 初期化完了を待機するTask
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>初期化完了を待機するTask</returns>
    Task WaitForCompletionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 初期化完了を通知
    /// ApplicationInitializerがコンポーネントダウンロード・解凍完了後に呼び出す
    /// </summary>
    void SignalCompletion();

    /// <summary>
    /// 初期化が完了したかどうか
    /// </summary>
    bool IsCompleted { get; }
}
