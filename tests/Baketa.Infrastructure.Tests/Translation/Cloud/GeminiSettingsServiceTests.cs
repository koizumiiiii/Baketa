using System.Reflection;
using System.Text;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Auth;
using Baketa.Infrastructure.DI.Modules;
using Baketa.Infrastructure.Translation.Cloud;
using Baketa.Infrastructure.Translation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Cloud;

public sealed class GeminiSettingsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "byok-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SetOptionsAsync_WithoutKey_KeepsLocalPreference()
    {
        using var settings = new GeminiSettingsService(_root);
        await settings.SetOptionsAsync(settings.Model, true);
        Assert.False(settings.PreferGemini);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task UpdateAsync_UnchangedSettings_DoesNotUpdateRevisionOrNotifyOrWrite()
    {
        using var settings = new GeminiSettingsService(_root);
        var events = 0;
        settings.Changed += (_, _) => events++;
        await settings.DeleteApiKeyAsync();
        await settings.SetOptionsAsync(settings.Model, settings.PreferGemini);
        Assert.Equal(0, settings.Revision);
        Assert.Equal(0, settings.ApiKeySaveRevision);
        Assert.Equal(0, events);
        Assert.False(Directory.Exists(_root));
        await settings.SaveApiKeyAsync("test-only-credential");
        var path = Path.Combine(_root, "byok", "gemini.dpapi");
        var bytes = await File.ReadAllBytesAsync(path);
        await settings.SaveApiKeyAsync(" test-only-credential ");
        await settings.SetOptionsAsync(settings.Model, settings.PreferGemini);
        Assert.Equal(1, settings.Revision);
        Assert.Equal(2, settings.ApiKeySaveRevision);
        Assert.Equal(1, events);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        await settings.SetOptionsAsync(settings.Model, false);
        Assert.Equal(2, settings.Revision);
        Assert.Equal(2, settings.ApiKeySaveRevision);
        Assert.Equal(2, events);
    }

    [Fact]
    public async Task SaveAndDelete_Key_IsEncryptedAndAvailabilityUpdatesImmediately()
    {
        using var settings = new GeminiSettingsService(_root);
        using var availability = new GeminiTranslationAvailabilityService(settings);
        var events = new List<bool>();
        availability.AvailabilityChanged += (_, e) => events.Add(e.IsEnabled);
        Assert.False(availability.IsEffectivelyEnabled);
        await settings.SaveApiKeyAsync("test-only-credential");
        Assert.True(availability.IsEffectivelyEnabled);
        var stored = await File.ReadAllBytesAsync(Path.Combine(_root, "byok", "gemini.dpapi"));
        Assert.DoesNotContain("test-only-credential", Encoding.UTF8.GetString(stored));
        using (var reloaded = new GeminiSettingsService(_root)) Assert.Equal("test-only-credential", reloaded.GetApiKey());
        await availability.SetPreferredAsync(false);
        Assert.True(availability.IsEntitled);
        Assert.False(availability.IsEffectivelyEnabled);
        await settings.SetOptionsAsync("gemini-flash-latest", true);
        Assert.True(availability.IsEffectivelyEnabled);
        await settings.DeleteApiKeyAsync();
        Assert.False(availability.IsEntitled);
        using var deleted = new GeminiSettingsService(_root);
        Assert.Null(deleted.GetApiKey());
        Assert.False(deleted.PreferGemini);
        Assert.Equal("gemini-flash-latest", deleted.Model);
        Assert.Equal(new[] { true, false, true, false }, events);
    }

    [Fact]
    public async Task StartAsync_LegacyMigration_DoesNotDeleteGeminiKey()
    {
        using var settings = new GeminiSettingsService(_root);
        await settings.SaveApiKeyAsync("test-only-credential");
        var tokens = new Mock<ITokenStorage>(MockBehavior.Strict);
        tokens.Setup(x => x.ClearTokensAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await new LocalCredentialMigrationService(tokens.Object, NullLogger<LocalCredentialMigrationService>.Instance, _root).StartAsync(default);
        using var reloaded = new GeminiSettingsService(_root);
        Assert.Equal("test-only-credential", reloaded.GetApiKey());
        tokens.Verify(x => x.ClearTokensAsync(It.IsAny<CancellationToken>()), Times.Once);
        tokens.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisterCloudServices_KeyPresentOrAbsent_UsesDynamicByokProvider(bool keyPresent)
    {
        using var settings = new GeminiSettingsService(_root);
        if (keyPresent) await settings.SaveApiKeyAsync("test-only-credential");
        var services = new ServiceCollection();
        services.AddLogging();
        // Exercise the production registration without starting unrelated OCR/native services.
        var register = typeof(InfrastructureModule).GetMethod("RegisterCloudAIPhase2Services", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(register);
        register.Invoke(null, [services]);
        services.Replace(ServiceDescriptor.Singleton<IGeminiSettingsService>(settings));
        using var google = new GoogleTranslationSettingsService(_root);
        using var methods = new TranslationMethodService(settings, google, _root);
        services.Replace(ServiceDescriptor.Singleton<IGoogleTranslationSettingsService>(google));
        services.Replace(ServiceDescriptor.Singleton<ITranslationMethodService>(methods));
        await using var provider = services.BuildServiceProvider();
        var translator = provider.GetRequiredKeyedService<ICloudImageTranslator>("primary");
        Assert.IsType<GeminiImageTranslator>(translator);
        Assert.Equal(keyPresent, await translator.IsAvailableAsync());
        Assert.IsType<DisabledCloudImageTranslator>(provider.GetRequiredKeyedService<ICloudImageTranslator>("secondary"));
        await settings.SaveApiKeyAsync("test-only-credential");
        await methods.SelectAsync(Baketa.Core.Models.TranslationMethod.Gemini);
        Assert.True(await translator.IsAvailableAsync());
        await settings.DeleteApiKeyAsync();
        Assert.False(await translator.IsAvailableAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
