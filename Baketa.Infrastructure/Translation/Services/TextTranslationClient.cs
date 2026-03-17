using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// [Issue #542] Relay Server /api/translate-text クライアント
/// DeepL Free → Google Free → NLLBフォールバック指示のテキスト翻訳
/// </summary>
public sealed class TextTranslationClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TextTranslationClient> _logger;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TextTranslationClient(
        ILogger<TextTranslationClient> logger,
        string? baseUrl = null)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        _logger = logger;
        _baseUrl = baseUrl ?? "https://baketa-relay.suke009.workers.dev";
    }

    /// <summary>
    /// テキスト翻訳を試行。DeepL/Google Freeで翻訳できればその結果を返す。
    /// 枠不足/エラー時はnullを返す（NLLBにフォールバック）。
    /// </summary>
    public async Task<TextTranslationResult?> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requestBody = new
            {
                text,
                source_language = sourceLanguage,
                target_language = targetLanguage,
                request_id = Guid.NewGuid().ToString()
            };

            using var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/api/translate-text",
                requestBody,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("[Issue #542] Relay Server応答エラー: {StatusCode}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<TextTranslateResponse>(
                JsonOptions, cancellationToken).ConfigureAwait(false);

            if (result == null)
                return null;

            if (result.Success && !string.IsNullOrEmpty(result.TranslatedText))
            {
                _logger.LogDebug("[Issue #542] テキスト翻訳成功: engine={Engine}, text='{Text}'",
                    result.Engine, result.TranslatedText?.Length > 30 ? result.TranslatedText[..30] + "..." : result.TranslatedText);

                return new TextTranslationResult
                {
                    TranslatedText = result.TranslatedText,
                    Engine = result.Engine ?? "unknown"
                };
            }

            // fallback: "local" → NLLBにフォールバック
            if (result.Fallback == "local")
            {
                _logger.LogDebug("[Issue #542] テキスト翻訳フォールバック: NLLBを使用");
            }

            return null;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "[Issue #542] Relay Server接続エラー（NLLBにフォールバック）");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Issue #542] テキスト翻訳エラー（NLLBにフォールバック）");
            return null;
        }
    }

    /// <summary>
    /// [Issue #542] テキスト翻訳サービスが利用可能かチェック（疎通確認）
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 短いテキストでDeepL/Googleの疎通確認
            var result = await TranslateAsync("hello", "en", "ja", cancellationToken).ConfigureAwait(false);
            return result != null;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed class TextTranslateResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("translated_text")]
        public string? TranslatedText { get; set; }

        [JsonPropertyName("engine")]
        public string? Engine { get; set; }

        [JsonPropertyName("fallback")]
        public string? Fallback { get; set; }

        [JsonPropertyName("request_id")]
        public string? RequestId { get; set; }
    }
}

/// <summary>
/// テキスト翻訳結果
/// </summary>
public sealed class TextTranslationResult
{
    public required string TranslatedText { get; init; }
    public required string Engine { get; init; }
}
