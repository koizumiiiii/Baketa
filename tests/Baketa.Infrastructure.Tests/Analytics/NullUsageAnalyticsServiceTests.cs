using Baketa.Infrastructure.Analytics;
using Xunit;

namespace Baketa.Infrastructure.Tests.Analytics;

public class NullUsageAnalyticsServiceTests
{
    [Fact]
    public async Task FlushAsync_AfterSessionAndTranslationEvents_RemainsDisabled()
    {
        // Arrange
        var service = new NullUsageAnalyticsService();

        // Act
        service.TrackEvent("session_start");
        service.TrackEvent("translation", new() { ["text_length"] = 10 });
        service.TrackEvent("session_end");
        var result = await service.FlushAsync();

        // Assert
        Assert.False(service.IsEnabled);
        Assert.Equal(Guid.Empty, service.SessionId);
        Assert.False(result);
    }
}
