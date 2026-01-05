using System.Net.Http;
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

        // Gemini APIエンドポイント（APIキーはヘッダーで送信）
        var url = $"{GeminiApiBaseUrl}/models/{DefaultModel}:generateContent";

        // プロンプト作成（OCR + 翻訳を1回で実行）- Issue #242: 複数テキスト対応 + Phase 2座標
        var targetLang = CloudTranslationHelpers.GetLanguageDisplayName(request.TargetLanguage);
        var prompt = $@"あなたはゲームローカライズの専門家です。この画像に含まれる全てのテキストを検出し、{targetLang}に翻訳してください。

## 翻訳ガイドライン
- 直訳ではなく、自然で流暢な{targetLang}の表現を使用してください
- ゲームのUIやダイアログに適した口調を維持してください
- 固有名詞（キャラクター名、地名など）はそのまま残すか、一般的なカタカナ表記にしてください
- 文脈に応じて適切な敬語レベルを選択してください

## 出力形式
複数のテキストがある場合は全て含めてください。
各テキストのバウンディングボックス座標も含めてください。

以下のJSON形式で回答してください：
{{
  ""texts"": [
    {{
      ""original"": ""original_text_1"",
      ""translation"": ""translated_text_1"",
      ""bounding_box"": [y_min, x_min, y_max, x_max]
    }}
  ],
  ""detected_language"": ""en, ja, ko, etc.""
}}

bounding_boxは0-1000スケールの正規化座標で、[y_min, x_min, y_max, x_max]の順序です。
テキストが検出されない場合は、textsを空配列[]にしてください。
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

        // APIキーをヘッダーで送信（URLに含めるとログに記録されるリスクがあるため）
        httpRequest.Headers.Add("x-goog-api-key", _settings.DirectGeminiApiKey);

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
            InputTokens = responseBody.UsageMetadata?.PromptTokenCount ?? 0,
            OutputTokens = responseBody.UsageMetadata?.CandidatesTokenCount ?? 0,
            ImageTokens = 0 // Gemini APIはイメージトークンを個別に報告しない
        };

        // Issue #242: 複数テキスト対応レスポンス生成 + Phase 2座標統合
        var translatedTexts = translationResult.Texts
            .Where(t => !string.IsNullOrEmpty(t.Original) || !string.IsNullOrEmpty(t.Translation))
            .Select(t => new TranslatedTextItem
            {
                Original = t.Original ?? string.Empty,
                Translation = t.Translation ?? string.Empty,
                BoundingBox = ConvertToPixelRect(t.BoundingBox, request.Width, request.Height)
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

#if DEBUG
        _logger.LogInformation(
            "翻訳成功: RequestId={RequestId}, Provider={Provider}, Input={InputTokens}, Output={OutputTokens}, Total={TotalTokens}, TextCount={TextCount}",
            request.RequestId,
            ProviderId,
            tokenUsage.InputTokens,
            tokenUsage.OutputTokens,
            tokenUsage.TotalTokens,
            translatedTexts.Count);
#endif

        return ImageTranslationResponse.SuccessWithMultipleTexts(
            request.RequestId,
            translatedTexts,
            ProviderId,
            tokenUsage,
            processingTime,
            translationResult.DetectedLanguage);
    }

    /// <summary>
    /// 複数テキスト翻訳結果をパース（Issue #242）
    /// </summary>
    internal MultiTextTranslationResultDto? ParseTranslationResult(string geminiText)
    {
        var jsonText = CloudTranslationHelpers.ExtractJsonFromResponse(geminiText);
        if (string.IsNullOrEmpty(jsonText))
        {
            return null;
        }

        try
        {
            // まず新形式（複数テキスト）でパースを試みる
            var multiResult = JsonSerializer.Deserialize<MultiTextTranslationResultDto>(
                jsonText, CloudTranslationHelpers.SnakeCaseJsonOptions);
            if (multiResult?.Texts != null)
            {
                return multiResult;
            }
        }
        catch (JsonException)
        {
            // 新形式でパース失敗、旧形式を試みる
        }

        // フォールバック: 旧形式（単一テキスト）でパースを試みる
        try
        {
            var legacyResult = JsonSerializer.Deserialize<LegacyTranslationResultDto>(
                jsonText, CloudTranslationHelpers.SnakeCaseJsonOptions);
            if (legacyResult != null && !string.IsNullOrEmpty(legacyResult.DetectedText))
            {
                _logger.LogDebug("旧形式JSONからのパースにフォールバック");
                return new MultiTextTranslationResultDto
                {
                    Texts =
                    [
                        new TextItemDto
                        {
                            Original = legacyResult.DetectedText,
                            Translation = legacyResult.TranslatedText
                        }
                    ],
                    DetectedLanguage = legacyResult.DetectedLanguage
                };
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "翻訳結果JSONのパース失敗: {Text}", geminiText);
        }

        // 最終フォールバック: レスポンス全体を単一テキストとして扱う
        if (!string.IsNullOrWhiteSpace(geminiText))
        {
            _logger.LogWarning("非JSONレスポンスをフォールバック処理: {Text}", geminiText);
            return new MultiTextTranslationResultDto
            {
                Texts =
                [
                    new TextItemDto
                    {
                        Original = string.Empty,
                        Translation = geminiText.Trim()
                    }
                ],
                DetectedLanguage = null
            };
        }

        return null;
    }

    /// <summary>
    /// 正規化座標(0-1000)をピクセル座標に変換
    /// Phase 2: 座標ベースオーバーレイ表示用
    /// </summary>
    /// <param name="box">正規化座標 [y_min, x_min, y_max, x_max]</param>
    /// <param name="imageWidth">画像幅（ピクセル）</param>
    /// <param name="imageHeight">画像高さ（ピクセル）</param>
    /// <returns>ピクセル座標のInt32Rect、無効な場合はnull</returns>
    internal static Int32Rect? ConvertToPixelRect(int[]? box, int imageWidth, int imageHeight)
    {
        // 無効な入力をチェック
        if (box is not { Length: 4 } || imageWidth <= 0 || imageHeight <= 0)
        {
            return null;
        }

        // Gemini座標形式: [y_min, x_min, y_max, x_max] (0-1000スケール)
        int yMin = box[0], xMin = box[1], yMax = box[2], xMax = box[3];

        // 座標値の妥当性チェック
        if (yMin < 0 || xMin < 0 || yMax > 1000 || xMax > 1000 || yMin >= yMax || xMin >= xMax)
        {
            return null;
        }

        // ピクセル座標に変換
        return new Int32Rect(
            X: xMin * imageWidth / 1000,
            Y: yMin * imageHeight / 1000,
            Width: (xMax - xMin) * imageWidth / 1000,
            Height: (yMax - yMin) * imageHeight / 1000
        );
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await Task.CompletedTask.ConfigureAwait(false);
    }

    #region DTOs

    /// <summary>
    /// 複数テキスト翻訳結果DTO（Issue #242）
    /// </summary>
    internal sealed class MultiTextTranslationResultDto
    {
        [JsonPropertyName("texts")]
        public List<TextItemDto>? Texts { get; set; }

        [JsonPropertyName("detected_language")]
        public string? DetectedLanguage { get; set; }
    }

    /// <summary>
    /// 個別テキストアイテムDTO
    /// </summary>
    internal sealed class TextItemDto
    {
        [JsonPropertyName("original")]
        public string? Original { get; set; }

        [JsonPropertyName("translation")]
        public string? Translation { get; set; }

        /// <summary>
        /// バウンディングボックス [y_min, x_min, y_max, x_max] (0-1000スケール)
        /// Phase 2: 座標ベースオーバーレイ表示用
        /// </summary>
        [JsonPropertyName("bounding_box")]
        public int[]? BoundingBox { get; set; }
    }

    /// <summary>
    /// 旧形式（単一テキスト）のDTO - フォールバック用
    /// </summary>
    internal sealed class LegacyTranslationResultDto
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
