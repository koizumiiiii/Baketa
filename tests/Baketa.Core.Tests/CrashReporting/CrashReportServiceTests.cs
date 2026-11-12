using Baketa.Core.CrashReporting;
using Xunit;

namespace Baketa.Core.Tests.CrashReporting;

public class CrashReportServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "crash-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SendCrashReportAsync_WithoutSender_KeepsPendingReportLocal()
    {
        // Arrange
        var service = new CrashReportService(reportsDirectory: _directory);
        var report = await service.GenerateCrashReportAsync(new InvalidOperationException("test"), "test");
        var savedPath = await service.SaveCrashReportAsync(report);
        await service.CreateCrashPendingFlagAsync();

        // Act
        var result = await service.SendCrashReportAsync(report, true, true);
        var pending = await service.GetPendingCrashReportsAsync();
        var saved = await service.LoadCrashReportAsync(report.ReportId);

        // Assert
        Assert.False(result);
        Assert.True(File.Exists(savedPath));
        Assert.True(service.HasPendingCrashReport());
        Assert.Single(pending);
        Assert.NotNull(saved);
        Assert.False(saved.IsSent);
        Assert.Null(saved.SentAt);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
        GC.SuppressFinalize(this);
    }
}
