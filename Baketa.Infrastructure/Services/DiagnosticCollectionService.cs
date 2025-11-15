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

        Console.WriteLine("🩺 [DIAGNOSTIC_COLLECTION] 診断データ収集開始 - IsCollecting=true");
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

    public async Task CollectDiagnosticAsync(PipelineDiagnosticEvent diagnosticEvent, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"🩺 [DIAGNOSTIC_COLLECTION] CollectDiagnosticAsync呼び出し - Stage: {diagnosticEvent.Stage}, IsCollecting: {_isCollecting}, Disposed: {_disposed}");

        if (!_isCollecting || _disposed)
        {
            Console.WriteLine($"🩺 [DIAGNOSTIC_COLLECTION] 収集スキップ - IsCollecting: {_isCollecting}, Disposed: {_disposed}");
            return;
        }

        // 🔧 CRITICAL FIX: イベントを即座に蓄積（バックグラウンド処理ではなく同期処理）
        Console.WriteLine($"🩺 [DIAGNOSTIC_COLLECTION] 即座にイベント蓄積開始 - Stage: {diagnosticEvent.Stage}");

        // 即座に蓄積処理を実行
        await ProcessDiagnosticEventAsync(diagnosticEvent, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"🩺 [DIAGNOSTIC_COLLECTION] 即座にイベント蓄積完了 - Stage: {diagnosticEvent.Stage}");
    }

    public async Task<string> GenerateReportAsync(string reportType = "diagnostic", CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"🩺 [DIAGNOSTIC_COLLECTION] GenerateReportAsync開始 - reportType: {reportType}, IsCollecting: {_isCollecting}");
        Console.WriteLine($"🩺 [DIAGNOSTIC_COLLECTION] ExtractAllEvents呼び出し前 - キューサイズ: {_diagnosticEvents.Count}");

        var events = ExtractAllEvents();

        Console.WriteLine($"🩺 [DIAGNOSTIC_COLLECTION] ExtractAllEvents完了 - 取得イベント数: {events.Count}");

        if (!events.Any())
        {
            Console.WriteLine("🩺 [DIAGNOSTIC_COLLECTION] 警告: 生成する診断イベントがありません");
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
            Console.WriteLine($"🩺 [DIAGNOSTIC_COLLECTION] ProcessDiagnosticEventAsync開始 - Stage: {diagnosticEvent.Stage}, 現在キューサイズ: {_diagnosticEvents.Count}");

            _diagnosticEvents.Enqueue(diagnosticEvent);

            Console.WriteLine($"🩺 [DIAGNOSTIC_COLLECTION] イベント追加完了 - 新キューサイズ: {_diagnosticEvents.Count}");

            // メモリ制限チェック - バックグラウンドでフラッシュ実行
            if (_diagnosticEvents.Count > MaxEventsInMemory)
            {
                Console.WriteLine($"🩺 [DIAGNOSTIC_COLLECTION] メモリ制限到達 - バックグラウンドフラッシュ実行: {_diagnosticEvents.Count} > {MaxEventsInMemory}");

                // フラッシュ処理はバックグラウンドで実行（パフォーマンスを保持）
                _backgroundQueue.QueueBackgroundWorkItem(async token =>
                {
                    await FlushEventsAsync(token).ConfigureAwait(false);
                });
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
            Console.WriteLine($"❌ [DIAGNOSTIC_COLLECTION] ProcessDiagnosticEventAsyncエラー: {ex.Message}");
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
