using System.Threading;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Events.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Events.Processors;

/// <summary>
/// 診断イベントを処理するプロセッサー
/// PipelineDiagnosticEventを受信して診断収集サービスに転送
/// </summary>
public sealed class DiagnosticEventProcessor : IEventProcessor<PipelineDiagnosticEvent>
{
    private readonly IDiagnosticCollectionService _diagnosticCollectionService;
    private readonly ILogger<DiagnosticEventProcessor> _logger;

    public DiagnosticEventProcessor(
        IDiagnosticCollectionService diagnosticCollectionService,
        ILogger<DiagnosticEventProcessor> logger)
    {
        _diagnosticCollectionService = diagnosticCollectionService;
        _logger = logger;
    }

    public int Priority => 100;
    public bool SynchronousExecution => false;

    public async Task HandleAsync(PipelineDiagnosticEvent eventData, CancellationToken cancellationToken = default)
    {
        try
        {
            Console.WriteLine($"🩺 [DIAGNOSTIC_PROCESSOR] イベント受信: {eventData.Stage} - {eventData.Severity}");

            // 診断収集サービスが動作中の場合のみ処理
            if (_diagnosticCollectionService.IsCollecting)
            {
                Console.WriteLine($"🩺 [DIAGNOSTIC_PROCESSOR] 診断収集サービスが動作中 - 処理開始");
                await _diagnosticCollectionService.CollectDiagnosticAsync(eventData, cancellationToken)
                    .ConfigureAwait(false);

                Console.WriteLine($"🩺 [DIAGNOSTIC_PROCESSOR] 診断イベント収集完了: {eventData.Stage}");

                // 詳細ログ（開発時のみ）
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("診断イベント収集: {Stage} - {IsSuccess} ({ProcessingTimeMs}ms)",
                        eventData.Stage, eventData.IsSuccess, eventData.ProcessingTimeMs);
                }
            }
            else
            {
                Console.WriteLine($"🩺 [DIAGNOSTIC_PROCESSOR] 診断収集サービスが未開始 - スキップ");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🩺 [ERROR] 診断イベント処理エラー: {ex.Message}");
            _logger.LogError(ex, "診断イベント処理エラー: {Stage}", eventData.Stage);
        }
    }
}
