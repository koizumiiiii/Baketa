using System.IO;
using Baketa.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Auth;

/// <summary>
/// Unit tests for FileTokenAuditLogger
/// Tests audit log writing, formatting, and error handling
/// </summary>
public sealed class FileTokenAuditLoggerTests : IDisposable
{
    private readonly Mock<ILogger<FileTokenAuditLogger>> _loggerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly string _testLogDirectory;
    private readonly string _testLogPath;

    public FileTokenAuditLoggerTests()
    {
        _loggerMock = new Mock<ILogger<FileTokenAuditLogger>>();
        _configurationMock = new Mock<IConfiguration>();

        // Create unique test directory for each test run
        _testLogDirectory = Path.Combine(Path.GetTempPath(), "BaketaTests", Guid.NewGuid().ToString());
        _testLogPath = Path.Combine(_testLogDirectory, "test_token_audit.log");

        _configurationMock
            .Setup(x => x["Logging:TokenAuditLogPath"])
            .Returns(_testLogPath);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FileTokenAuditLogger(
            null!,
            _configurationMock.Object));
    }

    [Fact]
    public async Task LogTokenIssuedAsync_WritesToFile()
    {
        // Arrange
        using var sut = new FileTokenAuditLogger(_loggerMock.Object, _configurationMock.Object);
        var userId = "user-12345678"; // >8 chars to get "user****5678" mask
        var expiresAt = DateTime.UtcNow.AddHours(1);

        // Act
        await sut.LogTokenIssuedAsync(userId, expiresAt);

        // Assert
        Assert.True(File.Exists(_testLogPath));
        var content = await File.ReadAllTextAsync(_testLogPath);
        Assert.Contains("TOKEN_ISSUED", content);
        Assert.Contains("user", content); // Masked user ID (first 4 chars preserved)
    }

    [Fact]
    public async Task LogTokenRefreshedAsync_WritesToFile()
    {
        // Arrange
        using var sut = new FileTokenAuditLogger(_loggerMock.Object, _configurationMock.Object);
        var userId = "user-123";
        var oldExpiry = DateTime.UtcNow;
        var newExpiry = DateTime.UtcNow.AddHours(1);

        // Act
        await sut.LogTokenRefreshedAsync(userId, oldExpiry, newExpiry);

        // Assert
        Assert.True(File.Exists(_testLogPath));
        var content = await File.ReadAllTextAsync(_testLogPath);
        Assert.Contains("TOKEN_REFRESHED", content);
        Assert.Contains("OldExpiry", content);
        Assert.Contains("NewExpiry", content);
    }

    [Fact]
    public async Task LogTokenRevokedAsync_WritesToFile()
    {
        // Arrange
        using var sut = new FileTokenAuditLogger(_loggerMock.Object, _configurationMock.Object);
        var userId = "user-123";
        var reason = "User logged out";

        // Act
        await sut.LogTokenRevokedAsync(userId, reason);

        // Assert
        Assert.True(File.Exists(_testLogPath));
        var content = await File.ReadAllTextAsync(_testLogPath);
        Assert.Contains("TOKEN_REVOKED", content);
        Assert.Contains("User logged out", content);
    }

    [Fact]
    public async Task LogTokenValidationFailedAsync_WritesToFile()
    {
        // Arrange
        using var sut = new FileTokenAuditLogger(_loggerMock.Object, _configurationMock.Object);
        var reason = "Token expired";

        // Act
        await sut.LogTokenValidationFailedAsync(reason);

        // Assert
        Assert.True(File.Exists(_testLogPath));
        var content = await File.ReadAllTextAsync(_testLogPath);
        Assert.Contains("TOKEN_VALIDATION_FAILED", content);
        Assert.Contains("Token expired", content);
    }

    [Fact]
    public async Task LogEntry_ContainsIso8601Timestamp()
    {
        // Arrange
        using var sut = new FileTokenAuditLogger(_loggerMock.Object, _configurationMock.Object);

        // Act
        await sut.LogTokenIssuedAsync("user-123", DateTime.UtcNow);

        // Assert
        var content = await File.ReadAllTextAsync(_testLogPath);
        // ISO 8601 format includes 'T' separator and timezone
        Assert.Matches(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", content);
    }

    [Fact]
    public async Task LogEntry_MasksUserId()
    {
        // Arrange
        using var sut = new FileTokenAuditLogger(_loggerMock.Object, _configurationMock.Object);
        var longUserId = "1234567890abcdef";

        // Act
        await sut.LogTokenIssuedAsync(longUserId, DateTime.UtcNow);

        // Assert
        var content = await File.ReadAllTextAsync(_testLogPath);
        // Should be masked (first 4 and last 4 chars visible)
        Assert.Contains("1234****cdef", content);
    }

    [Fact]
    public async Task LogEntry_SanitizesReasonWithNewlines()
    {
        // Arrange
        using var sut = new FileTokenAuditLogger(_loggerMock.Object, _configurationMock.Object);
        var maliciousReason = "Reason\nwith\r\nnewlines";

        // Act
        await sut.LogTokenRevokedAsync("user-123", maliciousReason);

        // Assert
        var content = await File.ReadAllTextAsync(_testLogPath);
        // Newlines should be replaced
        Assert.DoesNotContain("\n", content.Split("Reason")[1]);
    }

    [Fact]
    public async Task MultipleLogEntries_AppendsToFile()
    {
        // Arrange
        using var sut = new FileTokenAuditLogger(_loggerMock.Object, _configurationMock.Object);

        // Act
        await sut.LogTokenIssuedAsync("user-1", DateTime.UtcNow);
        await sut.LogTokenRefreshedAsync("user-1", DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
        await sut.LogTokenRevokedAsync("user-1", "Logout");

        // Assert
        var lines = await File.ReadAllLinesAsync(_testLogPath);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public async Task LogWithNullConfiguration_UsesDefaultPath()
    {
        // Arrange
        var configMock = new Mock<IConfiguration>();
        configMock
            .Setup(x => x["Logging:TokenAuditLogPath"])
            .Returns((string?)null);

        using var sut = new FileTokenAuditLogger(_loggerMock.Object, configMock.Object);

        // Act & Assert (should not throw)
        await sut.LogTokenIssuedAsync("user-123", DateTime.UtcNow);

        // The default path is in AppDomain.CurrentDomain.BaseDirectory/logs/
        // Just verify no exception was thrown
    }

    public void Dispose()
    {
        // Clean up test files
        try
        {
            if (Directory.Exists(_testLogDirectory))
            {
                Directory.Delete(_testLogDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
