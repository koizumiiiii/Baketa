using System.Drawing;
using Baketa.Core.Abstractions.OCR.Results;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.PostProcessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.OCR.PostProcessing;

public class TimedChunkAggregatorTests : IDisposable
{
    private readonly ILogger<TimedChunkAggregator> _logger;
    private readonly CoordinateBasedLineBreakProcessor _lineBreakProcessor;
    private readonly Mock<ICoordinateTransformationService> _coordinateTransformationServiceMock;
    private readonly ProximityGroupingService _proximityGroupingService;
    private readonly TimedAggregatorSettings _settings;
    private readonly TimedChunkAggregator _aggregator;

    public TimedChunkAggregatorTests()
    {
        _logger = NullLogger<TimedChunkAggregator>.Instance;
        _lineBreakProcessor = new CoordinateBasedLineBreakProcessor(
            NullLogger<CoordinateBasedLineBreakProcessor>.Instance);

        // Setup coordinate transformation service mock
        _coordinateTransformationServiceMock = new Mock<ICoordinateTransformationService>();
        // 🔥 [PHASE2.1_TEST_FIX] オプションパラメータ追加に伴うMock修正
        // 🚀 [Issue #193] alreadyScaledToOriginalSizeパラメータ追加
        _coordinateTransformationServiceMock
            .Setup(x => x.ConvertRoiToScreenCoordinates(It.IsAny<Rectangle>(), It.IsAny<IntPtr>(), It.IsAny<float>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns((Rectangle rect, IntPtr handle, float scale, bool isBorderless, bool alreadyScaled) => rect); // Return original rect for testing
        _coordinateTransformationServiceMock
            .Setup(x => x.ConvertRoiToScreenCoordinatesBatch(It.IsAny<Rectangle[]>(), It.IsAny<IntPtr>(), It.IsAny<float>(), It.IsAny<bool>()))
            .Returns((Rectangle[] rects, IntPtr handle, float scale, bool isBorderless) => rects); // Return original rects for testing

        _settings = new TimedAggregatorSettings
        {
            IsFeatureEnabled = true,
            BufferDelayMs = 1000
        };

        // Setup proximity grouping service with actual instances (sealed classes cannot be mocked)
        var proximityAnalyzerLogger = NullLogger<ChunkProximityAnalyzer>.Instance;
        var proximitySettings = ProximityGroupingSettings.Default; // Use default settings for testing
        var proximityAnalyzer = new ChunkProximityAnalyzer(proximityAnalyzerLogger, proximitySettings);
        var proximityGroupingLogger = NullLogger<ProximityGroupingService>.Instance;
        _proximityGroupingService = new ProximityGroupingService(proximityAnalyzer, proximityGroupingLogger);

        var optionsMonitorMock = new Mock<IOptionsMonitor<TimedAggregatorSettings>>();
        optionsMonitorMock.Setup(x => x.CurrentValue).Returns(_settings);

        var eventAggregatorMock = new Mock<Baketa.Core.Abstractions.Events.IEventAggregator>();

        _aggregator = new TimedChunkAggregator(
            optionsMonitorMock.Object,
            _lineBreakProcessor,
            _coordinateTransformationServiceMock.Object,
            _proximityGroupingService,
            eventAggregatorMock.Object,
            _logger);
    }

    [Fact]
    public void Constructor_ValidParameters_CreatesInstance()
    {
        // Act & Assert
        Assert.NotNull(_aggregator);
        var stats = _aggregator.GetStatistics();
        Assert.Equal(0, stats.TotalChunksProcessed);
        Assert.Equal(0, stats.TotalAggregationEvents);
    }

    [Fact]
    public async Task TryAddChunkAsync_SingleChunk_ReturnsTrue()
    {
        // Arrange
        var chunk = CreateTestChunk(1, new IntPtr(1001));

        // Act
        var result = await _aggregator.TryAddChunkAsync(chunk);

        // Assert
        Assert.True(result);
    }

    [Fact(Skip = "Phase 12.2: OnChunksAggregated削除により無効化 - AggregatedChunksReadyEvent検証に移行必要")]
    public async Task TryAddChunkAsync_MultipleChunks_AggregatesCorrectly()
    {
        // Arrange
        var aggregatedChunks = new List<TextChunk>();
        var aggregationEventCount = 0;
        // _aggregator.OnChunksAggregated += (chunks) => {
        //     aggregatedChunks.AddRange(chunks);
        //     aggregationEventCount++;
        //     return Task.CompletedTask;
        // };

        var chunk1 = CreateTestChunk(1, new IntPtr(1001), "Hello");
        var chunk2 = CreateTestChunk(2, new IntPtr(1001), "World");

        // Act
        var result1 = await _aggregator.TryAddChunkAsync(chunk1);
        var result2 = await _aggregator.TryAddChunkAsync(chunk2);

        // Wait for aggregation timeout
        await Task.Delay(1200);

        // Assert
        Assert.True(result1, "First chunk should be accepted");
        Assert.True(result2, "Second chunk should be accepted");

        // Verify aggregation occurred
        Assert.True(aggregationEventCount >= 1, "At least one aggregation event should have occurred");
        Assert.NotEmpty(aggregatedChunks);

        // Verify aggregated content contains both texts
        var allAggregatedText = string.Join(" ", aggregatedChunks.Select(c => c.CombinedText));
        Assert.Contains("Hello", allAggregatedText);
        Assert.Contains("World", allAggregatedText);

        // Verify statistics
        var stats = _aggregator.GetStatistics();
        Assert.True(stats.TotalChunksProcessed >= 2, $"Expected at least 2 processed chunks, got {stats.TotalChunksProcessed}");
        Assert.True(stats.TotalAggregationEvents >= 1, $"Expected at least 1 aggregation event, got {stats.TotalAggregationEvents}");
    }

    [Fact(Skip = "Phase 12.2: OnChunksAggregated削除により無効化 - AggregatedChunksReadyEvent検証に移行必要")]
    public async Task TryAddChunkAsync_DifferentWindows_ProcessesSeparately()
    {
        // Arrange
        var aggregatedChunksByEvent = new List<List<TextChunk>>();
        // _aggregator.OnChunksAggregated += (chunks) => {
        //     aggregatedChunksByEvent.Add(new List<TextChunk>(chunks));
        //     return Task.CompletedTask;
        // };

        var chunk1 = CreateTestChunk(1, new IntPtr(1001), "Window1 Text");
        var chunk2 = CreateTestChunk(2, new IntPtr(1002), "Window2 Text");

        // Act
        var result1 = await _aggregator.TryAddChunkAsync(chunk1);
        var result2 = await _aggregator.TryAddChunkAsync(chunk2);

        // Wait for aggregation
        await Task.Delay(1200);

        // Assert
        Assert.True(result1, "Chunk from window 1 should be accepted");
        Assert.True(result2, "Chunk from window 2 should be accepted");

        // Verify separate processing - should have separate aggregation events for different windows
        Assert.True(aggregatedChunksByEvent.Count >= 1, "Should have at least one aggregation event");

        // Verify that chunks from different windows are processed separately
        var allChunks = aggregatedChunksByEvent.SelectMany(list => list).ToList();
        Assert.True(allChunks.Count >= 1, "Should have aggregated chunks");

        // Verify statistics
        var stats = _aggregator.GetStatistics();
        Assert.True(stats.TotalChunksProcessed >= 2, $"Expected at least 2 processed chunks, got {stats.TotalChunksProcessed}");
    }

    private static TextChunk CreateTestChunk(int chunkId, IntPtr windowHandle, string text = "Test text")
    {
        var textResult = new PositionedTextResult
        {
            Text = text,
            BoundingBox = new Rectangle(10, 10, 100, 20),
            Confidence = 0.95f,
            ChunkId = chunkId
        };

        return new TextChunk
        {
            ChunkId = chunkId,
            TextResults = [textResult],
            CombinedBounds = new Rectangle(10, 10, 100, 20),
            CombinedText = text,
            SourceWindowHandle = windowHandle
        };
    }

    [Fact(Skip = "Phase 12.2: OnChunksAggregated削除により無効化 - AggregatedChunksReadyEvent検証に移行必要")]
    public async Task TryAddChunkAsync_MaxChunkCountReached_TriggersImmediateAggregation()
    {
        // Arrange - Create aggregator with low MaxChunkCount for testing
        var testSettings = new TimedAggregatorSettings
        {
            IsFeatureEnabled = true,
            BufferDelayMs = 5000, // Long delay to test immediate aggregation
            MaxChunkCount = 2 // Low threshold for testing
        };
        var optionsMonitorMock = new Mock<IOptionsMonitor<TimedAggregatorSettings>>();
        optionsMonitorMock.Setup(x => x.CurrentValue).Returns(testSettings);

        var eventAggregatorMock = new Mock<Baketa.Core.Abstractions.Events.IEventAggregator>();

        using var testAggregator = new TimedChunkAggregator(
            optionsMonitorMock.Object,
            _lineBreakProcessor,
            _coordinateTransformationServiceMock.Object,
            _proximityGroupingService,
            eventAggregatorMock.Object,
            _logger);

        var aggregatedChunks = new List<TextChunk>();
        var aggregationEventCount = 0;
        // testAggregator.OnChunksAggregated += (chunks) => {
        //     aggregatedChunks.AddRange(chunks);
        //     aggregationEventCount++;
        //     return Task.CompletedTask;
        // };

        var windowHandle = new IntPtr(2001);
        var chunk1 = CreateTestChunk(1, windowHandle, "First");
        var chunk2 = CreateTestChunk(2, windowHandle, "Second");

        // Act
        await testAggregator.TryAddChunkAsync(chunk1);
        await testAggregator.TryAddChunkAsync(chunk2);

        // Short wait to allow immediate aggregation processing
        await Task.Delay(100);

        // Assert
        Assert.True(aggregationEventCount >= 1, "Should trigger aggregation when MaxChunkCount is reached");
        Assert.True(aggregatedChunks.Count >= 1, "Should have aggregated chunks");

        var stats = testAggregator.GetStatistics();
        Assert.True(stats.TotalChunksProcessed >= 2, $"Expected at least 2 processed chunks, got {stats.TotalChunksProcessed}");
        Assert.True(stats.TotalAggregationEvents >= 1, $"Expected at least 1 aggregation event, got {stats.TotalAggregationEvents}");
    }

    [Fact]
    public async Task TryAddChunkAsync_FeatureDisabled_ReturnsFalse()
    {
        // Arrange - Create aggregator with feature disabled
        var disabledSettings = new TimedAggregatorSettings
        {
            IsFeatureEnabled = false,
            BufferDelayMs = 1000
        };
        var optionsMonitorMock = new Mock<IOptionsMonitor<TimedAggregatorSettings>>();
        optionsMonitorMock.Setup(x => x.CurrentValue).Returns(disabledSettings);

        var eventAggregatorMock = new Mock<Baketa.Core.Abstractions.Events.IEventAggregator>();

        using var testAggregator = new TimedChunkAggregator(
            optionsMonitorMock.Object,
            _lineBreakProcessor,
            _coordinateTransformationServiceMock.Object,
            _proximityGroupingService,
            eventAggregatorMock.Object,
            _logger);

        var aggregationEventCount = 0;
        // testAggregator.OnChunksAggregated += (chunks) => {
        //     aggregationEventCount++;
        //     return Task.CompletedTask;
        // };

        var chunk = CreateTestChunk(1, new IntPtr(3001), "Test");

        // Act
        var result = await testAggregator.TryAddChunkAsync(chunk);

        // Wait to ensure no aggregation occurs
        await Task.Delay(200);

        // Assert
        Assert.False(result, "Should return false when feature is disabled");
        Assert.Equal(0, aggregationEventCount);

        var stats = testAggregator.GetStatistics();
        Assert.Equal(0, stats.TotalChunksProcessed);
        Assert.Equal(0, stats.TotalAggregationEvents);
    }

    [Fact(Skip = "Phase 12.2: OnChunksAggregated削除により無効化 - AggregatedChunksReadyEvent検証に移行必要")]
    public async Task OnChunksAggregated_ExceptionInHandler_DoesNotCrashAggregator()
    {
        // Arrange
        var aggregationAttempts = 0;
        var successfulAggregations = 0;

        // _aggregator.OnChunksAggregated += (chunks) => {
        //     aggregationAttempts++;
        //     if (aggregationAttempts == 1)
        //     {
        //         // First call throws exception
        //         throw new InvalidOperationException("Test exception in aggregation handler");
        //     }
        //     // Second call succeeds
        //     successfulAggregations++;
        //     return Task.CompletedTask;
        // };

        var windowHandle = new IntPtr(4001);
        var chunk1 = CreateTestChunk(1, windowHandle, "First batch");
        var chunk2 = CreateTestChunk(2, windowHandle, "Second batch");

        // Act - First batch should trigger exception
        await _aggregator.TryAddChunkAsync(chunk1);
        await Task.Delay(1200); // Wait for first aggregation attempt

        // Second batch should work normally despite previous exception
        await _aggregator.TryAddChunkAsync(chunk2);
        await Task.Delay(1200); // Wait for second aggregation

        // Assert
        Assert.True(aggregationAttempts >= 1, "Should have attempted aggregation at least once");
        Assert.True(successfulAggregations >= 1, "Should have successful aggregations after exception");

        // Aggregator should still be functional
        var stats = _aggregator.GetStatistics();
        Assert.True(stats.TotalChunksProcessed >= 1, "Aggregator should continue processing after exception");
    }

    public void Dispose()
    {
        _aggregator?.Dispose();
    }
}
