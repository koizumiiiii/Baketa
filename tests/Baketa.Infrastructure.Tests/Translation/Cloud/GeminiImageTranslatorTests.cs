using Baketa.Core.Models;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Translation.Cloud;
using Baketa.Infrastructure.Translation.Services;
using Baketa.Core.Abstractions.License;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Cloud;

public sealed class GeminiImageTranslatorTests
{
    private const string FakeKey = "test-only-credential";
    private static ImageTranslationRequest Request => ImageTranslationRequest.FromBytes([1], "ja", "", 2000, 1000);
    private static string SuccessBody => JsonSerializer.Serialize(new
    {
        candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = """
            {"texts":[{"is_legible":true,"original":"Hello","translation":"縺薙ｓ縺ｫ縺｡縺ｯ","bounding_box":[100,200,300,400],"confidence_score":0.95}],"detected_language":"en"}
            """ } } } } }
    });

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? RequestBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("generativelanguage.googleapis.com", request.RequestUri!.Host);
            Assert.DoesNotContain(FakeKey, request.RequestUri.ToString());
            Assert.Equal(FakeKey, Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            if (request.Content != null) RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return action(request);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static Mock<IGeminiSettingsService> Settings()
    {
        var settings = new Mock<IGeminiSettingsService>();
        settings.SetupGet(x => x.HasApiKey).Returns(true);
        settings.SetupGet(x => x.PreferGemini).Returns(true);
        settings.SetupGet(x => x.Model).Returns("gemini-flash-lite-latest");
        settings.Setup(x => x.GetApiKey()).Returns(FakeKey);
        return settings;
    }

    private static GeminiImageTranslator Create(Handler handler, IGeminiSettingsService? settings = null, TimeProvider? clock = null, IGeminiNotificationService? notifications = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(GeminiImageTranslator.ClientName)).Returns(() => new HttpClient(handler, false));
        return new(factory.Object, settings ?? Settings().Object, notifications, clock);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    [Fact]
    public async Task TranslateWithFallbackAsync_RetryAfterExpires_ResumesGeminiWhileNotificationIsPending()
    {
        var clock = new Clock();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = new Mock<IGeminiNotificationService>();
        notifications.Setup(x => x.NotifyAsync(It.IsAny<string>())).Returns(pending.Task);
        var calls = 0;
        using var handler = new Handler(_ =>
        {
            if (++calls > 1) return Response(HttpStatusCode.OK, SuccessBody);
            var response = Response(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(10));
            return response;
        });
        await using var translator = Create(handler, clock: clock, notifications: notifications.Object);
        await using var provider = new ServiceCollection()
            .AddKeyedSingleton<ICloudImageTranslator>("primary", translator).BuildServiceProvider();
        var status = new EngineStatusManager(NullLogger<EngineStatusManager>.Instance);
        var orchestrator = new FallbackOrchestrator(provider, status, Mock.Of<ILicenseManager>(), NullLogger<FallbackOrchestrator>.Instance);
        try
        {
            Assert.False((await orchestrator.TranslateWithFallbackAsync(Request).WaitAsync(TimeSpan.FromSeconds(2))).IsSuccess);
            clock.Now += TimeSpan.FromSeconds(9);
            Assert.False((await orchestrator.TranslateWithFallbackAsync(Request).WaitAsync(TimeSpan.FromSeconds(2))).IsSuccess);
            Assert.Equal(1, handler.Calls);
            clock.Now += TimeSpan.FromSeconds(1);
            Assert.True((await orchestrator.TranslateWithFallbackAsync(Request).WaitAsync(TimeSpan.FromSeconds(2))).IsSuccess);
            Assert.Equal(2, handler.Calls);
            Assert.False(pending.Task.IsCompleted);
        }
        finally { pending.SetResult(); }
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TranslateImageAsync_PendingNotification_ReleasesGateAndReturnsFailure(HttpStatusCode status)
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = new Mock<IGeminiNotificationService>();
        notifications.Setup(x => x.NotifyAsync(It.IsAny<string>())).Returns(pending.Task);
        using var handler = new Handler(_ => Response(status, "{}"));
        await using var translator = Create(handler, notifications: notifications.Object);
        try
        {
            Assert.False((await translator.TranslateImageAsync(Request).WaitAsync(TimeSpan.FromSeconds(2))).IsSuccess);
            Assert.False(await translator.IsAvailableAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally { pending.SetResult(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TranslateImageAsync_NotificationThrows_LogsSafeWarningAndReturnsFailure(bool synchronous)
    {
        var notifications = new Mock<IGeminiNotificationService>();
        notifications.Setup(x => x.NotifyAsync(It.IsAny<string>())).Returns(() => synchronous
            ? throw new InvalidOperationException(FakeKey)
            : Task.FromException(new InvalidOperationException(FakeKey)));
        var logger = new Mock<ILogger<GeminiImageTranslator>>();
        using var handler = new Handler(_ => throw new HttpRequestException());
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
        await using var translator = new GeminiImageTranslator(factory.Object, Settings().Object, notifications.Object, logger: logger.Object);
        Assert.False((await translator.TranslateImageAsync(Request)).IsSuccess);
        logger.Verify(x => x.Log(LogLevel.Warning, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((value, _) => !value.ToString()!.Contains(FakeKey)),
            null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task TranslateImageAsync_ValidResponse_ConvertsBoundingBoxAndUsesHeader()
    {
        using var handler = new Handler(_ => Response(HttpStatusCode.OK, SuccessBody));
        await using var translator = Create(handler);
        var result = await translator.TranslateImageAsync(Request);
        Assert.True(result.IsSuccess);
        var text = Assert.Single(result.Texts!);
        Assert.Equal("縺薙ｓ縺ｫ縺｡縺ｯ", text.Translation);
        Assert.Equal(new Baketa.Core.Models.Int32Rect(200, 100, 200, 200), text.BoundingBox);
        Assert.Equal("en", result.DetectedLanguage);
    }

    [Fact]
    public async Task TranslateImageAsync_OverTwentyItems_ReturnsFirstTwentyInOrder()
    {
        var texts = Enumerable.Range(0, 21).Select(i => new
        {
            is_legible = true, original = $"Original {i}", translation = $"Translation {i}",
            bounding_box = new[] { 100, 200, 300, 400 }, confidence_score = 0.95
        });
        var body = JsonSerializer.Serialize(new
        {
            candidates = new[] { new { finishReason = "STOP", content = new
            {
                parts = new[] { new { text = JsonSerializer.Serialize(new { texts }) } }
            } } }
        });
        using var handler = new Handler(_ => Response(HttpStatusCode.OK, body));
        await using var translator = Create(handler);

        var result = await translator.TranslateImageAsync(Request);

        Assert.True(result.IsSuccess);
        Assert.Equal(Enumerable.Range(0, 20).Select(i => $"Original {i}"), result.Texts!.Select(t => t.Original));
        Assert.Equal(Enumerable.Range(0, 20).Select(i => $"Translation {i}"), result.Texts!.Select(t => t.Translation));
    }

    [Fact]
    public async Task TranslateImageAsync_ModelNotFound_DiscoversSafeFlashModelAndCachesIt()
    {
        var paths = new List<string>();
        using var handler = new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            paths.Add(path);
            if (path.Contains("latest")) return Response(HttpStatusCode.NotFound, "{}");
            if (request.Method == HttpMethod.Get) return Response(HttpStatusCode.OK, """
                {"models":[{"name":"models/gemini-flash-image","supportedGenerationMethods":["generateContent"]},{"name":"models/gemini-flash-preview","supportedGenerationMethods":["generateContent"]},{"name":"models/gemini-flash-audio","supportedGenerationMethods":["generateContent"]},{"name":"models/gemini-2.5-flash-lite","supportedGenerationMethods":["generateContent"]}]}
                """);
            return Response(HttpStatusCode.OK, SuccessBody);
        });
        await using var translator = Create(handler);
        Assert.True((await translator.TranslateImageAsync(Request)).IsSuccess);
        Assert.True((await translator.TranslateImageAsync(Request)).IsSuccess);
        Assert.Equal(4, handler.Calls);
        Assert.EndsWith("gemini-2.5-flash-lite:generateContent", paths[^1]);
    }

    [Fact]
    public async Task TranslateImageAsync_RateLimited_RespectsLongestDelayAndResumes()
    {
        var clock = new Clock();
        using var handler = new Handler(_ =>
        {
            var response = Response(HttpStatusCode.TooManyRequests, """{"error":{"details":[{"retryDelay":"120s"}]}}""");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
            return response;
        });
        await using var translator = Create(handler, clock: clock);
        Assert.False((await translator.TranslateImageAsync(Request)).IsSuccess);
        clock.Now += TimeSpan.FromSeconds(119);
        Assert.False(await translator.IsAvailableAsync());
        await translator.TranslateImageAsync(Request);
        Assert.Equal(1, handler.Calls);
        clock.Now += TimeSpan.FromSeconds(2);
        Assert.True(await translator.IsAvailableAsync());
        await translator.TranslateImageAsync(Request);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task TranslateImageAsync_InvalidKey_NotifiesOnceUntilKeyChanges(HttpStatusCode status)
    {
        var settings = Settings();
        var notifications = new Mock<IGeminiNotificationService>();
        notifications.Setup(x => x.NotifyAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        using var handler = new Handler(_ => Response(status, FakeKey));
        await using var translator = Create(handler, settings.Object, notifications: notifications.Object);
        var result = await translator.TranslateImageAsync(Request);
        await translator.TranslateImageAsync(Request);
        Assert.False(await translator.IsAvailableAsync());
        Assert.DoesNotContain(FakeKey, result.Error!.Message);
        Assert.Equal(1, handler.Calls);
        notifications.Verify(x => x.NotifyAsync("Gemini_InvalidKey"), Times.Once);
        settings.SetupGet(x => x.Revision).Returns(1);
        Assert.False(await translator.IsAvailableAsync());
        settings.Setup(x => x.GetApiKey()).Returns("replacement-test-credential");
        settings.SetupGet(x => x.Revision).Returns(2);
        Assert.True(await translator.IsAvailableAsync());
    }

    [Theory]
    [InlineData(600, 120, false, false, 600)]
    [InlineData(120, 660, false, true, 660)]
    [InlineData(600, 120, true, false, 600)]
    [InlineData(0, 0, false, false, 60)]
    public async Task TranslateImageAsync_ModelDiscoveryRateLimited_RespectsDelayAndResumes(
        int headerSeconds, int bodySeconds, bool dateHeader, bool secondPage, int expectedSeconds)
    {
        var clock = new Clock();
        var resumeAt = clock.Now.AddSeconds(expectedSeconds);
        using var handler = new Handler(request =>
        {
            if (clock.Now >= resumeAt) return Response(HttpStatusCode.OK, SuccessBody);
            if (request.Method == HttpMethod.Post) return Response(HttpStatusCode.NotFound, "{}");
            if (secondPage && string.IsNullOrEmpty(request.RequestUri!.Query))
                return Response(HttpStatusCode.OK, """{"models":[],"nextPageToken":"page2"}""");
            var response = Response(HttpStatusCode.TooManyRequests,
                JsonSerializer.Serialize(new { error = new { details = new[] { new { retryDelay = $"{bodySeconds}s" } } } }));
            if (headerSeconds > 0)
                response.Headers.RetryAfter = dateHeader
                    ? new RetryConditionHeaderValue(clock.Now.AddSeconds(headerSeconds))
                    : new RetryConditionHeaderValue(TimeSpan.FromSeconds(headerSeconds));
            return response;
        });
        var notifications = new Mock<IGeminiNotificationService>();
        notifications.Setup(x => x.NotifyAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        await using var translator = Create(handler, clock: clock, notifications: notifications.Object);

        var result = await translator.TranslateImageAsync(Request);
        Assert.Equal(TranslationErrorDetail.Codes.RateLimited, result.Error!.Code);
        notifications.Verify(x => x.NotifyAsync("Gemini_RateLimited"), Times.Once);
        clock.Now = resumeAt.AddSeconds(-1);
        Assert.False(await translator.IsAvailableAsync());
        Assert.False((await translator.TranslateImageAsync(Request)).IsSuccess);
        Assert.Equal(secondPage ? 3 : 2, handler.Calls);
        clock.Now = resumeAt;
        Assert.True(await translator.IsAvailableAsync());
        Assert.True((await translator.TranslateImageAsync(Request)).IsSuccess);
        Assert.Equal(secondPage ? 4 : 3, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TranslateImageAsync_SameKeyRecovery_ResumesAndResetsAuthNotification(bool connectionTest)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "byok-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var settings = new GeminiSettingsService(root);
            await settings.SaveApiKeyAsync(FakeKey);
            var forbidden = true;
            using var handler = new Handler(request => request.Method == HttpMethod.Get
                ? Response(HttpStatusCode.OK, "{\"models\":[]}")
                : Response(forbidden ? HttpStatusCode.Forbidden : HttpStatusCode.OK, forbidden ? "{}" : SuccessBody));
            var notifications = new Mock<IGeminiNotificationService>();
            notifications.Setup(x => x.NotifyAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
            await using var translator = Create(handler, settings, new Clock(), notifications.Object);

            Assert.False((await translator.TranslateImageAsync(Request)).IsSuccess);
            Assert.False(await translator.IsAvailableAsync());
            await settings.SetOptionsAsync("gemini-flash-latest", false);
            await settings.SetOptionsAsync(settings.Model, true);
            Assert.False(await translator.IsAvailableAsync());
            var revision = settings.Revision;
            if (connectionTest) Assert.Equal(GeminiConnectionResult.Success, await translator.TestConnectionAsync());
            else await settings.SaveApiKeyAsync(" " + FakeKey + " ");
            Assert.Equal(revision, settings.Revision);
            Assert.True(await translator.IsAvailableAsync());
            forbidden = false;
            Assert.True((await translator.TranslateImageAsync(Request)).IsSuccess);

            forbidden = true;
            Assert.False((await translator.TranslateImageAsync(Request)).IsSuccess);
            notifications.Verify(x => x.NotifyAsync("Gemini_InvalidKey"), Times.Exactly(2));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TranslateImageAsync_SameKeyRecovery_PreservesCooldownAndNotificationThrottle(bool connectionTest)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "byok-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var settings = new GeminiSettingsService(root);
            await settings.SaveApiKeyAsync(FakeKey);
            var clock = new Clock();
            using var handler = new Handler(request =>
            {
                if (request.Method == HttpMethod.Get) return Response(HttpStatusCode.OK, "{\"models\":[]}");
                var response = Response(HttpStatusCode.TooManyRequests, "{}");
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(10));
                return response;
            });
            var notifications = new Mock<IGeminiNotificationService>();
            notifications.Setup(x => x.NotifyAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
            await using var translator = Create(handler, settings, clock, notifications.Object);
            await translator.TranslateImageAsync(Request);
            await settings.SaveApiKeyAsync(FakeKey);
            if (connectionTest) Assert.Equal(GeminiConnectionResult.Success, await translator.TestConnectionAsync());
            Assert.False(await translator.IsAvailableAsync());
            await settings.SetOptionsAsync(settings.Model, false);
            Assert.False(await translator.IsAvailableAsync());
            await settings.SetOptionsAsync("gemini-flash-latest", true);
            Assert.False(await translator.IsAvailableAsync());
            clock.Now += TimeSpan.FromSeconds(9);
            await translator.TranslateImageAsync(Request);
            Assert.Equal(connectionTest ? 2 : 1, handler.Calls);
            clock.Now += TimeSpan.FromSeconds(1);
            Assert.True(await translator.IsAvailableAsync());
            await translator.TranslateImageAsync(Request);
            Assert.Equal(connectionTest ? 3 : 2, handler.Calls);
            notifications.Verify(x => x.NotifyAsync("Gemini_RateLimited"), Times.Once);
            await settings.SaveApiKeyAsync("replacement-test-credential");
            Assert.True(await translator.IsAvailableAsync());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("invalid json")]
    [InlineData("{}")]
    [InlineData("{\"candidates\":[]}")]
    public async Task TranslateImageAsync_InvalidJson_ReturnsFailure(string body)
    {
        using var handler = new Handler(_ => Response(HttpStatusCode.OK, body));
        await using var translator = Create(handler);
        Assert.False((await translator.TranslateImageAsync(Request)).IsSuccess);
    }

    [Fact]
    public async Task TranslateImageAsync_NetworkError_ReturnsSafeFailure()
    {
        using var handler = new Handler(_ => throw new HttpRequestException(FakeKey));
        await using var translator = Create(handler);
        var result = await translator.TranslateImageAsync(Request);
        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(FakeKey, result.Error!.Message);
    }

    [Fact]
    public async Task TranslateImageAsync_ThinkingUnsupported_RetriesOnce()
    {
        var calls = 0;
        using var handler = new Handler(_ => ++calls == 1
            ? Response(HttpStatusCode.BadRequest, "thinkingLevel unsupported") : Response(HttpStatusCode.OK, SuccessBody));
        await using var translator = Create(handler);
        Assert.True((await translator.TranslateImageAsync(Request)).IsSuccess);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task TestConnectionAsync_SavedKey_OnlyListsModels()
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v1beta/models", request.RequestUri!.AbsolutePath);
            return Response(HttpStatusCode.OK, "{\"models\":[]}");
        });
        await using var translator = Create(handler);
        Assert.Equal(GeminiConnectionResult.Success, await translator.TestConnectionAsync());
    }

    [Fact]
    public async Task TranslateImageAsync_Request_UsesStructuredOutputAndMinimalThinking()
    {
        using var handler = new Handler(_ => Response(HttpStatusCode.OK, SuccessBody));
        await using var translator = Create(handler);
        Assert.True((await translator.TranslateImageAsync(Request)).IsSuccess);
        using var json = JsonDocument.Parse(handler.RequestBody!);
        var config = json.RootElement.GetProperty("generationConfig");
        Assert.Equal(20, config.GetProperty("responseSchema").GetProperty("properties").GetProperty("texts").GetProperty("maxItems").GetInt32());
        Assert.Equal("application/json", config.GetProperty("responseMimeType").GetString());
        Assert.Equal("minimal", config.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
        var required = config.GetProperty("responseSchema").GetProperty("properties").GetProperty("texts")
            .GetProperty("items").GetProperty("required").EnumerateArray().Select(x => x.GetString());
        Assert.Contains("bounding_box", required);
        Assert.DoesNotContain(FakeKey, handler.RequestBody!);
    }

    [Fact]
    public async Task TranslateImageAsync_RetryAfterDate_PreventsRequestsUntilExpiry()
    {
        var clock = new Clock();
        using var handler = new Handler(_ =>
        {
            var response = Response(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(clock.Now.AddMinutes(3));
            return response;
        });
        await using var translator = Create(handler, clock: clock);
        await translator.TranslateImageAsync(Request);
        clock.Now += TimeSpan.FromSeconds(179);
        Assert.False(await translator.IsAvailableAsync());
        clock.Now += TimeSpan.FromSeconds(2);
        Assert.True(await translator.IsAvailableAsync());
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TranslateImageAsync_NoKeyOrLocalPreference_DoesNotSendRequest()
    {
        var settings = Settings();
        settings.SetupGet(x => x.PreferGemini).Returns(false);
        using var handler = new Handler(_ => throw new InvalidOperationException());
        await using var translator = Create(handler, settings.Object);
        Assert.False(await translator.IsAvailableAsync());
        Assert.False((await translator.TranslateImageAsync(Request)).IsSuccess);
        settings.SetupGet(x => x.PreferGemini).Returns(true);
        settings.Setup(x => x.GetApiKey()).Returns((string?)null);
        Assert.False((await translator.TranslateImageAsync(Request)).IsSuccess);
        Assert.Equal(0, handler.Calls);
    }
    [Theory]
    [InlineData(200, "{\"models\":[]}", GeminiConnectionResult.Success)]
    [InlineData(200, "[]", GeminiConnectionResult.Failed)]
    [InlineData(200, "invalid-json", GeminiConnectionResult.Failed)]
    [InlineData(400, "API_KEY_INVALID", GeminiConnectionResult.InvalidKey)]
    [InlineData(400, "other", GeminiConnectionResult.Failed)]
    [InlineData(401, "", GeminiConnectionResult.InvalidKey)]
    [InlineData(403, "", GeminiConnectionResult.InvalidKey)]
    [InlineData(429, "", GeminiConnectionResult.RateLimited)]
    [InlineData(500, "", GeminiConnectionResult.Failed)]
    public async Task TestConnectionAsync_HttpResponse_ClassifiesAndLogsSafely(int status, string body, GeminiConnectionResult expected)
    {
        var settings = Settings();
        settings.Setup(x => x.GetApiKey()).Returns("different-saved-key");
        var logger = new Mock<ILogger<GeminiImageTranslator>>();
        using var handler = new Handler(_ => Response((HttpStatusCode)status, status == 200 ? body : body + FakeKey));
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
        await using var translator = new GeminiImageTranslator(factory.Object, settings.Object, logger: logger.Object);
        Assert.Equal(expected, await translator.TestConnectionAsync(FakeKey));
        settings.Verify(x => x.SaveApiKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        AssertSafeLog(logger, expected, status);
    }

    private static void AssertSafeLog(Mock<ILogger<GeminiImageTranslator>> logger, GeminiConnectionResult expected, int? status)
    {
        var logs = logger.Invocations.Where(x => x.Method.Name == "Log").ToArray();
        Assert.NotEmpty(logs);
        Assert.All(logs, log =>
        {
            Assert.Null(log.Arguments[3]);
            Assert.DoesNotContain(FakeKey, log.Arguments[2].ToString()!);
            var fields = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(log.Arguments[2]).ToDictionary(x => x.Key, x => x.Value);
            Assert.Equal(expected, fields["ResultKind"]);
            Assert.Equal(status, fields["HttpStatusCode"]);
            Assert.Equal(new[] { "Operation", "ResultKind", "HttpStatusCode", "{OriginalFormat}" }, fields.Keys);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Requests_TransportFailure_LogsOnlySafeMetadata(bool timeout, bool translate)
    {
        var logger = new Mock<ILogger<GeminiImageTranslator>>();
        using var handler = new Handler(_ => timeout ? throw new TaskCanceledException(FakeKey) : throw new HttpRequestException(FakeKey));
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
        await using var translator = new GeminiImageTranslator(factory.Object, Settings().Object, logger: logger.Object);
        var expected = timeout ? GeminiConnectionResult.Timeout : GeminiConnectionResult.NetworkError;
        if (translate) Assert.False((await translator.TranslateImageAsync(Request)).IsSuccess);
        else Assert.Equal(expected, await translator.TestConnectionAsync());
        AssertSafeLog(logger, expected, null);
    }

    [Fact]
    public async Task TestConnectionAsync_NoKey_DoesNotSendHttpRequest()
    {
        var settings = Settings();
        settings.Setup(x => x.GetApiKey()).Returns((string?)null);
        using var handler = new Handler(_ => throw new InvalidOperationException());
        await using var translator = Create(handler, settings.Object);
        Assert.Equal(GeminiConnectionResult.NoKey, await translator.TestConnectionAsync());
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task TestConnectionAsync_DifferentInputKey_DoesNotResetAuthenticationError()
    {
        var settings = Settings();
        using var handler = new Handler(request => request.Method == HttpMethod.Get
            ? Response(HttpStatusCode.OK, "{\"models\":[]}") : Response(HttpStatusCode.Forbidden, ""));
        await using var translator = Create(handler, settings.Object);
        Assert.False((await translator.TranslateImageAsync(Request)).IsSuccess);
        settings.Setup(x => x.GetApiKey()).Returns("different-saved-key");
        Assert.Equal(GeminiConnectionResult.Success, await translator.TestConnectionAsync(FakeKey));
        Assert.False(await translator.IsAvailableAsync());
    }

    [Theory]
    [InlineData(400, "API_KEY_INVALID", GeminiConnectionResult.InvalidKey)]
    [InlineData(401, "", GeminiConnectionResult.InvalidKey)]
    [InlineData(403, "", GeminiConnectionResult.InvalidKey)]
    [InlineData(429, "", GeminiConnectionResult.RateLimited)]
    [InlineData(500, "", GeminiConnectionResult.Failed)]
    public async Task TranslateImageAsync_HttpFailure_LogsOnlySafeMetadata(int status, string body, GeminiConnectionResult expected)
    {
        var logger = new Mock<ILogger<GeminiImageTranslator>>();
        using var handler = new Handler(_ => Response((HttpStatusCode)status, body + FakeKey));
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
        await using var translator = new GeminiImageTranslator(factory.Object, Settings().Object, logger: logger.Object);
        Assert.False((await translator.TranslateImageAsync(Request)).IsSuccess);
        AssertSafeLog(logger, expected, status);
    }
}
