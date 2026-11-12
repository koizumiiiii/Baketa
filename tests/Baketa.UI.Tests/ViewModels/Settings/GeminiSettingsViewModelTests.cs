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

public sealed class GeminiSettingsViewModelTests
{
    private const string Key = "test-only-credential";
    private static Mock<IGeminiSettingsService> Settings(bool hasKey = false)
    {
        var settings = new Mock<IGeminiSettingsService>();
        settings.SetupGet(x => x.Model).Returns("gemini-flash-lite-latest");
        settings.SetupGet(x => x.HasApiKey).Returns(hasKey);
        settings.SetupGet(x => x.PreferGemini).Returns(true);
        return settings;
    }

    private static Mock<IGeminiConnectionService> Connection(GeminiConnectionResult result)
    {
        var connection = new Mock<IGeminiConnectionService>();
        connection.Setup(x => x.TestConnectionAsync(Key, It.IsAny<CancellationToken>())).ReturnsAsync(result);
        return connection;
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
            .Callback(() => Assert.True(verified)).Returns(Task.CompletedTask);
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
