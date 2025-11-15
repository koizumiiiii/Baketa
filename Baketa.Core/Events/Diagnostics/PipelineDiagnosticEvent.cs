using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events.Diagnostics;

/// <summary>
/// パイプライン工程の診断情報を収集するイベント
/// α版テスト効率化のための統一診断データ構造
/// </summary>
public sealed class PipelineDiagnosticEvent : IEvent
{
    /// <summary>イベントID</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>イベント発生時刻</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>イベント名</summary>
    public string Name => $"PipelineDiagnostic_{Stage}";

    /// <summary>イベントカテゴリ</summary>
    public string Category => "Diagnostics";

    /// <summary>パイプライン工程名 ("ScreenCapture", "OCR", "Translation", "Overlay")</summary>
    public required string Stage { get; init; }

    /// <summary>処理成功/失敗</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>処理時間（ミリ秒）</summary>
    public required long ProcessingTimeMs { get; init; }

    /// <summary>エラーメッセージ（失敗時）</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>診断メッセージ（成功・失敗時共通）</summary>
    public string? Message { get; init; }

    /// <summary>詳細メトリクス（品質スコア、リソース使用量等）</summary>
    public Dictionary<string, object> Metrics { get; init; } = [];

    /// <summary>診断の重要度</summary>
    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Information;

    /// <summary>関連するセッションID（一連の処理を紐付け）</summary>
    public string? SessionId { get; init; }
}
