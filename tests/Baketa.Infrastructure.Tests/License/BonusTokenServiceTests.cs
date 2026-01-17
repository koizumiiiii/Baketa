using System.Net;
using System.Net.Http;
using System.Text.Json;
using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Models;
using Baketa.Infrastructure.License;
using Baketa.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Baketa.Infrastructure.Tests.License;

/// <summary>
/// BonusTokenService のユニットテスト
/// Issue #280+#281: ボーナストークン管理機能のテスト
/// </summary>
public sealed class BonusTokenServiceTests : IDisposable
{
    private readonly Mock<ILogger<BonusTokenService>> _loggerMock;
    private readonly Mock<IApiRequestDeduplicator> _deduplicatorMock;
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly BonusTokenService _service;

    public BonusTokenServiceTests()
    {
        _loggerMock = new Mock<ILogger<BonusTokenService>>();
        _deduplicatorMock = new Mock<IApiRequestDeduplicator>();
        _httpHandlerMock = new Mock<HttpMessageHandler>();

        // [Issue #299] Deduplicator mockをパススルーに設定
        _deduplicatorMock
            .Setup(d => d.ExecuteOnceAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<BonusSyncResult?>>>(),
                It.IsAny<TimeSpan?>()))
            .Returns<string, Func<Task<BonusSyncResult?>>, TimeSpan?>(
                (key, factory, duration) => factory());

        _httpClient = new HttpClient(_httpHandlerMock.Object)
        {
            BaseAddress = new Uri("https://test.example.com")
        };

        _service = new BonusTokenService(_httpClient, _deduplicatorMock.Object, _loggerMock.Object);
    }

    public void Dispose()
    {
        _service.Dispose();
        _httpClient.Dispose();
    }

    #region GetBonusTokens Tests

    [Fact]
    public void GetBonusTokens_InitialState_ReturnsEmptyList()
    {
        // Act
        var result = _service.GetBonusTokens();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region GetTotalRemainingTokens Tests

    [Fact]
    public void GetTotalRemainingTokens_InitialState_ReturnsZero()
    {
        // Act
        var result = _service.GetTotalRemainingTokens();

        // Assert
        Assert.Equal(0, result);
    }

    #endregion

    #region ConsumeTokens Tests

    [Fact]
    public void ConsumeTokens_NoTokensAvailable_ReturnsZero()
    {
        // Act
        var consumed = _service.ConsumeTokens(100);

        // Assert
        Assert.Equal(0, consumed);
    }

    [Fact]
    public void ConsumeTokens_NegativeAmount_ReturnsZero()
    {
        // Act
        var consumed = _service.ConsumeTokens(-10);

        // Assert
        Assert.Equal(0, consumed);
    }

    [Fact]
    public void ConsumeTokens_ZeroAmount_ReturnsZero()
    {
        // Act
        var consumed = _service.ConsumeTokens(0);

        // Assert
        Assert.Equal(0, consumed);
    }

    #endregion

    #region GetConsumeableAmount Tests

    [Fact]
    public void GetConsumeableAmount_NoTokensAvailable_ReturnsZero()
    {
        // Act
        var consumeable = _service.GetConsumeableAmount(100);

        // Assert
        Assert.Equal(0, consumeable);
    }

    [Fact]
    public void GetConsumeableAmount_NegativeAmount_ReturnsZero()
    {
        // Act
        var consumeable = _service.GetConsumeableAmount(-10);

        // Assert
        Assert.Equal(0, consumeable);
    }

    #endregion

    #region HasPendingSync Tests

    [Fact]
    public void HasPendingSync_InitialState_ReturnsFalse()
    {
        // Act
        var hasPending = _service.HasPendingSync;

        // Assert
        Assert.False(hasPending);
    }

    #endregion

    #region FetchFromServerAsync Tests

    [Fact]
    public async Task FetchFromServerAsync_NullAccessToken_ReturnsFailure()
    {
        // Act
        var result = await _service.FetchFromServerAsync(null!);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Authentication required", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchFromServerAsync_EmptyAccessToken_ReturnsFailure()
    {
        // Act
        var result = await _service.FetchFromServerAsync("");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Authentication required", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchFromServerAsync_UnauthorizedResponse_ReturnsFailure()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.Unauthorized, "{}");

        // Act
        var result = await _service.FetchFromServerAsync("valid-token");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Authentication failed", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchFromServerAsync_RateLimited_ReturnsFailure()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.TooManyRequests, "{}");

        // Act
        var result = await _service.FetchFromServerAsync("valid-token");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Rate limited", result.ErrorMessage);
    }

    [Fact]
    public async Task FetchFromServerAsync_Success_ReturnsBonuses()
    {
        // Arrange
        var responseJson = JsonSerializer.Serialize(new
        {
            bonuses = new[]
            {
                new
                {
                    id = Guid.NewGuid().ToString(),
                    source_type = "promotion",
                    granted_tokens = 1000L,
                    used_tokens = 100L,
                    remaining_tokens = 900L,
                    expires_at = DateTime.UtcNow.AddDays(30).ToString("O"),
                    is_expired = false
                }
            },
            total_remaining = 900L,
            active_count = 1
        });

        SetupHttpResponse(HttpStatusCode.OK, responseJson);

        // Act
        var result = await _service.FetchFromServerAsync("valid-token");

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Bonuses);
        Assert.Equal(900, result.TotalRemaining);
    }

    #endregion

    #region SyncToServerAsync Tests

    [Fact]
    public async Task SyncToServerAsync_NullAccessToken_ReturnsFailure()
    {
        // Act
        var result = await _service.SyncToServerAsync(null!);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Authentication required", result.ErrorMessage);
    }

    [Fact]
    public async Task SyncToServerAsync_NoPendingSync_ReturnsSuccess()
    {
        // Act
        var result = await _service.SyncToServerAsync("valid-token");

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Bonuses);
    }

    [Fact]
    public async Task SyncToServerAsync_UnauthorizedResponse_ReturnsFailure()
    {
        // Arrange
        // まずボーナスをフェッチして消費を発生させる
        var bonusId = Guid.NewGuid();
        var fetchResponseJson = JsonSerializer.Serialize(new
        {
            bonuses = new[]
            {
                new
                {
                    id = bonusId.ToString(),
                    source_type = "promotion",
                    granted_tokens = 1000L,
                    used_tokens = 0L,
                    remaining_tokens = 1000L,
                    expires_at = DateTime.UtcNow.AddDays(30).ToString("O"),
                    is_expired = false
                }
            },
            total_remaining = 1000L,
            active_count = 1
        });

        SetupHttpResponse(HttpStatusCode.OK, fetchResponseJson);
        await _service.FetchFromServerAsync("valid-token");

        // 消費を発生させる
        _service.ConsumeTokens(100);

        // Syncリクエストは401を返す
        SetupHttpResponse(HttpStatusCode.Unauthorized, "{}");

        // Act
        var result = await _service.SyncToServerAsync("valid-token");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Authentication failed", result.ErrorMessage);
    }

    #endregion

    #region Event Tests

    [Fact]
    public async Task FetchFromServerAsync_Success_RaisesBonusTokensChangedEvent()
    {
        // Arrange
        var eventRaised = false;
        _service.BonusTokensChanged += (_, _) => eventRaised = true;

        var responseJson = JsonSerializer.Serialize(new
        {
            bonuses = new[]
            {
                new
                {
                    id = Guid.NewGuid().ToString(),
                    source_type = "promotion",
                    granted_tokens = 1000L,
                    used_tokens = 0L,
                    remaining_tokens = 1000L,
                    expires_at = DateTime.UtcNow.AddDays(30).ToString("O"),
                    is_expired = false
                }
            },
            total_remaining = 1000L,
            active_count = 1
        });

        SetupHttpResponse(HttpStatusCode.OK, responseJson);

        // Act
        await _service.FetchFromServerAsync("valid-token");

        // Assert
        Assert.True(eventRaised);
    }

    #endregion

    #region Helper Methods

    private void SetupHttpResponse(HttpStatusCode statusCode, string content)
    {
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            });
    }

    #endregion
}
