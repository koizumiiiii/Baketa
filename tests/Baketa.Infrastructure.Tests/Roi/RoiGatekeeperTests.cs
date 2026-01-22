using Baketa.Core.Abstractions.Roi;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Roi.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Roi;

/// <summary>
/// [Issue #293] RoiGatekeeperの単体テスト
/// </summary>
public class RoiGatekeeperTests
{
    private readonly Mock<ILogger<RoiGatekeeper>> _loggerMock;
    private readonly Mock<IRoiManager> _roiManagerMock;
    private readonly IOptions<RoiGatekeeperSettings> _enabledSettings;
    private readonly IOptions<RoiGatekeeperSettings> _disabledSettings;

    public RoiGatekeeperTests()
    {
        _loggerMock = new Mock<ILogger<RoiGatekeeper>>();
        _roiManagerMock = new Mock<IRoiManager>();
        _enabledSettings = Options.Create(new RoiGatekeeperSettings
        {
            Enabled = true,
            ShortTextThreshold = 20,
            LongTextThreshold = 100,
            ShortTextChangeThreshold = 0.3f,
            MediumTextChangeThreshold = 0.15f,
            LongTextChangeThreshold = 0.08f,
            EnableStatistics = true
        });
        _disabledSettings = Options.Create(new RoiGatekeeperSettings
        {
            Enabled = false
        });
    }

    #region IsEnabled テスト

    [Fact]
    public void IsEnabled_WithEnabledSettings_ShouldReturnTrue()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        // Act & Assert
        Assert.True(gatekeeper.IsEnabled);
    }

    [Fact]
    public void IsEnabled_WithDisabledSettings_ShouldReturnFalse()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _disabledSettings);

        // Act & Assert
        Assert.False(gatekeeper.IsEnabled);
    }

    [Fact]
    public void IsEnabled_CanBeToggledAtRuntime()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        // Act
        gatekeeper.IsEnabled = false;

        // Assert
        Assert.False(gatekeeper.IsEnabled);
    }

    #endregion

    #region ShouldTranslate - Gatekeeper無効時 テスト

    [Fact]
    public void ShouldTranslate_WhenDisabled_ShouldAlwaysAllow()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _disabledSettings);

        // Act
        var decision = gatekeeper.ShouldTranslate("old text", "new text");

        // Assert
        Assert.True(decision.ShouldTranslate);
        Assert.Equal(GatekeeperReason.GatekeeperDisabled, decision.Reason);
    }

    #endregion

    #region ShouldTranslate - 初回テキスト テスト

    [Fact]
    public void ShouldTranslate_WithNullPreviousText_ShouldAllowFirstText()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        // Act
        var decision = gatekeeper.ShouldTranslate(null, "Hello World");

        // Assert
        Assert.True(decision.ShouldTranslate);
        Assert.Equal(GatekeeperReason.FirstText, decision.Reason);
    }

    [Fact]
    public void ShouldTranslate_WithEmptyPreviousText_ShouldAllowFirstText()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        // Act
        var decision = gatekeeper.ShouldTranslate("", "Hello World");

        // Assert
        Assert.True(decision.ShouldTranslate);
        Assert.Equal(GatekeeperReason.FirstText, decision.Reason);
    }

    #endregion

    #region ShouldTranslate - 空テキスト テスト

    [Fact]
    public void ShouldTranslate_WithEmptyCurrentText_ShouldDeny()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        // Act
        var decision = gatekeeper.ShouldTranslate("old text", "");

        // Assert
        Assert.False(decision.ShouldTranslate);
        Assert.Equal(GatekeeperReason.EmptyText, decision.Reason);
    }

    [Fact]
    public void ShouldTranslate_WithWhitespaceCurrentText_ShouldDeny()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        // Act
        var decision = gatekeeper.ShouldTranslate("old text", "   ");

        // Assert
        Assert.False(decision.ShouldTranslate);
        Assert.Equal(GatekeeperReason.EmptyText, decision.Reason);
    }

    #endregion

    #region ShouldTranslate - 同一テキスト テスト

    [Fact]
    public void ShouldTranslate_WithIdenticalText_ShouldDeny()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        // Act
        var decision = gatekeeper.ShouldTranslate("Hello World", "Hello World");

        // Assert
        Assert.False(decision.ShouldTranslate);
        Assert.Equal(GatekeeperReason.IdenticalText, decision.Reason);
    }

    #endregion

    #region ShouldTranslate - 変化率 テスト

    [Fact]
    public void ShouldTranslate_WithSufficientChange_ShouldAllow()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        // 長文（100文字以上）で8%以上の変化
        var previousText = new string('a', 100);
        var currentText = new string('a', 90) + "CHANGED..."; // 10文字変更 = 10%

        // Act
        var decision = gatekeeper.ShouldTranslate(previousText, currentText);

        // Assert
        Assert.True(decision.ShouldTranslate);
        Assert.True(decision.ChangeRatio >= 0.08f);
    }

    [Fact]
    public void ShouldTranslate_WithInsufficientChange_ShouldDeny()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        // 長文で3%程度の変化（8%閾値未満）
        var previousText = new string('a', 100);
        var currentText = new string('a', 97) + "bbb"; // 3文字変更 = 3%

        // Act
        var decision = gatekeeper.ShouldTranslate(previousText, currentText);

        // Assert
        Assert.False(decision.ShouldTranslate);
        Assert.Equal(GatekeeperReason.InsufficientChange, decision.Reason);
    }

    [Fact]
    public void ShouldTranslate_ShortText_RequiresHigherChangeRatio()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        // 短文（20文字以下）で30%閾値
        var previousText = "Hello World!"; // 12文字
        var currentText = "Hello Worlt!"; // 1文字変更 = 約8%（30%未満）

        // Act
        var decision = gatekeeper.ShouldTranslate(previousText, currentText);

        // Assert
        Assert.False(decision.ShouldTranslate);
        Assert.Equal(GatekeeperReason.InsufficientChange, decision.Reason);
    }

    [Fact]
    public void ShouldTranslate_ShortText_WithSufficientChange_ShouldAllow()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        // 短文で30%以上の変化
        var previousText = "Hello World!"; // 12文字
        var currentText = "Good Night!!"; // 8文字変更 = 約67%

        // Act
        var decision = gatekeeper.ShouldTranslate(previousText, currentText);

        // Assert
        Assert.True(decision.ShouldTranslate);
        Assert.Equal(GatekeeperReason.ShortTextChange, decision.Reason);
    }

    #endregion

    #region ShouldTranslate - 除外ゾーン テスト

    [Fact]
    public void ShouldTranslate_InExclusionZone_ShouldDeny()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        var region = new GatekeeperRegionInfo
        {
            IsInExclusionZone = true,
            NormalizedX = 0.5f,
            NormalizedY = 0.5f
        };

        // Act
        var decision = gatekeeper.ShouldTranslate("old", "completely new text", region);

        // Assert
        Assert.False(decision.ShouldTranslate);
        Assert.Equal(GatekeeperReason.InExclusionZone, decision.Reason);
    }

    #endregion

    #region ShouldTranslate - 長さ変化 テスト

    [Fact]
    public void ShouldTranslate_WithSignificantLengthChange_ShouldAllow()
    {
        // Arrange
        var settings = Options.Create(new RoiGatekeeperSettings
        {
            Enabled = true,
            EnableLengthChangeForceTranslate = true,
            LengthChangeForceThreshold = 0.5f
        });

        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            settings);

        // 50%以上の長さ変化
        var previousText = "Short text";
        var currentText = "This is a much longer piece of text that should trigger force translate";

        // Act
        var decision = gatekeeper.ShouldTranslate(previousText, currentText);

        // Assert
        Assert.True(decision.ShouldTranslate);
        Assert.Equal(GatekeeperReason.SignificantLengthChange, decision.Reason);
    }

    #endregion

    #region Statistics テスト

    [Fact]
    public void GetStatistics_InitialState_ShouldReturnZeroValues()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        // Act
        var stats = gatekeeper.GetStatistics();

        // Assert
        Assert.Equal(0, stats.TotalDecisions);
        Assert.Equal(0, stats.AllowedCount);
        Assert.Equal(0, stats.DeniedCount);
    }

    [Fact]
    public void GetStatistics_AfterDecisions_ShouldReflectCounts()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        // Act
        gatekeeper.ShouldTranslate(null, "First text"); // Allow (FirstText)
        gatekeeper.ShouldTranslate("Hello", "Hello");   // Deny (IdenticalText)
        gatekeeper.ShouldTranslate("", "New text");     // Allow (FirstText)

        var stats = gatekeeper.GetStatistics();

        // Assert
        Assert.Equal(3, stats.TotalDecisions);
        Assert.Equal(2, stats.AllowedCount);
        Assert.Equal(1, stats.DeniedCount);
    }

    [Fact]
    public void ResetStatistics_ShouldClearAllCounts()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        gatekeeper.ShouldTranslate(null, "First text");
        gatekeeper.ShouldTranslate("Hello", "Hello");

        // Act
        gatekeeper.ResetStatistics();
        var stats = gatekeeper.GetStatistics();

        // Assert
        Assert.Equal(0, stats.TotalDecisions);
        Assert.Equal(0, stats.AllowedCount);
        Assert.Equal(0, stats.DeniedCount);
    }

    [Fact]
    public void GetStatistics_ShouldTrackTokensSaved()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        // 同一テキストは拒否されるので、トークン節約が発生
        var text = new string('a', 100);
        gatekeeper.ShouldTranslate(text, text);

        // Act
        var stats = gatekeeper.GetStatistics();

        // Assert
        Assert.True(stats.EstimatedTokensSaved > 0);
    }

    #endregion

    #region ReportTranslationResult テスト

    [Fact]
    public void ReportTranslationResult_ShouldTrackActualTokens()
    {
        // Arrange
        var gatekeeper = new RoiGatekeeper(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _enabledSettings);

        var decision = gatekeeper.ShouldTranslate(null, "Test text");

        // Act
        gatekeeper.ReportTranslationResult(decision, wasSuccessful: true, tokensUsed: 50);
        var stats = gatekeeper.GetStatistics();

        // Assert
        Assert.Equal(50, stats.ActualTokensUsed);
    }

    #endregion
}
