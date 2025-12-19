using Baketa.Core.License.Extensions;
using Baketa.Core.License.Models;
using Xunit;

namespace Baketa.Core.Tests.License;

/// <summary>
/// PlanTypeExtensions拡張メソッドの単体テスト
/// </summary>
public class PlanTypeExtensionsTests
{
    /// <summary>
    /// テスト用の未定義プラン値
    /// </summary>
    private static readonly PlanType UndefinedPlan = (PlanType)999;

    #region HasCloudAiAccess Tests

    [Theory]
    [InlineData(PlanType.Free, false)]
    [InlineData(PlanType.Standard, false)]
    [InlineData(PlanType.Pro, true)]
    [InlineData(PlanType.Premia, true)]
    public void HasCloudAiAccess_ReturnsExpectedValue(PlanType plan, bool expected)
    {
        // Act
        var result = plan.HasCloudAiAccess();

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region ShowsAds Tests

    [Theory]
    [InlineData(PlanType.Free, true)]
    [InlineData(PlanType.Standard, false)]
    [InlineData(PlanType.Pro, false)]
    [InlineData(PlanType.Premia, false)]
    public void ShowsAds_ReturnsExpectedValue(PlanType plan, bool expected)
    {
        // Act
        var result = plan.ShowsAds();

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region GetMonthlyTokenLimit Tests

    [Theory]
    [InlineData(PlanType.Free, 0L)]
    [InlineData(PlanType.Standard, 0L)]
    [InlineData(PlanType.Pro, 4_000_000L)]
    [InlineData(PlanType.Premia, 8_000_000L)]
    public void GetMonthlyTokenLimit_ReturnsExpectedValue(PlanType plan, long expected)
    {
        // Act
        var result = plan.GetMonthlyTokenLimit();

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region GetMonthlyPriceYen Tests

    [Theory]
    [InlineData(PlanType.Free, 0)]
    [InlineData(PlanType.Standard, 100)]
    [InlineData(PlanType.Pro, 300)]
    [InlineData(PlanType.Premia, 500)]
    public void GetMonthlyPriceYen_ReturnsExpectedValue(PlanType plan, int expected)
    {
        // Act
        var result = plan.GetMonthlyPriceYen();

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetMonthlyPriceYen_UndefinedPlan_ReturnsZero()
    {
        // Arrange
        var undefinedPlan = UndefinedPlan;

        // Act
        var result = undefinedPlan.GetMonthlyPriceYen();

        // Assert
        Assert.Equal(0, result);
    }

    #endregion

    #region GetDisplayName Tests

    [Theory]
    [InlineData(PlanType.Free, "無料プラン")]
    [InlineData(PlanType.Standard, "スタンダードプラン")]
    [InlineData(PlanType.Pro, "プロプラン")]
    [InlineData(PlanType.Premia, "プレミアプラン")]
    public void GetDisplayName_ReturnsJapaneseName(PlanType plan, string expected)
    {
        // Act
        var result = plan.GetDisplayName();

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetDisplayName_UndefinedPlan_ReturnsUnknown()
    {
        // Arrange
        var undefinedPlan = UndefinedPlan;

        // Act
        var result = undefinedPlan.GetDisplayName();

        // Assert
        Assert.Equal("不明なプラン", result);
    }

    #endregion

    #region GetEnglishDisplayName Tests

    [Theory]
    [InlineData(PlanType.Free, "Free Plan")]
    [InlineData(PlanType.Standard, "Standard Plan")]
    [InlineData(PlanType.Pro, "Pro Plan")]
    [InlineData(PlanType.Premia, "Premia Plan")]
    public void GetEnglishDisplayName_ReturnsEnglishName(PlanType plan, string expected)
    {
        // Act
        var result = plan.GetEnglishDisplayName();

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetEnglishDisplayName_UndefinedPlan_ReturnsUnknown()
    {
        // Arrange
        var undefinedPlan = UndefinedPlan;

        // Act
        var result = undefinedPlan.GetEnglishDisplayName();

        // Assert
        Assert.Equal("Unknown Plan", result);
    }

    #endregion

    #region GetDescription Tests

    [Fact]
    public void GetDescription_Free_ReturnsCorrectDescription()
    {
        // Act
        var result = PlanType.Free.GetDescription();

        // Assert
        Assert.Contains("ローカル翻訳のみ", result);
        Assert.Contains("広告表示あり", result);
    }

    [Fact]
    public void GetDescription_Standard_ReturnsCorrectDescription()
    {
        // Act
        var result = PlanType.Standard.GetDescription();

        // Assert
        Assert.Contains("ローカル翻訳", result);
        Assert.Contains("広告なし", result);
    }

    [Fact]
    public void GetDescription_Pro_ReturnsCorrectDescription()
    {
        // Act
        var result = PlanType.Pro.GetDescription();

        // Assert
        Assert.Contains("クラウドAI翻訳", result);
        Assert.Contains("400万トークン", result);
    }

    [Fact]
    public void GetDescription_Premia_ReturnsCorrectDescription()
    {
        // Act
        var result = PlanType.Premia.GetDescription();

        // Assert
        Assert.Contains("クラウドAI翻訳", result);
        Assert.Contains("800万トークン", result);
        Assert.Contains("優先サポート", result);
    }

    [Fact]
    public void GetDescription_UndefinedPlan_ReturnsUnknown()
    {
        // Arrange
        var undefinedPlan = UndefinedPlan;

        // Act
        var result = undefinedPlan.GetDescription();

        // Assert
        Assert.Equal("不明なプラン", result);
    }

    #endregion

    #region IsFeatureAvailable Tests - LocalTranslation

    [Theory]
    [InlineData(PlanType.Free)]
    [InlineData(PlanType.Standard)]
    [InlineData(PlanType.Pro)]
    [InlineData(PlanType.Premia)]
    public void IsFeatureAvailable_LocalTranslation_AlwaysTrue(PlanType plan)
    {
        // Act
        var result = plan.IsFeatureAvailable(FeatureType.LocalTranslation);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region IsFeatureAvailable Tests - CloudAiTranslation

    [Theory]
    [InlineData(PlanType.Free, false)]
    [InlineData(PlanType.Standard, false)]
    [InlineData(PlanType.Pro, true)]
    [InlineData(PlanType.Premia, true)]
    public void IsFeatureAvailable_CloudAiTranslation_OnlyProAndPremiaPlan(
        PlanType plan, bool expected)
    {
        // Act
        var result = plan.IsFeatureAvailable(FeatureType.CloudAiTranslation);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region IsFeatureAvailable Tests - AdFree

    [Theory]
    [InlineData(PlanType.Free, false)]
    [InlineData(PlanType.Standard, true)]
    [InlineData(PlanType.Pro, true)]
    [InlineData(PlanType.Premia, true)]
    public void IsFeatureAvailable_AdFree_NotFree(PlanType plan, bool expected)
    {
        // Act
        var result = plan.IsFeatureAvailable(FeatureType.AdFree);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region IsFeatureAvailable Tests - PrioritySupport

    [Theory]
    [InlineData(PlanType.Free, false)]
    [InlineData(PlanType.Standard, false)]
    [InlineData(PlanType.Pro, false)]
    [InlineData(PlanType.Premia, true)]
    public void IsFeatureAvailable_PrioritySupport_OnlyPremiaPlan(
        PlanType plan, bool expected)
    {
        // Act
        var result = plan.IsFeatureAvailable(FeatureType.PrioritySupport);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region IsFeatureAvailable Tests - AdvancedOcrSettings

    [Theory]
    [InlineData(PlanType.Free, false)]
    [InlineData(PlanType.Standard, false)]
    [InlineData(PlanType.Pro, true)]
    [InlineData(PlanType.Premia, true)]
    public void IsFeatureAvailable_AdvancedOcrSettings_ProAndPremiaOnly(
        PlanType plan, bool expected)
    {
        // Act
        var result = plan.IsFeatureAvailable(FeatureType.AdvancedOcrSettings);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region IsFeatureAvailable Tests - BatchTranslation

    [Theory]
    [InlineData(PlanType.Free, false)]
    [InlineData(PlanType.Standard, false)]
    [InlineData(PlanType.Pro, true)]
    [InlineData(PlanType.Premia, true)]
    public void IsFeatureAvailable_BatchTranslation_ProAndPremiaOnly(
        PlanType plan, bool expected)
    {
        // Act
        var result = plan.IsFeatureAvailable(FeatureType.BatchTranslation);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region IsFeatureAvailable Tests - Unknown Feature

    [Fact]
    public void IsFeatureAvailable_UndefinedFeature_ReturnsFalse()
    {
        // Arrange
        var undefinedFeature = (FeatureType)999;

        // Act
        var result = PlanType.Premia.IsFeatureAvailable(undefinedFeature);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetRank Tests

    [Theory]
    [InlineData(PlanType.Free, 0)]
    [InlineData(PlanType.Standard, 1)]
    [InlineData(PlanType.Pro, 2)]
    [InlineData(PlanType.Premia, 3)]
    public void GetRank_ReturnsEnumIntegerValue(PlanType plan, int expected)
    {
        // Act
        var result = plan.GetRank();

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region IsUpgradeTo Tests

    [Fact]
    public void IsUpgradeTo_FreeToStandard_ReturnsTrue()
    {
        // Act
        var result = PlanType.Free.IsUpgradeTo(PlanType.Standard);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsUpgradeTo_FreeToPro_ReturnsTrue()
    {
        // Act
        var result = PlanType.Free.IsUpgradeTo(PlanType.Pro);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsUpgradeTo_FreeToPremi_ReturnsTrue()
    {
        // Act
        var result = PlanType.Free.IsUpgradeTo(PlanType.Premia);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsUpgradeTo_StandardToPro_ReturnsTrue()
    {
        // Act
        var result = PlanType.Standard.IsUpgradeTo(PlanType.Pro);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsUpgradeTo_ProToPremi_ReturnsTrue()
    {
        // Act
        var result = PlanType.Pro.IsUpgradeTo(PlanType.Premia);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsUpgradeTo_SamePlan_ReturnsFalse()
    {
        // Act
        var result = PlanType.Pro.IsUpgradeTo(PlanType.Pro);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsUpgradeTo_HigherToLower_ReturnsFalse()
    {
        // Act
        var result = PlanType.Pro.IsUpgradeTo(PlanType.Free);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region IsDowngradeTo Tests

    [Fact]
    public void IsDowngradeTo_ProToFree_ReturnsTrue()
    {
        // Act
        var result = PlanType.Pro.IsDowngradeTo(PlanType.Free);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsDowngradeTo_PremiaToStandard_ReturnsTrue()
    {
        // Act
        var result = PlanType.Premia.IsDowngradeTo(PlanType.Standard);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsDowngradeTo_SamePlan_ReturnsFalse()
    {
        // Act
        var result = PlanType.Standard.IsDowngradeTo(PlanType.Standard);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsDowngradeTo_LowerToHigher_ReturnsFalse()
    {
        // Act
        var result = PlanType.Free.IsDowngradeTo(PlanType.Premia);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region IsValid Tests

    [Theory]
    [InlineData(PlanType.Free, true)]
    [InlineData(PlanType.Standard, true)]
    [InlineData(PlanType.Pro, true)]
    [InlineData(PlanType.Premia, true)]
    public void IsValid_DefinedPlanType_ReturnsTrue(PlanType plan, bool expected)
    {
        // Act
        var result = plan.IsValid();

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsValid_UndefinedPlanType_ReturnsFalse()
    {
        // Arrange
        var undefinedPlan = UndefinedPlan;

        // Act
        var result = undefinedPlan.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_NegativePlanType_ReturnsFalse()
    {
        // Arrange
        var negativePlan = (PlanType)(-1);

        // Act
        var result = negativePlan.IsValid();

        // Assert
        Assert.False(result);
    }

    #endregion
}
