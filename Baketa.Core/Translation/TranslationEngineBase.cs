using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

// 名前空間エイリアスを定義して曖昧さを回避
using CoreModels = Baketa.Core.Models.Translation;
using TransModels = Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation
{
    /// <summary>
    /// 翻訳エンジンの基本機能を提供する抽象クラス
    /// </summary>
    public abstract class TranslationEngineBase : ITranslationEngine
    {
        private readonly ILogger<TranslationEngineBase> _logger;
        private bool _isInitialized;
        private SemaphoreSlim _initializationLock = new(1, 1);
        private bool _disposed;

        /// <summary>
        /// エンジン名
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// エンジンの説明
        /// </summary>
        public abstract string Description { get; }

        /// <summary>
        /// エンジンがオンライン接続を必要とするかどうか
        /// </summary>
        public abstract bool RequiresNetwork { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="logger">ロガー</param>
        protected TranslationEngineBase(ILogger<TranslationEngineBase> logger)
        {
            ArgumentNullException.ThrowIfNull(logger);
            _logger = logger;
        }

        /// <summary>
        /// サポートしている言語ペアを取得します
        /// </summary>
        /// <returns>サポートされている言語ペアのコレクション</returns>
        /// <remarks>
        /// 子クラスでこのメソッドを実装する必要があります
        /// </remarks>
        public abstract Task<IReadOnlyCollection<CoreModels.LanguagePair>> GetSupportedLanguagePairsAsync();

        /// <summary>
        /// サポートしている言語ペアを取得します（インターフェース実装）
        /// </summary>
        /// <returns>サポートされている言語ペアのコレクション</returns>
        async Task<IReadOnlyCollection<TransModels.LanguagePair>> ITranslationEngine.GetSupportedLanguagePairsAsync()
        {
            var corePairs = await GetSupportedLanguagePairsAsync().ConfigureAwait(false);
            
            // CoreModelsからTransModelsに変換
            return corePairs.Select(p => new TransModels.LanguagePair
            {
                SourceLanguage = new TransModels.Language
                {
                    Code = p.SourceLanguage.Code,
                    DisplayName = p.SourceLanguage.Name
                },
                TargetLanguage = new TransModels.Language
                {
                    Code = p.TargetLanguage.Code,
                    DisplayName = p.TargetLanguage.Name
                }
            }).ToList();
        }

        /// <summary>
        /// 指定された言語ペアをサポートしているかどうかを確認します
        /// </summary>
        /// <param name="languagePair">確認する言語ペア</param>
        /// <returns>サポートしていればtrue</returns>
        public virtual async Task<bool> SupportsLanguagePairAsync(CoreModels.LanguagePair languagePair)
        {
            var supportedPairs = await GetSupportedLanguagePairsAsync().ConfigureAwait(false);
            return supportedPairs.Any(pair => pair.Equals(languagePair));
        }

        /// <summary>
        /// 指定された言語ペアをサポートしているかどうかを確認します（インターフェース実装）
        /// </summary>
        /// <param name="languagePair">確認する言語ペア</param>
        /// <returns>サポートしていればtrue</returns>
        async Task<bool> ITranslationEngine.SupportsLanguagePairAsync(TransModels.LanguagePair languagePair)
        {
            // TransModelsからCoreModelsに変換
            var corePair = new CoreModels.LanguagePair
            {
                SourceLanguage = new CoreModels.Language
                {
                    Code = languagePair.SourceLanguage.Code,
                    Name = languagePair.SourceLanguage.DisplayName
                },
                TargetLanguage = new CoreModels.Language
                {
                    Code = languagePair.TargetLanguage.Code,
                    Name = languagePair.TargetLanguage.DisplayName
                }
            };
            
            return await SupportsLanguagePairAsync(corePair).ConfigureAwait(false);
        }

        /// <summary>
        /// テキストを翻訳します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンス</returns>
        public async Task<CoreModels.TranslationResponse> TranslateAsync(
            CoreModels.TranslationRequest request, 
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // エンジンの準備状態を確認
            if (!await IsReadyAsync().ConfigureAwait(false))
            {
                var initResult = await InitializeAsync().ConfigureAwait(false);
                if (!initResult)
                {
                    return CreateErrorResponse(
                        request,
                        CoreModels.TranslationError.ServiceUnavailable,
                        $"翻訳エンジン {Name} の初期化に失敗しました。");
                }
            }

            // 言語ペアのサポートを確認
            var languagePair = new CoreModels.LanguagePair 
            { 
                SourceLanguage = request.SourceLanguage, 
                TargetLanguage = request.TargetLanguage 
            };
            
            var isSupported = await SupportsLanguagePairAsync(languagePair).ConfigureAwait(false);
            if (!isSupported)
            {
                return CreateErrorResponse(
                    request,
                    CoreModels.TranslationError.UnsupportedLanguagePair,
                    $"言語ペア {languagePair} はサポートされていません。");
            }

            // オンライン接続が必要な場合は、ネットワーク接続を確認
            if (RequiresNetwork)
            {
                var isNetworkAvailable = await CheckNetworkConnectivityAsync().ConfigureAwait(false);
                if (!isNetworkAvailable)
                {
                    return CreateErrorResponse(
                        request,
                        CoreModels.TranslationError.NetworkError,
                        "ネットワーク接続が利用できません。");
                }
            }

            try
            {
                // 翻訳の実行と時間測定
                var (result, elapsedMs) = await MeasureExecutionTimeAsync(() =>
                    TranslateInternalAsync(request, cancellationToken)).ConfigureAwait(false);

                // 結果にエンジン名と処理時間を設定
                result.EngineName = Name;
                result.ProcessingTimeMs = elapsedMs;

                _logger.LogDebug(
                    "翻訳完了: リクエストID={RequestId}, 処理時間={ElapsedMs}ms, 成功={IsSuccess}",
                    request.RequestId, elapsedMs, result.IsSuccess);

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("翻訳がキャンセルされました: リクエストID={RequestId}", request.RequestId);
                return CreateErrorResponse(
                    request,
                    CoreModels.TranslationError.TimeoutError,
                    "翻訳処理がキャンセルされました。");
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "翻訳タイムアウト: リクエストID={RequestId}", request.RequestId);
                return CreateErrorResponseFromException(
                    request,
                    CoreModels.TranslationError.TimeoutError,
                    "翻訳処理がタイムアウトしました。",
                    ex);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "翻訳の無効な操作: リクエストID={RequestId}", request.RequestId);
                return CreateErrorResponseFromException(
                    request,
                    CoreModels.TranslationError.InvalidRequest,
                    "翻訳処理中に無効な操作が発生しました。",
                    ex);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "翻訳の引数エラー: リクエストID={RequestId}", request.RequestId);
                return CreateErrorResponseFromException(
                    request,
                    CoreModels.TranslationError.InvalidRequest,
                    "翻訳処理に無効な引数が提供されました。",
                    ex);
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                _logger.LogError(ex, "翻訳中のHTTPリクエストエラー: リクエストID={RequestId}", request.RequestId);
                return CreateErrorResponseFromException(
                    request,
                    CoreModels.TranslationError.NetworkError,
                    "翻訳サービスとの通信中にエラーが発生しました。",
                    ex);
            }
            catch (System.IO.IOException ex)
            {
                _logger.LogError(ex, "翻訳中のI/Oエラー: リクエストID={RequestId}", request.RequestId);
                return CreateErrorResponseFromException(
                    request,
                    CoreModels.TranslationError.InternalError,
                    "翻訳処理中にI/Oエラーが発生しました。",
                    ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && 
                                  ex is not TimeoutException && 
                                  ex is not InvalidOperationException && 
                                  ex is not ArgumentException && 
                                  ex is not System.Net.Http.HttpRequestException && 
                                  ex is not System.IO.IOException && 
                                  ex is not ObjectDisposedException && 
                                  ex is not NotImplementedException && 
                                  ex is not NotSupportedException)
            {
                _logger.LogError(ex, "翻訳中の予期しないエラー: リクエストID={RequestId}", request.RequestId);
                return CreateErrorResponseFromException(
                    request,
                    CoreModels.TranslationError.InternalError,
                    "翻訳処理中に予期しないエラーが発生しました。",
                    ex);
            }
        }

        /// <summary>
        /// テキストを翻訳します（インターフェース実装）
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンス</returns>
        async Task<TransModels.TranslationResponse> ITranslationEngine.TranslateAsync(
            TransModels.TranslationRequest request, 
            CancellationToken cancellationToken)
        {
            // TransModelsからCoreModelsに変換
            var coreRequest = new CoreModels.TranslationRequest
            {
                SourceText = request.SourceText,
                SourceLanguage = new CoreModels.Language
                {
                    Code = request.SourceLanguage.Code,
                    Name = request.SourceLanguage.DisplayName
                },
                TargetLanguage = new CoreModels.Language
                {
                    Code = request.TargetLanguage.Code,
                    Name = request.TargetLanguage.DisplayName
                },
                Context = request.Context?.DialogueId
            };
            
            // 翻訳を実行
            var coreResponse = await TranslateAsync(coreRequest, cancellationToken).ConfigureAwait(false);
            
            // CoreModelsからTransModelsに変換
            return new TransModels.TranslationResponse
            {
                RequestId = coreResponse.RequestId,
                SourceText = coreResponse.SourceText,
                TranslatedText = coreResponse.TranslatedText,
                SourceLanguage = new TransModels.Language
                {
                    Code = coreResponse.SourceLanguage.Code,
                    DisplayName = coreResponse.SourceLanguage.Name
                },
                TargetLanguage = new TransModels.Language
                {
                    Code = coreResponse.TargetLanguage.Code,
                    DisplayName = coreResponse.TargetLanguage.Name
                },
                EngineName = coreResponse.EngineName,
                ProcessingTimeMs = coreResponse.ProcessingTimeMs,
                IsSuccess = coreResponse.IsSuccess,
                Error = coreResponse.Error != null ? new TransModels.TranslationError
                {
                    ErrorCode = coreResponse.Error.ErrorCode,
                    Message = coreResponse.Error.Message,
                    Details = coreResponse.Error.Details
                } : null
            };
        }

        /// <summary>
        /// エンジン固有の翻訳処理を実装します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンス</returns>
        protected abstract Task<CoreModels.TranslationResponse> TranslateInternalAsync(
            CoreModels.TranslationRequest request,
            CancellationToken cancellationToken);

        /// <summary>
        /// 複数のテキストをバッチ翻訳します
        /// </summary>
        /// <param name="requests">翻訳リクエストのコレクション</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンスのコレクション</returns>
        public virtual async Task<IReadOnlyList<CoreModels.TranslationResponse>> TranslateBatchAsync(
            IReadOnlyList<CoreModels.TranslationRequest> requests, 
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requests);
            
            if (requests.Count == 0)
            {
                throw new ArgumentException("リクエストが空です。", nameof(requests));
            }

            // 各リクエストを並行して処理
            var tasks = requests.Select(request =>
                TranslateAsync(request, cancellationToken));

            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// 複数のテキストをバッチ翻訳します（インターフェース実装）
        /// </summary>
        /// <param name="requests">翻訳リクエストのコレクション</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンスのコレクション</returns>
        async Task<IReadOnlyList<TransModels.TranslationResponse>> ITranslationEngine.TranslateBatchAsync(
            IReadOnlyList<TransModels.TranslationRequest> requests, 
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(requests);
            
            if (requests.Count == 0)
            {
                return Array.Empty<TransModels.TranslationResponse>();
            }

            // TransModelsからCoreModelsに変換
            var coreRequests = new List<CoreModels.TranslationRequest>();
            foreach (var request in requests)
            {
                coreRequests.Add(new CoreModels.TranslationRequest
                {
                    SourceText = request.SourceText,
                    SourceLanguage = new CoreModels.Language
                    {
                        Code = request.SourceLanguage.Code,
                        Name = request.SourceLanguage.DisplayName
                    },
                    TargetLanguage = new CoreModels.Language
                    {
                        Code = request.TargetLanguage.Code,
                        Name = request.TargetLanguage.DisplayName
                    },
                    Context = request.Context?.DialogueId
                });
            }
            
            // バッチ翻訳を実行
            var coreResponses = await TranslateBatchAsync(coreRequests, cancellationToken).ConfigureAwait(false);
            
            // CoreModelsからTransModelsに変換
            return coreResponses.Select(coreResponse => new TransModels.TranslationResponse
            {
                RequestId = coreResponse.RequestId,
                SourceText = coreResponse.SourceText,
                TranslatedText = coreResponse.TranslatedText,
                SourceLanguage = new TransModels.Language
                {
                    Code = coreResponse.SourceLanguage.Code,
                    DisplayName = coreResponse.SourceLanguage.Name
                },
                TargetLanguage = new TransModels.Language
                {
                    Code = coreResponse.TargetLanguage.Code,
                    DisplayName = coreResponse.TargetLanguage.Name
                },
                EngineName = coreResponse.EngineName,
                ProcessingTimeMs = coreResponse.ProcessingTimeMs,
                IsSuccess = coreResponse.IsSuccess,
                Error = coreResponse.Error != null ? new TransModels.TranslationError
                {
                    ErrorCode = coreResponse.Error.ErrorCode,
                    Message = coreResponse.Error.Message,
                    Details = coreResponse.Error.Details
                } : null
            }).ToList();
        }

        /// <summary>
        /// エンジンの準備状態を確認します
        /// </summary>
        /// <returns>準備ができていればtrue</returns>
        public virtual Task<bool> IsReadyAsync()
        {
            return Task.FromResult(_isInitialized);
        }

        /// <summary>
        /// エンジンを初期化します
        /// </summary>
        /// <returns>初期化が成功すればtrue</returns>
        public virtual async Task<bool> InitializeAsync()
        {
            // 既に初期化済みなら何もしない
            if (_isInitialized)
            {
                return true;
            }

            // 同時初期化を防止するためのロック
            await _initializationLock.WaitAsync().ConfigureAwait(false);

            try
            {
                // ロック取得後に再チェック
                if (_isInitialized)
                {
                    return true;
                }

                _logger.LogInformation("翻訳エンジン {EngineName} を初期化しています...", Name);

                if (RequiresNetwork)
                {
                    var isNetworkAvailable = await CheckNetworkConnectivityAsync().ConfigureAwait(false);
                    if (!isNetworkAvailable)
                    {
                        _logger.LogWarning("ネットワーク接続が利用できません。エンジン初期化スキップ: {EngineName}", Name);
                        return false;
                    }
                }

                var result = await InitializeInternalAsync().ConfigureAwait(false);
                _isInitialized = result;

                if (result)
                {
                    _logger.LogInformation("翻訳エンジン {EngineName} の初期化が完了しました", Name);
                }
                else
                {
                    _logger.LogError("翻訳エンジン {EngineName} の初期化に失敗しました", Name);
                }

                return result;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "翻訳エンジン {EngineName} の初期化がキャンセルされました", Name);
                _isInitialized = false;
                return false;
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "翻訳エンジン {EngineName} の初期化がタイムアウトしました", Name);
                _isInitialized = false;
                return false;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "翻訳エンジン {EngineName} の初期化中に無効な操作が発生しました", Name);
                _isInitialized = false;
                return false;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "翻訳エンジン {EngineName} の初期化に無効な引数が提供されました", Name);
                _isInitialized = false;
                return false;
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                _logger.LogError(ex, "翻訳エンジン {EngineName} の初期化中にHTTPリクエストエラーが発生しました", Name);
                _isInitialized = false;
                return false;
            }
            catch (System.IO.IOException ex)
            {
                _logger.LogError(ex, "翻訳エンジン {EngineName} の初期化中にI/Oエラーが発生しました", Name);
                _isInitialized = false;
                return false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && 
                                      ex is not TimeoutException && 
                                      ex is not InvalidOperationException && 
                                      ex is not ArgumentException && 
                                      ex is not System.Net.Http.HttpRequestException && 
                                      ex is not System.IO.IOException && 
                                      ex is not ObjectDisposedException && 
                                      ex is not NotImplementedException && 
                                      ex is not NotSupportedException)
            {
                _logger.LogError(ex, "翻訳エンジン {EngineName} の初期化中に予期しないエラーが発生しました", Name);
                _isInitialized = false;
                return false;
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        /// <summary>
        /// エンジン固有の初期化処理を実装します
        /// </summary>
        /// <returns>初期化が成功すればtrue</returns>
        protected abstract Task<bool> InitializeInternalAsync();

        /// <summary>
        /// ネットワーク接続を確認します
        /// </summary>
        /// <returns>接続可能ならtrue</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
            Justification = "ネットワーク診断目的のメソッドであり、あらゆる例外をキャッチして接続不可と判断する必要があるため")]
        protected virtual Task<bool> CheckNetworkConnectivityAsync()
        {
            // 基本実装 - 継承先で必要に応じてオーバーライド
            try
            {
                using var ping = new Ping();
                var reply = ping.Send("8.8.8.8", 1000);
                return Task.FromResult(reply?.Status == IPStatus.Success);
            }
            catch (Exception ex) when (ex is PingException || ex is InvalidOperationException)
            {
                _logger.LogDebug(ex, "ネットワーク接続確認中にエラーが発生しました");
                return Task.FromResult(false);
            }
            catch (Exception ex) when (ex is SecurityException ||
                                     ex is SocketException ||
                                     ex is NetworkInformationException)
            {
                _logger.LogDebug(ex, "ネットワーク接続確認中にネットワーク関連のエラーが発生しました");
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                // 全ての例外をログ記録し、接続不可として扱う
                // このメソッドは診断目的のためであり、例外が発生しても致命的ではないため
                // 汎用的な例外キャッチはここでは適切
                _logger.LogWarning(ex, "ネットワーク接続確認中に予期しないエラーが発生しました: {ExceptionType}", ex.GetType().Name);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 翻訳処理時間を計測します
        /// </summary>
        /// <param name="action">計測対象の処理</param>
        /// <returns>処理時間（ミリ秒）</returns>
        protected static async Task<long> MeasureExecutionTimeAsync(Func<Task> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await action().ConfigureAwait(false);
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        /// <summary>
        /// 翻訳処理時間を計測します
        /// </summary>
        /// <typeparam name="T">戻り値の型</typeparam>
        /// <param name="func">計測対象の処理</param>
        /// <returns>(戻り値, 処理時間（ミリ秒）)のタプル</returns>
        protected static async Task<(T Result, long ElapsedMs)> MeasureExecutionTimeAsync<T>(Func<Task<T>> func)
        {
            ArgumentNullException.ThrowIfNull(func);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await func().ConfigureAwait(false);
            sw.Stop();
            return (result, sw.ElapsedMilliseconds);
        }

        /// <summary>
        /// 標準的なエラーレスポンスを作成します
        /// </summary>
        /// <param name="request">元のリクエスト</param>
        /// <param name="errorCode">エラーコード</param>
        /// <param name="message">エラーメッセージ</param>
        /// <param name="details">詳細（オプション）</param>
        /// <returns>エラーを含む翻訳レスポンス</returns>
        protected CoreModels.TranslationResponse CreateErrorResponse(
            CoreModels.TranslationRequest request, 
            string errorCode, 
            string message,
            string? details = null)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(errorCode);
            ArgumentNullException.ThrowIfNull(message);
            
            _logger.LogError(
                "翻訳エラー: {ErrorCode}, {Message}, リクエストID={RequestId}",
                errorCode, message, request.RequestId);

            return new CoreModels.TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = Name,
                IsSuccess = false,
                Error = new CoreModels.TranslationError
                {
                    ErrorCode = errorCode,
                    Message = message,
                    Details = details
                }
            };
        }

        /// <summary>
        /// 例外から標準的なエラーレスポンスを作成します
        /// </summary>
        /// <param name="request">元のリクエスト</param>
        /// <param name="errorCode">エラーコード</param>
        /// <param name="message">エラーメッセージ</param>
        /// <param name="exception">例外</param>
        /// <returns>エラーを含む翻訳レスポンス</returns>
        protected CoreModels.TranslationResponse CreateErrorResponseFromException(
            CoreModels.TranslationRequest request, 
            string errorCode, 
            string message, 
            Exception exception)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(errorCode);
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(exception);
            
            _logger.LogError(exception,
                "翻訳エラー: {ErrorCode}, {Message}, リクエストID={RequestId}",
                errorCode, message, request.RequestId);

            return new CoreModels.TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = Name,
                IsSuccess = false,
                Error = new CoreModels.TranslationError
                {
                    ErrorCode = errorCode,
                    Message = message,
                    Details = exception.ToString(),
                    Exception = exception
                }
            };
        }

        /// <summary>
        /// 言語検出機能（インターフェース実装）
        /// </summary>
        /// <param name="text">検出対象テキスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>検出結果</returns>
        public virtual Task<TransModels.LanguageDetectionResult> DetectLanguageAsync(
            string text, 
            CancellationToken cancellationToken = default)
        {
            // 基本実装（派生クラスでオーバーライド可能）
            var result = new TransModels.LanguageDetectionResult
            {
                DetectedLanguage = new TransModels.Language
                {
                    Code = "auto",
                    DisplayName = "自動検出"
                },
                Confidence = 0.5f,
                EngineName = Name
            };
            
            return Task.FromResult(result);
        }

        /// <summary>
        /// リソースの解放
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// リソースの解放（派生クラスでオーバーライド可能）
        /// </summary>
        /// <param name="disposing">マネージドリソースも解放する場合はtrue</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            
            if (disposing)
            {
                // マネージドリソースの解放
                _initializationLock?.Dispose();
                _initializationLock = null!;
            }
            
            // アンマネージドリソースの解放（必要な場合）
            
            _disposed = true;
        }
    }
}