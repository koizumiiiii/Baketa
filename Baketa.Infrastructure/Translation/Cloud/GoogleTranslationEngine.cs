using System.Net;
using System.Net.Http;
using System.Text.Json;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models;
using Baketa.Core.Models.Translation;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;
using LanguagePair = Baketa.Core.Translation.Models.LanguagePair;

namespace Baketa.Infrastructure.Translation.Cloud;

/// <summary>Basic v2 transport. Never logs credentials, response bodies, or translated text.</summary>
public sealed class GoogleTranslationEngine(
    IHttpClientFactory clients,
    IGoogleTranslationSettingsService settings,
    ILogger<GoogleTranslationEngine> logger,
    IGeminiNotificationService? notifications = null,
    TimeProvider? timeProvider = null,
    TimeSpan? operationTimeout = null) : IBatchTranslationEngine, IGoogleTranslationConnectionService
{
    public const string ClientName = "GoogleTranslationByok";
    private const string Endpoint = "https://translation.googleapis.com/language/translate/v2";
    private const int MaxBytes = 99000;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    public static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(15);
    private readonly object _gate = new();
    private long _revision = -1;
    private bool _invalidKey;
    private DateTimeOffset _cooldown;
    private DateTimeOffset _nextNotification;
    public string Name => "GoogleTranslation";
    public string Description => "Google Cloud Translation Basic (v2)";
    public bool RequiresNetwork => true;

    public static string? GetLanguageCode(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);
        return language.IsAutoDetect || language.Code.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? null : Language.FromCode(language.Code).Code;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string key, byte[]? body, CancellationToken ct)
    {
        using var client = clients.CreateClient(ClientName);
        using var message = new HttpRequestMessage(method, method == HttpMethod.Get ? Endpoint + "/languages?target=en" : Endpoint);
        message.Headers.Add("X-Goog-Api-Key", key);
        if (body != null)
        {
            message.Content = new ByteArrayContent(body);
            message.Content.Headers.ContentType = new("application/json");
        }
        return await client.SendAsync(message, ct).ConfigureAwait(false);
    }

    public async Task<GoogleTranslationResult> TestConnectionAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        var result = GoogleTranslationResult.InvalidKey;
        int? status = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(apiKey) && !apiKey.Any(char.IsControl))
            {
                using var response = await SendAsync(HttpMethod.Get, apiKey.Trim(), null, cancellationToken).ConfigureAwait(false);
                status = (int)response.StatusCode;
                result = response.IsSuccessStatusCode ? GoogleTranslationResult.Success : await ClassifyAsync(response, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { result = GoogleTranslationResult.Timeout; }
        catch (HttpRequestException) { result = GoogleTranslationResult.NetworkError; }
        logger.LogInformation("Google Translation {Operation}: {Result}, HTTP {StatusCode}", "Validation", result, status);
        return result;
    }

    public static async Task<GoogleTranslationResult> ClassifyAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        var reasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                foreach (var field in new[] { "details", "errors" })
                    if (error.TryGetProperty(field, out var array) && array.ValueKind == JsonValueKind.Array)
                        foreach (var item in array.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                                reasons.Add(reason.GetString()!);
        }
        catch (JsonException) { }
        if (response.StatusCode == HttpStatusCode.Unauthorized || reasons.Contains("API_KEY_INVALID")) return GoogleTranslationResult.InvalidKey;
        if (response.StatusCode == HttpStatusCode.TooManyRequests || reasons.Overlaps(["rateLimitExceeded", "userRateLimitExceeded", "dailyLimitExceeded", "quotaExceeded"])) return GoogleTranslationResult.RateLimited;
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            if (reasons.Overlaps(["SERVICE_DISABLED", "accessNotConfigured"])) return GoogleTranslationResult.ServiceDisabled;
            if (reasons.Overlaps(["BILLING_DISABLED", "billingNotActive", "billingDisabled"])) return GoogleTranslationResult.BillingDisabled;
            return GoogleTranslationResult.Forbidden;
        }
        return GoogleTranslationResult.Failed;
    }

    private static byte[] Body(IEnumerable<string> texts, string? source, string target)
    {
        var body = new Dictionary<string, object> { ["q"] = texts.ToArray(), ["target"] = target, ["format"] = "text" };
        if (source != null) body["source"] = source;
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    public async Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default) =>
        (await TranslateBatchAsync([request], cancellationToken).ConfigureAwait(false))[0];

    public async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(IReadOnlyList<TranslationRequest> requests, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0) return [];
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = new CancellationTokenSource(operationTimeout ?? DefaultOperationTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var ct = linked.Token;
        long revision = -1;
        try
        {
            string? key;
            lock (_gate)
            {
                revision = settings.ApiKeySaveRevision;
                if (_revision != revision)
                {
                    _revision = revision;
                    _invalidKey = false;
                    _cooldown = default;
                    _nextNotification = default;
                }
                key = settings.GetApiKey();
                if (string.IsNullOrEmpty(key) || _invalidKey || _time.GetUtcNow() < _cooldown)
                    return Fail(requests, GoogleTranslationResult.Failed);
            }

            var results = new TranslationResponse[requests.Count];
            foreach (var group in requests.Select((request, index) => (request, index))
                         .GroupBy(x => (Source: GetLanguageCode(x.request.SourceLanguage), Target: GetLanguageCode(x.request.TargetLanguage))))
            {
                if (group.Key.Target == null) return Fail(requests, GoogleTranslationResult.Failed);
                // Split long individual strings too, without breaking a UTF-16 surrogate pair.
                var pieces = new List<(int Index, string Text)>();
                foreach (var item in group)
                {
                    var text = item.request.SourceText;
                    if (text.Length == 0) pieces.Add((item.index, ""));
                    for (var start = 0; start < text.Length;)
                    {
                        var count = Math.Min(10000, text.Length - start);
                        if (start + count < text.Length && char.IsHighSurrogate(text[start + count - 1])) count--;
                        pieces.Add((item.index, text.Substring(start, count)));
                        start += count;
                    }
                }
                var translated = new Dictionary<int, string>();
                for (var offset = 0; offset < pieces.Count;)
                {
                    var count = Math.Min(128, pieces.Count - offset);
                    var body = Body(pieces.Skip(offset).Take(count).Select(x => x.Text), group.Key.Source, group.Key.Target);
                    while (body.Length > MaxBytes && count > 1)
                    {
                        count = Math.Max(1, count / 2);
                        body = Body(pieces.Skip(offset).Take(count).Select(x => x.Text), group.Key.Source, group.Key.Target);
                    }
                    ct.ThrowIfCancellationRequested();
                    lock (_gate)
                    {
                        if (_revision != revision || settings.ApiKeySaveRevision != revision || _invalidKey || _time.GetUtcNow() < _cooldown)
                            return Fail(requests, GoogleTranslationResult.Failed);
                    }
                    using var response = await SendAsync(HttpMethod.Post, key, body, ct).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        var failure = await ClassifyAsync(response, ct).ConfigureAwait(false);
                        lock (_gate)
                        {
                            // Late responses from a previous key revision must not disable the new key.
                            if (_revision == revision && settings.ApiKeySaveRevision == revision)
                            {
                                _invalidKey |= failure is GoogleTranslationResult.InvalidKey or GoogleTranslationResult.ServiceDisabled or GoogleTranslationResult.BillingDisabled or GoogleTranslationResult.Forbidden;
                                if (failure == GoogleTranslationResult.RateLimited)
                                {
                                    var cooldown = response.Headers.RetryAfter?.Date ?? (_time.GetUtcNow() + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1)));
                                    if (cooldown > _cooldown) _cooldown = cooldown;
                                }
                            }
                        }
                        await NotifyFailureAsync(failure, (int)response.StatusCode, revision, ct).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        return Fail(requests, failure);
                    }
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                    var items = json.RootElement.GetProperty("data").GetProperty("translations");
                    if (items.GetArrayLength() != count) throw new JsonException();
                    for (var i = 0; i < count; i++)
                    {
                        var index = pieces[offset + i].Index;
                        translated.TryGetValue(index, out var previous);
                        translated[index] = previous + (items[i].GetProperty("translatedText").GetString() ?? throw new JsonException());
                    }
                    offset += count;
                }
                foreach (var item in group)
                    results[item.index] = TranslationResponse.CreateSuccess(item.request, translated[item.index], Name, 0);
            }
            ct.ThrowIfCancellationRequested();
            return results;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await NotifyFailureAsync(GoogleTranslationResult.Timeout, null, revision, ct).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Fail(requests, GoogleTranslationResult.Timeout);
        }
        catch (HttpRequestException)
        {
            await NotifyFailureAsync(GoogleTranslationResult.NetworkError, null, revision, ct).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Fail(requests, GoogleTranslationResult.NetworkError);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            await NotifyFailureAsync(GoogleTranslationResult.Failed, null, revision, ct).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Fail(requests, GoogleTranslationResult.Failed);
        }

    }

    private IReadOnlyList<TranslationResponse> Fail(IReadOnlyList<TranslationRequest> requests, GoogleTranslationResult result) =>
        requests.Select(r => TranslationResponse.CreateError(r, new TranslationError { ErrorCode = result.ToString(), Message = "Google Translation unavailable." }, Name)).ToArray();

    private async Task NotifyFailureAsync(GoogleTranslationResult result, int? status, long revision, CancellationToken ct)
    {
        logger.LogWarning("Google Translation {Operation}: {Result}, HTTP {StatusCode}", "Translation", result, status);
        lock (_gate)
        {
            if (_revision != revision || settings.ApiKeySaveRevision != revision || _time.GetUtcNow() < _nextNotification) return;
            _nextNotification = _time.GetUtcNow().AddMinutes(1);
        }
        if (notifications != null)
        {
            try { await notifications.NotifyAsync("Google_Fallback").WaitAsync(ct).ConfigureAwait(false); }
            catch (Exception) { /* Notifications must not prevent local fallback. */ }
        }
    }

    public Task<bool> IsReadyAsync() => Task.FromResult(settings.HasApiKey);
    public Task<bool> InitializeAsync() => IsReadyAsync();
    public Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair) => Task.FromResult(
        GetLanguageCode(languagePair.TargetLanguage) != null);
    public Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync() =>
        Task.FromResult<IReadOnlyCollection<LanguagePair>>(Language.SupportedLanguages.Append(Language.Auto)
            .SelectMany(source => Language.SupportedLanguages.Select(target => new LanguagePair { SourceLanguage = source, TargetLanguage = target })).ToArray());
    public void Dispose() { }
}
