using Baketa.Core.Settings;
using Xunit;

namespace Baketa.Infrastructure.Tests.License;

/// <summary>
/// PatreonSettingsのテスト
/// Issue #125: StandardTierId削除（Standardプラン廃止）に対応
/// </summary>
public class PatreonSettingsTests
{
    [Fact]
    public void ValidateSettings_WithValidHttpsUrl_ReturnsSuccess()
    {
        // Arrange
        var settings = new PatreonSettings
        {
            ClientId = "test-client-id",
            RelayServerUrl = "https://baketa-relay.workers.dev",
            ProTierId = "123456"
        };

        // Act
        var result = settings.ValidateSettings();

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.GetErrorMessages());
    }

    [Fact]
    public void ValidateSettings_WithHttpUrl_ReturnsError()
    {
        // Arrange
        var settings = new PatreonSettings
        {
            ClientId = "test-client-id",
            RelayServerUrl = "http://insecure-server.com",
            ProTierId = "123456"
        };

        // Act
        var result = settings.ValidateSettings();

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("HTTPS", result.GetErrorMessages());
    }

    [Fact]
    public void ValidateSettings_WithMissingRelayServerUrl_WhenClientIdSet_ReturnsError()
    {
        // Arrange
        var settings = new PatreonSettings
        {
            ClientId = "test-client-id",
            RelayServerUrl = "",
            ProTierId = "123456"
        };

        // Act
        var result = settings.ValidateSettings();

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("中継サーバーURL", result.GetErrorMessages());
    }

    [Fact]
    public void ValidateSettings_WithInvalidUrl_ReturnsError()
    {
        // Arrange
        var settings = new PatreonSettings
        {
            ClientId = "test-client-id",
            RelayServerUrl = "not-a-valid-url",
            ProTierId = "123456"
        };

        // Act
        var result = settings.ValidateSettings();

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateSettings_WithMissingClientId_ReturnsWarning()
    {
        // Arrange
        var settings = new PatreonSettings
        {
            ClientId = "",
            RelayServerUrl = "https://baketa-relay.workers.dev"
        };

        // Act
        var result = settings.ValidateSettings();

        // Assert - Missing ClientId is just a warning, not an error
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateSettings_WithMissingTierIds_ReturnsWarning()
    {
        // Arrange
        // Issue #125: StandardTierId削除
        // Issue #257: PremiaTierId → PremiumTierId, UltimateTierId追加
        var settings = new PatreonSettings
        {
            ClientId = "test-client-id",
            RelayServerUrl = "https://baketa-relay.workers.dev",
            ProTierId = "",
            PremiumTierId = "",
            UltimateTierId = ""
        };

        // Act
        var result = settings.ValidateSettings();

        // Assert - Missing Tier IDs is just a warning
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(4, true)]    // Too short - warning
    [InlineData(5, false)]   // Minimum valid - no warning
    [InlineData(60, false)]  // Default - no warning
    [InlineData(1440, false)] // Maximum valid - no warning
    [InlineData(1441, true)] // Too long - warning
    public void ValidateSettings_CacheDuration_ValidationWorks(int minutes, bool shouldHaveWarning)
    {
        // Arrange
        var settings = new PatreonSettings
        {
            ClientId = "test-client-id",
            RelayServerUrl = "https://baketa-relay.workers.dev",
            CacheDurationMinutes = minutes,
            ProTierId = "123456"
        };

        // Act
        var result = settings.ValidateSettings();

        // Assert - Cache duration out of range is a warning, not an error
        Assert.True(result.IsValid); // Always valid (warnings don't affect IsValid)
        var hasWarning = result.GetWarningMessages().Contains("キャッシュ");
        Assert.Equal(shouldHaveWarning, hasWarning);
    }

    [Theory]
    [InlineData(0, true)]    // Too short - warning
    [InlineData(1, false)]   // Minimum valid - no warning
    [InlineData(7, false)]   // Default - no warning
    [InlineData(30, false)]  // Maximum valid - no warning
    [InlineData(31, true)]   // Too long - warning
    public void ValidateSettings_OfflineGracePeriod_ValidationWorks(int days, bool shouldHaveWarning)
    {
        // Arrange
        var settings = new PatreonSettings
        {
            ClientId = "test-client-id",
            RelayServerUrl = "https://baketa-relay.workers.dev",
            OfflineGracePeriodDays = days,
            ProTierId = "123456"
        };

        // Act
        var result = settings.ValidateSettings();

        // Assert - Grace period out of range is a warning, not an error
        Assert.True(result.IsValid); // Always valid (warnings don't affect IsValid)
        var hasWarning = result.GetWarningMessages().Contains("グレースピリオド");
        Assert.Equal(shouldHaveWarning, hasWarning);
    }
}
