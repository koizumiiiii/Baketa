using System.IO;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Auth;

/// <summary>Deletes retired credentials locally once, retrying incomplete cleanup on the next launch.</summary>
public sealed class LocalCredentialMigrationService : IHostedService
{
    private readonly ITokenStorage _tokenStorage;
    private readonly ILogger<LocalCredentialMigrationService> _logger;
    private readonly string _licenseDirectory;
    private readonly string _markerPath;

    public LocalCredentialMigrationService(ITokenStorage tokenStorage, ILogger<LocalCredentialMigrationService> logger, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(tokenStorage);
        ArgumentNullException.ThrowIfNull(logger);
        _tokenStorage = tokenStorage;
        _logger = logger;
        var root = baseDirectory ?? BaketaSettingsPaths.BaseDirectory;
        _licenseDirectory = Path.Combine(root, "license");
        _markerPath = Path.Combine(root, "local-credentials-migrated.flag");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_markerPath))
            return;

        var completed = true;
        try
        {
            completed = await _tokenStorage.ClearTokensAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            completed = false;
            _logger.LogWarning(ex, "Could not remove retired authentication tokens; cleanup will be retried.");
        }

        // Delete only known retired files, without reading their contents.
        string[] retiredFiles = ["patreon-credentials.json", "license-cache.json", "pending-consumptions.json"];
        foreach (var fileName in retiredFiles)
        {
            try
            {
                File.Delete(Path.Combine(_licenseDirectory, fileName));
            }
            catch (DirectoryNotFoundException)
            {
                // A clean installation has no license directory to migrate.
            }
            catch (Exception ex)
            {
                completed = false;
                _logger.LogWarning(ex, "Could not remove retired credential file {FileName}; cleanup will be retried.", fileName);
            }
        }

        if (!completed)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
            await File.WriteAllTextAsync(_markerPath, "1", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record local credential migration; cleanup will be retried.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
