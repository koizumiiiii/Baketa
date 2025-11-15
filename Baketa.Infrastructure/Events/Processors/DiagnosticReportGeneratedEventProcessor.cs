using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Events.Processors;

/// <summary>
/// 診断レポート生成イベントを処理するプロセッサー
/// レポート生成完了時のログ出力とクリーンアップ処理
/// </summary>
public sealed class DiagnosticReportGeneratedEventProcessor : IEventProcessor<DiagnosticReportGeneratedEvent>
{
    private readonly ILogger<DiagnosticReportGeneratedEventProcessor> _logger;

    public DiagnosticReportGeneratedEventProcessor(ILogger<DiagnosticReportGeneratedEventProcessor> logger)
    {
        _logger = logger;
    }

    public int Priority => 100;
    public bool SynchronousExecution => false;

    public Task HandleAsync(DiagnosticReportGeneratedEvent eventData)
    {
        try
        {
            Console.WriteLine($"📊 [REPORT_GENERATED] 診断レポート生成完了!");
            Console.WriteLine($"📊 [REPORT_GENERATED] ファイルパス: {eventData.FilePath}");
            Console.WriteLine($"📊 [REPORT_GENERATED] レポートID: {eventData.ReportId}");
            Console.WriteLine($"📊 [REPORT_GENERATED] イベント数: {eventData.DiagnosticEventCount}");
            Console.WriteLine($"📊 [REPORT_GENERATED] ファイルサイズ: {eventData.FileSizeBytes / 1024}KB");

            _logger.LogInformation(
                "📊 診断レポート生成完了: {ReportType} - {EventCount}イベント, サイズ: {FileSizeKB}KB",
                eventData.ReportType,
                eventData.DiagnosticEventCount,
                eventData.FileSizeBytes / 1024);

            // クリティカルな診断レポートの場合は注意喚起
            if (eventData.ReportType.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
                eventData.ReportType.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"🚨 [CRITICAL] 重要な診断レポート: {eventData.FilePath}");
                _logger.LogWarning(
                    "🚨 重要な診断レポートが生成されました: {FilePath}",
                    eventData.FilePath);
            }

            // 将来的な機能拡張: 自動送信、圧縮、アーカイブ等
            // ここで追加の処理を実装可能

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"📊 [ERROR] レポート生成イベント処理エラー: {ex.Message}");
            _logger.LogError(ex, "診断レポート生成イベント処理エラー");
            return Task.CompletedTask;
        }
    }
}
