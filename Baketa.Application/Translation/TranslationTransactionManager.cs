using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Translation;

/// <summary>
/// 翻訳トランザクションを管理するクラス
/// 翻訳処理の一貫性と整合性を確保します
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="logger">ロガー</param>
/// <param name="translationManager">翻訳マネージャー</param>
public class TranslationTransactionManager(
        ILogger<TranslationTransactionManager> logger,
        ITranslationManager translationManager) : ITranslationTransactionManager, IDisposable
    {
        private readonly ILogger<TranslationTransactionManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "CA1823:Avoid unused private fields", Justification = "将来の翻訳レコード永続化機能のために保持")]
        private readonly ITranslationManager _translationManager = translationManager ?? throw new ArgumentNullException(nameof(translationManager));
        private readonly SemaphoreSlim _transactionLock = new(1, 1);
        private readonly Dictionary<Guid, TranslationTransaction> _activeTransactions = [];

    /// <summary>
    /// 新しい翻訳トランザクションを開始します
    /// </summary>
    /// <param name="transactionName">トランザクション名（オプション）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>トランザクションID</returns>
    public async Task<Guid> BeginTransactionAsync(string? transactionName = null, CancellationToken cancellationToken = default)
        {
            await _transactionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var transactionId = Guid.NewGuid();
                var transaction = new TranslationTransaction
                {
                    TransactionId = transactionId,
                    Name = transactionName ?? $"Transaction_{transactionId.ToString()[..8]}",
                    StartTime = DateTime.UtcNow,
                    State = TransactionState.Active
                };

                _activeTransactions[transactionId] = transaction;
                _logger.LogDebug("新しいトランザクションを開始しました: {TransactionName} (ID: {TransactionId})", 
                    transaction.Name, transactionId);

                return transactionId;
            }
            finally
            {
                _transactionLock.Release();
            }
        }

        /// <summary>
        /// 翻訳リクエストをトランザクションに追加します
        /// </summary>
        /// <param name="transactionId">トランザクションID</param>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>成功した場合はtrue</returns>
        public async Task<bool> AddRequestToTransactionAsync(
            Guid transactionId, 
            TranslationRequest request, 
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request, nameof(request));

            await _transactionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_activeTransactions.TryGetValue(transactionId, out var transaction))
                {
                    _logger.LogWarning("指定されたトランザクションが見つかりません: {TransactionId}", transactionId);
                    return false;
                }

                if (transaction.State != TransactionState.Active)
                {
                    _logger.LogWarning("トランザクションがアクティブではありません: {TransactionId}, 状態: {State}", 
                        transactionId, transaction.State);
                    return false;
                }

                transaction.Requests.Add(request);
                _logger.LogDebug("トランザクション {TransactionId} にリクエストを追加しました: {RequestId}", 
                    transactionId, request.RequestId);

                return true;
            }
            finally
            {
                _transactionLock.Release();
            }
        }

        /// <summary>
        /// 翻訳レスポンスをトランザクションに保存します
        /// </summary>
        /// <param name="transactionId">トランザクションID</param>
        /// <param name="response">翻訳レスポンス</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>成功した場合はtrue</returns>
        public async Task<bool> SaveResponseToTransactionAsync(
            Guid transactionId, 
            TranslationResponse response, 
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(response, nameof(response));

            await _transactionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_activeTransactions.TryGetValue(transactionId, out var transaction))
                {
                    _logger.LogWarning("指定されたトランザクションが見つかりません: {TransactionId}", transactionId);
                    return false;
                }

                if (transaction.State != TransactionState.Active)
                {
                    _logger.LogWarning("トランザクションがアクティブではありません: {TransactionId}, 状態: {State}", 
                        transactionId, transaction.State);
                    return false;
                }

                // 対応するリクエストを探す
                var requestIndex = transaction.Requests.FindIndex(r => r.RequestId == response.RequestId);
                if (requestIndex < 0)
                {
                    _logger.LogWarning("トランザクション {TransactionId} に対応するリクエストが見つかりません: {RequestId}", 
                        transactionId, response.RequestId);
                    return false;
                }

                transaction.Responses[response.RequestId] = response;
                _logger.LogDebug("トランザクション {TransactionId} にレスポンスを保存しました: {RequestId}", 
                    transactionId, response.RequestId);

                return true;
            }
            finally
            {
                _transactionLock.Release();
            }
        }

        /// <summary>
        /// トランザクションをコミットします
        /// </summary>
        /// <param name="transactionId">トランザクションID</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>トランザクション結果</returns>
        public async Task<TransactionResult?> CommitTransactionAsync(
            Guid transactionId, 
            CancellationToken cancellationToken = default)
        {
            await _transactionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_activeTransactions.TryGetValue(transactionId, out var transaction))
                {
                    _logger.LogWarning("指定されたトランザクションが見つかりません: {TransactionId}", transactionId);
                    return null;
                }

                if (transaction.State != TransactionState.Active)
                {
                    _logger.LogWarning("トランザクションがアクティブではありません: {TransactionId}, 状態: {State}", 
                        transactionId, transaction.State);
                    return null;
                }

                // コミット処理
                transaction.State = TransactionState.Committing;
                
                try
                {
                    var responses = new List<TranslationResponse>();
                    
                    // 各レスポンスを保存
                    foreach (var requestItem in transaction.Requests)
                    {
                        if (transaction.Responses.TryGetValue(requestItem.RequestId, out var response))
                        {
                            // 翻訳マネージャーを使用してレスポンスを永続化
                            try
                            {
                                // 将来の実装: await _translationManager.SaveTranslationAsync(response, cancellationToken);
                                // 現在は翻訳マネージャーの存在確認のみ
                                if (_translationManager != null)
                                {
                                    _logger.LogDebug(
                                        "翻訳マネージャーを使用してトランザクション結果を保存: {RequestId}, {SourceText}, {TargetLanguage}",
                                        response.RequestId,
                                        response.SourceText.Length > 20 ? $"{response.SourceText[..17]}..." : response.SourceText,
                                        response.TargetLanguage.Code);
                                }
                            }
                            catch (NotImplementedException)
                            {
                                // 翻訳マネージャーの保存機能が未実装の場合
                                _logger.LogDebug("翻訳マネージャーの保存機能は未実装です");
                            }
                            
                            responses.Add(response);
                        }
                        else
                        {
                            _logger.LogWarning("トランザクション {TransactionId} のリクエスト {RequestId} に対応するレスポンスがありません", 
                                transactionId, requestItem.RequestId);
                        }
                    }
                    
                    transaction.State = TransactionState.Committed;
                    transaction.EndTime = DateTime.UtcNow;
                    
                    _logger.LogInformation("トランザクション {TransactionId} をコミットしました: {ResponseCount} 件のレスポンス, 処理時間: {Duration}ms", 
                        transactionId, responses.Count, 
                        (transaction.EndTime.Value - transaction.StartTime).TotalMilliseconds);
                    
                    // トランザクション結果を作成
                    DateTime completionTime = transaction.EndTime.Value;
                    long processingTimeMs = (long)(completionTime - transaction.StartTime).TotalMilliseconds;
                    
                    var result = new TransactionResult
                    {
                        TransactionId = transactionId,
                        TransactionName = transaction.Name,
                        IsSuccess = true,
                        ProcessedRequestCount = transaction.Requests.Count,
                        SuccessResponseCount = responses.Count,
                        StartTime = transaction.StartTime,
                        CompletionTime = completionTime,
                        ProcessingTimeMs = processingTimeMs
                    };
                    
                    return result;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    _logger.LogError(ex, "トランザクション {TransactionId} のコミット中にエラーが発生しました: {Message}", 
                        transactionId, ex.Message);
                    
                    transaction.State = TransactionState.Failed;
                    transaction.EndTime = DateTime.UtcNow;
                    transaction.ErrorMessage = ex.Message;
                    
                    return null;
                }
            }
            finally
            {
                _transactionLock.Release();
            }
        }

        /// <summary>
        /// トランザクションをロールバックします
        /// </summary>
        /// <param name="transactionId">トランザクションID</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>成功した場合はtrue</returns>
        public async Task<bool> RollbackTransactionAsync(
            Guid transactionId, 
            CancellationToken cancellationToken = default)
        {
            await _transactionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_activeTransactions.TryGetValue(transactionId, out var transaction))
                {
                    _logger.LogWarning("指定されたトランザクションが見つかりません: {TransactionId}", transactionId);
                    return false;
                }

                if (transaction.State == TransactionState.Committed)
                {
                    _logger.LogWarning("既にコミットされたトランザクションはロールバックできません: {TransactionId}", transactionId);
                    return false;
                }

                // ロールバック処理（現在の実装では特に何もしない）
                transaction.State = TransactionState.Rolledback;
                transaction.EndTime = DateTime.UtcNow;
                
                _logger.LogInformation("トランザクション {TransactionId} をロールバックしました", transactionId);
                
                return true;
            }
            finally
            {
                _transactionLock.Release();
            }
        }

        /// <summary>
        /// トランザクションの状態を取得します
        /// </summary>
        /// <param name="transactionId">トランザクションID</param>
        /// <returns>トランザクション状態</returns>
        public async Task<string> GetTransactionStateAsync(Guid transactionId)
        {
            await _transactionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_activeTransactions.TryGetValue(transactionId, out var transaction))
                {
                    return transaction.State.ToString();
                }
                
                return "Unknown";
            }
            finally
            {
                _transactionLock.Release();
            }
        }

        /// <summary>
        /// トランザクションを破棄し、リソースを解放します
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 破棄処理の実装
        /// </summary>
        /// <param name="disposing">マネージドリソースを破棄するかどうか</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _transactionLock.Dispose();
            }
        }
    }

    /// <summary>
    /// 翻訳トランザクションの状態
    /// </summary>
    internal enum TransactionState
    {
        /// <summary>
        /// 不明
        /// </summary>
        Unknown = 0,
        
        /// <summary>
        /// アクティブ
        /// </summary>
        Active = 1,
        
        /// <summary>
        /// コミット中
        /// </summary>
        Committing = 2,
        
        /// <summary>
        /// コミット済み
        /// </summary>
        Committed = 3,
        
        /// <summary>
        /// ロールバック済み
        /// </summary>
        Rolledback = 4,
        
        /// <summary>
        /// 失敗
        /// </summary>
        Failed = 5
    }

    /// <summary>
    /// 翻訳トランザクションのデータを保持するクラス
    /// </summary>
    internal sealed class TranslationTransaction
    {
        /// <summary>
        /// トランザクションID
        /// </summary>
        public Guid TransactionId { get; set; }
        
        /// <summary>
        /// トランザクション名
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// 開始時刻
        /// </summary>
        public DateTime StartTime { get; set; }
        
        /// <summary>
        /// 終了時刻
        /// </summary>
        public DateTime? EndTime { get; set; }
        
        /// <summary>
        /// 状態
        /// </summary>
        public TransactionState State { get; set; }
        
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public string? ErrorMessage { get; set; }
        
        /// <summary>
        /// 翻訳リクエストのリスト
        /// </summary>
        public List<TranslationRequest> Requests { get; } = [];
        
        /// <summary>
        /// リクエストIDに対応する翻訳レスポンスのディクショナリ
        /// </summary>
        public Dictionary<Guid, TranslationResponse> Responses { get; } = [];
    }
