using System;
using System.Threading.Tasks;
using Baketa.Core.Services;
using Baketa.UI.Resources;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

public sealed class FinalReleaseNoticeViewModel(ISettingsService settingsService) : ReactiveObject
{
    private readonly ISettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

    public bool ShouldShow => !_settingsService.GetSettings().FinalReleaseNoticeAcknowledged;
    public string Title => Strings.GetString("FinalRelease_Title");
    public string Message => Strings.GetString("FinalRelease_Message");
    public string OpenSettingsText => Strings.GetString("FinalRelease_OpenSettings");
    public string CloseText => Strings.GetString("FinalRelease_Close");

    internal async Task CompleteAsync(bool openSettings, Func<Task> showSettingsAsync, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(showSettingsAsync);

        try
        {
            await AcknowledgeAsync();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not save final release notice acknowledgement; OpenSettings={OpenSettings}.", openSettings);
        }

        if (!openSettings) return;

        try
        {
            // Preserve the UI context, including when saving completes asynchronously.
            await showSettingsAsync();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not open settings after the final release notice.");
        }
    }

    public async Task AcknowledgeAsync()
    {
        var settings = _settingsService.GetSettings();
        settings.FinalReleaseNoticeAcknowledged = true;
        try
        {
            await _settingsService.SetSettingsAsync(settings).ConfigureAwait(false);
        }
        catch
        {
            settings.FinalReleaseNoticeAcknowledged = false;
            throw;
        }
    }
}
