using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baketa.Core.Settings;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Translation.Cloud;

/// <summary>
/// Direct Gemini API翻訳クライアント（開発・テスト用）
/// </summary>
/// <remarks>
/// Relay Serverを経由せずに直接Gemini APIを呼び出します。
/// Patreon認証をバイパスするため、開発・テスト目的でのみ使用してください。
/// </remarks>
public sealed class DirectGeminiImageTranslator : ICloudImageTranslator
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DirectGeminiImageTranslator> _logger;
    private readonly CloudTranslationSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string GeminiApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const string DefaultModel = "gemini-2.0-flash-exp";

    private bool _disposed;

    /// <inheritdoc />
    public string ProviderId => "direct-gemini";

    /// <inheritdoc />
    public string DisplayName => "Gemini (Direct API)";

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public DirectGeminiImageTranslator(
        HttpClient httpClient,
        IOptions<CloudTranslationSettings> settings,
        ILogger<DirectGeminiImageTranslator> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.UseDirectApiMode)
        {
            _logger.LogDebug("Direct APIモードが無効です");
            return false;
        }

        if (string.IsNullOrEmpty(_settings.DirectGeminiApiKey))
        {
            _logger.LogWarning("Direct Gemini APIキーが設定されていません");
            return false;
        }

        // Gemini APIは単純なヘルスチェックエンドポイントがないため、
        // APIキーが設定されていれば利用可能とみなす
        return true;
    }

    /// <inheritdoc />
    public async Task<ImageTranslationResponse> TranslateImageAsync(
        ImageTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_settings.UseDirectApiMode)
        {
            return ImageTranslationResponse.Failure(
                request.RequestId,
                new TranslationErrorDetail
                {
                    Code = TranslationErrorDetail.Codes.NotImplemented,
                    Message = "Direct APIモードが無効です",
                    IsRetryable = false
                },
                TimeSpan.Zero);
        }

        if (string.IsNullOrEmpty(_settings.DirectGeminiApiKey))
        {
            return ImageTranslationResponse.Failure(
                request.RequestId,
                new TranslationErrorDetail
                {
                    Code = TranslationErrorDetail.Codes.ApiError,
                    Message = "Direct Gemini APIキーが設定されていません",
                    IsRetryable = false
                },
                TimeSpan.Zero);
        }

        var startTime = DateTime.UtcNow;

        _logger.LogDebug(
            "Direct Gemini翻訳開始: RequestId={RequestId}, Target={Target}",
            request.RequestId,
            request.TargetLanguage);

        try
        {
            var response = await SendGeminiRequestAsync(request, cancellationToken).ConfigureAwait(false);
            var processingTime = DateTime.UtcNow - startTime;

            if (response.IsSuccess)
            {
                _logger.LogInformation(
                    "Direct Gemini翻訳成功: RequestId={RequestId}, Duration={Duration}ms",
                    request.RequestId,
                    processingTime.TotalMilliseconds);
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Direct Gemini API通信エラー: RequestId={RequestId}", request.RequestId);

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
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Direct Gemini APIタイムアウト: RequestId={RequestId}", request.RequestId);

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Direct Gemini翻訳がキャンセルされました: RequestId={RequestId}", request.RequestId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct Gemini翻訳中に予期せぬエラー: RequestId={RequestId}", request.RequestId);

            return ImageTranslationResponse.Failure(
                request.RequestId,
                new TranslationErrorDetail
                {
                    Code = TranslationErrorDetail.Codes.InternalError,
                    Message = $"内部エラー: {ex.Message}",
                    IsRetryable = false
                },
                DateTime.UtcNow - startTime);
        }
    }

    private async Task<ImageTranslationResponse> SendGeminiRequestAsync(
        ImageTranslationRequest request,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        // Gemini APIエンドポイント
        var url = $"{GeminiApiBaseUrl}/models/{DefaultModel}:generateContent?key={_settings.DirectGeminiApiKey}";

        // プロンプト作成（OCR + 翻訳を1回で実行）
        var targetLang = GetLanguageDisplayName(request.TargetLanguage);
        var prompt = $@"この画像に含まれるテキストを検出し、{targetLang}に翻訳してください。

以下のJSON形式で回答してください：
{{
  ""detected_text"": ""検出された元のテキスト"",
  ""detected_language"": ""検出された言語コード（例: en, ja, ko）"",
  ""translated_text"": ""翻訳されたテキスト""
}}

テキストが検出されない場合は、detected_textを空文字列にしてください。
JSONのみを出力し、他の説明は不要です。";

        // リクエストボディ作成
        var requestBody = new GeminiRequest
        {
            Contents =
            [
                new GeminiContent
                {
                    Parts =
                    [
                        new GeminiPart { Text = prompt },
                        new GeminiPart
                        {
                            InlineData = new GeminiInlineData
                            {
                                MimeType = request.MimeType,
                                Data = request.ImageBase64
                            }
                        }
                    ]
                }
            ],
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = 0.1,
                MaxOutputTokens = 2048
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Content = JsonContent.Create(requestBody, options: _jsonOptions);

        using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var processingTime = DateTime.UtcNow - startTime;

        // エラーレスポンスの処理
        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("Gemini APIエラー: Status={Status}, Body={Body}",
                httpResponse.StatusCode, errorContent);

            var errorCode = httpResponse.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => TranslationErrorDetail.Codes.SessionInvalid,
                System.Net.HttpStatusCode.TooManyRequests => TranslationErrorDetail.Codes.RateLimited,
                System.Net.HttpStatusCode.ServiceUnavailable => TranslationErrorDetail.Codes.ApiError,
                _ => TranslationErrorDetail.Codes.ApiError
            };

            return ImageTranslationResponse.Failure(
                request.RequestId,
                new TranslationErrorDetail
                {
                    Code = errorCode,
                    Message = $"Gemini APIエラー: {(int)httpResponse.StatusCode}",
                    IsRetryable = (int)httpResponse.StatusCode >= 500 || httpResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                },
                processingTime);
        }

        // 成功レスポンスのパース
        var responseBody = await httpResponse.Content.ReadFromJsonAsync<GeminiResponse>(
            _jsonOptions, cancellationToken).ConfigureAwait(false);

        if (responseBody?.Candidates == null || responseBody.Candidates.Count == 0)
        {
            return ImageTranslationResponse.Failure(
                request.RequestId,
                new TranslationErrorDetail
                {
                    Code = TranslationErrorDetail.Codes.ApiError,
                    Message = "Gemini APIから有効なレスポンスがありませんでした",
                    IsRetryable = false
                },
                processingTime);
        }

        // Geminiのレスポンスからテキストを抽出
        var geminiText = responseBody.Candidates[0].Content?.Parts?[0]?.Text ?? string.Empty;

        // JSONをパース
        var translationResult = ParseTranslationResult(geminiText);

        if (translationResult == null)
        {
            return ImageTranslationResponse.Failure(
                request.RequestId,
                new TranslationErrorDetail
                {
                    Code = TranslationErrorDetail.Codes.ApiError,
                    Message = "翻訳結果のパースに失敗しました",
                    IsRetryable = false
                },
                processingTime);
        }

        // トークン使用量を取得
        var tokenUsage = new TokenUsageDetail
        {
            InputTokens = responseBody.UsageMetadata?.PromptTokenCount ?? 0,
            OutputTokens = responseBody.UsageMetadata?.CandidatesTokenCount ?? 0,
            ImageTokens = 0 // Gemini APIはイメージトークンを個別に報告しない
        };

        return ImageTranslationResponse.Success(
            request.RequestId,
            translationResult.DetectedText ?? string.Empty,
            translationResult.TranslatedText ?? string.Empty,
            ProviderId,
            tokenUsage,
            processingTime,
            translationResult.DetectedLanguage);
    }

    private TranslationResultDto? ParseTranslationResult(string geminiText)
    {
        try
        {
            // Geminiがマークダウンのコードブロックで囲む場合があるので除去
            var jsonText = geminiText.Trim();
            if (jsonText.StartsWith("```json", StringComparison.Ordinal))
            {
                jsonText = jsonText[7..];
            }
            else if (jsonText.StartsWith("```", StringComparison.Ordinal))
            {
                jsonText = jsonText[3..];
            }

            if (jsonText.EndsWith("```", StringComparison.Ordinal))
            {
                jsonText = jsonText[..^3];
            }

            jsonText = jsonText.Trim();

            return JsonSerializer.Deserialize<TranslationResultDto>(jsonText, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "翻訳結果JSONのパース失敗: {Text}", geminiText);
            return null;
        }
    }

    private static string GetLanguageDisplayName(string languageCode)
    {
        return languageCode.ToLowerInvariant() switch
        {
            "ja" => "日本語",
            "en" => "英語",
            "ko" => "韓国語",
            "zh" or "zh-cn" => "中国語（簡体字）",
            "zh-tw" => "中国語（繁体字）",
            "es" => "スペイン語",
            "fr" => "フランス語",
            "de" => "ドイツ語",
            "it" => "イタリア語",
            "pt" => "ポルトガル語",
            "ru" => "ロシア語",
            _ => languageCode
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await Task.CompletedTask.ConfigureAwait(false);
    }

    #region DTOs

    private sealed class TranslationResultDto
    {
        [JsonPropertyName("detected_text")]
        public string? DetectedText { get; set; }

        [JsonPropertyName("detected_language")]
        public string? DetectedLanguage { get; set; }

        [JsonPropertyName("translated_text")]
        public string? TranslatedText { get; set; }
    }

    private sealed class GeminiRequest
    {
        [JsonPropertyName("contents")]
        public List<GeminiContent> Contents { get; set; } = [];

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; set; } = [];
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("inlineData")]
        public GeminiInlineData? InlineData { get; set; }
    }

    private sealed class GeminiInlineData
    {
        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public string Data { get; set; } = string.Empty;
    }

    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.1;

        [JsonPropertyName("maxOutputTokens")]
        public int MaxOutputTokens { get; set; } = 2048;
    }

    private sealed class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }

        [JsonPropertyName("usageMetadata")]
        public GeminiUsageMetadata? UsageMetadata { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    private sealed class GeminiUsageMetadata
    {
        [JsonPropertyName("promptTokenCount")]
        public int PromptTokenCount { get; set; }

        [JsonPropertyName("candidatesTokenCount")]
        public int CandidatesTokenCount { get; set; }

        [JsonPropertyName("totalTokenCount")]
        public int TotalTokenCount { get; set; }
    }

    #endregion
}
