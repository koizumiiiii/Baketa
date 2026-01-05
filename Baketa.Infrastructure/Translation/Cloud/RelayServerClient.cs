using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baketa.Core.Settings;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Translation.Cloud;

/// <summary>
/// Relay Server（Cloudflare Workers）との通信を行うHTTPクライアント
/// </summary>
/// <remarks>
/// - セッショントークンによる認証
/// - Cloud AI翻訳リクエストの送受信
/// - エラーハンドリングとリトライ
/// </remarks>
public sealed class RelayServerClient : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RelayServerClient> _logger;
    private readonly CloudTranslationSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions;

    private bool _disposed;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public RelayServerClient(
        HttpClient httpClient,
        IOptions<CloudTranslationSettings> settings,
        ILogger<RelayServerClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // HTTPクライアントの設定
        _httpClient.BaseAddress = new Uri(_settings.RelayServerUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);

        // APIキーヘッダー設定
        if (!string.IsNullOrEmpty(_settings.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", _settings.ApiKey);
        }

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// 画像翻訳を実行
    /// </summary>
    /// <param name="request">翻訳リクエスト</param>
    /// <param name="sessionToken">セッショントークン（Patreon認証）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>翻訳レスポンス</returns>
    public async Task<ImageTranslationResponse> TranslateImageAsync(
        ImageTranslationRequest request,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(sessionToken);

        var startTime = DateTime.UtcNow;
        var lastException = default(Exception);

        for (int attempt = 0; attempt <= _settings.MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    _logger.LogDebug("リトライ試行 {Attempt}/{MaxRetries}", attempt, _settings.MaxRetries);
                    await Task.Delay(_settings.RetryDelayMs, cancellationToken).ConfigureAwait(false);
                }

                var response = await SendTranslateRequestAsync(request, sessionToken, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccess)
                {
                    _logger.LogInformation(
                        "翻訳成功: RequestId={RequestId}, Provider={Provider}, Input={InputTokens}, Output={OutputTokens}, Total={TotalTokens}",
                        response.RequestId,
                        response.ProviderId,
                        response.TokenUsage?.InputTokens ?? 0,
                        response.TokenUsage?.OutputTokens ?? 0,
                        response.TokenUsage?.TotalTokens ?? 0);
                }
                else if (response.Error?.IsRetryable == true && attempt < _settings.MaxRetries)
                {
                    _logger.LogWarning(
                        "リトライ可能エラー: Code={Code}, Message={Message}",
                        response.Error.Code,
                        response.Error.Message);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "HTTP通信エラー (試行 {Attempt}/{MaxRetries})", attempt + 1, _settings.MaxRetries + 1);

                if (attempt >= _settings.MaxRetries)
                {
                    return ImageTranslationResponse.Failure(
                        request.RequestId,
                        new TranslationErrorDetail
                        {
                            Code = TranslationErrorDetail.Codes.NetworkError,
                            Message = $"通信エラー: {ex.Message}",
                            IsRetryable = true
                        },
                        DateTime.UtcNow - startTime);
                }
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                _logger.LogWarning(ex, "タイムアウト (試行 {Attempt}/{MaxRetries})", attempt + 1, _settings.MaxRetries + 1);

                if (attempt >= _settings.MaxRetries)
                {
                    return ImageTranslationResponse.Failure(
                        request.RequestId,
                        new TranslationErrorDetail
                        {
                            Code = TranslationErrorDetail.Codes.Timeout,
                            Message = "リクエストがタイムアウトしました",
                            IsRetryable = true
                        },
                        DateTime.UtcNow - startTime);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("翻訳がキャンセルされました: RequestId={RequestId}", request.RequestId);
                throw;
            }
        }

        // すべてのリトライ失敗
        return ImageTranslationResponse.Failure(
            request.RequestId,
            new TranslationErrorDetail
            {
                Code = TranslationErrorDetail.Codes.NetworkError,
                Message = lastException?.Message ?? "通信エラー",
                IsRetryable = false
            },
            DateTime.UtcNow - startTime);
    }

    /// <summary>
    /// Relay Serverのヘルスチェック
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/health", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ヘルスチェック失敗");
            return false;
        }
    }

    private async Task<ImageTranslationResponse> SendTranslateRequestAsync(
        ImageTranslationRequest request,
        string sessionToken,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        // リクエストボディ作成
        var requestBody = new RelayTranslateRequest
        {
            Provider = _settings.PrimaryProviderId,
            ImageBase64 = request.ImageBase64,
            MimeType = request.MimeType,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            Context = request.Context,
            RequestId = request.RequestId
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/translate");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
        httpRequest.Content = JsonContent.Create(requestBody, options: _jsonOptions);

        using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var processingTime = DateTime.UtcNow - startTime;

        var responseBody = await httpResponse.Content.ReadFromJsonAsync<RelayTranslateResponse>(
            _jsonOptions, cancellationToken).ConfigureAwait(false);

        if (responseBody == null)
        {
            return ImageTranslationResponse.Failure(
                request.RequestId,
                new TranslationErrorDetail
                {
                    Code = TranslationErrorDetail.Codes.ApiError,
                    Message = "空のレスポンスを受信しました",
                    IsRetryable = false
                },
                processingTime);
        }

        // HTTPステータスコードによるエラー判定
        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorCode = httpResponse.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => TranslationErrorDetail.Codes.SessionInvalid,
                System.Net.HttpStatusCode.Forbidden => TranslationErrorDetail.Codes.PlanNotSupported,
                System.Net.HttpStatusCode.TooManyRequests => TranslationErrorDetail.Codes.RateLimited,
                System.Net.HttpStatusCode.ServiceUnavailable => TranslationErrorDetail.Codes.ApiError,
                _ => TranslationErrorDetail.Codes.ApiError
            };

            return ImageTranslationResponse.Failure(
                request.RequestId,
                new TranslationErrorDetail
                {
                    Code = responseBody.Error?.Code ?? errorCode,
                    Message = responseBody.Error?.Message ?? $"HTTPエラー: {(int)httpResponse.StatusCode}",
                    IsRetryable = responseBody.Error?.IsRetryable ?? (int)httpResponse.StatusCode >= 500
                },
                processingTime);
        }

        // 成功レスポンス
        if (responseBody.Success)
        {
            return ImageTranslationResponse.Success(
                responseBody.RequestId ?? request.RequestId,
                responseBody.DetectedText ?? string.Empty,
                responseBody.TranslatedText ?? string.Empty,
                responseBody.ProviderId ?? _settings.PrimaryProviderId,
                new TokenUsageDetail
                {
                    InputTokens = responseBody.TokenUsage?.InputTokens ?? 0,
                    OutputTokens = responseBody.TokenUsage?.OutputTokens ?? 0,
                    ImageTokens = responseBody.TokenUsage?.ImageTokens ?? 0
                },
                TimeSpan.FromMilliseconds(responseBody.ProcessingTimeMs ?? processingTime.TotalMilliseconds),
                responseBody.DetectedLanguage);
        }

        // API応答内のエラー
        return ImageTranslationResponse.Failure(
            request.RequestId,
            new TranslationErrorDetail
            {
                Code = responseBody.Error?.Code ?? TranslationErrorDetail.Codes.ApiError,
                Message = responseBody.Error?.Message ?? "翻訳に失敗しました",
                IsRetryable = responseBody.Error?.IsRetryable ?? false
            },
            processingTime);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // HttpClientはHttpClientFactoryで管理されている場合はDisposeしない
        await Task.CompletedTask.ConfigureAwait(false);
    }

    #region DTOs

    private sealed class RelayTranslateRequest
    {
        [JsonPropertyName("provider")]
        public string Provider { get; set; } = "gemini";

        [JsonPropertyName("image_base64")]
        public string ImageBase64 { get; set; } = string.Empty;

        [JsonPropertyName("mime_type")]
        public string MimeType { get; set; } = "image/png";

        [JsonPropertyName("source_language")]
        public string SourceLanguage { get; set; } = "auto";

        [JsonPropertyName("target_language")]
        public string TargetLanguage { get; set; } = string.Empty;

        [JsonPropertyName("context")]
        public string? Context { get; set; }

        [JsonPropertyName("request_id")]
        public string? RequestId { get; set; }
    }

    private sealed class RelayTranslateResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("request_id")]
        public string? RequestId { get; set; }

        [JsonPropertyName("detected_text")]
        public string? DetectedText { get; set; }

        [JsonPropertyName("translated_text")]
        public string? TranslatedText { get; set; }

        [JsonPropertyName("detected_language")]
        public string? DetectedLanguage { get; set; }

        [JsonPropertyName("provider_id")]
        public string? ProviderId { get; set; }

        [JsonPropertyName("token_usage")]
        public RelayTokenUsage? TokenUsage { get; set; }

        [JsonPropertyName("processing_time_ms")]
        public double? ProcessingTimeMs { get; set; }

        [JsonPropertyName("error")]
        public RelayError? Error { get; set; }
    }

    private sealed class RelayTokenUsage
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }

        [JsonPropertyName("image_tokens")]
        public int ImageTokens { get; set; }
    }

    private sealed class RelayError
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("is_retryable")]
        public bool IsRetryable { get; set; }
    }

    #endregion
}
