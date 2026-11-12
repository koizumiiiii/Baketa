using Baketa.Core.License.Models;
using Baketa.Infrastructure.License.Services;
using Xunit;

namespace Baketa.Infrastructure.Tests.License;

public class NullAccountServicesTests
{
    [Fact]
    public async Task SyncLicenseAsync_RetiredAccount_RemainsDisconnectedWithoutPendingSync()
    {
        var service = new NullAccountServices();

        var sync = await service.SyncLicenseAsync(true);
        await service.SyncFromServerAsync("retired-session");
        await service.FetchFromServerAsync("retired-session");
        await service.SyncToServerAsync("retired-session");
        service.ApplySyncedData([new BonusTokenInfo { BonusId = "old", RemainingTokens = 100 }], 100);

        Assert.False(sync.Success);
        Assert.False(service.IsAuthenticated);
        Assert.False(service.HasPendingSync);
        Assert.Null(await service.LoadCredentialsAsync());
        Assert.Null(await service.GetSessionTokenAsync());
        Assert.Empty(service.GetBonusTokens());
        Assert.Equal(0, service.GetTotalRemainingTokens());
        Assert.False(service.CanHandle("baketa://patreon/callback?code=old"));
    }
}
