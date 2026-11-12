using System.Reactive.Linq;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;
using Baketa.UI.ViewModels.Settings;
using Moq;
using Xunit;

namespace Baketa.UI.Tests.ViewModels.Settings;

public sealed class GeminiSettingsViewModelTests
{
    [Fact]
    public async Task SaveKeyCommand_NewKey_ClearsInputAndEnablesGemini()
    {
        var settings = new Mock<IGeminiSettingsService>();
        settings.SetupGet(x => x.Model).Returns("gemini-flash-lite-latest");
        settings.Setup(x => x.SaveApiKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        using var vm = new GeminiSettingsViewModel(settings.Object, Mock.Of<IGeminiConnectionService>(), Mock.Of<IEventAggregator>());
        vm.ApiKey = "test-only-credential";
        await vm.SaveKeyCommand.Execute();
        settings.Verify(x => x.SaveApiKeyAsync("test-only-credential", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(vm.ApiKey);
        Assert.True(vm.PreferGemini);
        Assert.DoesNotContain("test-only-credential", vm.Status);
    }

    [Fact]
    public async Task SaveKeyCommand_StorageFailure_ClearsInputAndHidesException()
    {
        var settings = new Mock<IGeminiSettingsService>();
        settings.SetupGet(x => x.Model).Returns("gemini-flash-lite-latest");
        settings.Setup(x => x.SaveApiKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("test-only-credential"));
        using var vm = new GeminiSettingsViewModel(settings.Object, Mock.Of<IGeminiConnectionService>(), Mock.Of<IEventAggregator>());
        vm.ApiKey = "test-only-credential";
        await vm.SaveKeyCommand.Execute();
        Assert.Empty(vm.ApiKey);
        Assert.DoesNotContain("test-only-credential", vm.Status);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Commands_DeleteTestAndOptions_CallServicesWithoutReadingSavedKey()
    {
        var settings = new Mock<IGeminiSettingsService>();
        settings.SetupGet(x => x.Model).Returns("gemini-flash-lite-latest");
        settings.Setup(x => x.DeleteApiKeyAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        settings.Setup(x => x.SetOptionsAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var connection = new Mock<IGeminiConnectionService>();
        connection.Setup(x => x.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var vm = new GeminiSettingsViewModel(settings.Object, connection.Object, Mock.Of<IEventAggregator>());
        vm.Model = "gemini-flash-latest";
        vm.PreferGemini = false;
        await vm.SaveOptionsCommand.Execute();
        await vm.TestCommand.Execute();
        await vm.DeleteKeyCommand.Execute();
        settings.Verify(x => x.SetOptionsAsync("gemini-flash-latest", false, It.IsAny<CancellationToken>()), Times.Once);
        settings.Verify(x => x.DeleteApiKeyAsync(It.IsAny<CancellationToken>()), Times.Once);
        connection.Verify(x => x.TestConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
        settings.Verify(x => x.GetApiKey(), Times.Never);
    }
}
