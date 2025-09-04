using System.ComponentModel.DataAnnotations;
using System.IO;

namespace Baketa.Core.Settings;

/// <summary>
/// ログ出力設定
/// </summary>
/// <remarks>
/// debug_app_logs.txtハードコード問題解決のためのログ設定外部化
/// appsettings.json の Logging セクション対応
/// </remarks>
public sealed record LoggingSettings
{
    /// <summary>
    /// デバッグログファイルのパス
    /// </summary>
    [Display(Name = "デバッグログパス", Description = "デバッグ用ログファイルの出力パス")]
    [Required(ErrorMessage = "デバッグログパスは必須です")]
    public string DebugLogPath { get; init; } = "debug_app_logs.txt";

    /// <summary>
    /// デバッグファイルログの有効性
    /// </summary>
    [Display(Name = "デバッグファイルログ", Description = "デバッグ情報のファイル出力有効性")]
    public bool EnableDebugFileLogging { get; init; } = true;

    /// <summary>
    /// デバッグログファイルの最大サイズ（MB）
    /// </summary>
    [Display(Name = "最大ファイルサイズ", Description = "デバッグログファイルの最大サイズ（MB）")]
    [Range(1, 100, ErrorMessage = "最大ファイルサイズは1MBから100MBの間で設定してください")]
    public int MaxDebugLogFileSizeMB { get; init; } = 10;

    /// <summary>
    /// デバッグログファイルの保持日数
    /// </summary>
    [Display(Name = "ログ保持日数", Description = "デバッグログファイルの保持日数")]
    [Range(1, 30, ErrorMessage = "ログ保持日数は1日から30日の間で設定してください")]
    public int DebugLogRetentionDays { get; init; } = 7;

    /// <summary>
    /// 設定の妥当性を検証
    /// </summary>
    /// <returns>検証結果</returns>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(DebugLogPath) &&
               MaxDebugLogFileSizeMB > 0 &&
               DebugLogRetentionDays > 0;
    }

    /// <summary>
    /// フルパスでデバッグログパスを取得
    /// </summary>
    /// <returns>フルパス</returns>
    /// <remarks>
    /// AppContext.BaseDirectoryを使用することで、実行方法に関係なく安定したパスを提供します。
    /// Directory.GetCurrentDirectory()と異なり、アプリケーションのベースディレクトリが常に基点となります。
    /// </remarks>
    public string GetFullDebugLogPath()
    {
        return Path.IsPathRooted(DebugLogPath) 
            ? DebugLogPath 
            : Path.Combine(AppContext.BaseDirectory, DebugLogPath);
    }

    /// <summary>
    /// αテスト用設定を作成
    /// </summary>
    /// <returns>αテスト設定</returns>
    public static LoggingSettings CreateAlphaTestSettings() => new()
    {
        DebugLogPath = "alpha_debug_logs.txt",
        EnableDebugFileLogging = true,
        MaxDebugLogFileSizeMB = 20, // αテストでは大きなログサイズを許可
        DebugLogRetentionDays = 3 // 短い保持期間
    };

    /// <summary>
    /// 本番用設定を作成
    /// </summary>
    /// <returns>本番設定</returns>
    public static LoggingSettings CreateProductionSettings() => new()
    {
        DebugLogPath = "production_logs.txt",
        EnableDebugFileLogging = false, // 本番ではファイルログ無効
        MaxDebugLogFileSizeMB = 5, // 小さなサイズ
        DebugLogRetentionDays = 3 // 短い保持期間
    };

    /// <summary>
    /// 開発用設定を作成（ローカル開発・テスト用）
    /// </summary>
    /// <returns>開発設定</returns>
    public static LoggingSettings CreateDevelopmentSettings() => new()
    {
        DebugLogPath = "dev_debug_logs.txt",
        EnableDebugFileLogging = true,
        MaxDebugLogFileSizeMB = 50, // 開発時は大きなサイズを許可
        DebugLogRetentionDays = 14 // 長い保持期間
    };

    /// <summary>
    /// パフォーマンステスト用設定を作成
    /// </summary>
    /// <returns>パフォーマンステスト設定</returns>
    public static LoggingSettings CreatePerformanceTestSettings() => new()
    {
        DebugLogPath = "performance_test_logs.txt",
        EnableDebugFileLogging = true,
        MaxDebugLogFileSizeMB = 100, // パフォーマンス分析のため最大サイズ
        DebugLogRetentionDays = 1 // テスト後すぐに削除
    };
}