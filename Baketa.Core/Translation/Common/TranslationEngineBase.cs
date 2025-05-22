using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Exceptions;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

using TransModels = Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Common;

    /// <summary>
    /// 翻訳エンジンの基本実装を提供する抽象クラス
    /// </summary>
    public abstract class TranslationEngineBase : ITranslationEngine, ITranslationEngineInternal
    {
        private readonly SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1);
        private bool _isInitialized;
        private bool _isDisposed;
        /// <summary>
        /// ロガーインスタンス
        /// </summary>
        protected ILogger? Logger { get; }

        /// <summary>
        /// 翻訳エンジンの名称
        /// </summary>
        public abstract string Name { get; }
        
        /// <summary>
        /// 翻訳エンジンの説明
        /// </summary>
        public abstract string Description { get; }
        
        /// <summary>
        /// ネットワーク接続が必要かどうか
        /// </summary>
        public abstract bool RequiresNetwork { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="logger">ロガー</param>
        protected TranslationEngineBase(ILogger? logger = null)
        {
            Logger = logger;
        }
        
        /// <summary>
        /// テキストを翻訳します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンス</returns>
        Task<TranslationResponse> ITranslationEngine.TranslateAsync(
            TranslationRequest request, 
            CancellationToken cancellationToken)
        {
            return TranslateAsync(request, cancellationToken);
        }

        /// <summary>
        /// テキストを翻訳します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンス</returns>
        public async Task<TranslationResponse> TranslateAsync(
            TranslationRequest request, 
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
                
            // エンジンの初期化確認
            await EnsureInitializedAsync().ConfigureAwait(false);
            
            // ネットワーク接続の確認
            if (RequiresNetwork)
            {
                bool isConnected = await CheckNetworkConnectivityAsync().ConfigureAwait(false);
                if (!isConnected)
                {
                    return TranslationResponse.CreateError(
                        request,
                        new TranslationError { ErrorCode = TranslationError.NetworkError, Message = "ネットワーク接続がありません", IsRetryable = true },
                        Name);
                }
            }
            
            // 言語ペアのサポート確認
            var languagePair = request.LanguagePair;
            bool isSupported = await SupportsLanguagePairAsync(languagePair).ConfigureAwait(false);
            if (!isSupported)
            {
                return TranslationResponse.CreateError(
                    request,
                    new TranslationError { ErrorCode = TranslationError.UnsupportedLanguagePair, Message = $"言語ペア {languagePair} はサポートされていません", IsRetryable = false },
                    Name);
            }
            
            // 処理時間の計測開始
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                // 内部翻訳メソッドの呼び出し
                var response = await TranslateInternalAsync(request, cancellationToken).ConfigureAwait(false);
                
                // 処理時間の記録
                stopwatch.Stop();
                response.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
                
                return response;
            }
            catch (OperationCanceledException)
            {
                // キャンセル処理
                stopwatch.Stop();
                Logger?.LogWarning("翻訳処理がキャンセルされました: {EngineName}, RequestId: {RequestId}", 
                    Name, request.RequestId);
                
                return TranslationResponse.CreateError(
                    request,
                    new TranslationError { ErrorCode = "RequestCancelled", Message = "翻訳リクエストがキャンセルされました", IsRetryable = true },
                    Name);
            }
            catch (TranslationBaseException ex)
            {
                // 翻訳関連の例外
                stopwatch.Stop();
                Logger?.LogError(ex, "翻訳処理中に例外が発生しました: {EngineName}, RequestId: {RequestId}, ErrorCode: {ErrorCode}", 
                    Name, request.RequestId, ex.ErrorCode);
                
                return TranslationResponse.CreateError(
                    request,
                    new TranslationError { ErrorCode = ex.ErrorCode, Message = ex.Message, IsRetryable = ex.IsRetryable },
                    Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // その他の例外
                stopwatch.Stop();
                Logger?.LogError(ex, "翻訳処理中に予期しない例外が発生しました: {EngineName}, RequestId: {RequestId}", 
                    Name, request.RequestId);
                
                return TranslationResponse.CreateError(
                    request,
                    new TranslationError { ErrorCode = TranslationError.InternalError, Message = $"内部エラー: {ex.Message}", IsRetryable = false },
                    Name);
            }
        }
        
        /// <summary>
        /// 複数のテキストを一括翻訳します
        /// </summary>
        /// <param name="requests">翻訳リクエストのリスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンスのリスト</returns>
        public virtual async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
            IReadOnlyList<TranslationRequest> requests, 
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requests);
                
            if (requests.Count == 0)
                return [];
                
            // バッチ処理をサポートするカスタム実装がある場合は、サブクラスで上書きしてください
            // このデフォルト実装は個別のリクエストを順次処理します
            
            var results = new List<TranslationResponse>();
            
            foreach (var request in requests)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                    
                var response = await TranslateAsync(request, cancellationToken).ConfigureAwait(false);
                results.Add(response);
            }
            
            return results;
        }
        
        /// <summary>
        /// サポートしている言語ペアを取得します
        /// </summary>
        /// <returns>サポートされている言語ペアのコレクション</returns>
        Task<IReadOnlyCollection<LanguagePair>> ITranslationEngine.GetSupportedLanguagePairsAsync()
        {
            return GetSupportedLanguagePairsAsync();
        }

        /// <summary>
        /// サポートしている言語ペアを取得します
        /// </summary>
        /// <returns>サポートされている言語ペアのコレクション</returns>
        public async Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            return await GetSupportedLanguagePairsInternalAsync().ConfigureAwait(false);
        }
        
        /// <summary>
        /// 指定した言語ペアがサポートされているか確認します
        /// </summary>
        /// <param name="languagePair">言語ペア</param>
        /// <returns>サポートされている場合はtrue</returns>
        public virtual async Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
        {
            ArgumentNullException.ThrowIfNull(languagePair);
                
            var supportedPairs = await GetSupportedLanguagePairsAsync().ConfigureAwait(false);
            return supportedPairs.Any(pair => 
                pair.SourceLanguage.Code.Equals(languagePair.SourceLanguage.Code, StringComparison.OrdinalIgnoreCase) && 
                pair.TargetLanguage.Code.Equals(languagePair.TargetLanguage.Code, StringComparison.OrdinalIgnoreCase));
        }
        
        /// <summary>
        /// 翻訳エンジンが準備完了しているか確認します
        /// </summary>
        /// <returns>準備完了している場合はtrue</returns>
        public virtual async Task<bool> IsReadyAsync()
        {
            if (!_isInitialized)
                return false;
                
            if (RequiresNetwork)
            {
                return await CheckNetworkConnectivityAsync().ConfigureAwait(false);
            }
            
            return true;
        }
        
        /// <summary>
        /// 翻訳エンジンを初期化します
        /// </summary>
        /// <returns>初期化に成功した場合はtrue</returns>
        public async Task<bool> InitializeAsync()
        {
            // 複数回の同時初期化を防ぐためのロック
            await _initializationLock.WaitAsync().ConfigureAwait(false);
            
            try
            {
                if (_isInitialized)
                    return true;
                    
                Logger?.LogInformation("翻訳エンジンの初期化を開始します: {EngineName}", Name);
                
                bool success = await InitializeInternalAsync().ConfigureAwait(false);
                
                if (success)
                {
                    _isInitialized = true;
                    Logger?.LogInformation("翻訳エンジンの初期化が完了しました: {EngineName}", Name);
                }
                else
                {
                    Logger?.LogWarning("翻訳エンジンの初期化に失敗しました: {EngineName}", Name);
                }
                
                return success;
            }
            catch (TranslationBaseException ex)
            {
                Logger?.LogError(ex, "翻訳エンジンの初期化中に翻訳例外が発生しました: {EngineName}, ErrorCode: {ErrorCode}", 
                    Name, ex.ErrorCode);
                return false;
            }
            catch (OperationCanceledException ex)
            {
                Logger?.LogWarning(ex, "翻訳エンジンの初期化がキャンセルされました: {EngineName}", Name);
                return false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger?.LogError(ex, "翻訳エンジンの初期化中に予期しない例外が発生しました: {EngineName}", Name);
                return false;
            }
            finally
            {
                _initializationLock.Release();
            }
        }
        
        /// <summary>
        /// 初期化が完了していることを確認します
        /// </summary>
        protected async Task EnsureInitializedAsync()
        {
            if (!_isInitialized)
            {
                bool success = await InitializeAsync().ConfigureAwait(false);
                if (!success)
                {
                    throw new TranslationEngineException(
                        $"翻訳エンジンの初期化に失敗しました: {Name}",
                        "InitializationFailed",
                        isRetryable: true);
                }
            }
        }
        
        /// <summary>
        /// ネットワーク接続を確認します
        /// </summary>
        /// <returns>ネットワーク接続がある場合はtrue</returns>
        protected virtual Task<bool> CheckNetworkConnectivityAsync()
        {
            try
            {
                // 単純なネットワーク接続チェック
                return Task.FromResult(NetworkInterface.GetIsNetworkAvailable());
            }
            catch (NetworkInformationException ex)
            {
                Logger?.LogWarning(ex, "ネットワーク情報の取得に失敗しました: {EngineName}", Name);
                return Task.FromResult(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger?.LogError(ex, "ネットワーク接続確認中に予期しない例外が発生しました: {EngineName}", Name);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 内部翻訳処理を実装します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンス</returns>
        Task<TranslationResponse> ITranslationEngineInternal.TranslateInternalAsync(
            TranslationRequest request, 
            CancellationToken cancellationToken)
        {
            return TranslateInternalAsync(request, cancellationToken);
        }

        /// <summary>
        /// 内部翻訳処理を実装します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンス</returns>
        protected virtual Task<TranslationResponse> TranslateInternalAsync(
            TranslationRequest request, 
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException("子クラスで実装してください");
        }
        
        /// <summary>
        /// 内部初期化処理を実装します
        /// </summary>
        /// <returns>初期化に成功した場合はtrue</returns>
        Task<bool> ITranslationEngineInternal.InitializeInternalAsync()
        {
            return InitializeInternalAsync();
        }

        /// <summary>
        /// 内部初期化処理を実装します
        /// </summary>
        /// <returns>初期化に成功した場合はtrue</returns>
        protected virtual Task<bool> InitializeInternalAsync()
        {
            return Task.FromResult(true);
        }
        
        /// <summary>
        /// 内部でサポートされている言語ペアを取得する処理を実装します
        /// </summary>
        /// <returns>サポートされている言語ペアのコレクション</returns>
        Task<IReadOnlyCollection<LanguagePair>> ITranslationEngineInternal.GetSupportedLanguagePairsInternalAsync()
        {
            return GetSupportedLanguagePairsInternalAsync();
        }

        /// <summary>
        /// 内部でサポートされている言語ペアを取得する処理を実装します
        /// </summary>
        /// <returns>サポートされている言語ペアのコレクション</returns>
        protected virtual Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsInternalAsync()
        {
            return Task.FromResult<IReadOnlyCollection<LanguagePair>>([]);
        }

        /// <summary>
        /// テキストの言語を自動検出します
        /// </summary>
        /// <param name="text">検出対象テキスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>検出された言語と信頼度</returns>
        public virtual Task<TransModels.LanguageDetectionResult> DetectLanguageAsync(
            string text, 
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(text);
            
            // ベース実装では単純に自動検出言語を返す
            // 実際の検出機能はサブクラスで実装する
            Logger?.LogWarning("言語検出は実装されていません: {EngineName}", Name);
            
            // 単純な生成方法で返却
            return Task.FromResult(new TransModels.LanguageDetectionResult
            {
                DetectedLanguage = new TransModels.Language
                {
                    Code = "auto",
                    DisplayName = "自動検出"
                },
                Confidence = 0.0f,
                EngineName = Name
            });
        }

        /// <summary>
        /// 内部言語検出処理を実装します
        /// </summary>
        /// <param name="text">検出対象テキスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>検出された言語と信頼度</returns>
        protected virtual Task<TransModels.LanguageDetectionResult> DetectLanguageInternalAsync(
            string text, 
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException("子クラスで実装してください");
        }

        /// <summary>
        /// リソースを破棄します
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースも解放するかどうか</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _initializationLock.Dispose();
                }
                
                _isDisposed = true;
            }
        }
    }
