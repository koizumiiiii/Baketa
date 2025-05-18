using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Common;
using Baketa.Core.Translation.Exceptions;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

using TransModels = Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Common;

    /// <summary>
    /// WebAPI翻訳エンジンの基本実装を提供する抽象クラス
    /// </summary>
    public abstract class WebApiTranslationEngineBase : TranslationEngineBase, IWebApiTranslationEngine
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly WebApiTranslationOptions _options;
        
        /// <summary>
        /// APIのベースURL
        /// </summary>
        public abstract Uri ApiBaseUrl { get; }
        
        /// <summary>
        /// APIキーが設定されているかどうか
        /// </summary>
        public virtual bool HasApiKey => !string.IsNullOrEmpty(_apiKey);
        
        /// <summary>
        /// APIのリクエスト制限（リクエスト/分）
        /// </summary>
        public abstract int RateLimit { get; }
        
        /// <summary>
        /// APIの現在のクォータ残量（リクエスト数）
        /// </summary>
        public virtual int? QuotaRemaining { get; protected set; }
        
        /// <summary>
        /// APIのクォータリセット時刻
        /// </summary>
        public virtual DateTime? QuotaResetTime { get; protected set; }
        
        /// <summary>
        /// 自動検出言語をサポートしているかどうか
        /// </summary>
        public abstract bool SupportsAutoDetection { get; }
        
        /// <summary>
        /// ネットワーク接続が必要かどうか
        /// </summary>
        public override bool RequiresNetwork => true;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="httpClient">HTTPクライアント</param>
        /// <param name="apiKey">APIキー</param>
        /// <param name="options">WebAPI翻訳オプション</param>
        /// <param name="logger">ロガー</param>
        protected WebApiTranslationEngineBase(
            HttpClient httpClient,
            string apiKey,
            WebApiTranslationOptions options,
            ILogger? logger = null)
            : base(logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TranslationEngineBase>.Instance)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentNullException.ThrowIfNull(options);

            _httpClient = httpClient;
            _apiKey = apiKey ?? string.Empty;
            _options = options;
            
            // HTTPクライアントの設定
            // 注意: この時点では派生クラスのメソッドは使用不可
            ConfigureHttpClientInternal();
        }

        /// <summary>
        /// HTTPクライアントを設定します（内部用）
        /// </summary>
        private void ConfigureHttpClientInternal()
        {
            // タイムアウトの設定
            _httpClient.Timeout = _options.RequestTimeoutSeconds();
            
            // UserAgentの設定
            string userAgent = _options.UserAgent();
            if (!string.IsNullOrEmpty(userAgent) && !_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
            }
        }
        
        /// <summary>
        /// インスタンス初期化後に必要な追加HTTPクライアント設定を行います
        /// </summary>
        protected virtual void ConfigureHttpClient()
        {
            // 派生クラスで必要に応じてオーバーライドして実装
        }
        
        /// <summary>
        /// APIのステータスを確認します
        /// </summary>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>APIのステータス情報</returns>
        public abstract Task<ApiStatusInfo> CheckApiStatusAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// テキストの言語を自動検出します
        /// </summary>
        /// <param name="text">検出対象テキスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>検出された言語と信頼度</returns>
        public override Task<TransModels.LanguageDetectionResult> DetectLanguageAsync(string text, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(text);
            
            // Web API翻訳エンジンでの言語検出はサブクラスで実装する必要があります
            // デフォルト実装はディスパッチするためのものです
            Logger?.LogWarning("WebAPI翻訳エンジンでの言語検出はサブクラスで実装されていません: {EngineName}", Name);
            
            return base.DetectLanguageAsync(text, cancellationToken);
        }

        /// <summary>
        /// 初期化時にAPIの接続性を確認します
        /// </summary>
        /// <returns>初期化に成功した場合はtrue</returns>
        protected override async Task<bool> InitializeInternalAsync()
        {
            // 派生クラスのメソッドを使用できるよう、ここでHTTPクライアントの追加設定を実行
            ConfigureHttpClient();
            
            if (!HasApiKey)
            {
                Logger?.LogWarning("APIキーが設定されていません: {EngineName}", Name);
                return false;
            }
            
            try
            {
                // APIのステータスを確認
                var status = await CheckApiStatusAsync().ConfigureAwait(false);
                
                if (status.IsAvailable)
                {
                    // クォータ情報の保存
                    QuotaRemaining = status.QuotaRemaining;
                    QuotaResetTime = status.QuotaResetTime;
                    
                    Logger?.LogInformation("APIステータスの確認が完了しました: {EngineName}, Status: {Status}", 
                        Name, status.StatusMessage);
                    
                    return true;
                }
                else
                {
                    Logger?.LogWarning("APIが利用できません: {EngineName}, Status: {Status}", 
                        Name, status.StatusMessage);
                    
                    return false;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger?.LogError(ex, "APIステータスの確認中に例外が発生しました: {EngineName}", Name);
                return false;
            }
        }
        
        /// <summary>
        /// より詳細なネットワーク接続確認を実装します
        /// </summary>
        /// <returns>ネットワーク接続がある場合はtrue</returns>
        protected override async Task<bool> CheckNetworkConnectivityAsync()
        {
            // 基本的なネットワーク接続チェック
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return false;
            }
            
            try
            {
                // APIホストへの接続性をチェック
                Uri uri = ApiBaseUrl;
                
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                
                using var request = new HttpRequestMessage(HttpMethod.Head, uri.GetLeftPart(UriPartial.Authority));
                using var response = await client.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
                
                // 応答があればネットワーク接続OK
                return response.IsSuccessStatusCode || 
                       (int)response.StatusCode >= 300 && (int)response.StatusCode < 500;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger?.LogDebug(ex, "ネットワーク接続確認中にエラーが発生しました: {EngineName}", Name);
                return false;
            }
        }
        
        /// <summary>
        /// リソース解放時の処理
        /// </summary>
        /// <param name="disposing">マネージドリソースも解放するかどうか</param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            // HttpClientはDIコンテナが管理するので、ここでは解放しない
        }
        
        /// <summary>
        /// レスポンスヘッダーからクォータ情報を更新します
        /// </summary>
        /// <param name="headers">レスポンスヘッダー</param>
        protected virtual void UpdateQuotaInfo(HttpResponseHeaders headers)
        {
            ArgumentNullException.ThrowIfNull(headers);
            // サブクラスで実装する場合はオーバーライドしてください
        }
        
        /// <summary>
        /// リクエストを再試行するメソッド
        /// </summary>
        /// <typeparam name="T">レスポンスの型</typeparam>
        /// <param name="requestFunc">リクエスト関数</param>
        /// <param name="maxRetries">最大再試行回数</param>
        /// <param name="retryInterval">再試行間隔（ミリ秒）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>レスポンス</returns>
        protected async Task<T> RetryRequestAsync<T>(
            Func<CancellationToken, Task<T>> requestFunc,
            int maxRetries = 3,
            int retryInterval = 1000,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requestFunc);
            
            int attempts = 0;
            Exception? lastException = null;
            
            while (attempts <= maxRetries)
            {
                try
                {
                    if (attempts > 0)
                    {
                        Logger?.LogDebug("リクエストを再試行します: {EngineName}, 試行回数: {Attempt}/{MaxRetries}", 
                            Name, attempts, maxRetries);
                        
                        // 再試行前に待機
                        await Task.Delay(retryInterval * attempts, cancellationToken).ConfigureAwait(false);
                    }
                    
                    return await requestFunc(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // キャンセルされた場合は再試行しない
                    throw;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastException = ex;
                    
                    // 再試行不可能なエラーの場合はすぐに失敗
                    if (ex is TranslationBaseException translationEx && !translationEx.IsRetryable)
                    {
                        Logger?.LogWarning(ex, "再試行不可能なエラーが発生しました: {EngineName}, ErrorCode: {ErrorCode}", 
                            Name, translationEx.ErrorCode);
                        throw;
                    }
                    
                    Logger?.LogWarning(ex, "リクエスト中にエラーが発生しました: {EngineName}, 試行回数: {Attempt}/{MaxRetries}", 
                        Name, attempts, maxRetries);
                }
                
                attempts++;
            }
            
            // すべての試行が失敗した場合
            if (lastException != null)
            {
                Logger?.LogError(lastException, "すべての再試行が失敗しました: {EngineName}, 試行回数: {MaxRetries}", 
                    Name, maxRetries);
                
                if (lastException is TranslationBaseException)
                {
                    throw lastException;
                }
                
                throw new TranslationNetworkException(
                    $"すべての再試行が失敗しました: {lastException.Message}",
                    TranslationError.NetworkError,
                    isRetryable: false,
                    innerException: lastException);
            }
            
            // この行には到達しないはずだが、コンパイラのために必要
            throw new InvalidOperationException("予期しないエラーが発生しました");
        }
    }
