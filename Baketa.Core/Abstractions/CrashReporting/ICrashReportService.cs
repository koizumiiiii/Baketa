namespace Baketa.Core.Abstractions.CrashReporting;

/// <summary>
/// [Issue #252] クラッシュレポートサービスのインターフェース
/// アプリケーションクラッシュ時のレポート生成・管理を担当
/// </summary>
public interface ICrashReportService
{
    /// <summary>
    /// クラッシュレポートを生成
    /// </summary>
    /// <param name="exception">発生した例外</param>
    /// <param name="source">クラッシュ発生元（AppDomain, TaskScheduler, RxApp等）</param>
    /// <param name="additionalContext">追加のコンテキスト情報</param>
    /// <returns>生成されたクラッシュレポート</returns>
    Task<CrashReport> GenerateCrashReportAsync(
        Exception exception,
        string source,
        Dictionary<string, object?>? additionalContext = null);

    /// <summary>
    /// クラッシュレポートをファイルに保存
    /// </summary>
    /// <param name="report">保存するレポート</param>
    /// <returns>保存されたファイルパス</returns>
    Task<string> SaveCrashReportAsync(CrashReport report);

    /// <summary>
    /// .crash_pendingフラグファイルを作成
    /// 次回起動時にクラッシュを検出するため
    /// </summary>
    Task CreateCrashPendingFlagAsync();

    /// <summary>
    /// .crash_pendingフラグファイルが存在するかチェック
    /// </summary>
    bool HasPendingCrashReport();

    /// <summary>
    /// .crash_pendingフラグファイルを削除
    /// </summary>
    Task ClearCrashPendingFlagAsync();

    /// <summary>
    /// 未送信のクラッシュレポートを取得
    /// </summary>
    /// <returns>未送信レポートのリスト</returns>
    Task<IReadOnlyList<CrashReportSummary>> GetPendingCrashReportsAsync();

    /// <summary>
    /// クラッシュレポートを読み込み
    /// </summary>
    /// <param name="reportId">レポートID</param>
    /// <returns>クラッシュレポート（見つからない場合はnull）</returns>
    Task<CrashReport?> LoadCrashReportAsync(string reportId);

    /// <summary>
    /// クラッシュレポートを送信済みとしてマーク
    /// </summary>
    /// <param name="reportId">レポートID</param>
    Task MarkReportAsSentAsync(string reportId);

    /// <summary>
    /// クラッシュレポートをサーバーに送信
    /// </summary>
    /// <param name="report">送信するレポート</param>
    /// <param name="includeSystemInfo">システム情報を含めるか</param>
    /// <param name="includeLogs">ログを含めるか</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>送信成功時はtrue</returns>
    Task<bool> SendCrashReportAsync(
        CrashReport report,
        bool includeSystemInfo,
        bool includeLogs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// クラッシュレポートを削除
    /// </summary>
    /// <param name="reportId">レポートID</param>
    Task DeleteCrashReportAsync(string reportId);

    /// <summary>
    /// レポートディレクトリのパスを取得
    /// </summary>
    string ReportsDirectory { get; }
}

/// <summary>
/// クラッシュレポートのサマリー情報
/// </summary>
public sealed class CrashReportSummary
{
    /// <summary>レポートID</summary>
    public required string ReportId { get; init; }

    /// <summary>ファイルパス</summary>
    public required string FilePath { get; init; }

    /// <summary>クラッシュ発生日時</summary>
    public required DateTime CrashedAt { get; init; }

    /// <summary>例外タイプ</summary>
    public required string ExceptionType { get; init; }

    /// <summary>例外メッセージ</summary>
    public required string ExceptionMessage { get; init; }

    /// <summary>クラッシュ発生元</summary>
    public required string Source { get; init; }

    /// <summary>送信済みかどうか</summary>
    public bool IsSent { get; init; }
}

/// <summary>
/// クラッシュレポートのデータ構造
/// </summary>
public sealed class CrashReport
{
    /// <summary>レポートID（一意識別子）</summary>
    public required string ReportId { get; init; }

    /// <summary>クラッシュ発生日時（UTC）</summary>
    public DateTime CrashedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Baketaバージョン</summary>
    public required string BaketaVersion { get; init; }

    /// <summary>クラッシュ発生元</summary>
    public required string Source { get; init; }

    /// <summary>例外情報</summary>
    public required ExceptionInfo Exception { get; init; }

    /// <summary>システム情報</summary>
    public required SystemInfoSnapshot SystemInfo { get; init; }

    /// <summary>リングバッファログ（最新N行）</summary>
    public List<string> RecentLogs { get; init; } = [];

    /// <summary>追加コンテキスト</summary>
    public Dictionary<string, object?> AdditionalContext { get; init; } = [];

    /// <summary>送信済みかどうか</summary>
    public bool IsSent { get; set; }

    /// <summary>送信日時（UTC）</summary>
    public DateTime? SentAt { get; set; }
}

/// <summary>
/// 例外情報
/// </summary>
public sealed class ExceptionInfo
{
    /// <summary>例外タイプ名</summary>
    public required string Type { get; init; }

    /// <summary>例外メッセージ</summary>
    public required string Message { get; init; }

    /// <summary>スタックトレース</summary>
    public string? StackTrace { get; init; }

    /// <summary>内部例外</summary>
    public ExceptionInfo? InnerException { get; init; }

    /// <summary>例外ソース</summary>
    public string? Source { get; init; }

    /// <summary>HResult</summary>
    public int HResult { get; init; }

    /// <summary>追加データ</summary>
    public Dictionary<string, string> Data { get; init; } = [];
}

/// <summary>
/// システム情報スナップショット
/// </summary>
public sealed class SystemInfoSnapshot
{
    /// <summary>OS情報</summary>
    public required string OsVersion { get; init; }

    /// <summary>OSプラットフォーム</summary>
    public required string OsPlatform { get; init; }

    /// <summary>マシン名（サニタイズ済み）</summary>
    public required string MachineNameHash { get; init; }

    /// <summary>プロセッサ数</summary>
    public int ProcessorCount { get; init; }

    /// <summary>64ビットプロセスかどうか</summary>
    public bool Is64BitProcess { get; init; }

    /// <summary>使用メモリ（バイト）</summary>
    public long WorkingSetBytes { get; init; }

    /// <summary>.NETバージョン</summary>
    public required string ClrVersion { get; init; }

    /// <summary>起動からの経過時間</summary>
    public TimeSpan Uptime { get; init; }

    /// <summary>カルチャ</summary>
    public required string Culture { get; init; }

    /// <summary>タイムゾーン</summary>
    public required string TimeZone { get; init; }
}
