using Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Translation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation;

/// <summary>
/// FallbackOrchestratorの単体テスト
/// </summary>
public class FallbackOrchestratorTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IKeyedServiceProvider> _mockKeyedServiceProvider;
    private readonly Mock<IEngineStatusManager> _mockEngineStatusManager;
    private readonly Mock<ILogger<FallbackOrchestrator>> _mockLogger;
    private readonly FallbackOrchestrator _orchestrator;

    private const string PrimaryKey = "primary";
    private const string SecondaryKey = "secondary";

    public FallbackOrchestratorTests()
    {
        _mockKeyedServiceProvider = new Mock<IKeyedServiceProvider>();
        _mockServiceProvider = _mockKeyedServiceProvider.As<IServiceProvider>();
        _mockEngineStatusManager = new Mock<IEngineStatusManager>();
        _mockLogger = new Mock<ILogger<FallbackOrchestrator>>();

        // IKeyedServiceProviderを返すように設定
        _mockServiceProvider
            .Setup(x => x.GetService(typeof(IKeyedServiceProvider)))
            .Returns(_mockKeyedServiceProvider.Object);

        _orchestrator = new FallbackOrchestrator(
            _mockServiceProvider.Object,
            _mockEngineStatusManager.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_ThrowsOnNullServiceProvider()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FallbackOrchestrator(
            null!,
            _mockEngineStatusManager.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_ThrowsOnNullEngineStatusManager()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FallbackOrchestrator(
            _mockServiceProvider.Object,
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_ThrowsOnNullLogger()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FallbackOrchestrator(
            _mockServiceProvider.Object,
            _mockEngineStatusManager.Object,
            null!));
    }

    #endregion

    #region TranslateWithFallbackAsync Tests

    [Fact]
    public async Task TranslateWithFallbackAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _orchestrator.TranslateWithFallbackAsync(null!));
    }

    [Fact]
    public async Task TranslateWithFallbackAsync_PrimarySuccess_ReturnsPrimaryResult()
    {
        // Arrange
        var request = CreateTestRequest();
        var mockTranslator = CreateMockTranslator("primary", true, request.RequestId);

        SetupEngineAvailable(PrimaryKey, true);
        SetupKeyedService(PrimaryKey, mockTranslator.Object);

        // Act
        var result = await _orchestrator.TranslateWithFallbackAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(FallbackLevel.Primary, result.UsedEngine);
        Assert.Single(result.Attempts);
        Assert.True(result.Attempts[0].Success);
    }

    [Fact]
    public async Task TranslateWithFallbackAsync_PrimaryFails_FallbacksToSecondary()
    {
        // Arrange
        var request = CreateTestRequest();
        var mockPrimaryTranslator = CreateMockTranslator("primary", false, request.RequestId);
        var mockSecondaryTranslator = CreateMockTranslator("secondary", true, request.RequestId);

        SetupEngineAvailable(PrimaryKey, true);
        SetupEngineAvailable(SecondaryKey, true);
        SetupKeyedService(PrimaryKey, mockPrimaryTranslator.Object);
        SetupKeyedService(SecondaryKey, mockSecondaryTranslator.Object);

        // Act
        var result = await _orchestrator.TranslateWithFallbackAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(FallbackLevel.Secondary, result.UsedEngine);
        Assert.Equal(2, result.Attempts.Count);
        Assert.False(result.Attempts[0].Success);
        Assert.True(result.Attempts[1].Success);
    }

    [Fact]
    public async Task TranslateWithFallbackAsync_PrimaryUnavailable_SkipsToPrimary()
    {
        // Arrange
        var request = CreateTestRequest();
        var mockSecondaryTranslator = CreateMockTranslator("secondary", true, request.RequestId);

        SetupEngineAvailable(PrimaryKey, false);
        SetupEngineAvailable(SecondaryKey, true);
        SetupKeyedService(SecondaryKey, mockSecondaryTranslator.Object);

        _mockEngineStatusManager
            .Setup(x => x.GetNextRetryTime(PrimaryKey))
            .Returns(DateTime.UtcNow.AddMinutes(5));

        // Act
        var result = await _orchestrator.TranslateWithFallbackAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(FallbackLevel.Secondary, result.UsedEngine);
        Assert.Single(result.Attempts);
    }

    [Fact]
    public async Task TranslateWithFallbackAsync_BothFail_ReturnsFailure()
    {
        // Arrange
        var request = CreateTestRequest();
        var mockPrimaryTranslator = CreateMockTranslator("primary", false, request.RequestId);
        var mockSecondaryTranslator = CreateMockTranslator("secondary", false, request.RequestId);

        SetupEngineAvailable(PrimaryKey, true);
        SetupEngineAvailable(SecondaryKey, true);
        SetupKeyedService(PrimaryKey, mockPrimaryTranslator.Object);
        SetupKeyedService(SecondaryKey, mockSecondaryTranslator.Object);

        // Act
        var result = await _orchestrator.TranslateWithFallbackAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(3, result.Attempts.Count); // Primary, Secondary, Local
        Assert.All(result.Attempts, a => Assert.False(a.Success));
    }

    [Fact]
    public async Task TranslateWithFallbackAsync_BothUnavailable_ReturnsFailure()
    {
        // Arrange
        var request = CreateTestRequest();

        SetupEngineAvailable(PrimaryKey, false);
        SetupEngineAvailable(SecondaryKey, false);

        _mockEngineStatusManager
            .Setup(x => x.GetNextRetryTime(It.IsAny<string>()))
            .Returns(DateTime.UtcNow.AddMinutes(5));

        // Act
        var result = await _orchestrator.TranslateWithFallbackAsync(request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Single(result.Attempts); // Only local attempt recorded
    }

    [Fact]
    public async Task TranslateWithFallbackAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var request = CreateTestRequest();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var mockTranslator = new Mock<ICloudImageTranslator>();
        mockTranslator
            .Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        SetupEngineAvailable(PrimaryKey, true);
        SetupKeyedService(PrimaryKey, mockTranslator.Object);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _orchestrator.TranslateWithFallbackAsync(request, cts.Token));
    }

    [Fact]
    public async Task TranslateWithFallbackAsync_TranslatorNotFound_ContinuesToNext()
    {
        // Arrange
        var request = CreateTestRequest();
        var mockSecondaryTranslator = CreateMockTranslator("secondary", true, request.RequestId);

        SetupEngineAvailable(PrimaryKey, true);
        SetupEngineAvailable(SecondaryKey, true);
        SetupKeyedService(PrimaryKey, null); // Primary not found
        SetupKeyedService(SecondaryKey, mockSecondaryTranslator.Object);

        // Act
        var result = await _orchestrator.TranslateWithFallbackAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(FallbackLevel.Secondary, result.UsedEngine);
        Assert.Equal(2, result.Attempts.Count);
        Assert.Equal(TranslationErrorDetail.Codes.InternalError, result.Attempts[0].ErrorCode);
        Assert.True(result.Attempts[1].Success);
    }

    [Fact]
    public async Task TranslateWithFallbackAsync_TranslatorThrowsException_MarksUnavailableAndContinues()
    {
        // Arrange
        var request = CreateTestRequest();

        var mockPrimaryTranslator = new Mock<ICloudImageTranslator>();
        mockPrimaryTranslator.Setup(x => x.ProviderId).Returns("primary");
        mockPrimaryTranslator
            .Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        var mockSecondaryTranslator = CreateMockTranslator("secondary", true, request.RequestId);

        SetupEngineAvailable(PrimaryKey, true);
        SetupEngineAvailable(SecondaryKey, true);
        SetupKeyedService(PrimaryKey, mockPrimaryTranslator.Object);
        SetupKeyedService(SecondaryKey, mockSecondaryTranslator.Object);

        // Act
        var result = await _orchestrator.TranslateWithFallbackAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(FallbackLevel.Secondary, result.UsedEngine);

        // Verify engine was marked unavailable
        _mockEngineStatusManager.Verify(
            x => x.MarkEngineUnavailable(PrimaryKey, It.IsAny<TimeSpan>(), It.IsAny<string>()),
            Times.Once);
    }

    #endregion

    #region GetCurrentStatus Tests

    [Fact]
    public void GetCurrentStatus_ReturnsCorrectStatus()
    {
        // Arrange
        _mockEngineStatusManager
            .Setup(x => x.GetStatus(PrimaryKey))
            .Returns(EngineStatus.CreateAvailable(PrimaryKey));

        _mockEngineStatusManager
            .Setup(x => x.GetStatus(SecondaryKey))
            .Returns(EngineStatus.CreateUnavailable(SecondaryKey, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5)));

        // Act
        var status = _orchestrator.GetCurrentStatus();

        // Assert
        Assert.True(status.PrimaryAvailable);
        Assert.False(status.SecondaryAvailable);
        Assert.True(status.LocalAvailable); // Always true
        Assert.Null(status.PrimaryNextRetry);
        Assert.NotNull(status.SecondaryNextRetry);
    }

    [Fact]
    public void GetCurrentStatus_BothAvailable_ReturnsAllTrue()
    {
        // Arrange
        _mockEngineStatusManager
            .Setup(x => x.GetStatus(PrimaryKey))
            .Returns(EngineStatus.CreateAvailable(PrimaryKey));

        _mockEngineStatusManager
            .Setup(x => x.GetStatus(SecondaryKey))
            .Returns(EngineStatus.CreateAvailable(SecondaryKey));

        // Act
        var status = _orchestrator.GetCurrentStatus();

        // Assert
        Assert.True(status.PrimaryAvailable);
        Assert.True(status.SecondaryAvailable);
        Assert.True(status.LocalAvailable);
    }

    [Fact]
    public void GetCurrentStatus_BothUnavailable_ReturnsOnlyLocalTrue()
    {
        // Arrange
        var nextRetry = DateTime.UtcNow.AddMinutes(5);
        _mockEngineStatusManager
            .Setup(x => x.GetStatus(PrimaryKey))
            .Returns(EngineStatus.CreateUnavailable(PrimaryKey, DateTime.UtcNow, nextRetry));

        _mockEngineStatusManager
            .Setup(x => x.GetStatus(SecondaryKey))
            .Returns(EngineStatus.CreateUnavailable(SecondaryKey, DateTime.UtcNow, nextRetry));

        // Act
        var status = _orchestrator.GetCurrentStatus();

        // Assert
        Assert.False(status.PrimaryAvailable);
        Assert.False(status.SecondaryAvailable);
        Assert.True(status.LocalAvailable); // Always true
        Assert.Equal(nextRetry, status.PrimaryNextRetry);
        Assert.Equal(nextRetry, status.SecondaryNextRetry);
    }

    #endregion

    #region Helper Methods

    private static ImageTranslationRequest CreateTestRequest()
    {
        return ImageTranslationRequest.FromBytes(
            imageData: new byte[] { 1, 2, 3, 4 },
            targetLanguage: "ja",
            sessionToken: "test-session-token",
            width: 1920,
            height: 1080,
            mimeType: "image/png");
    }

    private Mock<ICloudImageTranslator> CreateMockTranslator(string providerId, bool succeeds, string requestId)
    {
        var mock = new Mock<ICloudImageTranslator>();
        mock.Setup(x => x.ProviderId).Returns(providerId);
        mock.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        if (succeeds)
        {
            var response = ImageTranslationResponse.Success(
                requestId: requestId,
                detectedText: "Hello",
                translatedText: "こんにちは",
                providerId: providerId,
                tokenUsage: new TokenUsageDetail { InputTokens = 100, OutputTokens = 50, ImageTokens = 200 },
                processingTime: TimeSpan.FromMilliseconds(500),
                detectedLanguage: "en");

            mock.Setup(x => x.TranslateImageAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);
        }
        else
        {
            var response = ImageTranslationResponse.Failure(
                requestId: requestId,
                error: new TranslationErrorDetail
                {
                    Code = TranslationErrorDetail.Codes.ApiError,
                    Message = "Translation failed",
                    IsRetryable = true
                },
                processingTime: TimeSpan.FromMilliseconds(100));

            mock.Setup(x => x.TranslateImageAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);
        }

        return mock;
    }

    private void SetupEngineAvailable(string engineKey, bool available)
    {
        _mockEngineStatusManager
            .Setup(x => x.IsEngineAvailable(engineKey))
            .Returns(available);
    }

    private void SetupKeyedService(string key, ICloudImageTranslator? translator)
    {
        // 既に初期化済みの_mockKeyedServiceProviderにセットアップを追加
        _mockKeyedServiceProvider
            .Setup(x => x.GetKeyedService(typeof(ICloudImageTranslator), key))
            .Returns(translator);
    }

    #endregion
}
