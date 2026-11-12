using System.Reflection;
using System.Text;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models;
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

public sealed class GoogleTranslationSettingsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "google-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveApiKeyAsync_Encrypted_IsIndependentAndSurvivesLegacyMigration()
    {
        using var google = new GoogleTranslationSettingsService(_root);
        using var gemini = new GeminiSettingsService(_root);
        await google.SaveApiKeyAsync("google-test-key");
        await gemini.SaveApiKeyAsync("gemini-test-key");
        var encrypted = await File.ReadAllBytesAsync(Path.Combine(_root, "byok", "google-translation.dpapi"));
        Assert.DoesNotContain("google-test-key", Encoding.UTF8.GetString(encrypted));
        await google.SaveApiKeyAsync("google-test-key");
        Assert.Equal(2, google.ApiKeySaveRevision);
        var tokens = new Mock<ITokenStorage>(MockBehavior.Strict);
        tokens.Setup(x => x.ClearTokensAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await new LocalCredentialMigrationService(tokens.Object, NullLogger<LocalCredentialMigrationService>.Instance, _root).StartAsync(default);
        using var loaded = new GoogleTranslationSettingsService(_root);
        Assert.Equal("google-test-key", loaded.GetApiKey());
        await google.DeleteApiKeyAsync();
        Assert.False(google.HasApiKey);
        Assert.Equal("gemini-test-key", gemini.GetApiKey());
        using var deleted = new GoogleTranslationSettingsService(_root);
        Assert.False(deleted.HasApiKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TranslationMethod_LegacyPreference_MigratesAndPersistsThreeChoices(bool preferGemini)
    {
        using var gemini = new GeminiSettingsService(_root);
        using var google = new GoogleTranslationSettingsService(_root);
        await gemini.SaveApiKeyAsync("gemini-test-key");
        await gemini.SetOptionsAsync(gemini.Model, preferGemini);
        using var methods = new TranslationMethodService(gemini, google, _root);
        Assert.Equal(preferGemini ? TranslationMethod.Gemini : TranslationMethod.Local, methods.Current);
        Assert.False(methods.CanSelect(TranslationMethod.Google));
        await methods.SelectAsync(TranslationMethod.Google);
        Assert.NotEqual(TranslationMethod.Google, methods.Current);
        await google.SaveApiKeyAsync("google-test-key");
        using var availability = new GeminiTranslationAvailabilityService(gemini, methods: methods);
        foreach (var method in new[] { TranslationMethod.Google, TranslationMethod.Gemini, TranslationMethod.Local })
        {
            await methods.SelectAsync(method);
            using var reloaded = new TranslationMethodService(gemini, google, _root);
            Assert.Equal(method, reloaded.Current);
            Assert.Equal(method == TranslationMethod.Gemini, availability.IsEffectivelyEnabled);
        }
        var stored = await File.ReadAllTextAsync(Path.Combine(_root, "byok", "translation-method.json"));
        Assert.DoesNotContain("test-key", stored);
    }

    [Theory]
    [InlineData(TranslationMethod.Google)]
    [InlineData(TranslationMethod.Gemini)]
    public async Task DeleteApiKeyAsync_SelectedProvider_ReturnsToLocal(TranslationMethod method)
    {
        using var gemini = new GeminiSettingsService(_root);
        using var google = new GoogleTranslationSettingsService(_root);
        await gemini.SaveApiKeyAsync("gemini-test-key");
        await google.SaveApiKeyAsync("google-test-key");
        using var methods = new TranslationMethodService(gemini, google, _root);
        await methods.SelectAsync(method);
        if (method == TranslationMethod.Google) await google.DeleteApiKeyAsync();
        else await gemini.DeleteApiKeyAsync();
        Assert.Equal(TranslationMethod.Local, methods.Current);
        using var reloaded = new TranslationMethodService(gemini, google, _root);
        Assert.Equal(TranslationMethod.Local, reloaded.Current);
    }

    [Fact]
    public async Task RegisterCloudServices_ResolvesGoogleAndCentralSelectionWithoutNetwork()
    {
        using var gemini = new GeminiSettingsService(_root);
        using var google = new GoogleTranslationSettingsService(_root);
        using var methods = new TranslationMethodService(gemini, google, _root);
        var services = new ServiceCollection();
        services.AddLogging();
        var register = typeof(InfrastructureModule).GetMethod("RegisterCloudAIPhase2Services", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(register);
        register.Invoke(null, [services]);
        services.Replace(ServiceDescriptor.Singleton<IGeminiSettingsService>(gemini));
        services.Replace(ServiceDescriptor.Singleton<IGoogleTranslationSettingsService>(google));
        services.Replace(ServiceDescriptor.Singleton<ITranslationMethodService>(methods));
        await using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<GoogleTranslationEngine>(), provider.GetRequiredService<IGoogleTranslationConnectionService>());
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(GoogleTranslationEngine.ClientName);
        Assert.Equal(TimeSpan.FromSeconds(15), client.Timeout);
        await gemini.SaveApiKeyAsync("gemini-test-key");
        await google.SaveApiKeyAsync("google-test-key");
        await methods.SelectAsync(TranslationMethod.Google);
        Assert.False(await provider.GetRequiredKeyedService<ICloudImageTranslator>("primary").IsAvailableAsync());
        await methods.SelectAsync(TranslationMethod.Gemini);
        Assert.True(await provider.GetRequiredKeyedService<ICloudImageTranslator>("primary").IsAvailableAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
