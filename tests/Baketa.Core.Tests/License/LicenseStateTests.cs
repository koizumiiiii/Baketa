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
    [InlineData(PlanType.Standard)]
    [InlineData(PlanType.Pro)]
    [InlineData(PlanType.Premia)]
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
    [InlineData(PlanType.Standard, 0L)]
    [InlineData(PlanType.Pro, 4_000_000L)]
    [InlineData(PlanType.Premia, 8_000_000L)]
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
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 0
        };

        // Assert
        Assert.Equal(4_000_000L, state.RemainingTokens);
    }

    [Fact]
    public void RemainingTokens_SomeTokensUsed_ReturnsCorrectValue()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 1_000_000
        };

        // Assert
        Assert.Equal(3_000_000L, state.RemainingTokens);
    }

    [Fact]
    public void RemainingTokens_AllTokensUsed_ReturnsZero()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 4_000_000
        };

        // Assert
        Assert.Equal(0L, state.RemainingTokens);
    }

    [Fact]
    public void RemainingTokens_ExceededQuota_ReturnsZero()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 5_000_000
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
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 4_000_000
        };

        // Assert
        Assert.True(state.IsQuotaExceeded);
    }

    [Fact]
    public void IsQuotaExceeded_OverLimit_ReturnsTrue()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 5_000_000
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
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 2_000_000
        };

        // Assert
        Assert.Equal(50, state.TokenUsagePercentage);
    }

    [Fact]
    public void TokenUsagePercentage_AllUsed_Returns100()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 4_000_000
        };

        // Assert
        Assert.Equal(100, state.TokenUsagePercentage);
    }

    [Fact]
    public void TokenUsagePercentage_OverLimit_CapsAt100()
    {
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 5_000_000
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
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 4_000_000,
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
    public void IsAdFree_FreePlan_ReturnsFalse()
    {
        // Arrange
        var state = new LicenseState { CurrentPlan = PlanType.Free };

        // Assert
        Assert.False(state.IsAdFree);
    }

    [Theory]
    [InlineData(PlanType.Standard)]
    [InlineData(PlanType.Pro)]
    [InlineData(PlanType.Premia)]
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
        // Arrange
        var state = new LicenseState
        {
            CurrentPlan = PlanType.Pro,
            CloudAiTokensUsed = 4_000_000
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
        Assert.False(state.IsFeatureAvailable(FeatureType.AdFree));
    }

    [Fact]
    public void IsFeatureAvailable_PrioritySupport_OnlyPremiaPlan()
    {
        // Arrange
        var proState = new LicenseState { CurrentPlan = PlanType.Pro };
        var premiaState = new LicenseState { CurrentPlan = PlanType.Premia };

        // Assert
        Assert.False(proState.IsFeatureAvailable(FeatureType.PrioritySupport));
        Assert.True(premiaState.IsFeatureAvailable(FeatureType.PrioritySupport));
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
