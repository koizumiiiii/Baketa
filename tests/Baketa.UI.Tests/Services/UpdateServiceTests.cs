using Baketa.UI.Services;
using Xunit;

namespace Baketa.UI.Tests.Services;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdates_FinalRelease_AllEntryPointsRemainDisabled()
    {
        using var service = new UpdateService();
        service.Initialize();

        Assert.False(await service.CheckForUpdatesEarlyAsync());
        Assert.False(await service.CheckForUpdatesAsync());
        await service.CheckForUpdatesInBackgroundAsync();
        service.ShowUpdateDialog();

        Assert.False(service.IsCheckingForUpdates);
        Assert.False(service.UpdateAvailable);
        Assert.Null(service.LatestVersion);
    }
}
