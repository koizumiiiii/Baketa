using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baketa.Core.Settings;
using Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Services;
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
    private readonly IApiRequestDeduplicator _deduplicator;
    private readonly CloudTranslationSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions;

    private bool _disposed;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public RelayServerClient(
        HttpClient httpClient,
        IApiRequestDeduplicator deduplicator,
        IOptions<CloudTranslationSettings> settings,
        ILogger<RelayServerClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _deduplicator = deduplicator ?? throw new ArgumentNullException(nameof(deduplicator));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // HTTPクライアントの設定
        _httpClient.BaseAddress = new Uri(_settings.RelayServerUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);

        // [Issue #287] 静的API Key削除 - JWT認証またはSessionToken認証を使用
        // X-API-Key ヘッダーは不要（セッショントークンがリクエスト毎に設定される）

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _logger.LogDebug("[Issue #299] RelayServerClient initialized with ApiRequestDeduplicator");
    }

    /// <summary>
    /// 画像翻訳を実行（デフォルトプロバイダー使用）
    /// </summary>
    /// <param name="request">翻訳リクエスト</param>
    /// <param name="sessionToken">セッショントークン（Patreon認証）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>翻訳レスポンス</returns>
    public Task<ImageTranslationResponse> TranslateImageAsync(
        ImageTranslationRequest request,
        string sessionToken,
        CancellationToken cancellationToken = default)
        => TranslateImageAsync(request, sessionToken, _settings.PrimaryProviderId, cancellationToken);

    /// <summary>
    /// 画像翻訳を実行（プロバイダー指定）
    /// </summary>
    /// <param name="request">翻訳リクエスト</param>
    /// <param name="sessionToken">セッショントークン（Patreon認証）</param>
    /// <param name="providerId">プロバイダーID（gemini/openai）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>翻訳レスポンス</returns>
    public async Task<ImageTranslationResponse> TranslateImageAsync(
        ImageTranslationRequest request,
        string sessionToken,
        string providerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(sessionToken);
        ArgumentException.ThrowIfNullOrEmpty(providerId);

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

                var response = await SendTranslateRequestAsync(request, sessionToken, providerId, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccess)
                {
#if DEBUG
                    _logger.LogInformation(
                        "翻訳成功: RequestId={RequestId}, Provider={Provider}, Input={InputTokens}, Output={OutputTokens}, Total={TotalTokens}",
                        response.RequestId,
                        response.ProviderId,
                        response.TokenUsage?.InputTokens ?? 0,
                        response.TokenUsage?.OutputTokens ?? 0,
                        response.TokenUsage?.TotalTokens ?? 0);
#endif
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
        string providerId,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        // リクエストボディ作成
        var requestBody = new RelayTranslateRequest
        {
            Provider = providerId,
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

        // [Issue #333] レスポンス本体を文字列として読み込み（デバッグ用）
        var rawResponse = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        RelayTranslateResponse? responseBody = null;
        try
        {
            responseBody = JsonSerializer.Deserialize<RelayTranslateResponse>(rawResponse, _jsonOptions);
        }
        catch (JsonException ex)
        {
            // [Issue #333] JSONパースエラー時の詳細ログ
            var truncatedResponse = rawResponse.Length > 500 ? rawResponse[..500] + "..." : rawResponse;
            _logger.LogError(ex,
                "[Issue #333] JSON parse error: StatusCode={StatusCode}, RawResponse={RawResponse}",
                (int)httpResponse.StatusCode,
                truncatedResponse);
        }

        if (responseBody == null)
        {
            // [Issue #333] レスポンス解析失敗時の詳細ログ
            var truncatedResponse = rawResponse.Length > 500 ? rawResponse[..500] + "..." : rawResponse;
            _logger.LogError(
                "[Issue #333] Empty or invalid response: StatusCode={StatusCode}, RawResponse={RawResponse}",
                (int)httpResponse.StatusCode,
                truncatedResponse);

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
            // [Issue #333] HTTPエラー時の詳細ログ
            var truncatedResponse = rawResponse.Length > 500 ? rawResponse[..500] + "..." : rawResponse;
            _logger.LogWarning(
                "[Issue #333] HTTP error: StatusCode={StatusCode}, ErrorCode={ErrorCode}, ErrorMessage={ErrorMessage}, RawResponse={RawResponse}",
                (int)httpResponse.StatusCode,
                responseBody.Error?.Code ?? "unknown",
                responseBody.Error?.Message ?? "unknown",
                truncatedResponse);

            var errorCode = httpResponse.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => TranslationErrorDetail.Codes.SessionInvalid,
                System.Net.HttpStatusCode.Forbidden => TranslationErrorDetail.Codes.PlanNotSupported,
                System.Net.HttpStatusCode.TooManyRequests => TranslationErrorDetail.Codes.RateLimited,
                System.Net.HttpStatusCode.ServiceUnavailable => TranslationErrorDetail.Codes.ApiError,
                _ => TranslationErrorDetail.Codes.ApiError
            };

            // [Issue #296] サーバーからのエラーコードを使用（QUOTA_EXCEEDED対応）
            var actualErrorCode = responseBody.Error?.Code ?? errorCode;

            // [Issue #296] エラーレスポンスでもmonthly_usageが含まれている場合がある（QUOTA_EXCEEDED時）
            ServerMonthlyUsage? monthlyUsage = null;
            if (responseBody.MonthlyUsage is not null && !string.IsNullOrEmpty(responseBody.MonthlyUsage.YearMonth))
            {
                monthlyUsage = new ServerMonthlyUsage
                {
                    YearMonth = responseBody.MonthlyUsage.YearMonth,
                    TokensUsed = responseBody.MonthlyUsage.TokensUsed,
                    TokensLimit = responseBody.MonthlyUsage.TokensLimit
                };

                _logger.LogWarning(
                    "[Issue #296] エラーレスポンスにmonthly_usage含む: Code={Code}, Used={Used}, Limit={Limit}",
                    actualErrorCode, monthlyUsage.TokensUsed, monthlyUsage.TokensLimit);
            }

            return ImageTranslationResponse.FailureWithMonthlyUsage(
                request.RequestId,
                new TranslationErrorDetail
                {
                    Code = actualErrorCode,
                    Message = responseBody.Error?.Message ?? $"HTTPエラー: {(int)httpResponse.StatusCode}",
                    IsRetryable = responseBody.Error?.IsRetryable ?? (int)httpResponse.StatusCode >= 500
                },
                processingTime,
                monthlyUsage);
        }

        // 成功レスポンス
        if (responseBody.Success)
        {
            // [Issue #296] サーバーサイドの月間使用状況
            ServerMonthlyUsage? monthlyUsage = null;
            if (responseBody.MonthlyUsage is not null && !string.IsNullOrEmpty(responseBody.MonthlyUsage.YearMonth))
            {
                monthlyUsage = new ServerMonthlyUsage
                {
                    YearMonth = responseBody.MonthlyUsage.YearMonth,
                    TokensUsed = responseBody.MonthlyUsage.TokensUsed,
                    TokensLimit = responseBody.MonthlyUsage.TokensLimit
                };
            }

            // [Issue #275] 複数テキスト対応
            if (responseBody.Texts is { Count: > 0 })
            {
                var texts = responseBody.Texts
                    .Select(t => new TranslatedTextItem
                    {
                        Original = t.Original ?? string.Empty,
                        Translation = t.Translation ?? string.Empty,
                        // BoundingBox座標変換: [y_min, x_min, y_max, x_max] (0-1000スケール) → Int32Rect (同スケール)
                        // Note: この時点では画像サイズが不明なため、0-1000スケールのままInt32Rectに格納する。
                        //       後続の処理（CrossValidator等）で実際のピクセル座標へスケール変換される。
                        BoundingBox = t.BoundingBox is { Length: 4 }
                            ? new Baketa.Core.Models.Int32Rect(
                                t.BoundingBox[1], // x = x_min
                                t.BoundingBox[0], // y = y_min
                                t.BoundingBox[3] - t.BoundingBox[1], // width = x_max - x_min
                                t.BoundingBox[2] - t.BoundingBox[0]) // height = y_max - y_min
                            : null
                    })
                    .ToList();

                return new ImageTranslationResponse
                {
                    RequestId = responseBody.RequestId ?? request.RequestId,
                    IsSuccess = true,
                    DetectedText = texts.Count > 0 ? texts[0].Original : string.Empty,
                    TranslatedText = texts.Count > 0 ? texts[0].Translation : string.Empty,
                    DetectedLanguage = responseBody.DetectedLanguage,
                    ProviderId = responseBody.ProviderId ?? _settings.PrimaryProviderId,
                    TokenUsage = new TokenUsageDetail
                    {
                        InputTokens = responseBody.TokenUsage?.InputTokens ?? 0,
                        OutputTokens = responseBody.TokenUsage?.OutputTokens ?? 0,
                        ImageTokens = responseBody.TokenUsage?.ImageTokens ?? 0
                    },
                    ProcessingTime = TimeSpan.FromMilliseconds(responseBody.ProcessingTimeMs ?? processingTime.TotalMilliseconds),
                    Texts = texts,
                    MonthlyUsage = monthlyUsage  // [Issue #296]
                };
            }

            // 旧形式（単一テキスト）の場合
            return new ImageTranslationResponse
            {
                RequestId = responseBody.RequestId ?? request.RequestId,
                IsSuccess = true,
                DetectedText = responseBody.DetectedText ?? string.Empty,
                TranslatedText = responseBody.TranslatedText ?? string.Empty,
                DetectedLanguage = responseBody.DetectedLanguage,
                ProviderId = responseBody.ProviderId ?? _settings.PrimaryProviderId,
                TokenUsage = new TokenUsageDetail
                {
                    InputTokens = responseBody.TokenUsage?.InputTokens ?? 0,
                    OutputTokens = responseBody.TokenUsage?.OutputTokens ?? 0,
                    ImageTokens = responseBody.TokenUsage?.ImageTokens ?? 0
                },
                ProcessingTime = TimeSpan.FromMilliseconds(responseBody.ProcessingTimeMs ?? processingTime.TotalMilliseconds),
                MonthlyUsage = monthlyUsage  // [Issue #296]
            };
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

    /// <summary>
    /// [Issue #296] サーバーからクォータ状態を取得
    /// </summary>
    /// <param name="sessionToken">セッショントークン</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>クォータ状態（取得失敗時はnull）</returns>
    public async Task<QuotaStatusResponse?> GetQuotaStatusAsync(
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionToken);

        // [Issue #299] 重複呼び出し削減 - 同一キーのリクエストは1回のみ実行
        return await _deduplicator.ExecuteOnceAsync(
            "quota-status",
            () => GetQuotaStatusCoreAsync(sessionToken, cancellationToken),
            ApiCacheDurations.QuotaStatus).ConfigureAwait(false);
    }

    /// <summary>
    /// [Issue #299] クォータ状態取得の実装
    /// </summary>
    private async Task<QuotaStatusResponse?> GetQuotaStatusCoreAsync(
        string sessionToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, "/api/quota/status");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);

            using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[Issue #296] クォータ状態取得失敗: StatusCode={StatusCode}",
                    httpResponse.StatusCode);
                return null;
            }

            var response = await httpResponse.Content.ReadFromJsonAsync<RelayQuotaStatusResponse>(
                _jsonOptions, cancellationToken).ConfigureAwait(false);

            if (response?.Success != true || response.MonthlyUsage == null)
            {
                _logger.LogWarning("[Issue #296] クォータ状態レスポンスが不正");
                return null;
            }

            _logger.LogInformation(
                "[Issue #299] クォータ状態取得成功: YearMonth={YearMonth}, Used={Used}, Limit={Limit}",
                response.MonthlyUsage.YearMonth,
                response.MonthlyUsage.TokensUsed,
                response.MonthlyUsage.TokensLimit);

            return new QuotaStatusResponse
            {
                YearMonth = response.MonthlyUsage.YearMonth ?? string.Empty,
                TokensUsed = response.MonthlyUsage.TokensUsed,
                TokensLimit = response.MonthlyUsage.TokensLimit,
                IsExceeded = response.MonthlyUsage.IsExceeded,
                Plan = response.Plan ?? string.Empty,
                HasBonusTokens = response.HasBonusTokens
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[Issue #296] クォータ状態取得中に通信エラー");
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("[Issue #296] クォータ状態取得がタイムアウト");
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #296] クォータ状態取得中に予期せぬエラー");
            return null;
        }
    }

    /// <summary>
    /// [Issue #299] 統合初期化エンドポイントから全ステータスを一括取得
    /// </summary>
    /// <param name="sessionToken">セッショントークン（Supabase JWT）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>統合レスポンス（取得失敗時はnull）</returns>
    public async Task<SyncInitResponse?> SyncInitAsync(
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionToken);

        // [Issue #299] 起動時に1回だけ呼ばれるため、キャッシュは短め（60秒）
        return await _deduplicator.ExecuteOnceAsync(
            "sync-init",
            () => SyncInitCoreAsync(sessionToken, cancellationToken),
            TimeSpan.FromSeconds(60)).ConfigureAwait(false);
    }

    /// <summary>
    /// [Issue #299] 統合初期化エンドポイントの実装
    /// </summary>
    private async Task<SyncInitResponse?> SyncInitCoreAsync(
        string sessionToken,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("[Issue #299] 統合初期化エンドポイント呼び出し開始");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, "/api/sync/init");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);

            using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

            sw.Stop();

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[Issue #299] 統合初期化失敗: StatusCode={StatusCode}, Duration={Duration}ms",
                    httpResponse.StatusCode,
                    sw.ElapsedMilliseconds);
                return null;
            }

            var response = await httpResponse.Content.ReadFromJsonAsync<RelaySyncInitResponse>(
                _jsonOptions, cancellationToken).ConfigureAwait(false);

            if (response?.Success != true)
            {
                _logger.LogWarning("[Issue #299] 統合初期化レスポンスが不正");
                return null;
            }

            // 部分的失敗をログ
            if (response.PartialFailure)
            {
                _logger.LogWarning(
                    "[Issue #299] 統合初期化で部分的失敗: FailedComponents={Components}",
                    response.FailedComponents != null ? string.Join(", ", response.FailedComponents) : "unknown");
            }

            _logger.LogInformation(
                "[Issue #299] 統合初期化成功: Duration={Duration}ms, HasPromotion={HasPromo}, BonusCount={BonusCount}, PartialFailure={PartialFailure}",
                sw.ElapsedMilliseconds,
                response.Promotion?.HasPromotion ?? false,
                response.BonusTokens?.ActiveCount ?? 0,
                response.PartialFailure);

            // 公開DTOに変換
            return new SyncInitResponse
            {
                Promotion = response.Promotion != null
                    ? new SyncPromotionStatus
                    {
                        HasPromotion = response.Promotion.HasPromotion,
                        Expired = response.Promotion.Expired,
                        PromotionCode = response.Promotion.Promotion?.Code,
                        PromotionTier = response.Promotion.Promotion?.Tier,
                        ExpiresAt = response.Promotion.Promotion?.ExpiresAt
                    }
                    : null,
                Consent = response.Consent != null
                    ? new SyncConsentStatus
                    {
                        HasPrivacyPolicy = response.Consent.PrivacyPolicy != null,
                        PrivacyPolicyVersion = response.Consent.PrivacyPolicy?.Version,
                        HasTermsOfService = response.Consent.TermsOfService != null,
                        TermsOfServiceVersion = response.Consent.TermsOfService?.Version
                    }
                    : null,
                // [Issue #347] 有効期限関連フィールド削除
                BonusTokens = response.BonusTokens != null
                    ? new SyncBonusTokensStatus
                    {
                        TotalRemaining = response.BonusTokens.TotalRemaining,
                        ActiveCount = response.BonusTokens.ActiveCount,
                        Bonuses = response.BonusTokens.Bonuses?.Select(b => new SyncBonusTokenInfo
                        {
                            BonusId = b.BonusId ?? string.Empty,
                            RemainingTokens = b.RemainingTokens,
                            GrantedTokens = b.GrantedTokens,
                            UsedTokens = b.UsedTokens
                        }).ToList() ?? []
                    }
                    : null,
                Quota = response.Quota != null
                    ? new SyncQuotaStatus
                    {
                        YearMonth = response.Quota.YearMonth ?? string.Empty,
                        TokensUsed = response.Quota.TokensUsed,
                        TokensLimit = response.Quota.TokensLimit
                    }
                    : null,
                PartialFailure = response.PartialFailure,
                FailedComponents = response.FailedComponents?.AsReadOnly()
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[Issue #299] 統合初期化中に通信エラー");
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("[Issue #299] 統合初期化がタイムアウト");
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #299] 統合初期化中に予期せぬエラー");
            return null;
        }
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

        /// <summary>
        /// [Issue #275] 複数テキスト対応 - BoundingBox付きテキスト配列
        /// </summary>
        [JsonPropertyName("texts")]
        public List<RelayTextItem>? Texts { get; set; }

        /// <summary>
        /// [Issue #296] サーバーサイドの月間トークン使用状況
        /// </summary>
        [JsonPropertyName("monthly_usage")]
        public RelayMonthlyUsage? MonthlyUsage { get; set; }
    }

    /// <summary>
    /// [Issue #296] サーバーサイドの月間トークン使用状況
    /// </summary>
    private sealed class RelayMonthlyUsage
    {
        [JsonPropertyName("year_month")]
        public string? YearMonth { get; set; }

        [JsonPropertyName("tokens_used")]
        public long TokensUsed { get; set; }

        [JsonPropertyName("tokens_limit")]
        public long TokensLimit { get; set; }
    }

    /// <summary>
    /// [Issue #275] 翻訳されたテキストアイテム
    /// </summary>
    private sealed class RelayTextItem
    {
        [JsonPropertyName("original")]
        public string? Original { get; set; }

        [JsonPropertyName("translation")]
        public string? Translation { get; set; }

        /// <summary>
        /// BoundingBox座標 [y_min, x_min, y_max, x_max] (0-1000スケール)
        /// </summary>
        [JsonPropertyName("bounding_box")]
        public int[]? BoundingBox { get; set; }
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

    /// <summary>
    /// [Issue #296] クォータ状態レスポンス（サーバーからのJSON）
    /// </summary>
    private sealed class RelayQuotaStatusResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("monthly_usage")]
        public RelayQuotaMonthlyUsage? MonthlyUsage { get; set; }

        [JsonPropertyName("plan")]
        public string? Plan { get; set; }

        [JsonPropertyName("has_bonus_tokens")]
        public bool HasBonusTokens { get; set; }
    }

    /// <summary>
    /// [Issue #296] クォータ状態の月間使用量（サーバーからのJSON）
    /// </summary>
    private sealed class RelayQuotaMonthlyUsage
    {
        [JsonPropertyName("year_month")]
        public string? YearMonth { get; set; }

        [JsonPropertyName("tokens_used")]
        public long TokensUsed { get; set; }

        [JsonPropertyName("tokens_limit")]
        public long TokensLimit { get; set; }

        [JsonPropertyName("is_exceeded")]
        public bool IsExceeded { get; set; }
    }

    // ============================================
    // [Issue #299] 統合初期化エンドポイント用DTO
    // ============================================

    private sealed class RelaySyncInitResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("promotion")]
        public RelaySyncPromotion? Promotion { get; set; }

        [JsonPropertyName("consent")]
        public RelaySyncConsent? Consent { get; set; }

        [JsonPropertyName("bonus_tokens")]
        public RelaySyncBonusTokens? BonusTokens { get; set; }

        [JsonPropertyName("quota")]
        public RelaySyncQuota? Quota { get; set; }

        /// <summary>部分的失敗があったかどうか</summary>
        [JsonPropertyName("partial_failure")]
        public bool PartialFailure { get; set; }

        /// <summary>失敗したコンポーネント名リスト</summary>
        [JsonPropertyName("failed_components")]
        public List<string>? FailedComponents { get; set; }
    }

    private sealed class RelaySyncPromotion
    {
        [JsonPropertyName("has_promotion")]
        public bool HasPromotion { get; set; }

        [JsonPropertyName("promotion")]
        public RelaySyncPromotionDetail? Promotion { get; set; }

        [JsonPropertyName("expired")]
        public bool Expired { get; set; }
    }

    private sealed class RelaySyncPromotionDetail
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("tier")]
        public string? Tier { get; set; }

        [JsonPropertyName("expires_at")]
        public string? ExpiresAt { get; set; }
    }

    private sealed class RelaySyncConsent
    {
        [JsonPropertyName("privacy_policy")]
        public RelaySyncConsentItem? PrivacyPolicy { get; set; }

        [JsonPropertyName("terms_of_service")]
        public RelaySyncConsentItem? TermsOfService { get; set; }
    }

    private sealed class RelaySyncConsentItem
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("recorded_at")]
        public string? RecordedAt { get; set; }
    }

    private sealed class RelaySyncBonusTokens
    {
        [JsonPropertyName("bonuses")]
        public List<RelaySyncBonusItem>? Bonuses { get; set; }

        [JsonPropertyName("total_remaining")]
        public long TotalRemaining { get; set; }

        [JsonPropertyName("active_count")]
        public int ActiveCount { get; set; }
    }

    /// <summary>
    /// [Issue #347] 有効期限関連フィールド削除
    /// </summary>
    private sealed class RelaySyncBonusItem
    {
        // [Issue #298] サーバーは "id" を返す（"bonus_id" ではない）
        [JsonPropertyName("id")]
        public string? BonusId { get; set; }

        [JsonPropertyName("remaining_tokens")]
        public long RemainingTokens { get; set; }

        [JsonPropertyName("granted_tokens")]
        public long GrantedTokens { get; set; }

        [JsonPropertyName("used_tokens")]
        public long UsedTokens { get; set; }
    }

    private sealed class RelaySyncQuota
    {
        [JsonPropertyName("year_month")]
        public string? YearMonth { get; set; }

        [JsonPropertyName("tokens_used")]
        public long TokensUsed { get; set; }

        [JsonPropertyName("tokens_limit")]
        public long TokensLimit { get; set; }
    }

    #endregion
}

/// <summary>
/// [Issue #296] クォータ状態レスポンス（公開DTO）
/// </summary>
public sealed record QuotaStatusResponse
{
    /// <summary>年月（YYYY-MM形式）</summary>
    public required string YearMonth { get; init; }

    /// <summary>使用済みトークン数</summary>
    public required long TokensUsed { get; init; }

    /// <summary>月間トークン上限</summary>
    public required long TokensLimit { get; init; }

    /// <summary>クォータ超過しているか</summary>
    public required bool IsExceeded { get; init; }

    /// <summary>プラン名</summary>
    public required string Plan { get; init; }

    /// <summary>ボーナストークンを所有しているか</summary>
    public required bool HasBonusTokens { get; init; }
}

// ============================================
// [Issue #299] 統合初期化レスポンス（公開DTO）
// ============================================

/// <summary>
/// [Issue #299] 統合初期化レスポンス
/// </summary>
public sealed record SyncInitResponse
{
    /// <summary>プロモーション状態</summary>
    public SyncPromotionStatus? Promotion { get; init; }

    /// <summary>同意状態</summary>
    public SyncConsentStatus? Consent { get; init; }

    /// <summary>ボーナストークン状態</summary>
    public SyncBonusTokensStatus? BonusTokens { get; init; }

    /// <summary>クォータ状態</summary>
    public SyncQuotaStatus? Quota { get; init; }

    /// <summary>部分的失敗があったかどうか</summary>
    public bool PartialFailure { get; init; }

    /// <summary>失敗したコンポーネント名リスト</summary>
    public IReadOnlyList<string>? FailedComponents { get; init; }
}

/// <summary>
/// [Issue #299] プロモーション状態
/// </summary>
public sealed record SyncPromotionStatus
{
    /// <summary>プロモーション適用中か</summary>
    public bool HasPromotion { get; init; }

    /// <summary>期限切れか</summary>
    public bool Expired { get; init; }

    /// <summary>プロモーションコード</summary>
    public string? PromotionCode { get; init; }

    /// <summary>プロモーションTier</summary>
    public string? PromotionTier { get; init; }

    /// <summary>有効期限</summary>
    public string? ExpiresAt { get; init; }
}

/// <summary>
/// [Issue #299] 同意状態
/// </summary>
public sealed record SyncConsentStatus
{
    /// <summary>プライバシーポリシー同意済みか</summary>
    public bool HasPrivacyPolicy { get; init; }

    /// <summary>プライバシーポリシーバージョン</summary>
    public string? PrivacyPolicyVersion { get; init; }

    /// <summary>利用規約同意済みか</summary>
    public bool HasTermsOfService { get; init; }

    /// <summary>利用規約バージョン</summary>
    public string? TermsOfServiceVersion { get; init; }
}

/// <summary>
/// [Issue #299] ボーナストークン状態
/// </summary>
public sealed record SyncBonusTokensStatus
{
    /// <summary>残りトークン合計</summary>
    public long TotalRemaining { get; init; }

    /// <summary>有効なボーナス数</summary>
    public int ActiveCount { get; init; }

    /// <summary>ボーナス一覧</summary>
    public List<SyncBonusTokenInfo> Bonuses { get; init; } = [];
}

/// <summary>
/// [Issue #299+#347] ボーナストークン情報（有効期限削除）
/// </summary>
public sealed record SyncBonusTokenInfo
{
    /// <summary>ボーナスID</summary>
    public required string BonusId { get; init; }

    /// <summary>残りトークン数</summary>
    public long RemainingTokens { get; init; }

    /// <summary>付与されたトークン数</summary>
    public long GrantedTokens { get; init; }

    /// <summary>使用済みトークン数</summary>
    public long UsedTokens { get; init; }
}

/// <summary>
/// [Issue #299] クォータ状態
/// </summary>
public sealed record SyncQuotaStatus
{
    /// <summary>年月（YYYY-MM形式）</summary>
    public required string YearMonth { get; init; }

    /// <summary>使用済みトークン数</summary>
    public long TokensUsed { get; init; }

    /// <summary>月間トークン上限</summary>
    public long TokensLimit { get; init; }
}
