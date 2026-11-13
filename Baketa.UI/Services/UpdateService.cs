using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Logging;
using NetSparkleUpdater;

namespace Baketa.UI.Services;

/// <summary>
/// NetSparkle用ロガーアダプター
/// </summary>
internal sealed class SparkleLogger : NetSparkleUpdater.Interfaces.ILogger
{
    private readonly Microsoft.Extensions.Logging.ILogger? _logger;

    public SparkleLogger(Microsoft.Extensions.Logging.ILogger? logger) => _logger = logger;

    public void PrintMessage(string message, params object[]? arguments)
    {
        _logger?.LogInformation("[NetSparkle] " + message, arguments ?? []);
    }
}

/// <summary>Compatibility facade: the final release never contacts the appcast server.</summary>
public sealed class UpdateService : IDisposable, IAsyncDisposable
{
    public UpdateService(IPythonServerManager? pythonServerManager = null, ILogger<UpdateService>? logger = null)
    {
        logger?.LogDebug("Application updates are disabled in the final release.");
    }

    public bool IsCheckingForUpdates => false;
    public bool UpdateAvailable => false;
    public AppCastItem? LatestVersion => null;
    public void Initialize() { }
    public Task<bool> CheckForUpdatesEarlyAsync(int timeoutMs = 3000) => Task.FromResult(false);
    public Task<bool> CheckForUpdatesAsync() => Task.FromResult(false);
    public Task CheckForUpdatesInBackgroundAsync() => Task.CompletedTask;
    public void ShowUpdateDialog() { }
    public void Dispose() => GC.SuppressFinalize(this);
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
