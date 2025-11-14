using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Abstractions;

/// <summary>
/// 翻訳トランザクションマネージャーのインターフェース
/// </summary>
public interface ITranslationTransactionManager
{
    /// <summary>
    /// 新しいトランザクションを開始します
    /// </summary>
    /// <param name="transactionName">トランザクション名</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>トランザクションID</returns>
    Task<Guid> BeginTransactionAsync(
        string transactionName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// トランザクションにリクエストを追加します
    /// </summary>
    /// <param name="transactionId">トランザクションID</param>
    /// <param name="request">翻訳リクエスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>成功した場合はtrue</returns>
    Task<bool> AddRequestToTransactionAsync(
        Guid transactionId,
        TranslationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// トランザクションにレスポンスを保存します
    /// </summary>
    /// <param name="transactionId">トランザクションID</param>
    /// <param name="response">翻訳レスポンス</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>成功した場合はtrue</returns>
    Task<bool> SaveResponseToTransactionAsync(
        Guid transactionId,
        TranslationResponse response,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// トランザクションをコミットします
    /// </summary>
    /// <param name="transactionId">トランザクションID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>トランザクション結果</returns>
    Task<TransactionResult?> CommitTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// トランザクションをロールバックします
    /// </summary>
    /// <param name="transactionId">トランザクションID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>成功した場合はtrue</returns>
    Task<bool> RollbackTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// トランザクション結果を表すクラス
/// </summary>
public class TransactionResult
{
    /// <summary>
    /// トランザクションID
    /// </summary>
    public Guid TransactionId { get; set; }

    /// <summary>
    /// トランザクション名
    /// </summary>
    public string? TransactionName { get; set; }

    /// <summary>
    /// 成功したかどうか
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 処理されたリクエスト数
    /// </summary>
    public int ProcessedRequestCount { get; set; }

    /// <summary>
    /// 成功したレスポンス数
    /// </summary>
    public int SuccessResponseCount { get; set; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 開始タイムスタンプ
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 完了タイムスタンプ
    /// </summary>
    public DateTime CompletionTime { get; set; }

    /// <summary>
    /// 処理時間（ミリ秒）
    /// </summary>
    public long ProcessingTimeMs { get; set; }
}
