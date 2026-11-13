using Baketa.Core.Models;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;
using Baketa.UI.Resources;
using Baketa.UI.ViewModels.Settings;
using Moq;
using Xunit;

namespace Baketa.UI.Tests.ViewModels.Settings;

[Collection("BYOK status localization")]
public sealed class GeminiSettingsViewModelTests
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
        var settings = new Mock<IGeminiSettingsService>();
        settings.SetupGet(x => x.HasApiKey).Returns(hasKey);
        var methods = new Mock<ITranslationMethodService>();
        methods.SetupGet(x => x.Current).Returns(current);

        // Act
        using var vm = new GeminiSettingsViewModel(settings.Object,
            Mock.Of<IGeminiConnectionService>(), Mock.Of<IEventAggregator>(), methods.Object);

        // Assert
        Assert.Equal(Strings.ResourceManager.GetString(
            hasKey ? "Gemini_KeySaved" : "Gemini_NoKey", Strings.Culture), vm.Status);
        Assert.Equal(hasKey, vm.HasApiKey);
        methods.Verify(x => x.SelectAsync(It.IsAny<TranslationMethod>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveKeyCommand_ThenSelectLocal_KeepsKeyValidationStatus()
    {
        // Arrange: persistence and connection operations use in-memory mocks.
        var settings = new Mock<IGeminiSettingsService>();
        settings.Setup(x => x.SaveApiKeyAsync("test-key", It.IsAny<CancellationToken>()))
            .Callback(() => settings.SetupGet(x => x.HasApiKey).Returns(true)).Returns(Task.CompletedTask);
        var connection = new Mock<IGeminiConnectionService>();
        connection.Setup(x => x.TestConnectionAsync("test-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GeminiConnectionResult.Success);
        var current = TranslationMethod.Local;
        var methods = new Mock<ITranslationMethodService>();
        methods.SetupGet(x => x.Current).Returns(() => current);
        methods.Setup(x => x.SelectAsync(It.IsAny<TranslationMethod>(), It.IsAny<CancellationToken>()))
            .Callback<TranslationMethod, CancellationToken>((method, _) => current = method)
            .Returns(Task.CompletedTask);
        using var vm = new GeminiSettingsViewModel(settings.Object, connection.Object,
            Mock.Of<IEventAggregator>(), methods.Object);
        using var selector = new TranslationMethodViewModel(methods.Object, Mock.Of<IEventAggregator>());

        // Act
        vm.ApiKey = "test-key";
        await vm.SaveKeyCommand.Execute();
        Assert.Equal(TranslationMethod.Gemini, current);
        await selector.SelectLocalCommand.Execute();

        // Assert
        Assert.True(selector.IsLocal);
        Assert.True(vm.HasApiKey);
        Assert.True(vm.IsStatusSuccess);
        Assert.Equal(Strings.ResourceManager.GetString("Gemini_TestSuccess", Strings.Culture), vm.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenLinkCommand_WhileSaving_PreservesBusyAndPreventsDeletion(bool launchFails)
    {
        // Arrange: keep validation pending without accessing a real profile or API.
        var settings = new Mock<IGeminiSettingsService>();
        settings.SetupGet(x => x.HasApiKey).Returns(true);
        var validation = new TaskCompletionSource<GeminiConnectionResult>();
        var connection = new Mock<IGeminiConnectionService>();
        connection.Setup(x => x.TestConnectionAsync("test-key", It.IsAny<CancellationToken>()))
            .Returns(validation.Task);
        var opened = new List<string>();
        using var vm = new GeminiSettingsViewModel(
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
            var links = new[] { vm.OpenStudioCommand };

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
            validation.SetResult(GeminiConnectionResult.Success);
            await save;
        }
        Assert.False(vm.IsBusy);
        Assert.True(await vm.DeleteKeyCommand.CanExecute.FirstAsync());
        settings.Verify(x => x.SaveApiKeyAsync("test-key", It.IsAny<CancellationToken>()), Times.Once);
    }

    private const string Key = "test-only-credential";
    private static Mock<IGeminiSettingsService> Settings(bool hasKey = false)
    {
        var settings = new Mock<IGeminiSettingsService>();
        settings.SetupGet(x => x.Model).Returns("gemini-flash-lite-latest");
        settings.SetupGet(x => x.HasApiKey).Returns(hasKey);
        settings.SetupGet(x => x.PreferGemini).Returns(true);
        settings.Setup(x => x.SaveApiKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => settings.SetupGet(x => x.HasApiKey).Returns(true)).Returns(Task.CompletedTask);
        settings.Setup(x => x.DeleteApiKeyAsync(It.IsAny<CancellationToken>()))
            .Callback(() => settings.SetupGet(x => x.HasApiKey).Returns(false)).Returns(Task.CompletedTask);
        settings.Setup(x => x.SetOptionsAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<string, bool, CancellationToken>((_, selected, _) => settings.SetupGet(x => x.PreferGemini).Returns(selected)).Returns(Task.CompletedTask);
        return settings;
    }

    private static Mock<IGeminiConnectionService> Connection(GeminiConnectionResult result)
    {
        var connection = new Mock<IGeminiConnectionService>();
        connection.Setup(x => x.TestConnectionAsync(Key, It.IsAny<CancellationToken>())).ReturnsAsync(result);
        return connection;
    }

    [Fact]
    public async Task SaveKeyCommand_MethodSaveFails_ReflectsSavedKeyAndAllowsDeletion()
    {
        var settings = new Mock<IGeminiSettingsService>();
        settings.Setup(x => x.SaveApiKeyAsync("test-key", It.IsAny<CancellationToken>()))
            .Callback(() => settings.SetupGet(x => x.HasApiKey).Returns(true)).Returns(Task.CompletedTask);
        settings.Setup(x => x.DeleteApiKeyAsync(It.IsAny<CancellationToken>()))
            .Callback(() => settings.SetupGet(x => x.HasApiKey).Returns(false)).Returns(Task.CompletedTask);
        var connection = new Mock<IGeminiConnectionService>();
        connection.Setup(x => x.TestConnectionAsync("test-key", It.IsAny<CancellationToken>())).ReturnsAsync(GeminiConnectionResult.Success);
        var methods = new Mock<ITranslationMethodService>();
        methods.Setup(x => x.SelectAsync(TranslationMethod.Gemini, It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("test-only"));
        using var vm = new GeminiSettingsViewModel(settings.Object, connection.Object, Mock.Of<IEventAggregator>(), methods.Object);
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

    [Fact]
    public async Task SaveKeyCommand_NewKey_VerifiesBeforeSavingAndEnablesGemini()
    {
        var settings = Settings();
        var connection = Connection(GeminiConnectionResult.Success);
        var verified = false;
        connection.Setup(x => x.TestConnectionAsync(Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { verified = true; return GeminiConnectionResult.Success; });
        settings.Setup(x => x.SaveApiKeyAsync(Key, It.IsAny<CancellationToken>()))
            .Callback(() => { Assert.True(verified); settings.SetupGet(x => x.HasApiKey).Returns(true); }).Returns(Task.CompletedTask);
        using var vm = new GeminiSettingsViewModel(settings.Object, connection.Object, Mock.Of<IEventAggregator>());
        Assert.False(await vm.SaveKeyCommand.CanExecute.FirstAsync());
        vm.ApiKey = " " + Key + " ";
        Assert.True(await vm.SaveKeyCommand.CanExecute.FirstAsync());
        await vm.SaveKeyCommand.Execute();
        settings.Verify(x => x.SaveApiKeyAsync(Key, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(vm.ApiKey);
        Assert.True(vm.PreferGemini);
        Assert.True(vm.HasApiKey);
        Assert.True(vm.IsStatusSuccess);
        Assert.DoesNotContain(Key, vm.Status);
    }

    [Theory]
    [InlineData(GeminiConnectionResult.InvalidKey, "Gemini_TestInvalidKey")]
    [InlineData(GeminiConnectionResult.RateLimited, "Gemini_TestRateLimited")]
    [InlineData(GeminiConnectionResult.NetworkError, "Gemini_TestNetworkError")]
    [InlineData(GeminiConnectionResult.Timeout, "Gemini_TestTimeout")]
    [InlineData(GeminiConnectionResult.Failed, "Gemini_TestFailed")]
    [InlineData(GeminiConnectionResult.NoKey, "Gemini_TestFailed")]
    public async Task SaveKeyCommand_VerificationFails_DoesNotSave(GeminiConnectionResult result, string resource)
    {
        var settings = Settings();
        using var vm = new GeminiSettingsViewModel(settings.Object, Connection(result).Object, Mock.Of<IEventAggregator>());
        vm.ApiKey = Key;
        await vm.SaveKeyCommand.Execute();
        settings.Verify(x => x.SaveApiKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(Strings.ResourceManager.GetString(resource, Strings.Culture), vm.Status);
        Assert.True(vm.IsStatusError);
        Assert.False(vm.HasApiKey);
        Assert.False(vm.PreferGemini);
        Assert.Empty(vm.ApiKey);
    }

    [Fact]
    public async Task SaveKeyCommand_StorageFailure_ClearsInputAndHidesException()
    {
        var settings = Settings();
        settings.Setup(x => x.SaveApiKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException(Key));
        using var vm = new GeminiSettingsViewModel(settings.Object, Connection(GeminiConnectionResult.Success).Object, Mock.Of<IEventAggregator>());
        vm.ApiKey = Key;
        await vm.SaveKeyCommand.Execute();
        Assert.Empty(vm.ApiKey);
        Assert.DoesNotContain(Key, vm.Status);
        Assert.True(vm.IsStatusError);
        Assert.False(vm.PreferGemini);
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task SaveKeyCommand_EmptyInput_Disabled(string input)
    {
        using var vm = new GeminiSettingsViewModel(Settings().Object, Connection(GeminiConnectionResult.Success).Object, Mock.Of<IEventAggregator>());
        vm.ApiKey = input;
        Assert.False(await vm.SaveKeyCommand.CanExecute.FirstAsync());
        Assert.False(await vm.SelectGeminiCommand.CanExecute.FirstAsync());
        Assert.False(vm.PreferGemini);
    }

    [Fact]
    public async Task SaveKeyCommand_VerificationPending_DisablesMutationsAndShowsProgress()
    {
        var pending = new TaskCompletionSource<GeminiConnectionResult>();
        var connection = Connection(GeminiConnectionResult.Success);
        connection.Setup(x => x.TestConnectionAsync(Key, It.IsAny<CancellationToken>())).Returns(pending.Task);
        using var vm = new GeminiSettingsViewModel(Settings(true).Object, connection.Object, Mock.Of<IEventAggregator>());
        vm.ApiKey = Key;
        var saving = vm.SaveKeyCommand.Execute().ToTask();
        Assert.True(vm.IsBusy);
        Assert.Equal(Strings.ResourceManager.GetString("Gemini_Checking", Strings.Culture), vm.Status);
        Assert.False(await vm.SaveKeyCommand.CanExecute.FirstAsync());
        Assert.False(await vm.DeleteKeyCommand.CanExecute.FirstAsync());
        Assert.False(await vm.SelectLocalCommand.CanExecute.FirstAsync());
        pending.SetResult(GeminiConnectionResult.Success);
        await saving;
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task SelectAndDeleteCommands_PersistImmediatelyAndReturnToLocal()
    {
        var settings = Settings(true);
        using var vm = new GeminiSettingsViewModel(settings.Object, Connection(GeminiConnectionResult.Success).Object, Mock.Of<IEventAggregator>());
        await vm.SelectLocalCommand.Execute();
        Assert.False(vm.PreferGemini);
        settings.Verify(x => x.SetOptionsAsync("gemini-flash-lite-latest", false, It.IsAny<CancellationToken>()), Times.Once);
        await vm.SelectGeminiCommand.Execute();
        Assert.True(vm.PreferGemini);
        settings.Verify(x => x.SetOptionsAsync("gemini-flash-lite-latest", true, It.IsAny<CancellationToken>()), Times.Once);
        await vm.DeleteKeyCommand.Execute();
        settings.Verify(x => x.DeleteApiKeyAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(vm.PreferGemini);
        Assert.False(vm.HasApiKey);
        Assert.False(await vm.SelectGeminiCommand.CanExecute.FirstAsync());
        Assert.Equal(Strings.ResourceManager.GetString("Gemini_NoKey", Strings.Culture), vm.Status);
        settings.Verify(x => x.GetApiKey(), Times.Never);
    }

    [Fact]
    public async Task SelectLocalCommand_StorageFailure_PreservesSelection()
    {
        var settings = Settings(true);
        settings.Setup(x => x.SetOptionsAsync(It.IsAny<string>(), false, It.IsAny<CancellationToken>())).ThrowsAsync(new IOException(Key));
        using var vm = new GeminiSettingsViewModel(settings.Object, Connection(GeminiConnectionResult.Success).Object, Mock.Of<IEventAggregator>());
        await vm.SelectLocalCommand.Execute();
        Assert.True(vm.PreferGemini);
        Assert.True(vm.IsStatusError);
        Assert.DoesNotContain(Key, vm.Status);
    }
}
