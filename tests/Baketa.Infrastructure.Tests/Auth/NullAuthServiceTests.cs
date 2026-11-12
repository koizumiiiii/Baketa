using Baketa.Core.Abstractions.Auth;
using Baketa.Infrastructure.Auth;
using Xunit;

namespace Baketa.Infrastructure.Tests.Auth;

public class NullAuthServiceTests
{
    [Fact]
    public async Task RestoreSessionAsync_LocalEdition_NeverRestoresOrExchangesCredentials()
    {
        using var service = new NullAuthService();

        await service.RestoreSessionAsync();
        var exchange = await service.ExchangeSessionTokenAsync("retired-session");

        Assert.Null(await service.GetCurrentSessionAsync());
        Assert.Null(await service.GetAccessTokenAsync());
        Assert.Null(exchange);
        Assert.Null(await service.RefreshAsync());
        Assert.False(service.HasValidToken);
        Assert.IsType<AuthFailure>(await service.StartOAuthFlowAsync(AuthProvider.Google));
    }
}
