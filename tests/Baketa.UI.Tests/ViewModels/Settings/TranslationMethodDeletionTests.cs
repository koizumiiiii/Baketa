using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models;
using Baketa.Infrastructure.Translation.Cloud;
using Baketa.UI.ViewModels.Settings;
using Moq;
using Xunit;

namespace Baketa.UI.Tests.ViewModels.Settings;

[Collection("BYOK status localization")]
public sealed class TranslationMethodDeletionTests
{
    [Theory]
    [InlineData(TranslationMethod.Gemini, null)]
    [InlineData(TranslationMethod.Gemini, TranslationMethod.Google)]
    [InlineData(TranslationMethod.Gemini, TranslationMethod.Local)]
    [InlineData(TranslationMethod.Google, null)]
    [InlineData(TranslationMethod.Google, TranslationMethod.Gemini)]
    [InlineData(TranslationMethod.Google, TranslationMethod.Local)]
    public async Task DeleteKeyCommand_SelectionDuringDeletion_PreservesLatestStoredChoice(
        TranslationMethod deleted, TranslationMethod? selectionDuringDeletion)
    {
        // Arrange: real selection persistence in an isolated directory, in-memory credentials.
        var root = Path.Combine(AppContext.BaseDirectory, "deletion-tests", Guid.NewGuid().ToString("N"));
        var gemini = new Mock<IGeminiSettingsService>();
        var google = new Mock<IGoogleTranslationSettingsService>();
        gemini.SetupGet(x => x.HasApiKey).Returns(true);
        google.SetupGet(x => x.HasApiKey).Returns(true);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task DeleteAsync()
        {
            started.SetResult();
            await release.Task;
            if (deleted == TranslationMethod.Gemini)
            {
                gemini.SetupGet(x => x.HasApiKey).Returns(false);
                gemini.Raise(x => x.Changed += null, EventArgs.Empty);
            }
            else
            {
                google.SetupGet(x => x.HasApiKey).Returns(false);
                google.Raise(x => x.Changed += null, EventArgs.Empty);
            }
        }
        gemini.Setup(x => x.DeleteApiKeyAsync(It.IsAny<CancellationToken>())).Returns(DeleteAsync);
        google.Setup(x => x.DeleteApiKeyAsync(It.IsAny<CancellationToken>())).Returns(DeleteAsync);
        try
        {
            using var methods = new TranslationMethodService(gemini.Object, google.Object, root);
            await methods.SelectAsync(deleted);
            using var geminiVm = new GeminiSettingsViewModel(gemini.Object, Mock.Of<IGeminiConnectionService>(), Mock.Of<IEventAggregator>(), methods);
            using var googleVm = new GoogleTranslationSettingsViewModel(google.Object, Mock.Of<IGoogleTranslationConnectionService>(), Mock.Of<IEventAggregator>(), methods);
            var command = deleted == TranslationMethod.Gemini ? geminiVm.DeleteKeyCommand : googleVm.DeleteKeyCommand;
            var deletion = command.Execute().ToTask();
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                // Act: make a selection while deletion is suspended.
                if (selectionDuringDeletion.HasValue) await methods.SelectAsync(selectionDuringDeletion.Value);
            }
            finally
            {
                release.TrySetResult();
                await deletion;
            }

            // Assert: verify disk state too, with eligibility restored so fallback cannot mask it.
            var expected = selectionDuringDeletion ?? TranslationMethod.Local;
            Assert.Equal(expected, methods.Current);
            Assert.False(geminiVm.IsStatusError);
            Assert.False(googleVm.IsStatusError);
            gemini.SetupGet(x => x.HasApiKey).Returns(true);
            google.SetupGet(x => x.HasApiKey).Returns(true);
            using var reloaded = new TranslationMethodService(gemini.Object, google.Object, root);
            Assert.Equal(expected, reloaded.Current);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
