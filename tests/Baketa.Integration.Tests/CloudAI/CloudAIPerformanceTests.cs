using System.Diagnostics;
using Baketa.Core.Abstractions.License;
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
/// Cloud AI翻訳機能のパフォーマンステスト
/// Issue #78 Phase 6.4: パフォーマンス検証
/// </summary>
public class CloudAIPerformanceTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    #region フォールバックオーケストレーターパフォーマンス

    /// <summary>
    /// Primary成功時のレイテンシが許容範囲内であることを確認
    /// 基準: < 100ms（モック使用時）
    /// </summary>
    [Fact]
    public async Task FallbackOrchestrator_PrimarySuccess_LatencyUnder100ms()
    {
        // Arrange
        var services = CreateTestServiceCollection();
        var mockPrimaryTranslator = CreateMockCloudTranslator("primary", success: true, latencyMs: 50);
        var mockEngineStatusManager = new Mock<IEngineStatusManager>();

        mockEngineStatusManager.Setup(x => x.IsEngineAvailable("primary")).Returns(true);
        mockEngineStatusManager.Setup(x => x.IsEngineAvailable("secondary")).Returns(true);
        mockEngineStatusManager.Setup(x => x.GetStatus("primary"))
            .Returns(EngineStatus.CreateAvailable("primary"));
        mockEngineStatusManager.Setup(x => x.GetStatus("secondary"))
            .Returns(EngineStatus.CreateAvailable("secondary"));

        services.AddSingleton(mockEngineStatusManager.Object);
        services.AddKeyedSingleton<ICloudImageTranslator>("primary", mockPrimaryTranslator.Object);
        services.AddSingleton<ILogger<FallbackOrchestrator>>(
            new Mock<ILogger<FallbackOrchestrator>>().Object);
        services.AddSingleton<IFallbackOrchestrator, FallbackOrchestrator>();

        using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<IFallbackOrchestrator>();
        var request = CreateTestRequest();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await orchestrator.TranslateWithFallbackAsync(request);
        stopwatch.Stop();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(stopwatch.ElapsedMilliseconds < 100,
            $"レイテンシが許容範囲を超過: {stopwatch.ElapsedMilliseconds}ms (基準: < 100ms)");

        _output.WriteLine($"✅ Primary成功レイテンシ: {stopwatch.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// フォールバック時のレイテンシが許容範囲内であることを確認
    /// 基準: < 200ms（モック使用時、Primary失敗 + Secondary成功）
    /// </summary>
    [Fact]
    public async Task FallbackOrchestrator_Fallback_LatencyUnder200ms()
    {
        // Arrange
        var services = CreateTestServiceCollection();
        var mockPrimaryTranslator = CreateMockCloudTranslator("primary", success: false, latencyMs: 30);
        var mockSecondaryTranslator = CreateMockCloudTranslator("secondary", success: true, latencyMs: 50);
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
        services.AddSingleton<ILogger<FallbackOrchestrator>>(
            new Mock<ILogger<FallbackOrchestrator>>().Object);
        services.AddSingleton<IFallbackOrchestrator, FallbackOrchestrator>();

        using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<IFallbackOrchestrator>();
        var request = CreateTestRequest();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await orchestrator.TranslateWithFallbackAsync(request);
        stopwatch.Stop();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(FallbackLevel.Secondary, result.UsedEngine);
        Assert.True(stopwatch.ElapsedMilliseconds < 200,
            $"フォールバックレイテンシが許容範囲を超過: {stopwatch.ElapsedMilliseconds}ms (基準: < 200ms)");

        _output.WriteLine($"✅ フォールバックレイテンシ: {stopwatch.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// 連続翻訳リクエストのスループットを確認
    /// 基準: 10リクエスト/秒以上（モック使用時）
    /// </summary>
    [Fact]
    public async Task FallbackOrchestrator_Throughput_AtLeast10RequestsPerSecond()
    {
        // Arrange
        var services = CreateTestServiceCollection();
        var mockPrimaryTranslator = CreateMockCloudTranslator("primary", success: true, latencyMs: 10);
        var mockEngineStatusManager = new Mock<IEngineStatusManager>();

        mockEngineStatusManager.Setup(x => x.IsEngineAvailable("primary")).Returns(true);
        mockEngineStatusManager.Setup(x => x.GetStatus("primary"))
            .Returns(EngineStatus.CreateAvailable("primary"));
        mockEngineStatusManager.Setup(x => x.GetStatus("secondary"))
            .Returns(EngineStatus.CreateAvailable("secondary"));

        services.AddSingleton(mockEngineStatusManager.Object);
        services.AddKeyedSingleton<ICloudImageTranslator>("primary", mockPrimaryTranslator.Object);
        services.AddSingleton<ILogger<FallbackOrchestrator>>(
            new Mock<ILogger<FallbackOrchestrator>>().Object);
        services.AddSingleton<IFallbackOrchestrator, FallbackOrchestrator>();

        using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<IFallbackOrchestrator>();

        const int requestCount = 20;
        var successCount = 0;

        // Act
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < requestCount; i++)
        {
            var request = CreateTestRequest();
            var result = await orchestrator.TranslateWithFallbackAsync(request);
            if (result.IsSuccess) successCount++;
        }
        stopwatch.Stop();

        // Calculate throughput
        var throughput = requestCount / (stopwatch.ElapsedMilliseconds / 1000.0);

        // Assert
        Assert.Equal(requestCount, successCount);
        Assert.True(throughput >= 10,
            $"スループットが基準を下回りました: {throughput:F2} req/s (基準: >= 10 req/s)");

        _output.WriteLine($"✅ スループット: {throughput:F2} requests/second ({requestCount}リクエスト/{stopwatch.ElapsedMilliseconds}ms)");
    }

    /// <summary>
    /// 並列翻訳リクエストの耐性を確認
    /// 基準: 10並列リクエストが全て成功すること
    /// </summary>
    [Fact]
    public async Task FallbackOrchestrator_ParallelRequests_AllSucceed()
    {
        // Arrange
        var services = CreateTestServiceCollection();
        var mockPrimaryTranslator = CreateMockCloudTranslator("primary", success: true, latencyMs: 20);
        var mockEngineStatusManager = new Mock<IEngineStatusManager>();

        mockEngineStatusManager.Setup(x => x.IsEngineAvailable("primary")).Returns(true);
        mockEngineStatusManager.Setup(x => x.GetStatus("primary"))
            .Returns(EngineStatus.CreateAvailable("primary"));
        mockEngineStatusManager.Setup(x => x.GetStatus("secondary"))
            .Returns(EngineStatus.CreateAvailable("secondary"));

        services.AddSingleton(mockEngineStatusManager.Object);
        services.AddKeyedSingleton<ICloudImageTranslator>("primary", mockPrimaryTranslator.Object);
        services.AddSingleton<ILogger<FallbackOrchestrator>>(
            new Mock<ILogger<FallbackOrchestrator>>().Object);
        services.AddSingleton<IFallbackOrchestrator, FallbackOrchestrator>();

        using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<IFallbackOrchestrator>();

        const int parallelCount = 10;

        // Act - 並列リクエストを同時に発行
        var stopwatch = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, parallelCount)
            .Select(_ => orchestrator.TranslateWithFallbackAsync(CreateTestRequest()))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var successCount = results.Count(r => r.IsSuccess);
        Assert.Equal(parallelCount, successCount);

        // 並列実行により、逐次実行より高速であることを確認（20ms * 10 = 200ms より短い）
        Assert.True(stopwatch.ElapsedMilliseconds < 150,
            $"並列処理時間が期待より長い: {stopwatch.ElapsedMilliseconds}ms (期待: < 150ms)");

        _output.WriteLine($"✅ 並列リクエスト: {parallelCount}件全て成功 ({stopwatch.ElapsedMilliseconds}ms)");
    }

    #endregion

    #region トークン消費トラッカーパフォーマンス

    /// <summary>
    /// トークン記録のレイテンシが許容範囲内であることを確認
    /// 基準: < 10ms
    /// </summary>
    [Fact]
    public async Task TokenTracker_RecordUsage_LatencyUnder10ms()
    {
        // Arrange
        var mockRepository = new Mock<ITokenUsageRepository>();
        var mockAccessController = new Mock<IEngineAccessController>();
        var mockLogger = new Mock<ILogger<TokenConsumptionTracker>>();

        mockRepository
            .Setup(x => x.SaveRecordAsync(It.IsAny<TokenUsageRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tracker = new TokenConsumptionTracker(
            mockAccessController.Object,
            mockRepository.Object,
            mockLogger.Object);

        // Act
        var stopwatch = Stopwatch.StartNew();
        await tracker.RecordUsageAsync(1500, "primary", TokenUsageType.Input);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 10,
            $"トークン記録レイテンシが許容範囲を超過: {stopwatch.ElapsedMilliseconds}ms (基準: < 10ms)");

        _output.WriteLine($"✅ トークン記録レイテンシ: {stopwatch.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// アラートレベルチェックのレイテンシが許容範囲内であることを確認
    /// 基準: < 20ms
    /// </summary>
    [Fact]
    public async Task TokenTracker_CheckAlertLevel_LatencyUnder20ms()
    {
        // Arrange
        var mockRepository = new Mock<ITokenUsageRepository>();
        var mockAccessController = new Mock<IEngineAccessController>();
        var mockLogger = new Mock<ILogger<TokenConsumptionTracker>>();

        mockRepository
            .Setup(x => x.GetMonthlySummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonthlyUsageSummary
            {
                TotalTokens = 2_000_000,
                InputTokens = 1_200_000,
                OutputTokens = 800_000,
                LastUpdated = DateTime.UtcNow,
                ByProvider = new Dictionary<string, long>()
            });

        mockAccessController
            .Setup(x => x.GetCurrentPlanAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Core.License.Models.PlanType.Pro);

        mockAccessController
            .Setup(x => x.GetMonthlyTokenLimit(Core.License.Models.PlanType.Pro))
            .Returns(4_000_000);

        var tracker = new TokenConsumptionTracker(
            mockAccessController.Object,
            mockRepository.Object,
            mockLogger.Object);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var alertLevel = await tracker.CheckAlertLevelAsync();
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.ElapsedMilliseconds < 20,
            $"アラートレベルチェックレイテンシが許容範囲を超過: {stopwatch.ElapsedMilliseconds}ms (基準: < 20ms)");

        _output.WriteLine($"✅ アラートレベルチェックレイテンシ: {stopwatch.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// 大量トークン記録のスループットを確認
    /// 基準: 100記録/秒以上
    /// </summary>
    [Fact]
    public async Task TokenTracker_BulkRecording_AtLeast100RecordsPerSecond()
    {
        // Arrange
        var mockRepository = new Mock<ITokenUsageRepository>();
        var mockAccessController = new Mock<IEngineAccessController>();
        var mockLogger = new Mock<ILogger<TokenConsumptionTracker>>();

        mockRepository
            .Setup(x => x.SaveRecordAsync(It.IsAny<TokenUsageRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tracker = new TokenConsumptionTracker(
            mockAccessController.Object,
            mockRepository.Object,
            mockLogger.Object);

        const int recordCount = 100;

        // Act
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < recordCount; i++)
        {
            await tracker.RecordUsageAsync(100 + i, "primary", TokenUsageType.Input);
        }
        stopwatch.Stop();

        // Calculate throughput
        var throughput = recordCount / (stopwatch.ElapsedMilliseconds / 1000.0);

        // Assert
        Assert.True(throughput >= 100,
            $"トークン記録スループットが基準を下回りました: {throughput:F2} rec/s (基準: >= 100 rec/s)");

        _output.WriteLine($"✅ トークン記録スループット: {throughput:F2} records/second ({recordCount}記録/{stopwatch.ElapsedMilliseconds}ms)");
    }

    #endregion

    #region 画像トークン推定パフォーマンス

    /// <summary>
    /// 画像トークン推定のレイテンシが許容範囲内であることを確認
    /// 基準: < 1ms
    /// </summary>
    [Fact]
    public void TokenTracker_EstimateImageTokens_LatencyUnder1ms()
    {
        // Arrange
        var mockRepository = new Mock<ITokenUsageRepository>();
        var mockAccessController = new Mock<IEngineAccessController>();
        var mockLogger = new Mock<ILogger<TokenConsumptionTracker>>();

        var tracker = new TokenConsumptionTracker(
            mockAccessController.Object,
            mockRepository.Object,
            mockLogger.Object);

        // Act
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            _ = tracker.EstimateImageTokens(1920, 1080, "primary");
        }
        stopwatch.Stop();

        var avgLatency = stopwatch.ElapsedMilliseconds / 1000.0;

        // Assert
        Assert.True(avgLatency < 1,
            $"画像トークン推定平均レイテンシが許容範囲を超過: {avgLatency:F3}ms (基準: < 1ms)");

        _output.WriteLine($"✅ 画像トークン推定平均レイテンシ: {avgLatency:F3}ms (1000回実行)");
    }

    /// <summary>
    /// 様々な画像サイズでのトークン推定精度を確認
    /// </summary>
    [Theory]
    [InlineData(1920, 1080, "primary", 2765)]   // FHD
    [InlineData(3840, 2160, "primary", 11059)]  // 4K
    [InlineData(1280, 720, "primary", 1229)]    // HD
    [InlineData(640, 480, "primary", 410)]      // VGA
    public void TokenTracker_EstimateImageTokens_AccuracyWithin10Percent(
        int width, int height, string provider, int expectedApprox)
    {
        // Arrange
        var mockRepository = new Mock<ITokenUsageRepository>();
        var mockAccessController = new Mock<IEngineAccessController>();
        var mockLogger = new Mock<ILogger<TokenConsumptionTracker>>();

        var tracker = new TokenConsumptionTracker(
            mockAccessController.Object,
            mockRepository.Object,
            mockLogger.Object);

        // Act
        var tokens = tracker.EstimateImageTokens(width, height, provider);

        // Assert
        var minExpected = expectedApprox * 0.9;
        var maxExpected = expectedApprox * 1.1;
        Assert.InRange(tokens, minExpected, maxExpected);

        _output.WriteLine($"✅ {width}x{height} ({provider}): {tokens} tokens (期待値: {expectedApprox}±10%)");
    }

    #endregion

    #region ヘルパーメソッド

    private static ServiceCollection CreateTestServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        return services;
    }

    private static Mock<ICloudImageTranslator> CreateMockCloudTranslator(
        string providerId, bool success, int latencyMs = 0)
    {
        var mock = new Mock<ICloudImageTranslator>();
        mock.Setup(x => x.ProviderId).Returns(providerId);
        mock.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        if (success)
        {
            mock.Setup(x => x.TranslateImageAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()))
                .Returns(async (ImageTranslationRequest req, CancellationToken _) =>
                {
                    if (latencyMs > 0) await Task.Delay(latencyMs);
                    return ImageTranslationResponse.Success(
                        requestId: req.RequestId,
                        detectedText: "Hello",
                        translatedText: "こんにちは",
                        providerId: providerId,
                        tokenUsage: new TokenUsageDetail { InputTokens = 100, OutputTokens = 50, ImageTokens = 200 },
                        processingTime: TimeSpan.FromMilliseconds(latencyMs),
                        detectedLanguage: "en");
                });
        }
        else
        {
            mock.Setup(x => x.TranslateImageAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()))
                .Returns(async (ImageTranslationRequest req, CancellationToken _) =>
                {
                    if (latencyMs > 0) await Task.Delay(latencyMs);
                    return ImageTranslationResponse.Failure(
                        requestId: req.RequestId,
                        error: new TranslationErrorDetail
                        {
                            Code = TranslationErrorDetail.Codes.ApiError,
                            Message = "API error",
                            IsRetryable = true
                        },
                        processingTime: TimeSpan.FromMilliseconds(latencyMs));
                });
        }

        return mock;
    }

    private static ImageTranslationRequest CreateTestRequest()
    {
        return ImageTranslationRequest.FromBytes(
            imageData: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            targetLanguage: "ja",
            sessionToken: $"test-session-{Guid.NewGuid():N}",
            width: 1920,
            height: 1080,
            mimeType: "image/png");
    }

    #endregion
}
