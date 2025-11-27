using Baketa.Application.Services.Auth;
using Baketa.Core.Abstractions.Auth;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Application.Tests.Services.Auth;

/// <summary>
/// Unit tests for TokenRefreshService
/// Tests token monitoring, refresh logic, and parallel control
/// </summary>
public sealed class TokenRefreshServiceTests : IDisposable
{
    private readonly Mock<IAuthService> _authServiceMock;
    private readonly Mock<ITokenStorage> _tokenStorageMock;
    private readonly Mock<ITokenAuditLogger> _auditLoggerMock;
    private readonly Mock<ILogger<TokenRefreshService>> _loggerMock;
    private readonly TokenRefreshService _sut;

    public TokenRefreshServiceTests()
    {
        _authServiceMock = new Mock<IAuthService>();
        _tokenStorageMock = new Mock<ITokenStorage>();
        _auditLoggerMock = new Mock<ITokenAuditLogger>();
        _loggerMock = new Mock<ILogger<TokenRefreshService>>();

        _sut = new TokenRefreshService(
            _authServiceMock.Object,
            _tokenStorageMock.Object,
            _auditLoggerMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public void Constructor_WithNullAuthService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TokenRefreshService(
            null!,
            _tokenStorageMock.Object,
            _auditLoggerMock.Object,
            _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullTokenStorage_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TokenRefreshService(
            _authServiceMock.Object,
            null!,
            _auditLoggerMock.Object,
            _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullAuditLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TokenRefreshService(
            _authServiceMock.Object,
            _tokenStorageMock.Object,
            null!,
            _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TokenRefreshService(
            _authServiceMock.Object,
            _tokenStorageMock.Object,
            _auditLoggerMock.Object,
            null!));
    }

    [Fact]
    public void IsMonitoring_Initially_ReturnsFalse()
    {
        // Assert
        Assert.False(_sut.IsMonitoring);
    }

    [Fact]
    public async Task StartMonitoringAsync_WhenNotMonitoring_StartsMonitoring()
    {
        // Act
        await _sut.StartMonitoringAsync();

        // Assert
        Assert.True(_sut.IsMonitoring);
    }

    [Fact]
    public async Task StartMonitoringAsync_WhenAlreadyMonitoring_DoesNotThrow()
    {
        // Arrange
        await _sut.StartMonitoringAsync();

        // Act & Assert (should not throw)
        await _sut.StartMonitoringAsync();
        Assert.True(_sut.IsMonitoring);
    }

    [Fact]
    public async Task StopMonitoring_WhenMonitoring_StopsMonitoring()
    {
        // Arrange
        await _sut.StartMonitoringAsync();
        Assert.True(_sut.IsMonitoring);

        // Act
        _sut.StopMonitoring();

        // Assert
        Assert.False(_sut.IsMonitoring);
    }

    [Fact]
    public void StopMonitoring_WhenNotMonitoring_DoesNotThrow()
    {
        // Act & Assert (should not throw)
        _sut.StopMonitoring();
        Assert.False(_sut.IsMonitoring);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithNoSession_ReturnsNull()
    {
        // Arrange
        _authServiceMock
            .Setup(x => x.GetCurrentSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthSession?)null);

        // Act
        var result = await _sut.RefreshTokenAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithValidSession_RefreshesToken()
    {
        // Arrange
        var user = new UserInfo("user-123", "test@example.com");
        var oldSession = new AuthSession(
            "old-access-token",
            "old-refresh-token",
            DateTime.UtcNow.AddMinutes(-5), // Expired
            user);

        var newSession = new AuthSession(
            "new-access-token",
            "new-refresh-token",
            DateTime.UtcNow.AddHours(1),
            user);

        _authServiceMock
            .Setup(x => x.GetCurrentSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldSession);

        _authServiceMock
            .Setup(x => x.RestoreSessionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // After restore, return the new session
        _authServiceMock
            .SetupSequence(x => x.GetCurrentSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldSession)
            .ReturnsAsync(newSession)
            .ReturnsAsync(newSession);

        // Act
        var result = await _sut.RefreshTokenAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("new-access-token", result.AccessToken);
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        // Arrange
        _sut.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _sut.RefreshTokenAsync());
    }

    [Fact]
    public async Task Dispose_WhenCalled_DisposesResources()
    {
        // Arrange
        // Start monitoring to create timer
        await _sut.StartMonitoringAsync();

        // Act
        _sut.Dispose();

        // Assert
        Assert.False(_sut.IsMonitoring);
    }

    [Fact]
    public void Dispose_WhenCalledMultipleTimes_DoesNotThrow()
    {
        // Act & Assert (should not throw)
        _sut.Dispose();
        _sut.Dispose();
    }

    [Fact]
    public async Task RefreshFailed_WhenRefreshFails_RaisesEvent()
    {
        // Arrange
        var eventRaised = false;
        _sut.RefreshFailed += (_, _) => eventRaised = true;

        var user = new UserInfo("user-123", "test@example.com");
        var expiredSession = new AuthSession(
            "expired-token",
            "refresh-token",
            DateTime.UtcNow.AddMinutes(-5),
            user);

        _authServiceMock
            .Setup(x => x.GetCurrentSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredSession);

        _authServiceMock
            .Setup(x => x.RestoreSessionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Return null after restore (indicating failure)
        _authServiceMock
            .SetupSequence(x => x.GetCurrentSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredSession)
            .ReturnsAsync((AuthSession?)null);

        // Act
        await _sut.RefreshTokenAsync();

        // Assert
        Assert.True(eventRaised);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }
}
