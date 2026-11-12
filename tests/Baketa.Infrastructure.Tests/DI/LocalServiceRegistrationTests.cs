using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.CrashReporting;
using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Settings;
using Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Analytics;
using Baketa.Infrastructure.Auth;
using Baketa.Infrastructure.CrashReporting;
using Baketa.Infrastructure.DI.Modules;
using Baketa.Infrastructure.License.Services;
using Baketa.Infrastructure.Services.Settings;
using Baketa.Infrastructure.Translation.Cloud;
using Baketa.Infrastructure.Translation.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.DI;

public class LocalServiceRegistrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RegisterServices_BothInfrastructureOverloads_ResolveByokWithoutRetiredServices(bool withConfiguration, bool hasKey)
    {
        // Legacy cloud preferences must not reactivate retired network services.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Translation:EnableCloudAiTranslation"] = "true",
            ["Translation:UseLocalEngine"] = "false",
            ["CloudTranslation:PrimaryProviderId"] = "gemini",
            ["CloudTranslation:SecondaryProviderId"] = "openai"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        var module = new InfrastructureModule();
        if (withConfiguration)
            module.RegisterServices(services, configuration);
        else
            module.RegisterServices(services);

        // Never read or mutate the real user profile in registration tests.
        var byok = new Mock<IGeminiSettingsService>();
        byok.SetupGet(x => x.HasApiKey).Returns(hasKey);
        byok.SetupGet(x => x.PreferGemini).Returns(true);
        byok.SetupGet(x => x.Model).Returns("gemini-flash-lite-latest");
        byok.Setup(x => x.SetOptionsAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        services.AddSingleton(byok.Object);
        var http = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        services.AddSingleton(http.Object);
        await using var provider = services.BuildServiceProvider();

        // Act: resolve the actual registrations, avoiding unrelated OCR/model startup.
        var primary = provider.GetRequiredKeyedService<ICloudImageTranslator>("primary");
        var secondary = provider.GetRequiredKeyedService<ICloudImageTranslator>("secondary");
        var availability = provider.GetRequiredService<ICloudTranslationAvailabilityService>();
        var analytics = provider.GetRequiredService<IUsageAnalyticsService>();
        var crashSender = provider.GetRequiredService<ICrashReportSender>();
        var consent = provider.GetRequiredService<IConsentService>();
        await availability.SetPreferredAsync(true);
        analytics.TrackEvent("session_start");
        await consent.SyncLocalConsentToServerAsync("legacy-user", "legacy-session");

        // Assert: both overloads expose the same network-free boundaries.
        Assert.IsType<GeminiImageTranslator>(primary);
        Assert.IsType<DisabledCloudImageTranslator>(secondary);
        Assert.Equal(hasKey, await primary.IsAvailableAsync());
        Assert.False(await secondary.IsAvailableAsync());
        Assert.IsType<GeminiTranslationAvailabilityService>(availability);
        Assert.Equal(hasKey, availability.IsEffectivelyEnabled);
        Assert.IsType<NullUsageAnalyticsService>(analytics);
        Assert.False(analytics.IsEnabled);
        Assert.False(await analytics.FlushAsync());
        Assert.IsType<NullCrashReportSender>(crashSender);
        Assert.IsType<LocalConsentService>(consent);
        Assert.Equal(ConsentVerificationState.LocalOnly, await consent.EnsureConsentValidAsync("legacy-session"));
        Assert.Null(provider.GetService<RelayServerClient>());
        Assert.Null(provider.GetService<TextTranslationClient>());
        Assert.Null(provider.GetService<PrimaryCloudTranslator>());
        Assert.Null(provider.GetService<SecondaryCloudTranslator>());
        Assert.Null(provider.GetService<DirectOpenAIImageTranslator>());
        Assert.Null(provider.GetService<CrashReportSender>());
        Assert.Null(provider.GetService<UsageAnalyticsService>());
        http.VerifyNoOtherCalls();
    }

    [Fact]
    public void RegisterServices_WithLegacyConfiguration_OnlyLocalImplementationsResolve()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["License:EnableMockMode"] = "false",
            ["Patreon:ClientId"] = "legacy-client",
            ["Auth:AutoRefreshSession"] = "true"
        }).Build());
        services.AddSingleton(Mock.Of<ITokenStorage>(MockBehavior.Strict));
        services.AddSingleton(Mock.Of<IUnifiedSettingsService>(MockBehavior.Strict));
        new AuthModule().RegisterServices(services);
        new LicenseModule().RegisterServices(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<NullAuthService>(provider.GetRequiredService<IAuthService>());
        Assert.IsType<NullAuthService>(provider.GetRequiredService<IUserService>());
        Assert.IsType<NullAuthService>(provider.GetRequiredService<IOAuthCallbackHandler>());
        Assert.IsType<NullAuthService>(provider.GetRequiredService<IJwtTokenService>());
        Assert.IsType<LocalLicenseManager>(provider.GetRequiredService<ILicenseManager>());
        Assert.IsType<LocalLicenseManager>(provider.GetRequiredService<ILicenseInfoProvider>());
        Assert.IsType<NullAccountServices>(provider.GetRequiredService<IPatreonOAuthService>());
        Assert.IsType<NullAccountServices>(provider.GetRequiredService<IPromotionCodeService>());
        Assert.IsType<NullAccountServices>(provider.GetRequiredService<IBonusTokenService>());
        Assert.IsType<LocalCredentialMigrationService>(Assert.Single(provider.GetServices<IHostedService>()));
        Assert.Null(provider.GetService<SupabaseAuthService>());
        Assert.Null(provider.GetService<LicenseManager>());
        Assert.Null(provider.GetService<PatreonOAuthService>());
    }
}
