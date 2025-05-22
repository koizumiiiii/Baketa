using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Exceptions;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Translation.Cloud;

/// <summary>
/// Google Gemini API翻訳エンジンの実装
/// </summary>
public partial class GeminiTranslationEngine : TranslationEngineBase, ICloudTranslationEngine
{
    // APIエンドポイント定数
    private const string DEFAULT_API_ENDPOINT = "https://generativelanguage.googleapis.com/v1/models/";
    private const string DEFAULT_MODEL = "gemini-1.5-pro";
    
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiTranslationEngine> _logger;
    private readonly string _apiKey;
    private readonly GeminiEngineOptions _options;
    
    /// <inheritdoc/>
    public override string Name => "Google Gemini";
    
    /// <inheritdoc/>
    public override string Description => "Google Gemini AIを使用した高品質翻訳エンジン";
    
    /// <inheritdoc/>
    public override bool RequiresNetwork => true;
    
    /// <inheritdoc/>
    public Uri ApiBaseUrl { get; }
    
    /// <inheritdoc/>
    public bool HasApiKey => !string.IsNullOrEmpty(_apiKey);
    
    /// <inheritdoc/>
    public CloudProviderType ProviderType => CloudProviderType.Gemini;
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="httpClient">HTTPクライアント</param>
    /// <param name="options">エンジンオプション</param>
    /// <param name="logger">ロガー</param>
    public GeminiTranslationEngine(
        HttpClient httpClient,
        IOptions<GeminiEngineOptions> options,
        ILogger<GeminiTranslationEngine> logger) : base(logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _apiKey = _options.ApiKey ?? throw new ArgumentException("APIキーが設定されていません", nameof(options));
        
        // APIベースURLの設定
        var baseUrl = _options.ApiEndpoint ?? DEFAULT_API_ENDPOINT;
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += '/';
        }
        ApiBaseUrl = new Uri(baseUrl);
        
        // HTTPクライアントの設定
        _httpClient.BaseAddress = ApiBaseUrl;
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }
    
    /// <inheritdoc/>
    protected override async Task<bool> InitializeInternalAsync()
    {
        try
        {
            if (IsInitialized)
                return true;
                
            // APIの状態を確認
            var apiStatus = await CheckApiStatusAsync().ConfigureAwait(false);
            if (!apiStatus.IsAvailable)
            {
                _logger.LogError("Gemini APIが利用できません: {Message}", apiStatus.StatusMessage);
                return false;
            }
            
            IsInitialized = true;
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Gemini APIに接続できません: {StatusCode}", ex.StatusCode);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Gemini翻訳エンジンの初期化中に無効な操作が発生しました");
            return false;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "APIレスポンスの解析中にJSON処理エラーが発生しました");
            return false;
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "APIレスポンスのフォーマット解析中にエラーが発生しました");
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Gemini翻訳エンジンの初期化中にタスクがキャンセルされました");
            return false;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Gemini翻訳エンジンの初期化中に引数エラーが発生しました");
            return false;
        }
        catch (System.IO.IOException ex)
        {
            _logger.LogError(ex, "Gemini翻訳エンジンの初期化中に入出力エラーが発生しました");
            return false;
        }
        
        // すべてのcatchブロックを通過した場合 - このコードには到達しない
    }
    
    /// <inheritdoc/>
    public override async Task<bool> IsReadyAsync()
    {
        if (IsInitialized)
            return true;
            
        try
        {
            return await InitializeAsync().ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "API接続確認中にHTTPエラーが発生しました");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex, "API接続確認中に無効な操作が発生しました");
            return false;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "API接続確認中にJSON処理エラーが発生しました");
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogDebug(ex, "API接続確認がタイムアウトしました");
            return false;
        }
        
        // すべてのcatchブロックを通過した場合 - このコードには到達しない
    }
    
    /// <inheritdoc/>
    protected override async Task<TranslationResponse> TranslateInternalAsync(
        TranslationRequest request, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request, nameof(request));
            
        if (!IsInitialized && !await InitializeAsync().ConfigureAwait(false))
        {
            return CreateErrorResponse(
                request, 
                TranslationErrorType.ServiceUnavailable, 
                "翻訳エンジンが初期化されていません");
        }
        
        try
        {
            // 基本リクエストを高度なリクエストに変換
            var advancedRequest = request is AdvancedTranslationRequest ar
                ? ar
                : new AdvancedTranslationRequest
                {
                    SourceText = request.SourceText,
                    SourceLanguage = request.SourceLanguage,
                    TargetLanguage = request.TargetLanguage,
                    Context = request.Context // Contextも必ずコピー
                };
            
            // 高度な翻訳を実行
            var advancedResponse = await TranslateAdvancedAsync(advancedRequest, cancellationToken).ConfigureAwait(false);
            
            // 基本レスポンスとして返却
            return advancedResponse;
        }
        catch (TranslationException ex)
        {
            // ログ出力用にテキストを分離処理
            var sourceTextPreview = request.SourceText.Length > 50 ?
                string.Concat(request.SourceText.AsSpan(0, 50), "...") :
                request.SourceText;
                
            _logger.LogError(ex, "Gemini翻訳に失敗しました（翻訳例外）: {SourceText}", sourceTextPreview);
            
            return CreateErrorResponse(
                request, 
                ex.ErrorType, 
                $"翻訳処理に失敗しました: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            // ログ出力用にテキストを分離処理
            var sourceTextPreview = request.SourceText.Length > 50 ?
                string.Concat(request.SourceText.AsSpan(0, 50), "...") :
                request.SourceText;
                
            _logger.LogError(ex, "Gemini翻訳に失敗しました（HTTP例外）: {SourceText}", sourceTextPreview);
            
            return CreateErrorResponse(
                request, 
                TranslationErrorType.NetworkError, 
                $"通信エラーが発生しました: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            // ログ出力用にテキストを分離処理
            var sourceTextPreview = request.SourceText.Length > 50 ?
                string.Concat(request.SourceText.AsSpan(0, 50), "...") :
                request.SourceText;
                
            _logger.LogError(ex, "Gemini翻訳に失敗しました: {SourceText}", sourceTextPreview);
            
            return CreateErrorResponse(
                request, 
                TranslationErrorType.ProcessingError, 
                $"翻訳処理に失敗しました: {ex.Message}");
        }
    }
    
    /// <inheritdoc/>
    public override async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests, nameof(requests));
            
        var results = new List<TranslationResponse>(requests.Count);
        
        // 一度に多くのリクエストを送ると問題が発生する可能性があるため
        // 順次処理する
        foreach (var request in requests)
        {
            try
            {
                var response = await TranslateAsync(request, cancellationToken).ConfigureAwait(false);
                results.Add(response);
            }
            catch (TranslationException ex)
            {
                _logger.LogError(ex, "バッチ翻訳処理中に翻訳例外が発生しました: {ErrorType}", ex.ErrorType);
                
                results.Add(CreateErrorResponse(
                    request, 
                    ex.ErrorType, 
                    $"バッチ翻訳処理に失敗しました: {ex.Message}"));
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "バッチ翻訳処理中にHTTP例外が発生しました: {StatusCode}", ex.StatusCode);
                
                results.Add(CreateErrorResponse(
                    request, 
                    TranslationErrorType.NetworkError, 
                    $"バッチ翻訳処理に通信エラーが発生しました: {ex.Message}"));
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "バッチ翻訳処理中にタスクキャンセル例外が発生しました");
                
                results.Add(CreateErrorResponse(
                    request, 
                    TranslationErrorType.OperationCanceled, 
                    $"バッチ翻訳処理がキャンセルされました: {ex.Message}"));
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "バッチ翻訳処理中にJSON例外が発生しました");
                
                results.Add(CreateErrorResponse(
                    request, 
                    TranslationErrorType.InvalidResponse, 
                    $"バッチ翻訳処理にJSON処理エラーが発生しました: {ex.Message}"));
            }
            catch (System.IO.IOException ex)
            {
                _logger.LogError(ex, "バッチ翻訳処理中に入出力エラーが発生しました");
                
                results.Add(CreateErrorResponse(
                    request, 
                    TranslationErrorType.NetworkError, 
                    $"バッチ翻訳処理中に入出力エラーが発生しました: {ex.Message}"));
            }
            
            // キャンセル確認
            if (cancellationToken.IsCancellationRequested)
                break;
        }
        
        return results;
    }
    
    /// <inheritdoc/>
    public override async Task<bool> SupportsLanguagePairAsync(LanguagePair pair)
    {
        ArgumentNullException.ThrowIfNull(pair, nameof(pair));
            
        var supportedPairs = await GetSupportedLanguagePairsAsync().ConfigureAwait(false);
        
        foreach (var supportedPair in supportedPairs)
        {
            if (string.Equals(supportedPair.SourceLanguage.Code, pair.SourceLanguage.Code, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(supportedPair.TargetLanguage.Code, pair.TargetLanguage.Code, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <inheritdoc/>
    public override async Task<LanguageDetectionResult> DetectLanguageAsync(
        string text, 
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text, nameof(text));
            
        if (!IsInitialized && !await InitializeAsync().ConfigureAwait(false))
        {
            throw new InvalidOperationException("翻訳エンジンが初期化されていません");
        }
        
        try
        {
            // Gemini APIでは言語検出のエンドポイントが明示的にないため
            // プロンプトで言語検出を実行
            var prompt = "次のテキストの言語を検出し、ISO 639-1の言語コードのみを返してください:\n\n" + text;
            
            var apiRequest = new GeminiApiRequest
            {
                Contents = 
                [
                    new GeminiContent
                    {
                        Role = "user",
                        Parts = 
                        [
                            new GeminiPart { Text = prompt }
                        ]
                    }
                ],
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = 0.0f,  // 決定論的な応答
                    MaxOutputTokens = 10  // 短い応答
                }
            };
            
            var modelName = _options.ModelName ?? DEFAULT_MODEL;
            var uri = new Uri($"{modelName}:generateContent?key={_apiKey}", UriKind.Relative);
            
            var response = await _httpClient.PostAsJsonAsync(
                uri, 
                apiRequest, 
                JsonSerializerOptions.Default, 
                cancellationToken).ConfigureAwait(false);
            
            response.EnsureSuccessStatusCode();
            
            var apiResponseTemp = await response.Content.ReadFromJsonAsync<GeminiApiResponse>(
                cancellationToken: cancellationToken).ConfigureAwait(false);
                
            if (apiResponseTemp == null)
            {
                throw new TranslationException(TranslationErrorType.InvalidResponse, "APIから空の応答が返されました");
            }
            
            // 不変条件を満たす値を代入
            var apiResponse = apiResponseTemp;
                
            if (apiResponse.Candidates == null || apiResponse.Candidates.Count == 0)
            {
                throw new TranslationException(TranslationErrorType.InvalidRequest, "APIからの応答が無効です");
            }
            
            var detectedLanguageCode = ExtractTextFromCandidate(apiResponse.Candidates[0]);
            if (string.IsNullOrEmpty(detectedLanguageCode))
            {
                throw new TranslationException(TranslationErrorType.InvalidResponse, "APIから受信した言語コードが無効です");
            }
            
            detectedLanguageCode = detectedLanguageCode.Trim();
            
            // ISO 639-1コードは2文字のはず
            if (detectedLanguageCode.Length != 2)
            {
                // 想定外の応答だが、何らかの言語コードの可能性がある
                _logger.LogWarning("言語検出で想定外の応答: {Response}", detectedLanguageCode);
            }
            
            // 言語名の解決を試みる
            var displayName = GetLanguageName(detectedLanguageCode);
            
            return LanguageDetectionResult.CreateSuccess(
                new Language { Code = detectedLanguageCode, DisplayName = displayName },
                0.8  // Gemini APIは信頼度を提供しないため固定値
            );
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "言語検出中にHTTP例外が発生しました: {StatusCode}", ex.StatusCode);
            return LanguageDetectionResult.CreateErrorFromException(
                TranslationErrorType.NetworkError,
                "HTTP通信エラーにより言語検出に失敗しました",
                ex
            );
        }
        catch (TranslationException ex)
        {
            _logger.LogError(ex, "言語検出中に翻訳例外が発生しました: {ErrorType}", ex.ErrorType);
            return LanguageDetectionResult.CreateErrorFromException(
                ex.ErrorType,
                "言語検出に失敗しました",
                ex
            );
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "言語検出中にJSON処理例外が発生しました");
            return LanguageDetectionResult.CreateErrorFromException(
                TranslationErrorType.InvalidResponse,
                "JSON処理エラーにより言語検出に失敗しました",
                ex
            );
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "言語検出中にタスクキャンセル例外が発生しました");
            return LanguageDetectionResult.CreateErrorFromException(
                TranslationErrorType.OperationCanceled,
                "タスクキャンセルにより言語検出に失敗しました",
                ex
            );
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "言語検出中に無効な操作例外が発生しました");
            return LanguageDetectionResult.CreateErrorFromException(
                TranslationErrorType.ProcessingError,
                "無効な操作により言語検出に失敗しました",
                ex
            );
        }
        catch (System.IO.IOException ex)
        {
            _logger.LogError(ex, "言語検出中に入出力例外が発生しました");
            return LanguageDetectionResult.CreateErrorFromException(
                TranslationErrorType.NetworkError,
                "入出力エラーにより言語検出に失敗しました",
                ex
            );
        }
    }
    
    /// <inheritdoc/>
    protected override void DisposeManagedResources()
    {
        // HttpClientのライフサイクルはDIコンテナで管理されている可能性があるため
        // ここでは特に何もしない
        base.DisposeManagedResources(); // 親クラスのDispose呼び出し
    }
    
    /// <inheritdoc/>
    protected override async ValueTask DisposeAsyncCore()
    {
        // 必要に応じて非同期リソースの解放を実装
        await Task.CompletedTask.ConfigureAwait(false);
        
        // 親クラスのDisposeAsyncCoreを呼び出す
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }
    
    /// <inheritdoc/>
    public async Task<AdvancedTranslationResponse> TranslateAdvancedAsync(
        AdvancedTranslationRequest request, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request, nameof(request));
                
        if (!IsInitialized && !await InitializeAsync().ConfigureAwait(false))
        {
            throw new TranslationServiceException("翻訳エンジンが初期化されていません");
        }
        
        // プロンプトの構築
        var prompt = BuildTranslationPrompt(request);
        
        try
        {
            // APIリクエストの構築
            var apiRequest = new GeminiApiRequest
            {
                Contents = 
                [
                    new GeminiContent
                    {
                        Role = "user",
                        Parts = 
                        [
                            new GeminiPart { Text = prompt }
                        ]
                    }
                ],
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = GetTemperatureFromQualityLevel(request.QualityLevel),
                    MaxOutputTokens = request.MaxTokens,
                    TopP = 0.8,
                    TopK = 40
                }
            };
            
            var modelName = _options.ModelName ?? DEFAULT_MODEL;
            var uri = new Uri($"{modelName}:generateContent?key={_apiKey}", UriKind.Relative);
            
            // APIリクエスト送信
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsJsonAsync(
                    uri, 
                    apiRequest, 
                    new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull },
                    cancellationToken).ConfigureAwait(false);
                
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Gemini API呼び出し中にHTTPエラーが発生しました: {StatusCode}", ex.StatusCode);
                return CreateErrorResponseFromHttpException(request, ex);
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("ユーザーによりGemini API呼び出しがキャンセルされました");
                return CreateAdvancedErrorResponse(
                request, 
                TranslationErrorType.OperationCanceled, 
                "操作がキャンセルされました");
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Gemini API呼び出しがタイムアウトしました");
                return CreateAdvancedErrorResponse(
                request, 
                TranslationErrorType.Timeout, 
                "API呼び出しがタイムアウトしました");
            }
            
            // レスポンス処理
            GeminiApiResponse apiResponse;
            try
            {
                GeminiApiResponse? apiResponseTemp = await response.Content.ReadFromJsonAsync<GeminiApiResponse>(
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                    
                if (apiResponseTemp == null)
                {
                    throw new TranslationFormatException("APIから空の応答が返されました");
                }
                
                apiResponse = apiResponseTemp;
                    
                if (apiResponse.Candidates == null || apiResponse.Candidates.Count == 0)
                {
                    throw new TranslationFormatException("APIからの応答が無効です");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Gemini API応答のJSONパースに失敗しました");
                return CreateAdvancedErrorResponse(
                    request, 
                    TranslationErrorType.InvalidResponse, 
                    "API応答の解析に失敗しました");
            }
            
            // 翻訳結果の抽出
            var translationResult = ExtractTranslationFromResponse(apiResponse);
            
            // ConfidenceScoreの計算
            double? confidenceScore = null;
            if (apiResponse.Candidates != null && apiResponse.Candidates.Count > 0)
            {
                var candidate = apiResponse.Candidates[0];
                if (candidate != null && candidate.SafetyRatings != null && candidate.SafetyRatings.Count > 0)
                {
                    var safetyRating = candidate.SafetyRatings[0];
                    if (safetyRating != null)
                    {
                        confidenceScore = safetyRating.Score;
                    }
                }
            }
            
            // レスポンスの構築
            var advancedResponse = new AdvancedTranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                TranslatedText = translationResult,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = Name,
                ProcessingTimeMs = 0, // 計測は呼び出し元で行う
                IsSuccess = true,
                ConfidenceScore = confidenceScore,
                UsedTokens = apiResponse.UsageMetadata?.TotalTokenCount ?? 0,
                ModelVersion = apiResponse.ModelVersion ?? modelName
            };
            
            // 代替翻訳の抽出（複数候補がある場合）
            if (apiResponse.Candidates != null && apiResponse.Candidates.Count > 1)
            {
                var candidates = apiResponse.Candidates;
                for (int i = 1; i < candidates.Count; i++)
                {
                    if (candidates[i] != null)
                    {
                        var candidateText = ExtractTextFromCandidate(candidates[i]);
                        if (!string.IsNullOrEmpty(candidateText))
                        {
                            // 代替翻訳を追加
                            advancedResponse.AddAlternativeTranslation(candidateText);
                        }
                    }
                }
            }
            
            return advancedResponse;
        }
        catch (TranslationException ex)
        {
            // カスタム翻訳例外は直接エラーレスポンスに変換
            _logger.LogError(ex, "翻訳処理中にエラーが発生しました: {ErrorType}", ex.ErrorType);
            return CreateErrorResponseFromTranslationException(request, ex);
        }
        catch (System.IO.IOException ex) 
        {
            // 入出力関連の例外
            _logger.LogError(ex, "Gemini翻訳処理中に入出力エラーが発生しました");
            return CreateAdvancedErrorResponse(
                request, 
                TranslationErrorType.NetworkError, 
                $"入出力エラーが発生しました: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            // 無効な操作関連の例外
            _logger.LogError(ex, "Gemini翻訳処理中に無効な操作が行われました");
            return CreateAdvancedErrorResponse(
                request, 
                TranslationErrorType.InvalidRequest, 
                $"無効な操作: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            // 引数関連の例外
            _logger.LogError(ex, "Gemini翻訳処理中に引数エラーが発生しました");
            return CreateAdvancedErrorResponse(
                request, 
                TranslationErrorType.InvalidRequest, 
                $"引数エラー: {ex.Message}");
        }
        catch (JsonException ex)
        {
            // JSON処理関連の例外
            _logger.LogError(ex, "Gemini翻訳処理中にJSON処理エラーが発生しました");
            return CreateAdvancedErrorResponse(
                request, 
                TranslationErrorType.InvalidResponse, 
                $"JSON処理エラー: {ex.Message}");
        }
        catch (TimeoutException ex)
        {
            // タイムアウト関連の例外
            _logger.LogError(ex, "Gemini翻訳処理中にタイムアウトが発生しました");
            return CreateAdvancedErrorResponse(
                request, 
                TranslationErrorType.Timeout, 
                $"処理タイムアウト: {ex.Message}");
        }
    }
    
    /// <inheritdoc/>
    public async Task<ApiStatusInfo> CheckApiStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 軽量なモデル情報リクエストで状態確認
            var modelName = _options.ModelName ?? DEFAULT_MODEL;
            var uri = new Uri($"{modelName}?key={_apiKey}", UriKind.Relative);
            
            var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            
            if (response.IsSuccessStatusCode)
            {
                return new ApiStatusInfo
                {
                    IsAvailable = true,
                    StatusMessage = "API接続正常",
                    ServiceLatency = response.Headers.TryGetValues("X-Server-Timing", out var timingValues)
                        ? ParseLatency(timingValues)
                        : null
                };
            }
            else
            {
                return new ApiStatusInfo
                {
                    IsAvailable = false,
                    StatusMessage = $"APIエラー: {response.StatusCode}",
                    ServiceLatency = response.Headers.TryGetValues("X-Server-Timing", out var timingValues)
                        ? ParseLatency(timingValues)
                        : null
                };
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Gemini API状態確認中にHTTPリクエストエラーが発生しました");
            
            return new ApiStatusInfo
            {
                IsAvailable = false,
                StatusMessage = $"HTTP通信エラー: {ex.Message}"
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Gemini API状態確認中にタイムアウトが発生しました");
            
            return new ApiStatusInfo
            {
                IsAvailable = false,
                StatusMessage = $"タイムアウトエラー: {ex.Message}"
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Gemini API状態確認のJSON解析中にエラーが発生しました");
            
            return new ApiStatusInfo
            {
                IsAvailable = false,
                StatusMessage = $"JSONフォーマットエラー: {ex.Message}"
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Gemini API状態確認中に無効な操作が発生しました");
            
            return new ApiStatusInfo
            {
                IsAvailable = false,
                StatusMessage = $"操作エラー: {ex.Message}"
            };
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Gemini API状態確認中に引数エラーが発生しました");
            
            return new ApiStatusInfo
            {
                IsAvailable = false,
                StatusMessage = $"引数エラー: {ex.Message}"
            };
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Gemini API状態確認中にフォーマットエラーが発生しました");
            
            return new ApiStatusInfo
            {
                IsAvailable = false,
                StatusMessage = $"フォーマットエラー: {ex.Message}"
            };
        }
    }
    
    /// <inheritdoc/>
    public override async Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        // Geminiでサポートする言語ペアリスト
        // 注: Geminiは多くの言語をサポートしているが、最適な結果のために主要言語ペアのみ定義
        var pairs = new List<LanguagePair>
        {
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "en", DisplayName = "English" }, 
                TargetLanguage = new Language { Code = "ja", DisplayName = "Japanese" } 
            },
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "ja", DisplayName = "Japanese" }, 
                TargetLanguage = new Language { Code = "en", DisplayName = "English" } 
            },
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "en", DisplayName = "English" }, 
                TargetLanguage = new Language { Code = "de", DisplayName = "German" } 
            },
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "de", DisplayName = "German" }, 
                TargetLanguage = new Language { Code = "en", DisplayName = "English" } 
            },
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "en", DisplayName = "English" }, 
                TargetLanguage = new Language { Code = "fr", DisplayName = "French" } 
            },
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "fr", DisplayName = "French" }, 
                TargetLanguage = new Language { Code = "en", DisplayName = "English" } 
            },
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "en", DisplayName = "English" }, 
                TargetLanguage = new Language { Code = "es", DisplayName = "Spanish" } 
            },
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "es", DisplayName = "Spanish" }, 
                TargetLanguage = new Language { Code = "en", DisplayName = "English" } 
            },
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "en", DisplayName = "English" }, 
                TargetLanguage = new Language { Code = "zh", DisplayName = "Chinese" } 
            },
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "zh", DisplayName = "Chinese" }, 
                TargetLanguage = new Language { Code = "en", DisplayName = "English" } 
            },
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "en", DisplayName = "English" }, 
                TargetLanguage = new Language { Code = "ko", DisplayName = "Korean" } 
            },
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "ko", DisplayName = "Korean" }, 
                TargetLanguage = new Language { Code = "en", DisplayName = "English" } 
            },
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "ja", DisplayName = "Japanese" }, 
                TargetLanguage = new Language { Code = "zh", DisplayName = "Chinese" } 
            },
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "zh", DisplayName = "Chinese" }, 
                TargetLanguage = new Language { Code = "ja", DisplayName = "Japanese" } 
            },
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "fr", DisplayName = "French" }, 
                TargetLanguage = new Language { Code = "de", DisplayName = "German" } 
            },
            new LanguagePair 
            { 
                SourceLanguage = new Language { Code = "de", DisplayName = "German" }, 
                TargetLanguage = new Language { Code = "fr", DisplayName = "French" } 
            }
        };
        
        // 非同期メソッドの正しい成續である Task<IReadOnlyCollection<LanguagePair>> を返す
        await Task.CompletedTask.ConfigureAwait(false); // 非同期コンテキストを確保
        return pairs;
    }
    
    #region ヘルパーメソッド

    /// <summary>
    /// 高度な翻訳リクエスト用のエラーレスポンスを作成
    /// </summary>
    private AdvancedTranslationResponse CreateAdvancedErrorResponse(
        AdvancedTranslationRequest request,
        TranslationErrorType errorType,
        string message)
    {
        return new AdvancedTranslationResponse
        {
            RequestId = request.RequestId,
            SourceText = request.SourceText,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            EngineName = Name,
            IsSuccess = false,
            Error = new TranslationError
            {
                ErrorCode = errorType.ToString(),
                Message = message
            }
        };
    }
    
    /// <summary>
    /// HTTP例外からエラーレスポンスを作成する
    /// </summary>
    private AdvancedTranslationResponse CreateErrorResponseFromHttpException(
        AdvancedTranslationRequest request, 
        HttpRequestException ex)
    {
        var errorType = ex.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => TranslationErrorType.AuthError,
            System.Net.HttpStatusCode.Forbidden => TranslationErrorType.AuthError,
            System.Net.HttpStatusCode.NotFound => TranslationErrorType.InvalidRequest,
            System.Net.HttpStatusCode.BadRequest => TranslationErrorType.InvalidRequest,
            System.Net.HttpStatusCode.TooManyRequests => TranslationErrorType.RateLimitExceeded,
            System.Net.HttpStatusCode.RequestTimeout => TranslationErrorType.Timeout,
            System.Net.HttpStatusCode.ServiceUnavailable => TranslationErrorType.ServiceUnavailable,
            System.Net.HttpStatusCode.GatewayTimeout => TranslationErrorType.Timeout,
            _ => TranslationErrorType.NetworkError
        };
        
        string errorMessage = errorType switch
        {
            TranslationErrorType.AuthError => "認証エラー: APIキーが無効か権限が不足しています",
            TranslationErrorType.InvalidRequest => $"無効なリクエスト: {ex.Message}",
            TranslationErrorType.RateLimitExceeded => "レート制限に達しました。しばらく経ってから再試行してください",
            TranslationErrorType.Timeout => "APIリクエストがタイムアウトしました",
            TranslationErrorType.ServiceUnavailable => "翻訳サービスは現在利用できません",
            _ => $"API通信エラー: {ex.Message}"
        };
        
        return new AdvancedTranslationResponse
        {
            RequestId = request.RequestId,
            SourceText = request.SourceText,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            EngineName = Name,
            IsSuccess = false,
            Error = new TranslationError
            {
                ErrorCode = errorType.ToString(),
                Message = errorMessage
            }
        };
    }
    
    /// <summary>
    /// 翻訳例外からエラーレスポンスを作成する
    /// </summary>
    private AdvancedTranslationResponse CreateErrorResponseFromTranslationException(
        AdvancedTranslationRequest request, 
        TranslationException ex)
    {
        return new AdvancedTranslationResponse
        {
            RequestId = request.RequestId,
            SourceText = request.SourceText,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            EngineName = Name,
            IsSuccess = false,
            Error = new TranslationError
            {
                ErrorCode = ex.ErrorType.ToString(),
                Message = ex.Message
            }
        };
    }
    
    /// <summary>
    /// 翻訳用プロンプトの構築
    /// </summary>
    private string BuildTranslationPrompt(AdvancedTranslationRequest request)
    {
        string sourceLangName = GetLanguageName(request.SourceLanguage.Code);
        string targetLangName = GetLanguageName(request.TargetLanguage.Code);
        
        // ユーザー定義のプロンプトテンプレートがあれば使用
        if (!string.IsNullOrEmpty(request.PromptTemplate))
        {
            return request.PromptTemplate
                .Replace("{SOURCE_TEXT}", request.SourceText, StringComparison.Ordinal)
                .Replace("{SOURCE_LANGUAGE}", sourceLangName, StringComparison.Ordinal)
                .Replace("{TARGET_LANGUAGE}", targetLangName, StringComparison.Ordinal);
        }
        
        // デフォルトのプロンプトテンプレート
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine(string.Format(CultureInfo.InvariantCulture, "あなたはプロの翻訳者として、以下の{0}テキストを{1}に翻訳してください。", sourceLangName, targetLangName));
        
        // 追加コンテキストの反映
        if (request.Context != null)
        {
            promptBuilder.AppendLine("翻訳コンテキスト:");
            
            if (!string.IsNullOrEmpty(request.Context.Genre))
            {
                promptBuilder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- ジャンル: {0}", request.Context.Genre));
            }
            
            if (!string.IsNullOrEmpty(request.Context.Domain))
            {
                promptBuilder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- 専門分野: {0}", request.Context.Domain));
            }
            
            if (request.Context.Tags.Count > 0)
            {
                promptBuilder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- タグ: {0}", string.Join(", ", request.Context.Tags)));
            }
            
            promptBuilder.AppendLine();
        }
        
        // 追加コンテキストリストの反映
        if (request.AdditionalContexts.Count > 0)
        {
            promptBuilder.AppendLine("追加コンテキスト:");
            foreach (var context in request.AdditionalContexts)
            {
                promptBuilder.AppendLine(string.Format(CultureInfo.InvariantCulture, "- {0}", context));
            }
            promptBuilder.AppendLine();
        }
        
        // 翻訳の品質レベルに応じた指示
        switch (request.QualityLevel)
        {
            case 5:
                promptBuilder.AppendLine("最も自然で流暢な翻訳を提供してください。文脈を完全に理解し、文化的ニュアンスも反映した翻訳が求められます。");
                break;
            case 4:
                promptBuilder.AppendLine("高品質で自然な翻訳を提供してください。文脈を考慮し、ターゲット言語の自然な表現を使用してください。");
                break;
            case 3:
                promptBuilder.AppendLine("バランスの取れた翻訳を提供してください。原文の意味を保ちつつ、自然な表現を心がけてください。");
                break;
            case 2:
                promptBuilder.AppendLine("原文に忠実な翻訳を提供してください。より直訳に近い翻訳が求められます。");
                break;
            case 1:
                promptBuilder.AppendLine("できるだけ原文に忠実な翻訳を提供してください。文法的な正確性よりも原文の意味を優先してください。");
                break;
            case 0:
                promptBuilder.AppendLine("厳密な直訳を提供してください。各単語や構造をできるだけ忠実に翻訳してください。");
                break;
            default:
                promptBuilder.AppendLine("バランスの取れた翻訳を提供してください。原文の意味を保ちつつ、自然な表現を心がけてください。");
                break;
        }
        
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("翻訳対象テキスト:");
        promptBuilder.AppendLine(request.SourceText);
        promptBuilder.AppendLine();
        promptBuilder.AppendLine(string.Format(CultureInfo.InvariantCulture, "以下に{0}翻訳を提供してください。翻訳のみを出力し、説明や追加コメントはしないでください。", targetLangName));
        
        return promptBuilder.ToString();
    }
    
    /// <summary>
    /// 言語コードから言語名を取得
    /// </summary>
    private string GetLanguageName(string languageCode)
    {
        string code = languageCode.ToUpperInvariant();
        
        return code switch
        {
            "JA" => "日本語",
            "EN" => "英語",
            "ZH" => "中国語",
            "KO" => "韓国語",
            "DE" => "ドイツ語",
            "FR" => "フランス語",
            "ES" => "スペイン語",
            "IT" => "イタリア語",
            "RU" => "ロシア語",
            "PT" => "ポルトガル語",
            _ => languageCode
        };
    }
    
    /// <summary>
    /// 品質レベルからモデルの温度パラメータを取得
    /// </summary>
    private float GetTemperatureFromQualityLevel(int qualityLevel)
    {
        return qualityLevel switch
        {
            0 => 0.0f,  // 最も決定論的
            1 => 0.2f,
            2 => 0.4f,
            3 => 0.6f,
            4 => 0.8f,
            5 => 1.0f,  // 最も創造的
            _ => 0.7f   // デフォルト
        };
    }
    
    /// <summary>
    /// レスポンスから翻訳テキストを抽出
    /// </summary>
    private string ExtractTranslationFromResponse(GeminiApiResponse response)
    {
        if (response.Candidates == null || response.Candidates.Count == 0)
        {
            return string.Empty;
        }
        
        return ExtractTextFromCandidate(response.Candidates[0]);
    }
    
    /// <summary>
    /// 候補から翻訳テキストを抽出
    /// </summary>
    private string ExtractTextFromCandidate(GeminiCandidate candidate)
    {
        if (candidate.Content?.Parts == null || candidate.Content.Parts.Count == 0)
        {
            return string.Empty;
        }
        
        var firstPart = candidate.Content.Parts[0];
        return firstPart?.Text?.Trim() ?? string.Empty;
    }
    
    /// <summary>
    /// レイテンシ情報を解析
    /// </summary>
    private TimeSpan? ParseLatency(IEnumerable<string> timingValues)
    {
        foreach (var value in timingValues)
        {
            if (value.Contains("total=", StringComparison.Ordinal))
            {
                // 正規表現の実装
                var match = TotalRegex().Match(value);
                if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int milliseconds))
                {
                    return TimeSpan.FromMilliseconds(milliseconds);
                }
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// 正規表現のコンパイル時生成用メソッド
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"total=(\d+)")]
    private static partial System.Text.RegularExpressions.Regex TotalRegex();
    
    #endregion
    
    #region Gemini API DTOs
    
    /// <summary>
    /// Gemini API リクエスト
    /// </summary>
    private sealed class GeminiApiRequest
    {
        [JsonPropertyName("contents")]
        public List<GeminiContent> Contents { get; set; } = [];
        
        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }
        
        [JsonPropertyName("safetySettings")]
        public List<GeminiSafetySetting>? SafetySettings { get; set; }
    }
    
    /// <summary>
    /// Gemini コンテンツ
    /// </summary>
    private sealed class GeminiContent
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }
        
        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; set; } = [];
    }
    
    /// <summary>
    /// Gemini パート
    /// </summary>
    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
    
    /// <summary>
    /// Gemini 生成設定
    /// </summary>
    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("temperature")]
        public float? Temperature { get; set; }
        
        [JsonPropertyName("topK")]
        public int? TopK { get; set; }
        
        [JsonPropertyName("topP")]
        public double? TopP { get; set; }
        
        [JsonPropertyName("maxOutputTokens")]
        public int? MaxOutputTokens { get; set; }
        
        [JsonPropertyName("candidateCount")]
        public int? CandidateCount { get; set; }
    }
    
    /// <summary>
    /// Gemini 安全設定
    /// </summary>
    private sealed class GeminiSafetySetting
    {
        [JsonPropertyName("category")]
        public string? Category { get; set; }
        
        [JsonPropertyName("threshold")]
        public string? Threshold { get; set; }
    }
    
    /// <summary>
    /// Gemini API レスポンス
    /// </summary>
    private sealed class GeminiApiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
        
        [JsonPropertyName("promptFeedback")]
        public GeminiPromptFeedback? PromptFeedback { get; set; }
        
        [JsonPropertyName("usageMetadata")]
        public GeminiUsageMetadata? UsageMetadata { get; set; }
        
        [JsonPropertyName("modelVersion")]
        public string? ModelVersion { get; set; }
    }
    
    /// <summary>
    /// Gemini 候補
    /// </summary>
    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
        
        [JsonPropertyName("finishReason")]
        public string? FinishReason { get; set; }
        
        [JsonPropertyName("index")]
        public int? Index { get; set; }
        
        [JsonPropertyName("safetyRatings")]
        public List<GeminiSafetyRating>? SafetyRatings { get; set; }
    }
    
    /// <summary>
    /// Gemini 安全性評価
    /// </summary>
    private sealed class GeminiSafetyRating
    {
        [JsonPropertyName("category")]
        public string? Category { get; set; }
        
        [JsonPropertyName("probability")]
        public string? Probability { get; set; }
        
        [JsonPropertyName("score")]
        public double? Score { get; set; }
    }
    
    /// <summary>
    /// Gemini プロンプトフィードバック
    /// </summary>
    private sealed class GeminiPromptFeedback
    {
        [JsonPropertyName("safetyRatings")]
        public List<GeminiSafetyRating>? SafetyRatings { get; set; }
    }
    
    /// <summary>
    /// Gemini 使用メタデータ
    /// </summary>
    private sealed class GeminiUsageMetadata
    {
        [JsonPropertyName("promptTokenCount")]
        public int? PromptTokenCount { get; set; }
        
        [JsonPropertyName("candidatesTokenCount")]
        public int? CandidatesTokenCount { get; set; }
        
        [JsonPropertyName("totalTokenCount")]
        public int? TotalTokenCount { get; set; }
    }
    
    #endregion
}

/// <summary>
/// Gemini翻訳エンジンのオプション
/// </summary>
public class GeminiEngineOptions
{
    /// <summary>
    /// APIキー
    /// </summary>
    public string? ApiKey { get; set; }
    
    /// <summary>
    /// API エンドポイント
    /// </summary>
    public string? ApiEndpoint { get; set; }
    
    /// <summary>
    /// モデル名
    /// </summary>
    public string? ModelName { get; set; }
    
    /// <summary>
    /// タイムアウト（秒）
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// リトライ回数
    /// </summary>
    public int RetryCount { get; set; } = 3;
    
    /// <summary>
    /// リトライ間隔（秒）
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 1;
}