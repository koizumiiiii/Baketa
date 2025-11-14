using System.Diagnostics;
using Baketa.Core.Events.Implementation;
using Xunit;

namespace Baketa.Core.Tests.Events;

/// <summary>
/// イベントプロセッサメトリクスのテスト
/// </summary>
public class EventProcessorMetricsTests
{
    /// <summary>
    /// 基本的な測定テスト
    /// </summary>
    [Fact]
    public void MeasurementCycle_RecordsMetrics()
    {
        // Arrange
        var metrics = new EventProcessorMetrics();
        var processorType = typeof(TestEventProcessor);
        var eventType = typeof(TestEvent);

        // Act
        var stopwatch = metrics.StartMeasurement(processorType, eventType);
        Thread.Sleep(10); // 処理時間のシミュレーション
        metrics.EndMeasurement(stopwatch, processorType, eventType, true);

        // レポート生成
        var report = metrics.GenerateReport();

        // Assert
        Assert.NotEmpty(report);
        Assert.Contains("TestEventProcessor_TestEvent", report);
        Assert.Contains("成功率: 100.00%", report);
    }

    /// <summary>
    /// エラー発生時のメトリクス記録テスト
    /// </summary>
    [Fact]
    public void EndMeasurement_WithError_RecordsError()
    {
        // Arrange
        var metrics = new EventProcessorMetrics();
        var processorType = typeof(ErrorTestEventProcessor);
        var eventType = typeof(ErrorTestEvent);

        // Act
        // 成功ケース
        var successStopwatch = metrics.StartMeasurement(processorType, eventType);
        metrics.EndMeasurement(successStopwatch, processorType, eventType, true);

        // エラーケース
        var errorStopwatch = metrics.StartMeasurement(processorType, eventType);
        metrics.EndMeasurement(errorStopwatch, processorType, eventType, false);

        // メトリクス取得
        var metricsData = metrics.GetMetrics();

        // Assert
        Assert.NotEmpty(metricsData);
        Assert.True(metricsData.ContainsKey("ErrorTestEventProcessor_ErrorTestEvent"));

        var processorMetric = metricsData["ErrorTestEventProcessor_ErrorTestEvent"];
        Assert.Equal(2, processorMetric.InvocationCount);
        Assert.Equal(1, processorMetric.ErrorCount);
        Assert.Equal(50.0, processorMetric.SuccessRate);
    }

    /// <summary>
    /// キーの生成テスト
    /// </summary>
    [Fact]
    public void GetMetrics_ReturnsCorrectKeys()
    {
        // Arrange
        var metrics = new EventProcessorMetrics();
        var processor1Type = typeof(TestEventProcessor);
        var processor2Type = typeof(ErrorTestEventProcessor);
        var eventType = typeof(TestEvent);

        // Act
        // プロセッサ1の測定
        var sw1 = metrics.StartMeasurement(processor1Type, eventType);
        metrics.EndMeasurement(sw1, processor1Type, eventType, true);

        // プロセッサ2の測定
        var sw2 = metrics.StartMeasurement(processor2Type, eventType);
        metrics.EndMeasurement(sw2, processor2Type, eventType, true);

        // メトリクス取得
        var metricsData = metrics.GetMetrics();

        // Assert
        Assert.Equal(2, metricsData.Count);
        Assert.True(metricsData.ContainsKey("TestEventProcessor_TestEvent"));
        Assert.True(metricsData.ContainsKey("ErrorTestEventProcessor_TestEvent"));
    }

    /// <summary>
    /// 引数検証のテスト
    /// </summary>
    [Fact]
    public void StartMeasurement_WithNullArguments_ThrowsArgumentNullException()
    {
        // Arrange
        var metrics = new EventProcessorMetrics();
        var validType = typeof(TestEventProcessor);

        // Act & Assert
#pragma warning disable CS8625 // テスト目的のnullリテラル
        Assert.Throws<ArgumentNullException>(() => metrics.StartMeasurement(null, validType));
        Assert.Throws<ArgumentNullException>(() => metrics.StartMeasurement(validType, null));
#pragma warning restore CS8625
    }
}
