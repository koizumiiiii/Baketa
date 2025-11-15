using System.Reactive;

namespace Baketa.Application.Services.Diagnostics;

/// <summary>
/// 診断レポート生成統一サービス
/// MainOverlayViewModelで重複していた診断レポート生成ロジックを統一化
/// </summary>
public interface IDiagnosticReportService
{
    /// <summary>
    /// 診断レポートを生成します
    /// </summary>
    /// <param name="trigger">レポート生成のトリガー</param>
    /// <param name="context">追加のコンテキスト情報</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>生成されたレポートファイルのパス（失敗時はnull）</returns>
    Task<string?> GenerateReportAsync(string trigger, string? context = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// システムヘルス状態を取得します
    /// </summary>
    /// <returns>システムヘルス情報</returns>
    Task<SystemHealthStatus> GetSystemHealthAsync();

    /// <summary>
    /// パフォーマンスメトリクスのストリーム
    /// </summary>
    IObservable<PerformanceMetrics> MetricsStream { get; }

    /// <summary>
    /// レポート生成状態の変更通知
    /// </summary>
    IObservable<DiagnosticReportGenerated> ReportGenerated { get; }
}

/// <summary>
/// システムヘルス状態
/// </summary>
public sealed record SystemHealthStatus(
    bool IsHealthy,
    TimeSpan Uptime,
    double CpuUsage,
    long MemoryUsageBytes,
    string[] ActiveServices,
    string[] Warnings,
    string[] Errors
);

/// <summary>
/// パフォーマンスメトリクス
/// </summary>
public sealed record PerformanceMetrics(
    DateTime Timestamp,
    double CpuUsage,
    long MemoryUsage,
    int ActiveTranslations,
    TimeSpan AverageResponseTime
);

/// <summary>
/// 診断レポート生成イベント
/// </summary>
public sealed record DiagnosticReportGenerated(
    string ReportPath,
    string Trigger,
    DateTime GeneratedAt,
    bool IsSuccess,
    string? ErrorMessage = null
);
