using Baketa.Core.Events.Diagnostics;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// 診断レポート生成サービスの抽象化
/// </summary>
public interface IDiagnosticReportGenerator
{
    /// <summary>
    /// 診断イベントリストからJSONレポートを生成
    /// </summary>
    Task<string> GenerateReportAsync(
        IEnumerable<PipelineDiagnosticEvent> events, 
        string reportType,
        string? userComment = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// システム情報を含む包括的なレポートを生成
    /// </summary>
    Task<string> GenerateComprehensiveReportAsync(
        IEnumerable<PipelineDiagnosticEvent> events,
        string reportType,
        Dictionary<string, object>? systemInfo = null,
        string? userComment = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 診断レポートのデータ構造
/// </summary>
public sealed class DiagnosticReport
{
    /// <summary>レポートID</summary>
    public required string ReportId { get; init; }
    
    /// <summary>生成時刻</summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>Baketaバージョン</summary>
    public required string BaketaVersion { get; init; }
    
    /// <summary>システム情報</summary>
    public Dictionary<string, object> SystemInfo { get; init; } = [];
    
    /// <summary>診断イベントリスト</summary>
    public List<PipelineDiagnosticEvent> PipelineEvents { get; init; } = [];
    
    /// <summary>ユーザーコメント</summary>
    public string? UserComment { get; init; }
    
    /// <summary>レポート種別</summary>
    public string ReportType { get; init; } = "diagnostic";
    
    /// <summary>ユーザー確認済みフラグ</summary>
    public bool IsReviewed { get; init; }
}