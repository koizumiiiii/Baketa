using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events.Diagnostics;

/// <summary>
/// 診断レポートが生成された際に発行されるイベント
/// </summary>
public sealed class DiagnosticReportGeneratedEvent : IEvent
{
    /// <summary>イベントID</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>イベント発生時刻</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>イベント名</summary>
    public string Name => $"DiagnosticReportGenerated_{ReportType}";

    /// <summary>イベントカテゴリ</summary>
    public string Category => "Diagnostics";

    /// <summary>生成されたレポートのID</summary>
    public required string ReportId { get; init; }

    /// <summary>レポートファイルパス</summary>
    public required string FilePath { get; init; }

    /// <summary>レポート生成時刻</summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>レポートに含まれる診断イベント数</summary>
    public int DiagnosticEventCount { get; init; }

    /// <summary>レポートの種別（crash, performance, error等）</summary>
    public string ReportType { get; init; } = "diagnostic";

    /// <summary>レポートサイズ（バイト）</summary>
    public long FileSizeBytes { get; init; }
}
