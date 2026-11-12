using Baketa.Core.Abstractions.Services;

namespace Baketa.Infrastructure.Analytics;

/// <summary>Disables collection and upload for the local-only edition.</summary>
public sealed class NullUsageAnalyticsService : IUsageAnalyticsService
{
    public Guid SessionId => Guid.Empty;
    public bool IsEnabled => false;

    public void TrackEvent(string eventType, Dictionary<string, object>? eventData = null)
    {
        // Do not retain usage data or schedule background work.
    }

    public Task<bool> FlushAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
}
