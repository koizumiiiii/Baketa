using System.Net;
using System.Text.Json;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models;
using Baketa.Core.Models.Translation;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Cloud;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Baketa.Infrastructure.Services;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Cloud;

public sealed class GoogleTranslationEngineTests
{
    private sealed class PendingHandler : HttpMessageHandler
    {
        private int _calls;
        public TaskCompletionSource BothStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 2) BothStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TranslateBatchAsync_TwoPendingChunks_FallBackOrPropagateCallerCancellation(bool cancelCaller)
    {
        using var handler = new PendingHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(GoogleTranslationEngine.ClientName)).Returns(() => new HttpClient(handler, false));
        var notification = new Mock<IGeminiNotificationService>();
        notification.Setup(x => x.NotifyAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        using var google = new GoogleTranslationEngine(factory.Object, Settings().Object,
            NullLogger<GoogleTranslationEngine>.Instance, notification.Object,
            operationTimeout: TimeSpan.FromMilliseconds(cancelCaller ? 5000 : 500));
        var methods = new Mock<ITranslationMethodService>();
        methods.SetupGet(x => x.Current).Returns(TranslationMethod.Google);
        var local = new Mock<ITranslationEngine>();
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        local.Setup(x => x.TranslateBatchAsync(It.IsAny<IReadOnlyList<TranslationRequest>>(), caller.Token))
            .ReturnsAsync((IReadOnlyList<TranslationRequest> batch, CancellationToken token) =>
            {
                Assert.False(token.IsCancellationRequested);
                return batch.Select(r => TranslationResponse.CreateSuccess(r, "local", "NLLB", 0)).ToArray();
            });
        using var router = new SelectedTextTranslationEngine(local.Object, google, methods.Object);
        var first = router.TranslateBatchAsync([Request("first")], caller.Token);
        var second = router.TranslateBatchAsync([Request("second")], caller.Token);
        await handler.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (cancelCaller)
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
            local.Verify(x => x.TranslateBatchAsync(It.IsAny<IReadOnlyList<TranslationRequest>>(), It.IsAny<CancellationToken>()), Times.Never);
            notification.Verify(x => x.NotifyAsync(It.IsAny<string>()), Times.Never);
        }
        else
        {
            var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.All(results, chunk => { var result = Assert.Single(chunk); Assert.True(result.IsSuccess); Assert.Equal("NLLB", result.EngineName); });
            local.Verify(x => x.TranslateBatchAsync(It.IsAny<IReadOnlyList<TranslationRequest>>(), caller.Token), Times.Exactly(2));
            notification.Verify(x => x.NotifyAsync("Google_Fallback"), Times.Once);
        }
    }

    [Theory]
    [InlineData(401, 500, false)]
    [InlineData(429, 429, false)]
    [InlineData(500, 401, true)]
    public async Task TranslateAsync_ConcurrentFailures_PreserveStateAndIgnoreOldRevision(int firstStatus, int lateStatus, bool saveKey)
    {
        var firstResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var handler = new Handler(request => Interlocked.Increment(ref calls) switch
        {
            1 => firstResponse.Task,
            2 => lateResponse.Task,
            _ => Echo(request)
        });
        var settings = Settings();
        var notification = new Mock<IGeminiNotificationService>();
        notification.Setup(x => x.NotifyAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var clock = new Clock();
        using var engine = Engine(handler, settings.Object, notification.Object, clock);
        var first = engine.TranslateAsync(Request());
        var late = engine.TranslateAsync(Request());
        Assert.Equal(2, calls);
        var error = Error(firstStatus, "");
        error.Headers.RetryAfter = new(TimeSpan.FromMinutes(2));
        firstResponse.SetResult(error);
        await first;
        if (saveKey)
        {
            settings.SetupGet(x => x.ApiKeySaveRevision).Returns(1);
            Assert.True((await engine.TranslateAsync(Request())).IsSuccess);
        }
        lateResponse.SetResult(Error(lateStatus, ""));
        await late;
        clock.Now = clock.Now.AddSeconds(90);
        Assert.Equal(saveKey, (await engine.TranslateAsync(Request())).IsSuccess);
        Assert.Equal(saveKey ? 4 : 2, calls);
        notification.Verify(x => x.NotifyAsync("Google_Fallback"), Times.Once);
    }

    private const string Key = "test-only-google-key";

    [Theory]
    [InlineData("&lt;tag&gt;")]
    [InlineData("&#39;quoted&#39;")]
    [InlineData("&amp;lt; &quot; &#x27; 日本語")]
    public async Task TranslateAsync_PlainTextResponse_PreservesLiteralEntities(string translation)
    {
        using var handler = new Handler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("text", body.RootElement.GetProperty("format").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    data = new { translations = new[] { new { translatedText = translation } } }
                }))
            };
        });
        using var engine = Engine(handler);

        var response = await engine.TranslateAsync(Request());

        Assert.True(response.IsSuccess);
        Assert.Equal(translation, response.TranslatedText);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static Mock<IGoogleTranslationSettingsService> Settings()
    {
        var settings = new Mock<IGoogleTranslationSettingsService>();
        settings.SetupGet(x => x.HasApiKey).Returns(true);
        settings.Setup(x => x.GetApiKey()).Returns(Key);
        return settings;
    }
    private static GoogleTranslationEngine Engine(HttpMessageHandler handler, IGoogleTranslationSettingsService? settings = null,
        IGeminiNotificationService? notifications = null, TimeProvider? clock = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(GoogleTranslationEngine.ClientName)).Returns(() => new HttpClient(handler, false));
        return new(factory.Object, settings ?? Settings().Object, NullLogger<GoogleTranslationEngine>.Instance, notifications, clock);
    }
    private static TranslationRequest Request(string text = "hello", Language? source = null) => new()
    {
        SourceText = text, SourceLanguage = source ?? Language.English, TargetLanguage = Language.Japanese
    };
    private static HttpResponseMessage Error(int status, string reason, string field = "details") => new((HttpStatusCode)status)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { error = new Dictionary<string, object> { [field] = new[] { new { reason } } } }))
    };
    private static async Task<HttpResponseMessage> Echo(HttpRequestMessage request)
    {
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { data = new { translations = json.RootElement.GetProperty("q")
                .EnumerateArray().Select(q => new { translatedText = q.GetString() }).ToArray() } }))
        };
    }

    [Fact]
    public async Task StartWarmupAsync_GoogleSelected_InitializesAndWarmsOnlyLocalModels()
    {
        var calls = 0;
        using var handler = new Handler(request => { calls++; return Echo(request); });
        var settings = Settings();
        using var google = Engine(handler, settings.Object);
        var methods = new Mock<ITranslationMethodService>();
        methods.SetupGet(x => x.Current).Returns(TranslationMethod.Google);
        var local = new Mock<ITranslationEngine>();
        var unloadable = local.As<IUnloadableTranslationEngine>();
        unloadable.Setup(x => x.UnloadModelsAsync()).Returns(Task.CompletedTask);
        local.SetupGet(x => x.Name).Returns("NLLB");
        local.SetupGet(x => x.Aliases).Returns(["NLLB200"]);
        local.Setup(x => x.InitializeAsync()).ReturnsAsync(true);
        local.Setup(x => x.IsReadyAsync()).ReturnsAsync(true);
        local.Setup(x => x.TranslateAsync(It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TranslationRequest request, CancellationToken _) => TranslationResponse.CreateSuccess(request, "こんにちは", "NLLB", 0));
        using var router = new SelectedTextTranslationEngine(local.Object, google, methods.Object);
        using var provider = new ServiceCollection().AddSingleton<ITranslationEngine>(router).BuildServiceProvider();
        using var warmup = new BackgroundWarmupService(provider, NullLogger<BackgroundWarmupService>.Instance);

        Assert.True(await router.InitializeAsync());
        Assert.True(await router.IsReadyAsync());
        await warmup.StartWarmupAsync();
        Assert.True(await warmup.WaitForWarmupAsync(TimeSpan.FromSeconds(10)));
        await router.UnloadModelsAsync();
        Assert.True(await router.InitializeAsync());

        local.Verify(x => x.InitializeAsync(), Times.Exactly(3));
        local.Verify(x => x.IsReadyAsync(), Times.Exactly(2));
        local.Verify(x => x.TranslateAsync(It.Is<TranslationRequest>(r => r.SourceText == "Hello" && r.SourceLanguage == Language.English && r.TargetLanguage == Language.Japanese), It.IsAny<CancellationToken>()), Times.Once);
        unloadable.Verify(x => x.UnloadModelsAsync(), Times.Once);
        Assert.Equal(0, calls);
        settings.Verify(x => x.GetApiKey(), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TranslateBatchAsync_AutoToEnglish_PreservesFailuresAndOrder(bool localSelected)
    {
        using var handler = new Handler(_ => Task.FromResult(Error(500, "")));
        using var google = Engine(handler);
        var methods = new Mock<ITranslationMethodService>();
        methods.SetupGet(x => x.Current).Returns(localSelected ? TranslationMethod.Local : TranslationMethod.Google);
        var local = new Mock<ITranslationEngine>();
        local.SetupGet(x => x.Name).Returns("NLLB");
        local.Setup(x => x.TranslateBatchAsync(It.IsAny<IReadOnlyList<TranslationRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TranslationRequest> batch, CancellationToken _) =>
            {
                Assert.Equal(new[] { "ja", "ko" }, batch.Select(r => r.SourceLanguage.Code));
                return batch.Select(r => TranslationResponse.CreateSuccess(r, "translated", "NLLB", 0)).ToArray();
            });
        using var router = new SelectedTextTranslationEngine(local.Object, google, methods.Object);
        var requests = new[] { "Bonjour", "こんにちは", "hello", "안녕하세요" }
            .Select(text => new TranslationRequest { SourceText = text, SourceLanguage = Language.Auto, TargetLanguage = Language.English }).ToArray();
        var results = await router.TranslateBatchAsync(requests);
        Assert.Equal(new[] { false, true, false, true }, results.Select(r => r.IsSuccess));
        Assert.Equal(requests.Select(r => r.RequestId), results.Select(r => r.RequestId));
        Assert.Equal("SOURCE_LANGUAGE_UNDETERMINED", results[0].Error?.ErrorCode);
        Assert.Null(results[0].TranslatedText);
        Assert.All(requests, r => Assert.Equal(Language.Auto, r.SourceLanguage));
    }

    [Fact]
    public async Task TranslateAsync_GoogleDetectsFrenchToEnglish_DoesNotUseLocalHeuristics()
    {
        using var handler = new Handler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.False(body.RootElement.TryGetProperty("source", out _));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":{"translations":[{"translatedText":"Hello","detectedSourceLanguage":"fr"}]}}""")
            };
        });
        using var google = Engine(handler);
        var methods = new Mock<ITranslationMethodService>();
        methods.SetupGet(x => x.Current).Returns(TranslationMethod.Google);
        var local = new Mock<ITranslationEngine>(MockBehavior.Strict);
        using var router = new SelectedTextTranslationEngine(local.Object, google, methods.Object);
        var result = await router.TranslateAsync(new TranslationRequest { SourceText = "Bonjour", SourceLanguage = Language.Auto, TargetLanguage = Language.English });
        Assert.True(result.IsSuccess);
        Assert.Equal("Hello", result.TranslatedText);
        Assert.Equal(google.Name, result.EngineName);
        local.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TranslateBatchAsync_AutoResolvesToTarget_PreservesMixedBatchOrder(bool localSelected)
    {
        using var handler = new Handler(_ => Task.FromResult(Error(500, "")));
        using var google = Engine(handler);
        var methods = new Mock<ITranslationMethodService>();
        methods.SetupGet(x => x.Current).Returns(localSelected ? TranslationMethod.Local : TranslationMethod.Google);
        var local = new Mock<ITranslationEngine>();
        local.SetupGet(x => x.Name).Returns("NLLB");
        local.Setup(x => x.TranslateBatchAsync(It.IsAny<IReadOnlyList<TranslationRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TranslationRequest> batch, CancellationToken _) =>
            {
                Assert.Equal("hello", Assert.Single(batch).SourceText);
                return batch.Select(r => TranslationResponse.CreateSuccess(r, "翻訳", "NLLB", 0)).ToArray();
            });
        using var router = new SelectedTextTranslationEngine(local.Object, google, methods.Object);
        var requests = new[] { Request("こんにちは", Language.Auto), Request("hello", Language.Auto) };
        var results = await router.TranslateBatchAsync(requests);
        Assert.Equal(new[] { "こんにちは", "翻訳" }, results.Select(r => r.TranslatedText));
        Assert.Equal(requests.Select(r => r.RequestId), results.Select(r => r.RequestId));
        Assert.All(results, r => Assert.True(r.IsSuccess));
    }

    [Fact]
    public async Task TranslateAsync_MethodChanges_SelectsGoogleAndLocalEngines()
    {
        using var handler = new Handler(Echo);
        using var google = Engine(handler);
        var selected = TranslationMethod.Local;
        var methods = new Mock<ITranslationMethodService>();
        methods.SetupGet(x => x.Current).Returns(() => selected);
        var local = new Mock<ITranslationEngine>();
        local.Setup(x => x.TranslateBatchAsync(It.IsAny<IReadOnlyList<TranslationRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TranslationRequest> batch, CancellationToken _) =>
                batch.Select(r => TranslationResponse.CreateSuccess(r, "local", "NLLB", 0)).ToArray());
        using var router = new SelectedTextTranslationEngine(local.Object, google, methods.Object);
        Assert.Equal("NLLB", (await router.TranslateAsync(Request())).EngineName);
        selected = TranslationMethod.Google;
        Assert.Equal(google.Name, (await router.TranslateAsync(Request())).EngineName);
        selected = TranslationMethod.Local;
        Assert.Equal("NLLB", (await router.TranslateAsync(Request())).EngineName);
        local.Verify(x => x.TranslateBatchAsync(It.IsAny<IReadOnlyList<TranslationRequest>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("hello", "en", false)]
    [InlineData("こんにちは", "ja", false)]
    [InlineData("안녕하세요", "ko", false)]
    [InlineData("你好", "zh-CN", false)]
    [InlineData("hello", "en", true)]
    [InlineData("Bonjour", "en", false)]
    [InlineData("Bonjour", "en", true)]
    [InlineData("こんにちは", "ja", true)]
    [InlineData("안녕하세요", "ko", true)]
    [InlineData("你好", "zh-CN", true)]
    [InlineData("Привет", "ru", false)]
    [InlineData("مرحبا", "ar", false)]
    public async Task TranslateAsync_AutoSource_UsesSupportedNllbPair(string text, string expected, bool localSelected)
    {
        using var handler = new Handler(_ => Task.FromResult(Error(500, "")));
        using var google = Engine(handler);
        using var nllb = new Baketa.Infrastructure.Translation.Onnx.OnnxTranslationEngine(
            "unused-model-directory", NullLogger<Baketa.Infrastructure.Translation.Onnx.OnnxTranslationEngine>.Instance);
        var methods = new Mock<ITranslationMethodService>();
        methods.SetupGet(x => x.Current).Returns(localSelected ? TranslationMethod.Local : TranslationMethod.Google);
        var request = new TranslationRequest { SourceText = text, SourceLanguage = Language.Auto, TargetLanguage = Language.French };
        Assert.False(await nllb.SupportsLanguagePairAsync(request.LanguagePair));
        var local = new Mock<ITranslationEngine>();
        local.Setup(x => x.TranslateBatchAsync(It.IsAny<IReadOnlyList<TranslationRequest>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<TranslationRequest> batch, CancellationToken _) =>
            {
                var resolved = Assert.Single(batch);
                Assert.Equal(expected, resolved.SourceLanguage.Code);
                Assert.True(await nllb.SupportsLanguagePairAsync(resolved.LanguagePair));
                return new[] { TranslationResponse.CreateSuccess(resolved, "translated", nllb.Name, 0) };
            });
        using var router = new SelectedTextTranslationEngine(local.Object, google, methods.Object);
        var response = await router.TranslateAsync(request);
        Assert.True(response.IsSuccess);
        Assert.Equal(request.RequestId, response.RequestId);
        Assert.Equal(Language.Auto, request.SourceLanguage);
        Assert.Equal(nllb.Name, response.EngineName);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(129, 2)]
    [InlineData(257, 3)]
    public async Task TranslateBatchAsync_MultipleTexts_SplitsAt128AndPreservesOrder(int count, int expectedCalls)
    {
        var calls = 0;
        using var handler = new Handler(async request =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://translation.googleapis.com/language/translate/v2", request.RequestUri!.ToString());
            Assert.Equal(Key, Assert.Single(request.Headers.GetValues("X-Goog-Api-Key")));
            Assert.DoesNotContain(Key, request.RequestUri.ToString());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("text", body.RootElement.GetProperty("format").GetString());
            Assert.Equal("en", body.RootElement.GetProperty("source").GetString());
            Assert.Equal("ja", body.RootElement.GetProperty("target").GetString());
            Assert.InRange(body.RootElement.GetProperty("q").GetArrayLength(), 1, 128);
            return await Echo(request);
        });
        using var engine = Engine(handler);
        var requests = Enumerable.Range(0, count).Select(i => Request(i.ToString())).ToArray();
        var results = await engine.TranslateBatchAsync(requests);
        Assert.Equal(expectedCalls, calls);
        Assert.Equal(requests.Select(x => x.RequestId), results.Select(x => x.RequestId));
        Assert.Equal(requests.Select(x => x.SourceText), results.Select(x => x.TranslatedText));
        Assert.All(results, x => Assert.True(x.IsSuccess));
    }

    [Fact]
    public async Task TranslateBatchAsync_LargeUnicodeInput_RespectsByteLimitAndReassembles()
    {
        var calls = 0;
        using var handler = new Handler(async request =>
        {
            calls++;
            Assert.InRange((await request.Content!.ReadAsByteArrayAsync()).Length, 1, 99000);
            using var json = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
            Assert.False(json.RootElement.TryGetProperty("source", out _));
            return await Echo(request);
        });
        using var engine = Engine(handler);
        var text = string.Concat(Enumerable.Repeat("日本語😀", 12000));
        var result = await engine.TranslateAsync(Request(text, Language.Auto));
        Assert.True(result.IsSuccess);
        Assert.Equal(text, result.TranslatedText);
        Assert.True(calls > 1);
    }

    [Fact]
    public void GetLanguageCode_AllBaketaLanguages_MapsWithoutLosingChineseVariants()
    {
        foreach (var language in Language.SupportedLanguages) Assert.Equal(language.Code, GoogleTranslationEngine.GetLanguageCode(language));
        Assert.Null(GoogleTranslationEngine.GetLanguageCode(Language.Auto));
        Assert.Null(GoogleTranslationEngine.GetLanguageCode(new Language("auto", "Auto")));
    }

    [Theory]
    [InlineData(400, "API_KEY_INVALID", "details", GoogleTranslationResult.InvalidKey)]
    [InlineData(401, "", "errors", GoogleTranslationResult.InvalidKey)]
    [InlineData(403, "SERVICE_DISABLED", "details", GoogleTranslationResult.ServiceDisabled)]
    [InlineData(403, "accessNotConfigured", "errors", GoogleTranslationResult.ServiceDisabled)]
    [InlineData(403, "BILLING_DISABLED", "details", GoogleTranslationResult.BillingDisabled)]
    [InlineData(403, "forbidden", "errors", GoogleTranslationResult.Forbidden)]
    [InlineData(429, "", "details", GoogleTranslationResult.RateLimited)]
    [InlineData(403, "dailyLimitExceeded", "errors", GoogleTranslationResult.RateLimited)]
    [InlineData(500, "", "details", GoogleTranslationResult.Failed)]
    public async Task TestConnectionAsync_Error_ClassifiesWithoutExposingBody(int status, string reason, string field, GoogleTranslationResult expected)
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://translation.googleapis.com/language/translate/v2/languages?target=en", request.RequestUri!.ToString());
            Assert.Null(request.Content);
            Assert.Equal(Key, Assert.Single(request.Headers.GetValues("X-Goog-Api-Key")));
            return Task.FromResult(Error(status, reason, field));
        });
        using var engine = Engine(handler);
        Assert.Equal(expected, await engine.TestConnectionAsync(Key));
    }

    [Theory]
    [InlineData(false, GoogleTranslationResult.NetworkError)]
    [InlineData(true, GoogleTranslationResult.Timeout)]
    public async Task TestConnectionAsync_TransportFailure_ReturnsSafeResult(bool timeout, GoogleTranslationResult expected)
    {
        using var handler = new Handler(_ => throw (timeout ? new TaskCanceledException(Key) : new HttpRequestException(Key)));
        using var engine = Engine(handler);
        Assert.Equal(expected, await engine.TestConnectionAsync(Key));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task TranslateBatchAsync_Failure_UsesLocalBatchAndThrottlesNotifications(int status)
    {
        using var handler = new Handler(_ => Task.FromResult(Error(status, "")));
        var notification = new Mock<IGeminiNotificationService>();
        notification.Setup(x => x.NotifyAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        using var engine = Engine(handler, notifications: notification.Object);
        var methods = new Mock<ITranslationMethodService>();
        methods.SetupGet(x => x.Current).Returns(TranslationMethod.Google);
        var local = new Mock<ITranslationEngine>();
        var requests = new[] { Request("a"), Request("b") };
        local.Setup(x => x.TranslateBatchAsync(requests, It.IsAny<CancellationToken>()))
            .ReturnsAsync(requests.Select(x => TranslationResponse.CreateSuccess(x, "local", "NLLB", 0)).ToArray());
        using var router = new SelectedTextTranslationEngine(local.Object, engine, methods.Object);
        for (var i = 0; i < 3; i++) Assert.All(await router.TranslateBatchAsync(requests), x => Assert.Equal("NLLB", x.EngineName));
        local.Verify(x => x.TranslateBatchAsync(requests, It.IsAny<CancellationToken>()), Times.Exactly(3));
        notification.Verify(x => x.NotifyAsync("Google_Fallback"), Times.Once);
    }

    [Fact]
    public async Task TranslateAsync_AuthFailure_RecoversOnlyAfterExplicitSameKeySave()
    {
        var calls = 0;
        var settings = Settings();
        using var handler = new Handler(request => ++calls == 1 ? Task.FromResult(Error(401, "")) : Echo(request));
        using var engine = Engine(handler, settings.Object);
        Assert.False((await engine.TranslateAsync(Request())).IsSuccess);
        Assert.False((await engine.TranslateAsync(Request())).IsSuccess);
        Assert.Equal(1, calls);
        settings.SetupGet(x => x.ApiKeySaveRevision).Returns(1);
        Assert.True((await engine.TranslateAsync(Request())).IsSuccess);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TranslateAsync_RetryAfter_RespectsDeltaAndDate(bool date)
    {
        var calls = 0;
        var clock = new Clock();
        using var handler = new Handler(async request =>
        {
            if (++calls > 1) return await Echo(request);
            var error = Error(429, "");
            error.Headers.RetryAfter = date ? new(clock.Now.AddMinutes(2)) : new(TimeSpan.FromMinutes(2));
            return error;
        });
        using var engine = Engine(handler, clock: clock);
        Assert.False((await engine.TranslateAsync(Request())).IsSuccess);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.False((await engine.TranslateAsync(Request())).IsSuccess);
        Assert.Equal(1, calls);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.True((await engine.TranslateAsync(Request())).IsSuccess);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task TranslateAsync_CallerCancellation_Propagates()
    {
        using var handler = new Handler(_ => throw new TaskCanceledException());
        using var engine = Engine(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.TranslateAsync(Request(), cts.Token));
    }
}
