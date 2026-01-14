using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Baketa.Infrastructure.Tests.Auth;

/// <summary>
/// [Issue #287] JwtTokenServiceの単体テスト
/// JWT交換・リフレッシュ・レースコンディション対策のテスト
/// </summary>
public sealed class JwtTokenServiceTests : IDisposable
{
    private readonly Mock<ILogger<JwtTokenService>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly IOptions<CloudTranslationSettings> _settings;

    public JwtTokenServiceTests()
    {
        _loggerMock = new Mock<ILogger<JwtTokenService>>();
        _httpHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpHandlerMock.Object)
        {
            BaseAddress = new Uri("https://test-relay.example.com")
        };
        _settings = Options.Create(new CloudTranslationSettings
        {
            RelayServerUrl = "https://test-relay.example.com"
        });
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new JwtTokenService(
            null!,
            _loggerMock.Object,
            _settings));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new JwtTokenService(
            _httpClient,
            null!,
            _settings));
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);

        // Assert
        Assert.NotNull(sut);
        Assert.False(sut.HasValidToken);
    }

    #endregion

    #region HasValidToken Tests

    [Fact]
    public void HasValidToken_WithNoToken_ReturnsFalse()
    {
        // Arrange
        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);

        // Assert
        Assert.False(sut.HasValidToken);
    }

    [Fact]
    public async Task HasValidToken_AfterSuccessfulExchange_ReturnsTrue()
    {
        // Arrange
        SetupSuccessfulTokenResponse();
        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);

        // Act
        await sut.ExchangeSessionTokenAsync("valid-session-token");

        // Assert
        Assert.True(sut.HasValidToken);
    }

    #endregion

    #region ExchangeSessionTokenAsync Tests

    [Fact]
    public async Task ExchangeSessionTokenAsync_WithNullToken_ThrowsArgumentNullException()
    {
        // Arrange
        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => sut.ExchangeSessionTokenAsync(null!));
    }

    [Fact]
    public async Task ExchangeSessionTokenAsync_WithEmptyToken_ThrowsArgumentException()
    {
        // Arrange
        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);

        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => sut.ExchangeSessionTokenAsync(string.Empty));
    }

    [Fact]
    public async Task ExchangeSessionTokenAsync_WithValidToken_ReturnsTokenPair()
    {
        // Arrange
        SetupSuccessfulTokenResponse(expiresIn: 900); // 15 minutes
        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);

        // Act
        var result = await sut.ExchangeSessionTokenAsync("valid-session-token");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-access-token", result.AccessToken);
        Assert.Equal("test-refresh-token", result.RefreshToken);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task ExchangeSessionTokenAsync_WithServerError_ReturnsNull()
    {
        // Arrange
        SetupErrorResponse(HttpStatusCode.InternalServerError);
        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);

        // Act
        var result = await sut.ExchangeSessionTokenAsync("valid-session-token");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ExchangeSessionTokenAsync_WithUnauthorized_ReturnsNull()
    {
        // Arrange
        SetupErrorResponse(HttpStatusCode.Unauthorized);
        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);

        // Act
        var result = await sut.ExchangeSessionTokenAsync("invalid-session-token");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetAccessTokenAsync Tests

    [Fact]
    public async Task GetAccessTokenAsync_WithNoToken_ReturnsNull()
    {
        // Arrange
        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);

        // Act
        var result = await sut.GetAccessTokenAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithValidToken_ReturnsAccessToken()
    {
        // Arrange
        SetupSuccessfulTokenResponse();
        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);
        await sut.ExchangeSessionTokenAsync("valid-session-token");

        // Act
        var result = await sut.GetAccessTokenAsync();

        // Assert
        Assert.Equal("test-access-token", result);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ConcurrentCalls_OnlyOneRefresh()
    {
        // Arrange - 最初のExchangeで有効なトークンを取得
        var callCount = 0;
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                Interlocked.Increment(ref callCount);
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = JsonContent.Create(new
                {
                    accessToken = $"token-{callCount}",
                    refreshToken = "refresh-token",
                    expiresIn = 900
                });
                return response;
            });

        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);
        await sut.ExchangeSessionTokenAsync("session-token");

        // リセットしてカウンタを確認
        callCount = 0;

        // Act - 並行アクセス（有効なトークンがあるのでリフレッシュ不要）
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => sut.GetAccessTokenAsync())
            .ToList();

        await Task.WhenAll(tasks);

        // Assert - 有効なトークンがあるのでHTTPコールは0回
        Assert.Equal(0, callCount);
    }

    #endregion

    #region RefreshAsync Tests

    [Fact]
    public async Task RefreshAsync_WithNoToken_RaisesRefreshFailedEvent()
    {
        // Arrange
        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);
        JwtRefreshFailedEventArgs? eventArgs = null;
        sut.RefreshFailed += (_, args) => eventArgs = args;

        // Act
        var result = await sut.RefreshAsync();

        // Assert
        Assert.Null(result);
        Assert.NotNull(eventArgs);
        Assert.True(eventArgs.RequiresReLogin);
        Assert.Contains("No refresh token", eventArgs.Reason);
    }

    [Fact]
    public async Task RefreshAsync_WithValidRefreshToken_ReturnsNewTokenPair()
    {
        // Arrange
        var responseCount = 0;
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                responseCount++;
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = JsonContent.Create(new
                {
                    accessToken = $"access-token-{responseCount}",
                    refreshToken = $"refresh-token-{responseCount}",
                    expiresIn = 900
                });
                return response;
            });

        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);
        await sut.ExchangeSessionTokenAsync("session-token");

        // Act
        var result = await sut.RefreshAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("access-token-2", result.AccessToken);
        Assert.Equal("refresh-token-2", result.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_WithUnauthorized_ClearsTokensAndRaisesEvent()
    {
        // Arrange - 最初は成功、次は失敗
        var requestCount = 0;
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                requestCount++;
                if (requestCount == 1)
                {
                    var successResponse = new HttpResponseMessage(HttpStatusCode.OK);
                    successResponse.Content = JsonContent.Create(new
                    {
                        accessToken = "access-token",
                        refreshToken = "refresh-token",
                        expiresIn = 900
                    });
                    return successResponse;
                }

                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            });

        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);
        JwtRefreshFailedEventArgs? eventArgs = null;
        sut.RefreshFailed += (_, args) => eventArgs = args;

        await sut.ExchangeSessionTokenAsync("session-token");
        Assert.True(sut.HasValidToken);

        // Act
        var result = await sut.RefreshAsync();

        // Assert
        Assert.Null(result);
        Assert.NotNull(eventArgs);
        Assert.True(eventArgs.RequiresReLogin);
        Assert.False(sut.HasValidToken); // トークンがクリアされていること
    }

    #endregion

    #region IsNearExpiry Tests

    [Fact]
    public void IsNearExpiry_WithNoToken_ReturnsFalse()
    {
        // Arrange
        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);

        // Assert
        Assert.False(sut.IsNearExpiry());
    }

    [Fact]
    public async Task IsNearExpiry_WithLongExpiry_ReturnsFalse()
    {
        // Arrange - 15分有効期限のトークン
        SetupSuccessfulTokenResponse(expiresIn: 900);
        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);
        await sut.ExchangeSessionTokenAsync("session-token");

        // Assert - 2分以上残っているのでfalse
        Assert.False(sut.IsNearExpiry());
    }

    [Fact]
    public async Task IsNearExpiry_WithShortExpiry_ReturnsTrue()
    {
        // Arrange - 1分有効期限のトークン（閾値は2分）
        SetupSuccessfulTokenResponse(expiresIn: 60);
        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);
        await sut.ExchangeSessionTokenAsync("session-token");

        // Assert - 2分未満なのでtrue
        Assert.True(sut.IsNearExpiry());
    }

    #endregion

    #region ClearTokensAsync Tests

    [Fact]
    public async Task ClearTokensAsync_ClearsToken()
    {
        // Arrange
        SetupSuccessfulTokenResponse();
        using var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);
        await sut.ExchangeSessionTokenAsync("session-token");
        Assert.True(sut.HasValidToken);

        // Act
        await sut.ClearTokensAsync();

        // Assert
        Assert.False(sut.HasValidToken);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task Dispose_ClearsToken()
    {
        // Arrange
        SetupSuccessfulTokenResponse();
        var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);
        await sut.ExchangeSessionTokenAsync("session-token");

        // Act
        sut.Dispose();

        // Assert
        Assert.False(sut.HasValidToken);
    }

    [Fact]
    public async Task GetAccessTokenAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        SetupSuccessfulTokenResponse();
        var sut = new JwtTokenService(_httpClient, _loggerMock.Object, _settings);
        sut.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sut.GetAccessTokenAsync());
    }

    #endregion

    #region Helper Methods

    private void SetupSuccessfulTokenResponse(int expiresIn = 900)
    {
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    accessToken = "test-access-token",
                    refreshToken = "test-refresh-token",
                    expiresIn
                })
            });
    }

    private void SetupErrorResponse(HttpStatusCode statusCode, string body = "Error")
    {
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            });
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    #endregion
}
