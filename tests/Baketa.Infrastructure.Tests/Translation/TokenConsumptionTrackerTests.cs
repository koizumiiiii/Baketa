using Baketa.Core.License.Models;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation;

/// <summary>
/// TokenConsumptionTrackerの単体テスト
/// </summary>
public class TokenConsumptionTrackerTests
{
    /// <summary>
    /// Proプランの月間トークン上限
    /// </summary>
    private const long ProPlanMonthlyLimit = 4_000_000;

    private readonly Mock<IEngineAccessController> _mockAccessController;
    private readonly Mock<ITokenUsageRepository> _mockRepository;
    private readonly Mock<ILogger<TokenConsumptionTracker>> _mockLogger;
    private readonly TokenConsumptionTracker _tracker;

    public TokenConsumptionTrackerTests()
    {
        _mockAccessController = new Mock<IEngineAccessController>();
        _mockRepository = new Mock<ITokenUsageRepository>();
        _mockLogger = new Mock<ILogger<TokenConsumptionTracker>>();

        _tracker = new TokenConsumptionTracker(
            _mockAccessController.Object,
            _mockRepository.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_ThrowsOnNullAccessController()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TokenConsumptionTracker(
            null!,
            _mockRepository.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_ThrowsOnNullRepository()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TokenConsumptionTracker(
            _mockAccessController.Object,
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_ThrowsOnNullLogger()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TokenConsumptionTracker(
            _mockAccessController.Object,
            _mockRepository.Object,
            null!));
    }

    #endregion

    #region RecordUsageAsync Tests

    [Fact]
    public async Task RecordUsageAsync_ValidInput_SavesRecord()
    {
        // Arrange
        TokenUsageRecord? savedRecord = null;
        _mockRepository
            .Setup(x => x.SaveRecordAsync(It.IsAny<TokenUsageRecord>(), It.IsAny<CancellationToken>()))
            .Callback<TokenUsageRecord, CancellationToken>((r, _) => savedRecord = r)
            .Returns(Task.CompletedTask);

        // Act
        await _tracker.RecordUsageAsync(1000, "primary", TokenUsageType.Input);

        // Assert
        Assert.NotNull(savedRecord);
        Assert.Equal(1000, savedRecord.TokensUsed);
        Assert.Equal("primary", savedRecord.ProviderId);
        Assert.Equal("Input", savedRecord.UsageType);
    }

    [Fact]
    public async Task RecordUsageAsync_ZeroTokens_SkipsRecord()
    {
        // Act
        await _tracker.RecordUsageAsync(0, "primary", TokenUsageType.Input);

        // Assert
        _mockRepository.Verify(
            x => x.SaveRecordAsync(It.IsAny<TokenUsageRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecordUsageAsync_NegativeTokens_SkipsRecord()
    {
        // Act
        await _tracker.RecordUsageAsync(-100, "primary", TokenUsageType.Input);

        // Assert
        _mockRepository.Verify(
            x => x.SaveRecordAsync(It.IsAny<TokenUsageRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecordUsageAsync_EmptyProviderId_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _tracker.RecordUsageAsync(1000, "", TokenUsageType.Input));
    }

    [Fact]
    public async Task RecordUsageAsync_NullProviderId_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _tracker.RecordUsageAsync(1000, null!, TokenUsageType.Input));
    }

    #endregion

    #region GetMonthlyUsageAsync Tests

    [Fact]
    public async Task GetMonthlyUsageAsync_NoData_ReturnsZeroUsage()
    {
        // Arrange
        _mockRepository
            .Setup(x => x.GetMonthlySummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(null as MonthlyUsageSummary);

        _mockAccessController
            .Setup(x => x.GetCurrentPlanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlanType.Pro);

        _mockAccessController
            .Setup(x => x.GetMonthlyTokenLimit(PlanType.Pro))
            .Returns(ProPlanMonthlyLimit);

        // Act
        var result = await _tracker.GetMonthlyUsageAsync();

        // Assert
        Assert.Equal(0, result.TotalTokensUsed);
        Assert.Equal(ProPlanMonthlyLimit, result.MonthlyLimit);
    }

    [Fact]
    public async Task GetMonthlyUsageAsync_WithData_ReturnsCorrectUsage()
    {
        // Arrange
        var summary = new MonthlyUsageSummary
        {
            TotalTokens = 1_500_000,
            InputTokens = 1_000_000,
            OutputTokens = 500_000,
            LastUpdated = DateTime.UtcNow,
            ByProvider = new Dictionary<string, long> { ["primary"] = 1_500_000 }
        };

        _mockRepository
            .Setup(x => x.GetMonthlySummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        _mockAccessController
            .Setup(x => x.GetCurrentPlanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlanType.Pro);

        _mockAccessController
            .Setup(x => x.GetMonthlyTokenLimit(PlanType.Pro))
            .Returns(ProPlanMonthlyLimit);

        // Act
        var result = await _tracker.GetMonthlyUsageAsync();

        // Assert
        Assert.Equal(1_500_000, result.TotalTokensUsed);
        Assert.Equal(1_000_000, result.InputTokensUsed);
        Assert.Equal(500_000, result.OutputTokensUsed);
        Assert.Equal(ProPlanMonthlyLimit, result.MonthlyLimit);
    }

    #endregion

    #region GetRemainingTokensAsync Tests

    [Fact]
    public async Task GetRemainingTokensAsync_ReturnsCorrectRemaining()
    {
        // Arrange
        var summary = new MonthlyUsageSummary
        {
            TotalTokens = 1_000_000,
            InputTokens = 700_000,
            OutputTokens = 300_000,
            LastUpdated = DateTime.UtcNow,
            ByProvider = new Dictionary<string, long>()
        };

        _mockRepository
            .Setup(x => x.GetMonthlySummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        _mockAccessController
            .Setup(x => x.GetCurrentPlanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlanType.Pro);

        _mockAccessController
            .Setup(x => x.GetMonthlyTokenLimit(PlanType.Pro))
            .Returns(ProPlanMonthlyLimit);

        // Act
        var remaining = await _tracker.GetRemainingTokensAsync();

        // Assert
        Assert.Equal(3_000_000, remaining);
    }

    #endregion

    #region IsLimitExceededAsync Tests

    [Fact]
    public async Task IsLimitExceededAsync_UnderLimit_ReturnsFalse()
    {
        // Arrange
        var summary = new MonthlyUsageSummary
        {
            TotalTokens = 3_000_000,
            InputTokens = 2_000_000,
            OutputTokens = 1_000_000,
            LastUpdated = DateTime.UtcNow,
            ByProvider = new Dictionary<string, long>()
        };

        _mockRepository
            .Setup(x => x.GetMonthlySummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        _mockAccessController
            .Setup(x => x.GetCurrentPlanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlanType.Pro);

        _mockAccessController
            .Setup(x => x.GetMonthlyTokenLimit(PlanType.Pro))
            .Returns(ProPlanMonthlyLimit);

        // Act
        var exceeded = await _tracker.IsLimitExceededAsync();

        // Assert
        Assert.False(exceeded);
    }

    [Fact]
    public async Task IsLimitExceededAsync_OverLimit_ReturnsTrue()
    {
        // Arrange
        var summary = new MonthlyUsageSummary
        {
            TotalTokens = 4_500_000, // Over 4M limit
            InputTokens = 3_000_000,
            OutputTokens = 1_500_000,
            LastUpdated = DateTime.UtcNow,
            ByProvider = new Dictionary<string, long>()
        };

        _mockRepository
            .Setup(x => x.GetMonthlySummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        _mockAccessController
            .Setup(x => x.GetCurrentPlanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlanType.Pro);

        _mockAccessController
            .Setup(x => x.GetMonthlyTokenLimit(PlanType.Pro))
            .Returns(ProPlanMonthlyLimit);

        // Act
        var exceeded = await _tracker.IsLimitExceededAsync();

        // Assert
        Assert.True(exceeded);
    }

    #endregion

    #region EstimateImageTokens Tests

    [Theory]
    [InlineData(1920, 1080, "primary", 2765)]    // 1920*1080/750 ≈ 2765
    [InlineData(1280, 720, "primary", 1229)]     // 1280*720/750 ≈ 1229
    [InlineData(1920, 1080, "secondary", 4765)]  // 1920*1080/435.2 ≈ 4765
    public void EstimateImageTokens_CalculatesCorrectly(int width, int height, string provider, int expectedApprox)
    {
        // Act
        var tokens = _tracker.EstimateImageTokens(width, height, provider);

        // Assert - Allow 10% margin for rounding differences
        Assert.InRange(tokens, expectedApprox * 0.9, expectedApprox * 1.1);
    }

    [Fact]
    public void EstimateImageTokens_ZeroWidth_ReturnsZero()
    {
        // Act
        var tokens = _tracker.EstimateImageTokens(0, 1080, "primary");

        // Assert
        Assert.Equal(0, tokens);
    }

    [Fact]
    public void EstimateImageTokens_ZeroHeight_ReturnsZero()
    {
        // Act
        var tokens = _tracker.EstimateImageTokens(1920, 0, "primary");

        // Assert
        Assert.Equal(0, tokens);
    }

    [Fact]
    public void EstimateImageTokens_NegativeDimensions_ReturnsZero()
    {
        // Act
        var tokens = _tracker.EstimateImageTokens(-100, 1080, "primary");

        // Assert
        Assert.Equal(0, tokens);
    }

    [Fact]
    public void EstimateImageTokens_UnknownProvider_UsesDefaultCoefficient()
    {
        // Act
        var tokensUnknown = _tracker.EstimateImageTokens(1920, 1080, "unknown");
        var tokensDefault = _tracker.EstimateImageTokens(1920, 1080, "primary"); // default uses same as primary

        // Assert - Both should use default coefficient (same as primary)
        Assert.Equal(tokensDefault, tokensUnknown);
    }

    #endregion

    #region CheckAlertLevelAsync Tests

    [Theory]
    [InlineData(0, UsageAlertLevel.None)]
    [InlineData(79, UsageAlertLevel.None)]
    [InlineData(80, UsageAlertLevel.Warning80)]
    [InlineData(89, UsageAlertLevel.Warning80)]
    [InlineData(90, UsageAlertLevel.Warning90)]
    [InlineData(99, UsageAlertLevel.Warning90)]
    [InlineData(100, UsageAlertLevel.Exceeded)]
    [InlineData(110, UsageAlertLevel.Exceeded)]
    public async Task CheckAlertLevelAsync_ReturnsCorrectLevel(int usagePercent, UsageAlertLevel expectedLevel)
    {
        // Arrange
        var totalTokens = ProPlanMonthlyLimit * usagePercent / 100;
        var summary = new MonthlyUsageSummary
        {
            TotalTokens = totalTokens,
            InputTokens = totalTokens / 2,
            OutputTokens = totalTokens / 2,
            LastUpdated = DateTime.UtcNow,
            ByProvider = new Dictionary<string, long>()
        };

        _mockRepository
            .Setup(x => x.GetMonthlySummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        _mockAccessController
            .Setup(x => x.GetCurrentPlanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlanType.Pro);

        _mockAccessController
            .Setup(x => x.GetMonthlyTokenLimit(PlanType.Pro))
            .Returns(ProPlanMonthlyLimit);

        // Act
        var level = await _tracker.CheckAlertLevelAsync();

        // Assert
        Assert.Equal(expectedLevel, level);
    }

    #endregion

    #region ResetMonthlyUsageAsync Tests

    [Fact]
    public async Task ResetMonthlyUsageAsync_CallsRepositoryClear()
    {
        // Act
        await _tracker.ResetMonthlyUsageAsync();

        // Assert
        _mockRepository.Verify(
            x => x.ClearMonthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
