using Baketa.Infrastructure.Translation.Services;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Services;

public class DisabledCloudTranslationAvailabilityServiceTests
{
    [Fact]
    public async Task SetPreferredAsync_CloudRequested_CloudRemainsUnavailable()
    {
        var service = new DisabledCloudTranslationAvailabilityService();

        await service.SetPreferredAsync(true);
        await service.RefreshStateAsync();

        Assert.True(service.IsEntitled);
        Assert.False(service.IsEffectivelyEnabled);
        Assert.False(service.IsPreferred);
    }
}
