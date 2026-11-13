using System.Net.Http;
using Baketa.Core.Abstractions.Services;
using Baketa.Infrastructure.Services.Setup;
using Baketa.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace Baketa.UI.Tests.Services;

public class ComponentUpdateNotificationServiceTests
{
    [Fact]
    public async Task CheckForUpdatesOnStartupAsync_FinalRelease_NoHttpOrNotification()
    {
        var handler = new Mock<HttpMessageHandler>();
        using var client = new HttpClient(handler.Object);
        using var fetcher = new ManifestFetcher(NullLogger<ManifestFetcher>.Instance, client);
        var versions = new Mock<IComponentVersionService>(MockBehavior.Strict);
        var service = new ComponentUpdateNotificationService(
            NullLogger<ComponentUpdateNotificationService>.Instance, fetcher, versions.Object,
            Mock.Of<IGpuEnvironmentService>(), new ConfigurationBuilder().Build());

        Assert.False(await service.CheckForUpdatesOnStartupAsync());
        Assert.Empty(await service.CheckForUpdatesManuallyAsync());
        handler.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<System.Threading.CancellationToken>());
        versions.VerifyNoOtherCalls();
    }
}
