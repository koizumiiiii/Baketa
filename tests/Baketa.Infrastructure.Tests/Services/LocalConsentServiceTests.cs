using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Services.Settings;
using Baketa.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Services;

public sealed class LocalConsentServiceTests
{
    [Fact]
    public async Task ServerOperations_WithLegacySession_NeverCreateHttpClient()
    {
        // Arrange
        var http = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var deduplicator = new Mock<IApiRequestDeduplicator>(MockBehavior.Strict);
        using var store = new ConsentService(NullLogger<ConsentService>.Instance, http.Object, deduplicator.Object);
        var service = new LocalConsentService(store);

        // Act
        await service.RecordConsentToServerAsync("legacy-user", "legacy-token", ConsentType.PrivacyPolicy, "1");
        await service.SyncLocalConsentToServerAsync("legacy-user", "legacy-token");
        await service.SyncFromServerAsync("legacy-token");
        var state = await service.EnsureConsentValidAsync("legacy-token");

        // Assert
        Assert.Equal(ConsentVerificationState.LocalOnly, state);
        http.VerifyNoOtherCalls();
        deduplicator.VerifyNoOtherCalls();
    }
}
