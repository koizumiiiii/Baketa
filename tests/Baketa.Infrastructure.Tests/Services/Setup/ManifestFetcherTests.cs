using Baketa.Infrastructure.Services.Setup;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace Baketa.Infrastructure.Tests.Services.Setup;

public class ManifestFetcherTests
{
    [Fact]
    public async Task CheckForUpdatesAsync_FinalRelease_NeverSendsHttp()
    {
        var handler = new Mock<HttpMessageHandler>();
        using var client = new HttpClient(handler.Object);
        using var fetcher = new ManifestFetcher(NullLogger<ManifestFetcher>.Instance, client);
        var versions = new Mock<IComponentVersionService>(MockBehavior.Strict);
        Assert.Empty(await fetcher.CheckForUpdatesAsync(versions.Object, "1.0.0"));
        Assert.Null(await fetcher.GetManifestAsync(forceRefresh: true));
        fetcher.ClearCache();
        Assert.Null(await fetcher.GetManifestAsync());
        handler.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        versions.VerifyNoOtherCalls();
    }
}
