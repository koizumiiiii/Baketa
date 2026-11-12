using Baketa.Core.Abstractions.CrashReporting;

namespace Baketa.Infrastructure.CrashReporting;

/// <summary>Keeps crash reports local without reporting a successful upload.</summary>
public sealed class NullCrashReportSender : ICrashReportSender
{
    public Task<bool> SendAsync(
        CrashReport report,
        bool includeSystemInfo,
        bool includeLogs,
        CancellationToken cancellationToken = default) => Task.FromResult(false);
}
