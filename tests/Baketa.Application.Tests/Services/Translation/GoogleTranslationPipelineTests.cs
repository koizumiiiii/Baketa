using Baketa.Application.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models.Translation;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Common;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Baketa.Application.Tests.Services.Translation;

public sealed class GoogleTranslationPipelineTests
{
    private sealed class UnavailableGoogleHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExecuteAsync_UndeterminedAutoSourceToEnglish_FailsWithoutCaching(bool batch, bool googleSelected)
    {
        using var handler = new UnavailableGoogleHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, false));
        var settings = new Mock<IGoogleTranslationSettingsService>();
        settings.SetupGet(x => x.HasApiKey).Returns(true);
        settings.Setup(x => x.GetApiKey()).Returns("test-only-key");
        using var google = new Baketa.Infrastructure.Translation.Cloud.GoogleTranslationEngine(factory.Object, settings.Object,
            NullLogger<Baketa.Infrastructure.Translation.Cloud.GoogleTranslationEngine>.Instance);
        var methods = new Mock<ITranslationMethodService>();
        methods.SetupGet(x => x.Current).Returns(googleSelected ? Baketa.Core.Models.TranslationMethod.Google : Baketa.Core.Models.TranslationMethod.Local);
        var local = new Mock<ITranslationEngine>();
        local.SetupGet(x => x.Name).Returns("NLLB");
        local.SetupGet(x => x.Aliases).Returns(["NLLB200"]);
        using var router = new Baketa.Infrastructure.Translation.Cloud.SelectedTextTranslationEngine(local.Object, google, methods.Object);
        var cache = new Mock<ITranslationCache>();
        cache.Setup(x => x.GetManyAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, TranslationCacheEntry>());
        var options = new TranslationOptions();
        options.PipelineOptions.EnableTransactions = false;
        var pipeline = new StandardTranslationPipeline(NullLogger<StandardTranslationPipeline>.Instance,
            [router], cache.Object, Mock.Of<ITranslationManager>(), Mock.Of<IEventAggregator>(), Mock.Of<ITranslationTransactionManager>(), options);
        var requests = new[]
        {
            new TranslationRequest { SourceText = "Bonjour", SourceLanguage = Language.Auto, TargetLanguage = Language.English },
            new TranslationRequest { SourceText = "Merci", SourceLanguage = Language.Auto, TargetLanguage = Language.English }
        };

        for (var i = 0; i < 2; i++)
        {
            IReadOnlyList<TranslationResponse> results = batch
                ? await pipeline.ExecuteBatchAsync(requests, "NLLB200")
                : [await pipeline.ExecuteAsync(requests[0], "NLLB200")];
            Assert.Equal(batch ? 2 : 1, results.Count);
            for (var j = 0; j < results.Count; j++)
            {
                Assert.Equal(requests[j].RequestId, results[j].RequestId);
                Assert.False(results[j].IsSuccess);
                Assert.Equal("SOURCE_LANGUAGE_UNDETERMINED", results[j].Error?.ErrorCode);
            }
        }

        cache.Verify(x => x.GetManyAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), batch ? Times.Exactly(2) : Times.Never());
        cache.Verify(x => x.SetAsync(It.IsAny<string>(), It.IsAny<TranslationCacheEntry>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
        cache.Verify(x => x.SetManyAsync(It.IsAny<IDictionary<string, TranslationCacheEntry>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
        local.Verify(x => x.TranslateBatchAsync(It.IsAny<IReadOnlyList<TranslationRequest>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteBatchAsync_MixedSuccessAndFailure_CachesOnlySuccess(bool failureFirst)
    {
        // Arrange
        var success = new TranslationRequest { SourceText = "Hello", SourceLanguage = Language.English, TargetLanguage = Language.Japanese };
        var failure = new TranslationRequest { SourceText = "Goodbye", SourceLanguage = Language.English, TargetLanguage = Language.Japanese };
        TranslationRequest[] requests = failureFirst ? [failure, success] : [success, failure];
        var engine = new Mock<ITranslationEngine>();
        engine.SetupGet(x => x.Name).Returns("GoogleTranslation");
        engine.Setup(x => x.TranslateBatchAsync(It.IsAny<IReadOnlyList<TranslationRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TranslationRequest> batch, CancellationToken _) => batch.Select(request =>
                request.SourceText == success.SourceText
                    ? TranslationResponse.CreateSuccess(request, "こんにちは", "GoogleTranslation", 0)
                    : TranslationResponse.CreateError(request, new TranslationError { ErrorCode = "TEST_FAILURE", Message = "Translation failed." }, "GoogleTranslation")).ToArray());
        var cache = new Mock<ITranslationCache>();
        cache.Setup(x => x.GetManyAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, TranslationCacheEntry>());
        var options = new TranslationOptions();
        options.PipelineOptions.EnableTransactions = false;
        var pipeline = new StandardTranslationPipeline(NullLogger<StandardTranslationPipeline>.Instance,
            [engine.Object], cache.Object, Mock.Of<ITranslationManager>(), Mock.Of<IEventAggregator>(), Mock.Of<ITranslationTransactionManager>(), options);

        // Act
        var results = await pipeline.ExecuteBatchAsync(requests, "GoogleTranslation");

        // Assert
        Assert.Equal(2, results.Count);
        var successfulResult = Assert.Single(results, result => result.RequestId == success.RequestId);
        Assert.True(successfulResult.IsSuccess);
        Assert.Equal("こんにちは", successfulResult.TranslatedText);
        var failedResult = Assert.Single(results, result => result.RequestId == failure.RequestId);
        Assert.False(failedResult.IsSuccess);
        Assert.Equal("TEST_FAILURE", failedResult.Error?.ErrorCode);
        var successKey = CacheKeyGenerator.GenerateKey(success.SourceText, success.SourceLanguage.Code, success.TargetLanguage.Code, "GoogleTranslation");
        cache.Verify(x => x.SetManyAsync(It.Is<IDictionary<string, TranslationCacheEntry>>(entries =>
            entries.Count == 1 && entries.ContainsKey(successKey) &&
            entries[successKey].SourceText == success.SourceText && entries[successKey].TranslatedText == "こんにちは" &&
            entries[successKey].Engine == "GoogleTranslation"), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(x => x.SetManyAsync(It.IsAny<IDictionary<string, TranslationCacheEntry>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(x => x.SetAsync(It.IsAny<string>(), It.IsAny<TranslationCacheEntry>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
        engine.Verify(x => x.TranslateBatchAsync(It.Is<IReadOnlyList<TranslationRequest>>(batch => batch.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
        engine.Verify(x => x.TranslateAsync(It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_SelectionChanges_KeepsFallbackCacheSeparate(bool batch)
    {
        var name = "GoogleTranslation";
        var actual = "NLLB-200 ONNX";
        var calls = 0;
        var engine = new Mock<ITranslationEngine>();
        engine.SetupGet(x => x.Name).Returns(() => name);
        engine.SetupGet(x => x.Aliases).Returns(["NLLB200"]);
        engine.Setup(x => x.TranslateAsync(It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TranslationRequest request, CancellationToken _) =>
            {
                calls++;
                return TranslationResponse.CreateSuccess(request, actual, actual, 0);
            });
        engine.Setup(x => x.TranslateBatchAsync(It.IsAny<IReadOnlyList<TranslationRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TranslationRequest> requests, CancellationToken _) =>
            {
                calls++;
                return requests.Select(r => TranslationResponse.CreateSuccess(r, actual, actual, 0)).ToArray();
            });
        var entries = new Dictionary<string, TranslationCacheEntry>();
        var cache = new Mock<ITranslationCache>();
        cache.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => entries.GetValueOrDefault(key));
        cache.Setup(x => x.GetManyAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> keys, CancellationToken _) => (IDictionary<string, TranslationCacheEntry>)entries.Where(x => keys.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value));
        cache.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<TranslationCacheEntry>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, TranslationCacheEntry entry, TimeSpan? _, CancellationToken _) => { entries[key] = entry; return true; });
        cache.Setup(x => x.SetManyAsync(It.IsAny<IDictionary<string, TranslationCacheEntry>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IDictionary<string, TranslationCacheEntry> added, TimeSpan? _, CancellationToken _) =>
            {
                foreach (var item in added) entries[item.Key] = item.Value;
                return true;
            });
        var options = new TranslationOptions();
        options.PipelineOptions.EnableTransactions = false;
        var pipeline = new StandardTranslationPipeline(NullLogger<StandardTranslationPipeline>.Instance,
            [engine.Object], cache.Object, Mock.Of<ITranslationManager>(), Mock.Of<IEventAggregator>(), Mock.Of<ITranslationTransactionManager>(), options);
        var requests = new[]
        {
            new TranslationRequest { SourceText = "one", SourceLanguage = Language.English, TargetLanguage = Language.Japanese },
            new TranslationRequest { SourceText = "two", SourceLanguage = Language.English, TargetLanguage = Language.Japanese }
        };
        async Task<IReadOnlyList<TranslationResponse>> Run() => batch
            ? await pipeline.ExecuteBatchAsync(requests, "NLLB200")
            : [await pipeline.ExecuteAsync(requests[0], "NLLB200")];

        Assert.All(await Run(), r => { Assert.True(r.IsSuccess); Assert.Equal(actual, r.EngineName); });
        actual = name;
        Assert.All(await Run(), r => Assert.Equal("GoogleTranslation", r.EngineName));
        Assert.Equal(2, calls);
        await Run();
        Assert.Equal(2, calls);
        name = "NLLB-200 ONNX";
        Assert.All(await Run(), r => Assert.Equal(name, r.EngineName));
        Assert.Equal(2, calls);
    }
}
