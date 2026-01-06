using System.Net.Http;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Events;
using Baketa.Core.License.Models;
using Baketa.Core.Settings;
using Baketa.Infrastructure.License.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.License;

/// <summary>
/// LicenseManagerの単体テスト
/// </summary>
public class LicenseManagerTests : IDisposable
{
    private readonly Mock<ILogger<LicenseManager>> _mockLogger;
    private readonly Mock<ILicenseApiClient> _mockApiClient;
    private readonly Mock<ILicenseCacheService> _mockCacheService;
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly LicenseSettings _settings;
    private readonly LicenseManager _licenseManager;

    public LicenseManagerTests()
    {
        _mockLogger = new Mock<ILogger<LicenseManager>>();
        _mockApiClient = new Mock<ILicenseApiClient>();
        _mockCacheService = new Mock<ILicenseCacheService>();
        _mockEventAggregator = new Mock<IEventAggregator>();

        _settings = new LicenseSettings
        {
            EnableMockMode = true, // タイマーを無効化
            MockPlanType = 0, // Issue #257: デフォルトFreeプランで初期化
            TokenWarningThresholdPercent = 75,
            TokenCriticalThresholdPercent = 90,
            PlanExpirationWarningDays = 7,
            RefreshRateLimitPerMinute = 10,
            CloudAiRateLimitPerMinute = 60
        };

        var options = Options.Create(_settings);

        _licenseManager = new LicenseManager(
            _mockLogger.Object,
            _mockApiClient.Object,
            _mockCacheService.Object,
            _mockEventAggregator.Object,
            options);
    }

    public void Dispose()
    {
        _licenseManager.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_InitializesWithFreePlan()
    {
        // Assert
        Assert.Equal(PlanType.Free, _licenseManager.CurrentState.CurrentPlan);
    }

    [Fact]
    public void Constructor_ThrowsOnNullLogger()
    {
        // Arrange
        var options = Options.Create(_settings);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LicenseManager(
            null!,
            _mockApiClient.Object,
            _mockCacheService.Object,
            _mockEventAggregator.Object,
            options));
    }

    [Fact]
    public void Constructor_ThrowsOnNullApiClient()
    {
        // Arrange
        var options = Options.Create(_settings);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LicenseManager(
            _mockLogger.Object,
            null!,
            _mockCacheService.Object,
            _mockEventAggregator.Object,
            options));
    }

    [Fact]
    public void Constructor_ThrowsOnNullCacheService()
    {
        // Arrange
        var options = Options.Create(_settings);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LicenseManager(
            _mockLogger.Object,
            _mockApiClient.Object,
            null!,
            _mockEventAggregator.Object,
            options));
    }

    [Fact]
    public void Constructor_ThrowsOnNullEventAggregator()
    {
        // Arrange
        var options = Options.Create(_settings);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LicenseManager(
            _mockLogger.Object,
            _mockApiClient.Object,
            _mockCacheService.Object,
            null!,
            options));
    }

    [Fact]
    public void Constructor_ThrowsOnNullSettings()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LicenseManager(
            _mockLogger.Object,
            _mockApiClient.Object,
            _mockCacheService.Object,
            _mockEventAggregator.Object,
            null!));
    }

    #endregion

    #region GetCurrentStateAsync Tests

    [Fact]
    public async Task GetCurrentStateAsync_Unauthenticated_ReturnsDefault()
    {
        // Act - No SetUserCredentials called
        var state = await _licenseManager.GetCurrentStateAsync();

        // Assert
        Assert.Equal(PlanType.Free, state.CurrentPlan);
    }

    [Fact]
    public async Task GetCurrentStateAsync_WithCachedState_ReturnsCached()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        var cachedState = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            UserId = "user-123",
            CloudAiTokensUsed = 1_000_000
        };

        _mockCacheService
            .Setup(x => x.GetCachedStateAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedState);

        // Act
        var state = await _licenseManager.GetCurrentStateAsync();

        // Assert
        Assert.Equal(PlanType.Pro, state.CurrentPlan);
        Assert.Equal(1_000_000, state.CloudAiTokensUsed);
    }

    #endregion

    #region RefreshStateAsync Tests

    [Fact]
    public async Task RefreshStateAsync_Unauthenticated_ReturnsDefault()
    {
        // Act - No SetUserCredentials called
        var state = await _licenseManager.RefreshStateAsync();

        // Assert
        Assert.Equal(PlanType.Free, state.CurrentPlan);
    }

    [Fact]
    public async Task RefreshStateAsync_WithValidCache_ReturnsCached()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        var cachedState = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            UserId = "user-123"
        };

        _mockCacheService
            .Setup(x => x.IsCacheValidAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockCacheService
            .Setup(x => x.GetCachedStateAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedState);

        // Act
        var state = await _licenseManager.RefreshStateAsync();

        // Assert
        Assert.Equal(PlanType.Pro, state.CurrentPlan);
    }

    #endregion

    #region ForceRefreshAsync Tests

    [Fact]
    public async Task ForceRefreshAsync_Unauthenticated_ReturnsDefault()
    {
        // Act
        var state = await _licenseManager.ForceRefreshAsync();

        // Assert
        Assert.Equal(PlanType.Free, state.CurrentPlan);
    }

    [Fact]
    public async Task ForceRefreshAsync_ClearsCacheAndFetches()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        var serverState = new LicenseState
        {
            CurrentPlan = PlanType.Premium,
            UserId = "user-123"
        };

        _mockApiClient
            .Setup(x => x.GetLicenseStateAsync("user-123", "session-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LicenseApiResponse.CreateSuccess(serverState));

        _mockCacheService
            .Setup(x => x.GetPendingConsumptionsAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingTokenConsumption>());

        // Act
        var state = await _licenseManager.ForceRefreshAsync();

        // Assert
        _mockCacheService.Verify(
            x => x.ClearCacheAsync("user-123", It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.Equal(PlanType.Premium, state.CurrentPlan);
    }

    #endregion

    #region IsFeatureAvailable Tests

    [Fact]
    public void IsFeatureAvailable_LocalTranslation_AlwaysTrue()
    {
        // Act
        var result = _licenseManager.IsFeatureAvailable(FeatureType.LocalTranslation);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsFeatureAvailable_CloudAiTranslation_FreePlan_ReturnsFalse()
    {
        // Act - Default state is Free plan
        var result = _licenseManager.IsFeatureAvailable(FeatureType.CloudAiTranslation);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region ConsumeCloudAiTokensAsync Tests

    [Fact]
    public async Task ConsumeCloudAiTokensAsync_Unauthenticated_ReturnsSessionInvalid()
    {
        // Arrange - モックモード無効の新しいLicenseManagerを作成
        // （モックモードでは自動的に認証情報が設定されるため）
        var settingsWithoutMock = new LicenseSettings
        {
            EnableMockMode = false,
            TokenWarningThresholdPercent = 75,
            TokenCriticalThresholdPercent = 90
        };
        var options = Options.Create(settingsWithoutMock);
        using var unauthenticatedManager = new LicenseManager(
            _mockLogger.Object,
            _mockApiClient.Object,
            _mockCacheService.Object,
            _mockEventAggregator.Object,
            options);

        // Act - SetUserCredentialsを呼ばない
        var result = await unauthenticatedManager.ConsumeCloudAiTokensAsync(100, "key-1");

        // Assert
        Assert.False(result.Success);
        Assert.Equal(TokenConsumptionFailureReason.SessionInvalid, result.FailureReason);
    }

    [Fact]
    public async Task ConsumeCloudAiTokensAsync_FreePlan_ReturnsPlanNotSupported()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        // Act - Default state is Free plan
        var result = await _licenseManager.ConsumeCloudAiTokensAsync(100, "key-1");

        // Assert
        Assert.False(result.Success);
        Assert.Equal(TokenConsumptionFailureReason.PlanNotSupported, result.FailureReason);
    }

    [Fact]
    public async Task ConsumeCloudAiTokensAsync_InvalidTokenCount_Throws()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _licenseManager.ConsumeCloudAiTokensAsync(0, "key-1"));
    }

    [Fact]
    public async Task ConsumeCloudAiTokensAsync_NegativeTokenCount_Throws()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _licenseManager.ConsumeCloudAiTokensAsync(-100, "key-1"));
    }

    [Fact]
    public async Task ConsumeCloudAiTokensAsync_EmptyIdempotencyKey_Throws()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _licenseManager.ConsumeCloudAiTokensAsync(100, ""));
    }

    [Fact]
    public async Task ConsumeCloudAiTokensAsync_NullIdempotencyKey_Throws()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        // Act & Assert - ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _licenseManager.ConsumeCloudAiTokensAsync(100, null!));
    }

    #endregion

    #region SetUserCredentials Tests

    [Fact]
    public void SetUserCredentials_ValidCredentials_DoesNotThrow()
    {
        // Act
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        // Assert - No exception thrown
    }

    [Fact]
    public void SetUserCredentials_EmptyUserId_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _licenseManager.SetUserCredentials("", "session-abc"));
    }

    [Fact]
    public void SetUserCredentials_NullUserId_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _licenseManager.SetUserCredentials(null!, "session-abc"));
    }

    [Fact]
    public void SetUserCredentials_EmptySessionToken_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _licenseManager.SetUserCredentials("user-123", ""));
    }

    [Fact]
    public void SetUserCredentials_NullSessionToken_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _licenseManager.SetUserCredentials("user-123", null!));
    }

    #endregion

    #region StateChanged Event Tests

    [Fact]
    public async Task StateChanged_RaisedOnServerRefresh()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        var serverState = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            UserId = "user-123"
        };

        _mockApiClient
            .Setup(x => x.GetLicenseStateAsync("user-123", "session-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LicenseApiResponse.CreateSuccess(serverState));

        _mockCacheService
            .Setup(x => x.IsCacheValidAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockCacheService
            .Setup(x => x.GetPendingConsumptionsAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingTokenConsumption>());

        LicenseStateChangedEventArgs? eventArgs = null;
        _licenseManager.StateChanged += (_, args) => eventArgs = args;

        // Act
        await _licenseManager.RefreshStateAsync();

        // Assert
        Assert.NotNull(eventArgs);
        Assert.Equal(PlanType.Free, eventArgs.OldState.CurrentPlan);
        Assert.Equal(PlanType.Pro, eventArgs.NewState.CurrentPlan);
        Assert.Equal(LicenseChangeReason.ServerRefresh, eventArgs.Reason);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Act - Should not throw
        _licenseManager.Dispose();
        _licenseManager.Dispose();

        // Assert - No exception thrown
    }

    [Fact]
    public async Task MethodsAfterDispose_ThrowObjectDisposedException()
    {
        // Arrange
        _licenseManager.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            _licenseManager.GetCurrentStateAsync());
    }

    #endregion

    #region Token Consumption Success Tests

    [Fact]
    public async Task ConsumeCloudAiTokensAsync_Success_UpdatesStateAndReturnsResult()
    {
        // Arrange - Set up Pro plan state first
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        var proState = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            UserId = "user-123",
            CloudAiTokensUsed = 1_000_000
        };

        // First, load the Pro plan state
        _mockCacheService
            .Setup(x => x.GetCachedStateAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(proState);

        await _licenseManager.GetCurrentStateAsync();

        // Set up API to return success
        _mockApiClient.Setup(x => x.IsAvailable).Returns(true);
        _mockApiClient
            .Setup(x => x.ConsumeTokensAsync(It.IsAny<TokenConsumptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenConsumptionApiResponse
            {
                Success = true,
                NewUsageTotal = 1_100_000,
                RemainingTokens = 2_900_000
            });

        // Act
        var result = await _licenseManager.ConsumeCloudAiTokensAsync(100_000, "key-1");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1_100_000, result.NewUsageTotal);
        Assert.Equal(2_900_000, result.RemainingTokens);
    }

    [Fact]
    public async Task ConsumeCloudAiTokensAsync_Success_RaisesStateChangedEvent()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        var proState = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            UserId = "user-123",
            CloudAiTokensUsed = 1_000_000
        };

        _mockCacheService
            .Setup(x => x.GetCachedStateAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(proState);

        await _licenseManager.GetCurrentStateAsync();

        _mockApiClient.Setup(x => x.IsAvailable).Returns(true);
        _mockApiClient
            .Setup(x => x.ConsumeTokensAsync(It.IsAny<TokenConsumptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenConsumptionApiResponse
            {
                Success = true,
                NewUsageTotal = 1_100_000,
                RemainingTokens = 2_900_000
            });

        // Track event
        LicenseStateChangedEventArgs? eventArgs = null;
        _licenseManager.StateChanged += (_, args) => eventArgs = args;

        // Act
        await _licenseManager.ConsumeCloudAiTokensAsync(100_000, "key-1");

        // Assert - State should be updated, event should fire
        Assert.Equal(1_100_000, _licenseManager.CurrentState.CloudAiTokensUsed);
    }

    #endregion

    #region API Error Fallback Tests

    [Fact]
    public async Task RefreshStateAsync_ApiError_FallsBackToCache()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        var cachedState = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            UserId = "user-123",
            IsCached = true
        };

        _mockCacheService
            .Setup(x => x.IsCacheValidAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockCacheService
            .Setup(x => x.GetCachedStateAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedState);

        _mockApiClient
            .Setup(x => x.GetLicenseStateAsync("user-123", "session-abc", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var state = await _licenseManager.RefreshStateAsync();

        // Assert - Should return cached state on API error
        Assert.Equal(PlanType.Pro, state.CurrentPlan);
    }

    [Fact]
    public async Task RefreshStateAsync_ApiReturnsFailure_FallsBackToCache()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        // Issue #125: Standardプラン廃止、Proでテスト
        var cachedState = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            UserId = "user-123"
        };

        _mockCacheService
            .Setup(x => x.IsCacheValidAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockCacheService
            .Setup(x => x.GetCachedStateAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedState);

        _mockApiClient
            .Setup(x => x.GetLicenseStateAsync("user-123", "session-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LicenseApiResponse.CreateFailure("SERVER_ERROR", "Internal server error"));

        // Act
        var state = await _licenseManager.RefreshStateAsync();

        // Assert
        // Issue #125: Standardプラン廃止、Proでテスト
        Assert.Equal(PlanType.Pro, state.CurrentPlan);
    }

    #endregion

    #region Offline Consumption and Sync Tests

    [Fact]
    public async Task ConsumeCloudAiTokensAsync_Offline_StoresPendingConsumption()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        var proState = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            UserId = "user-123",
            CloudAiTokensUsed = 1_000_000
        };

        _mockCacheService
            .Setup(x => x.GetCachedStateAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(proState);

        await _licenseManager.GetCurrentStateAsync();

        // API is unavailable (offline)
        _mockApiClient.Setup(x => x.IsAvailable).Returns(false);

        var updatedState = proState with { CloudAiTokensUsed = 1_100_000 };
        _mockCacheService
            .Setup(x => x.UpdateTokenUsageAsync("user-123", 100_000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedState);

        // Act
        var result = await _licenseManager.ConsumeCloudAiTokensAsync(100_000, "offline-key-1");

        // Assert
        Assert.True(result.Success);

        // Verify pending consumption was added
        _mockCacheService.Verify(
            x => x.AddPendingConsumptionAsync(
                It.Is<PendingTokenConsumption>(p =>
                    p.UserId == "user-123" &&
                    p.IdempotencyKey == "offline-key-1" &&
                    p.TokenCount == 100_000),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ForceRefreshAsync_SyncsPendingConsumptions()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        var serverState = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            UserId = "user-123"
        };

        var pendingConsumptions = new List<PendingTokenConsumption>
        {
            new()
            {
                UserId = "user-123",
                IdempotencyKey = "pending-1",
                TokenCount = 50_000,
                ConsumedAt = DateTime.UtcNow.AddMinutes(-10)
            },
            new()
            {
                UserId = "user-123",
                IdempotencyKey = "pending-2",
                TokenCount = 30_000,
                ConsumedAt = DateTime.UtcNow.AddMinutes(-5)
            }
        };

        _mockApiClient
            .Setup(x => x.GetLicenseStateAsync("user-123", "session-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LicenseApiResponse.CreateSuccess(serverState));

        _mockCacheService
            .Setup(x => x.GetPendingConsumptionsAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingConsumptions);

        _mockApiClient
            .Setup(x => x.ConsumeTokensAsync(It.IsAny<TokenConsumptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenConsumptionApiResponse { Success = true });

        // Act
        await _licenseManager.ForceRefreshAsync();

        // Assert - Verify sync was attempted for each pending consumption
        _mockApiClient.Verify(
            x => x.ConsumeTokensAsync(
                It.Is<TokenConsumptionRequest>(r => r.IdempotencyKey == "pending-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockApiClient.Verify(
            x => x.ConsumeTokensAsync(
                It.Is<TokenConsumptionRequest>(r => r.IdempotencyKey == "pending-2"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify synced consumptions were removed
        _mockCacheService.Verify(
            x => x.RemoveSyncedConsumptionsAsync(
                It.Is<IEnumerable<string>>(keys => keys.Contains("pending-1") && keys.Contains("pending-2")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Token Usage Warning Tests

    [Fact]
    public async Task ConsumeCloudAiTokensAsync_ExceedsWarningThreshold_RaisesTokenUsageWarning()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        // Issue #257: Pro plan now has 10M tokens
        // Set up Pro plan with ~74% usage (just below 75% warning threshold)
        var proState = new LicenseState
        {
            CurrentPlan = PlanType.Pro, // 10M tokens
            UserId = "user-123",
            CloudAiTokensUsed = 7_400_000 // Already at 74%
        };

        _mockCacheService
            .Setup(x => x.GetCachedStateAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(proState);

        await _licenseManager.GetCurrentStateAsync();

        _mockApiClient.Setup(x => x.IsAvailable).Returns(true);
        _mockApiClient
            .Setup(x => x.ConsumeTokensAsync(It.IsAny<TokenConsumptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenConsumptionApiResponse
            {
                Success = true,
                NewUsageTotal = 7_600_000, // 76% - exceeds 75% warning threshold
                RemainingTokens = 2_400_000
            });

        TokenUsageWarningEventArgs? warningEventArgs = null;
        _licenseManager.TokenUsageWarning += (_, args) => warningEventArgs = args;

        // Act
        await _licenseManager.ConsumeCloudAiTokensAsync(200_000, "warning-key");

        // Assert
        Assert.NotNull(warningEventArgs);
        Assert.Equal(7_600_000, warningEventArgs.CurrentUsage);
        Assert.Equal(10_000_000, warningEventArgs.MonthlyLimit);
    }

    [Fact]
    public async Task ConsumeCloudAiTokensAsync_ExceedsCriticalThreshold_RaisesCriticalWarning()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        // Issue #257: Pro plan now has 10M tokens
        // Set up Pro plan with ~89% usage (just below 90% critical threshold)
        var proState = new LicenseState
        {
            CurrentPlan = PlanType.Pro, // 10M tokens
            UserId = "user-123",
            CloudAiTokensUsed = 8_900_000 // 89%
        };

        _mockCacheService
            .Setup(x => x.GetCachedStateAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(proState);

        await _licenseManager.GetCurrentStateAsync();

        _mockApiClient.Setup(x => x.IsAvailable).Returns(true);
        _mockApiClient
            .Setup(x => x.ConsumeTokensAsync(It.IsAny<TokenConsumptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenConsumptionApiResponse
            {
                Success = true,
                NewUsageTotal = 9_100_000, // 91% - exceeds 90% critical threshold
                RemainingTokens = 900_000
            });

        TokenUsageWarningEventArgs? warningEventArgs = null;
        _licenseManager.TokenUsageWarning += (_, args) => warningEventArgs = args;

        // Act
        await _licenseManager.ConsumeCloudAiTokensAsync(200_000, "critical-key");

        // Assert
        Assert.NotNull(warningEventArgs);
        Assert.Equal(TokenWarningLevel.Critical, warningEventArgs.Level);
    }

    #endregion

    #region Plan Expiration Warning Tests

    [Fact]
    public async Task RefreshStateAsync_NearExpiry_RaisesPlanExpirationWarning()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        var nearExpiryState = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            UserId = "user-123",
            ExpirationDate = DateTime.UtcNow.AddDays(5) // Within 7 days warning period
        };

        _mockCacheService
            .Setup(x => x.IsCacheValidAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockApiClient
            .Setup(x => x.GetLicenseStateAsync("user-123", "session-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LicenseApiResponse.CreateSuccess(nearExpiryState));

        _mockCacheService
            .Setup(x => x.GetPendingConsumptionsAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingTokenConsumption>());

        PlanExpirationWarningEventArgs? expirationEventArgs = null;
        _licenseManager.PlanExpirationWarning += (_, args) => expirationEventArgs = args;

        // Act
        await _licenseManager.RefreshStateAsync();

        // Assert
        Assert.NotNull(expirationEventArgs);
        Assert.InRange(expirationEventArgs.DaysRemaining, 4, 6);
    }

    #endregion

    #region Session Invalidation Tests

    [Fact]
    public async Task RefreshStateAsync_SessionInvalid_RaisesSessionInvalidatedEvent()
    {
        // Arrange
        _licenseManager.SetUserCredentials("user-123", "session-abc");

        _mockCacheService
            .Setup(x => x.IsCacheValidAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockApiClient
            .Setup(x => x.GetLicenseStateAsync("user-123", "session-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LicenseApiResponse.CreateFailure("SESSION_INVALID", "Session expired"));

        _mockCacheService
            .Setup(x => x.GetCachedStateAsync("user-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LicenseState?)null);

        SessionInvalidatedEventArgs? sessionEventArgs = null;
        _licenseManager.SessionInvalidated += (_, args) => sessionEventArgs = args;

        // Act
        await _licenseManager.RefreshStateAsync();

        // Assert
        Assert.NotNull(sessionEventArgs);
        Assert.Contains("Session expired", sessionEventArgs.Reason);
    }

    #endregion
}
