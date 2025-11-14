using System;
using Baketa.Core.Abstractions.Services;

namespace Baketa.Infrastructure.Imaging.ChangeDetection;

/// <summary>
/// 画像変化検知のメトリクス収集サービス
/// </summary>
public class ImageChangeMetricsService : IImageChangeMetricsService
{
    private int _ocrSkippedCount;
    private int _ocrExecutedCount;
    private float _totalSkippedChangePercentage;
    private float _totalExecutedChangePercentage;
    private TimeSpan _totalSkippedTime;
    private TimeSpan _totalExecutedTime;
    private readonly object _lock = new();

    public void RecordOcrSkipped(float changePercentage, TimeSpan processingTime)
    {
        lock (_lock)
        {
            _ocrSkippedCount++;
            _totalSkippedChangePercentage += changePercentage;
            _totalSkippedTime = _totalSkippedTime.Add(processingTime);
        }
    }

    public void RecordOcrExecuted(float changePercentage, TimeSpan processingTime)
    {
        lock (_lock)
        {
            _ocrExecutedCount++;
            _totalExecutedChangePercentage += changePercentage;
            _totalExecutedTime = _totalExecutedTime.Add(processingTime);
        }
    }

    public ImageChangeMetrics GetMetrics()
    {
        lock (_lock)
        {
            var totalTime = _totalSkippedTime.Add(_totalExecutedTime);
            var totalCount = _ocrSkippedCount + _ocrExecutedCount;

            return new ImageChangeMetrics
            {
                TotalSkippedCount = _ocrSkippedCount,
                TotalExecutedCount = _ocrExecutedCount,
                AverageSkippedChangePercentage = _ocrSkippedCount > 0 ? _totalSkippedChangePercentage / _ocrSkippedCount : 0f,
                AverageExecutedChangePercentage = _ocrExecutedCount > 0 ? _totalExecutedChangePercentage / _ocrExecutedCount : 0f,
                AverageProcessingTime = totalCount > 0 ? TimeSpan.FromTicks(totalTime.Ticks / totalCount) : TimeSpan.Zero,
                CollectionStartTime = DateTime.Now.AddTicks(-totalTime.Ticks), // 概算
                LastUpdated = DateTime.Now
            };
        }
    }

    public void ResetMetrics()
    {
        lock (_lock)
        {
            _ocrSkippedCount = 0;
            _ocrExecutedCount = 0;
            _totalSkippedChangePercentage = 0f;
            _totalExecutedChangePercentage = 0f;
            _totalSkippedTime = TimeSpan.Zero;
            _totalExecutedTime = TimeSpan.Zero;
        }
    }
}
