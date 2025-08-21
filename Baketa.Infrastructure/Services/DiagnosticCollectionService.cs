using System.Collections.Concurrent;
using System.IO;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Events.Diagnostics;
using Baketa.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// 診断データ収集サービスの実装
/// パフォーマンス影響を最小化した非同期診断データ収集
/// </summary>
public sealed class DiagnosticCollectionService : IDiagnosticCollectionService, IDisposable
{
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly IDiagnosticReportGenerator _reportGenerator;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<DiagnosticCollectionService> _logger;
    
    private readonly ConcurrentQueue<PipelineDiagnosticEvent> _diagnosticEvents = new();
    private readonly System.Threading.Timer _flushTimer;
    private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
    
    private volatile bool _isCollecting;
    private volatile bool _disposed;
    
    private const int MaxEventsInMemory = 1000;
    private const int FlushIntervalMs = 30000; // 30秒
    
    public DiagnosticCollectionService(
        IBackgroundTaskQueue backgroundQueue,
        IDiagnosticReportGenerator reportGenerator,
        IEventAggregator eventAggregator,
        ILogger<DiagnosticCollectionService> logger)
    {
        _backgroundQueue = backgroundQueue;
        _reportGenerator = reportGenerator;
        _eventAggregator = eventAggregator;
        _logger = logger;
        
        // 定期フラッシュタイマー設定
        _flushTimer = new System.Threading.Timer(FlushToFile, null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsCollecting => _isCollecting;

    public Task StartCollectionAsync(CancellationToken cancellationToken = default)
    {
        _isCollecting = true;
        _flushTimer.Change(FlushIntervalMs, FlushIntervalMs);
        
        _logger.LogInformation("診断データ収集開始");
        
        return Task.CompletedTask;
    }

    public async Task StopCollectionAsync(CancellationToken cancellationToken = default)
    {
        _isCollecting = false;
        _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
        
        // 残りのデータをフラッシュ
        await FlushEventsAsync(cancellationToken).ConfigureAwait(false);
        
        _logger.LogInformation("診断データ収集停止");
    }

    public Task CollectDiagnosticAsync(PipelineDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken = default)
    {
        if (!_isCollecting || _disposed)
            return Task.CompletedTask;

        // メイン処理をブロックしないようバックグラウンドで処理
        _backgroundQueue.QueueBackgroundWorkItem(async token =>
        {
            await ProcessDiagnosticEventAsync(diagnosticEvent, token).ConfigureAwait(false);
        });

        return Task.CompletedTask;
    }

    public async Task<string> GenerateReportAsync(string reportType = "diagnostic", CancellationToken cancellationToken = default)
    {
        var events = ExtractAllEvents();
        
        if (!events.Any())
        {
            _logger.LogWarning("生成する診断イベントがありません");
            return string.Empty;
        }

        var reportFilePath = await _reportGenerator.GenerateComprehensiveReportAsync(
            events, reportType, GetSystemInfo(), cancellationToken: cancellationToken).ConfigureAwait(false);

        // レポート生成イベントを発行
        await _eventAggregator.PublishAsync(new DiagnosticReportGeneratedEvent
        {
            ReportId = Path.GetFileNameWithoutExtension(reportFilePath),
            FilePath = reportFilePath,
            DiagnosticEventCount = events.Count,
            ReportType = reportType,
            FileSizeBytes = new FileInfo(reportFilePath).Length
        }).ConfigureAwait(false);

        return reportFilePath;
    }

    private async Task ProcessDiagnosticEventAsync(PipelineDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken)
    {
        try
        {
            _diagnosticEvents.Enqueue(diagnosticEvent);

            // メモリ制限チェック
            if (_diagnosticEvents.Count > MaxEventsInMemory)
            {
                await FlushEventsAsync(cancellationToken).ConfigureAwait(false);
            }

            // 重要度が高い場合は即座にコンソール出力（ファイル出力は診断レポートで一元化）
            if (diagnosticEvent.Severity >= DiagnosticSeverity.Error)
            {
                Console.WriteLine($"🚨 [DIAGNOSTIC] [{diagnosticEvent.Severity}] {diagnosticEvent.Stage}: {diagnosticEvent.ErrorMessage}");
                _logger.LogError("診断イベント: [{Severity}] {Stage}: {ErrorMessage}", 
                    diagnosticEvent.Severity, diagnosticEvent.Stage, diagnosticEvent.ErrorMessage);
            }
            else if (diagnosticEvent.Severity >= DiagnosticSeverity.Warning)
            {
                Console.WriteLine($"⚠️ [DIAGNOSTIC] [{diagnosticEvent.Severity}] {diagnosticEvent.Stage}: {diagnosticEvent.ErrorMessage}");
                _logger.LogWarning("診断イベント: [{Severity}] {Stage}: {ErrorMessage}", 
                    diagnosticEvent.Severity, diagnosticEvent.Stage, diagnosticEvent.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "診断イベント処理エラー");
        }
    }

    private async void FlushToFile(object? state)
    {
        if (!_isCollecting || _disposed)
            return;

        try
        {
            await FlushEventsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "定期フラッシュエラー");
        }
    }

    private async Task FlushEventsAsync(CancellationToken cancellationToken)
    {
        if (!await _flushSemaphore.WaitAsync(1000, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            var events = ExtractAllEvents();
            if (!events.Any())
                return;

            var reportPath = await _reportGenerator.GenerateReportAsync(
                events, "flush", cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("診断データフラッシュ完了: {EventCount}イベント, {FilePath}", 
                events.Count, reportPath);
        }
        finally
        {
            _flushSemaphore.Release();
        }
    }

    private List<PipelineDiagnosticEvent> ExtractAllEvents()
    {
        var events = new List<PipelineDiagnosticEvent>();
        
        while (_diagnosticEvents.TryDequeue(out var evt))
        {
            events.Add(evt);
        }
        
        return events;
    }

    private static Dictionary<string, object> GetSystemInfo()
    {
        return new Dictionary<string, object>
        {
            ["MachineName"] = Environment.MachineName,
            ["OSVersion"] = Environment.OSVersion.ToString(),
            ["ProcessorCount"] = Environment.ProcessorCount,
            ["WorkingSet"] = Environment.WorkingSet,
            ["CLRVersion"] = Environment.Version.ToString(),
            ["Timestamp"] = DateTime.UtcNow
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _flushTimer?.Dispose();
        _flushSemaphore?.Dispose();
    }
}