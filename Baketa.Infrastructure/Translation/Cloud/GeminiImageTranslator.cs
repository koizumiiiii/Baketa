using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Baketa.Infrastructure.Translation.Cloud;

/// <summary>Direct BYOK translation. No relay, session, license or token balance is consulted.</summary>
public sealed class GeminiImageTranslator(
    IHttpClientFactory httpClientFactory,
    IGeminiSettingsService settings,
    IGeminiNotificationService? notifications = null,
    TimeProvider? timeProvider = null,
    ILogger<GeminiImageTranslator>? logger = null) : ICloudImageTranslator, IGeminiConnectionService
{
    public const string ClientName = "GeminiByok";
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _withoutThinking = [];
    private long _revision = -1;
    private long _apiKeySaveRevision = -1;
    private string? _apiKey;
    private string? _resolvedModel;
    private DateTimeOffset _cooldown;
    private DateTimeOffset _nextNotification;
    private DateTimeOffset _nextAuthNotification;
    private bool _invalidKey;
    public string ProviderId => "primary";
    public string DisplayName => "Gemini";
    public bool ManagesOwnCooldown => true;

    private void Refresh()
    {
        var saveRevision = settings.ApiKeySaveRevision;
        if (_apiKeySaveRevision != saveRevision)
        {
            _apiKeySaveRevision = saveRevision;
            ResetAuthenticationError();
        }
        if (_revision == settings.Revision) return;
        _revision = settings.Revision;
        _resolvedModel = null;
        var key = settings.GetApiKey();
        if (string.Equals(_apiKey, key, StringComparison.Ordinal)) return;
        _apiKey = key;
        ResetAuthenticationError();
        _cooldown = default;
        _nextNotification = default;
    }

    private void ResetAuthenticationError()
    {
        _invalidKey = false;
        _nextAuthNotification = default;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Refresh();
            return settings.HasApiKey && settings.PreferGemini && !_invalidKey && _time.GetUtcNow() >= _cooldown;
        }
        finally { _gate.Release(); }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string key, object? body, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient(ClientName);
        using var message = new HttpRequestMessage(method, "https://generativelanguage.googleapis.com/v1beta/" + path);
        message.Headers.Add("x-goog-api-key", key);
        if (body != null) message.Content = JsonContent.Create(body);
        return await client.SendAsync(message, ct).ConfigureAwait(false);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Refresh();
            var key = settings.GetApiKey();
            if (string.IsNullOrEmpty(key)) return false;
            try
            {
                using var response = await SendAsync(HttpMethod.Get, "models", key, null, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return false;
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                var success = json.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array;
                if (success && string.Equals(key, settings.GetApiKey(), StringComparison.Ordinal))
                    ResetAuthenticationError();
                return success;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
            catch (Exception ex) when (ex is HttpRequestException or JsonException) { return false; }
        }
        finally { _gate.Release(); }
    }

    public async Task<ImageTranslationResponse> TranslateImageAsync(ImageTranslationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var started = _time.GetUtcNow();
        string? notification = null;
        try
        {
            Refresh();
            var key = settings.GetApiKey();
            if (string.IsNullOrEmpty(key) || !settings.PreferGemini || _invalidKey || started < _cooldown)
                return Failure(request, "GEMINI_UNAVAILABLE", started);
            var model = _resolvedModel ?? settings.Model;
            var discovered = false;
            while (true)
            {
                var thinking = !_withoutThinking.Contains(model);
                using var response = await SendAsync(HttpMethod.Post, "models/" + Uri.EscapeDataString(model) + ":generateContent",
                    key, CreateBody(request, model, thinking), cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound && !discovered)
                {
                    discovered = true;
                    var (replacement, rateLimited) = await FindModelAsync(key, cancellationToken).ConfigureAwait(false);
                    if (rateLimited)
                    {
                        notification = "Gemini_RateLimited";
                        return Failure(request, TranslationErrorDetail.Codes.RateLimited, started);
                    }
                    if (replacement != null) { model = _resolvedModel = replacement; continue; }
                }
                // Some aliases change model families. Retry once without unsupported thinking settings.
                if (response.StatusCode == HttpStatusCode.BadRequest && thinking && body.Contains("thinking", StringComparison.OrdinalIgnoreCase))
                {
                    _withoutThinking.Add(model);
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    SetRateLimitCooldown(response, body);
                    notification = "Gemini_RateLimited";
                    return Failure(request, TranslationErrorDetail.Codes.RateLimited, started);
                }
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ||
                    (response.StatusCode == HttpStatusCode.BadRequest && body.Contains("API_KEY_INVALID", StringComparison.Ordinal)))
                {
                    _invalidKey = true;
                    notification = "Gemini_InvalidKey";
                    return Failure(request, "GEMINI_INVALID_KEY", started);
                }
                if (!response.IsSuccessStatusCode)
                {
                    notification = "Gemini_Failed";
                    return Failure(request, TranslationErrorDetail.Codes.ApiError, started);
                }
                return Parse(request, body, _time.GetUtcNow() - started);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            notification = "Gemini_Failed";
            return Failure(request, TranslationErrorDetail.Codes.Timeout, started);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or IndexOutOfRangeException)
        {
            // Do not log exception details: HTTP bodies can echo credentials or screen text.
            notification = "Gemini_Failed";
            return Failure(request, TranslationErrorDetail.Codes.ApiError, started);
        }
        finally
        {
            // Reserve notification throttling under the gate, but never await UI lifetime here.
            var authNotification = notification == "Gemini_InvalidKey";
            var notify = notification != null && _time.GetUtcNow() >= (authNotification ? _nextAuthNotification : _nextNotification);
            if (notify)
            {
                if (authNotification) _nextAuthNotification = _time.GetUtcNow().AddMinutes(1);
                else _nextNotification = _time.GetUtcNow().AddMinutes(1);
            }
            _gate.Release();
            if (notify) _ = NotifySafelyAsync(notification!);
        }
    }

    private async Task NotifySafelyAsync(string resourceKey)
    {
        try
        {
            if (notifications != null) await notifications.NotifyAsync(resourceKey).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Do not expose exception messages that could contain credentials.
            (logger ?? NullLogger<GeminiImageTranslator>.Instance).LogWarning("Gemini notification failed: {ResourceKey}", resourceKey);
        }
    }

    private ImageTranslationResponse Failure(ImageTranslationRequest request, string code, DateTimeOffset started)
    {
        // Preserve the existing backoff for ordinary API failures; rate limits use Retry-After.
        if (code is TranslationErrorDetail.Codes.ApiError or TranslationErrorDetail.Codes.Timeout)
            _cooldown = _time.GetUtcNow().AddMinutes(5);
        return ImageTranslationResponse.Failure(request.RequestId, new() { Code = code, Message = "Gemini unavailable; use local translation.", IsRetryable = false }, _time.GetUtcNow() - started);
    }

    private void SetRateLimitCooldown(HttpResponseMessage response, string body)
    {
        var now = _time.GetUtcNow();
        _cooldown = now + RetryDelay(response, body, now);
    }

    private async Task<(string? Model, bool RateLimited)> FindModelAsync(string key, CancellationToken ct)
    {
        string? page = null;
        var candidates = new List<string>();
        do
        {
            using var response = await SendAsync(HttpMethod.Get, "models" + (page == null ? "" : "?pageToken=" + Uri.EscapeDataString(page)), key, null, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                SetRateLimitCooldown(response, body);
                return (null, true);
            }
            if (!response.IsSuccessStatusCode) return (null, false);
            using var json = JsonDocument.Parse(body);
            foreach (var item in json.RootElement.GetProperty("models").EnumerateArray())
            {
                var name = item.GetProperty("name").GetString() ?? "";
                string[] excluded = ["tts", "image", "audio", "live", "omni", "preview"];
                if (name.StartsWith("models/", StringComparison.Ordinal) && name.Contains("flash", StringComparison.OrdinalIgnoreCase) &&
                    !excluded.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase)) &&
                    item.TryGetProperty("supportedGenerationMethods", out var methods) && methods.EnumerateArray().Any(x => x.GetString() == "generateContent"))
                    candidates.Add(name[7..]);
            }
            page = json.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (!string.IsNullOrEmpty(page));
        return (candidates.OrderByDescending(x => x.Contains("lite", StringComparison.OrdinalIgnoreCase) == settings.Model.Contains("lite", StringComparison.Ordinal))
            .ThenByDescending(x => x, StringComparer.Ordinal).FirstOrDefault(), false);
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response, string body, DateTimeOffset now)
    {
        var delay = response.Headers.RetryAfter?.Delta ??
            (response.Headers.RetryAfter?.Date - now) ?? TimeSpan.Zero;
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("details", out var details))
                foreach (var item in details.EnumerateArray())
                    if (item.TryGetProperty("retryDelay", out var retry) &&
                        double.TryParse(retry.GetString()?.TrimEnd('s'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                        double.IsFinite(seconds) && seconds > 0 && seconds < TimeSpan.MaxValue.TotalSeconds)
                        delay = TimeSpan.FromSeconds(Math.Max(delay.TotalSeconds, seconds));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException) { }
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromMinutes(1);
    }

    private static object CreateBody(ImageTranslationRequest request, string model, bool thinking)
    {
        var schema = JsonNode.Parse("""
            {"type":"OBJECT","properties":{"texts":{"type":"ARRAY","maxItems":20,"items":{"type":"OBJECT","properties":{"is_legible":{"type":"BOOLEAN"},"original":{"type":"STRING","description":"Readable characters only. No tabs, padding or control characters."},"translation":{"type":"STRING"},"bounding_box":{"type":"ARRAY","items":{"type":"NUMBER"},"minItems":4,"maxItems":4},"confidence_score":{"type":"NUMBER"}},"required":["is_legible","original","translation","bounding_box","confidence_score"]}},"detected_language":{"type":"STRING"}},"required":["texts"]}
            """);
        var config = new Dictionary<string, object?>
        {
            ["temperature"] = 0.1, ["maxOutputTokens"] = 8192,
            ["responseMimeType"] = "application/json", ["responseSchema"] = schema
        };
        if (thinking)
            config["thinkingConfig"] = model.StartsWith("gemini-2.5", StringComparison.Ordinal)
                ? new { thinkingBudget = 0 } : (object)new { thinkingLevel = "minimal" };
        return new
        {
            contents = new[] { new { parts = new object[] { new { text = GeminiPrompt.Create(request) }, new { inlineData = new { mimeType = request.MimeType, data = request.ImageBase64 } } } } },
            generationConfig = config
        };
    }

    private static ImageTranslationResponse Parse(ImageTranslationRequest request, string body, TimeSpan elapsed)
    {
        using var envelope = JsonDocument.Parse(body);
        var candidate = envelope.RootElement.GetProperty("candidates")[0];
        if (candidate.TryGetProperty("finishReason", out var finish) && finish.GetString() != "STOP") throw new JsonException();
        var parts = candidate.GetProperty("content").GetProperty("parts").EnumerateArray();
        var text = string.Concat(parts.Where(p => !p.TryGetProperty("thought", out var thought) || !thought.GetBoolean())
            .Where(p => p.TryGetProperty("text", out _)).Select(p => p.GetProperty("text").GetString()));
        using var json = JsonDocument.Parse(text);
        var items = new List<TranslatedTextItem>();
        foreach (var item in json.RootElement.GetProperty("texts").EnumerateArray().Take(20))
        {
            if (!item.GetProperty("is_legible").GetBoolean()) continue;
            var box = item.GetProperty("bounding_box").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            if (box.Length != 4 || box.Any(x => !double.IsFinite(x) || x < 0 || x > 1000) || box[2] <= box[0] || box[3] <= box[1]) throw new JsonException();
            // Match RelayServerClient: keep the 0-1000 scale; the consumer converts to pixels.
            var x = (int)Math.Round(box[1]);
            var y = (int)Math.Round(box[0]);
            items.Add(new()
            {
                Original = item.GetProperty("original").GetString() ?? throw new JsonException(),
                Translation = item.GetProperty("translation").GetString() ?? throw new JsonException(),
                ConfidenceScore = item.GetProperty("confidence_score").GetSingle(),
                BoundingBox = new Int32Rect(x, y, (int)Math.Round(box[3]) - x, (int)Math.Round(box[2]) - y)
            });
        }
        return ImageTranslationResponse.SuccessWithMultipleTexts(request.RequestId, items, "primary", TokenUsageDetail.Empty, elapsed,
            json.RootElement.TryGetProperty("detected_language", out var language) ? language.GetString() : null);
    }
    public ValueTask DisposeAsync() { _gate.Dispose(); return ValueTask.CompletedTask; }
}
