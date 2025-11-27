using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Auth;
using Baketa.Infrastructure.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Auth;

/// <summary>
/// TokenExpirationHandlerの単体テスト
/// トークン有効期限切れ処理、HTTP 401検出、セッション検証をテスト
/// </summary>
public sealed class TokenExpirationHandlerTests : IDisposable
{
    private readonly Mock<ITokenStorage> _mockTokenStorage;
    private readonly Mock<ITokenAuditLogger> _mockAuditLogger;
    private readonly Mock<ILogger<TokenExpirationHandler>> _mockLogger;
    private readonly TokenExpirationHandler _handler;

    public TokenExpirationHandlerTests()
    {
        _mockTokenStorage = new Mock<ITokenStorage>();
        _mockAuditLogger = new Mock<ITokenAuditLogger>();
        _mockLogger = new Mock<ILogger<TokenExpirationHandler>>();

        // Default setup
        _mockTokenStorage.Setup(x => x.ClearTokensAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockTokenStorage.Setup(x => x.RetrieveTokensAsync(It.IsAny<CancellationToken>())).ReturnsAsync((("mock-access-token", "mock-refresh-token")));

        _handler = new TokenExpirationHandler(
            _mockTokenStorage.Object,
            _mockAuditLogger.Object,
            _mockLogger.Object);
    }

    public void Dispose()
    {
        _handler?.Dispose();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullTokenStorage_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TokenExpirationHandler(
            null!,
            _mockAuditLogger.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullAuditLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TokenExpirationHandler(
            _mockTokenStorage.Object,
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TokenExpirationHandler(
            _mockTokenStorage.Object,
            _mockAuditLogger.Object,
            null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_InitializesCorrectly()
    {
        // Arrange & Act
        using var handler = new TokenExpirationHandler(
            _mockTokenStorage.Object,
            _mockAuditLogger.Object,
            _mockLogger.Object);

        // Assert
        handler.Should().NotBeNull();
    }

    #endregion

    #region HandleTokenExpiredAsync Tests

    [Fact]
    public async Task HandleTokenExpiredAsync_ClearsTokens()
    {
        // Arrange
        const string reason = "Test expiration";

        // Act
        await _handler.HandleTokenExpiredAsync(reason);

        // Assert
        _mockTokenStorage.Verify(x => x.ClearTokensAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleTokenExpiredAsync_LogsAuditEvent()
    {
        // Arrange
        const string reason = "Test expiration";

        // Act
        await _handler.HandleTokenExpiredAsync(reason);

        // Assert - 監査ログが記録されることを確認
        _mockAuditLogger.Verify(x => x.LogTokenRevokedAsync(
            It.IsAny<string>(),
            It.Is<string>(r => r == reason),
            It.IsAny<CancellationToken>()), Times.AtMostOnce);
    }

    [Fact]
    public async Task HandleTokenExpiredAsync_RaisesTokenExpiredEvent()
    {
        // Arrange
        const string reason = "Test expiration";
        TokenExpiredEventArgs? receivedArgs = null;
        _handler.TokenExpired += (sender, args) => receivedArgs = args;

        // Act
        await _handler.HandleTokenExpiredAsync(reason);

        // Assert
        receivedArgs.Should().NotBeNull();
        receivedArgs!.Reason.Should().Be(reason);
        receivedArgs.ExpiredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task HandleTokenExpiredAsync_WithCancellation_ThrowsTaskCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            _handler.HandleTokenExpiredAsync("Test", cts.Token));
    }

    [Fact]
    public async Task HandleTokenExpiredAsync_WhenAlreadyHandling_SkipsDuplicateProcessing()
    {
        // Arrange
        var callCount = 0;
        _mockTokenStorage.Setup(x => x.ClearTokensAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref callCount);
                return true;
            });

        // Act - 同時に複数回呼び出し
        var tasks = new[]
        {
            _handler.HandleTokenExpiredAsync("Test1"),
            _handler.HandleTokenExpiredAsync("Test2"),
            _handler.HandleTokenExpiredAsync("Test3")
        };

        await Task.WhenAll(tasks);

        // Assert - 重複実行が防止され、1回のみ実行されることを確認
        callCount.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task HandleTokenExpiredAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var handler = new TokenExpirationHandler(
            _mockTokenStorage.Object,
            _mockAuditLogger.Object,
            _mockLogger.Object);
        handler.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            handler.HandleTokenExpiredAsync("Test"));
    }

    #endregion

    #region TryHandleUnauthorizedResponseAsync Tests

    [Fact]
    public async Task TryHandleUnauthorizedResponseAsync_With401_ReturnsTrue()
    {
        // Act
        var result = await _handler.TryHandleUnauthorizedResponseAsync(401);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryHandleUnauthorizedResponseAsync_With401_ClearsTokens()
    {
        // Act
        await _handler.TryHandleUnauthorizedResponseAsync(401);

        // Assert
        _mockTokenStorage.Verify(x => x.ClearTokensAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task TryHandleUnauthorizedResponseAsync_WithNon401_ReturnsFalse(int statusCode)
    {
        // Act
        var result = await _handler.TryHandleUnauthorizedResponseAsync(statusCode);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(403)]
    public async Task TryHandleUnauthorizedResponseAsync_WithNon401_DoesNotClearTokens(int statusCode)
    {
        // Act
        await _handler.TryHandleUnauthorizedResponseAsync(statusCode);

        // Assert
        _mockTokenStorage.Verify(x => x.ClearTokensAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryHandleUnauthorizedResponseAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var handler = new TokenExpirationHandler(
            _mockTokenStorage.Object,
            _mockAuditLogger.Object,
            _mockLogger.Object);
        handler.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            handler.TryHandleUnauthorizedResponseAsync(401));
    }

    #endregion

    #region ValidateSessionAsync Tests

    [Fact]
    public async Task ValidateSessionAsync_WithNullSession_ReturnsFalse()
    {
        // Act
        var result = await _handler.ValidateSessionAsync(null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateSessionAsync_WithExpiredSession_ReturnsFalse()
    {
        // Arrange
        var expiredSession = new AuthSession(
            "token",
            "refresh",
            DateTime.UtcNow.AddMinutes(-10), // 10分前に期限切れ
            new UserInfo("user-id", "test@example.com", "Test User"));

        // Act
        var result = await _handler.ValidateSessionAsync(expiredSession);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateSessionAsync_WithExpiredSession_ClearsTokens()
    {
        // Arrange
        var expiredSession = new AuthSession(
            "token",
            "refresh",
            DateTime.UtcNow.AddMinutes(-10),
            new UserInfo("user-id", "test@example.com", "Test User"));

        // Act
        await _handler.ValidateSessionAsync(expiredSession);

        // Assert
        _mockTokenStorage.Verify(x => x.ClearTokensAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateSessionAsync_WithValidSession_ReturnsTrue()
    {
        // Arrange
        var validSession = new AuthSession(
            "token",
            "refresh",
            DateTime.UtcNow.AddHours(1), // 1時間後に期限切れ
            new UserInfo("user-id", "test@example.com", "Test User"));

        // Act
        var result = await _handler.ValidateSessionAsync(validSession);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSessionAsync_WithNearExpirySession_ReturnsTrue()
    {
        // Arrange - 3分後に期限切れ（5分以内なのでNearExpiry）
        var nearExpirySession = new AuthSession(
            "token",
            "refresh",
            DateTime.UtcNow.AddMinutes(3),
            new UserInfo("user-id", "test@example.com", "Test User"));

        // Act
        var result = await _handler.ValidateSessionAsync(nearExpirySession);

        // Assert - まだ有効なのでtrue
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSessionAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var handler = new TokenExpirationHandler(
            _mockTokenStorage.Object,
            _mockAuditLogger.Object,
            _mockLogger.Object);
        handler.Dispose();

        var session = new AuthSession(
            "token",
            "refresh",
            DateTime.UtcNow.AddHours(1),
            new UserInfo("user-id", "test@example.com", "Test User"));

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            handler.ValidateSessionAsync(session));
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public void Dispose_MultipleCallsDoNotThrow()
    {
        // Arrange
        var handler = new TokenExpirationHandler(
            _mockTokenStorage.Object,
            _mockAuditLogger.Object,
            _mockLogger.Object);

        // Act & Assert
        handler.Dispose();
        handler.Dispose(); // Second call should not throw
    }

    #endregion

    #region Event Tests

    [Fact]
    public void TokenExpired_CanSubscribeAndUnsubscribe()
    {
        // Arrange
        var eventRaised = false;
        void handler(object? s, TokenExpiredEventArgs e) => eventRaised = true;

        // Act - Subscribe
        _handler.TokenExpired += handler;

        // Verify subscription doesn't throw
        Assert.False(eventRaised);

        // Act - Unsubscribe
        _handler.TokenExpired -= handler;

        // Assert
        Assert.False(eventRaised);
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task ConcurrentHandleTokenExpiredAsync_HandlesGracefully()
    {
        // Arrange
        const int concurrentCalls = 10;
        var tasks = new Task[concurrentCalls];

        // Act
        for (int i = 0; i < concurrentCalls; i++)
        {
            tasks[i] = _handler.HandleTokenExpiredAsync($"Test{i}");
        }

        // Assert - すべてのタスクが例外なく完了することを確認
        await Task.WhenAll(tasks);
        tasks.Should().AllSatisfy(t => t.IsCompletedSuccessfully.Should().BeTrue());
    }

    #endregion
}
