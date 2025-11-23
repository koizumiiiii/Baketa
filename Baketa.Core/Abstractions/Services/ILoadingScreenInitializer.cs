namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// ローディング画面初期化処理のインターフェース
/// 起動時のローディング画面で実行される初期化ステップを管理します
/// </summary>
public interface ILoadingScreenInitializer
{
    /// <summary>
    /// 初期化進捗イベント
    /// 各ステップの開始・完了時に発火されます
    /// </summary>
    event EventHandler<LoadingProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// ローディング画面用初期化を非同期で実行します
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <exception cref="InvalidOperationException">初期化に失敗した場合</exception>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// ローディング画面進捗情報を保持するイベント引数
/// </summary>
public class LoadingProgressEventArgs : EventArgs
{
    /// <summary>
    /// ステップID（例: "resolve_dependencies", "load_ocr"）
    /// </summary>
    public required string StepId { get; init; }

    /// <summary>
    /// ステップのメッセージ（例: "依存関係を解決しています..."）
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// ステップが完了したかどうか
    /// </summary>
    public bool IsCompleted { get; init; }

    /// <summary>
    /// 進捗率（0-100）
    /// </summary>
    public int Progress { get; init; }
}
