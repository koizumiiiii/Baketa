using Baketa.Core.License.Models;
using Xunit;

namespace Baketa.Core.Tests.License;

/// <summary>
/// LicenseState recordの単体テスト
/// </summary>
public class LicenseStateTests
{
    #region Default Factory Tests

    [Fact]
    public void Default_ReturnsFreePlan()
    {
        // Act
        var state = LicenseState.Default;

        // Assert
        Assert.Equal(PlanType.Free, state.CurrentPlan);
    }

    [Fact]
    public void Default_HasNullUserId()
    {
        // Act
        var state = LicenseState.Default;

        // Assert
        Assert.Null(state.UserId);
    }

    [Fact]
    public void Default_HasZeroTokensUsed()
    {
        // Act
        var state = LicenseState.Default;

        // Assert
        Assert.Equal(0, state.CloudAiTokensUsed);
    }

    [Fact]
    public void Default_IsNotCached()
    {
        // Act
        var state = LicenseState.Default;

        // Assert
        Assert.False(state.IsCached);
    }

    #endregion

    #region IsFree/IsPaid Tests

    [Fact]
    public void IsFree_FreePlan_ReturnsTrue()
    {
        // Arrange
        var state = new LicenseState { CurrentPlan = PlanType.Free };

        // Assert
        Assert.True(state.IsFree);
        Assert.False(state.IsPaid);
    }

    [Theory]
    [InlineData(PlanType.Pro)]
    [InlineData(PlanType.Premium)]
    [InlineData(PlanType.Ultimate)]
    public void IsPaid_PaidPlans_ReturnsTrue(PlanType plan)
    {
        // Arrange
        var state = new LicenseState { CurrentPlan = plan };

        // Assert
        Assert.True(state.IsPaid);
        Assert.False(state.IsFree);
    }

    #endregion

    #region MonthlyTokenLimit Tests

    [Theory]
    [InlineData(PlanType.Free, 0L)]
    [InlineData(PlanType.Pro, 10_000_000L)]
    [InlineData(PlanType.Premium, 20_000_000L)]
    [InlineData(PlanType.Ultimate, 50_000_000L)]
    public void MonthlyTokenLimit_DelegatesToPlanTypeExtensions(
        PlanType plan, long expected)
    {
        // Arrange
        var state = new LicenseState { CurrentPlan = plan };

        // Act
        var limit = state.MonthlyTokenLimit;

        // Assert
        Assert.Equal(expected, limit);
    }

    #endregion

    #region RemainingTokens Tests

    [Fact]
    public void RemainingTokens_NoTokensUsed_ReturnsFullLimit()
    {
        // Arrange - Issue #257: Pro plan now has 10,000,000 token limit
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 0
        };

        // Assert
        Assert.Equal(10_000_000L, state.RemainingTokens);
    }

    [Fact]
    public void RemainingTokens_SomeTokensUsed_ReturnsCorrectValue()
    {
        // Arrange - Issue #257: Pro plan now has 10,000,000 token limit
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 1_000_000
        };

        // Assert
        Assert.Equal(9_000_000L, state.RemainingTokens);
    }

    [Fact]
    public void RemainingTokens_AllTokensUsed_ReturnsZero()
    {
        // Arrange - Issue #257: Pro plan now has 10,000,000 token limit
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 10_000_000
        };

        // Assert
        Assert.Equal(0L, state.RemainingTokens);
    }

    [Fact]
    public void RemainingTokens_ExceededQuota_ReturnsZero()
    {
        // Arrange - Issue #257: Pro plan now has 10,000,000 token limit
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 12_000_000
        };

        // Assert
        Assert.Equal(0L, state.RemainingTokens);
    }

    [Fact]
    public void RemainingTokens_FreePlan_ReturnsZero()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Free,
            CloudAiTokensUsed = 0
        };

        // Assert
        Assert.Equal(0L, state.RemainingTokens);
    }

    #endregion

    #region IsQuotaExceeded Tests

    [Fact]
    public void IsQuotaExceeded_UnderLimit_ReturnsFalse()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 3_999_999
        };

        // Assert
        Assert.False(state.IsQuotaExceeded);
    }

    [Fact]
    public void IsQuotaExceeded_AtLimit_ReturnsTrue()
    {
        // Arrange - Issue #257: Pro plan now has 10,000,000 token limit
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 10_000_000  // At Pro's 10M limit
        };

        // Assert
        Assert.True(state.IsQuotaExceeded);
    }

    [Fact]
    public void IsQuotaExceeded_OverLimit_ReturnsTrue()
    {
        // Arrange - Issue #257: Pro plan now has 10,000,000 token limit
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 11_000_000  // Exceeds Pro's 10M limit
        };

        // Assert
        Assert.True(state.IsQuotaExceeded);
    }

    [Fact]
    public void IsQuotaExceeded_FreePlanNoLimit_ReturnsFalse()
    {
        // Arrange - Free plan has 0 token limit
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Free,
            CloudAiTokensUsed = 100
        };

        // Assert - MonthlyTokenLimit is 0, so condition (> 0) fails
        Assert.False(state.IsQuotaExceeded);
    }

    #endregion

    #region IsSubscriptionActive Tests

    [Fact]
    public void IsSubscriptionActive_NoExpiration_ReturnsTrue()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            ExpirationDate = null
        };

        // Assert
        Assert.True(state.IsSubscriptionActive);
    }

    [Fact]
    public void IsSubscriptionActive_FutureExpiration_ReturnsTrue()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            ExpirationDate = DateTime.UtcNow.AddDays(30)
        };

        // Assert
        Assert.True(state.IsSubscriptionActive);
    }

    [Fact]
    public void IsSubscriptionActive_PastExpiration_ReturnsFalse()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            ExpirationDate = DateTime.UtcNow.AddDays(-1)
        };

        // Assert
        Assert.False(state.IsSubscriptionActive);
    }

    #endregion

    #region IsExpired Tests

    [Fact]
    public void IsExpired_NoExpiration_ReturnsFalse()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            ExpirationDate = null
        };

        // Assert
        Assert.False(state.IsExpired);
    }

    [Fact]
    public void IsExpired_FutureExpiration_ReturnsFalse()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            ExpirationDate = DateTime.UtcNow.AddDays(30)
        };

        // Assert
        Assert.False(state.IsExpired);
    }

    [Fact]
    public void IsExpired_PastExpiration_ReturnsTrue()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            ExpirationDate = DateTime.UtcNow.AddDays(-1)
        };

        // Assert
        Assert.True(state.IsExpired);
    }

    #endregion

    #region IsNearExpiry Tests

    [Fact]
    public void IsNearExpiry_NoExpiration_ReturnsFalse()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            ExpirationDate = null
        };

        // Assert
        Assert.False(state.IsNearExpiry);
    }

    [Fact]
    public void IsNearExpiry_MoreThan7Days_ReturnsFalse()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            ExpirationDate = DateTime.UtcNow.AddDays(10)
        };

        // Assert
        Assert.False(state.IsNearExpiry);
    }

    [Fact]
    public void IsNearExpiry_Within7Days_ReturnsTrue()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            ExpirationDate = DateTime.UtcNow.AddDays(5)
        };

        // Assert
        Assert.True(state.IsNearExpiry);
    }

    [Fact]
    public void IsNearExpiry_Exactly7Days_ReturnsTrue()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            ExpirationDate = DateTime.UtcNow.AddDays(7)
        };

        // Assert
        Assert.True(state.IsNearExpiry);
    }

    [Fact]
    public void IsNearExpiry_PastExpiration_ReturnsFalse()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            ExpirationDate = DateTime.UtcNow.AddDays(-1)
        };

        // Assert
        Assert.False(state.IsNearExpiry);
    }

    #endregion

    #region DaysUntilExpiration Tests

    [Fact]
    public void DaysUntilExpiration_NoExpiration_ReturnsNull()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            ExpirationDate = null
        };

        // Assert
        Assert.Null(state.DaysUntilExpiration);
    }

    [Fact]
    public void DaysUntilExpiration_FutureDate_ReturnsCorrectDays()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            ExpirationDate = DateTime.UtcNow.AddDays(15)
        };

        // Assert
        Assert.NotNull(state.DaysUntilExpiration);
        Assert.InRange(state.DaysUntilExpiration.Value, 14, 15);
    }

    [Fact]
    public void DaysUntilExpiration_PastDate_ReturnsZero()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            ExpirationDate = DateTime.UtcNow.AddDays(-5)
        };

        // Assert
        Assert.Equal(0, state.DaysUntilExpiration);
    }

    #endregion

    #region TokenUsagePercentage Tests

    [Fact]
    public void TokenUsagePercentage_NoTokensUsed_ReturnsZero()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 0
        };

        // Assert
        Assert.Equal(0, state.TokenUsagePercentage);
    }

    [Fact]
    public void TokenUsagePercentage_HalfUsed_Returns50()
    {
        // Arrange - Issue #257: Pro plan now has 10,000,000 token limit
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 5_000_000  // 50% of Pro's 10M limit
        };

        // Assert
        Assert.Equal(50, state.TokenUsagePercentage);
    }

    [Fact]
    public void TokenUsagePercentage_AllUsed_Returns100()
    {
        // Arrange - Issue #257: Pro plan now has 10,000,000 token limit
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 10_000_000  // 100% of Pro's 10M limit
        };

        // Assert
        Assert.Equal(100, state.TokenUsagePercentage);
    }

    [Fact]
    public void TokenUsagePercentage_OverLimit_CapsAt100()
    {
        // Arrange - Issue #257: Pro plan now has 10,000,000 token limit
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 12_000_000  // Exceeds Pro's 10M limit
        };

        // Assert
        Assert.Equal(100, state.TokenUsagePercentage);
    }

    [Fact]
    public void TokenUsagePercentage_FreePlan_ReturnsZero()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Free,
            CloudAiTokensUsed = 100
        };

        // Assert - MonthlyTokenLimit is 0
        Assert.Equal(0, state.TokenUsagePercentage);
    }

    #endregion

    #region HasCloudAiAccess Tests

    [Fact]
    public void HasCloudAiAccess_ProPlanActiveNotExceeded_ReturnsTrue()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 1_000_000,
            ExpirationDate = DateTime.UtcNow.AddDays(30)
        };

        // Assert
        Assert.True(state.HasCloudAiAccess);
    }

    [Fact]
    public void HasCloudAiAccess_FreePlan_ReturnsFalse()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Free,
            ExpirationDate = null
        };

        // Assert
        Assert.False(state.HasCloudAiAccess);
    }

    [Fact]
    public void HasCloudAiAccess_QuotaExceeded_ReturnsFalse()
    {
        // Arrange - Issue #257: Pro plan now has 10,000,000 token limit
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 11_000_000,  // Exceeds Pro's 10M limit
            ExpirationDate = DateTime.UtcNow.AddDays(30)
        };

        // Assert
        Assert.False(state.HasCloudAiAccess);
    }

    [Fact]
    public void HasCloudAiAccess_Expired_ReturnsFalse()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 0,
            ExpirationDate = DateTime.UtcNow.AddDays(-1)
        };

        // Assert
        Assert.False(state.HasCloudAiAccess);
    }

    #endregion

    #region IsAdFree Tests

    [Fact]
    public void IsAdFree_FreePlan_ReturnsTrue_AfterAdRemoval()
    {
        // Arrange
        // Issue #125: 広告機能廃止により、全プランでIsAdFree=true
        var state = new LicenseState { CurrentPlan = PlanType.Free };

        // Assert
        Assert.True(state.IsAdFree);
    }

    [Theory]
    [InlineData(PlanType.Pro)]
    [InlineData(PlanType.Premium)]
    [InlineData(PlanType.Ultimate)]
    public void IsAdFree_PaidPlans_ReturnsTrue(PlanType plan)
    {
        // Arrange
        var state = new LicenseState { CurrentPlan = plan };

        // Assert
        Assert.True(state.IsAdFree);
    }

    #endregion

    #region IsFeatureAvailable Tests

    [Fact]
    public void IsFeatureAvailable_LocalTranslation_AlwaysTrue()
    {
        // Arrange
        var state = new LicenseState { CurrentPlan = PlanType.Free };

        // Assert
        Assert.True(state.IsFeatureAvailable(FeatureType.LocalTranslation));
    }

    [Fact]
    public void IsFeatureAvailable_CloudAiTranslation_QuotaExceeded_ReturnsFalse()
    {
        // Arrange - Issue #257: Pro plan now has 10,000,000 token limit
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 11_000_000  // Exceeds Pro's 10M limit
        };

        // Assert
        Assert.False(state.IsFeatureAvailable(FeatureType.CloudAiTranslation));
    }

    [Fact]
    public void IsFeatureAvailable_CloudAiTranslation_NotExceeded_ReturnsTrue()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 1_000_000
        };

        // Assert
        Assert.True(state.IsFeatureAvailable(FeatureType.CloudAiTranslation));
    }

    [Fact]
    public void IsFeatureAvailable_Expired_FallsBackToFreeFeatures()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            ExpirationDate = DateTime.UtcNow.AddDays(-1)
        };

        // Assert
        Assert.True(state.IsFeatureAvailable(FeatureType.LocalTranslation));
        Assert.False(state.IsFeatureAvailable(FeatureType.CloudAiTranslation));
        // Issue #125: 広告機能廃止により、AdFreeは常にTrue
        Assert.True(state.IsFeatureAvailable(FeatureType.AdFree));
    }

    [Fact]
    public void IsFeatureAvailable_PrioritySupport_PremiumAndUltimateOnly()
    {
        // Arrange
        var proState = new LicenseState { CurrentPlan = PlanType.Pro };
        var premiumState = new LicenseState { CurrentPlan = PlanType.Premium };
        var ultimateState = new LicenseState { CurrentPlan = PlanType.Ultimate };

        // Assert
        Assert.False(proState.IsFeatureAvailable(FeatureType.PrioritySupport));
        Assert.True(premiumState.IsFeatureAvailable(FeatureType.PrioritySupport));
        Assert.True(ultimateState.IsFeatureAvailable(FeatureType.PrioritySupport));
    }

    #endregion

    #region WithTokensConsumed Tests

    [Fact]
    public void WithTokensConsumed_AddsTokens()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 1_000_000
        };

        // Act
        var newState = state.WithTokensConsumed(500_000);

        // Assert
        Assert.Equal(1_500_000, newState.CloudAiTokensUsed);
    }

    [Fact]
    public void WithTokensConsumed_OriginalUnchanged()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 1_000_000
        };

        // Act
        _ = state.WithTokensConsumed(500_000);

        // Assert - Original state should be unchanged
        Assert.Equal(1_000_000, state.CloudAiTokensUsed);
    }

    [Fact]
    public void WithTokensConsumed_PreservesOtherProperties()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 1_000_000,
            UserId = "user-123",
            SessionId = "session-abc"
        };

        // Act
        var newState = state.WithTokensConsumed(500_000);

        // Assert
        Assert.Equal(PlanType.Pro, newState.CurrentPlan);
        Assert.Equal("user-123", newState.UserId);
        Assert.Equal("session-abc", newState.SessionId);
    }

    #endregion

    #region WithServerSync Tests

    [Fact]
    public void WithServerSync_SetsCachedToFalse()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            IsCached = true
        };

        // Act
        var newState = state.WithServerSync();

        // Assert
        Assert.False(newState.IsCached);
    }

    [Fact]
    public void WithServerSync_SetsLastServerSync()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            LastServerSync = null
        };
        var beforeSync = DateTime.UtcNow;

        // Act
        var newState = state.WithServerSync();

        // Assert
        Assert.NotNull(newState.LastServerSync);
        Assert.True(newState.LastServerSync >= beforeSync);
    }

    [Fact]
    public void WithServerSync_OriginalUnchanged()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            IsCached = true
        };

        // Act
        _ = state.WithServerSync();

        // Assert
        Assert.True(state.IsCached);
    }

    #endregion

    #region FromCache Tests

    [Fact]
    public void FromCache_SetsCachedToTrue()
    {
        // Arrange
        var originalState = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            IsCached = false
        };

        // Act
        var cachedState = LicenseState.FromCache(originalState);

        // Assert
        Assert.True(cachedState.IsCached);
    }

    [Fact]
    public void FromCache_SetsCacheTimestamp()
    {
        // Arrange
        var originalState = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CacheTimestamp = null
        };
        var beforeCache = DateTime.UtcNow;

        // Act
        var cachedState = LicenseState.FromCache(originalState);

        // Assert
        Assert.NotNull(cachedState.CacheTimestamp);
        Assert.True(cachedState.CacheTimestamp >= beforeCache);
    }

    [Fact]
    public void FromCache_PreservesOtherProperties()
    {
        // Arrange
        var originalState = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            UserId = "user-123",
            CloudAiTokensUsed = 1_000_000
        };

        // Act
        var cachedState = LicenseState.FromCache(originalState);

        // Assert
        Assert.Equal(PlanType.Pro, cachedState.CurrentPlan);
        Assert.Equal("user-123", cachedState.UserId);
        Assert.Equal(1_000_000, cachedState.CloudAiTokensUsed);
    }

    #endregion

    #region TimeUntilTokenReset Tests

    [Fact]
    public void TimeUntilTokenReset_NoBillingCycleEnd_ReturnsNull()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            BillingCycleEnd = null
        };

        // Assert
        Assert.Null(state.TimeUntilTokenReset);
    }

    [Fact]
    public void TimeUntilTokenReset_FutureDate_ReturnsPositiveTimeSpan()
    {
        // Arrange
        var futureDate = DateTime.UtcNow.AddDays(15);
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            BillingCycleEnd = futureDate
        };

        // Act
        var timeUntilReset = state.TimeUntilTokenReset;

        // Assert
        Assert.NotNull(timeUntilReset);
        Assert.True(timeUntilReset.Value.TotalDays > 14);
    }

    [Fact]
    public void TimeUntilTokenReset_PastDate_ReturnsNegativeTimeSpan()
    {
        // Arrange
        var pastDate = DateTime.UtcNow.AddDays(-5);
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            BillingCycleEnd = pastDate
        };

        // Act
        var timeUntilReset = state.TimeUntilTokenReset;

        // Assert
        Assert.NotNull(timeUntilReset);
        Assert.True(timeUntilReset.Value.TotalDays < 0);
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        // Arrange
        var state1 = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            UserId = "user-123",
            CloudAiTokensUsed = 1_000_000
        };
        var state2 = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            UserId = "user-123",
            CloudAiTokensUsed = 1_000_000
        };

        // Assert
        Assert.Equal(state1, state2);
    }

    [Fact]
    public void RecordEquality_DifferentValues_AreNotEqual()
    {
        // Arrange
        var state1 = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            UserId = "user-123"
        };
        var state2 = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            UserId = "user-456"
        };

        // Assert
        Assert.NotEqual(state1, state2);
    }

    #endregion
}
