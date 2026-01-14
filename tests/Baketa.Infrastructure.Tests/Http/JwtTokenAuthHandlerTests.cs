using System.Net;
using System.Net.Http.Headers;
using Baketa.Core.Abstractions.Auth;
using Baketa.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Baketa.Infrastructure.Tests.Http;

/// <summary>
/// [Issue #287] JwtTokenAuthHandlerの単体テスト
/// JWT自動付与・401リトライ・バックグラウンドリフレッシュのテスト
/// </summary>
public sealed class JwtTokenAuthHandlerTests : IDisposable
{
    private readonly Mock<IJwtTokenService> _tokenServiceMock;
    private readonly Mock<ILogger<JwtTokenAuthHandler>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _innerHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly JwtTokenAuthHandler _sut;

    public JwtTokenAuthHandlerTests()
    {
        _tokenServiceMock = new Mock<IJwtTokenService>();
        _loggerMock = new Mock<ILogger<JwtTokenAuthHandler>>();
        _innerHandlerMock = new Mock<HttpMessageHandler>();

        _sut = new JwtTokenAuthHandler(_tokenServiceMock.Object, _loggerMock.Object)
        {
            InnerHandler = _innerHandlerMock.Object
        };

        _httpClient = new HttpClient(_sut)
        {
            BaseAddress = new Uri("https://test.example.com")
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullTokenService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new JwtTokenAuthHandler(
            null!,
            _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new JwtTokenAuthHandler(
            _tokenServiceMock.Object,
            null!));
    }

    #endregion

    #region SendAsync - JWT Attachment Tests

    [Fact]
    public async Task SendAsync_WithValidJwt_AddsAuthorizationHeader()
    {
        // Arrange
        _tokenServiceMock
            .Setup(x => x.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("valid-jwt-token");
        _tokenServiceMock
            .Setup(x => x.IsNearExpiry())
            .Returns(false);

        HttpRequestMessage? capturedRequest = null;
        _innerHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        await _httpClient.GetAsync("/api/test");

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
        Assert.Equal("valid-jwt-token", capturedRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SendAsync_WithNoJwt_DoesNotAddAuthorizationHeader()
    {
        // Arrange
        _tokenServiceMock
            .Setup(x => x.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        HttpRequestMessage? capturedRequest = null;
        _innerHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        await _httpClient.GetAsync("/api/test");

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Null(capturedRequest.Headers.Authorization);
    }

    #endregion

    #region SendAsync - 401 Retry Tests

    [Fact]
    public async Task SendAsync_With401Response_AttemptsRefreshAndRetry()
    {
        // Arrange
        var callCount = 0;
        var isFirstCall = true;
        _tokenServiceMock
            .Setup(x => x.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("initial-jwt");
        _tokenServiceMock
            .Setup(x => x.IsNearExpiry())
            .Returns(() =>
            {
                // 401リトライ時はIsNearExpiryをtrueにしてRefreshを強制
                if (isFirstCall)
                {
                    isFirstCall = false;
                    return false;
                }
                return true;
            });
        _tokenServiceMock
            .Setup(x => x.HasValidToken)
            .Returns(true);
        _tokenServiceMock
            .Setup(x => x.RefreshAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JwtTokenPair("new-jwt", "new-refresh", DateTime.UtcNow.AddMinutes(15)));

        _innerHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    : new HttpResponseMessage(HttpStatusCode.OK);
            });

        // Act
        var response = await _httpClient.GetAsync("/api/test");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount); // 初回401 + リトライ
        _tokenServiceMock.Verify(x => x.RefreshAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SendAsync_With401AndNoValidToken_DoesNotRetry()
    {
        // Arrange
        _tokenServiceMock
            .Setup(x => x.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("jwt");
        _tokenServiceMock
            .Setup(x => x.IsNearExpiry())
            .Returns(false);
        _tokenServiceMock
            .Setup(x => x.HasValidToken)
            .Returns(false); // トークンが無効

        var callCount = 0;
        _innerHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            });

        // Act
        var response = await _httpClient.GetAsync("/api/test");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, callCount); // リトライなし
        _tokenServiceMock.Verify(x => x.RefreshAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_With401AndRefreshFails_Returns401()
    {
        // Arrange
        _tokenServiceMock
            .Setup(x => x.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("jwt");
        _tokenServiceMock
            .Setup(x => x.IsNearExpiry())
            .Returns(false);
        _tokenServiceMock
            .Setup(x => x.HasValidToken)
            .Returns(true);
        _tokenServiceMock
            .Setup(x => x.RefreshAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((JwtTokenPair?)null); // リフレッシュ失敗

        _innerHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        // Act
        var response = await _httpClient.GetAsync("/api/test");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region SendAsync - Background Refresh Tests

    [Fact]
    public async Task SendAsync_WhenTokenNearExpiry_TriggersBackgroundRefresh()
    {
        // Arrange
        _tokenServiceMock
            .Setup(x => x.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("jwt");
        _tokenServiceMock
            .Setup(x => x.IsNearExpiry())
            .Returns(true); // トークンが期限間近
        _tokenServiceMock
            .Setup(x => x.RefreshAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JwtTokenPair("new-jwt", "new-refresh", DateTime.UtcNow.AddMinutes(15)));

        _innerHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        await _httpClient.GetAsync("/api/test");

        // バックグラウンドタスクが完了するのを少し待つ
        await Task.Delay(100);

        // Assert - バックグラウンドでリフレッシュが呼ばれたことを確認
        _tokenServiceMock.Verify(x => x.RefreshAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    #endregion

    #region Request Cloning Tests

    [Fact]
    public async Task SendAsync_With401_ClonesRequestCorrectly()
    {
        // Arrange
        var isFirstNearExpiryCall = true;
        _tokenServiceMock
            .Setup(x => x.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("jwt");
        _tokenServiceMock
            .Setup(x => x.IsNearExpiry())
            .Returns(() =>
            {
                // 401リトライ時はtrueを返してリフレッシュを強制
                if (isFirstNearExpiryCall)
                {
                    isFirstNearExpiryCall = false;
                    return false;
                }
                return true;
            });
        _tokenServiceMock
            .Setup(x => x.HasValidToken)
            .Returns(true);
        _tokenServiceMock
            .Setup(x => x.RefreshAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JwtTokenPair("new-jwt", "new-refresh", DateTime.UtcNow.AddMinutes(15)));

        var capturedRequests = new List<HttpRequestMessage>();
        var callCount = 0;
        _innerHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                // リクエストを複製して保存（元のリクエストは消費される可能性）
                var clone = new HttpRequestMessage(req.Method, req.RequestUri);
                foreach (var header in req.Headers)
                {
                    clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                capturedRequests.Add(clone);
            })
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    : new HttpResponseMessage(HttpStatusCode.OK);
            });

        // Act
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/test");
        request.Headers.Add("X-Custom-Header", "custom-value");
        request.Content = new StringContent("{\"data\":\"test\"}");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        await _httpClient.SendAsync(request);

        // Assert
        Assert.Equal(2, capturedRequests.Count);

        // リトライリクエストがカスタムヘッダーを保持していることを確認
        var retryRequest = capturedRequests[1];
        Assert.True(retryRequest.Headers.Contains("X-Custom-Header"));

        // リトライリクエストにJWTが設定されていることを確認（リフレッシュ後の新トークン）
        Assert.NotNull(retryRequest.Headers.Authorization);
        Assert.Equal("Bearer", retryRequest.Headers.Authorization?.Scheme);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_DisposesResources()
    {
        // Arrange
        var handler = new JwtTokenAuthHandler(_tokenServiceMock.Object, _loggerMock.Object);

        // Act & Assert (should not throw)
        handler.Dispose();
    }

    #endregion

    public void Dispose()
    {
        _httpClient.Dispose();
        _sut.Dispose();
    }
}
