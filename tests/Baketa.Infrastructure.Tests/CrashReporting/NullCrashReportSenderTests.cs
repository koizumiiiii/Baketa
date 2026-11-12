using Baketa.Core.Abstractions.CrashReporting;
using Baketa.Infrastructure.CrashReporting;
using Xunit;

namespace Baketa.Infrastructure.Tests.CrashReporting;

public class NullCrashReportSenderTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task SendAsync_WithReport_DoesNotMarkItAsSent(bool includeSystemInfo, bool includeLogs)
    {
        // Arrange
        var sender = new NullCrashReportSender();
        var report = new CrashReport
        {
            ReportId = Guid.NewGuid().ToString("N"),
            BaketaVersion = "test",
            Source = "test",
            Exception = new ExceptionInfo { Type = "test", Message = "test" },
            SystemInfo = new SystemInfoSnapshot
            {
                OsVersion = "test", OsPlatform = "test", MachineNameHash = "test",
                ClrVersion = "test", Culture = "test", TimeZone = "test"
            }
        };

        // Act
        var result = await sender.SendAsync(report, includeSystemInfo, includeLogs);

        // Assert
        Assert.False(result);
        Assert.False(report.IsSent);
        Assert.Null(report.SentAt);
    }
}
