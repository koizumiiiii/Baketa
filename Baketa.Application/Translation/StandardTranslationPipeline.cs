using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Cache;
using Baketa.Core.Translation.Events;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Common;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Translation
{
    /// <summary>
    /// 標準翻訳パイプライン
    /// 翻訳パイプラインは、翻訳リクエストを受け取り、キャッシュ確認、翻訳実行、
    /// 結果の保存、イベント発行などの処理を一貫して行います。
    /// </summary>
#pragma warning disable CA2016 // CancellationTokenパラメーターの転送問題を一時的に拘束します
    public class StandardTranslationPipeline : ITranslationPipeline
    {
        private readonly ILogger<StandardTranslationPipeline> _logger;
        private readonly ITranslationEngineFactory _engineFactory;
        private readonly ITranslationCache _cache;
        private readonly ITranslationManager _translationManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly ITranslationTransactionManager _transactionManager;
        private readonly Baketa.Core.Translation.Common.TranslationOptions _options;

        /// <summary>
        /// 標準翻訳パイプラインのコンストラクタ
        /// </summary>
        /// <param name="logger">ロガー</param>
        /// <param name="engineFactory">翻訳エンジンファクトリー</param>
        /// <param name="cache">翻訳キャッシュ</param>
        /// <param name="translationManager">翻訳マネージャー</param>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="options">翻訳オプション</param>
        public StandardTranslationPipeline(
            ILogger<StandardTranslationPipeline> logger,
            ITranslationEngineFactory engineFactory,
            ITranslationCache cache,
            ITranslationManager translationManager,
            IEventAggregator eventAggregator,
            ITranslationTransactionManager transactionManager,
            Baketa.Core.Translation.Common.TranslationOptions options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _translationManager = translationManager ?? throw new ArgumentNullException(nameof(translationManager));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// 翻訳パイプラインを実行します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="preferredEngine">優先エンジン名（オプション）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンス</returns>
        /// <exception cref="ArgumentNullException">requestがnullの場合</exception>
        /// <exception cref="InvalidOperationException">翻訳エンジンが見つからない場合やエンジンからの応答が無効な場合</exception>
        public async Task<TranslationResponse> ExecuteAsync(
            TranslationRequest request,
            string? preferredEngine = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request, nameof(request));
            ArgumentNullException.ThrowIfNull(request.SourceLanguage, nameof(request.SourceLanguage));
            ArgumentNullException.ThrowIfNull(request.TargetLanguage, nameof(request.TargetLanguage));

            // 翻訳リクエスト開始イベントを発行
            var startedEvent = new TranslationStartedEvent
            {
                RequestId = request.RequestId.ToString(),
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage.Code,
                TargetLanguage = request.TargetLanguage.Code,
                Context = request.Context != null ? new TranslationEventContext(request.Context) : null,
                // Timestampはプロパティとして実装されているため、コンストラクタで初期化される
                // Timestamp = DateTimeOffset.UtcNow
            };
            
            await _eventAggregator.PublishAsync(startedEvent).ConfigureAwait(false);

            // パフォーマンス計測開始
            var stopwatch = Stopwatch.StartNew();
            
            // トランザクションの開始
            Guid? transactionId = null;
            if (_options.PipelineOptions.EnableTransactions)
            {
                transactionId = await _transactionManager.BeginTransactionAsync(
                    $"Translation_{request.RequestId}",
                    cancellationToken).ConfigureAwait(false);
                
                // リクエストをトランザクションに追加
                await _transactionManager.AddRequestToTransactionAsync(
                transactionId.Value, request, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                // 1. キャッシュ確認
                if (_options.CacheOptions.EnableMemoryCache)
                {
                    var cacheKey = CacheKeyGenerator.GenerateKey(
                        request.SourceText,
                        request.SourceLanguage.Code.ToString(),
                        request.TargetLanguage.Code.ToString(),
                        preferredEngine ?? "any");

                    var cachedEntry = await _cache.GetAsync(cacheKey).ConfigureAwait(false);
                    if (cachedEntry != null)
                    {
                        _logger.LogDebug("キャッシュヒット: {SourceText}", 
                            request.SourceText.Length > 30 
                                ? string.Concat(request.SourceText.AsSpan(0, 27), "...") 
                                : request.SourceText);

                        // キャッシュヒットイベントを発行
                        var cacheHitEvent = new TranslationCacheHitEvent
                        {
                            RequestId = request.RequestId.ToString(),
                            SourceText = request.SourceText,
                            SourceLanguage = request.SourceLanguage.Code,
                            TargetLanguage = request.TargetLanguage.Code,
                            TranslatedText = cachedEntry.TranslatedText ?? string.Empty,
                            // Timestampはプロパティとして実装されているため、コンストラクタで初期化される
                            // Timestamp = DateTimeOffset.UtcNow
                        };
                        
                        await _eventAggregator.PublishAsync(cacheHitEvent).ConfigureAwait(false);

                        // キャッシュから翻訳結果を取得
                        var cachedResponse = TranslationResponse.CreateSuccess(
                            request,
                            cachedEntry.TranslatedText ?? string.Empty,
                            cachedEntry.Engine,
                            stopwatch.ElapsedMilliseconds
                        );
                        cachedResponse.Metadata["FromCache"] = true;

                        // 翻訳完了イベントを発行
                        var completedEvent = new TranslationCompletedEvent
                        {
                            RequestId = request.RequestId.ToString(),
                            SourceText = request.SourceText,
                            SourceLanguage = request.SourceLanguage.Code,
                            TargetLanguage = request.TargetLanguage.Code,
                            TranslatedText = cachedEntry.TranslatedText ?? string.Empty,
                            ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                            TranslationEngine = cachedEntry.Engine,
                            FromCache = true,
                            // Timestampはプロパティとして実装されているため、コンストラクタで初期化される
                            // Timestamp = DateTimeOffset.UtcNow
                        };
                        
                        await _eventAggregator.PublishAsync(completedEvent).ConfigureAwait(false);

                        return cachedResponse;
                    }
                }

                // 2. 翻訳エンジンの選択
                Core.Abstractions.Translation.ITranslationEngine engine;
                
                if (!string.IsNullOrEmpty(preferredEngine))
                {
                    engine = await _engineFactory.GetEngineAsync(preferredEngine).ConfigureAwait(false)
                        ?? throw new InvalidOperationException($"指定された翻訳エンジン '{preferredEngine}' が見つかりません。");
                }
                else
                {
                    var languagePair = new Baketa.Core.Translation.Models.LanguagePair
                    {
                        SourceLanguage = new Baketa.Core.Translation.Models.Language { 
                            Code = request.SourceLanguage.Code, 
                            Name = !string.IsNullOrEmpty(request.SourceLanguage.DisplayName) ? request.SourceLanguage.DisplayName : request.SourceLanguage.Code,
                            DisplayName = !string.IsNullOrEmpty(request.SourceLanguage.DisplayName) ? request.SourceLanguage.DisplayName : request.SourceLanguage.Code
                        },
                        TargetLanguage = new Baketa.Core.Translation.Models.Language { 
                            Code = request.TargetLanguage.Code, 
                            Name = !string.IsNullOrEmpty(request.TargetLanguage.DisplayName) ? request.TargetLanguage.DisplayName : request.TargetLanguage.Code,
                            DisplayName = !string.IsNullOrEmpty(request.TargetLanguage.DisplayName) ? request.TargetLanguage.DisplayName : request.TargetLanguage.Code
                        }
                    };
                    engine = await _engineFactory.GetBestEngineForLanguagePairAsync(languagePair).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("翻訳エンジンが見つかりません。");
                }

                // 3. 翻訳実行
                _logger.LogDebug("翻訳実行: エンジン '{EngineName}', {SourceLanguage} -> {TargetLanguage}, テキスト: {SourceText}",
                    engine.Name,
                    request.SourceLanguage.Code,
                    request.TargetLanguage.Code,
                    request.SourceText.Length > 30 
                        ? string.Concat(request.SourceText.AsSpan(0, 27), "...") 
                        : request.SourceText);

                // null参照を安全に扱うよう修正
                var translationRequest = new TranslationRequest
                {
                    SourceText = request.SourceText,
                    SourceLanguage = new Language { 
                        Code = request.SourceLanguage.Code, 
                        Name = !string.IsNullOrEmpty(request.SourceLanguage.DisplayName) ? request.SourceLanguage.DisplayName : request.SourceLanguage.Code,
                        DisplayName = !string.IsNullOrEmpty(request.SourceLanguage.DisplayName) ? request.SourceLanguage.DisplayName : request.SourceLanguage.Code
                    },
                    TargetLanguage = new Language { 
                        Code = request.TargetLanguage.Code, 
                        Name = !string.IsNullOrEmpty(request.TargetLanguage.DisplayName) ? request.TargetLanguage.DisplayName : request.TargetLanguage.Code,
                        DisplayName = !string.IsNullOrEmpty(request.TargetLanguage.DisplayName) ? request.TargetLanguage.DisplayName : request.TargetLanguage.Code
                    },
                    Context = request.Context != null ? new TranslationContext { DialogueId = request.Context.ToString() } : null
                };

                var engineResponse = await engine.TranslateAsync(translationRequest, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("翻訳エンジンからの応答がnullでした。");

                // 4. 翻訳結果の変換
                var response = TranslationResponse.CreateSuccess(
                    request,
                    engineResponse.TranslatedText ?? string.Empty,
                    engine.Name,
                    stopwatch.ElapsedMilliseconds
                );
                
                // エラーの場合は成功フラグを変更
                if (!engineResponse.IsSuccess)
                {
                    response.IsSuccess = false;
                    response.Error = engineResponse.Error is not null 
                        ? new TranslationError 
                        { 
                            ErrorCode = engineResponse.Error.ErrorCode, 
                            Message = engineResponse.Error.Message,
                            ErrorType = TranslationErrorType.Unknown
                        } 
                        : null;
                }
                response.Metadata["FromCache"] = false;

                // 5. キャッシュに保存
                if (response.IsSuccess && _options.CacheOptions.EnableMemoryCache)
                {
                    var cacheKey = CacheKeyGenerator.GenerateKey(
                    request.SourceText,
                    request.SourceLanguage.Code.ToString(),
                    request.TargetLanguage.Code.ToString(),
                    engine.Name);

                    var cacheEntry = new TranslationCacheEntry
                    {
                    SourceText = request.SourceText,
                    TranslatedText = response.TranslatedText ?? string.Empty,
                    SourceLanguage = request.SourceLanguage.Code,
                    TargetLanguage = request.TargetLanguage.Code,
                    Engine = engine.Name,
                    CreatedAt = DateTime.UtcNow,
                    LastAccessedAt = DateTime.UtcNow,
                    AccessCount = 1
                    };

                    await _cache.SetAsync(cacheKey, cacheEntry, TimeSpan.FromHours(_options.CacheOptions.DefaultExpirationHours), cancellationToken)
                        .ConfigureAwait(false);

                    _logger.LogDebug("キャッシュに保存: {SourceText}", 
                        request.SourceText.Length > 30 
                            ? string.Concat(request.SourceText.AsSpan(0, 27), "...") 
                            : request.SourceText);
                }

                // 6. 翻訳レコードとして保存
                if (response.IsSuccess)
                {
                    // トランザクションが有効な場合はトランザクションに保存
                    if (transactionId.HasValue)
                    {
                        await _transactionManager.SaveResponseToTransactionAsync(
                        transactionId.Value, response, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
#pragma warning disable CA2016 // この特定行に対するCancellationToken警告を拘束
                        // トランザクションがない場合は直接保存
                        // SaveTranslationAsyncメソッドが実装された際には、以下のようにCancellationTokenを渡す
                        // await _translationManager.SaveTranslationAsync(response, request.Context, cancellationToken)
                        //    .ConfigureAwait(false);
                        _logger.LogDebug("Saving translation to database (implementation required)");
#pragma warning restore CA2016
                    }
                }

                // 7. 翻訳完了イベントを発行
                if (response.IsSuccess)
                {
                    var completedEvent = new TranslationCompletedEvent
                    {
                        RequestId = request.RequestId.ToString(),
                        SourceText = request.SourceText,
                        SourceLanguage = request.SourceLanguage.Code,
                        TargetLanguage = request.TargetLanguage.Code,
                        TranslatedText = response.TranslatedText ?? string.Empty,
                        ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                        TranslationEngine = engine.Name,
                        // FromCacheプロパティは存在しないため、メタデータに情報を追加
                        // FromCache = false,
                        // Timestampは読み取り専用プロパティのためコメントアウト
                        // Timestamp = DateTimeOffset.UtcNow
                    };
                    
                    await _eventAggregator.PublishAsync(completedEvent).ConfigureAwait(false);
                }
                else
                {
                    // エラーイベントを発行
                    var errorEvent = new TranslationErrorEvent
                    {
                        RequestId = request.RequestId.ToString(),
                        SourceText = request.SourceText,
                        SourceLanguage = request.SourceLanguage.Code,
                        TargetLanguage = request.TargetLanguage.Code,
                        ErrorMessage = response.Error?.Message ?? "未知のエラー",
                        ErrorType = TranslationErrorType.Engine,
                        TranslationEngine = engine.Name,
                        ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                        // Timestampは読み取り専用プロパティのためコメントアウト
                        // Timestamp = DateTimeOffset.UtcNow
                    };
                    
                    await _eventAggregator.PublishAsync(errorEvent).ConfigureAwait(false);
                }

                // トランザクションが有効な場合はコミット
                // トランザクションのロールバック処理
                if (transactionId.HasValue && response.IsSuccess)
                {
                    var commitResult = await _transactionManager.CommitTransactionAsync(
                        transactionId.Value, cancellationToken).ConfigureAwait(false);
                    
                    if (commitResult == null)
                    {
                        _logger.LogWarning("トランザクション {TransactionId} のコミットに失敗しました", transactionId);
                    }
                }
                
                return response;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "翻訳処理がキャンセルされました: {Message}", ex.Message);
                
                // トランザクションが有効な場合はロールバック
                // トランザクションのコミット処理

                if (transactionId.HasValue)
                {
                    await _transactionManager.RollbackTransactionAsync(
                    transactionId.Value, cancellationToken).ConfigureAwait(false);
            }

                // エラーイベントを発行
                var errorEvent = new TranslationErrorEvent
                {
                    RequestId = request.RequestId.ToString(),
                    SourceText = request.SourceText,
                    SourceLanguage = request.SourceLanguage.Code,
                    TargetLanguage = request.TargetLanguage.Code,
                    ErrorMessage = ex.Message,
                    ErrorType = TranslationErrorType.Exception,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
                
                await _eventAggregator.PublishAsync(errorEvent).ConfigureAwait(false);

                // キャンセルエラーレスポンスを作成
                var errorResponse = TranslationResponse.CreateError(
                    request,
                    new TranslationError
                    {
                        ErrorCode = "OPERATION_CANCELED",
                        Message = ex.Message,
                        ErrorType = TranslationErrorType.Exception
                    },
                    "Error"
                );
                errorResponse.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                errorResponse.Metadata["FromCache"] = false;
                return errorResponse;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "翻訳パイプライン実行中に無効な操作が発生しました: {Message}", ex.Message);
                
                // トランザクションが有効な場合はロールバック
                // トランザクションのロールバック処理

                if (transactionId.HasValue)
                {
                    await _transactionManager.RollbackTransactionAsync(
                        transactionId.Value, cancellationToken).ConfigureAwait(false);
                }

                // エラーイベントを発行
                var errorEvent = new TranslationErrorEvent
                {
                    RequestId = request.RequestId.ToString(),
                    SourceText = request.SourceText,
                    SourceLanguage = request.SourceLanguage.Code,
                    TargetLanguage = request.TargetLanguage.Code,
                    ErrorMessage = ex.Message,
                    ErrorType = TranslationErrorType.Engine, // 翻訳エンジンでの問題が多い
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
                
                await _eventAggregator.PublishAsync(errorEvent).ConfigureAwait(false);

                var errorResponse = TranslationResponse.CreateError(
                    request,
                    new TranslationError
                    {
                        ErrorCode = "INVALID_OPERATION",
                        Message = ex.Message,
                        ErrorType = TranslationErrorType.Engine
                    },
                    "Error"
                );
                errorResponse.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                errorResponse.Metadata["FromCache"] = false;
                return errorResponse;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "翻訳パイプライン実行中に引数エラーが発生しました: {Message}", ex.Message);
                
                // トランザクションが有効な場合はロールバック
                // キャンセレーショントークンを適切に転送
                if (transactionId.HasValue)
                {
                    await _transactionManager.RollbackTransactionAsync(
                transactionId.Value, cancellationToken).ConfigureAwait(false);
                }

                // エラーイベントを発行
                var errorEvent = new TranslationErrorEvent
                {
                    RequestId = request.RequestId.ToString(),
                    SourceText = request.SourceText,
                    SourceLanguage = request.SourceLanguage.Code,
                    TargetLanguage = request.TargetLanguage.Code,
                    ErrorMessage = ex.Message,
                    ErrorType = TranslationErrorType.InvalidInput,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
                
                await _eventAggregator.PublishAsync(errorEvent).ConfigureAwait(false);

                var errorResponse = TranslationResponse.CreateError(
                    request,
                    new TranslationError
                    {
                        ErrorCode = "INVALID_ARGUMENT",
                        Message = ex.Message,
                        ErrorType = TranslationErrorType.InvalidInput
                    },
                    "Error"
                );
                errorResponse.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                errorResponse.Metadata["FromCache"] = false;
                return errorResponse;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException && ex is not SystemException && ex is not OperationCanceledException && ex is not InvalidOperationException && ex is not ArgumentException)
            {
                _logger.LogError(ex, "翻訳パイプライン実行中にエラーが発生しました: {Message}", ex.Message);
                
                // トランザクションが有効な場合はロールバック
                // トランザクションのロールバック処理
                if (transactionId.HasValue)
                {
                    await _transactionManager.RollbackTransactionAsync(
                        transactionId.Value, cancellationToken).ConfigureAwait(false);
                }

                // エラーイベントを発行
                var errorEvent = new TranslationErrorEvent
                {
                    RequestId = request.RequestId.ToString(),
                    SourceText = request.SourceText,
                    SourceLanguage = request.SourceLanguage.Code,
                    TargetLanguage = request.TargetLanguage.Code,
                    ErrorMessage = ex.Message,
                    ErrorType = TranslationErrorType.Exception,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                    // Timestampは読み取り専用プロパティのためコメントアウト
                    // Timestamp = DateTimeOffset.UtcNow
                };
                
                await _eventAggregator.PublishAsync(errorEvent).ConfigureAwait(false);

                var errorResponse = TranslationResponse.CreateError(
                    request,
                    new TranslationError
                    {
                        ErrorCode = "PIPELINE_ERROR",
                        Message = ex.Message,
                        ErrorType = TranslationErrorType.Exception
                    },
                    "Error"
                );
                errorResponse.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                errorResponse.Metadata["FromCache"] = false;
                return errorResponse;
            }
        }

        /// <summary>
        /// 複数の翻訳リクエストをバッチ処理します
        /// </summary>
        /// <param name="requests">翻訳リクエストのリスト</param>
        /// <param name="preferredEngine">優先エンジン名（オプション）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンスのリスト</returns>
#pragma warning disable CA2016 // ExecuteBatchAsyncメソッド全体でCancellationToken警告を拘束
        public async Task<IReadOnlyList<TranslationResponse>> ExecuteBatchAsync(
            IReadOnlyList<TranslationRequest> requests,
            string? preferredEngine = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requests, nameof(requests));

            if (requests.Count == 0)
            {
#pragma warning disable IDE0301 // コレクションの初期化を簡素化できます
                return Array.Empty<TranslationResponse>();
#pragma warning restore IDE0301
            }

            if (requests.Count == 1)
            {
#pragma warning disable IDE0300 // コレクションの初期化を簡素化できます
                var response = await ExecuteAsync(requests[0], preferredEngine, cancellationToken).ConfigureAwait(false);
                return new TranslationResponse[] { response };
#pragma warning restore IDE0300
            }

            // バッチ処理のパフォーマンス計測開始
            var stopwatch = Stopwatch.StartNew();
            
            // トランザクションの開始
            Guid? transactionId = null;
            if (_options.PipelineOptions.EnableTransactions)
            {
                transactionId = await _transactionManager.BeginTransactionAsync(
                    $"BatchTranslation_{Guid.NewGuid()}",
                    cancellationToken).ConfigureAwait(false);
                
                // 各リクエストをトランザクションに追加
                foreach (var request in requests)
                {
                    await _transactionManager.AddRequestToTransactionAsync(
                        transactionId.Value, request, cancellationToken).ConfigureAwait(false);
                }
            }

            try
            {
                // 言語ペアに基づいてリクエストをグループ化
                // CancellationTokenはこのLINQ操作に直接適用できないため、手動で持続的にチェックする
                cancellationToken.ThrowIfCancellationRequested(); // 操作前に手動でキャンセル確認
                var requestGroups = requests.GroupBy(r => new { SourceLangCode = r.SourceLanguage.Code.ToString(), TargetLangCode = r.TargetLanguage.Code.ToString() })
                    .ToDictionary(g => g.Key, g => g.ToList());

                var results = new List<TranslationResponse>();

                foreach (var group in requestGroups)
                {
                    var sourceLang = group.Value.First().SourceLanguage;
                    var targetLang = group.Value.First().TargetLanguage;

                    // 2. 各グループに最適なエンジンを選択
                    Core.Abstractions.Translation.ITranslationEngine engine;
                    
                    if (!string.IsNullOrEmpty(preferredEngine))
                    {
                        engine = await _engineFactory.GetEngineAsync(preferredEngine).ConfigureAwait(false) ?? throw new InvalidOperationException($"指定された翻訳エンジン '{preferredEngine}' が見つかりません。");
                    }
                    else
                    {
                        var languagePair = new Baketa.Core.Translation.Models.LanguagePair
                        {
                            SourceLanguage = new Baketa.Core.Translation.Models.Language { 
                                Code = sourceLang.Code, 
                                Name = !string.IsNullOrEmpty(sourceLang.DisplayName) ? sourceLang.DisplayName : sourceLang.Code,
                                DisplayName = !string.IsNullOrEmpty(sourceLang.DisplayName) ? sourceLang.DisplayName : sourceLang.Code
                            },
                            TargetLanguage = new Baketa.Core.Translation.Models.Language { 
                                Code = targetLang.Code, 
                                Name = !string.IsNullOrEmpty(targetLang.DisplayName) ? targetLang.DisplayName : targetLang.Code,
                                DisplayName = !string.IsNullOrEmpty(targetLang.DisplayName) ? targetLang.DisplayName : targetLang.Code
                            }
                        };
                        engine = await _engineFactory.GetBestEngineForLanguagePairAsync(languagePair).ConfigureAwait(false)
                            ?? throw new InvalidOperationException(
                                $"言語ペア '{sourceLang?.Code}' -> '{targetLang?.Code}' に対応する翻訳エンジンが見つかりません。");
                    }

                    // 3. バッチ処理のためのリクエスト準備
                    var groupRequests = group.Value;
                    
                    // 3.1 キャッシュチェック
                    var cacheKeys = new Dictionary<string, (TranslationRequest Request, string CacheKey)>();
                    var nonCachedRequests = new List<(TranslationRequest Request, int OriginalIndex)>();
                    
                    if (_options.CacheOptions.EnableMemoryCache)
                    {
                        for (int i = 0; i < groupRequests.Count; i++)
                        {
                            var req = groupRequests[i];
                            if (req.SourceLanguage != null && req.TargetLanguage != null)
                            {
                                var cacheKey = CacheKeyGenerator.GenerateKey(
                                    req.SourceText,
                                    req.SourceLanguage.Code,
                                    req.TargetLanguage.Code,
                                    engine.Name);
                                    
                                cacheKeys.Add(req.RequestId.ToString(), (req, cacheKey));
                            }
                        }

                        // キャッシュから一括取得
                        var cachedEntries = await _cache.GetManyAsync(cacheKeys.Values.Select(v => v.CacheKey)).ConfigureAwait(false);
                        
                        var cacheHitResponses = new List<TranslationResponse>();
                        var cacheHitEvents = new List<TranslationCacheHitEvent>();
                        var cacheCompletedEvents = new List<TranslationCompletedEvent>();
                        
                        foreach (var kvp in cacheKeys)
                        {
                            var reqId = kvp.Key;
                            var req = kvp.Value.Request;
                            var key = kvp.Value.CacheKey;
                            
                            if (cachedEntries.TryGetValue(key, out var entry) && entry != null)
                            {
                                // キャッシュヒットした場合
                                var hitResponse = TranslationResponse.CreateSuccess(
                                    req,
                                    entry.TranslatedText ?? string.Empty,
                                    entry.Engine,
                                    0 // 後で更新
                                );
                                cacheHitResponses.Add(hitResponse);
                                var index = cacheHitResponses.Count - 1;
                                cacheHitResponses[index].Metadata["FromCache"] = true;
                                
                                // キャッシュヒットイベント準備
                                cacheHitEvents.Add(new TranslationCacheHitEvent
                                {
                                    RequestId = reqId,
                                    SourceText = req.SourceText,
                                    SourceLanguage = req.SourceLanguage.Code,
                                    TargetLanguage = req.TargetLanguage.Code,
                                    TranslatedText = entry.TranslatedText ?? string.Empty,
                                    // Timestampはプロパティとして実装されているため、コンストラクタで初期化される
                            // Timestamp = DateTimeOffset.UtcNow
                                });
                                
                                // 翻訳完了イベント準備 (キャッシュヒット)
                                cacheCompletedEvents.Add(new TranslationCompletedEvent
                                {
                                    RequestId = reqId,
                                    SourceText = req.SourceText,
                                    SourceLanguage = req.SourceLanguage.Code,
                                    TargetLanguage = req.TargetLanguage.Code,
                                    TranslatedText = entry.TranslatedText ?? string.Empty,
                                    ProcessingTimeMs = 0, // 後で更新
                                    TranslationEngine = entry.Engine,
                                    FromCache = true,
                                    // Timestampはプロパティとして実装されているため、コンストラクタで初期化される
                                    // Timestamp = DateTimeOffset.UtcNow
                                });
                            }
                            else
                            {
                                // キャッシュにない場合は翻訳対象として追加
                                cancellationToken.ThrowIfCancellationRequested(); // 操作前に手動でキャンセル確認
                                nonCachedRequests.Add((req, groupRequests.IndexOf(req)));
                            }
                        }
                        
                        // キャッシュヒットした部分の処理時間更新
                        foreach (var resp in cacheHitResponses)
                        {
                            resp.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                        }
                        
                        foreach (var evt in cacheCompletedEvents)
                        {
                            evt.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                        }
                        
                        // キャッシュヒットイベントを一括発行
                        foreach (var evt in cacheHitEvents)
                        {
                            await _eventAggregator.PublishAsync(evt).ConfigureAwait(false);
                        }
                        
                        // 翻訳完了イベント（キャッシュヒット）を一括発行
                        foreach (var evt in cacheCompletedEvents)
                        {
                            await _eventAggregator.PublishAsync(evt).ConfigureAwait(false);
                        }
                        
                        // キャッシュヒット結果を追加
                        results.AddRange(cacheHitResponses);
                    }
                    else
                    {
                        // キャッシュ無効の場合は全てのリクエストを翻訳
                        cancellationToken.ThrowIfCancellationRequested(); // 手動キャンセルチェック
                        nonCachedRequests = groupRequests.Select((r, i) => (r, i)).ToList();
                    }
                    
                    // 翻訳が必要なリクエストがある場合
                    if (nonCachedRequests.Count > 0)
                    {
                        var engineRequests = nonCachedRequests.Select(x => new TranslationRequest
                        {
                            SourceText = x.Request.SourceText,
                            SourceLanguage = new Language { 
                                Code = x.Request.SourceLanguage.Code, 
                                Name = !string.IsNullOrEmpty(x.Request.SourceLanguage.DisplayName) ? x.Request.SourceLanguage.DisplayName : x.Request.SourceLanguage.Code,
                                DisplayName = !string.IsNullOrEmpty(x.Request.SourceLanguage.DisplayName) ? x.Request.SourceLanguage.DisplayName : x.Request.SourceLanguage.Code
                            },
                            TargetLanguage = new Language { 
                                Code = x.Request.TargetLanguage.Code, 
                                Name = !string.IsNullOrEmpty(x.Request.TargetLanguage.DisplayName) ? x.Request.TargetLanguage.DisplayName : x.Request.TargetLanguage.Code,
                                DisplayName = !string.IsNullOrEmpty(x.Request.TargetLanguage.DisplayName) ? x.Request.TargetLanguage.DisplayName : x.Request.TargetLanguage.Code
                            },
                            // RequestIdは読み取り専用プロパティなので形式変更
                            // 代入はできないのでここではスキップ
                            // RequestId = x.Request.RequestId, 
                            Context = x.Request.Context != null ? new TranslationContext { DialogueId = x.Request.Context.ToString() } : null
                        }).ToList();

                        // 4. 一括翻訳実行
                        cancellationToken.ThrowIfCancellationRequested(); // 翻訳実行前にキャンセル確認
                        var engineResponses = await engine.TranslateBatchAsync(engineRequests, cancellationToken)
                            .ConfigureAwait(false);

                        var cacheEntriesToSet = new Dictionary<string, TranslationCacheEntry>();
                        var completedEvents = new List<TranslationCompletedEvent>();
                        var errorEvents = new List<TranslationErrorEvent>();

                        // リクエストとレスポンスのマッピング
                        for (int i = 0; i < nonCachedRequests.Count; i++)
                        {
                            var originalRequest = nonCachedRequests[i].Request;
                            var engineResponse = (i < engineResponses.Count)
                                    ? engineResponses[i] 
                                    : new TranslationResponse
                               {
                                // RequestIdはrequiredプロパティで初期化が必要
                                RequestId = Guid.NewGuid(), // 新しいGUIDを生成して設定
                                SourceText = engineRequests[i].SourceText,
                                SourceLanguage = engineRequests[i].SourceLanguage,
                                TargetLanguage = engineRequests[i].TargetLanguage,
                                IsSuccess = false,
                                EngineName = "ErrorEngine", // 必須プロパティを追加
                                Error = new TranslationError
                                {
                                    ErrorCode = "NO_RESPONSE", // 修正：CodeからErrorCodeに変更
                                    Message = "翻訳エンジンからの応答がありませんでした",
                                    ErrorType = TranslationErrorType.Unknown
                                }
                            };

                            var response = TranslationResponse.CreateSuccess(
                                originalRequest,
                                engineResponse.TranslatedText ?? string.Empty,
                                engine.Name,
                                stopwatch.ElapsedMilliseconds
                            );
                            
                            // エラーの場合は成功フラグを変更
                            if (!engineResponse.IsSuccess)
                            {
                                response.IsSuccess = false;
                                // null参照を安全に扱うよう修正
                                response.Error = engineResponse.Error is null
                                    ? null
                                    : new TranslationError
                                    { 
                                        ErrorCode = engineResponse.Error.ErrorCode ?? "UNKNOWN_ERROR", 
                                        Message = engineResponse.Error.Message ?? "不明なエラー",
                                        ErrorType = TranslationErrorType.Unknown
                                    };
                            }
                            response.Metadata["FromCache"] = false;

                            results.Add(response);

                            // 成功した翻訳はキャッシュと翻訳レコードに保存
                            if (response.IsSuccess)
                            {
                                if (_options.CacheOptions.EnableMemoryCache)
                                {
                                    var cacheKey = CacheKeyGenerator.GenerateKey(
                                        originalRequest.SourceText,
                                        originalRequest.SourceLanguage.Code.ToString(),
                                        originalRequest.TargetLanguage.Code.ToString(),
                                        engine.Name);

                                    var cacheEntry = new TranslationCacheEntry
                                    {
                                        SourceText = originalRequest.SourceText,
                                        TranslatedText = response.TranslatedText ?? string.Empty,
                                        SourceLanguage = originalRequest.SourceLanguage.Code,
                                        TargetLanguage = originalRequest.TargetLanguage.Code,
                                        Engine = engine.Name,
                                        CreatedAt = DateTime.UtcNow,
                                        LastAccessedAt = DateTime.UtcNow,
                                        AccessCount = 1
                                    };

                                    cacheEntriesToSet[cacheKey] = cacheEntry;
                                }

#pragma warning disable CA2016 // この特定行に対するCancellationToken警告を拘束
                                // 翻訳レコードとして保存
                                // SaveTranslationAsyncメソッドが実装された際には、以下のようにCancellationTokenを渡す
                                // await _translationManager.SaveTranslationAsync(response, originalRequest.Context, cancellationToken)
                                //    .ConfigureAwait(false);
                                _logger.LogDebug("Saving batch translation to database (implementation required)");
#pragma warning restore CA2016

                                // 翻訳完了イベント準備
                                completedEvents.Add(new TranslationCompletedEvent
                                {
                                    RequestId = originalRequest.RequestId.ToString(),
                                    SourceText = originalRequest.SourceText,
                                    SourceLanguage = originalRequest.SourceLanguage.Code,
                                    TargetLanguage = originalRequest.TargetLanguage.Code,
                                    TranslatedText = response.TranslatedText ?? string.Empty,
                                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                                    TranslationEngine = engine.Name,
                                    FromCache = false,
                                    // Timestampは読み取り専用プロパティのためコメントアウト
                                    // Timestamp = DateTimeOffset.UtcNow
                                });
                            }
                            else
                            {
                                                // エラーイベント準備
                                errorEvents.Add(new TranslationErrorEvent
                                {
                                    RequestId = originalRequest.RequestId.ToString(),
                                    SourceText = originalRequest.SourceText,
                                    SourceLanguage = originalRequest.SourceLanguage.Code,
                                    TargetLanguage = originalRequest.TargetLanguage.Code,
                                    ErrorMessage = response.Error?.Message ?? "未知のエラー",
                                    ErrorType = response.Error?.ErrorType ?? TranslationErrorType.Unknown,
                                    TranslationEngine = engine.Name,
                                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                                    // Timestampは読み取り専用プロパティのためコメントアウト
                                    // Timestamp = DateTimeOffset.UtcNow
                                });
                            }
                        }

                        // キャッシュに一括保存
                        if (cacheEntriesToSet.Count > 0 && _options.CacheOptions.EnableMemoryCache)
                        {
                            await _cache.SetManyAsync(cacheEntriesToSet, 
                                    TimeSpan.FromHours(_options.CacheOptions.DefaultExpirationHours), cancellationToken)
                                .ConfigureAwait(false);
                        }

                        // イベント発行
                        foreach (var evt in completedEvents)
                        {
                            await _eventAggregator.PublishAsync(evt).ConfigureAwait(false);
                        }

                        foreach (var evt in errorEvents)
                        {
                            await _eventAggregator.PublishAsync(evt).ConfigureAwait(false);
                        }
                    }
                }
                
                // トランザクションが有効な場合はコミット
                // トランザクションのコミット処理
                if (transactionId.HasValue)
                {
                    var commitResult = await _transactionManager.CommitTransactionAsync(
                        transactionId.Value, cancellationToken).ConfigureAwait(false);
                    
                    if (commitResult == null)
                    {
                        _logger.LogWarning("トランザクション {TransactionId} のコミットに失敗しました", transactionId);
                    }
                }

                // 結果を元のリクエスト順に整列
                cancellationToken.ThrowIfCancellationRequested(); // 結果整理前にキャンセル確認
                var orderedResults = new List<TranslationResponse>(requests.Count);
                foreach (var req in requests)
                {
                    cancellationToken.ThrowIfCancellationRequested(); // 各ループでキャンセル確認
                    var response = results.FirstOrDefault(r => r.RequestId == req.RequestId);
                    if (response != null)
                    {
                        orderedResults.Add(response);
                    }
                }
                return orderedResults.AsReadOnly();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && 
                                   ex is not StackOverflowException && 
                                   ex is not ThreadAbortException && 
                                   ex is not SystemException)
            {
                _logger.LogError(ex, "翻訳バッチパイプライン実行中にエラーが発生しました: {Message}", ex.Message);
                
                // トランザクションが有効な場合はロールバック
                // トランザクションのロールバック処理
                if (transactionId.HasValue)
                {
                    await _transactionManager.RollbackTransactionAsync(
                        transactionId.Value, cancellationToken).ConfigureAwait(false);
                }

                // 全リクエストに対してエラーレスポンスを作成
                var errorResponses = new List<TranslationResponse>();
                
                // パラメータのrequestsを使用（nullチェックは既に行われているが安全のため）
                var batchRequests = requests;
                foreach (var reqItem in batchRequests)
                {
                    var errorEvent = new TranslationErrorEvent
                    {
                        RequestId = reqItem.RequestId.ToString(),
                        SourceText = reqItem.SourceText,
                        SourceLanguage = reqItem.SourceLanguage.Code,
                        TargetLanguage = reqItem.TargetLanguage.Code,
                        ErrorMessage = ex.Message,
                        ErrorType = TranslationErrorType.Exception,
                        ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                        // Timestampは読み取り専用プロパティのためコメントアウト
                        // Timestamp = DateTimeOffset.UtcNow
                    };
                    
                    await _eventAggregator.PublishAsync(errorEvent).ConfigureAwait(false);

                    // エラーレスポンスの作成
                    var error = new TranslationError
                    {
                        ErrorCode = "BATCH_PIPELINE_ERROR",
                        Message = ex.Message,
                        ErrorType = TranslationErrorType.Exception
                    };
                    
                    var errorResponse = TranslationResponse.CreateError(
                        reqItem,
                        error,
                        "Error"
                    );
                    
                    errorResponse.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                    errorResponse.Metadata["FromCache"] = false;
                    errorResponses.Add(errorResponse);
                }

                return errorResponses.AsReadOnly();
            }
        }
#pragma warning restore CA2016
    }
#pragma warning restore CA2016 // CancellationToken警告の拘束を解除
}