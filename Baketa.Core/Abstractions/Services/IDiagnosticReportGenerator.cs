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

    /// <summary>ROI画像情報リスト</summary>
    public List<RoiImageInfo> RoiImages { get; init; } = [];
}

/// <summary>
/// ROI画像情報
/// </summary>
public sealed class RoiImageInfo
{
    /// <summary>ROI画像ID</summary>
    public required string ImageId { get; init; }

    /// <summary>ROI画像ファイルパス</summary>
    public required string FilePath { get; init; }

    /// <summary>関連テキスト</summary>
    public string? DetectedText { get; init; }

    /// <summary>OCR信頼度</summary>
    public double Confidence { get; init; }

    /// <summary>画像サイズ（幅）</summary>
    public int Width { get; init; }

    /// <summary>画像サイズ（高さ）</summary>
    public int Height { get; init; }

    /// <summary>画像形式</summary>
    public string Format { get; init; } = "png";

    /// <summary>OCRタイル情報</summary>
    public string? TileId { get; init; }

    /// <summary>画像生成時刻</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>関連診断イベントID</summary>
    public string? RelatedEventId { get; init; }
}
