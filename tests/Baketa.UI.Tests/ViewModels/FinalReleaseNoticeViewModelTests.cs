using Baketa.Core.Services;
using Baketa.Core.Settings;
using Baketa.Core.Settings.Migration;
using Baketa.UI.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Baketa.UI.Tests.ViewModels;

public sealed class FinalReleaseNoticeViewModelTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task CompleteAsync_WithDialogResult_HandlesSaveAndNavigationIndependently(
        bool openSettings, bool saveFails, bool navigationFails)
    {
        // Arrange
        var current = new AppSettings();
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetSettings()).Returns(current);
        var saveError = new IOException("Save failed");
        settings.Setup(s => s.SetSettingsAsync(It.IsAny<AppSettings>()))
            .Returns(saveFails ? Task.FromException(saveError) : Task.CompletedTask);
        var logger = new Mock<ILogger>();
        var viewModel = new FinalReleaseNoticeViewModel(settings.Object);
        var navigationError = new InvalidOperationException("Navigation failed");
        var navigationCalls = 0;

        // Act
        await viewModel.CompleteAsync(openSettings, () =>
        {
            navigationCalls++;
            settings.Verify(s => s.SetSettingsAsync(current), Times.Once);
            if (navigationFails) throw navigationError;
            return Task.CompletedTask;
        }, logger.Object);

        // Assert
        settings.Verify(s => s.SetSettingsAsync(current), Times.Once);
        Assert.Equal(openSettings ? 1 : 0, navigationCalls);
        Assert.Equal(saveFails, viewModel.ShouldShow);
        VerifyWarning(logger, saveError, saveFails);
        VerifyWarning(logger, navigationError, openSettings && navigationFails);
    }

    private static void VerifyWarning(Mock<ILogger> logger, Exception exception, bool expected)
    {
        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            exception,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            expected ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ShouldShow_WithAcknowledgement_ReturnsExpected(bool acknowledged, bool expected)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetSettings()).Returns(new AppSettings { FinalReleaseNoticeAcknowledged = acknowledged });
        var viewModel = new FinalReleaseNoticeViewModel(settings.Object);

        Assert.Equal(expected, viewModel.ShouldShow);
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenClosed_PersistsAcrossReload()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-temp", Guid.NewGuid().ToString("N"));
        using var settings = new EnhancedSettingsService(
            NullLogger<EnhancedSettingsService>.Instance,
            Mock.Of<ISettingMetadataService>(), Mock.Of<ISettingsMigrationManager>(),
            Path.Combine(directory, "settings.json"));
        var viewModel = new FinalReleaseNoticeViewModel(settings);

        await viewModel.AcknowledgeAsync();
        await settings.ReloadAsync();

        Assert.False(new FinalReleaseNoticeViewModel(settings).ShouldShow);
        Assert.Contains("\"FinalReleaseNoticeAcknowledged\": true", await File.ReadAllTextAsync(Path.Combine(directory, "settings.json")));
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenSaveFails_RemainsUnacknowledged()
    {
        var current = new AppSettings();
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetSettings()).Returns(current);
        settings.Setup(s => s.SetSettingsAsync(It.IsAny<AppSettings>())).ThrowsAsync(new IOException());
        var viewModel = new FinalReleaseNoticeViewModel(settings.Object);

        await Assert.ThrowsAsync<IOException>(viewModel.AcknowledgeAsync);

        Assert.True(viewModel.ShouldShow);
    }
}
