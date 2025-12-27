using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Events;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Integration.Tests.CloudAI;

/// <summary>
/// Cloud AI翻訳機能の統合テスト
/// Issue #78 Phase 6.2: エンドツーエンドフロー検証
/// </summary>
public class CloudAITranslationIntegrationTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    // テスト用定数
    private const long ProPlanMonthlyLimit = 4_000_000;

    #region フォールバック機能テスト

    /// <summary>
    /// Primary成功時、Secondaryにフォールバックしないことを確認
    /// </summary>
    [Fact]
    public async Task FallbackOrchestrator_PrimarySuccess_DoesNotFallback()
    {
        // Arrange
        var services = CreateTestServiceCollection();
        var mockPrimaryTranslator = CreateMockCloudTranslator("primary", success: true);
        var mockSecondaryTranslator = CreateMockCloudTranslator("secondary", success: true);
        var mockEngineStatusManager = new Mock<IEngineStatusManager>();

        mockEngineStatusManager.Setup(x => x.IsEngineAvailable("primary")).Returns(true);
        mockEngineStatusManager.Setup(x => x.IsEngineAvailable("secondary")).Returns(true);
        mockEngineStatusManager.Setup(x => x.GetStatus("primary"))
            .Returns(EngineStatus.CreateAvailable("primary"));
        mockEngineStatusManager.Setup(x => x.GetStatus("secondary"))
            .Returns(EngineStatus.CreateAvailable("secondary"));

        services.AddSingleton(mockEngineStatusManager.Object);
        services.AddKeyedSingleton<ICloudImageTranslator>("primary", mockPrimaryTranslator.Object);
        services.AddKeyedSingleton<ICloudImageTranslator>("secondary", mockSecondaryTranslator.Object);
        services.AddSingleton<ILogger<Baketa.Infrastructure.Translation.Services.FallbackOrchestrator>>(
            new Mock<ILogger<Baketa.Infrastructure.Translation.Services.FallbackOrchestrator>>().Object);
        services.AddSingleton<IFallbackOrchestrator, Baketa.Infrastructure.Translation.Services.FallbackOrchestrator>();

        using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<IFallbackOrchestrator>();
        var request = CreateTestRequest();

        // Act
        var result = await orchestrator.TranslateWithFallbackAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(FallbackLevel.Primary, result.UsedEngine);
        Assert.Single(result.Attempts);

        // Secondary翻訳が呼ばれていないことを確認
        mockSecondaryTranslator.Verify(
            x => x.TranslateImageAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _output.WriteLine("✅ Primary成功時、Secondaryにフォールバックしないことを確認");
    }

    /// <summary>
    /// Primary失敗時、Secondaryにフォールバックすることを確認
    /// </summary>
    [Fact]
    public async Task FallbackOrchestrator_PrimaryFails_FallbacksToSecondary()
    {
        // Arrange
        var services = CreateTestServiceCollection();
        var mockPrimaryTranslator = CreateMockCloudTranslator("primary", success: false);
        var mockSecondaryTranslator = CreateMockCloudTranslator("secondary", success: true);
        var mockEngineStatusManager = new Mock<IEngineStatusManager>();

        mockEngineStatusManager.Setup(x => x.IsEngineAvailable("primary")).Returns(true);
        mockEngineStatusManager.Setup(x => x.IsEngineAvailable("secondary")).Returns(true);
        mockEngineStatusManager.Setup(x => x.GetStatus("primary"))
            .Returns(EngineStatus.CreateAvailable("primary"));
        mockEngineStatusManager.Setup(x => x.GetStatus("secondary"))
            .Returns(EngineStatus.CreateAvailable("secondary"));

        services.AddSingleton(mockEngineStatusManager.Object);
        services.AddKeyedSingleton<ICloudImageTranslator>("primary", mockPrimaryTranslator.Object);
        services.AddKeyedSingleton<ICloudImageTranslator>("secondary", mockSecondaryTranslator.Object);
        services.AddSingleton<ILogger<Baketa.Infrastructure.Translation.Services.FallbackOrchestrator>>(
            new Mock<ILogger<Baketa.Infrastructure.Translation.Services.FallbackOrchestrator>>().Object);
        services.AddSingleton<IFallbackOrchestrator, Baketa.Infrastructure.Translation.Services.FallbackOrchestrator>();

        using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<IFallbackOrchestrator>();
        var request = CreateTestRequest();

        // Act
        var result = await orchestrator.TranslateWithFallbackAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(FallbackLevel.Secondary, result.UsedEngine);
        Assert.Equal(2, result.Attempts.Count);

        _output.WriteLine("✅ Primary失敗時、Secondaryにフォールバックすることを確認");
    }

    /// <summary>
    /// Primaryが利用不可能な場合、直接Secondaryを使用することを確認
    /// </summary>
    [Fact]
    public async Task FallbackOrchestrator_PrimaryUnavailable_SkipsToSecondary()
    {
        // Arrange
        var services = CreateTestServiceCollection();
        var mockSecondaryTranslator = CreateMockCloudTranslator("secondary", success: true);
        var mockEngineStatusManager = new Mock<IEngineStatusManager>();

        mockEngineStatusManager.Setup(x => x.IsEngineAvailable("primary")).Returns(false);
        mockEngineStatusManager.Setup(x => x.IsEngineAvailable("secondary")).Returns(true);
        mockEngineStatusManager.Setup(x => x.GetNextRetryTime("primary"))
            .Returns(DateTime.UtcNow.AddMinutes(5));
        mockEngineStatusManager.Setup(x => x.GetStatus("primary"))
            .Returns(EngineStatus.CreateUnavailable("primary", DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5)));
        mockEngineStatusManager.Setup(x => x.GetStatus("secondary"))
            .Returns(EngineStatus.CreateAvailable("secondary"));

        services.AddSingleton(mockEngineStatusManager.Object);
        services.AddKeyedSingleton<ICloudImageTranslator>("secondary", mockSecondaryTranslator.Object);
        services.AddSingleton<ILogger<Baketa.Infrastructure.Translation.Services.FallbackOrchestrator>>(
            new Mock<ILogger<Baketa.Infrastructure.Translation.Services.FallbackOrchestrator>>().Object);
        services.AddSingleton<IFallbackOrchestrator, Baketa.Infrastructure.Translation.Services.FallbackOrchestrator>();

        using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<IFallbackOrchestrator>();
        var request = CreateTestRequest();

        // Act
        var result = await orchestrator.TranslateWithFallbackAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(FallbackLevel.Secondary, result.UsedEngine);
        Assert.Single(result.Attempts);

        _output.WriteLine("✅ Primary利用不可能時、直接Secondaryを使用することを確認");
    }

    #endregion

    #region トークン消費トラッキングテスト

    /// <summary>
    /// 翻訳成功時にトークン使用量が記録されることを確認
    /// </summary>
    [Fact]
    public async Task TokenTracking_TranslationSuccess_RecordsUsage()
    {
        // Arrange
        var mockRepository = new Mock<ITokenUsageRepository>();
        var mockAccessController = new Mock<IEngineAccessController>();
        var mockLogger = new Mock<ILogger<Baketa.Infrastructure.Translation.Services.TokenConsumptionTracker>>();

        TokenUsageRecord? capturedRecord = null;
        mockRepository
            .Setup(x => x.SaveRecordAsync(It.IsAny<TokenUsageRecord>(), It.IsAny<CancellationToken>()))
            .Callback<TokenUsageRecord, CancellationToken>((r, _) => capturedRecord = r)
            .Returns(Task.CompletedTask);

        var tracker = new Baketa.Infrastructure.Translation.Services.TokenConsumptionTracker(
            mockAccessController.Object,
            mockRepository.Object,
            mockLogger.Object);

        // Act
        await tracker.RecordUsageAsync(1500, "primary", TokenUsageType.Input);

        // Assert
        Assert.NotNull(capturedRecord);
        Assert.Equal(1500, capturedRecord.TokensUsed);
        Assert.Equal("primary", capturedRecord.ProviderId);
        Assert.Equal("Input", capturedRecord.UsageType);

        _output.WriteLine("✅ 翻訳成功時にトークン使用量が記録されることを確認");
    }

    /// <summary>
    /// 複数種類のトークン（Input, Output, Image）が個別に記録されることを確認
    /// </summary>
    [Fact]
    public async Task TokenTracking_MultipleTokenTypes_RecordedSeparately()
    {
        // Arrange
        var mockRepository = new Mock<ITokenUsageRepository>();
        var mockAccessController = new Mock<IEngineAccessController>();
        var mockLogger = new Mock<ILogger<Baketa.Infrastructure.Translation.Services.TokenConsumptionTracker>>();

        var capturedRecords = new List<TokenUsageRecord>();
        mockRepository
            .Setup(x => x.SaveRecordAsync(It.IsAny<TokenUsageRecord>(), It.IsAny<CancellationToken>()))
            .Callback<TokenUsageRecord, CancellationToken>((r, _) => capturedRecords.Add(r))
            .Returns(Task.CompletedTask);

        var tracker = new Baketa.Infrastructure.Translation.Services.TokenConsumptionTracker(
            mockAccessController.Object,
            mockRepository.Object,
            mockLogger.Object);

        // Act
        await tracker.RecordUsageAsync(1000, "primary", TokenUsageType.Input);
        await tracker.RecordUsageAsync(500, "primary", TokenUsageType.Output);
        await tracker.RecordUsageAsync(3500, "primary", TokenUsageType.Total);

        // Assert
        Assert.Equal(3, capturedRecords.Count);
        Assert.Contains(capturedRecords, r => r.UsageType == "Input" && r.TokensUsed == 1000);
        Assert.Contains(capturedRecords, r => r.UsageType == "Output" && r.TokensUsed == 500);
        Assert.Contains(capturedRecords, r => r.UsageType == "Total" && r.TokensUsed == 3500);

        _output.WriteLine("✅ 複数種類のトークンが個別に記録されることを確認");
    }

    #endregion

    #region 使用量警告テスト

    /// <summary>
    /// 80%到達時にWarningレベルが発生することを確認
    /// </summary>
    [Fact]
    public async Task UsageAlert_At80Percent_ReturnsWarningLevel()
    {
        // Arrange
        var mockRepository = new Mock<ITokenUsageRepository>();
        var mockAccessController = new Mock<IEngineAccessController>();
        var mockLogger = new Mock<ILogger<Baketa.Infrastructure.Translation.Services.TokenConsumptionTracker>>();

        // 80%使用量 = 4,000,000 * 0.80 = 3,200,000
        mockRepository
            .Setup(x => x.GetMonthlySummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonthlyUsageSummary
            {
                TotalTokens = 3_200_000,
                InputTokens = 2_000_000,
                OutputTokens = 1_200_000,
                LastUpdated = DateTime.UtcNow,
                ByProvider = new Dictionary<string, long>()
            });

        mockAccessController
            .Setup(x => x.GetCurrentPlanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Core.License.Models.PlanType.Pro);

        mockAccessController
            .Setup(x => x.GetMonthlyTokenLimit(Core.License.Models.PlanType.Pro))
            .Returns(ProPlanMonthlyLimit);

        var tracker = new Baketa.Infrastructure.Translation.Services.TokenConsumptionTracker(
            mockAccessController.Object,
            mockRepository.Object,
            mockLogger.Object);

        // Act
        var alertLevel = await tracker.CheckAlertLevelAsync();

        // Assert
        Assert.Equal(UsageAlertLevel.Warning80, alertLevel);

        _output.WriteLine("✅ 80%到達時にWarning80レベルが発生することを確認");
    }

    /// <summary>
    /// 90%到達時にWarning90レベルが発生することを確認
    /// </summary>
    [Fact]
    public async Task UsageAlert_At90Percent_ReturnsWarning90Level()
    {
        // Arrange
        var mockRepository = new Mock<ITokenUsageRepository>();
        var mockAccessController = new Mock<IEngineAccessController>();
        var mockLogger = new Mock<ILogger<Baketa.Infrastructure.Translation.Services.TokenConsumptionTracker>>();

        // 90%使用量 = 4,000,000 * 0.90 = 3,600,000
        mockRepository
            .Setup(x => x.GetMonthlySummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonthlyUsageSummary
            {
                TotalTokens = 3_600_000,
                InputTokens = 2_200_000,
                OutputTokens = 1_400_000,
                LastUpdated = DateTime.UtcNow,
                ByProvider = new Dictionary<string, long>()
            });

        mockAccessController
            .Setup(x => x.GetCurrentPlanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Core.License.Models.PlanType.Pro);

        mockAccessController
            .Setup(x => x.GetMonthlyTokenLimit(Core.License.Models.PlanType.Pro))
            .Returns(ProPlanMonthlyLimit);

        var tracker = new Baketa.Infrastructure.Translation.Services.TokenConsumptionTracker(
            mockAccessController.Object,
            mockRepository.Object,
            mockLogger.Object);

        // Act
        var alertLevel = await tracker.CheckAlertLevelAsync();

        // Assert
        Assert.Equal(UsageAlertLevel.Warning90, alertLevel);

        _output.WriteLine("✅ 90%到達時にWarning90レベルが発生することを確認");
    }

    /// <summary>
    /// 100%到達時にExceededレベルが発生することを確認
    /// </summary>
    [Fact]
    public async Task UsageAlert_At100Percent_ReturnsExceededLevel()
    {
        // Arrange
        var mockRepository = new Mock<ITokenUsageRepository>();
        var mockAccessController = new Mock<IEngineAccessController>();
        var mockLogger = new Mock<ILogger<Baketa.Infrastructure.Translation.Services.TokenConsumptionTracker>>();

        // 100%超過 = 4,000,000 * 1.05 = 4,200,000
        mockRepository
            .Setup(x => x.GetMonthlySummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonthlyUsageSummary
            {
                TotalTokens = 4_200_000,
                InputTokens = 2_500_000,
                OutputTokens = 1_700_000,
                LastUpdated = DateTime.UtcNow,
                ByProvider = new Dictionary<string, long>()
            });

        mockAccessController
            .Setup(x => x.GetCurrentPlanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Core.License.Models.PlanType.Pro);

        mockAccessController
            .Setup(x => x.GetMonthlyTokenLimit(Core.License.Models.PlanType.Pro))
            .Returns(ProPlanMonthlyLimit);

        var tracker = new Baketa.Infrastructure.Translation.Services.TokenConsumptionTracker(
            mockAccessController.Object,
            mockRepository.Object,
            mockLogger.Object);

        // Act
        var alertLevel = await tracker.CheckAlertLevelAsync();

        // Assert
        Assert.Equal(UsageAlertLevel.Exceeded, alertLevel);

        _output.WriteLine("✅ 100%到達時にExceededレベルが発生することを確認");
    }

    #endregion

    #region ヘルパーメソッド

    private static ServiceCollection CreateTestServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        return services;
    }

    private static Mock<ICloudImageTranslator> CreateMockCloudTranslator(string providerId, bool success)
    {
        var mock = new Mock<ICloudImageTranslator>();
        mock.Setup(x => x.ProviderId).Returns(providerId);
        mock.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        if (success)
        {
            mock.Setup(x => x.TranslateImageAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ImageTranslationRequest req, CancellationToken _) =>
                    ImageTranslationResponse.Success(
                        requestId: req.RequestId,
                        detectedText: "Hello",
                        translatedText: "こんにちは",
                        providerId: providerId,
                        tokenUsage: new TokenUsageDetail { InputTokens = 100, OutputTokens = 50, ImageTokens = 200 },
                        processingTime: TimeSpan.FromMilliseconds(500),
                        detectedLanguage: "en"));
        }
        else
        {
            mock.Setup(x => x.TranslateImageAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ImageTranslationRequest req, CancellationToken _) =>
                    ImageTranslationResponse.Failure(
                        requestId: req.RequestId,
                        error: new TranslationErrorDetail
                        {
                            Code = TranslationErrorDetail.Codes.ApiError,
                            Message = "API error",
                            IsRetryable = true
                        },
                        processingTime: TimeSpan.FromMilliseconds(100)));
        }

        return mock;
    }

    private static ImageTranslationRequest CreateTestRequest()
    {
        return ImageTranslationRequest.FromBytes(
            imageData: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            targetLanguage: "ja",
            sessionToken: "test-session-token",
            width: 1920,
            height: 1080,
            mimeType: "image/png");
    }

    #endregion
}
