using System.Reactive.Linq;
using Baketa.Core.Abstractions.Events;
using System.Reactive.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models;
using Baketa.UI.Resources;
using Baketa.UI.ViewModels.Settings;
using Moq;
using Xunit;

namespace Baketa.UI.Tests.ViewModels.Settings;

[Collection("BYOK status localization")]
public sealed class GoogleTranslationSettingsViewModelTests
{
    [Theory]
    [InlineData(TranslationMethod.Local, false)]
    [InlineData(TranslationMethod.Google, false)]
    [InlineData(TranslationMethod.Gemini, false)]
    [InlineData(TranslationMethod.Local, true)]
    [InlineData(TranslationMethod.Google, true)]
    [InlineData(TranslationMethod.Gemini, true)]
    public void Constructor_AnySelectedMethod_ShowsOnlyKeyState(TranslationMethod current, bool hasKey)
    {
        // Arrange
        var settings = new Mock<IGoogleTranslationSettingsService>();
        settings.SetupGet(x => x.HasApiKey).Returns(hasKey);
        var methods = new Mock<ITranslationMethodService>();
        methods.SetupGet(x => x.Current).Returns(current);

        // Act
        using var vm = new GoogleTranslationSettingsViewModel(settings.Object,
            Mock.Of<IGoogleTranslationConnectionService>(), Mock.Of<IEventAggregator>(), methods.Object);

        // Assert
        Assert.Equal(Strings.ResourceManager.GetString(
            hasKey ? "Google_KeySaved" : "Google_NoKey", Strings.Culture), vm.Status);
        Assert.Equal(hasKey, vm.HasApiKey);
        methods.Verify(x => x.SelectAsync(It.IsAny<TranslationMethod>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveKeyCommand_ThenSelectLocal_KeepsKeyValidationStatus()
    {
        // Arrange: persistence and connection operations use in-memory mocks.
        var settings = new Mock<IGoogleTranslationSettingsService>();
        settings.Setup(x => x.SaveApiKeyAsync("test-key", It.IsAny<CancellationToken>()))
            .Callback(() => settings.SetupGet(x => x.HasApiKey).Returns(true)).Returns(Task.CompletedTask);
        var connection = new Mock<IGoogleTranslationConnectionService>();
        connection.Setup(x => x.TestConnectionAsync("test-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoogleTranslationResult.Success);
        var current = TranslationMethod.Local;
        var methods = new Mock<ITranslationMethodService>();
        methods.SetupGet(x => x.Current).Returns(() => current);
        methods.Setup(x => x.SelectAsync(It.IsAny<TranslationMethod>(), It.IsAny<CancellationToken>()))
            .Callback<TranslationMethod, CancellationToken>((method, _) => current = method)
            .Returns(Task.CompletedTask);
        using var vm = new GoogleTranslationSettingsViewModel(settings.Object, connection.Object,
            Mock.Of<IEventAggregator>(), methods.Object);
        using var selector = new TranslationMethodViewModel(methods.Object, Mock.Of<IEventAggregator>());

        // Act
        vm.ApiKey = "test-key";
        await vm.SaveKeyCommand.Execute();
        Assert.Equal(TranslationMethod.Google, current);
        await selector.SelectLocalCommand.Execute();

        // Assert
        Assert.True(selector.IsLocal);
        Assert.True(vm.HasApiKey);
        Assert.True(vm.IsStatusSuccess);
        Assert.Equal(Strings.ResourceManager.GetString("Google_TestSuccess", Strings.Culture), vm.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenLinkCommand_WhileSaving_PreservesBusyAndPreventsDeletion(bool launchFails)
    {
        // Arrange: keep validation pending without accessing a real profile or API.
        var settings = new Mock<IGoogleTranslationSettingsService>();
        settings.SetupGet(x => x.HasApiKey).Returns(true);
        var validation = new TaskCompletionSource<GoogleTranslationResult>();
        var connection = new Mock<IGoogleTranslationConnectionService>();
        connection.Setup(x => x.TestConnectionAsync("test-key", It.IsAny<CancellationToken>()))
            .Returns(validation.Task);
        var opened = new List<string>();
        using var vm = new GoogleTranslationSettingsViewModel(
            settings.Object, connection.Object, Mock.Of<IEventAggregator>(), openUrl: url =>
            {
                opened.Add(url);
                if (launchFails) throw new InvalidOperationException("test-only");
            });
        vm.ApiKey = "test-key";
        var save = vm.SaveKeyCommand.Execute().ToTask();
        try
        {
            Assert.True(vm.IsBusy);
            var status = vm.Status;
            var links = new[] { vm.OpenStudioCommand, vm.OpenApiCommand, vm.OpenPricingCommand, vm.OpenQuotaCommand };

            // Act: navigate while validation is still pending.
            foreach (var link in links)
            {
                Assert.True(await link.CanExecute.FirstAsync());
                await link.Execute();

                // Assert: navigation cannot unlock or overwrite the pending save.
                Assert.True(vm.IsBusy);
                Assert.Equal(status, vm.Status);
                Assert.False(await vm.DeleteKeyCommand.CanExecute.FirstAsync());
                Assert.False(await vm.SaveKeyCommand.CanExecute.FirstAsync());
                // Direct execution bypasses CanExecute; the operation guard must also hold.
                await vm.DeleteKeyCommand.Execute();
                Assert.True(vm.IsBusy);
            }
            Assert.Equal(links.Length, opened.Count);
            settings.Verify(x => x.DeleteApiKeyAsync(It.IsAny<CancellationToken>()), Times.Never);
            settings.Verify(x => x.SaveApiKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            validation.SetResult(GoogleTranslationResult.Success);
            await save;
        }
        Assert.False(vm.IsBusy);
        Assert.True(await vm.DeleteKeyCommand.CanExecute.FirstAsync());
        settings.Verify(x => x.SaveApiKeyAsync("test-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveKeyCommand_MethodSaveFails_ReflectsSavedKeyAndAllowsDeletion()
    {
        var settings = new Mock<IGoogleTranslationSettingsService>();
        settings.Setup(x => x.SaveApiKeyAsync("test-key", It.IsAny<CancellationToken>()))
            .Callback(() => settings.SetupGet(x => x.HasApiKey).Returns(true)).Returns(Task.CompletedTask);
        settings.Setup(x => x.DeleteApiKeyAsync(It.IsAny<CancellationToken>()))
            .Callback(() => settings.SetupGet(x => x.HasApiKey).Returns(false)).Returns(Task.CompletedTask);
        var connection = new Mock<IGoogleTranslationConnectionService>();
        connection.Setup(x => x.TestConnectionAsync("test-key", It.IsAny<CancellationToken>())).ReturnsAsync(GoogleTranslationResult.Success);
        var methods = new Mock<ITranslationMethodService>();
        methods.Setup(x => x.SelectAsync(TranslationMethod.Google, It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("test-only"));
        using var vm = new GoogleTranslationSettingsViewModel(settings.Object, connection.Object, Mock.Of<IEventAggregator>(), methods.Object);
        vm.ApiKey = "test-key";
        await vm.SaveKeyCommand.Execute();
        Assert.True(vm.HasApiKey);
        Assert.True(vm.IsStatusError);
        Assert.False(vm.IsStatusSuccess);
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.ApiKey);
        Assert.Equal(Strings.ResourceManager.GetString("BYOK_KeySavedMethodFailed", Strings.Culture), vm.Status);
        Assert.True(await vm.DeleteKeyCommand.CanExecute.FirstAsync());
        await vm.DeleteKeyCommand.Execute();
        Assert.False(vm.HasApiKey);
    }

    [Theory]
    [InlineData(GoogleTranslationResult.InvalidKey, "Google_TestInvalidKey")]
    [InlineData(GoogleTranslationResult.ServiceDisabled, "Google_ServiceDisabled")]
    [InlineData(GoogleTranslationResult.BillingDisabled, "Google_BillingDisabled")]
    [InlineData(GoogleTranslationResult.Forbidden, "Google_Forbidden")]
    [InlineData(GoogleTranslationResult.RateLimited, "Google_TestRateLimited")]
    [InlineData(GoogleTranslationResult.NetworkError, "Google_TestNetworkError")]
    [InlineData(GoogleTranslationResult.Timeout, "Google_TestTimeout")]
    [InlineData(GoogleTranslationResult.Failed, "Google_TestFailed")]
    public async Task SaveKeyCommand_ValidationFails_DoesNotSaveOrSwitch(GoogleTranslationResult result, string resource)
    {
        var settings = new Mock<IGoogleTranslationSettingsService>();
        var connection = new Mock<IGoogleTranslationConnectionService>();
        connection.Setup(x => x.TestConnectionAsync("test-key", It.IsAny<CancellationToken>())).ReturnsAsync(result);
        var methods = new Mock<ITranslationMethodService>();
        using var vm = new GoogleTranslationSettingsViewModel(settings.Object, connection.Object, Mock.Of<IEventAggregator>(), methods.Object);
        vm.ApiKey = "test-key";
        await vm.SaveKeyCommand.Execute();
        Assert.Equal(Strings.ResourceManager.GetString(resource, Strings.Culture), vm.Status);
        Assert.True(vm.IsStatusError);
        Assert.Empty(vm.ApiKey);
        settings.Verify(x => x.SaveApiKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        methods.Verify(x => x.SelectAsync(It.IsAny<TranslationMethod>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveKeyCommand_Success_ValidatesThenSavesThenSelects()
    {
        var sequence = new MockSequence();
        var settings = new Mock<IGoogleTranslationSettingsService>(MockBehavior.Strict);
        settings.SetupGet(x => x.HasApiKey).Returns(false);
        var connection = new Mock<IGoogleTranslationConnectionService>(MockBehavior.Strict);
        var methods = new Mock<ITranslationMethodService>(MockBehavior.Strict);
        connection.InSequence(sequence).Setup(x => x.TestConnectionAsync("test-key", It.IsAny<CancellationToken>())).ReturnsAsync(GoogleTranslationResult.Success);
        settings.InSequence(sequence).Setup(x => x.SaveApiKeyAsync("test-key", It.IsAny<CancellationToken>()))
            .Callback(() => settings.SetupGet(x => x.HasApiKey).Returns(true)).Returns(Task.CompletedTask);
        methods.InSequence(sequence).Setup(x => x.SelectAsync(TranslationMethod.Google, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        using var vm = new GoogleTranslationSettingsViewModel(settings.Object, connection.Object, Mock.Of<IEventAggregator>(), methods.Object);
        vm.ApiKey = " test-key ";
        await vm.SaveKeyCommand.Execute();
        Assert.True(vm.IsStatusSuccess);
        Assert.True(vm.HasApiKey);
        Assert.Empty(vm.ApiKey);
        connection.VerifyAll(); settings.VerifyAll(); methods.VerifyAll();
    }

    [Fact]
    public async Task DeleteKeyCommand_RequestsConditionalFallback()
    {
        var settings = new Mock<IGoogleTranslationSettingsService>();
        settings.SetupGet(x => x.HasApiKey).Returns(true);
        settings.Setup(x => x.DeleteApiKeyAsync(It.IsAny<CancellationToken>()))
            .Callback(() => settings.SetupGet(x => x.HasApiKey).Returns(false)).Returns(Task.CompletedTask);
        var methods = new Mock<ITranslationMethodService>();
        methods.Setup(x => x.SelectIfCurrentAsync(TranslationMethod.Google, TranslationMethod.Local, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        using var vm = new GoogleTranslationSettingsViewModel(settings.Object, Mock.Of<IGoogleTranslationConnectionService>(), Mock.Of<IEventAggregator>(), methods.Object);
        await vm.DeleteKeyCommand.Execute();
        methods.Verify(x => x.SelectIfCurrentAsync(TranslationMethod.Google, TranslationMethod.Local, It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(vm.HasApiKey);
    }

    [Fact]
    public async Task TranslationMethodViewModel_MissingKeys_DisablesCloudChoices()
    {
        var methods = new Mock<ITranslationMethodService>();
        using var vm = new TranslationMethodViewModel(methods.Object, Mock.Of<IEventAggregator>());
        Assert.False(await vm.SelectGoogleCommand.CanExecute.FirstAsync());
        Assert.False(await vm.SelectGeminiCommand.CanExecute.FirstAsync());
        Assert.True(await vm.SelectLocalCommand.CanExecute.FirstAsync());
        Assert.True(vm.IsLocal);
    }
}
