using Baketa.Application.Services.Roi;
using Baketa.Core.Abstractions.Roi;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Application.Tests.Services.Roi;

/// <summary>
/// [Issue #293] TranslationGatekeeperServiceの単体テスト
/// </summary>
public class TranslationGatekeeperServiceTests
{
    private readonly Mock<ILogger<TranslationGatekeeperService>> _loggerMock;
    private readonly Mock<IRoiGatekeeper> _gatekeeperMock;

    public TranslationGatekeeperServiceTests()
    {
        _loggerMock = new Mock<ILogger<TranslationGatekeeperService>>();
        _gatekeeperMock = new Mock<IRoiGatekeeper>();
        _gatekeeperMock.Setup(g => g.IsEnabled).Returns(true);
    }

    private TranslationGatekeeperService CreateService()
    {
        return new TranslationGatekeeperService(
            _loggerMock.Object,
            _gatekeeperMock.Object);
    }

    #region IsEnabled テスト

    [Fact]
    public void IsEnabled_ShouldDelegateToGatekeeper()
    {
        // Arrange
        _gatekeeperMock.Setup(g => g.IsEnabled).Returns(true);
        var service = CreateService();

        // Act & Assert
        Assert.True(service.IsEnabled);
    }

    [Fact]
    public void IsEnabled_WhenSet_ShouldUpdateGatekeeper()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.IsEnabled = false;

        // Assert
        _gatekeeperMock.VerifySet(g => g.IsEnabled = false, Times.Once);
    }

    #endregion

    #region ShouldTranslate テスト

    [Fact]
    public void ShouldTranslate_FirstCall_ShouldPassNullAsPreviousText()
    {
        // Arrange
        var service = CreateService();
        var expectedDecision = new GatekeeperDecision
        {
            ShouldTranslate = true,
            Reason = GatekeeperReason.FirstText,
            ChangeRatio = 1.0f
        };

        _gatekeeperMock
            .Setup(g => g.ShouldTranslate(null, "Hello World", null))
            .Returns(expectedDecision);

        // Act
        var decision = service.ShouldTranslate("source1", "Hello World");

        // Assert
        Assert.True(decision.ShouldTranslate);
        Assert.Equal(GatekeeperReason.FirstText, decision.Reason);
        _gatekeeperMock.Verify(g => g.ShouldTranslate(null, "Hello World", null), Times.Once);
    }

    [Fact]
    public void ShouldTranslate_SecondCall_ShouldPassPreviousText()
    {
        // Arrange
        var service = CreateService();
        var firstDecision = new GatekeeperDecision
        {
            ShouldTranslate = true,
            Reason = GatekeeperReason.FirstText,
            ChangeRatio = 1.0f
        };
        var secondDecision = new GatekeeperDecision
        {
            ShouldTranslate = true,
            Reason = GatekeeperReason.SufficientChange,
            ChangeRatio = 0.5f
        };

        _gatekeeperMock
            .Setup(g => g.ShouldTranslate(null, "First text", null))
            .Returns(firstDecision);
        _gatekeeperMock
            .Setup(g => g.ShouldTranslate("First text", "Second text", null))
            .Returns(secondDecision);

        // Act
        service.ShouldTranslate("source1", "First text");
        var decision = service.ShouldTranslate("source1", "Second text");

        // Assert
        Assert.True(decision.ShouldTranslate);
        _gatekeeperMock.Verify(g => g.ShouldTranslate("First text", "Second text", null), Times.Once);
    }

    [Fact]
    public void ShouldTranslate_WhenDenied_ShouldNotUpdatePreviousText()
    {
        // Arrange
        var service = CreateService();
        var firstDecision = new GatekeeperDecision
        {
            ShouldTranslate = true,
            Reason = GatekeeperReason.FirstText,
            ChangeRatio = 1.0f
        };
        var deniedDecision = new GatekeeperDecision
        {
            ShouldTranslate = false,
            Reason = GatekeeperReason.InsufficientChange,
            ChangeRatio = 0.01f
        };
        var thirdDecision = new GatekeeperDecision
        {
            ShouldTranslate = true,
            Reason = GatekeeperReason.SufficientChange,
            ChangeRatio = 0.5f
        };

        _gatekeeperMock
            .Setup(g => g.ShouldTranslate(null, "Original", null))
            .Returns(firstDecision);
        _gatekeeperMock
            .Setup(g => g.ShouldTranslate("Original", "Original.", null))
            .Returns(deniedDecision);
        _gatekeeperMock
            .Setup(g => g.ShouldTranslate("Original", "Completely different", null))
            .Returns(thirdDecision);

        // Act
        service.ShouldTranslate("source1", "Original");
        service.ShouldTranslate("source1", "Original."); // Denied - previous text stays "Original"
        var decision = service.ShouldTranslate("source1", "Completely different");

        // Assert
        // Third call should use "Original" as previous text (not "Original.")
        _gatekeeperMock.Verify(g => g.ShouldTranslate("Original", "Completely different", null), Times.Once);
    }

    [Fact]
    public void ShouldTranslate_DifferentSources_ShouldTrackSeparately()
    {
        // Arrange
        var service = CreateService();
        var firstDecision = new GatekeeperDecision { ShouldTranslate = true, Reason = GatekeeperReason.FirstText };
        var secondDecision = new GatekeeperDecision { ShouldTranslate = true, Reason = GatekeeperReason.SufficientChange };

        _gatekeeperMock
            .Setup(g => g.ShouldTranslate(null, It.IsAny<string>(), null))
            .Returns(firstDecision);
        _gatekeeperMock
            .Setup(g => g.ShouldTranslate("Text A", It.IsAny<string>(), null))
            .Returns(secondDecision);
        _gatekeeperMock
            .Setup(g => g.ShouldTranslate("Text B", It.IsAny<string>(), null))
            .Returns(secondDecision);

        // Act
        service.ShouldTranslate("source1", "Text A");
        service.ShouldTranslate("source2", "Text B");
        service.ShouldTranslate("source1", "New A");
        service.ShouldTranslate("source2", "New B");

        // Assert - Each source should track its own previous text
        _gatekeeperMock.Verify(g => g.ShouldTranslate("Text A", "New A", null), Times.Once);
        _gatekeeperMock.Verify(g => g.ShouldTranslate("Text B", "New B", null), Times.Once);
    }

    [Fact]
    public void ShouldTranslate_WithRegionInfo_ShouldPassToGatekeeper()
    {
        // Arrange
        var service = CreateService();
        var regionInfo = new GatekeeperRegionInfo
        {
            IsInExclusionZone = false,
            NormalizedX = 0.5f,
            NormalizedY = 0.5f
        };
        var decision = new GatekeeperDecision { ShouldTranslate = true, Reason = GatekeeperReason.FirstText };

        _gatekeeperMock
            .Setup(g => g.ShouldTranslate(null, "Text", regionInfo))
            .Returns(decision);

        // Act
        service.ShouldTranslate("source1", "Text", regionInfo);

        // Assert
        _gatekeeperMock.Verify(g => g.ShouldTranslate(null, "Text", regionInfo), Times.Once);
    }

    #endregion

    #region ClearPreviousText テスト

    [Fact]
    public void ClearPreviousText_ShouldResetForSpecificSource()
    {
        // Arrange
        var service = CreateService();
        var firstDecision = new GatekeeperDecision { ShouldTranslate = true, Reason = GatekeeperReason.FirstText };

        _gatekeeperMock
            .Setup(g => g.ShouldTranslate(null, It.IsAny<string>(), null))
            .Returns(firstDecision);

        service.ShouldTranslate("source1", "Text A");

        // Act
        service.ClearPreviousText("source1");
        service.ShouldTranslate("source1", "Text B");

        // Assert - After clear, previous text should be null again
        _gatekeeperMock.Verify(g => g.ShouldTranslate(null, "Text B", null), Times.Once);
    }

    [Fact]
    public void ClearPreviousText_ShouldNotAffectOtherSources()
    {
        // Arrange
        var service = CreateService();
        var firstDecision = new GatekeeperDecision { ShouldTranslate = true, Reason = GatekeeperReason.FirstText };
        var secondDecision = new GatekeeperDecision { ShouldTranslate = true, Reason = GatekeeperReason.SufficientChange };

        _gatekeeperMock
            .Setup(g => g.ShouldTranslate(null, It.IsAny<string>(), null))
            .Returns(firstDecision);
        _gatekeeperMock
            .Setup(g => g.ShouldTranslate("Text B", It.IsAny<string>(), null))
            .Returns(secondDecision);

        service.ShouldTranslate("source1", "Text A");
        service.ShouldTranslate("source2", "Text B");

        // Act
        service.ClearPreviousText("source1");
        service.ShouldTranslate("source2", "New B");

        // Assert - source2 should still have its previous text
        _gatekeeperMock.Verify(g => g.ShouldTranslate("Text B", "New B", null), Times.Once);
    }

    #endregion

    #region ClearAllPreviousText テスト

    [Fact]
    public void ClearAllPreviousText_ShouldResetAllSources()
    {
        // Arrange
        var service = CreateService();
        var firstDecision = new GatekeeperDecision { ShouldTranslate = true, Reason = GatekeeperReason.FirstText };

        _gatekeeperMock
            .Setup(g => g.ShouldTranslate(null, It.IsAny<string>(), null))
            .Returns(firstDecision);

        service.ShouldTranslate("source1", "Text A");
        service.ShouldTranslate("source2", "Text B");

        // Act
        service.ClearAllPreviousText();
        service.ShouldTranslate("source1", "New A");
        service.ShouldTranslate("source2", "New B");

        // Assert - Both sources should have null previous text after clear
        _gatekeeperMock.Verify(g => g.ShouldTranslate(null, "New A", null), Times.Once);
        _gatekeeperMock.Verify(g => g.ShouldTranslate(null, "New B", null), Times.Once);
    }

    #endregion

    #region ReportTranslationResult テスト

    [Fact]
    public void ReportTranslationResult_ShouldDelegateToGatekeeper()
    {
        // Arrange
        var service = CreateService();
        var decision = new GatekeeperDecision { ShouldTranslate = true, Reason = GatekeeperReason.FirstText };

        // Act
        service.ReportTranslationResult(decision, wasSuccessful: true, tokensUsed: 100);

        // Assert
        _gatekeeperMock.Verify(g => g.ReportTranslationResult(decision, true, 100), Times.Once);
    }

    #endregion

    #region GetStatistics テスト

    [Fact]
    public void GetStatistics_ShouldDelegateToGatekeeper()
    {
        // Arrange
        var service = CreateService();
        var expectedStats = new GatekeeperStatistics
        {
            TotalDecisions = 10,
            AllowedCount = 8,
            DeniedCount = 2
        };

        _gatekeeperMock.Setup(g => g.GetStatistics()).Returns(expectedStats);

        // Act
        var stats = service.GetStatistics();

        // Assert
        Assert.Equal(10, stats.TotalDecisions);
        Assert.Equal(8, stats.AllowedCount);
        Assert.Equal(2, stats.DeniedCount);
    }

    #endregion

    #region ResetStatistics テスト

    [Fact]
    public void ResetStatistics_ShouldResetBothGatekeeperAndPreviousText()
    {
        // Arrange
        var service = CreateService();
        var firstDecision = new GatekeeperDecision { ShouldTranslate = true, Reason = GatekeeperReason.FirstText };

        _gatekeeperMock
            .Setup(g => g.ShouldTranslate(null, It.IsAny<string>(), null))
            .Returns(firstDecision);

        service.ShouldTranslate("source1", "Text");

        // Act
        service.ResetStatistics();
        service.ShouldTranslate("source1", "New Text");

        // Assert
        _gatekeeperMock.Verify(g => g.ResetStatistics(), Times.Once);
        _gatekeeperMock.Verify(g => g.ShouldTranslate(null, "New Text", null), Times.Once);
    }

    #endregion
}
