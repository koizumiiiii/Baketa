using Baketa.Core.License.Events;
using Baketa.Core.License.Models;
using Baketa.Infrastructure.License.Services;
using Xunit;

namespace Baketa.Infrastructure.Tests.License;

public class LocalLicenseManagerTests
{
    [Fact]
    public async Task GetCurrentStateAsync_WithoutAccount_AllFeaturesRemainAvailable()
    {
        var service = new LocalLicenseManager();

        service.SetResolvedLicenseState(new LicenseState { CurrentPlan = PlanType.Free }, "legacy", default);
        service.SetSessionToken("retired-session");
        service.SetUserId("retired-user");
        service.SyncTokenUsage(long.MaxValue);
        await service.RefreshStateAsync();
        var state = await service.ForceRefreshAsync();

        Assert.All(Enum.GetValues<FeatureType>(), feature => Assert.True(service.IsFeatureAvailable(feature)));
        Assert.Equal(PlanType.Ultimate, state.CurrentPlan);
        Assert.False(state.IsExpired);
        Assert.False(state.IsQuotaExceeded);
        Assert.Null(service.CurrentSessionId);
        Assert.Null(state.UserId);
        Assert.Same(state, await service.GetCurrentStateAsync());
    }
}
