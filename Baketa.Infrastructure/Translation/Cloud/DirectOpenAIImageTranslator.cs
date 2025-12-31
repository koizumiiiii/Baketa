using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baketa.Core.Models;
using Baketa.Core.Settings;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Translation.Cloud;

/// <summary>
/// Direct OpenAI API翻訳クライアント（開発・テスト用）
/// </summary>
/// <remarks>
/// Gemini失敗時のフォールバックとして使用。
/// Relay Serverを経由せずに直接OpenAI APIを呼び出します。
/// </remarks>
public sealed class DirectOpenAIImageTranslator : ICloudImageTranslator
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DirectOpenAIImageTranslator> _logger;
    private readonly CloudTranslationSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions;

    private const string OpenAIApiBaseUrl = "https://api.openai.com/v1";
    private const string DefaultModel = "gpt-4.1-nano";  // 最速・最安・高性能
    private const int DefaultMaxTokens = 2048;
    private const double DefaultTemperature = 0.1;

    private bool _disposed;

    /// <inheritdoc />
    public string ProviderId => "direct-openai";

    /// <inheritdoc />
    public string DisplayName => "OpenAI GPT-4 Vision (Direct API)";

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public DirectOpenAIImageTranslator(
        HttpClient httpClient,
        IOptions<CloudTranslationSettings> settings,
        ILogger<DirectOpenAIImageTranslator> logger)
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
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.UseDirectApiMode)
        {
            _logger.LogDebug("Direct APIモードが無効です");
            return Task.FromResult(false);
        }

        if (string.IsNullOrEmpty(_settings.DirectOpenAIApiKey))
        {
            _logger.LogDebug("Direct OpenAI APIキーが設定されていません");
            return Task.FromResult(false);
        }

        // APIキーが設定されていれば利用可能とみなす
        return Task.FromResult(true);
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

        if (string.IsNullOrEmpty(_settings.DirectOpenAIApiKey))
        {
            return ImageTranslationResponse.Failure(
                request.RequestId,
                new TranslationErrorDetail
                {
                    Code = TranslationErrorDetail.Codes.ApiError,
                    Message = "Direct OpenAI APIキーが設定されていません",
                    IsRetryable = false
                },
                TimeSpan.Zero);
        }

        var startTime = DateTime.UtcNow;

        // デバッグ: 使用中のAPIキーの先頭を出力
        var keyPreview = _settings.DirectOpenAIApiKey?.Length > 20
            ? _settings.DirectOpenAIApiKey[..20] + "..."
            : "(empty or short)";
        _logger.LogDebug(
            "Direct OpenAI翻訳開始: RequestId={RequestId}, Target={Target}, KeyPreview={KeyPreview}",
            request.RequestId,
            request.TargetLanguage,
            keyPreview);

        try
        {
            var response = await SendOpenAIRequestAsync(request, cancellationToken).ConfigureAwait(false);
            var processingTime = DateTime.UtcNow - startTime;

            if (response.IsSuccess)
            {
                _logger.LogInformation(
                    "Direct OpenAI翻訳成功: RequestId={RequestId}, Duration={Duration}ms",
                    request.RequestId,
                    processingTime.TotalMilliseconds);
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Direct OpenAI API通信エラー: RequestId={RequestId}", request.RequestId);

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
            _logger.LogWarning(ex, "Direct OpenAI APIタイムアウト: RequestId={RequestId}", request.RequestId);

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
            _logger.LogDebug("Direct OpenAI翻訳がキャンセルされました: RequestId={RequestId}", request.RequestId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct OpenAI翻訳中に予期せぬエラー: RequestId={RequestId}", request.RequestId);

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

    private async Task<ImageTranslationResponse> SendOpenAIRequestAsync(
        ImageTranslationRequest request,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        // OpenAI Chat Completions APIエンドポイント
        var url = $"{OpenAIApiBaseUrl}/chat/completions";

        // プロンプト作成（Gemini推奨の改善版）
        var targetLang = CloudTranslationHelpers.GetLanguageDisplayName(request.TargetLanguage);
        var systemPrompt = @"あなたはゲーム画面のOCRとローカライズを行う専門システムです。
画像内の全てのテキスト要素を一つも漏らさず検出し、JSON形式で出力してください。

### 検出対象（全て検出必須）
1. ダイアログ、会話テキスト
2. UI要素（ボタンラベル、メニュー項目、タブ名）
3. アイテム名、スキル名、キャラクター名
4. 説明文、ツールチップ、ヘルプテキスト
5. タイトル、見出し
6. 背景に含まれるテキスト

### 絶対厳守の制約
- 【重要】複数のテキストが存在する場合、1件だけで終わらせないでください
- 【重要】画面に見えるテキストは全て検出してください
- 小さなテキストや薄い色のテキストも必ず含めてください";

        var userPrompt = $@"この画像からテキストを全て検出し、{targetLang}に翻訳してください。
ゲーム画面には通常、複数のテキスト要素（ボタン、メニュー、ダイアログ等）があります。
全て漏れなく抽出してください。

## 翻訳ガイドライン
- 自然で流暢な{targetLang}の表現を使用
- ゲームUIに適した口調を維持
- 固有名詞はそのまま残すかカタカナ表記

## 出力形式（JSON厳守）
{{
  ""total_detected"": <検出したテキスト総数>,
  ""texts"": [
    {{""original"": ""元テキスト1"", ""translation"": ""翻訳1""}},
    {{""original"": ""元テキスト2"", ""translation"": ""翻訳2""}}
  ],
  ""detected_language"": ""en""
}}

JSONのみを出力。説明文は不要です。";

        // リクエストボディ作成
        var requestBody = new OpenAIRequest
        {
            Model = DefaultModel,
            Messages =
            [
                new OpenAIMessage
                {
                    Role = "system",
                    Content = systemPrompt
                },
                new OpenAIMessage
                {
                    Role = "user",
                    Content = new OpenAIContentPart[]
                    {
                        new OpenAIContentPart { Type = "text", Text = userPrompt },
                        new OpenAIContentPart
                        {
                            Type = "image_url",
                            ImageUrl = new OpenAIImageUrl
                            {
                                Url = $"data:{request.MimeType};base64,{request.ImageBase64}",
                                Detail = "high"
                            }
                        }
                    }
                }
            ],
            MaxTokens = DefaultMaxTokens,
            Temperature = DefaultTemperature
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Content = JsonContent.Create(requestBody, options: _jsonOptions);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.DirectOpenAIApiKey);

        using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var processingTime = DateTime.UtcNow - startTime;

        // エラーレスポンスの処理
        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("OpenAI APIエラー: Status={Status}, Body={Body}",
                httpResponse.StatusCode, errorContent);

            var errorCode = httpResponse.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => TranslationErrorDetail.Codes.SessionInvalid,
                System.Net.HttpStatusCode.PaymentRequired => TranslationErrorDetail.Codes.ApiError,
                System.Net.HttpStatusCode.TooManyRequests => TranslationErrorDetail.Codes.RateLimited,
                System.Net.HttpStatusCode.ServiceUnavailable => TranslationErrorDetail.Codes.ApiError,
                _ => TranslationErrorDetail.Codes.ApiError
            };

            return ImageTranslationResponse.Failure(
                request.RequestId,
                new TranslationErrorDetail
                {
                    Code = errorCode,
                    Message = $"OpenAI APIエラー: {(int)httpResponse.StatusCode}",
                    IsRetryable = (int)httpResponse.StatusCode >= 500 || httpResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                },
                processingTime);
        }

        // 成功レスポンスのパース
        var responseBody = await httpResponse.Content.ReadFromJsonAsync<OpenAIResponse>(
            _jsonOptions, cancellationToken).ConfigureAwait(false);

        if (responseBody?.Choices == null || responseBody.Choices.Count == 0)
        {
            return ImageTranslationResponse.Failure(
                request.RequestId,
                new TranslationErrorDetail
                {
                    Code = TranslationErrorDetail.Codes.ApiError,
                    Message = "OpenAI APIから有効なレスポンスがありませんでした",
                    IsRetryable = false
                },
                processingTime);
        }

        // OpenAIのレスポンスからテキストを抽出
        var openAIText = responseBody.Choices[0].Message?.Content ?? string.Empty;

        // デバッグ: 生レスポンスを出力
        _logger.LogDebug("OpenAI生レスポンス: {Response}", openAIText);

        // JSONをパース
        var translationResult = ParseTranslationResult(openAIText);

        if (translationResult == null || translationResult.Texts == null || translationResult.Texts.Count == 0)
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
            InputTokens = responseBody.Usage?.PromptTokens ?? 0,
            OutputTokens = responseBody.Usage?.CompletionTokens ?? 0,
            ImageTokens = 0
        };

        // 複数テキスト対応レスポンス生成
        var translatedTexts = translationResult.Texts
            .Where(t => !string.IsNullOrEmpty(t.Original) || !string.IsNullOrEmpty(t.Translation))
            .Select(t => new TranslatedTextItem
            {
                Original = t.Original ?? string.Empty,
                Translation = t.Translation ?? string.Empty,
                BoundingBox = null // OpenAIはBoundingBoxを返さない
            })
            .ToList();

        if (translatedTexts.Count == 0)
        {
            return ImageTranslationResponse.Failure(
                request.RequestId,
                new TranslationErrorDetail
                {
                    Code = TranslationErrorDetail.Codes.ApiError,
                    Message = "テキストが検出されませんでした",
                    IsRetryable = false
                },
                processingTime);
        }

        _logger.LogDebug(
            "Direct OpenAI翻訳結果: {Count}件のテキストを検出",
            translatedTexts.Count);

        return ImageTranslationResponse.SuccessWithMultipleTexts(
            request.RequestId,
            translatedTexts,
            ProviderId,
            tokenUsage,
            processingTime,
            translationResult.DetectedLanguage);
    }

    /// <summary>
    /// 翻訳結果をパース
    /// </summary>
    private MultiTextTranslationResultDto? ParseTranslationResult(string responseText)
    {
        var jsonText = CloudTranslationHelpers.ExtractJsonFromResponse(responseText);
        if (string.IsNullOrEmpty(jsonText))
        {
            return null;
        }

        try
        {
            var multiResult = JsonSerializer.Deserialize<MultiTextTranslationResultDto>(
                jsonText, CloudTranslationHelpers.SnakeCaseJsonOptions);
            if (multiResult?.Texts != null)
            {
                return multiResult;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "翻訳結果JSONのパース失敗: {Text}", responseText);
        }

        // フォールバック: レスポンス全体を単一テキストとして扱う
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            _logger.LogWarning("非JSONレスポンスをフォールバック処理: {Text}", responseText);
            return new MultiTextTranslationResultDto
            {
                Texts =
                [
                    new TextItemDto
                    {
                        Original = string.Empty,
                        Translation = responseText.Trim()
                    }
                ],
                DetectedLanguage = null
            };
        }

        return null;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    #region DTOs

    private sealed class MultiTextTranslationResultDto
    {
        [JsonPropertyName("total_detected")]
        public int? TotalDetected { get; set; }

        [JsonPropertyName("texts")]
        public List<TextItemDto>? Texts { get; set; }

        [JsonPropertyName("detected_language")]
        public string? DetectedLanguage { get; set; }
    }

    private sealed class TextItemDto
    {
        [JsonPropertyName("original")]
        public string? Original { get; set; }

        [JsonPropertyName("translation")]
        public string? Translation { get; set; }
    }

    private sealed class OpenAIRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<OpenAIMessage> Messages { get; set; } = [];

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }
    }

    private sealed class OpenAIMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public object? Content { get; set; }
    }

    private sealed class OpenAIContentPart
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("image_url")]
        public OpenAIImageUrl? ImageUrl { get; set; }
    }

    private sealed class OpenAIImageUrl
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("detail")]
        public string Detail { get; set; } = "auto";
    }

    private sealed class OpenAIResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("choices")]
        public List<OpenAIChoice>? Choices { get; set; }

        [JsonPropertyName("usage")]
        public OpenAIUsage? Usage { get; set; }
    }

    private sealed class OpenAIChoice
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("message")]
        public OpenAIMessageContent? Message { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private sealed class OpenAIMessageContent
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private sealed class OpenAIUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }

    #endregion
}
